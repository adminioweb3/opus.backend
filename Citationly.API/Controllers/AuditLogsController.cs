using Citationly.API.Services;
using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AuditLogsController : ControllerBase
{
    private readonly ICurrentOrganizationAccessor _currentOrganization;
    private readonly IAuditLogRepository _auditLogs;

    public AuditLogsController(ICurrentOrganizationAccessor currentOrganization, IAuditLogRepository auditLogs)
    {
        _currentOrganization = currentOrganization;
        _auditLogs = auditLogs;
    }

    [HttpGet]
    [RequireOrgRole("Admin")]
    [AuditAction("audit_logs.read", "Compliance", "AuditLog")]
    public async Task<IActionResult> Get([FromQuery] int limit = 100)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized();

        var rows = await _auditLogs.GetByOrganizationAsync(organizationId.Value, limit, HttpContext.RequestAborted);
        return Ok(rows);
    }
}
