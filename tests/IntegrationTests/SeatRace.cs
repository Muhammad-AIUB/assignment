using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTests;

/// <summary>One POST /classes/{id}/holds request and what came back (status 0 = the request itself failed).</summary>
internal sealed record SeatAttempt(int Index, int Target, HttpStatusCode Status, string? ErrorCode, string? HoldId);

/// <summary>What the database looked like, sampled continuously while the race ran.</summary>
internal sealed record SeatObservation(
    int Samples,
    int SamplesDuringRace,
    int MaxSeatsTaken,       // highest HeldSeats + ConfirmedSeats seen in any sample
    int MaxActiveHoldRows,   // highest count of HELD/CONFIRMED rows seen in any sample
    int FinalSeatsTaken,
    int FinalActiveHoldRows,
    int FinalHoldRows,       // every row for the class, whatever its status
    int ViolationCount,
    IReadOnlyList<string> Violations);

internal sealed record SeatRaceResult(
    string ClassId,
    int TotalSeats,
    int Requests,
    int Targets,
    IReadOnlyList<SeatAttempt> Attempts,
    SeatObservation Observed,
    int PeakInFlight,
    TimeSpan Elapsed)
{
    public int Created => Attempts.Count(a => a.Status == HttpStatusCode.Created);
    public int Conflict => Attempts.Count(a => a.Status == HttpStatusCode.Conflict);

    public string Summary()
    {
        var sb = new StringBuilder();
        sb.Append($"class {ClassId}: {TotalSeats} seats, {Requests} parallel requests -> ")
          .Append($"{Created} x 201, {Conflict} x 409, {Requests - Created - Conflict} other, in {Elapsed.TotalMilliseconds:F0} ms; ")
          .Append($"peak {PeakInFlight} requests in flight. ")
          .Append($"Database sampled {Observed.Samples} times ({Observed.SamplesDuringRace} while the requests were running): ")
          .Append($"most seats taken at once {Observed.MaxSeatsTaken} (limit {TotalSeats}), ")
          .Append($"most active hold rows at once {Observed.MaxActiveHoldRows}, ")
          .Append($"final {Observed.FinalSeatsTaken} seats / {Observed.FinalActiveHoldRows} active rows / {Observed.FinalHoldRows} rows.");
        if (Targets > 1)
            for (var t = 0; t < Targets; t++)
                sb.Append($" [instance {t}: {Attempts.Count(a => a.Target == t)} sent, {Attempts.Count(a => a.Target == t && a.Status == HttpStatusCode.Created)} won]");
        return sb.ToString();
    }
}

/// <summary>
/// The seat race used by the concurrency tests: many students ask for a seat in the same class at the same moment,
/// against one or more running API instances, while a separate database connection watches the class the whole time.
/// </summary>
internal static class SeatRace
{
    /// <summary>A valid 17-digit student id; every request in a race uses its own.</summary>
    public static string StudentId(int n) => (20260000000000000L + n).ToString();

    public static async Task<string> CreateClassAsync(HttpClient client, int seats)
    {
        var res = await client.PostAsJsonAsync("/classes",
            new { name = "Race-" + Guid.NewGuid().ToString("N")[..8], totalSeats = seats });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Fires <paramref name="requests"/> hold requests (one distinct student each), spread round-robin over
    /// <paramref name="targets"/>, released together from a start gate so they really collide. A sampler reads the class's
    /// counters and its hold rows in one statement over and over for the whole run and once more after the last response.
    /// </summary>
    public static async Task<SeatRaceResult> RunAsync(
        string connectionString, string classId, int totalSeats, int requests, IReadOnlyList<HttpClient> targets)
    {
        await using var sampler = await SeatSampler.OpenAsync(connectionString, classId, totalSeats);
        using var stop = new CancellationTokenSource();
        var loop = Task.Run(() => sampler.LoopAsync(stop.Token));

        SeatAttempt[] attempts;
        int peak = 0, samplesBefore;
        TimeSpan elapsed;
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int parked = 0, inFlight = 0;

            var tasks = Enumerable.Range(0, requests).Select(i => Task.Run(async () =>
            {
                Interlocked.Increment(ref parked);
                await gate.Task;
                RaiseTo(ref peak, Interlocked.Increment(ref inFlight));
                try { return await SendAsync(targets[i % targets.Count], classId, i, i % targets.Count); }
                finally { Interlocked.Decrement(ref inFlight); }
            })).ToArray();

            // Every request is waiting at the gate before any is let through.
            while (Volatile.Read(ref parked) < requests) await Task.Delay(1);

            samplesBefore = sampler.Samples;
            var clock = Stopwatch.StartNew();
            gate.SetResult();
            attempts = await Task.WhenAll(tasks);
            clock.Stop();
            elapsed = clock.Elapsed;
        }
        finally
        {
            stop.Cancel();
            await Task.WhenAny(loop); // wait for the sampler to stop without hiding the race's own failure
        }
        await loop;                   // but do surface a sampler failure

        var samplesDuring = sampler.Samples - samplesBefore;
        var final = await sampler.SampleAsync(); // the state after the very last response

        var observed = new SeatObservation(
            sampler.Samples, samplesDuring, sampler.MaxSeatsTaken, sampler.MaxActiveHoldRows,
            final.Held + final.Confirmed, final.ActiveRows, final.AllRows,
            sampler.ViolationCount, sampler.Violations);

        return new SeatRaceResult(classId, totalSeats, requests, targets.Count, attempts, observed, peak, elapsed);
    }

    /// <summary>The assertions every race must satisfy, in one place so the single-process and multi-process tests cannot drift apart.</summary>
    public static void AssertNeverOversold(SeatRaceResult r, ITestOutputHelper output)
    {
        output.WriteLine(r.Summary());
        var summary = r.Summary();

        // Nothing but 201 and 409: no 5xx, no dropped connection, no timeout.
        var other = r.Attempts.Where(a => a.Status is not (HttpStatusCode.Created or HttpStatusCode.Conflict)).ToList();
        Assert.True(other.Count == 0,
            $"{other.Count} request(s) were neither 201 nor 409: " +
            string.Join("; ", other.Take(5).Select(a => $"#{a.Index} -> {(int)a.Status} {a.ErrorCode}")) + $". {summary}");

        // Exactly totalSeats winners, everyone else turned away.
        Assert.True(r.Created == r.TotalSeats, $"expected exactly {r.TotalSeats} x 201 but got {r.Created}. {summary}");
        Assert.True(r.Conflict == r.Requests - r.TotalSeats,
            $"expected exactly {r.Requests - r.TotalSeats} x 409 but got {r.Conflict}. {summary}");

        // The losers were refused because the class was full, not for some other 409 (every student is distinct,
        // so DUPLICATE_ACTIVE_HOLD would mean a bug).
        Assert.All(r.Attempts.Where(a => a.Status == HttpStatusCode.Conflict),
            a => Assert.Equal("NO_SEAT_AVAILABLE", a.ErrorCode));

        // The winners hold distinct, real holds.
        var holdIds = r.Attempts.Where(a => a.Status == HttpStatusCode.Created).Select(a => a.HoldId).ToList();
        Assert.All(holdIds, id => Assert.StartsWith("h_", id));
        Assert.Equal(r.TotalSeats, holdIds.Distinct().Count());

        // At no sampled instant did the database show more seats taken than exist, and the counters always matched the rows.
        Assert.True(r.Observed.ViolationCount == 0,
            $"{r.Observed.ViolationCount} bad sample(s): {string.Join(" | ", r.Observed.Violations)}. {summary}");
        Assert.InRange(r.Observed.MaxSeatsTaken, 0, r.TotalSeats);
        Assert.InRange(r.Observed.MaxActiveHoldRows, 0, r.TotalSeats);

        // And the final state: every seat taken, nothing more, no stray rows from the refused requests.
        Assert.Equal(r.TotalSeats, r.Observed.FinalSeatsTaken);
        Assert.Equal(r.TotalSeats, r.Observed.FinalActiveHoldRows);
        Assert.Equal(r.TotalSeats, r.Observed.FinalHoldRows);

        // The race really was a race: the requests overlapped and the database was being watched while they ran.
        Assert.True(r.PeakInFlight >= 2, $"requests never overlapped (peak in flight {r.PeakInFlight}); this was not a concurrent test. {summary}");
        Assert.True(r.Observed.SamplesDuringRace >= 1, $"the sampler took no sample while the requests were running. {summary}");
    }

    private static async Task<SeatAttempt> SendAsync(HttpClient client, string classId, int index, int target)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            req.Headers.Add("X-Student-Id", StudentId(index));
            using var res = await client.SendAsync(req);

            string? error = null, holdId = null;
            var text = await res.Content.ReadAsStringAsync();
            if (text.Length > 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (doc.RootElement.TryGetProperty("error", out var e)) error = e.GetString();
                        if (doc.RootElement.TryGetProperty("holdId", out var h)) holdId = h.GetString();
                    }
                }
                catch (JsonException) { error = "NON_JSON_BODY"; }
            }
            return new SeatAttempt(index, target, res.StatusCode, error, holdId);
        }
        catch (Exception ex)
        {
            // A request that did not complete is an outcome to report, not a reason to abort the whole race.
            return new SeatAttempt(index, target, 0, $"{ex.GetType().Name}: {ex.Message}", null);
        }
    }

    private static void RaiseTo(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
    }

    private readonly record struct Snapshot(int Held, int Confirmed, int ActiveRows, int AllRows);

    /// <summary>
    /// Watches one class over its own connection (its own pool, so it never takes a connection the API needs). Each sample
    /// is a single SQL statement, so it sees one consistent snapshot of the counters and the rows together.
    /// </summary>
    private sealed class SeatSampler : IAsyncDisposable
    {
        private const string Sql = """
            SELECT c."HeldSeats", c."ConfirmedSeats",
                   (SELECT count(*) FROM holds h WHERE h."ClassId" = c."Id" AND h."Status" IN ('Held', 'Confirmed')),
                   (SELECT count(*) FROM holds h WHERE h."ClassId" = c."Id")
            FROM classes c WHERE c."Id" = @id
            """;

        private const int MaxViolationsKept = 10;

        private readonly NpgsqlConnection _conn;
        private readonly NpgsqlCommand _cmd;
        private readonly int _totalSeats;
        private readonly List<string> _violations = [];
        private int _samples, _maxSeatsTaken, _maxActiveRows, _violationCount;

        private SeatSampler(NpgsqlConnection conn, string classId, int totalSeats)
        {
            _conn = conn;
            _totalSeats = totalSeats;
            _cmd = new NpgsqlCommand(Sql, conn);
            _cmd.Parameters.AddWithValue("id", classId);
        }

        public int Samples => Volatile.Read(ref _samples);
        public int MaxSeatsTaken => _maxSeatsTaken;
        public int MaxActiveHoldRows => _maxActiveRows;
        public int ViolationCount => _violationCount;
        public IReadOnlyList<string> Violations => _violations;

        public static async Task<SeatSampler> OpenAsync(string connectionString, string classId, int totalSeats)
        {
            var cs = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = "seat-race-sampler" };
            var conn = new NpgsqlConnection(cs.ConnectionString);
            await conn.OpenAsync();
            var sampler = new SeatSampler(conn, classId, totalSeats);
            await sampler.SampleAsync(); // watching before the first request is sent
            return sampler;
        }

        public async Task LoopAsync(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested) await SampleAsync();
        }

        /// <summary>Sequential use only: the loop and the final sample never overlap.</summary>
        public async Task<Snapshot> SampleAsync()
        {
            await using var reader = await _cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("The class being watched does not exist.");
            var s = new Snapshot(reader.GetInt32(0), reader.GetInt32(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3));
            Interlocked.Increment(ref _samples);

            var taken = s.Held + s.Confirmed;
            _maxSeatsTaken = Math.Max(_maxSeatsTaken, taken);
            _maxActiveRows = Math.Max(_maxActiveRows, s.ActiveRows);
            if (taken > _totalSeats) Flag($"held+confirmed {taken} > total {_totalSeats}");
            if (s.ActiveRows > _totalSeats) Flag($"{s.ActiveRows} active hold rows > total {_totalSeats}");
            // Counters and rows are written in one transaction, so no snapshot may show one without the other.
            if (taken != s.ActiveRows) Flag($"counters say {taken} seats taken but {s.ActiveRows} active hold rows exist");
            return s;
        }

        private void Flag(string what)
        {
            _violationCount++;
            if (_violations.Count < MaxViolationsKept) _violations.Add($"sample {_samples}: {what}");
        }

        public async ValueTask DisposeAsync()
        {
            await _cmd.DisposeAsync();
            await _conn.DisposeAsync();
        }
    }
}
