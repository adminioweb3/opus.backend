using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Opus.Application.Features.Metrics;

namespace Opus.API.Controllers;

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

    [HttpPost("run-scan")]
    public async Task<IActionResult> RunScan([FromQuery] Guid organizationId)
    {
        var command = new RunScanCommand
        {
            OrganizationId = organizationId == Guid.Empty ? Guid.NewGuid() : organizationId
        };

        var result = await _mediator.Send(command);
        return Ok(new { success = result });
    }
}
