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

    /// <summary>Hold one seat in a class for the calling student.</summary>
    /// <remarks>
    /// First come, first served. The seat is claimed atomically, so a class is never oversold however many students ask
    /// at the same moment. The hold lasts HOLD_DURATION_SECONDS (default 600): confirm it before expiresAt or the seat
    /// is released for the next student.
    /// </remarks>
    /// <param name="classId">The class to hold a seat in.</param>
    /// <param name="studentId">The student's 17-digit registration number.</param>
    /// <response code="201">The seat is held until expiresAt.</response>
    /// <response code="400">INVALID_STUDENT_ID: X-Student-Id is missing or is not exactly 17 digits.</response>
    /// <response code="404">CLASS_NOT_FOUND: no class has this id.</response>
    /// <response code="409">NO_SEAT_AVAILABLE: every seat is held or confirmed. DUPLICATE_ACTIVE_HOLD: the student already has an active hold or a confirmed admission in this class.</response>
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

    /// <summary>Get a hold and its current status.</summary>
    /// <remarks>
    /// status is HELD, CONFIRMED, EXPIRED or CANCELLED. A HELD hold past expiresAt is reported as EXPIRED straight
    /// away, without waiting for the background sweep. No X-Student-Id is needed.
    /// </remarks>
    /// <param name="holdId">A hold id as returned by POST /classes/{classId}/holds.</param>
    /// <response code="200">The hold's class, status and expiry.</response>
    /// <response code="404">HOLD_NOT_FOUND: no hold has this id.</response>
    [HttpGet("holds/{holdId}")]
    [ProducesResponseType<HoldStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string holdId, CancellationToken ct)
    {
        var hold = await holds.GetAsync(holdId, ct);
        return hold is null ? HoldNotFound(holdId) : Ok(hold);
    }

    /// <summary>Confirm a hold, turning the held seat into a confirmed admission.</summary>
    /// <remarks>
    /// Idempotent: confirming a hold its owner has already confirmed returns 200 again and does not count the seat a
    /// second time.
    /// </remarks>
    /// <param name="holdId">The hold to confirm.</param>
    /// <param name="studentId">The student's 17-digit registration number; must be the hold's owner.</param>
    /// <response code="200">The hold is CONFIRMED (this includes a repeat confirm by the owner).</response>
    /// <response code="400">INVALID_STUDENT_ID: X-Student-Id is missing or is not exactly 17 digits.</response>
    /// <response code="404">HOLD_NOT_FOUND: no hold has this id.</response>
    /// <response code="409">NOT_HOLD_OWNER: the hold belongs to another student. HOLD_EXPIRED: it expired before it was confirmed. HOLD_CANCELLED: it was cancelled.</response>
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

    /// <summary>Cancel a hold and release its seat immediately.</summary>
    /// <remarks>Only the owner can cancel, and only a hold that is still HELD (not expired, confirmed or already cancelled).</remarks>
    /// <param name="holdId">The hold to cancel.</param>
    /// <param name="studentId">The student's 17-digit registration number; must be the hold's owner.</param>
    /// <response code="204">The hold is CANCELLED and the seat is available to other students again.</response>
    /// <response code="400">INVALID_STUDENT_ID: X-Student-Id is missing or is not exactly 17 digits.</response>
    /// <response code="404">HOLD_NOT_FOUND: no hold has this id.</response>
    /// <response code="409">NOT_HOLD_OWNER: the hold belongs to another student. HOLD_EXPIRED, HOLD_CANCELLED or HOLD_ALREADY_CONFIRMED: the hold is no longer HELD.</response>
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

    /// <summary>List the calling student's holds, newest first.</summary>
    /// <remarks>Holds of every status (HELD, CONFIRMED, EXPIRED, CANCELLED) are included. A student with no holds gets an empty list.</remarks>
    /// <param name="studentId">The student's 17-digit registration number.</param>
    /// <response code="200">The student's holds, each with its class name, status and expiry.</response>
    /// <response code="400">INVALID_STUDENT_ID: X-Student-Id is missing or is not exactly 17 digits.</response>
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
