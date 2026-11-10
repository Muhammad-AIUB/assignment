using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Api.Dtos;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
public partial class HoldsController(IHoldService holds) : ControllerBase
{
    [GeneratedRegex(@"^[0-9]{17}$")]
    private static partial Regex StudentIdPattern();

    [HttpPost("classes/{classId}/holds")]
    [ProducesResponseType<HoldResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        string classId,
        [FromHeader(Name = "X-Student-Id")] string? studentId,
        CancellationToken ct)
    {
        if (!IsValidStudentId(studentId)) return InvalidStudentId();

        var result = await holds.CreateAsync(classId, studentId, ct);
        return result.Outcome switch
        {
            CreateHoldOutcome.Created => StatusCode(StatusCodes.Status201Created, result.Hold),
            CreateHoldOutcome.ClassNotFound => NotFound(new ErrorResponse("CLASS_NOT_FOUND", $"Class '{classId}' was not found.")),
            CreateHoldOutcome.NoSeatAvailable => Conflict(new ErrorResponse("NO_SEAT_AVAILABLE", "No seats are available in this class.")),
            CreateHoldOutcome.DuplicateActiveHold => Conflict(new ErrorResponse("DUPLICATE_ACTIVE_HOLD", "Student already has an active hold or confirmed admission in this class.")),
            _ => throw new InvalidOperationException("Unhandled outcome.")
        };
    }

    [HttpGet("holds/{holdId}")]
    [ProducesResponseType<HoldStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string holdId, CancellationToken ct)
    {
        var hold = await holds.GetAsync(holdId, ct);
        return hold is null ? HoldNotFound(holdId) : Ok(hold);
    }

    [HttpPost("holds/{holdId}/confirm")]
    [ProducesResponseType<ConfirmHoldResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(
        string holdId,
        [FromHeader(Name = "X-Student-Id")] string? studentId,
        CancellationToken ct)
    {
        if (!IsValidStudentId(studentId)) return InvalidStudentId();

        var outcome = await holds.ConfirmAsync(holdId, studentId, ct);
        return outcome == HoldActionOutcome.Succeeded
            ? Ok(new ConfirmHoldResponse(holdId, "CONFIRMED"))
            : HoldActionFailure(outcome, holdId);
    }

    [HttpDelete("holds/{holdId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        string holdId,
        [FromHeader(Name = "X-Student-Id")] string? studentId,
        CancellationToken ct)
    {
        if (!IsValidStudentId(studentId)) return InvalidStudentId();

        var outcome = await holds.CancelAsync(holdId, studentId, ct);
        return outcome == HoldActionOutcome.Succeeded ? NoContent() : HoldActionFailure(outcome, holdId);
    }

    [HttpGet("me/holds")]
    [ProducesResponseType<IReadOnlyList<MyHoldResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListMine(
        [FromHeader(Name = "X-Student-Id")] string? studentId,
        CancellationToken ct)
    {
        if (!IsValidStudentId(studentId)) return InvalidStudentId();

        return Ok(await holds.ListForStudentAsync(studentId, ct));
    }

    private static bool IsValidStudentId([NotNullWhen(true)] string? studentId) =>
        studentId is not null && StudentIdPattern().IsMatch(studentId);

    private BadRequestObjectResult InvalidStudentId() =>
        BadRequest(new ErrorResponse("INVALID_STUDENT_ID",
            "X-Student-Id header is required and must be exactly 17 digits."));

    private NotFoundObjectResult HoldNotFound(string holdId) =>
        NotFound(new ErrorResponse("HOLD_NOT_FOUND", $"Hold '{holdId}' was not found."));

    private IActionResult HoldActionFailure(HoldActionOutcome outcome, string holdId) => outcome switch
    {
        HoldActionOutcome.NotFound => HoldNotFound(holdId),
        HoldActionOutcome.NotOwner => Conflict(new ErrorResponse("NOT_HOLD_OWNER", "This hold belongs to another student.")),
        HoldActionOutcome.Expired => Conflict(new ErrorResponse("HOLD_EXPIRED", "This hold has expired.")),
        HoldActionOutcome.AlreadyCancelled => Conflict(new ErrorResponse("HOLD_CANCELLED", "This hold has been cancelled.")),
        HoldActionOutcome.AlreadyConfirmed => Conflict(new ErrorResponse("HOLD_ALREADY_CONFIRMED", "This hold has already been confirmed.")),
        _ => throw new InvalidOperationException("Unhandled outcome.")
    };
}
