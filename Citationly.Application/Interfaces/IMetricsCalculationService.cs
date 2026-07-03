using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IMetricsCalculationService
{
    Task CalculateAndStoreMetricsAsync(Guid organizationId, DateTime scanDate, string userBrandName);
}
