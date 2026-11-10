using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTests;

/// <summary>
/// Restart persistence: everything the API knows lives in PostgreSQL, nothing in the process. So when the API process is
/// killed and a brand-new one is started on the same database, every class, hold and seat count is as it was and every
/// rule keeps being enforced. The restart is real: the first <c>dotnet Api.dll</c> process is killed, a second one is
/// started (new PID, new memory, new connection pool), and only the database connects the two.
/// </summary>
// In the "api" collection only so it never overlaps the other integration tests (see MultiProcessSeatRaceTests).
[Collection("api")]
public class RestartPersistenceTests(ITestOutputHelper output)
{
    private static string Student(int n) => SeatRace.StudentId(n);

    [Fact]
    public async Task Classes_holds_and_seat_counts_are_unchanged_after_the_api_process_is_replaced_and_the_rules_still_apply()
    {
        var pg = new PostgreSqlBuilder("postgres:16").Build();
        await pg.StartAsync();
        try
        {
            string classId, confirmedHold, heldHold, cancelledHold, heldExpiresBefore;
            int firstPid, firstPort;

            // --- before the restart: 4 seats, one confirmed, one still held, one cancelled -----------------------------
            var first = await ApiProcess.StartAsync("before-restart", pg.GetConnectionString());
            try
            {
                firstPid = first.Pid;
                firstPort = first.Port;
                classId = await SeatRace.CreateClassAsync(first.Client, 4);

                confirmedHold = (await HoldAsync(first.Client, classId, Student(1))).HoldId;
                Assert.Equal(HttpStatusCode.OK, (await SendAsync(first.Client, HttpMethod.Post, $"/holds/{confirmedHold}/confirm", Student(1))).StatusCode);

                heldHold = (await HoldAsync(first.Client, classId, Student(2))).HoldId;
                heldExpiresBefore = (await GetJsonAsync(first.Client, $"/holds/{heldHold}")).GetProperty("expiresAt").GetString()!;

                cancelledHold = (await HoldAsync(first.Client, classId, Student(3))).HoldId;
                Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(first.Client, HttpMethod.Delete, $"/holds/{cancelledHold}", Student(3))).StatusCode);

                AssertCounts(await GetJsonAsync(first.Client, $"/classes/{classId}"), total: 4, held: 1, confirmed: 1, available: 2);
            }
            finally
            {
                await first.DisposeAsync(); // kills the process and its children: the restart
            }

            // The old server is really gone.
            using (var probe = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{firstPort}"), Timeout = TimeSpan.FromSeconds(3) })
                await Assert.ThrowsAnyAsync<HttpRequestException>(() => probe.GetAsync("/health"));

            // --- after the restart: a different process on the same database -----------------------------------------
            await using var second = await ApiProcess.StartAsync("after-restart", pg.GetConnectionString());
            output.WriteLine($"before restart: pid {firstPid}, port {firstPort}; after restart: pid {second.Pid}, port {second.Port}");
            Assert.NotEqual(firstPid, second.Pid);
            var api = second.Client;

            // Same class, same counts.
            AssertCounts(await GetJsonAsync(api, $"/classes/{classId}"), total: 4, held: 1, confirmed: 1, available: 2);

            // Same holds, same statuses, and the held one keeps its original expiry (it is not restarted or extended).
            Assert.Equal("CONFIRMED", (await GetJsonAsync(api, $"/holds/{confirmedHold}")).GetProperty("status").GetString());
            var held = await GetJsonAsync(api, $"/holds/{heldHold}");
            Assert.Equal("HELD", held.GetProperty("status").GetString());
            Assert.Equal(heldExpiresBefore, held.GetProperty("expiresAt").GetString());
            Assert.Equal("CANCELLED", (await GetJsonAsync(api, $"/holds/{cancelledHold}")).GetProperty("status").GetString());

            // The student's own list is intact too.
            var mine = await GetJsonAsync(api, "/me/holds", Student(2));
            Assert.Equal(heldHold, Assert.Single(mine.EnumerateArray()).GetProperty("holdId").GetString());

            // One active hold per student per class is still enforced against the pre-restart rows.
            Assert.Equal("DUPLICATE_ACTIVE_HOLD", await HoldErrorAsync(api, classId, Student(1))); // confirmed before the restart
            Assert.Equal("DUPLICATE_ACTIVE_HOLD", await HoldErrorAsync(api, classId, Student(2))); // still held

            // A hold made before the restart can still be confirmed, and the seat is counted once.
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(api, HttpMethod.Post, $"/holds/{heldHold}/confirm", Student(2))).StatusCode);
            AssertCounts(await GetJsonAsync(api, $"/classes/{classId}"), total: 4, held: 0, confirmed: 2, available: 2);

            // The seats left are claimable exactly as many times as there are seats: 2 more 201s, then sold out.
            await HoldAsync(api, classId, Student(3)); // cancelled before the restart, so it may hold again
            await HoldAsync(api, classId, Student(4));
            Assert.Equal("NO_SEAT_AVAILABLE", await HoldErrorAsync(api, classId, Student(5)));
            AssertCounts(await GetJsonAsync(api, $"/classes/{classId}"), total: 4, held: 2, confirmed: 2, available: 0);
        }
        finally
        {
            await pg.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_hold_that_runs_out_while_the_api_is_down_is_expired_and_its_seat_is_free_when_the_api_returns()
    {
        // A short window and the background sweep OFF, so the only thing that can report the hold as expired after the
        // restart is the computed rule applied to the stored expiry time. No timer from the first process survives it.
        var env = new Dictionary<string, string>
        {
            ["HOLD_DURATION_SECONDS"] = "3",
            ["HOLD_EXPIRY_SWEEP_ENABLED"] = "false",
        };

        var pg = new PostgreSqlBuilder("postgres:16").Build();
        await pg.StartAsync();
        try
        {
            string classId, holdId;
            DateTime expiresAt;

            var first = await ApiProcess.StartAsync("before-restart", pg.GetConnectionString(), env);
            try
            {
                classId = await SeatRace.CreateClassAsync(first.Client, 1);
                var hold = await HoldAsync(first.Client, classId, Student(1));
                holdId = hold.HoldId;
                expiresAt = DateTime.Parse(hold.ExpiresAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

                Assert.Equal("HELD", (await GetJsonAsync(first.Client, $"/holds/{holdId}")).GetProperty("status").GetString());
                Assert.Equal(1, (await GetJsonAsync(first.Client, $"/classes/{classId}")).GetProperty("heldSeats").GetInt32());
            }
            finally
            {
                await first.DisposeAsync();
            }

            // Wait until the hold's expiry has really passed, with the API down.
            var remaining = expiresAt - DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);

            await using var second = await ApiProcess.StartAsync("after-restart", pg.GetConnectionString(), env);
            var api = second.Client;

            Assert.Equal("EXPIRED", (await GetJsonAsync(api, $"/holds/{holdId}")).GetProperty("status").GetString());
            AssertCounts(await GetJsonAsync(api, $"/classes/{classId}"), total: 1, held: 0, confirmed: 0, available: 1);

            // The expired owner cannot confirm; the single seat goes to the next student.
            Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(api, HttpMethod.Post, $"/holds/{holdId}/confirm", Student(1))).StatusCode);
            await HoldAsync(api, classId, Student(2));
            Assert.Equal("NO_SEAT_AVAILABLE", await HoldErrorAsync(api, classId, Student(3)));
        }
        finally
        {
            await pg.DisposeAsync();
        }
    }

    // --- helpers ---------------------------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string? student = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (student is not null) request.Headers.Add("X-Student-Id", student);
        return client.SendAsync(request);
    }

    private static async Task<(string HoldId, string ExpiresAt)> HoldAsync(HttpClient client, string classId, string student)
    {
        var res = await SendAsync(client, HttpMethod.Post, $"/classes/{classId}/holds", student);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("holdId").GetString()!, body.GetProperty("expiresAt").GetString()!);
    }

    private static async Task<string?> HoldErrorAsync(HttpClient client, string classId, string student)
    {
        var res = await SendAsync(client, HttpMethod.Post, $"/classes/{classId}/holds", student);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url, string? student = null)
    {
        var res = await SendAsync(client, HttpMethod.Get, url, student);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static void AssertCounts(JsonElement cls, int total, int held, int confirmed, int available)
    {
        Assert.Equal(total, cls.GetProperty("totalSeats").GetInt32());
        Assert.Equal(held, cls.GetProperty("heldSeats").GetInt32());
        Assert.Equal(confirmed, cls.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(available, cls.GetProperty("availableSeats").GetInt32());
    }
}
