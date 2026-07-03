using Citationly.Domain.Entities;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface IRecommendationEngineService
{
    Task<IEnumerable<PromptRecommendation>> GenerateRecommendationsAsync(
        Guid analysisId, 
        PromptVisibility visibility, 
        IEnumerable<CompetitorComparison> competitors,
        CancellationToken ct);
}

public class RecommendationEngineService : IRecommendationEngineService
{
    public Task<IEnumerable<PromptRecommendation>> GenerateRecommendationsAsync(
        Guid analysisId, 
        PromptVisibility visibility, 
        IEnumerable<CompetitorComparison> competitors,
        CancellationToken ct)
    {
        var recs = new List<PromptRecommendation>();

        // Content Opportunity
        if (visibility.MentionFrequency < 50)
        {
            recs.Add(new PromptRecommendation
            {
                PromptAnalysisId = analysisId,
                Category = "Content",
                Title = "Create Comparison Guide",
                Description = "Your competitors are mentioned more frequently. Create a 'Best [Topic] Tools' comparison guide to capture semantic relevance.",
                Priority = "High",
                Difficulty = "Medium",
                EstimatedVisibilityGain = 15
            });
        }

        // Technical Opportunity
        if (visibility.CitationCount == 0)
        {
            recs.Add(new PromptRecommendation
            {
                PromptAnalysisId = analysisId,
                Category = "Technical",
                Title = "Implement FAQ Schema",
                Description = "LLMs are struggling to cite your direct answers. Implementing FAQ structured data will improve extraction rates.",
                Priority = "High",
                Difficulty = "Low",
                EstimatedVisibilityGain = 8
            });
        }

        // GEO
        if (visibility.AveragePosition > 50)
        {
            recs.Add(new PromptRecommendation
            {
                PromptAnalysisId = analysisId,
                Category = "GEO",
                Title = "Improve Topical Authority",
                Description = "You are mentioned late in responses. Build out a topic cluster with internal links to push your brand higher in the context window.",
                Priority = "Medium",
                Difficulty = "High",
                EstimatedVisibilityGain = 20
            });
        }
        
        // General
        recs.Add(new PromptRecommendation
        {
            PromptAnalysisId = analysisId,
            Category = "Content",
            Title = "Publish Case Studies",
            Description = "Increase brand trust signals by publishing detailed case studies. AI models prioritize verifiable outcomes.",
            Priority = "Medium",
            Difficulty = "Medium",
            EstimatedVisibilityGain = 10
        });

        return Task.FromResult<IEnumerable<PromptRecommendation>>(recs);
    }
}
