using Api.Services;
using Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Api.BackgroundServices;

/// <summary>
/// Runs <see cref="IHoldExpirySweeper"/> every few seconds (default 5, <c>HOLD_EXPIRY_SWEEP_INTERVAL_SECONDS</c>)
/// so expired holds get marked EXPIRED and their seats released in the stored data.
///
/// <b>This is for data cleanliness only. Nothing's correctness depends on it.</b> Whether a hold is expired,
/// and whether a seat is free, comes from the computed logic of Sessions 2-4 (<c>Hold.EffectiveStatus</c>,
/// the expired-excluding counts in GET /classes, and the cleanup inside the seat claim), all of which give
/// the right answer when this worker is slow, failing, or switched off (<c>HOLD_EXPIRY_SWEEP_ENABLED=false</c>).
/// That is why a failed sweep is logged and retried on the next tick rather than allowed to stop the host.
/// </summary>
public class HoldExpiryWorker(
    IServiceScopeFactory scopes,
    IOptions<HoldExpirySweepOptions> options,
    ILogger<HoldExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Hold expiry sweep is disabled ({Key}=false)", HoldExpirySweepOptions.EnabledConfigKey);
            return;
        }

        logger.LogInformation("Hold expiry sweep running every {Interval}", settings.Interval);

        using var timer = new PeriodicTimer(settings.Interval);
        try
        {
            // Sweep straight away (cleans up whatever expired while the app was down), then once per tick.
            do
            {
                await SweepOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        try
        {
            // The sweeper is scoped (it owns a DbContext), so each tick gets its own scope.
            await using var scope = scopes.CreateAsyncScope();
            var expired = await scope.ServiceProvider.GetRequiredService<IHoldExpirySweeper>().SweepAsync(ct);
            if (expired > 0)
                logger.LogInformation("Hold expiry sweep marked {Count} hold(s) EXPIRED", expired);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hold expiry sweep failed; will retry in {Interval}", options.Value.Interval);
        }
    }
}
