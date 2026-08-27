using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IAiVisibilityRepository
{
    Task<Guid> InsertCompetitorAsync(Competitor competitor);
    Task<List<Competitor>> GetCompetitorsByOrgAsync(Guid organizationId, int limit = 100);
    Task DeleteCompetitorsByOrgAsync(Guid organizationId);
    
    Task<Guid> InsertHistoricalScanAsync(HistoricalScan scan);
    Task<List<HistoricalScan>> GetHistoricalScansByOrgAsync(Guid organizationId, int limit = 365);
    
    Task<Guid> InsertShareOfVoiceAsync(ShareOfVoice share);
    Task<List<ShareOfVoice>> GetShareOfVoiceByOrgAsync(Guid organizationId, int limit = 1000);
    Task DeleteShareOfVoiceByScanDateAsync(Guid organizationId, DateOnly scanDate);

    Task<Guid> InsertGeoPillarAsync(GeoPillar pillar);
    Task<List<GeoPillar>> GetGeoPillarsByOrgAsync(Guid organizationId, DateOnly? fromDate = null, int limit = 1000);

    Task<Guid> InsertPromptCoverageAsync(PromptCoverage coverage);
    Task<List<PromptCoverage>> GetPromptCoverageByOrgAsync(Guid organizationId, DateOnly? fromDate = null, int limit = 1000);

    Task<Guid> InsertWinLossEventAsync(WinLossEvent winLoss);
    Task<List<WinLossEvent>> GetWinLossEventsByOrgAsync(Guid organizationId, int limit = 10);

    Task EnsureGeoTablesCreatedAsync();

    Task<List<Guid>> GetAllOrganizationIdsAsync();
}
