using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Citationly.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _repository;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(IAuditLogRepository repository, ILogger<AuditLogService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task RecordAsync(
        string action,
        string category,
        string outcome,
        Guid? organizationId = null,
        Guid? actorUserId = null,
        string actorEmail = "",
        string actorType = "User",
        string targetType = "",
        string targetId = "",
        string metadataJson = "{}",
        string ipAddress = "",
        string userAgent = "",
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _repository.CreateAsync(new AuditLog
            {
                OrganizationId = organizationId,
                ActorUserId = actorUserId,
                ActorEmail = actorEmail ?? string.Empty,
                ActorType = string.IsNullOrWhiteSpace(actorType) ? "User" : actorType,
                Action = action,
                Category = category,
                Outcome = outcome,
                TargetType = targetType ?? string.Empty,
                TargetId = targetId ?? string.Empty,
                MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson,
                IpAddress = ipAddress ?? string.Empty,
                UserAgent = userAgent ?? string.Empty
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record audit log for action {Action}", action);
        }
    }
}
