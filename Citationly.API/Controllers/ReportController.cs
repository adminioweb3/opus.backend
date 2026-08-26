using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.Report;
using Citationly.API.Services;

namespace Citationly.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[Authorize]
public class ReportController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentOrganizationAccessor _currentOrganization;

    public ReportController(IMediator mediator, ICurrentOrganizationAccessor currentOrganization)
    {
        _mediator = mediator;
        _currentOrganization = currentOrganization;
    }

    [HttpGet]
    [AuditAction("report.read", "DataExport", "Report")]
    public async Task<IActionResult> GetFullReport()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized();

        var query = new GetFullReportQuery { OrganizationId = organizationId.Value };
        var result = await _mediator.Send(query);

        if (!result.Success)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Data);
    }
}
