using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IRecommendationImpactService
{
    Task<RecommendationImplementation?> MarkImplementedAsync(Guid organizationId, Guid recommendationId, int monitoringWindowDays = 14, CancellationToken ct = default);
    Task<int> ProcessDueMeasurementsAsync(Guid? organizationId = null, CancellationToken ct = default);
}
