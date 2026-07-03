using Citationly.Application.Interfaces.Onboarding;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Onboarding;

public class RoadmapService : IRoadmapService
{
    public RoadmapGenerationResult GenerateRoadmap(Guid organizationId, List<DiscoveryRecommendationDto> discoveredRecs, GapAnalysisResult gapAnalysis)
    {
        var result = new RoadmapGenerationResult();
        result.Summary.OrganizationId = organizationId;
        
        int criticalCount = 0;
        int highCount = 0;
        int recIndex = 1;

        foreach (var disc in discoveredRecs)
        {
            var rec = new GeoRecommendation
            {
                RecommendationId = $"REC-{recIndex:D3}",
                Category = disc.Category,
                Title = disc.Title,
                Description = disc.Description,
                OrganizationId = organizationId,
                IsEnriched = false,
                ActionItemsJson = "[]",
                ExampleResourcesJson = "[]",
                ReferenceLinksJson = "[]"
            };

            // Deterministic Assignment Logic
            AssignMetrics(rec, gapAnalysis);

            if (rec.Priority == "Critical") criticalCount++;
            if (rec.Priority == "High") highCount++;

            result.Recommendations.Add(rec);
            recIndex++;
        }

        // Calculate Summary
        result.Summary.TotalRecommendations = result.Recommendations.Count;
        result.Summary.CriticalRecommendations = criticalCount;
        result.Summary.HighPriorityRecommendations = highCount;

        if (criticalCount > 3 || (gapAnalysis.HasLowSearchVisibility && gapAnalysis.LacksCitations))
        {
            result.Summary.OverallPriority = "Critical";
            result.Summary.EstimatedOverallImpact = "Very High";
            result.Summary.EstimatedImplementationTime = "3-6 months";
        }
        else if (criticalCount > 0 || highCount > 5)
        {
            result.Summary.OverallPriority = "High";
            result.Summary.EstimatedOverallImpact = "High";
            result.Summary.EstimatedImplementationTime = "1-3 months";
        }
        else
        {
            result.Summary.OverallPriority = "Medium";
            result.Summary.EstimatedOverallImpact = "Medium";
            result.Summary.EstimatedImplementationTime = "1-2 months";
        }

        // Sort by Priority (Critical -> High -> Medium -> Low), then Difficulty (Easy -> Moderate -> Difficult)
        result.Recommendations = result.Recommendations
            .OrderBy(r => PriorityScore(r.Priority))
            .ThenBy(r => DifficultyScore(r.EstimatedDifficulty))
            .ToList();

        return result;
    }

    private void AssignMetrics(GeoRecommendation rec, GapAnalysisResult gapAnalysis)
    {
        var category = rec.Category.ToLower();

        if (category.Contains("technical") || category.Contains("schema"))
        {
            rec.Priority = gapAnalysis.HasLowSearchVisibility ? "Critical" : "High";
            rec.EstimatedImpact = "High";
            rec.EstimatedDifficulty = "Moderate";
            rec.ImplementationTime = "1-2 weeks";
            rec.SuccessMetric = "Increase Platform Visibility";
            rec.ExpectedOutcome = "Improved crawlability and entity recognition by AI bots.";
        }
        else if (category.Contains("citation"))
        {
            rec.Priority = gapAnalysis.LacksCitations ? "Critical" : "Medium";
            rec.EstimatedImpact = "Medium";
            rec.EstimatedDifficulty = "Easy";
            rec.ImplementationTime = "Less than 1 week";
            rec.SuccessMetric = "Increase Citation Strength";
            rec.ExpectedOutcome = "Stronger authority signals leading to higher ranking in generative responses.";
        }
        else if (category.Contains("content") || category.Contains("prompt"))
        {
            rec.Priority = gapAnalysis.HasPoorPersonaCoverage ? "High" : "Medium";
            rec.EstimatedImpact = "High";
            rec.EstimatedDifficulty = "Difficult";
            rec.ImplementationTime = "2-4 weeks";
            rec.SuccessMetric = "Improve Prompt Coverage";
            rec.ExpectedOutcome = "Better alignment with user intent for unserved personas.";
        }
        else if (category.Contains("brand") || category.Contains("authority"))
        {
            rec.Priority = gapAnalysis.HasPoorBrandMentionRate ? "High" : "Medium";
            rec.EstimatedImpact = "Very High";
            rec.EstimatedDifficulty = "Very Difficult";
            rec.ImplementationTime = "3-6 months";
            rec.SuccessMetric = "Increase AI mention rate";
            rec.ExpectedOutcome = "Higher brand trust and likelihood of being recommended by AI as a solution.";
        }
        else // Fallback
        {
            rec.Priority = "Medium";
            rec.EstimatedImpact = "Medium";
            rec.EstimatedDifficulty = "Moderate";
            rec.ImplementationTime = "1-3 months";
            rec.SuccessMetric = "Increase Organic Traffic";
            rec.ExpectedOutcome = "Incremental improvement in AI visibility.";
        }
    }

    private int PriorityScore(string priority) => priority switch
    {
        "Critical" => 1,
        "High" => 2,
        "Medium" => 3,
        "Low" => 4,
        _ => 5
    };

    private int DifficultyScore(string diff) => diff switch
    {
        "Easy" => 1,
        "Moderate" => 2,
        "Difficult" => 3,
        "Very Difficult" => 4,
        _ => 5
    };
}
