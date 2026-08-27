using Citationly.API.Services;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/DataLifecycle")]
public class DataLifecycleController : ControllerBase
{
    private readonly ICurrentOrganizationAccessor _currentOrganization;
    private readonly IDataLifecycleRepository _repository;

    public DataLifecycleController(
        ICurrentOrganizationAccessor currentOrganization,
        IDataLifecycleRepository repository)
    {
        _currentOrganization = currentOrganization;
        _repository = repository;
    }

    [HttpGet("retention-policy")]
    [RequireOrgRole("Admin")]
    public async Task<IActionResult> GetRetentionPolicy()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized();

        var policy = await _repository.GetRetentionPolicyAsync(organizationId.Value, HttpContext.RequestAborted)
            ?? new RetentionPolicy { OrganizationId = organizationId.Value };
        return Ok(policy);
    }

    [HttpPut("retention-policy")]
    [RequireOrgRole("Admin")]
    [AuditAction("data_lifecycle.retention_policy.update", "Compliance", "RetentionPolicy")]
    public async Task<IActionResult> SaveRetentionPolicy([FromBody] RetentionPolicyRequest request)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized();

        var policy = await _repository.UpsertRetentionPolicyAsync(new RetentionPolicy
        {
            OrganizationId = organizationId.Value,
            RawPromptEvidenceDays = request.RawPromptEvidenceDays is > 0 ? request.RawPromptEvidenceDays : null,
            AuditLogDays = Math.Clamp(request.AuditLogDays, 365, 3650),
            SnapshotDays = Math.Clamp(request.SnapshotDays, 365, 3650)
        }, HttpContext.RequestAborted);

        return Ok(policy);
    }

    [HttpGet("deletion-preview")]
    [RequireOrgRole("Admin")]
    [AuditAction("data_lifecycle.deletion_preview.read", "Compliance", "Organization")]
    public async Task<IActionResult> GetDeletionPreview()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized();

        var counts = await _repository.GetOrganizationDeletionPreviewAsync(organizationId.Value, HttpContext.RequestAborted);
        return Ok(new
        {
            organizationId,
            scope = "Organization",
            mode = "PreviewOnly",
            totalRows = counts.Values.Sum(),
            tableCounts = counts
        });
    }

    [HttpGet("deletion-requests")]
    [RequireOrgRole("Admin")]
    public async Task<IActionResult> GetDeletionRequests([FromQuery] int limit = 100)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized();

        return Ok(await _repository.GetDeletionRequestsAsync(
            organizationId.Value,
            Math.Clamp(limit, 1, 500),
            HttpContext.RequestAborted));
    }

    [HttpPost("deletion-requests")]
    [RequireOrgRole("Owner")]
    [AuditAction("data_lifecycle.deletion_request.create", "Compliance", "Organization")]
    public async Task<IActionResult> RequestDeletion([FromBody] DataDeletionRequestBody request)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized();

        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var deletion = await _repository.CreateDeletionRequestAsync(new DataDeletionRequest
        {
            OrganizationId = organizationId.Value,
            RequestedByUserId = caller.Value.UserId,
            Scope = "Organization",
            Reason = request.Reason?.Trim() ?? string.Empty,
            ScheduledFor = DateTime.UtcNow.AddDays(Math.Clamp(request.GracePeriodDays, 7, 30))
        }, HttpContext.RequestAborted);

        return Ok(deletion);
    }

    [HttpPost("deletion-requests/{requestId:guid}/cancel")]
    [RequireOrgRole("Owner")]
    [AuditAction("data_lifecycle.deletion_request.cancel", "Compliance", "DataDeletionRequest")]
    public async Task<IActionResult> CancelDeletion(Guid requestId)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized();

        var cancelled = await _repository.CancelDeletionRequestAsync(organizationId.Value, requestId, HttpContext.RequestAborted);
        return cancelled ? Ok(new { cancelled = true }) : NotFound();
    }

    private async Task<(Guid UserId, Guid OrganizationId, string Role)?> GetCallerAsync()
    {
        return await _currentOrganization.GetCurrentUserAsync(User, HttpContext.RequestAborted);
    }
}

public class RetentionPolicyRequest
{
    public int? RawPromptEvidenceDays { get; set; }
    public int AuditLogDays { get; set; } = 365;
    public int SnapshotDays { get; set; } = 1095;
}

public class DataDeletionRequestBody
{
    public string? Reason { get; set; }
    public int GracePeriodDays { get; set; } = 14;
}
