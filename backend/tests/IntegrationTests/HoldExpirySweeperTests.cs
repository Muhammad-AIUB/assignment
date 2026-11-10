using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Services;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// The expiry sweep is housekeeping: it makes the stored rows match what the API already reports through
/// computed (lazy) expiry. These tests check that the sweep keeps the counters exact, and that
/// nothing the API returns depends on whether it has run. The shared factory runs with the background
/// worker disabled, so "unswept" rows stay unswept until a test calls the sweeper itself.
/// Every test uses its own class and its own student ids so tests stay independent in the shared DB.
/// </summary>
[Collection("api")]
public class HoldExpirySweeperTests(ApiFactory factory)
{
    private static readonly TimeSpan Past = TimeSpan.FromMinutes(-10);
    private static readonly TimeSpan Future = TimeSpan.FromMinutes(10);

    private readonly HttpClient _client = factory.CreateClient();

    private static long _nextStudent = 40_000_000_000_000_000L;
    private static string NewStudent() => Interlocked.Increment(ref _nextStudent).ToString();

    // ---------- helpers ----------

    private async Task<string> SeedClassAsync(int totalSeats)
    {
        var cls = new Class { Name = "Sweep-" + Guid.NewGuid().ToString("N")[..8], TotalSeats = totalSeats };
        await factory.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
        });
        return cls.Id;
    }

    /// <summary>Inserts hold rows directly and moves the class counters the way the endpoints would have.</summary>
    private async Task<string[]> SeedHoldsAsync(string classId, HoldStatus status, int count, TimeSpan expiresIn)
    {
        var now = DateTime.UtcNow;
        var holds = Enumerable.Range(0, count).Select(_ => new Hold
        {
            ClassId = classId,
            StudentId = NewStudent(),
            Status = status,
            CreatedAt = now.AddMinutes(-30),
            ExpiresAt = now + expiresIn,
            ConfirmedAt = status == HoldStatus.Confirmed ? now.AddMinutes(-29) : null
        }).ToArray();

        await factory.WithDbAsync(async db =>
        {
            db.Holds.AddRange(holds);
            if (status == HoldStatus.Held)
                await db.Classes.Where(c => c.Id == classId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.HeldSeats, c => c.HeldSeats + count));
            if (status == HoldStatus.Confirmed)
                await db.Classes.Where(c => c.Id == classId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConfirmedSeats, c => c.ConfirmedSeats + count));
            await db.SaveChangesAsync();
        });
        return holds.Select(h => h.Id).ToArray();
    }

    private Task<string[]> SeedDueAsync(string classId, int count) =>
        SeedHoldsAsync(classId, HoldStatus.Held, count, Past);

    private Task<string[]> SeedActiveAsync(string classId, int count) =>
        SeedHoldsAsync(classId, HoldStatus.Held, count, Future);

    private async Task<int> SweepAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IHoldExpirySweeper>().SweepAsync(CancellationToken.None);
    }

    private async Task<(int Held, int Confirmed)> CountersAsync(string classId)
    {
        var result = (Held: 0, Confirmed: 0);
        await factory.WithDbAsync(async db =>
        {
            var c = await db.Classes.AsNoTracking().SingleAsync(x => x.Id == classId);
            result = (c.HeldSeats, c.ConfirmedSeats);
        });
        return result;
    }

    private async Task<Dictionary<string, HoldStatus>> StoredStatusesAsync(string classId)
    {
        var result = new Dictionary<string, HoldStatus>();
        await factory.WithDbAsync(async db =>
        {
            result = await db.Holds.AsNoTracking()
                .Where(h => h.ClassId == classId)
                .ToDictionaryAsync(h => h.Id, h => h.Status);
        });
        return result;
    }

    private async Task<(string HoldStatus, int Held, int Confirmed, int Available)> ApiViewAsync(
        string classId, string holdId)
    {
        var hold = await _client.GetFromJsonAsync<JsonElement>($"/holds/{holdId}");
        var cls = await _client.GetFromJsonAsync<JsonElement>($"/classes/{classId}");
        return (hold.GetProperty("status").GetString()!,
                cls.GetProperty("heldSeats").GetInt32(),
                cls.GetProperty("confirmedSeats").GetInt32(),
                cls.GetProperty("availableSeats").GetInt32());
    }

    private Task<HttpResponseMessage> PostHoldAsync(string classId, string studentId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        req.Headers.Add("X-Student-Id", studentId);
        return _client.SendAsync(req);
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(200);
        }
        return await condition();
    }

    // ---------- the sweep ----------

    [Fact]
    public async Task Sweep_expires_due_holds_and_releases_exactly_those_seats()
    {
        var classId = await SeedClassAsync(20);
        var due = await SeedDueAsync(classId, 3);
        var active = await SeedActiveAsync(classId, 2);
        // A CONFIRMED hold keeps its seat after its ExpiresAt, and a CANCELLED one is already released.
        var confirmed = await SeedHoldsAsync(classId, HoldStatus.Confirmed, 1, Past);
        var cancelled = await SeedHoldsAsync(classId, HoldStatus.Cancelled, 1, Past);

        var swept = await SweepAsync();

        Assert.True(swept >= 3, $"expected at least the 3 due holds to be swept, got {swept}");
        var statuses = await StoredStatusesAsync(classId);
        Assert.All(due, id => Assert.Equal(HoldStatus.Expired, statuses[id]));
        Assert.All(active, id => Assert.Equal(HoldStatus.Held, statuses[id]));
        Assert.All(confirmed, id => Assert.Equal(HoldStatus.Confirmed, statuses[id]));
        Assert.All(cancelled, id => Assert.Equal(HoldStatus.Cancelled, statuses[id]));
        Assert.Equal((2, 1), await CountersAsync(classId));
    }

    [Fact]
    public async Task Sweep_is_idempotent()
    {
        var classId = await SeedClassAsync(10);
        await SeedDueAsync(classId, 4);
        await SeedActiveAsync(classId, 1);

        await SweepAsync();
        var second = await SweepAsync();

        Assert.Equal(0, second);
        Assert.Equal((1, 0), await CountersAsync(classId));
    }

    [Fact]
    public async Task Sweep_releases_seats_per_class_across_several_classes()
    {
        var a = await SeedClassAsync(10);
        var b = await SeedClassAsync(10);
        var dueA = await SeedDueAsync(a, 4);
        await SeedActiveAsync(a, 1);
        var dueB = await SeedDueAsync(b, 2);
        await SeedActiveAsync(b, 3);

        await SweepAsync();

        Assert.Equal((1, 0), await CountersAsync(a));
        Assert.Equal((3, 0), await CountersAsync(b));
        var statusesA = await StoredStatusesAsync(a);
        var statusesB = await StoredStatusesAsync(b);
        Assert.All(dueA, id => Assert.Equal(HoldStatus.Expired, statusesA[id]));
        Assert.All(dueB, id => Assert.Equal(HoldStatus.Expired, statusesB[id]));
    }

    [Fact]
    public async Task Sweep_drains_a_backlog_larger_than_one_batch()
    {
        var total = HoldExpirySweeper.BatchSize * 2 + 100;
        var classId = await SeedClassAsync(total + 10);
        var due = await SeedDueAsync(classId, total);

        await SweepAsync();

        var statuses = await StoredStatusesAsync(classId);
        Assert.All(due, id => Assert.Equal(HoldStatus.Expired, statuses[id]));
        Assert.Equal((0, 0), await CountersAsync(classId));
    }

    [Fact]
    public async Task Sweep_changes_nothing_the_api_reports()
    {
        var classId = await SeedClassAsync(10);
        var due = await SeedDueAsync(classId, 2);
        await SeedActiveAsync(classId, 1);

        var before = await ApiViewAsync(classId, due[0]);
        await SweepAsync();
        var after = await ApiViewAsync(classId, due[0]);

        // Correctness comes from the computed logic, so the answers are the same either side of the sweep.
        Assert.Equal(before, after);
        Assert.Equal(("EXPIRED", 1, 0, 9), after);
    }

    // ---------- concurrency ----------

    [Fact]
    public async Task Concurrent_sweeps_expire_every_hold_once_and_never_drift_the_counters()
    {
        var classes = new List<(string Id, string[] Due)>();
        for (var i = 0; i < 3; i++)
        {
            var id = await SeedClassAsync(500);
            classes.Add((id, await SeedDueAsync(id, 200)));
            await SeedActiveAsync(id, 25);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(SweepAsync)));

        Assert.True(results.Sum() >= 600, $"expected at least 600 holds swept in total, got {results.Sum()}");
        foreach (var (id, due) in classes)
        {
            var statuses = await StoredStatusesAsync(id);
            Assert.All(due, h => Assert.Equal(HoldStatus.Expired, statuses[h]));
            Assert.Equal((25, 0), await CountersAsync(id));
        }
    }

    /// <summary>
    /// Many requests all arrive at a class that has a pile of expired-but-unswept holds, so each one tries to
    /// clean up the same rows at once (optionally while sweeps run too). Every request must still succeed,
    /// and afterwards the counters must equal what the rows say. Repeated over several fresh classes because a
    /// lock-order problem only shows up on some interleavings.
    /// </summary>
    private async Task RunCleanupRaceAsync(int rounds, int dueHolds, int creates, int sweeps)
    {
        for (var round = 1; round <= rounds; round++)
        {
            var classId = await SeedClassAsync(dueHolds + creates + 10);
            var due = await SeedDueAsync(classId, dueHolds);

            var requests = Enumerable.Range(0, creates).Select(_ => PostHoldAsync(classId, NewStudent())).ToArray();
            var sweepers = Enumerable.Range(0, sweeps).Select(_ => Task.Run(SweepAsync)).ToArray();
            await Task.WhenAll(sweepers);
            var responses = await Task.WhenAll(requests);

            var failures = responses.Where(r => r.StatusCode != HttpStatusCode.Created)
                .Select(r => $"round {round}: {(int)r.StatusCode} {r.StatusCode}").ToList();
            Assert.True(failures.Count == 0,
                $"{failures.Count} of {creates} hold requests failed in round {round}:\n" + string.Join("\n", failures.Distinct()));

            var statuses = await StoredStatusesAsync(classId);
            Assert.All(due, id => Assert.Equal(HoldStatus.Expired, statuses[id]));
            Assert.Equal(creates, statuses.Values.Count(s => s == HoldStatus.Held));
            Assert.Equal((creates, 0), await CountersAsync(classId));
        }
    }

    [Fact]
    public async Task Concurrent_hold_requests_cleaning_up_the_same_expired_holds_never_fail()
    {
        // No sweeps at all: hold creation has to cope with expired rows on its own.
        await RunCleanupRaceAsync(rounds: 8, dueHolds: 80, creates: 40, sweeps: 0);
    }

    [Fact]
    public async Task Sweep_racing_hold_creation_keeps_counters_equal_to_the_rows()
    {
        // New holds clean up this class's expired rows themselves while the sweep is doing the same.
        await RunCleanupRaceAsync(rounds: 8, dueHolds: 80, creates: 40, sweeps: 6);
    }

    // ---------- the hosted worker ----------

    [Fact]
    public async Task Background_worker_expires_due_holds_without_any_request()
    {
        var classId = await SeedClassAsync(10);
        var due = await SeedDueAsync(classId, 2);
        await SeedActiveAsync(classId, 1);

        await using var withWorker = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("HOLD_EXPIRY_SWEEP_ENABLED", "true");
            b.UseSetting("HOLD_EXPIRY_SWEEP_INTERVAL_SECONDS", "1");
        });
        using var _ = withWorker.CreateClient(); // starts the host, and with it the worker

        var swept = await WaitUntilAsync(async () =>
        {
            var statuses = await StoredStatusesAsync(classId);
            return due.All(id => statuses[id] == HoldStatus.Expired);
        }, TimeSpan.FromSeconds(20));

        Assert.True(swept, "the worker did not expire the due holds within 20 seconds");
        Assert.Equal((1, 0), await CountersAsync(classId));
    }
}
