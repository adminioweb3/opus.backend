using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Citationly.Application.Features.Metrics;
using Citationly.API.Services;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentOrganizationAccessor _currentOrganization;

    public MetricsController(IMediator mediator, ICurrentOrganizationAccessor currentOrganization)
    {
        _mediator = mediator;
        _currentOrganization = currentOrganization;
    }

    [HttpGet("daily")]
    public async Task<IActionResult> GetDailyMetrics()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized();

        var query = new GetDailyMetricsQuery
        {
            OrganizationId = organizationId.Value
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("executive")]
    public async Task<IActionResult> GetExecutiveMetrics()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized();

        var query = new GetExecutiveMetricsQuery
        {
            OrganizationId = organizationId.Value
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpPost("run-scan")]
    public async Task<IActionResult> RunScan()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized();

        try
        {
            var result = await _mediator.Send(new RunScanCommand { OrganizationId = organizationId.Value });
            if (!result.Success) return BadRequest(new { success = false, message = result.Message });
            return Ok(new { success = true, message = result.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GEO scan failed for authenticated organization {organizationId}: {ex}");
            return StatusCode(500, new { success = false, message = "GEO scan failed. Please try again." });
        }
    }
}
