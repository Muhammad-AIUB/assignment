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
