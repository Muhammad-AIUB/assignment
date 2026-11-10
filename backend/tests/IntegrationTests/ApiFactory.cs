using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16").Build();

    public string ConnectionString => _pg.GetConnectionString();

    public async Task InitializeAsync() => await _pg.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _pg.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _pg.GetConnectionString());

        // The background expiry sweep stays off here so tests that deliberately leave expired HELD rows
        // unswept (to prove lazy expiry) are not changed under them. The sweeper tests call the sweeper
        // directly, and the one worker test switches the worker on for its own host.
        builder.UseSetting("HOLD_EXPIRY_SWEEP_ENABLED", "false");
    }

    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;
