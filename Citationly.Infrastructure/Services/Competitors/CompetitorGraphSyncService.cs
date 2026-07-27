using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;
using Citationly.Application.Interfaces.Competitors;
using Citationly.Domain.Entities;
using Citationly.Infrastructure.Services.Companies;

namespace Citationly.Infrastructure.Services.Competitors;

/// <summary>
/// Materializes CompanyCompetitor graph edges into the existing per-org Competitors table, so
/// every existing reader (report, Answer Atlas, Competitor Watch) keeps working unmodified —
/// this is the single choke point both onboarding entry points funnel through, which is also
/// what closes the old dueling-invention-pipeline bug (two separate services used to write to
/// Competitors independently; now there is exactly one writer).
/// </summary>
public class CompetitorGraphSyncService : ICompetitorGraphSyncService
{
    private readonly ICompanyCompetitorRepository _companyCompetitorRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly IWebsiteRepository _websiteRepository;

    public CompetitorGraphSyncService(
        ICompanyCompetitorRepository companyCompetitorRepository,
        ICompanyRepository companyRepository,
        IWebsiteRepository websiteRepository)
    {
        _companyCompetitorRepository = companyCompetitorRepository;
        _companyRepository = companyRepository;
        _websiteRepository = websiteRepository;
    }

    public async Task<List<Competitor>> SyncOrgCompetitorsAsync(Guid organizationId, Guid companyId)
    {
        var edges = (await _companyCompetitorRepository.GetByCompanyIdAsync(companyId)).ToList();
        if (edges.Count == 0)
        {
            await _websiteRepository.DeleteCompetitorsByOrgAsync(organizationId);
            return new List<Competitor>();
        }

        var competitorCompanies = (await _companyRepository.GetByIdsAsync(edges.Select(e => e.CompetitorCompanyId)))
            .ToDictionary(c => c.Id);

        var rows = new List<Competitor>();
        foreach (var edge in edges)
        {
            if (!competitorCompanies.TryGetValue(edge.CompetitorCompanyId, out var company)) continue;

            rows.Add(new Competitor
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = company.CompanyName,
                WebsiteUrl = company.Website,
                Industry = company.Industry ?? "",
                Description = edge.Reason ?? "",
                Category = "Direct",
                CompetitorType = "Direct",
                Confidence = edge.Confidence,
                Rank = edge.Rank,
                SimilarityScore = (int)Math.Round(edge.Similarity),
                Authority = CompanyProfileSummarizer.ExtractDomainAuthorityEstimate(company.BusinessProfileJson),
                EnrichmentStatus = "Completed",
                EnrichedJson = company.BusinessProfileJson,
                EnrichedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                RawJson = JsonSerializer.Serialize(new
                {
                    rank = edge.Rank,
                    similarity = edge.Similarity,
                    confidence = edge.Confidence,
                    reason = edge.Reason,
                    strength = edge.Strength,
                    weakness = edge.Weakness
                })
            });
        }

        await _websiteRepository.DeleteCompetitorsByOrgAsync(organizationId);
        await _websiteRepository.InsertCompetitorsAsync(rows);
        return rows;
    }
}
