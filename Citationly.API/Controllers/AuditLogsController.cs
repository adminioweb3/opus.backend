using Citationly.API.Services;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;

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

    [HttpGet("export.csv")]
    [RequireOrgRole("Admin")]
    [AuditAction("audit_logs.export", "Compliance", "AuditLog")]
    public async Task<IActionResult> ExportCsv([FromQuery] int limit = 1000)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized();

        var rows = await _auditLogs.GetByOrganizationAsync(
            organizationId.Value,
            Math.Clamp(limit, 1, 10000),
            HttpContext.RequestAborted);

        var csv = BuildCsv(rows);
        var fileName = $"citationly-audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }

    private static string BuildCsv(IEnumerable<AuditLog> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("created_at,actor_email,actor_type,action,category,outcome,target_type,target_id,ip_address,user_agent,metadata_json");

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(row.CreatedAt.ToString("O")),
                Csv(row.ActorEmail),
                Csv(row.ActorType),
                Csv(row.Action),
                Csv(row.Category),
                Csv(row.Outcome),
                Csv(row.TargetType),
                Csv(row.TargetId),
                Csv(row.IpAddress),
                Csv(row.UserAgent),
                Csv(row.MetadataJson)
            }));
        }

        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        var escaped = (value ?? string.Empty).Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
