using Api.Dtos;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("classes")]
public class ClassesController(IClassService classes) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ClassResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateClassRequest request, CancellationToken ct)
    {
        var created = await classes.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { classId = created.Id }, created);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ClassSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await classes.ListAsync(ct));

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
