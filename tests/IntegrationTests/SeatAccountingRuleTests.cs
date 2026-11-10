using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Services;
using Domain.Entities;
using Domain.Enums;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Seat accounting and class setup, without concurrency: how heldSeats, confirmedSeats and availableSeats move as a hold goes
/// through its life, that confirmed seats stay taken for good, and the rules for creating and listing classes.
/// The real services run against real Postgres, with a clock the test controls.
/// </summary>
[Collection("api")]
public class SeatAccountingRuleTests(ApiFactory factory)
{
    private readonly FakeClock _clock = new();
    private readonly HttpClient _client = factory.CreateClient();

    private ServiceHarness Svc() => new(factory, _clock);

    private static string Student() => ServiceHarness.NewStudent();

    // ---------- held + confirmed + available == total, at every step ----------

    [Fact]
    public async Task Seat_counts_follow_holds_through_confirming_cancelling_and_expiring()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(3);
        var a = Student();
        var b = Student();
        var c = Student();
        Assert.Equal((0, 0, 3), await svc.ViewAsync(classId));

        var holdA = await svc.MustHoldAsync(classId, a);
        Assert.Equal((1, 0, 2), await svc.ViewAsync(classId));        // a hold takes a seat at once

        var holdB = await svc.MustHoldAsync(classId, b);
        Assert.Equal((2, 0, 1), await svc.ViewAsync(classId));

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HoldActionOutcome.Succeeded, await svc.ConfirmAsync(holdA.HoldId, a));
        Assert.Equal((1, 1, 1), await svc.ViewAsync(classId));        // confirming turns held into confirmed: available unchanged

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HoldActionOutcome.Succeeded, await svc.CancelAsync(holdB.HoldId, b));
        Assert.Equal((0, 1, 2), await svc.ViewAsync(classId));        // cancelling gives the seat straight back

        _clock.Advance(TimeSpan.FromMinutes(1));
        var holdC = await svc.MustHoldAsync(classId, c);
        Assert.Equal((1, 1, 1), await svc.ViewAsync(classId));

        _clock.Advance(TimeSpan.FromSeconds(600));                    // c's hold runs out
        Assert.Equal((0, 1, 2), await svc.ViewAsync(classId));        // expiry gives the seat back, the confirmed seat stays
        Assert.Equal("EXPIRED", (await svc.GetHoldAsync(holdC.HoldId))!.Status);
        Assert.Equal("CONFIRMED", (await svc.GetHoldAsync(holdA.HoldId))!.Status);
    }

    [Fact]
    public async Task A_class_whose_seats_are_all_confirmed_is_full_and_nothing_a_student_can_do_reopens_it()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(2);
        var a = Student();
        var b = Student();
        var holdA = await svc.MustHoldAsync(classId, a);
        var holdB = await svc.MustHoldAsync(classId, b);
        await svc.ConfirmAsync(holdA.HoldId, a);
        await svc.ConfirmAsync(holdB.HoldId, b);
        Assert.Equal((0, 2, 0), await svc.ViewAsync(classId));

        // a newcomer is turned away, and so is time passing, and so is trying to cancel a confirmed seat
        Assert.Equal(CreateHoldOutcome.NoSeatAvailable, (await svc.HoldAsync(classId, Student())).Outcome);
        _clock.Advance(TimeSpan.FromDays(365));
        Assert.Equal(CreateHoldOutcome.NoSeatAvailable, (await svc.HoldAsync(classId, Student())).Outcome);
        Assert.Equal(HoldActionOutcome.AlreadyConfirmed, await svc.CancelAsync(holdA.HoldId, a));

        Assert.Equal((0, 2, 0), await svc.ViewAsync(classId));
        Assert.Equal((0, 2), await svc.CountersAsync(classId));
    }

    [Fact]
    public async Task A_refused_hold_takes_no_seat_whichever_way_it_was_refused()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(1);
        var holder = Student();
        await svc.MustHoldAsync(classId, holder);

        await svc.HoldAsync(classId, holder);        // duplicate
        await svc.HoldAsync(classId, Student());     // sold out
        await svc.HoldAsync("c_does_not_exist", Student()); // no such class

        Assert.Equal((1, 0, 0), await svc.ViewAsync(classId));
        Assert.Equal((1, 0), await svc.CountersAsync(classId));
    }

    [Fact]
    public async Task Available_seats_are_never_negative_even_if_the_hold_rows_add_up_to_more_than_the_total()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(2);

        // Rows only, no counters: three confirmed students in a two-seat class (the counters' check constraint would refuse this).
        await factory.WithDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            for (var i = 0; i < 3; i++)
                db.Holds.Add(new Hold
                {
                    ClassId = classId, StudentId = Student(), Status = HoldStatus.Confirmed,
                    CreatedAt = now, ExpiresAt = now, ConfirmedAt = now
                });
            await db.SaveChangesAsync();
        });

        var (held, confirmed, available) = await svc.ViewAsync(classId);
        Assert.Equal((0, 3), (held, confirmed));
        Assert.Equal(0, available);
    }

    // ---------- POST /classes and GET /classes ----------

    private async Task<JsonElement> CreateAsync(string name, int seats)
    {
        var res = await _client.PostAsJsonAsync("/classes", new { name, totalSeats = seats });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task A_new_class_starts_with_every_seat_available()
    {
        var created = await CreateAsync("Fresh-" + Guid.NewGuid().ToString("N")[..8], 120);

        var fetched = await _client.GetFromJsonAsync<JsonElement>($"/classes/{created.GetProperty("id").GetString()}");

        Assert.Equal(120, fetched.GetProperty("totalSeats").GetInt32());
        Assert.Equal(0, fetched.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, fetched.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(120, fetched.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task A_class_can_have_a_single_seat()
    {
        var created = await CreateAsync("Tiny-" + Guid.NewGuid().ToString("N")[..8], 1);

        Assert.Equal(1, created.GetProperty("totalSeats").GetInt32());
    }

    [Fact]
    public async Task The_class_name_is_trimmed_before_it_is_stored()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var created = await CreateAsync($"  \t Class 7 {suffix}  ", 10);

        Assert.Equal($"Class 7 {suffix}", created.GetProperty("name").GetString());
        var fetched = await _client.GetFromJsonAsync<JsonElement>($"/classes/{created.GetProperty("id").GetString()}");
        Assert.Equal($"Class 7 {suffix}", fetched.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_name_of_exactly_200_characters_is_kept_intact_and_one_more_is_a_400()
    {
        var name = new string('n', 200);
        var created = await CreateAsync(name, 10);
        var fetched = await _client.GetFromJsonAsync<JsonElement>($"/classes/{created.GetProperty("id").GetString()}");
        Assert.Equal(name, fetched.GetProperty("name").GetString());

        var tooLong = await _client.PostAsJsonAsync("/classes", new { name = name + "n", totalSeats = 10 });

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var error = await tooLong.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("error").GetString());
        Assert.Contains("200", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Names_in_other_scripts_survive_the_round_trip()
    {
        var name = "ষষ্ঠ শ্রেণী – সকাল শিফট " + Guid.NewGuid().ToString("N")[..6];

        var created = await CreateAsync(name, 10);

        var fetched = await _client.GetFromJsonAsync<JsonElement>($"/classes/{created.GetProperty("id").GetString()}");
        Assert.Equal(name, fetched.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Two_classes_may_share_a_name_and_are_still_separate_classes()
    {
        var name = "Twin-" + Guid.NewGuid().ToString("N")[..8];

        var first = await CreateAsync(name, 5);
        var second = await CreateAsync(name, 9);

        Assert.NotEqual(first.GetProperty("id").GetString(), second.GetProperty("id").GetString());
        Assert.Equal(5, first.GetProperty("totalSeats").GetInt32());
        Assert.Equal(9, second.GetProperty("totalSeats").GetInt32());
    }

    [Fact]
    public async Task The_class_list_is_ordered_by_name_then_id()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var zeta = await CreateAsync($"Zeta-{tag}", 5);
        var same1 = await CreateAsync($"Same-{tag}", 5);
        var alpha = await CreateAsync($"Alpha-{tag}", 5);
        var same2 = await CreateAsync($"Same-{tag}", 5);
        var sameIds = new[] { same1, same2 }.Select(c => c.GetProperty("id").GetString()!).Order(StringComparer.Ordinal).ToArray();

        var list = await _client.GetFromJsonAsync<JsonElement>("/classes");

        var mine = list.EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!)
            .Where(id => id == alpha.GetProperty("id").GetString() || id == zeta.GetProperty("id").GetString() || sameIds.Contains(id))
            .ToArray();
        Assert.Equal([alpha.GetProperty("id").GetString()!, sameIds[0], sameIds[1], zeta.GetProperty("id").GetString()!], mine);
    }
}
