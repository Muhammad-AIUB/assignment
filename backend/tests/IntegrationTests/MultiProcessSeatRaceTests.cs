using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTests;

/// <summary>
/// The same seat race, but against two separate API processes (different ports, one shared database), with the requests split
/// between them. Seat safety lives in the database, not in any one server's memory, so it has to hold when two servers admit
/// students at the same time. This is what a real deployment with more than one API instance does.
/// </summary>
// Runs inside the "api" collection only so it never overlaps the other integration tests (they would compete for the CPU
// and the wall-clock tests are timing-sensitive). It does not use that collection's ApiFactory or its database.
[Collection("api")]
public class MultiProcessSeatRaceTests(TwoApiInstancesFixture fx, ITestOutputHelper output) : IClassFixture<TwoApiInstancesFixture>
{
    private HttpClient[] Instances => [fx.A.Client, fx.B.Client];

    [Fact]
    public async Task The_two_instances_are_separate_live_processes_on_different_ports_sharing_one_database()
    {
        output.WriteLine($"A: pid {fx.A.Pid}, port {fx.A.Port}; B: pid {fx.B.Pid}, port {fx.B.Port}; test host: pid {Environment.ProcessId}");

        Assert.NotEqual(fx.A.Pid, fx.B.Pid);
        Assert.NotEqual(fx.A.Port, fx.B.Port);
        Assert.NotEqual(Environment.ProcessId, fx.A.Pid);
        Assert.NotEqual(Environment.ProcessId, fx.B.Pid);
        Assert.False(fx.A.HasExited);
        Assert.False(fx.B.HasExited);

        foreach (var instance in Instances)
            Assert.Equal(HttpStatusCode.OK, (await instance.GetAsync("/health")).StatusCode);

        // Written through one, read through the other: the same database behind both.
        var classId = await SeatRace.CreateClassAsync(fx.A.Client, 7);
        var seenByB = await fx.B.Client.GetAsync($"/classes/{classId}");
        Assert.Equal(HttpStatusCode.OK, seenByB.StatusCode);
        Assert.Equal(7, (await seenByB.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalSeats").GetInt32());
    }

    [Fact]
    public async Task Parallel_holds_split_across_two_processes_yield_exactly_totalSeats_201_and_the_rest_409()
    {
        const int seats = 5, requests = 50;
        var classId = await SeatRace.CreateClassAsync(fx.A.Client, seats);

        // Request i goes to instance i % 2, so 25 requests hit each process in the same instant.
        var result = await SeatRace.RunAsync(fx.ConnectionString, classId, seats, requests, Instances);

        SeatRace.AssertNeverOversold(result, output);
        Assert.Equal(requests / 2, result.Attempts.Count(a => a.Target == 0));
        Assert.Equal(requests / 2, result.Attempts.Count(a => a.Target == 1));

        // Every winner's hold was written by one process and is visible through the other.
        foreach (var win in result.Attempts.Where(a => a.Status == HttpStatusCode.Created))
        {
            var otherInstance = Instances[1 - win.Target];
            var res = await otherInstance.GetAsync($"/holds/{win.HoldId}");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("HELD", body.GetProperty("status").GetString());
            Assert.Equal(classId, body.GetProperty("classId").GetString());
        }
    }

    [Fact]
    public async Task The_same_two_process_race_repeated_on_fresh_classes_never_oversells_even_once()
    {
        const int rounds = 10, seats = 5, requests = 50;

        for (var round = 1; round <= rounds; round++)
        {
            var classId = await SeatRace.CreateClassAsync(Instances[round % 2], seats);
            var result = await SeatRace.RunAsync(fx.ConnectionString, classId, seats, requests, Instances);
            output.WriteLine($"round {round}/{rounds}");
            SeatRace.AssertNeverOversold(result, output);
        }
    }

    [Fact]
    public async Task The_last_seat_raced_for_across_two_processes_goes_to_exactly_one_student()
    {
        const int seats = 1, requests = 40;
        var classId = await SeatRace.CreateClassAsync(fx.B.Client, seats);

        var result = await SeatRace.RunAsync(fx.ConnectionString, classId, seats, requests, Instances);

        SeatRace.AssertNeverOversold(result, output);
    }
}
