using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IVisibilitySnapshotRepository
{
    Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId);
    Task<VisibilityScanSummary?> GetLatestSummaryAsync(Guid organizationId);
    Task<List<VisibilityScanSummary>> GetHistoryAsync(Guid organizationId, int days);
    Task<List<VisibilityPlatformSnapshot>> GetLatestPlatformsAsync(Guid organizationId);
    Task<Dictionary<string, List<int>>> GetPlatformSparklinesAsync(Guid organizationId, int days);
    Task SaveSnapshotAsync(VisibilityScanSummary summary, List<VisibilityPlatformSnapshot> platforms);
}
