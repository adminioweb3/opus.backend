using Citationly.Application.Interfaces.Visibility;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Visibility;

public class VisibilityScoringService : IVisibilityScoringService
{
    private const int TargetPromptSampleSize = 30;

    private static readonly string[] Platforms = new[]
    {
        "ChatGPT", "Claude", "Gemini"
    };

    public List<PlatformVisibility> CalculatePlatformScores(Guid organizationId, List<AiSearchPrompt> prompts)
    {
        var results = new List<PlatformVisibility>();

        // Base metrics from prompts
        double avgBrandStrength = prompts.Any() ? prompts.Average(p => p.BrandStrength) : 0;
        double avgContentStrength = prompts.Any() ? prompts.Average(p => p.ContentStrength) : 0;
        double avgCitationStrength = prompts.Any() ? prompts.Average(p => p.CitationStrength) : 0;
        
        double overallPromptCoverage = prompts.Any() ? (prompts.Count(p => p.AppearsInAnswer) / (double)prompts.Count) * 100 : 0;
        double overallMentionRate = prompts.Any() ? (prompts.Sum(p => p.MentionProbability) / (double)prompts.Count) : 0;

        foreach (var platform in Platforms)
        {
            // Apply platform-specific heuristics (e.g. Perplexity favors citations, ChatGPT favors brand)
            double visibilityBase = 0;
            switch (platform)
            {
                case "ChatGPT":
                    visibilityBase = (avgBrandStrength * 0.6) + (avgContentStrength * 0.4);
                    break;
                case "Claude":
                    visibilityBase = (avgContentStrength * 0.7) + (avgBrandStrength * 0.3);
                    break;
                case "Gemini":
                    visibilityBase = (avgContentStrength * 0.5) + (avgBrandStrength * 0.3) + (avgCitationStrength * 0.2);
                    break;
                default:
                    visibilityBase = (avgBrandStrength + avgContentStrength + avgCitationStrength) / 3.0;
                    break;
            }

            // Normalize base to 0-100 scale (assuming strengths are 0-100)
            int score = (int)Math.Clamp(visibilityBase, 0, 100);

            // Keep the summary deterministic. If the underlying prompt data is identical,
            // the output should be identical too.
            int mentionRate = (int)Math.Clamp(overallMentionRate, 0, 100);
            int promptCoverage = (int)Math.Clamp(overallPromptCoverage, 0, 100);
            int confidence = CalculateEvidenceConfidence(prompts);

            // Determine average rank bucket based on visibility score
            string avgRank = score >= 80 ? "1–3" :
                             score >= 60 ? "4–10" :
                             score >= 40 ? "11–20" :
                             score >= 20 ? "21–50" : "50+";

            results.Add(new PlatformVisibility
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Platform = platform,
                VisibilityScore = score,
                AverageRank = avgRank,
                MentionRate = mentionRate,
                PromptCoverage = promptCoverage,
                Confidence = confidence,
                IsEnriched = false,
                StrengthsJson = "[]",
                WeaknessesJson = "[]",
                Explanation = "",
                CreatedAt = DateTime.UtcNow
            });
        }

        return results;
    }

    /// <summary>
    /// Confidence v1: evidence quality, not score optimism.
    /// 60% sample size, 30% agreement across observed prompt scores, 10% evaluated-row coverage.
    /// </summary>
    private static int CalculateEvidenceConfidence(List<AiSearchPrompt> prompts)
    {
        if (prompts.Count == 0) return 0;

        var evaluated = prompts
            .Where(p => p.VisibilityScore > 0
                        || p.MentionProbability > 0
                        || p.AppearsInAnswer
                        || !string.IsNullOrWhiteSpace(p.EstimatedRank))
            .ToList();

        if (evaluated.Count == 0) return 15;

        var sampleScore = Math.Sqrt(Math.Min(evaluated.Count, TargetPromptSampleSize) / (double)TargetPromptSampleSize) * 60.0;
        var coverageScore = evaluated.Count / (double)prompts.Count * 10.0;
        var agreementScore = CalculateAgreementScore(evaluated.Select(p => (double)p.VisibilityScore).ToList()) * 30.0;

        return (int)Math.Round(Math.Clamp(sampleScore + agreementScore + coverageScore, 0, 100));
    }

    private static double CalculateAgreementScore(List<double> values)
    {
        if (values.Count <= 1) return 0.5;

        var average = values.Average();
        var variance = values.Sum(v => Math.Pow(v - average, 2)) / values.Count;
        var standardDeviation = Math.Sqrt(variance);

        return Math.Clamp(1.0 - (standardDeviation / 50.0), 0.0, 1.0);
    }
}
