namespace Citationly.Application.Interfaces.Competitors;

public interface ICompetitorGraphSyncService
{
    /// <summary>
    /// Materializes an org's Company's CompanyCompetitor edges into the existing per-org
    /// Competitors table, so every existing reader (report, Answer Atlas, Competitor Watch)
    /// keeps working unmodified. Returns the materialized rows.
    /// </summary>
    Task<List<Citationly.Domain.Entities.Competitor>> SyncOrgCompetitorsAsync(Guid organizationId, Guid companyId);
}
