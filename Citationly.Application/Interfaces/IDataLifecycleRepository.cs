using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IDataLifecycleRepository
{
    Task<RetentionPolicy?> GetRetentionPolicyAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<RetentionPolicy> UpsertRetentionPolicyAsync(RetentionPolicy policy, CancellationToken cancellationToken = default);
    Task<IEnumerable<DataDeletionRequest>> GetDeletionRequestsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<DataDeletionRequest> CreateDeletionRequestAsync(DataDeletionRequest request, CancellationToken cancellationToken = default);
    Task<bool> CancelDeletionRequestAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, long>> GetOrganizationDeletionPreviewAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
