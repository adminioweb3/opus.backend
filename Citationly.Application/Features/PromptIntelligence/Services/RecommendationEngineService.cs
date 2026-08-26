using Citationly.Domain.Entities;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface IRecommendationEngineService
{
    Task<IEnumerable<PromptRecommendation>> GenerateRecommendationsAsync(
        Guid analysisId, 
        PromptVisibility visibility, 
        IEnumerable<CompetitorComparison> competitors,
        IEnumerable<PromptCitation> citations,
        CancellationToken ct);
}

public class RecommendationEngineService : IRecommendationEngineService
{
    public Task<IEnumerable<PromptRecommendation>> GenerateRecommendationsAsync(
        Guid analysisId, 
        PromptVisibility visibility, 
        IEnumerable<CompetitorComparison> competitors,
        IEnumerable<PromptCitation> citations,
        CancellationToken ct)
    {
        var recs = new List<PromptRecommendation>();
        var competitorList = competitors.ToList();
        var citationList = citations.Where(c => !string.IsNullOrWhiteSpace(c.Url) || !string.IsNullOrWhiteSpace(c.Domain)).ToList();
        var strongestCompetitor = competitorList
            .OrderByDescending(c => c.VisibilityScore)
            .ThenByDescending(c => c.ShareOfVoice)
            .FirstOrDefault();
        var topCitation = citationList
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Url) ? c.Domain : c.Url)
            .OrderByDescending(g => g.Count())
            .Select(g => g.First())
            .FirstOrDefault();

        if (visibility.MentionFrequency < 50)
        {
            var competitorEvidence = strongestCompetitor == null
                ? "Observed AI answers mention the brand in fewer than half of tested model responses."
                : $"Observed AI answers favor {strongestCompetitor.CompetitorName} with visibility {strongestCompetitor.VisibilityScore}/100 and share of voice {strongestCompetitor.ShareOfVoice}%.";

            recs.Add(new PromptRecommendation
            {
                PromptAnalysisId = analysisId,
                Category = "Content",
                Title = "Close the observed prompt coverage gap",
                Description = $"{competitorEvidence} Build or refresh a page that directly answers this prompt's buyer intent, then include concise comparison criteria, use cases, pricing/proof points, and entity names AI engines can extract.",
                Priority = "High",
                Difficulty = "Medium",
                EstimatedVisibilityGain = 15
            });
        }

        if (visibility.CitationCount == 0)
        {
            var sourceEvidence = topCitation == null
                ? "No owned-domain citations were extracted from the model responses."
                : $"No owned-domain citations were extracted; the most repeated cited source was {DisplaySource(topCitation)}.";

            recs.Add(new PromptRecommendation
            {
                PromptAnalysisId = analysisId,
                Category = "GEO",
                Title = "Create a citation-worthy answer source",
                Description = $"{sourceEvidence} Match the winning source format with a crawlable section containing a direct answer, supporting evidence, outbound references, and FAQ/schema markup.",
                Priority = "High",
                Difficulty = "Low",
                EstimatedVisibilityGain = 8
            });
        }

        if (visibility.AveragePosition > 50)
        {
            var rankingEvidence = strongestCompetitor == null
                ? $"Average brand position was {visibility.AveragePosition}, which means the brand appeared late or inconsistently."
                : $"Average brand position was {visibility.AveragePosition}; {strongestCompetitor.CompetitorName} currently has the strongest observed competitor visibility.";

            recs.Add(new PromptRecommendation
            {
                PromptAnalysisId = analysisId,
                Category = "GEO",
                Title = "Improve answer prominence against the top competitor",
                Description = $"{rankingEvidence} Add summary tables, explicit differentiators, and internal links from supporting cluster pages so the brand is easy to rank near the top of generated answers.",
                Priority = "Medium",
                Difficulty = "High",
                EstimatedVisibilityGain = 20
            });
        }

        if (strongestCompetitor != null && topCitation != null)
        {
            recs.Add(new PromptRecommendation
            {
                PromptAnalysisId = analysisId,
                Category = "Revenue",
                Title = $"Displace {strongestCompetitor.CompetitorName} on cited source patterns",
                Description = $"The strongest competitor signal is {strongestCompetitor.CompetitorName}; a cited page/source in this prompt set is {DisplaySource(topCitation)}. Mirror the useful content characteristics that source exposes: direct answer block, comparison attributes, dated evidence, and source links.",
                Priority = strongestCompetitor.ShareOfVoice >= visibility.ShareOfVoice ? "High" : "Medium",
                Difficulty = "Medium",
                EstimatedVisibilityGain = 12
            });
        }

        if (recs.Count == 0)
        {
            recs.Add(new PromptRecommendation
            {
                PromptAnalysisId = analysisId,
                Category = "GEO",
                Title = "Defend current AI answer coverage",
                Description = $"Observed visibility is {visibility.OverallVisibilityScore}/100 with {visibility.CitationCount} owned citation(s). Keep the cited pages fresh, structured, and internally linked so current coverage does not decay.",
                Priority = "Low",
                Difficulty = "Low",
                EstimatedVisibilityGain = 4
            });
        }

        return Task.FromResult<IEnumerable<PromptRecommendation>>(recs);
    }

    private static string DisplaySource(PromptCitation citation)
    {
        if (!string.IsNullOrWhiteSpace(citation.Url))
        {
            return citation.Url;
        }

        return citation.Domain;
    }
}
