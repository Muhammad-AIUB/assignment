using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public class HealthService(AppDbContext db, ILogger<HealthService> logger) : IHealthService
{
    // A probe that hangs is no use to a load balancer; fail fast instead.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<bool> IsDatabaseUpAsync(CancellationToken ct)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(Timeout);

        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", bounded.Token);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller hung up; that says nothing about the database
        }
        catch (Exception ex)
        {
            // Includes our own timeout. The detail goes to the log, not to the client.
            logger.LogWarning(ex, "Database health check failed");
            return false;
        }
    }
}
