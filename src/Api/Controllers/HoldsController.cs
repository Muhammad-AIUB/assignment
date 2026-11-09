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
        if (studentId is null || !StudentIdPattern().IsMatch(studentId))
        {
            return BadRequest(new ErrorResponse("INVALID_STUDENT_ID",
                "X-Student-Id header is required and must be exactly 17 digits."));
        }

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
}
