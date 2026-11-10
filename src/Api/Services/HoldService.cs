using System.Data;
using System.Data.Common;
using Api.Dtos;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Options;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.Services;

public class HoldService(AppDbContext db, TimeProvider clock, IOptions<HoldOptions> options) : IHoldService
{
    private const string UniqueViolation = "23505";

    public async Task<CreateHoldResult> CreateAsync(string classId, string studentId, CancellationToken ct)
    {
        if (!await db.Classes.AsNoTracking().AnyAsync(c => c.Id == classId, ct))
            return new CreateHoldResult(CreateHoldOutcome.ClassNotFound);

        var now = clock.GetUtcNow().UtcDateTime;

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var dbTx = tx.GetDbTransaction();

        // 1. Release this class's expired-but-unswept holds so they neither occupy a seat nor
        //    count as the student's "active" hold. Idempotent and safe under concurrency: a
        //    row can only flip HELD -> EXPIRED once, and the counter is decremented by exactly
        //    the number of rows this statement flipped.
        await ExecuteAsync(dbTx, """
            WITH expired AS (
                UPDATE holds SET "Status" = 'Expired'
                WHERE "ClassId" = @classId AND "Status" = 'Held' AND "ExpiresAt" <= @now
                RETURNING 1)
            UPDATE classes SET "HeldSeats" = "HeldSeats" - (SELECT count(*) FROM expired)
            WHERE "Id" = @classId AND EXISTS (SELECT 1 FROM expired)
            """, ct, ("classId", classId), ("now", now));

        // 2. Atomic seat claim (rules 1). The conditional UPDATE both checks and reserves in one
        //    statement; it takes the class row lock, held until commit/rollback. Zero rows
        //    returned = no seat.
        var claimed = await ExecuteScalarAsync(dbTx, """
            UPDATE classes SET "HeldSeats" = "HeldSeats" + 1
            WHERE "Id" = @classId AND "HeldSeats" + "ConfirmedSeats" < "TotalSeats"
            RETURNING "HeldSeats"
            """, ct, ("classId", classId));

        if (claimed is null)
        {
            // Sold out. Report a duplicate if that is also true (more useful to the caller).
            var dup = await HasActiveHoldAsync(classId, studentId, ct);
            await tx.CommitAsync(ct); // keep step 1's cleanup
            return new CreateHoldResult(dup ? CreateHoldOutcome.DuplicateActiveHold : CreateHoldOutcome.NoSeatAvailable);
        }

        // 3. One active hold / admission per student per class (rule 3). Race-safe because every
        //    request for this class serialises on the class row lock taken in step 2: any competing
        //    transaction's hold is either committed (visible here) or has not started its own check.
        //    The partial unique index is the DB-level backstop.
        if (await HasActiveHoldAsync(classId, studentId, ct))
        {
            await tx.RollbackAsync(ct); // undoes the seat claim
            return new CreateHoldResult(CreateHoldOutcome.DuplicateActiveHold);
        }

        var hold = new Hold
        {
            ClassId = classId,
            StudentId = studentId,
            Status = HoldStatus.Held,
            CreatedAt = now,
            ExpiresAt = now + options.Value.Duration
        };
        db.Holds.Add(hold);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return new CreateHoldResult(CreateHoldOutcome.DuplicateActiveHold);
        }

        return new CreateHoldResult(CreateHoldOutcome.Created,
            new HoldResponse(hold.Id, hold.ClassId, hold.StudentId, "HELD", hold.ExpiresAt));
    }

    public async Task<HoldStatusResponse?> GetAsync(string holdId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await db.Holds.AsNoTracking()
            .Where(h => h.Id == holdId)
            .Select(h => new { h.Id, h.ClassId, h.Status, h.ExpiresAt })
            .FirstOrDefaultAsync(ct);

        // Read-only: expiry is computed here (rule 2), never written by a GET.
        return row is null
            ? null
            : new HoldStatusResponse(row.Id, row.ClassId,
                ToApiStatus(Hold.EffectiveStatus(row.Status, row.ExpiresAt, now)), row.ExpiresAt);
    }

    public async Task<HoldActionOutcome> ConfirmAsync(string holdId, string studentId, CancellationToken ct)
    {
        var outcome = await TransitionAsync(holdId, studentId, HoldTransition.Confirm, ct);
        // Rule 5: re-confirming your own CONFIRMED hold is a success. Nothing was counted a second time:
        // the guarded UPDATE matched no row, so the counters were never touched.
        return outcome == HoldActionOutcome.AlreadyConfirmed ? HoldActionOutcome.Succeeded : outcome;
    }

    public Task<HoldActionOutcome> CancelAsync(string holdId, string studentId, CancellationToken ct) =>
        TransitionAsync(holdId, studentId, HoldTransition.Cancel, ct);

    public async Task<IReadOnlyList<MyHoldResponse>> ListForStudentAsync(string studentId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await (
            from h in db.Holds.AsNoTracking()
            join c in db.Classes.AsNoTracking() on h.ClassId equals c.Id
            where h.StudentId == studentId
            orderby h.CreatedAt descending, h.Id
            select new { h.Id, h.ClassId, ClassName = c.Name, h.Status, h.ExpiresAt }).ToListAsync(ct);

        return rows.Select(r => new MyHoldResponse(r.Id, r.ClassId, r.ClassName,
            ToApiStatus(Hold.EffectiveStatus(r.Status, r.ExpiresAt, now)), r.ExpiresAt)).ToList();
    }

    private enum HoldTransition { Confirm, Cancel }

    /// <summary>
    /// Shared atomic state change for confirm and cancel; same pattern as the seat claim in
    /// <see cref="CreateAsync"/>. One statement both decides and applies the change:
    /// the guarded UPDATE on the hold row (owner, still HELD, not expired) is the only thing that can
    /// move a hold out of HELD, and the class counters move only for the row it returned. Concurrent
    /// requests for the same hold queue on that row's lock and re-check the guard under READ COMMITTED, so
    /// exactly one of them performs the transition and the counters move exactly once.
    /// Lock order is hold row then class row, the same as the expiry cleanup in <see cref="CreateAsync"/>.
    /// </summary>
    private async Task<HoldActionOutcome> TransitionAsync(string holdId, string studentId,
        HoldTransition kind, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        // Constants only; no request data is ever concatenated into the SQL text.
        var (holdSet, classSet) = kind == HoldTransition.Confirm
            ? ("\"Status\" = 'Confirmed', \"ConfirmedAt\" = @now",
               "\"HeldSeats\" = \"HeldSeats\" - 1, \"ConfirmedSeats\" = \"ConfirmedSeats\" + 1")
            : ("\"Status\" = 'Cancelled'",
               "\"HeldSeats\" = \"HeldSeats\" - 1");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var dbTx = tx.GetDbTransaction();

        var moved = await ExecuteScalarAsync(dbTx, $$"""
            WITH moved AS (
                UPDATE holds SET {{holdSet}}
                WHERE "Id" = @holdId AND "StudentId" = @studentId
                  AND "Status" = 'Held' AND "ExpiresAt" > @now
                RETURNING "ClassId")
            UPDATE classes SET {{classSet}}
            WHERE "Id" IN (SELECT "ClassId" FROM moved)
            RETURNING "Id"
            """, ct, ("holdId", holdId), ("studentId", studentId), ("now", now));

        if (moved is not null)
        {
            await tx.CommitAsync(ct);
            return HoldActionOutcome.Succeeded;
        }

        // Nothing changed (the transaction rolls back on dispose). Work out why, from committed state:
        // if a concurrent request won the race its transaction has already finished, because our
        // UPDATE waited on that row's lock.
        var row = await db.Holds.AsNoTracking()
            .Where(h => h.Id == holdId)
            .Select(h => new { h.StudentId, h.Status, h.ExpiresAt })
            .FirstOrDefaultAsync(ct);

        if (row is null) return HoldActionOutcome.NotFound;
        if (row.StudentId != studentId) return HoldActionOutcome.NotOwner;

        return Hold.EffectiveStatus(row.Status, row.ExpiresAt, now) switch
        {
            HoldStatus.Confirmed => HoldActionOutcome.AlreadyConfirmed,
            HoldStatus.Cancelled => HoldActionOutcome.AlreadyCancelled,
            HoldStatus.Expired => HoldActionOutcome.Expired,
            // Owner + HELD + unexpired is exactly what the UPDATE matches, and HELD never comes back once left.
            _ => throw new InvalidOperationException($"Hold '{holdId}' is active but the guarded update matched no row.")
        };
    }

    private static string ToApiStatus(HoldStatus status) => status.ToString().ToUpperInvariant();

    // Expired rows were swept in step 1, so any remaining HELD/CONFIRMED row is genuinely active.
    private Task<bool> HasActiveHoldAsync(string classId, string studentId, CancellationToken ct) =>
        db.Holds.AsNoTracking().AnyAsync(h =>
            h.ClassId == classId && h.StudentId == studentId &&
            (h.Status == HoldStatus.Held || h.Status == HoldStatus.Confirmed), ct);

    private async Task ExecuteAsync(DbTransaction tx, string sql, CancellationToken ct,
        params (string Name, object Value)[] args)
    {
        await using var cmd = Build(tx, sql, args);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<object?> ExecuteScalarAsync(DbTransaction tx, string sql, CancellationToken ct,
        params (string Name, object Value)[] args)
    {
        await using var cmd = Build(tx, sql, args);
        return await cmd.ExecuteScalarAsync(ct);
    }

    private DbCommand Build(DbTransaction tx, string sql, (string Name, object Value)[] args)
    {
        var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }
        return cmd;
    }
}
