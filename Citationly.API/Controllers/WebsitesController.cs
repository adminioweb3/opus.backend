using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.Websites;
using Citationly.Application.Interfaces;
using System.Security.Claims;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WebsitesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IUserRepository _userRepository;

    public WebsitesController(IMediator mediator, IUserRepository userRepository)
    {
        _mediator = mediator;
        _userRepository = userRepository;
    }

    private async Task<Guid?> GetOrganizationIdAsync()
    {
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(firebaseUid)) return null;

        var user = await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
        return user?.OrganizationId;
    }

    [HttpGet]
    public async Task<IActionResult> GetWebsites()
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new GetWebsitesQuery { OrganizationId = orgId.Value });
        return Ok(result);
    }

    [HttpPost("connect")]
    public async Task<IActionResult> ConnectWebsite([FromBody] ConnectWebsiteRequest request)
    {
        var orgId = await GetOrganizationIdAsync();
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

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeWebsiteRequest request)
    {
        var orgId = await GetOrganizationIdAsync() ?? (request.OrganizationId == Guid.Empty ? Guid.NewGuid() : request.OrganizationId);

        var command = new AnalyzeWebsiteCommand
        {
            OrganizationId = orgId,
            DomainUrl = request.DomainUrl
        };

        var result = await _mediator.Send(command);

        return Ok(new
        {
            Message = $"Analyzed {request.DomainUrl} successfully.",
            Recommendations = result
        });
    }
}

public class ConnectWebsiteRequest
{
    public string DomainUrl { get; set; } = string.Empty;
    public string PlatformName { get; set; } = "Custom";
}

public class AnalyzeWebsiteRequest
{
    public string DomainUrl { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; } 
}
