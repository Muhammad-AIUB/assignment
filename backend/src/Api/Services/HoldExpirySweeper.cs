using System.Data;
using System.Data.Common;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Api.Services;

/// <summary>
/// Data-cleanliness sweep: flips expired HELD holds to EXPIRED and gives their seats back to the class's
/// <c>HeldSeats</c> counter.
///
/// <b>Correctness does not depend on this.</b> A hold past its <c>ExpiresAt</c> is already EXPIRED as far as
/// the API is concerned, by computed logic that never needs a sweep to have run: <c>Hold.EffectiveStatus</c>
/// (Session 4) for what GET reports, the expired-excluding counts in GET /classes (Session 2), and the
/// per-class cleanup at the top of the seat claim in POST /classes/{id}/holds (Session 3). Stop this job
/// and every response stays correct; it only keeps the stored rows and counters tidy.
///
/// Because of that, the SQL below is deliberately conservative. It never waits on a row another request
/// holds (<c>SKIP LOCKED</c>: whoever has it is dealing with it), only one sweep runs at a time across all
/// API instances (advisory lock), and the counter is decremented by exactly the rows this statement flipped
/// (taken from <c>RETURNING</c>), so it cannot drift from the hold statuses.
/// </summary>
public class HoldExpirySweeper(AppDbContext db, TimeProvider clock) : IHoldExpirySweeper
{
    /// <summary>Holds flipped per statement, so one sweep never holds a huge number of row locks at once.</summary>
    public const int BatchSize = 500;

    // Arbitrary key naming this job in Postgres' advisory-lock space ("HOLDSWEP").
    private const long AdvisoryLockKey = 0x484F4C4453574550;

    // One statement: lock a batch of due rows, flip them, and decrement each class by the number flipped.
    // `released` is not read by the final SELECT, but a data-modifying CTE always runs to completion.
    private const string SweepSql = """
        WITH due AS (
            SELECT "Id" FROM holds
            WHERE "Status" = 'Held' AND "ExpiresAt" <= @now
            ORDER BY "ExpiresAt"
            LIMIT @batch
            FOR UPDATE SKIP LOCKED),
        expired AS (
            UPDATE holds h SET "Status" = 'Expired'
            FROM due
            WHERE h."Id" = due."Id" AND h."Status" = 'Held' AND h."ExpiresAt" <= @now
            RETURNING h."ClassId"),
        per_class AS (
            SELECT "ClassId", count(*) AS n FROM expired GROUP BY "ClassId"),
        released AS (
            UPDATE classes c SET "HeldSeats" = c."HeldSeats" - per_class.n
            FROM per_class
            WHERE c."Id" = per_class."ClassId"
            RETURNING 1)
        SELECT count(*) FROM expired
        """;

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var flipped = await SweepBatchAsync(ct);

            // null: another sweep is running, so leave it to that one. A short batch: nothing more is due.
            if (flipped is null) break;
            total += flipped.Value;
            if (flipped.Value < BatchSize) break;
        }
        return total;
    }

    private async Task<int?> SweepBatchAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var dbTx = tx.GetDbTransaction();

        // Transaction-scoped, so it is released at commit/rollback and can never be left behind by a crash.
        var gotLock = (bool)(await ExecuteScalarAsync(dbTx, "SELECT pg_try_advisory_xact_lock(@key)", ct,
            ("key", AdvisoryLockKey)))!;
        if (!gotLock) return null;

        var flipped = Convert.ToInt32(await ExecuteScalarAsync(dbTx, SweepSql, ct,
            ("now", now), ("batch", BatchSize)));

        await tx.CommitAsync(ct);
        return flipped;
    }

    private static async Task<object?> ExecuteScalarAsync(DbTransaction tx, string sql, CancellationToken ct,
        params (string Name, object Value)[] args)
    {
        await using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }
        return await cmd.ExecuteScalarAsync(ct);
    }
}
