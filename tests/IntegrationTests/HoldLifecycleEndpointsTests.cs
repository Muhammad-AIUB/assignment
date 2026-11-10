using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// GET /holds/{id}, POST /holds/{id}/confirm, DELETE /holds/{id}, GET /me/holds.
/// Every test uses its own class and its own student ids so tests stay independent in the shared DB.
/// </summary>
[Collection("api")]
public class HoldLifecycleEndpointsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static long _nextStudent = 30_000_000_000_000_000L;
    private static string NewStudent() => Interlocked.Increment(ref _nextStudent).ToString();

    // ---------- helpers ----------

    private async Task<string> CreateClassAsync(int seats, string? name = null)
    {
        var res = await _client.PostAsJsonAsync("/classes",
            new { name = name ?? "C-" + Guid.NewGuid().ToString("N")[..8], totalSeats = seats });
        res.EnsureSuccessStatusCode();
        return (await Json(res)).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? studentId)
    {
        var req = new HttpRequestMessage(method, url);
        if (studentId is not null) req.Headers.Add("X-Student-Id", studentId);
        return _client.SendAsync(req);
    }

    private Task<HttpResponseMessage> HoldAsync(string classId, string studentId) =>
        SendAsync(HttpMethod.Post, $"/classes/{classId}/holds", studentId);

    private Task<HttpResponseMessage> ConfirmAsync(string holdId, string? studentId) =>
        SendAsync(HttpMethod.Post, $"/holds/{holdId}/confirm", studentId);

    private Task<HttpResponseMessage> CancelAsync(string holdId, string? studentId) =>
        SendAsync(HttpMethod.Delete, $"/holds/{holdId}", studentId);

    private Task<HttpResponseMessage> GetHoldAsync(string holdId) =>
        _client.GetAsync($"/holds/{holdId}");

    private Task<HttpResponseMessage> MyHoldsAsync(string? studentId) =>
        SendAsync(HttpMethod.Get, "/me/holds", studentId);

    private async Task<string> HoldIdAsync(string classId, string studentId)
    {
        var res = await HoldAsync(classId, studentId);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Json(res)).GetProperty("holdId").GetString()!;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res) =>
        await res.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        Assert.Equal(status, res.StatusCode);
        var body = await Json(res);
        Assert.Equal(code, body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }

    private async Task<string> StatusOfAsync(string holdId)
    {
        var res = await GetHoldAsync(holdId);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await Json(res)).GetProperty("status").GetString()!;
    }

    /// <summary>Inserts a hold row directly, keeping the class counters consistent with it.</summary>
    private async Task<string> SeedAsync(string classId, string studentId, HoldStatus status,
        DateTime createdAt, DateTime expiresAt)
    {
        var hold = new Hold
        {
            ClassId = classId, StudentId = studentId, Status = status,
            CreatedAt = createdAt, ExpiresAt = expiresAt,
            ConfirmedAt = status == HoldStatus.Confirmed ? createdAt : null
        };
        await factory.WithDbAsync(async db =>
        {
            db.Holds.Add(hold);
            if (status == HoldStatus.Held)
                await db.Classes.Where(c => c.Id == classId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.HeldSeats, c => c.HeldSeats + 1));
            if (status == HoldStatus.Confirmed)
                await db.Classes.Where(c => c.Id == classId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConfirmedSeats, c => c.ConfirmedSeats + 1));
            await db.SaveChangesAsync();
        });
        return hold.Id;
    }

    private Task<string> SeedExpiredHeldAsync(string classId, string studentId) =>
        SeedAsync(classId, studentId, HoldStatus.Held, DateTime.UtcNow.AddMinutes(-20), DateTime.UtcNow.AddMinutes(-10));

    private async Task<(int Held, int Confirmed)> CountersAsync(string classId)
    {
        (int, int) result = default;
        await factory.WithDbAsync(async db =>
        {
            var c = await db.Classes.AsNoTracking().SingleAsync(x => x.Id == classId);
            result = (c.HeldSeats, c.ConfirmedSeats);
        });
        return result;
    }

    private async Task<(int Held, int Confirmed, int Available)> ClassViewAsync(string classId)
    {
        var body = await Json(await _client.GetAsync($"/classes/{classId}"));
        return (body.GetProperty("heldSeats").GetInt32(),
                body.GetProperty("confirmedSeats").GetInt32(),
                body.GetProperty("availableSeats").GetInt32());
    }

    // ---------- GET /holds/{holdId} ----------

    [Fact]
    public async Task Get_hold_returns_contract_shape()
    {
        var classId = await CreateClassAsync(3);
        var holdId = await HoldIdAsync(classId, NewStudent());

        var res = await GetHoldAsync(holdId);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json(res);
        Assert.Equal(new[] { "classId", "expiresAt", "holdId", "status" },
            body.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(holdId, body.GetProperty("holdId").GetString());
        Assert.Equal(classId, body.GetProperty("classId").GetString());
        Assert.Equal("HELD", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow);
    }

    [Fact]
    public async Task Get_unknown_hold_is_404()
    {
        await AssertError(await GetHoldAsync("h_missing"), HttpStatusCode.NotFound, "HOLD_NOT_FOUND");
    }

    [Fact]
    public async Task Get_hold_reports_EXPIRED_without_any_background_job()
    {
        var classId = await CreateClassAsync(3);
        var holdId = await SeedExpiredHeldAsync(classId, NewStudent()); // row still says Held in the DB

        var body = await Json(await GetHoldAsync(holdId));

        Assert.Equal("EXPIRED", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("expiresAt").GetDateTime() < DateTime.UtcNow);
    }

    [Fact]
    public async Task Get_confirmed_hold_stays_CONFIRMED_even_after_its_original_expiry_time()
    {
        var classId = await CreateClassAsync(3);
        var holdId = await SeedAsync(classId, NewStudent(), HoldStatus.Confirmed,
            DateTime.UtcNow.AddMinutes(-20), DateTime.UtcNow.AddMinutes(-10));

        Assert.Equal("CONFIRMED", await StatusOfAsync(holdId));
    }

    // ---------- POST /holds/{holdId}/confirm ----------

    [Fact]
    public async Task Owner_confirms_a_held_hold()
    {
        var classId = await CreateClassAsync(3);
        var student = NewStudent();
        var holdId = await HoldIdAsync(classId, student);

        var res = await ConfirmAsync(holdId, student);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json(res);
        Assert.Equal(new[] { "holdId", "status" }, body.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(holdId, body.GetProperty("holdId").GetString());
        Assert.Equal("CONFIRMED", body.GetProperty("status").GetString());

        Assert.Equal("CONFIRMED", await StatusOfAsync(holdId));
        Assert.Equal((0, 1, 2), await ClassViewAsync(classId));
        Assert.Equal((0, 1), await CountersAsync(classId));
    }

    [Fact]
    public async Task Confirming_twice_is_idempotent_and_does_not_double_count()
    {
        var classId = await CreateClassAsync(3);
        var student = NewStudent();
        var holdId = await HoldIdAsync(classId, student);

        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(holdId, student)).StatusCode);
        var second = await ConfirmAsync(holdId, student);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("CONFIRMED", (await Json(second)).GetProperty("status").GetString());
        Assert.Equal((0, 1, 2), await ClassViewAsync(classId));
        Assert.Equal((0, 1), await CountersAsync(classId));
    }

    [Fact]
    public async Task Another_student_cannot_confirm_and_the_hold_is_untouched()
    {
        var classId = await CreateClassAsync(3);
        var holdId = await HoldIdAsync(classId, NewStudent());

        await AssertError(await ConfirmAsync(holdId, NewStudent()), HttpStatusCode.Conflict, "NOT_HOLD_OWNER");

        Assert.Equal("HELD", await StatusOfAsync(holdId));
        Assert.Equal((1, 0), await CountersAsync(classId));
    }

    [Fact]
    public async Task Another_student_cannot_re_confirm_an_already_confirmed_hold()
    {
        var classId = await CreateClassAsync(3);
        var owner = NewStudent();
        var holdId = await HoldIdAsync(classId, owner);
        await ConfirmAsync(holdId, owner);

        // Idempotency applies to the owner only.
        await AssertError(await ConfirmAsync(holdId, NewStudent()), HttpStatusCode.Conflict, "NOT_HOLD_OWNER");
    }

    [Fact]
    public async Task Expired_hold_cannot_be_confirmed_even_though_no_job_has_run()
    {
        var classId = await CreateClassAsync(3);
        var student = NewStudent();
        var holdId = await SeedExpiredHeldAsync(classId, student);

        await AssertError(await ConfirmAsync(holdId, student), HttpStatusCode.Conflict, "HOLD_EXPIRED");

        Assert.Equal("EXPIRED", await StatusOfAsync(holdId));
        Assert.Equal(0, (await CountersAsync(classId)).Confirmed);
    }

    [Fact]
    public async Task Cancelled_hold_cannot_be_confirmed()
    {
        var classId = await CreateClassAsync(3);
        var student = NewStudent();
        var holdId = await HoldIdAsync(classId, student);
        Assert.Equal(HttpStatusCode.NoContent, (await CancelAsync(holdId, student)).StatusCode);

        await AssertError(await ConfirmAsync(holdId, student), HttpStatusCode.Conflict, "HOLD_CANCELLED");
        Assert.Equal((0, 0), await CountersAsync(classId));
    }

    [Fact]
    public async Task Confirming_unknown_hold_is_404()
    {
        await AssertError(await ConfirmAsync("h_missing", NewStudent()), HttpStatusCode.NotFound, "HOLD_NOT_FOUND");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567890123456")]
    [InlineData("123456789012345678")]
    [InlineData("2015269123456789a")]
    public async Task Confirm_rejects_missing_or_malformed_student_id(string? id)
    {
        var classId = await CreateClassAsync(3);
        var holdId = await HoldIdAsync(classId, NewStudent());
        await AssertError(await ConfirmAsync(holdId, id), HttpStatusCode.BadRequest, "INVALID_STUDENT_ID");
    }

    [Fact]
    public async Task Concurrent_confirms_by_the_owner_all_succeed_but_count_once()
    {
        var classId = await CreateClassAsync(3);
        var student = NewStudent();
        var holdId = await HoldIdAsync(classId, student);

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(async () => (await ConfirmAsync(holdId, student)).StatusCode)));

        Assert.All(results, s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Equal((0, 1), await CountersAsync(classId));
        Assert.Equal((0, 1, 2), await ClassViewAsync(classId));
    }

    // ---------- DELETE /holds/{holdId} ----------

    [Fact]
    public async Task Owner_cancels_a_held_hold_and_the_seat_is_released_immediately()
    {
        var classId = await CreateClassAsync(1);
        var first = NewStudent();
        var holdId = await HoldIdAsync(classId, first);

        // Class is full right now.
        await AssertError(await HoldAsync(classId, NewStudent()), HttpStatusCode.Conflict, "NO_SEAT_AVAILABLE");

        var res = await CancelAsync(holdId, first);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Empty(await res.Content.ReadAsByteArrayAsync());
        Assert.Equal("CANCELLED", await StatusOfAsync(holdId));
        Assert.Equal((0, 0), await CountersAsync(classId));
        Assert.Equal((0, 0, 1), await ClassViewAsync(classId));

        // The hold had not expired, yet the seat is already free for somebody else.
        Assert.Equal(HttpStatusCode.Created, (await HoldAsync(classId, NewStudent())).StatusCode);
    }

    [Fact]
    public async Task Student_can_hold_again_after_cancelling()
    {
        var classId = await CreateClassAsync(2);
        var student = NewStudent();
        var holdId = await HoldIdAsync(classId, student);
        await CancelAsync(holdId, student);

        Assert.Equal(HttpStatusCode.Created, (await HoldAsync(classId, student)).StatusCode);
    }

    [Fact]
    public async Task Another_student_cannot_cancel_and_the_hold_is_untouched()
    {
        var classId = await CreateClassAsync(3);
        var holdId = await HoldIdAsync(classId, NewStudent());

        await AssertError(await CancelAsync(holdId, NewStudent()), HttpStatusCode.Conflict, "NOT_HOLD_OWNER");

        Assert.Equal("HELD", await StatusOfAsync(holdId));
        Assert.Equal((1, 0), await CountersAsync(classId));
    }

    [Fact]
    public async Task Expired_hold_cannot_be_cancelled()
    {
        var classId = await CreateClassAsync(3);
        var student = NewStudent();
        var holdId = await SeedExpiredHeldAsync(classId, student);

        await AssertError(await CancelAsync(holdId, student), HttpStatusCode.Conflict, "HOLD_EXPIRED");
        Assert.Equal("EXPIRED", await StatusOfAsync(holdId));
    }

    [Fact]
    public async Task Confirmed_hold_cannot_be_cancelled_and_keeps_its_seat()
    {
        var classId = await CreateClassAsync(3);
        var student = NewStudent();
        var holdId = await HoldIdAsync(classId, student);
        await ConfirmAsync(holdId, student);

        await AssertError(await CancelAsync(holdId, student), HttpStatusCode.Conflict, "HOLD_ALREADY_CONFIRMED");

        Assert.Equal("CONFIRMED", await StatusOfAsync(holdId));
        Assert.Equal((0, 1), await CountersAsync(classId));
    }

    [Fact]
    public async Task Cancelling_twice_is_409_and_does_not_release_a_second_seat()
    {
        var classId = await CreateClassAsync(3);
        var other = NewStudent();
        await HoldIdAsync(classId, other); // a second holder, so a wrong decrement would be visible
        var student = NewStudent();
        var holdId = await HoldIdAsync(classId, student);

        Assert.Equal(HttpStatusCode.NoContent, (await CancelAsync(holdId, student)).StatusCode);
        await AssertError(await CancelAsync(holdId, student), HttpStatusCode.Conflict, "HOLD_CANCELLED");

        Assert.Equal((1, 0), await CountersAsync(classId));
    }

    [Fact]
    public async Task Cancelling_unknown_hold_is_404()
    {
        await AssertError(await CancelAsync("h_missing", NewStudent()), HttpStatusCode.NotFound, "HOLD_NOT_FOUND");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567890123456")]
    [InlineData("123456789012345678")]
    [InlineData("2015269123456789a")]
    public async Task Cancel_rejects_missing_or_malformed_student_id(string? id)
    {
        var classId = await CreateClassAsync(3);
        var holdId = await HoldIdAsync(classId, NewStudent());
        await AssertError(await CancelAsync(holdId, id), HttpStatusCode.BadRequest, "INVALID_STUDENT_ID");
    }

    [Fact]
    public async Task Concurrent_cancels_release_the_seat_exactly_once()
    {
        var classId = await CreateClassAsync(3);
        await HoldIdAsync(classId, NewStudent()); // unrelated holder that must keep its seat
        var student = NewStudent();
        var holdId = await HoldIdAsync(classId, student);

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(async () => (await CancelAsync(holdId, student)).StatusCode)));

        Assert.Equal(1, results.Count(s => s == HttpStatusCode.NoContent));
        Assert.Equal(9, results.Count(s => s == HttpStatusCode.Conflict));
        Assert.Equal((1, 0), await CountersAsync(classId));
    }

    [Fact]
    public async Task Concurrent_confirm_and_cancel_exactly_one_wins_and_counters_agree()
    {
        for (var round = 0; round < 10; round++)
        {
            var classId = await CreateClassAsync(2);
            var student = NewStudent();
            var holdId = await HoldIdAsync(classId, student);

            var confirm = Task.Run(async () => (await ConfirmAsync(holdId, student)).StatusCode);
            var cancel = Task.Run(async () => (await CancelAsync(holdId, student)).StatusCode);
            await Task.WhenAll(confirm, cancel);

            var confirmWon = confirm.Result == HttpStatusCode.OK;
            var cancelWon = cancel.Result == HttpStatusCode.NoContent;
            Assert.True(confirmWon ^ cancelWon, $"round {round}: confirm={confirm.Result} cancel={cancel.Result}");

            Assert.Equal(confirmWon ? "CONFIRMED" : "CANCELLED", await StatusOfAsync(holdId));
            Assert.Equal(confirmWon ? (0, 1) : (0, 0), await CountersAsync(classId));
        }
    }

    // ---------- GET /me/holds ----------

    [Fact]
    public async Task My_holds_returns_contract_shape_with_class_name_and_only_my_holds()
    {
        var student = NewStudent();
        var classId = await CreateClassAsync(3, "Class 6 - Morning Shift");
        var holdId = await HoldIdAsync(classId, student);
        await HoldIdAsync(classId, NewStudent()); // somebody else's hold must not show up

        var res = await MyHoldsAsync(student);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var items = (await Json(res)).EnumerateArray().ToList();
        var item = Assert.Single(items);
        Assert.Equal(new[] { "classId", "className", "expiresAt", "holdId", "status" },
            item.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(holdId, item.GetProperty("holdId").GetString());
        Assert.Equal(classId, item.GetProperty("classId").GetString());
        Assert.Equal("Class 6 - Morning Shift", item.GetProperty("className").GetString());
        Assert.Equal("HELD", item.GetProperty("status").GetString());
        Assert.True(item.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow);
    }

    [Fact]
    public async Task My_holds_lists_every_status_newest_first_with_computed_expiry()
    {
        var student = NewStudent();
        var now = DateTime.UtcNow;
        var expiredClass = await CreateClassAsync(3);
        var confirmedClass = await CreateClassAsync(3);
        var cancelledClass = await CreateClassAsync(3);
        var heldClass = await CreateClassAsync(3);

        var expired = await SeedAsync(expiredClass, student, HoldStatus.Held, now.AddMinutes(-40), now.AddMinutes(-30));
        var confirmed = await SeedAsync(confirmedClass, student, HoldStatus.Confirmed, now.AddMinutes(-30), now.AddMinutes(-20));
        var cancelled = await SeedAsync(cancelledClass, student, HoldStatus.Cancelled, now.AddMinutes(-20), now.AddMinutes(-10));
        var held = await HoldIdAsync(heldClass, student);

        var items = (await Json(await MyHoldsAsync(student))).EnumerateArray().ToList();

        Assert.Equal(new[] { held, cancelled, confirmed, expired },
            items.Select(i => i.GetProperty("holdId").GetString()).ToArray());
        Assert.Equal(new[] { "HELD", "CANCELLED", "CONFIRMED", "EXPIRED" },
            items.Select(i => i.GetProperty("status").GetString()).ToArray());
    }

    [Fact]
    public async Task My_holds_is_an_empty_array_for_a_student_with_no_holds()
    {
        var res = await MyHoldsAsync(NewStudent());

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Empty((await Json(res)).EnumerateArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567890123456")]
    [InlineData("123456789012345678")]
    [InlineData("2015269123456789a")]
    public async Task My_holds_rejects_missing_or_malformed_student_id(string? id)
    {
        await AssertError(await MyHoldsAsync(id), HttpStatusCode.BadRequest, "INVALID_STUDENT_ID");
    }
}
