using Api.Dtos;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>Liveness/readiness probe. Not part of the frozen student-facing contract; takes no X-Student-Id.</summary>
[ApiController]
[Route("health")]
public class HealthController(IHealthService health) : ControllerBase
{
    /// <summary>Check that the API and its database are up.</summary>
    /// <remarks>An operational probe outside the student-facing contract; it takes no X-Student-Id.</remarks>
    /// <response code="200">The API is running and the database answered.</response>
    /// <response code="503">DATABASE_UNAVAILABLE: the database did not answer within 3 seconds.</response>
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        await health.IsDatabaseUpAsync(ct)
            ? Ok(new HealthResponse("ok", "up"))
            : StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ErrorResponse("DATABASE_UNAVAILABLE", "The database is not reachable."));
}
