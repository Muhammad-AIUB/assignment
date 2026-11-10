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

/// <summary>Result of confirming or cancelling a hold. Anything but <see cref="Succeeded"/> maps to 404/409.</summary>
public enum HoldActionOutcome
{
    Succeeded,
    NotFound,
    NotOwner,
    Expired,
    AlreadyCancelled,
    AlreadyConfirmed
}

public interface IHoldService
{
    Task<CreateHoldResult> CreateAsync(string classId, string studentId, CancellationToken ct);

    /// <summary>Null when the hold does not exist. Status reflects expiry even if no job has swept the row.</summary>
    Task<HoldStatusResponse?> GetAsync(string holdId, CancellationToken ct);

    /// <summary>Owner only, HELD and unexpired. Re-confirming an already CONFIRMED hold is a success (idempotent).</summary>
    Task<HoldActionOutcome> ConfirmAsync(string holdId, string studentId, CancellationToken ct);

    /// <summary>Owner only, HELD and unexpired. Releases the seat immediately.</summary>
    Task<HoldActionOutcome> CancelAsync(string holdId, string studentId, CancellationToken ct);

    /// <summary>All of the student's holds (any status), newest first.</summary>
    Task<IReadOnlyList<MyHoldResponse>> ListForStudentAsync(string studentId, CancellationToken ct);
}
