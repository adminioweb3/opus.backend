using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Companies;

public interface ICompanyGraphService
{
    /// <summary>
    /// Ensures a Company node exists for this org's own website, upserting it (and refreshing its
    /// embedding) if missing or if LastAnalyzedAt is more than 30 days stale. Reuses the org's own
    /// already-produced business-profile JSON — no extra AI call for the org's own company. Also
    /// links Websites.CompanyId so future lookups are a cheap join.
    /// </summary>
    Task<Company> EnsureCompanyAsync(Guid organizationId, string websiteUrl, string businessName, string rawProfileJson, CancellationToken cancellationToken = default);

    /// <summary>True when the Company (by NormalizedDomain of websiteUrl) is missing or >30 days stale.</summary>
    Task<bool> IsStaleAsync(string websiteUrl);
}
