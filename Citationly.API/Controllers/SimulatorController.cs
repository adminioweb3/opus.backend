using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Citationly.API.Services;
using Citationly.Application.Features.Simulators;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SimulatorController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentOrganizationAccessor _currentOrganization;

    public SimulatorController(IMediator mediator, ICurrentOrganizationAccessor currentOrganization)
    {
        _mediator = mediator;
        _currentOrganization = currentOrganization;
    }

    [HttpPost("search")]
    public async Task<IActionResult> SearchSimilar([FromBody] SimulatorSearchRequest request)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized();

        var query = new SearchSimilarQuery
        {
            OrganizationId = organizationId.Value,
            QueryText = request.QueryText,
            TopK = request.TopK
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }
}

public class SimulatorSearchRequest
{
    public string QueryText { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
}
