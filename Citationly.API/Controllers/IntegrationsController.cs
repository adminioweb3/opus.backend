using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Citationly.Application.Features.Integrations;
using Citationly.Application.Interfaces;
using System.Security.Claims;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class IntegrationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IUserRepository _userRepository;

    public IntegrationsController(IMediator mediator, IUserRepository userRepository)
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
    public async Task<IActionResult> Get()
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new GetIntegrationsQuery { OrganizationId = orgId.Value });
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertIntegrationRequest request)
    {
        var orgId = await GetOrganizationIdAsync();
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
