using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Citationly.API.Services;
using Citationly.Application.Features.Integrations;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class IntegrationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentOrganizationAccessor _currentOrg;

    public IntegrationsController(IMediator mediator, ICurrentOrganizationAccessor currentOrg)
    {
        _mediator = mediator;
        _currentOrg = currentOrg;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new GetIntegrationsQuery { OrganizationId = orgId.Value });
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertIntegrationRequest request)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var command = new UpsertIntegrationCommand
        {
            OrganizationId = orgId.Value,
            PlatformName = request.PlatformName,
            ApiUrl = request.ApiUrl,
            ApiKey = request.ApiKey
        };

        try
        {
            var result = await _mediator.Send(command);
            return Ok(new { Message = "Integration connected successfully", IntegrationId = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
}

public class UpsertIntegrationRequest
{
    public string PlatformName { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
