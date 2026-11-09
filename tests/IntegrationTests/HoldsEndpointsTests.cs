using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

[Collection("api")]
public class HoldsEndpointsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> CreateClassAsync(int seats)
    {
        var res = await _client.PostAsJsonAsync("/classes", new { name = "C-" + Guid.NewGuid().ToString("N")[..8], totalSeats = seats });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> HoldAsync(string classId, string? studentId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        if (studentId is not null) req.Headers.Add("X-Student-Id", studentId);
        return _client.SendAsync(req);
    }

    private static string Student(int n) => (20150000000000000L + n).ToString();

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        Assert.Equal(status, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Creates_hold_with_contract_shape()
    {
        var classId = await CreateClassAsync(3);
        var res = await HoldAsync(classId, Student(1));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("h_", body.GetProperty("holdId").GetString());
        Assert.Equal(classId, body.GetProperty("classId").GetString());
        Assert.Equal(Student(1), body.GetProperty("studentId").GetString());
        Assert.Equal("HELD", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow);

        var cls = await (await _client.GetAsync($"/classes/{classId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, cls.GetProperty("heldSeats").GetInt32());
        Assert.Equal(2, cls.GetProperty("availableSeats").GetInt32());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567890123456")]
    [InlineData("123456789012345678")]
    [InlineData("2015269123456789a")]
    public async Task Rejects_missing_or_malformed_student_id(string? id)
    {
        var classId = await CreateClassAsync(3);
        await AssertError(await HoldAsync(classId, id), HttpStatusCode.BadRequest, "INVALID_STUDENT_ID");
    }

    [Fact]
    public async Task Unknown_class_is_404()
    {
        await AssertError(await HoldAsync("c_missing", Student(1)), HttpStatusCode.NotFound, "CLASS_NOT_FOUND");
    }

    [Fact]
    public async Task Full_class_is_409()
    {
        var classId = await CreateClassAsync(1);
        Assert.Equal(HttpStatusCode.Created, (await HoldAsync(classId, Student(1))).StatusCode);
        await AssertError(await HoldAsync(classId, Student(2)), HttpStatusCode.Conflict, "NO_SEAT_AVAILABLE");
    }

    [Fact]
    public async Task Duplicate_active_hold_is_409_and_does_not_consume_a_seat()
    {
        var classId = await CreateClassAsync(5);
        Assert.Equal(HttpStatusCode.Created, (await HoldAsync(classId, Student(1))).StatusCode);
        await AssertError(await HoldAsync(classId, Student(1)), HttpStatusCode.Conflict, "DUPLICATE_ACTIVE_HOLD");

        var cls = await (await _client.GetAsync($"/classes/{classId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, cls.GetProperty("heldSeats").GetInt32());
    }

    [Fact]
    public async Task Student_with_confirmed_admission_cannot_hold_again()
    {
        var classId = await CreateClassAsync(5);
        await factory.WithDbAsync(async db =>
        {
            db.Holds.Add(new Hold { ClassId = classId, StudentId = Student(1), Status = HoldStatus.Confirmed,
                CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(-1), ConfirmedAt = DateTime.UtcNow });
            await db.Classes.Where(c => c.Id == classId).ExecuteUpdateAsync(s => s.SetProperty(c => c.ConfirmedSeats, 1));
            await db.SaveChangesAsync();
        });
        await AssertError(await HoldAsync(classId, Student(1)), HttpStatusCode.Conflict, "DUPLICATE_ACTIVE_HOLD");
    }

    [Fact]
    public async Task Expired_hold_releases_its_seat_and_does_not_block_the_same_student()
    {
        var classId = await CreateClassAsync(1);
        await factory.WithDbAsync(async db =>
        {
            db.Holds.Add(new Hold { ClassId = classId, StudentId = Student(1), Status = HoldStatus.Held,
                CreatedAt = DateTime.UtcNow.AddMinutes(-20), ExpiresAt = DateTime.UtcNow.AddMinutes(-10) });
            await db.Classes.Where(c => c.Id == classId).ExecuteUpdateAsync(s => s.SetProperty(c => c.HeldSeats, 1));
            await db.SaveChangesAsync();
        });

        // Same student can re-hold the only seat once the old hold has expired.
        Assert.Equal(HttpStatusCode.Created, (await HoldAsync(classId, Student(1))).StatusCode);
        await factory.WithDbAsync(async db =>
        {
            var c = await db.Classes.AsNoTracking().SingleAsync(x => x.Id == classId);
            Assert.Equal(1, c.HeldSeats);
            Assert.Equal(1, await db.Holds.CountAsync(h => h.ClassId == classId && h.Status == HoldStatus.Expired));
        });
    }

    [Fact]
    public async Task Concurrent_requests_never_oversell()
    {
        const int seats = 5, students = 60;
        var classId = await CreateClassAsync(seats);

        var results = await Task.WhenAll(Enumerable.Range(1, students)
            .Select(i => Task.Run(async () => (await HoldAsync(classId, Student(i))).StatusCode)));

        Assert.Equal(seats, results.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(students - seats, results.Count(s => s == HttpStatusCode.Conflict));

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(seats, await db.Holds.CountAsync(h => h.ClassId == classId && h.Status == HoldStatus.Held));
            var c = await db.Classes.AsNoTracking().SingleAsync(x => x.Id == classId);
            Assert.Equal(seats, c.HeldSeats);
        });
    }

    [Fact]
    public async Task Concurrent_requests_from_one_student_create_exactly_one_hold()
    {
        var classId = await CreateClassAsync(50);

        var results = await Task.WhenAll(Enumerable.Range(0, 30)
            .Select(_ => Task.Run(async () => (await HoldAsync(classId, Student(7))).StatusCode)));

        Assert.Equal(1, results.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(29, results.Count(s => s == HttpStatusCode.Conflict));

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.Holds.CountAsync(h => h.ClassId == classId));
            var c = await db.Classes.AsNoTracking().SingleAsync(x => x.Id == classId);
            Assert.Equal(1, c.HeldSeats); // rolled-back duplicates did not leak counter increments
        });
    }
}
