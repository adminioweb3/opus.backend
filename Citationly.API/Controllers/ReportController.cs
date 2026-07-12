using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.Report;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReportController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{organizationId}")]
    public async Task<IActionResult> GetFullReport(Guid organizationId)
    {
        var query = new GetFullReportQuery { OrganizationId = organizationId };
        var result = await _mediator.Send(query);

        if (!result.Success)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Data);
    }
}
