using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Services;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// The expiry rule: a student has HOLD_DURATION_SECONDS to confirm, then the seat is released, and nothing depends on a
/// background job having run. The real services run against real Postgres; the clock is a <see cref="FakeClock"/> the test
/// moves, so the boundary is tested at the exact instant. One test at the end uses the real clock and the real HTTP
/// stack to prove it also works in wall-clock time.
/// </summary>
[Collection("api")]
public class HoldExpiryRuleTests(ApiFactory factory)
{
    private readonly FakeClock _clock = new();

    private ServiceHarness Svc(int durationSeconds = 600) => new(factory, _clock, durationSeconds);

    private static string Student() => ServiceHarness.NewStudent();

    // ---------- the window is HOLD_DURATION_SECONDS ----------

    [Theory]
    [InlineData(600)]
    [InlineData(45)]
    [InlineData(1)]
    public async Task A_hold_expires_exactly_the_configured_number_of_seconds_after_it_was_created(int seconds)
    {
        var svc = Svc(seconds);
        var classId = await svc.CreateClassAsync(2);
        var createdAt = _clock.UtcNow;

        var hold = await svc.MustHoldAsync(classId, Student());

        Assert.Equal(createdAt.AddSeconds(seconds), hold.ExpiresAt);
        var stored = await svc.StoredHoldAsync(hold.HoldId);
        Assert.Equal(createdAt, stored.CreatedAt);
        Assert.Equal(createdAt.AddSeconds(seconds), stored.ExpiresAt);
        Assert.Equal(HoldStatus.Held, stored.Status);
    }

    // ---------- the boundary ----------

    [Theory]
    [InlineData(-1, "HELD")]       // one millisecond before: still the student's seat
    [InlineData(0, "EXPIRED")]     // the expiry instant itself: gone
    [InlineData(1, "EXPIRED")]
    [InlineData(3_600_000, "EXPIRED")]
    public async Task A_hold_reads_HELD_until_the_last_instant_and_EXPIRED_from_the_expiry_instant_on(
        int msFromExpiry, string expected)
    {
        var svc = Svc();
        var hold = await svc.MustHoldAsync(await svc.CreateClassAsync(2), Student());

        _clock.Advance(TimeSpan.FromSeconds(600) + TimeSpan.FromMilliseconds(msFromExpiry));

        var seen = await svc.GetHoldAsync(hold.HoldId);
        Assert.Equal(expected, seen!.Status);
        Assert.Equal(hold.ExpiresAt, seen.ExpiresAt); // reading never moves the expiry
    }

    [Fact]
    public async Task Confirming_one_millisecond_before_the_expiry_still_works()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(2);
        var student = Student();
        var hold = await svc.MustHoldAsync(classId, student);

        _clock.Advance(TimeSpan.FromSeconds(600) - TimeSpan.FromMilliseconds(1));

        Assert.Equal(HoldActionOutcome.Succeeded, await svc.ConfirmAsync(hold.HoldId, student));
        Assert.Equal((0, 1, 1), await svc.ViewAsync(classId));
    }

    [Fact]
    public async Task Confirming_at_the_expiry_instant_is_refused_and_changes_nothing()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(2);
        var student = Student();
        var hold = await svc.MustHoldAsync(classId, student);

        _clock.Advance(TimeSpan.FromSeconds(600));

        Assert.Equal(HoldActionOutcome.Expired, await svc.ConfirmAsync(hold.HoldId, student));
        Assert.Equal((0, 0, 2), await svc.ViewAsync(classId)); // no confirmed seat appeared, the held one is gone
        var stored = await svc.StoredHoldAsync(hold.HoldId);
        Assert.Null(stored.ConfirmedAt);
        Assert.Equal((1, 0), await svc.CountersAsync(classId)); // refused requests do not touch the counters
    }

    [Fact]
    public async Task Cancelling_one_millisecond_before_the_expiry_releases_the_seat_and_at_the_instant_it_is_refused()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(2);
        var early = Student();
        var late = Student();
        var earlyHold = await svc.MustHoldAsync(classId, early);
        var lateHold = await svc.MustHoldAsync(classId, late);

        _clock.Advance(TimeSpan.FromSeconds(600) - TimeSpan.FromMilliseconds(1));
        Assert.Equal(HoldActionOutcome.Succeeded, await svc.CancelAsync(earlyHold.HoldId, early));
        Assert.Equal("CANCELLED", (await svc.GetHoldAsync(earlyHold.HoldId))!.Status);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(HoldActionOutcome.Expired, await svc.CancelAsync(lateHold.HoldId, late));
        Assert.Equal("EXPIRED", (await svc.GetHoldAsync(lateHold.HoldId))!.Status);
    }

    // ---------- the seat comes back, with no background job ----------

    [Fact]
    public async Task The_seat_is_free_again_the_instant_the_hold_expires_even_though_nothing_has_swept_it()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(2);
        var first = await svc.MustHoldAsync(classId, Student());
        _clock.Advance(TimeSpan.FromMinutes(1));
        await svc.MustHoldAsync(classId, Student());
        Assert.Equal((2, 0, 0), await svc.ViewAsync(classId));

        _clock.Advance(TimeSpan.FromMinutes(9)); // the first hold's expiry instant; the second still has a minute

        Assert.Equal((1, 0, 1), await svc.ViewAsync(classId));
        // Proof that this is computed, not swept: the database still says the old hold is HELD and counts it.
        Assert.Equal(HoldStatus.Held, (await svc.StoredHoldAsync(first.HoldId)).Status);
        Assert.Equal((2, 0), await svc.CountersAsync(classId));
    }

    [Fact]
    public async Task A_different_student_can_take_the_seat_of_an_expired_hold_and_the_old_holder_cannot_get_it_back()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(1);
        var first = Student();
        var second = Student();
        var firstHold = await svc.MustHoldAsync(classId, first);
        Assert.Equal(CreateHoldOutcome.NoSeatAvailable, (await svc.HoldAsync(classId, second)).Outcome);

        _clock.Advance(TimeSpan.FromSeconds(600));
        var secondHold = await svc.MustHoldAsync(classId, second);

        Assert.Equal("EXPIRED", (await svc.GetHoldAsync(firstHold.HoldId))!.Status);
        Assert.Equal("HELD", (await svc.GetHoldAsync(secondHold.HoldId))!.Status);
        Assert.Equal(HoldActionOutcome.Expired, await svc.ConfirmAsync(firstHold.HoldId, first));
        Assert.Equal(CreateHoldOutcome.NoSeatAvailable, (await svc.HoldAsync(classId, first)).Outcome);
        Assert.Equal(HoldActionOutcome.Succeeded, await svc.ConfirmAsync(secondHold.HoldId, second));
        Assert.Equal((0, 1, 0), await svc.ViewAsync(classId));
        Assert.Equal((0, 1), await svc.CountersAsync(classId));
    }

    [Fact]
    public async Task Holds_made_at_different_times_expire_independently()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(3);
        var early = Student();
        var late = Student();
        var earlyHold = await svc.MustHoldAsync(classId, early);
        _clock.Advance(TimeSpan.FromMinutes(4));
        var lateHold = await svc.MustHoldAsync(classId, late);

        _clock.Advance(TimeSpan.FromMinutes(6)); // 10 minutes after the first, 6 after the second
        Assert.Equal("EXPIRED", (await svc.GetHoldAsync(earlyHold.HoldId))!.Status);
        Assert.Equal("HELD", (await svc.GetHoldAsync(lateHold.HoldId))!.Status);
        Assert.Equal((1, 0, 2), await svc.ViewAsync(classId));

        _clock.Advance(TimeSpan.FromMinutes(4)); // 10 minutes after the second
        Assert.Equal("EXPIRED", (await svc.GetHoldAsync(lateHold.HoldId))!.Status);
        Assert.Equal((0, 0, 3), await svc.ViewAsync(classId));
    }

    // ---------- the statuses that must NOT expire ----------

    [Fact]
    public async Task A_confirmed_admission_never_expires_and_still_counts_as_the_students_seat()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(2);
        var student = Student();
        var hold = await svc.MustHoldAsync(classId, student);
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HoldActionOutcome.Succeeded, await svc.ConfirmAsync(hold.HoldId, student));

        _clock.Advance(TimeSpan.FromDays(30)); // far past the hold's original expiry

        Assert.Equal("CONFIRMED", (await svc.GetHoldAsync(hold.HoldId))!.Status);
        Assert.Equal((0, 1, 1), await svc.ViewAsync(classId));
        Assert.Equal(CreateHoldOutcome.DuplicateActiveHold, (await svc.HoldAsync(classId, student)).Outcome);
    }

    [Fact]
    public async Task Re_confirming_after_the_original_expiry_time_is_still_the_idempotent_success_and_cancelling_is_still_409_confirmed()
    {
        // The expiry rule must not leak into a confirmed hold: its ExpiresAt is in the past, but it is not an expired hold.
        var svc = Svc();
        var classId = await svc.CreateClassAsync(2);
        var student = Student();
        var hold = await svc.MustHoldAsync(classId, student);
        Assert.Equal(HoldActionOutcome.Succeeded, await svc.ConfirmAsync(hold.HoldId, student));

        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(HoldActionOutcome.Succeeded, await svc.ConfirmAsync(hold.HoldId, student));
        Assert.Equal(HoldActionOutcome.AlreadyConfirmed, await svc.CancelAsync(hold.HoldId, student));
        Assert.Equal((0, 1), await svc.CountersAsync(classId));
    }

    [Fact]
    public async Task A_cancelled_hold_stays_cancelled_after_its_time_runs_out()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(2);
        var student = Student();
        var hold = await svc.MustHoldAsync(classId, student);
        Assert.Equal(HoldActionOutcome.Succeeded, await svc.CancelAsync(hold.HoldId, student));

        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal("CANCELLED", (await svc.GetHoldAsync(hold.HoldId))!.Status);
        Assert.Equal(HoldActionOutcome.AlreadyCancelled, await svc.ConfirmAsync(hold.HoldId, student));
        Assert.Equal(HoldActionOutcome.AlreadyCancelled, await svc.CancelAsync(hold.HoldId, student));
        Assert.Equal((0, 0), await svc.CountersAsync(classId));
    }

    [Fact]
    public async Task Confirming_records_when_it_happened_and_leaves_the_original_expiry_alone()
    {
        var svc = Svc();
        var student = Student();
        var hold = await svc.MustHoldAsync(await svc.CreateClassAsync(2), student);
        _clock.Advance(TimeSpan.FromMinutes(3));
        var confirmedAt = _clock.UtcNow;

        Assert.Equal(HoldActionOutcome.Succeeded, await svc.ConfirmAsync(hold.HoldId, student));

        var stored = await svc.StoredHoldAsync(hold.HoldId);
        Assert.Equal(HoldStatus.Confirmed, stored.Status);
        Assert.Equal(confirmedAt, stored.ConfirmedAt);
        Assert.Equal(hold.ExpiresAt, stored.ExpiresAt);
    }

    // ---------- expiry and the one-active-hold rule ----------

    [Fact]
    public async Task An_expired_hold_does_not_stop_the_same_student_holding_again_and_the_history_is_kept()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(2);
        var student = Student();
        var old = await svc.MustHoldAsync(classId, student);

        _clock.Advance(TimeSpan.FromSeconds(600));
        var fresh = await svc.MustHoldAsync(classId, student);

        Assert.NotEqual(old.HoldId, fresh.HoldId);
        var mine = await svc.MyHoldsAsync(student);
        Assert.Equal([fresh.HoldId, old.HoldId], mine.Select(h => h.HoldId).ToArray());
        Assert.Equal(["HELD", "EXPIRED"], mine.Select(h => h.Status).ToArray());
        Assert.Equal((1, 0, 1), await svc.ViewAsync(classId));
    }

    // ---------- the same rule against the real clock, over real HTTP ----------

    [Fact]
    public async Task With_a_short_HOLD_DURATION_SECONDS_the_seat_really_does_come_back_in_wall_clock_time()
    {
        // This host reads HOLD_DURATION_SECONDS=4. The background sweep is off (the shared factory disables it), so
        // everything the client sees below comes from the computed expiry alone.
        await using var app = factory.WithWebHostBuilder(b => b.UseSetting("HOLD_DURATION_SECONDS", "4"));
        using var client = app.CreateClient();
        await client.GetAsync("/health"); // warm the host up so the timing below is not spent on start-up

        var classId = (await (await client.PostAsJsonAsync("/classes", new { name = "Wall-" + Guid.NewGuid().ToString("N")[..8], totalSeats = 1 }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var first = ServiceHarness.NewStudent();
        var second = ServiceHarness.NewStudent();

        Task<HttpResponseMessage> Send(HttpMethod method, string url, string student)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.Add("X-Student-Id", student);
            return client.SendAsync(req);
        }

        var before = DateTime.UtcNow;
        var created = await Send(HttpMethod.Post, $"/classes/{classId}/holds", first);
        var after = DateTime.UtcNow;
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var holdId = body.GetProperty("holdId").GetString()!;
        var expiresAt = body.GetProperty("expiresAt").GetDateTimeOffset().UtcDateTime;
        // The server stamped "now" somewhere between before and after, then added exactly 4 seconds.
        Assert.InRange(expiresAt, before.AddSeconds(4), after.AddSeconds(4));

        // While the hold is alive the only seat is taken.
        if (expiresAt - DateTime.UtcNow > TimeSpan.FromSeconds(1))
        {
            var blocked = await Send(HttpMethod.Post, $"/classes/{classId}/holds", second);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal("NO_SEAT_AVAILABLE", (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        }

        var wait = expiresAt - DateTime.UtcNow + TimeSpan.FromMilliseconds(300);
        if (wait > TimeSpan.Zero) await Task.Delay(wait);

        // Time is up: the hold reads EXPIRED, the seat is back, and confirming is refused.
        var hold = await client.GetFromJsonAsync<JsonElement>($"/holds/{holdId}");
        Assert.Equal("EXPIRED", hold.GetProperty("status").GetString());
        var cls = await client.GetFromJsonAsync<JsonElement>($"/classes/{classId}");
        Assert.Equal(0, cls.GetProperty("heldSeats").GetInt32());
        Assert.Equal(1, cls.GetProperty("availableSeats").GetInt32());

        var confirm = await Send(HttpMethod.Post, $"/holds/{holdId}/confirm", first);
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        Assert.Equal("HOLD_EXPIRED", (await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        // No job ran: the row is still stored as Held. The next student simply takes the seat.
        await factory.WithDbAsync(async db =>
        {
            var stored = await db.Holds.AsNoTracking().SingleAsync(h => h.Id == holdId);
            Assert.Equal(HoldStatus.Held, stored.Status);
        });
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, $"/classes/{classId}/holds", second)).StatusCode);
    }
}
