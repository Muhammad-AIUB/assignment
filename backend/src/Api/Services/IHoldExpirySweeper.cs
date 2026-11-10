namespace Api.Services;

public interface IHoldExpirySweeper
{
    /// <summary>Marks every expired HELD hold as EXPIRED and releases its seat. Returns how many holds it expired.</summary>
    Task<int> SweepAsync(CancellationToken ct);
}
