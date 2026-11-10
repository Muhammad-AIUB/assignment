using Api.Dtos;
using Api.Services;
using Domain.Entities;
using Infrastructure.Options;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace IntegrationTests;

/// <summary>A clock the test moves by hand, so expiry is tested at exact instants instead of by sleeping.</summary>
public sealed class FakeClock : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _now;

    /// <summary>Starts at the real current time cut to a whole second (Postgres keeps microseconds; whole seconds round-trip exactly).</summary>
    public FakeClock() => _now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    public DateTime UtcNow => GetUtcNow().UtcDateTime;

    public void Advance(TimeSpan by)
    {
        lock (_gate) _now += by;
    }
}

/// <summary>
/// Runs the real <see cref="HoldService"/> and <see cref="ClassService"/> against the real Postgres container, with the
/// clock and the hold duration under the test's control. Each call gets its own DI scope and DbContext, exactly as
/// each HTTP request would. Nothing is mocked.
/// </summary>
internal sealed class ServiceHarness(ApiFactory factory, FakeClock clock, int durationSeconds = 600)
{
    private static long _nextStudent = 50_000_000_000_000_000L;

    public FakeClock Clock { get; } = clock;

    public static string NewStudent() => Interlocked.Increment(ref _nextStudent).ToString();

    private async Task<T> RunAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private HoldService Holds(AppDbContext db) =>
        new(db, Clock, Options.Create(new HoldOptions { DurationSeconds = durationSeconds }));

    // ---------- classes ----------

    public async Task<string> CreateClassAsync(int seats) =>
        (await RunAsync(db => new ClassService(db, Clock)
            .CreateAsync(new CreateClassRequest("Svc-" + Guid.NewGuid().ToString("N")[..10], seats), default))).Id;

    public async Task<(int Held, int Confirmed, int Available)> ViewAsync(string classId)
    {
        var c = await RunAsync(db => new ClassService(db, Clock).GetAsync(classId, default));
        Assert.NotNull(c);
        return (c.HeldSeats, c.ConfirmedSeats, c.AvailableSeats);
    }

    /// <summary>The stored counters (what seat admission uses), as opposed to the view GET /classes derives from the rows.</summary>
    public Task<(int Held, int Confirmed)> CountersAsync(string classId) => RunAsync(async db =>
    {
        var c = await db.Classes.AsNoTracking().SingleAsync(x => x.Id == classId);
        return (c.HeldSeats, c.ConfirmedSeats);
    });

    // ---------- holds ----------

    public Task<CreateHoldResult> HoldAsync(string classId, string studentId) =>
        RunAsync(db => Holds(db).CreateAsync(classId, studentId, default));

    /// <summary>Creates a hold and asserts it was created.</summary>
    public async Task<HoldResponse> MustHoldAsync(string classId, string studentId)
    {
        var result = await HoldAsync(classId, studentId);
        Assert.Equal(CreateHoldOutcome.Created, result.Outcome);
        return result.Hold!;
    }

    public Task<HoldActionOutcome> ConfirmAsync(string holdId, string studentId) =>
        RunAsync(db => Holds(db).ConfirmAsync(holdId, studentId, default));

    public Task<HoldActionOutcome> CancelAsync(string holdId, string studentId) =>
        RunAsync(db => Holds(db).CancelAsync(holdId, studentId, default));

    public Task<HoldStatusResponse?> GetHoldAsync(string holdId) =>
        RunAsync(db => Holds(db).GetAsync(holdId, default));

    public Task<IReadOnlyList<MyHoldResponse>> MyHoldsAsync(string studentId) =>
        RunAsync(db => Holds(db).ListForStudentAsync(studentId, default));

    /// <summary>The hold row exactly as stored: what the API says is computed, this is what the database says.</summary>
    public Task<Hold> StoredHoldAsync(string holdId) => RunAsync(db => db.Holds.AsNoTracking().SingleAsync(h => h.Id == holdId));
}
