namespace Citationly.Application.Interfaces;

public interface IAuditLogService
{
    Task RecordAsync(
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
        CancellationToken cancellationToken = default);
}
