using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTests;

/// <summary>
/// Found by the Session 22 dual-instance run: after a burst of ~100 simultaneous requests instance A kept 98 idle pooled
/// connections (Npgsql's default <c>Maximum Pool Size</c> is 100 per process and the connection string set none), and
/// Postgres' default <c>max_connections</c> is 100. Instance B then had two connections: in the next race 23 of its 25
/// requests died with <c>53300 too many clients already</c> and the student saw 400 UNEXPECTED_ERROR. Seats were never
/// oversold (that lives in the database), so this is a capacity gap, not a logic bug, and it is silent to the student.
/// Two real API processes share one real Postgres here, exactly as in <see cref="MultiProcessSeatRaceTests"/>, with no
/// pool setting in the connection string, so this exercises whatever the API defaults to.
/// </summary>
[Collection("api")]
public class ConnectionPoolCapacityTests(TwoApiInstancesFixture fx, ITestOutputHelper output) : IClassFixture<TwoApiInstancesFixture>
{
    // Postgres' default max_connections is 100 and it keeps 3 of them for superusers, so ordinary clients share 97.
    private const int ConnectionsPostgresAllows = 97;
    // The requirement: one instance, however hard it is hit, must leave room for the others. Three of them.
    private const int InstancesToLeaveRoomFor = 3;

    [Fact]
    public async Task A_burst_on_one_instance_leaves_the_database_room_for_the_other_instances()
    {
        // 150 simultaneous hold requests on A alone: more than the 100 connections Postgres allows in total. Each request that
        // is waiting for the class row also holds a connection, so the pool grows as far as it is allowed to.
        var burstClass = await SeatRace.CreateClassAsync(fx.A.Client, 5);
        var burst = await SeatRace.RunAsync(fx.ConnectionString, burstClass, 5, 150, [fx.A.Client]);
        SeatRace.AssertNeverOversold(burst, output);

        // The burst is over. Npgsql keeps the connections it opened (idle, for minutes) so look at what A is still holding.
        var heldByA = await CountConnectionsAsync(TwoApiInstancesFixture.ApplicationName("A"));
        var limit = ConnectionsPostgresAllows / InstancesToLeaveRoomFor;
        output.WriteLine($"After the burst instance A holds {heldByA} connections (it may hold at most {limit}, so {InstancesToLeaveRoomFor} instances fit in {ConnectionsPostgresAllows}).");
        Assert.True(heldByA <= limit,
            $"instance A holds {heldByA} idle connections after a burst; at most {limit} leaves room for {InstancesToLeaveRoomFor} instances against Postgres' {ConnectionsPostgresAllows}.");

        // And what that protects: B still serves a full race at the same moment as A, every answer a 201 or a 409.
        var classId = await SeatRace.CreateClassAsync(fx.B.Client, 5);
        var race = await SeatRace.RunAsync(fx.ConnectionString, classId, 5, 50, [fx.A.Client, fx.B.Client]);
        SeatRace.AssertNeverOversold(race, output);
        Assert.Equal(25, race.Attempts.Count(a => a.Target == 1));
    }

    private async Task<int> CountConnectionsAsync(string applicationName)
    {
        var cs = new NpgsqlConnectionStringBuilder(fx.ConnectionString) { ApplicationName = "pool-capacity-monitor", Pooling = false };
        await using var conn = new NpgsqlConnection(cs.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE application_name = @n", conn);
        cmd.Parameters.AddWithValue("n", applicationName);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }
}
