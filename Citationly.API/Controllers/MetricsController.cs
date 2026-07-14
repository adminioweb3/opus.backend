using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Citationly.Application.Features.Metrics;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MetricsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("daily")]
    public async Task<IActionResult> GetDailyMetrics([FromQuery] Guid organizationId)
    {
        var query = new GetDailyMetricsQuery
        {
            OrganizationId = organizationId == Guid.Empty ? Guid.NewGuid() : organizationId
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("executive")]
    public async Task<IActionResult> GetExecutiveMetrics([FromQuery] Guid organizationId)
    {
        var query = new GetExecutiveMetricsQuery
        {
            OrganizationId = organizationId == Guid.Empty ? Guid.NewGuid() : organizationId
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("run-scan")]
    public async Task<IActionResult> RunScan([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty)
            return BadRequest(new { success = false, message = "OrganizationId is required." });

        try
        {
            var result = await _mediator.Send(new RunScanCommand { OrganizationId = organizationId });
            if (!result.Success) return BadRequest(new { success = false, message = result.Message });
            return Ok(new { success = true, message = result.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GEO scan failed for org {organizationId}: {ex}");
            return StatusCode(500, new { success = false, message = "GEO scan failed. Please try again." });
        }
    }
}
