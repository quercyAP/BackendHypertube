using Microsoft.AspNetCore.Mvc;

namespace Hypertube.WebAPI.Controllers;

/// <summary>
/// Health check endpoint for monitoring and Docker healthchecks
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class HealthController : ControllerBase
{

    /// <summary>
    /// Get API health status
    /// </summary>
    /// <returns>Health status with timestamp</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "healthy",
            service = "Hypertube API",
            timestamp = DateTime.UtcNow,
            version = "1.0.0",
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
        });
    }
}
