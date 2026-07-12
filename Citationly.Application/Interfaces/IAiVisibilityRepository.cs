using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IAiVisibilityRepository
{
    Task<Guid> InsertCompetitorAsync(Competitor competitor);
    Task<List<Competitor>> GetCompetitorsByOrgAsync(Guid organizationId);
    Task DeleteCompetitorsByOrgAsync(Guid organizationId);
    
    Task<Guid> InsertHistoricalScanAsync(HistoricalScan scan);
    Task<List<HistoricalScan>> GetHistoricalScansByOrgAsync(Guid organizationId);
    
    Task<Guid> InsertShareOfVoiceAsync(ShareOfVoice share);
    Task<List<ShareOfVoice>> GetShareOfVoiceByOrgAsync(Guid organizationId);
    Task DeleteShareOfVoiceByScanDateAsync(Guid organizationId, DateOnly scanDate);

    Task<Guid> InsertGeoPillarAsync(GeoPillar pillar);
    Task<List<GeoPillar>> GetGeoPillarsByOrgAsync(Guid organizationId, DateOnly? fromDate = null);

    Task<Guid> InsertPromptCoverageAsync(PromptCoverage coverage);
    Task<List<PromptCoverage>> GetPromptCoverageByOrgAsync(Guid organizationId, DateOnly? fromDate = null);

    Task<Guid> InsertWinLossEventAsync(WinLossEvent winLoss);
    Task<List<WinLossEvent>> GetWinLossEventsByOrgAsync(Guid organizationId, int limit = 10);

    Task EnsureGeoTablesCreatedAsync();

    Task<List<Guid>> GetAllOrganizationIdsAsync();
}
