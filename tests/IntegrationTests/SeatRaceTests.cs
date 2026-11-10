using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTests;

/// <summary>
/// The concurrency requirement: far more parallel hold requests than seats, against the running API on a real Postgres
/// (Testcontainers). Exactly <c>totalSeats</c> requests may win a 201, every other request must get a 409, and the database
/// must never show more seats taken than exist, not just at the end but while the requests are in flight.
/// </summary>
[Collection("api")]
public class SeatRaceTests(ApiFactory factory, ITestOutputHelper output)
{
    [Theory]
    [InlineData(5, 50)] // the assignment's scenario: 10x more requests than seats
    [InlineData(1, 30)] // the last seat: every request fights over the same single seat
    public async Task Parallel_holds_beyond_capacity_yield_exactly_totalSeats_201_and_the_rest_409(int seats, int requests)
    {
        var client = factory.CreateClient();
        var classId = await SeatRace.CreateClassAsync(client, seats);

        var result = await SeatRace.RunAsync(factory.ConnectionString, classId, seats, requests, [client]);

        SeatRace.AssertNeverOversold(result, output);
    }

    /// <summary>A race that is won by luck once is not evidence; this one runs the same scenario on fresh classes again and again.</summary>
    [Fact]
    public async Task The_same_race_repeated_on_fresh_classes_never_oversells_even_once()
    {
        const int rounds = 15, seats = 5, requests = 50;
        var client = factory.CreateClient();

        for (var round = 1; round <= rounds; round++)
        {
            var classId = await SeatRace.CreateClassAsync(client, seats);
            var result = await SeatRace.RunAsync(factory.ConnectionString, classId, seats, requests, [client]);
            output.WriteLine($"round {round}/{rounds}");
            SeatRace.AssertNeverOversold(result, output);
        }
    }

    /// <summary>
    /// Sampling can only see the instants it happens to hit. What makes "never more than totalSeats at any point" hold for
    /// every instant is that the database itself refuses the state, whatever the application code does. Prove the refusal.
    /// </summary>
    [Fact]
    public async Task The_database_itself_refuses_a_state_with_more_seats_taken_than_exist()
    {
        var classId = await SeatRace.CreateClassAsync(factory.CreateClient(), 3);

        async Task<PostgresException> SetCountersAsync(int held, int confirmed)
        {
            await using var conn = new NpgsqlConnection(factory.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE classes SET \"HeldSeats\" = @h, \"ConfirmedSeats\" = @c WHERE \"Id\" = @id", conn);
            cmd.Parameters.AddWithValue("h", held);
            cmd.Parameters.AddWithValue("c", confirmed);
            cmd.Parameters.AddWithValue("id", classId);
            return await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        }

        foreach (var (held, confirmed) in new[] { (4, 0), (0, 4), (2, 2), (3, 1) })
        {
            var ex = await SetCountersAsync(held, confirmed);
            Assert.Equal("23514", ex.SqlState); // check_violation
            Assert.Equal("ck_classes_seats_not_oversold", ex.ConstraintName);
        }
    }
}
