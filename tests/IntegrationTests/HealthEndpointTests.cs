using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

/// <summary>GET /health: 200 when the database answers, 503 (standard error shape) when it does not.</summary>
[Collection("api")]
public class HealthEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_returns_200_when_the_database_is_reachable()
    {
        // No X-Student-Id: the probe is not student-scoped.
        var res = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal("up", body.GetProperty("database").GetString());
    }

    [Fact]
    public async Task Health_returns_503_with_the_standard_error_shape_when_the_database_is_down()
    {
        // A real, separate Postgres that we stop mid-test, so the shared container used by other tests is untouched.
        var pg = new PostgreSqlBuilder("postgres:16").Build();
        await pg.StartAsync();
        try
        {
            await using var app = factory.WithWebHostBuilder(b =>
                b.UseSetting("ConnectionStrings:Postgres", pg.GetConnectionString()));
            var client = app.CreateClient();

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

            await pg.StopAsync();
            var clock = Stopwatch.StartNew();
            var res = await client.GetAsync("/health");
            clock.Stop();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("DATABASE_UNAVAILABLE", body.GetProperty("error").GetString());
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10),
                $"the probe must fail fast, but took {clock.Elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            await pg.DisposeAsync();
        }
    }
}
