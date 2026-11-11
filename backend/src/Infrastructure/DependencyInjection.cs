using Infrastructure.Options;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Postgres is not configured. Set it in appsettings or the ConnectionStrings__Postgres environment variable.");
        }

        // Applied here, not only in docker-compose.yml, so the cap holds however the API is started (dotnet run, a test host,
        // another orchestrator). An explicit "Maximum Pool Size" in the string still wins. See PostgresConnectionString.
        connectionString = PostgresConnectionString.WithDefaultPoolLimit(connectionString).ConnectionString;

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

        services.AddOptions<HoldOptions>()
            .Configure(o => o.DurationSeconds =
                configuration.GetValue(HoldOptions.ConfigKey, HoldOptions.DefaultDurationSeconds))
            .Validate(o => o.DurationSeconds > 0, $"{HoldOptions.ConfigKey} must be greater than 0")
            .ValidateOnStart();

        services.AddOptions<HoldExpirySweepOptions>()
            .Configure(o =>
            {
                o.Enabled = configuration.GetValue(HoldExpirySweepOptions.EnabledConfigKey, true);
                o.IntervalSeconds = configuration.GetValue(
                    HoldExpirySweepOptions.IntervalConfigKey, HoldExpirySweepOptions.DefaultIntervalSeconds);
            })
            .Validate(o => o.IntervalSeconds > 0, $"{HoldExpirySweepOptions.IntervalConfigKey} must be greater than 0")
            .ValidateOnStart();

        return services;
    }
}
