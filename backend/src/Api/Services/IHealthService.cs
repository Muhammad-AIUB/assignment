namespace Api.Services;

public interface IHealthService
{
    /// <summary>True when the database answers a trivial query within a few seconds. Never throws for a DB failure.</summary>
    Task<bool> IsDatabaseUpAsync(CancellationToken ct);
}
