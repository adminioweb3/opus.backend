using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Companies;

public interface ICompanyCompetitorRepository
{
    /// <summary>
    /// Delete-then-insert-in-transaction — the full set of ranked competitor edges for one
    /// Company node, replacing whatever was there before (same shape as the existing
    /// per-org Competitors delete/insert cycle).
    /// </summary>
    Task ReplaceCompetitorsForCompanyAsync(Guid companyId, IEnumerable<CompanyCompetitor> edges);

    Task<IEnumerable<CompanyCompetitor>> GetByCompanyIdAsync(Guid companyId);
}
