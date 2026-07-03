using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Citations;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Citations;

public class CitationAnalyticsService : ICitationAnalyticsService
{
    private readonly IWebsiteRepository _websiteRepository;

    public CitationAnalyticsService(IWebsiteRepository websiteRepository)
    {
        _websiteRepository = websiteRepository;
    }

    public async Task ComputeAnalyticsAsync(Guid organizationId)
    {
        var sources = await _websiteRepository.GetCitationSourcesAsync(organizationId);
        
        var enrichedSources = sources.Where(s => s.IsEnriched).ToList();
        
        // Even if no sources are enriched yet, we must maintain the summary record to not break the frontend
        var summary = await _websiteRepository.GetCitationSummaryAsync(organizationId);
        
        if (summary == null)
        {
            summary = new CitationSummary
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                CreatedAt = DateTime.UtcNow,
                TotalSources = sources.Count()
            };
            await _websiteRepository.InsertCitationsAsync(summary, Enumerable.Empty<CitationSource>());
        }

        if (enrichedSources.Any())
        {
            summary.AverageAuthorityScore = (int)Math.Round(enrichedSources.Average(s => s.AuthorityScore));
            summary.AverageInfluenceScore = (int)Math.Round(enrichedSources.Average(s => s.InfluenceScore));
            
            summary.HighestOpportunitySource = enrichedSources
                .OrderByDescending(s => s.OpportunityScore)
                .Select(s => s.Source)
                .FirstOrDefault() ?? "";

            summary.MostInfluentialSource = enrichedSources
                .OrderByDescending(s => s.InfluenceScore)
                .Select(s => s.Source)
                .FirstOrDefault() ?? "";
                
            summary.TotalSources = sources.Count();
        }

        await _websiteRepository.UpdateCitationSummaryAsync(summary);
    }
}
