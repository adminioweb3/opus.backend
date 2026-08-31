using Citationly.API.Services;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AlertsController : ControllerBase
{
    private readonly ICurrentOrganizationAccessor _currentOrganization;
    private readonly IAlertRepository _alertRepository;
    private readonly IAlertService _alertService;

    public AlertsController(
        ICurrentOrganizationAccessor currentOrganization,
        IAlertRepository alertRepository,
        IAlertService alertService)
    {
        _currentOrganization = currentOrganization;
        _alertRepository = alertRepository;
        _alertService = alertService;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int limit = 50, [FromQuery] bool unreadOnly = false)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        await _alertService.GenerateCommandCenterAlertsAsync(orgId.Value, HttpContext.RequestAborted);
        var alerts = await _alertRepository.GetAlertsAsync(orgId.Value, limit, unreadOnly);
        return Ok(alerts);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        var count = await _alertRepository.MarkReadAsync(orgId.Value, id);
        return count == 0 ? NotFound() : Ok(new { updated = count });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        var count = await _alertRepository.MarkReadAsync(orgId.Value);
        return Ok(new { updated = count });
    }

    [HttpGet("thresholds")]
    public async Task<IActionResult> GetThresholds()
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        return Ok(await _alertRepository.GetThresholdsAsync(orgId.Value));
    }

    [HttpPut("thresholds/{alertType}")]
    [RequireOrgRole("Manager")]
    [AuditAction("alerts.threshold.update", "Monitoring", "AlertThreshold")]
    public async Task<IActionResult> UpsertThreshold(string alertType, [FromBody] AlertThresholdRequest request)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        if (request.WebhookEnabled)
        {
            return BadRequest(new { message = "Alert webhooks are not available yet. Configure email alerts instead." });
        }

        await _alertRepository.UpsertThresholdAsync(new AlertThreshold
        {
            OrganizationId = orgId.Value,
            AlertType = alertType,
            ThresholdValue = Math.Clamp(request.ThresholdValue, 1, 100),
            EmailEnabled = request.EmailEnabled,
            WebhookEnabled = false,
            WebhookUrl = string.Empty
        });

        return Ok(new { saved = true });
    }
}

public class AlertThresholdRequest
{
    public int ThresholdValue { get; set; } = 5;
    public bool EmailEnabled { get; set; } = true;
    public bool WebhookEnabled { get; set; }
    public string? WebhookUrl { get; set; }
}
