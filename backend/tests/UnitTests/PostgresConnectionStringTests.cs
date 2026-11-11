using Infrastructure.Persistence;

namespace UnitTests;

/// <summary>
/// Npgsql's own default is 100 pooled connections per process, which is also Postgres' default <c>max_connections</c>: one busy
/// instance can hold every connection the server has and starve the others (Session 22, finding 1). The API therefore applies
/// its own cap unless the connection string already sets one.
/// </summary>
public class PostgresConnectionStringTests
{
    private const string Plain = "Host=db;Port=5432;Database=school;Username=app;Password=secret";

    [Fact]
    public void A_connection_string_with_no_pool_size_gets_the_default_cap()
    {
        var cs = PostgresConnectionString.WithDefaultPoolLimit(Plain);

        Assert.Equal(PostgresConnectionString.DefaultMaxPoolSize, cs.MaxPoolSize);
        Assert.Contains("Maximum Pool Size=30", cs.ConnectionString);
    }

    [Fact]
    public void The_default_cap_leaves_room_for_three_instances_against_postgres_default_max_connections()
    {
        // max_connections = 100, 3 of them reserved for superusers: ordinary clients share 97.
        Assert.True(3 * PostgresConnectionString.DefaultMaxPoolSize <= 97,
            $"3 instances x {PostgresConnectionString.DefaultMaxPoolSize} connections must fit in 97");
    }

    [Theory]
    [InlineData("Maximum Pool Size=10", 10)]
    [InlineData("MaxPoolSize=7", 7)]            // Npgsql's alias for the same setting
    [InlineData("maximum pool size=200", 200)]  // keywords are case-insensitive, and an operator may raise it deliberately
    [InlineData("Maximum Pool Size=100", 100)]  // explicitly asking for Npgsql's own default is still an explicit choice
    public void A_pool_size_set_in_the_connection_string_is_kept(string setting, int expected)
    {
        var cs = PostgresConnectionString.WithDefaultPoolLimit($"{Plain};{setting}");

        Assert.Equal(expected, cs.MaxPoolSize);
    }

    [Fact]
    public void Everything_else_in_the_connection_string_is_left_alone()
    {
        var cs = PostgresConnectionString.WithDefaultPoolLimit(Plain + ";Application Name=api-A;Timeout=7");

        Assert.Equal("db", cs.Host);
        Assert.Equal(5432, cs.Port);
        Assert.Equal("school", cs.Database);
        Assert.Equal("app", cs.Username);
        Assert.Equal("secret", cs.Password);
        Assert.Equal("api-A", cs.ApplicationName);
        Assert.Equal(7, cs.Timeout);
    }

    [Fact]
    public void A_minimum_pool_size_above_the_default_cap_is_not_turned_into_an_invalid_setting()
    {
        // Min 50 / Max 30 would be rejected by Npgsql, so a string that was valid before must not start failing.
        var cs = PostgresConnectionString.WithDefaultPoolLimit($"{Plain};Minimum Pool Size=50");

        Assert.True(cs.MaxPoolSize >= cs.MinPoolSize);
        Assert.Equal(50, cs.MinPoolSize);
    }

    [Fact]
    public void A_connection_string_that_is_not_one_is_refused_here_not_at_the_first_query()
    {
        Assert.Throws<ArgumentException>(() => PostgresConnectionString.WithDefaultPoolLimit("this is not a connection string"));
    }
}
