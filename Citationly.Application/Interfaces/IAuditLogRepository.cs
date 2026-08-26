using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IAuditLogRepository
{
    Task<Guid> CreateAsync(AuditLog log, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetByOrganizationAsync(Guid organizationId, int limit = 100, CancellationToken cancellationToken = default);
}
