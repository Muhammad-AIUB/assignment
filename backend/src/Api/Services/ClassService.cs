using Api.Dtos;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public class ClassService(AppDbContext db, TimeProvider clock) : IClassService
{
    public async Task<ClassResponse> CreateAsync(CreateClassRequest request, CancellationToken ct)
    {
        var entity = new Class { Name = request.Name.Trim(), TotalSeats = request.TotalSeats };
        db.Classes.Add(entity);
        await db.SaveChangesAsync(ct);
        return new ClassResponse(entity.Id, entity.Name, entity.TotalSeats);
    }

    public async Task<IReadOnlyList<ClassSummaryResponse>> ListAsync(CancellationToken ct)
    {
        var rows = await Rows(db.Classes).OrderBy(r => r.Name).ThenBy(r => r.Id).ToListAsync(ct);
        return rows.Select(ToResponse).ToList();
    }

    public async Task<ClassSummaryResponse?> GetAsync(string classId, CancellationToken ct)
    {
        var row = await Rows(db.Classes.Where(c => c.Id == classId)).FirstOrDefaultAsync(ct);
        return row is null ? null : ToResponse(row);
    }

    private static ClassSummaryResponse ToResponse(Row r) => new(
        r.Id, r.Name, r.TotalSeats, r.Held, r.Confirmed,
        Math.Max(0, r.TotalSeats - r.Held - r.Confirmed));

    // One SQL statement: per-class counts are correlated subqueries over holds.
    // Expired-but-unswept HELD rows (ExpiresAt <= now) do not count as held.
    private IQueryable<Row> Rows(IQueryable<Class> source)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return source.AsNoTracking().Select(c => new Row
        {
            Id = c.Id,
            Name = c.Name,
            TotalSeats = c.TotalSeats,
            Held = db.Holds.Count(h => h.ClassId == c.Id && h.Status == HoldStatus.Held && h.ExpiresAt > now),
            Confirmed = db.Holds.Count(h => h.ClassId == c.Id && h.Status == HoldStatus.Confirmed)
        });
    }

    private sealed class Row
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public int TotalSeats { get; init; }
        public int Held { get; init; }
        public int Confirmed { get; init; }
    }
}
