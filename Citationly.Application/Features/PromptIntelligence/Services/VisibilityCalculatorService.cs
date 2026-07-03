using System.Text.RegularExpressions;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface IVisibilityCalculatorService
{
    (PromptVisibility Visibility, IEnumerable<PromptMention> Mentions, IEnumerable<CompetitorComparison> CompetitorComparisons) CalculateVisibilityMetrics(
        Guid analysisId,
        IEnumerable<PromptResponse> responses,
        string brandName,
        IEnumerable<string> competitors);
}

public class VisibilityCalculatorService : IVisibilityCalculatorService
{
    public (PromptVisibility Visibility, IEnumerable<PromptMention> Mentions, IEnumerable<CompetitorComparison> CompetitorComparisons) CalculateVisibilityMetrics(
        Guid analysisId, 
        IEnumerable<PromptResponse> responses, 
        string brandName, 
        IEnumerable<string> competitors)
    {
        var mentions = new List<PromptMention>();
        var compComparisons = new List<CompetitorComparison>();

        int totalPlatforms = responses.Count();
        if (totalPlatforms == 0) totalPlatforms = 1;

        int brandMentionCount = 0;
        int brandTotalPosition = 0;
        int brandCitationCount = 0; // Simple simulation for citations (links)

        var competitorScores = new Dictionary<string, (int mentions, int totalPosition)>();
        foreach (var c in competitors) competitorScores[c] = (0, 0);

        foreach (var response in responses)
        {
            // Simple text analysis
            var text = response.ResponseText;
            
            // Extract Brand Mentions
            var brandIdx = text.IndexOf(brandName, StringComparison.OrdinalIgnoreCase);
            if (brandIdx >= 0)
            {
                brandMentionCount++;
                // Position is roughly where it appeared (0-100 scale, smaller is better/earlier)
                int position = (int)((double)brandIdx / Math.Max(1, text.Length) * 100);
                brandTotalPosition += position;
                
                // Simulated context snippet
                int snippetStart = Math.Max(0, brandIdx - 50);
                int snippetLength = Math.Min(text.Length - snippetStart, 100);
                
                mentions.Add(new PromptMention
                {
                    PromptAnalysisId = analysisId,
                    Platform = response.Platform,
                    EntityName = brandName,
                    IsBrand = true,
                    ContextSnippet = text.Substring(snippetStart, snippetLength).Replace("\n", " "),
                    Position = position
                });

                // Simulate citations if markdown links exist
                if (Regex.IsMatch(text, @"\[.*\]\(http.*\)", RegexOptions.IgnoreCase))
                {
                    brandCitationCount++;
                }
            }

            // Extract Competitor Mentions
            foreach (var comp in competitors)
            {
                var compIdx = text.IndexOf(comp, StringComparison.OrdinalIgnoreCase);
                if (compIdx >= 0)
                {
                    var current = competitorScores[comp];
                    int position = (int)((double)compIdx / Math.Max(1, text.Length) * 100);
                    competitorScores[comp] = (current.mentions + 1, current.totalPosition + position);

                    int snippetStart = Math.Max(0, compIdx - 50);
                    int snippetLength = Math.Min(text.Length - snippetStart, 100);

                    mentions.Add(new PromptMention
                    {
                        PromptAnalysisId = analysisId,
                        Platform = response.Platform,
                        EntityName = comp,
                        IsBrand = false,
                        ContextSnippet = text.Substring(snippetStart, snippetLength).Replace("\n", " "),
                        Position = position
                    });
                }
            }
        }

        // Calculate Visibility Score
        int mentionFrequency = (int)Math.Round((double)brandMentionCount / totalPlatforms * 100);
        int averagePosition = brandMentionCount > 0 ? brandTotalPosition / brandMentionCount : 100;
        
        // Visibility Formula: Weight frequency heavily, penalize late positions
        int visibilityScore = (mentionFrequency * 2) - (averagePosition / 2);
        visibilityScore = Math.Clamp(visibilityScore, 0, 100);

        int totalMentionsOverall = brandMentionCount + competitorScores.Values.Sum(v => v.mentions);
        int shareOfVoice = totalMentionsOverall > 0 ? (int)Math.Round((double)brandMentionCount / totalMentionsOverall * 100) : 0;

        var visibility = new PromptVisibility
        {
            PromptAnalysisId = analysisId,
            OverallVisibilityScore = visibilityScore,
            MentionFrequency = mentionFrequency,
            AveragePosition = averagePosition,
            ShareOfVoice = shareOfVoice,
            CitationCount = brandCitationCount,
            CompetitorCount = competitors.Count()
        };

        // Competitor Comparisons
        foreach (var comp in competitorScores)
        {
            int compMentions = comp.Value.mentions;
            int compAvgPos = compMentions > 0 ? comp.Value.totalPosition / compMentions : 100;
            int compFreq = (int)Math.Round((double)compMentions / totalPlatforms * 100);
            int compVis = Math.Clamp((compFreq * 2) - (compAvgPos / 2), 0, 100);
            int compSov = totalMentionsOverall > 0 ? (int)Math.Round((double)compMentions / totalMentionsOverall * 100) : 0;

            compComparisons.Add(new CompetitorComparison
            {
                PromptAnalysisId = analysisId,
                CompetitorName = comp.Key,
                VisibilityScore = compVis,
                ShareOfVoice = compSov,
                MissingTopicsJson = "[\"Pricing Comparison\", \"API Documentation\"]" // Simulated AI insight
            });
        }

        return (visibility, mentions, compComparisons);
    }
}
