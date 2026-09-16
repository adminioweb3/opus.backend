using Citationly.API.Services;
using Citationly.Application.Features.Integrations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class IntegrationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentOrganizationAccessor _currentOrg;
    private readonly ILogger<IntegrationsController> _logger;

    public IntegrationsController(IMediator mediator, ICurrentOrganizationAccessor currentOrg, ILogger<IntegrationsController> logger)
    {
        _mediator = mediator;
        _currentOrg = currentOrg;
        _logger = logger;
    }

    [HttpGet]
    [RequireOrgRole("Manager")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized(new { message = "User not found or unlinked." });
        return Ok(await _mediator.Send(new GetIntegrationsQuery { OrganizationId = orgId.Value }, cancellationToken));
    }

    [HttpPost]
    [RequireOrgRole("Admin")]
    [AuditAction("integration.upsert", "Integrations", "Integration")]
    public async Task<IActionResult> Upsert([FromBody] UpsertIntegrationRequest request, CancellationToken cancellationToken)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized(new { message = "User not found or unlinked." });

        try
        {
            var id = await _mediator.Send(new UpsertIntegrationCommand
            {
                OrganizationId = orgId.Value,
                PlatformName = request.PlatformName.Trim(),
                ApiUrl = request.ApiUrl.Trim(),
                ApiKey = request.ApiKey.Trim()
            }, cancellationToken);
            return Ok(new { message = "Integration connected successfully.", integrationId = id });
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            _logger.LogWarning("Integration connection rejected for {Platform}: {ErrorType}", request.PlatformName, ex.GetType().Name);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/test")]
    [RequireOrgRole("Admin")]
    [AuditAction("integration.test", "Integrations", "Integration")]
    public async Task<IActionResult> Test(Guid id, CancellationToken cancellationToken)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized(new { message = "User not found or unlinked." });
        var result = await _mediator.Send(new TestIntegrationCommand { OrganizationId = orgId.Value, IntegrationId = id }, cancellationToken);
        return result.Found ? Ok(result) : NotFound(new { message = result.Message });
    }

    [HttpDelete("{id:guid}")]
    [RequireOrgRole("Admin")]
    [AuditAction("integration.disconnect", "Integrations", "Integration")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized(new { message = "User not found or unlinked." });
        var deleted = await _mediator.Send(new DeleteIntegrationCommand { OrganizationId = orgId.Value, IntegrationId = id }, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}

public sealed class UpsertIntegrationRequest
{
    public string PlatformName { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
