using Api.Dtos;

namespace Api.Services;

public enum CreateHoldOutcome
{
    Created,
    ClassNotFound,
    NoSeatAvailable,
    DuplicateActiveHold
}

public record CreateHoldResult(CreateHoldOutcome Outcome, HoldResponse? Hold = null);

public interface IHoldService
{
    Task<CreateHoldResult> CreateAsync(string classId, string studentId, CancellationToken ct);
}
