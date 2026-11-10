using Api.Dtos;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("classes")]
public class ClassesController(IClassService classes) : ControllerBase
{
    /// <summary>Create a class with a fixed number of seats.</summary>
    /// <param name="request">The class name (1 to 200 characters) and its total seats (a positive whole number).</param>
    /// <response code="201">The class was created.</response>
    /// <response code="400">VALIDATION_ERROR: the name is blank or longer than 200 characters, totalSeats is not a positive whole number, or the body is missing or not valid JSON.</response>
    [HttpPost]
    [ProducesResponseType<ClassResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateClassRequest request, CancellationToken ct)
    {
        var created = await classes.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { classId = created.Id }, created);
    }

    /// <summary>List every class with its live seat counts.</summary>
    /// <remarks>
    /// availableSeats = totalSeats - heldSeats - confirmedSeats. A hold past its expiry is not counted as held, even
    /// before the background sweep has marked it EXPIRED.
    /// </remarks>
    /// <response code="200">All classes, each with heldSeats, confirmedSeats and availableSeats.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ClassSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await classes.ListAsync(ct));

    /// <summary>Get one class with its live seat counts.</summary>
    /// <param name="classId">A class id as returned by POST /classes.</param>
    /// <response code="200">The class, in the same shape as one item of GET /classes.</response>
    /// <response code="404">CLASS_NOT_FOUND: no class has this id.</response>
    [HttpGet("{classId}")]
    [ProducesResponseType<ClassSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string classId, CancellationToken ct)
    {
        var result = await classes.GetAsync(classId, ct);
        return result is null
            ? NotFound(new ErrorResponse("CLASS_NOT_FOUND", $"Class '{classId}' was not found."))
            : Ok(result);
    }
}
