using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.API.Services;
using Citationly.Application.Features.Websites;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WebsitesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentOrganizationAccessor _currentOrg;

    public WebsitesController(IMediator mediator, ICurrentOrganizationAccessor currentOrg)
    {
        _mediator = mediator;
        _currentOrg = currentOrg;
    }

    [HttpGet]
    public async Task<IActionResult> GetWebsites()
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new GetWebsitesQuery { OrganizationId = orgId.Value });
        return Ok(result);
    }

    [HttpPost("connect")]
    public async Task<IActionResult> ConnectWebsite([FromBody] ConnectWebsiteRequest request)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var command = new ConnectWebsiteCommand
        {
            OrganizationId = orgId.Value,
            DomainUrl = request.DomainUrl,
            PlatformName = request.PlatformName
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

}

public class ConnectWebsiteRequest
{
    public string DomainUrl { get; set; } = string.Empty;
    public string PlatformName { get; set; } = "Custom";
}
