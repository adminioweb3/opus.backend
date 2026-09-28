using System.Text.Json;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Assistant.Agents;

public sealed record GeoStrategyRecommendationDraft(
    string RecommendationType,
    string Category,
    string Title,
    string Summary,
    string Rationale,
    string TargetType,
    string TargetKey,
    string ExpectedImpact,
    int ImpactScore,
    int EffortScore,
    int UrgencyScore,
    int GoalAlignmentScore,
    decimal Confidence,
    decimal PriorityScore,
    IReadOnlyList<string> ActionPlan,
    object ValidationPlan,
    string DeduplicationKey);

public static class GeoStrategyRecommendationBuilder
{
    public static GeoStrategyRecommendationDraft Build(AgentFinding finding, string primaryGoal = AgentStrategyGoals.Balanced)
    {
        var targetKey = ExtractTargetKey(finding.EntityIdsJson);
        var template = ResolveTemplate(finding.FindingType, finding.EntityType, targetKey);
        var impact = finding.Severity switch
        {
            "Critical" => 95,
            "High" => 82,
            "Medium" => 68,
            _ => 55
        };
        var urgency = finding.Severity switch
        {
            "Critical" => 100,
            "High" => 85,
            "Medium" => 60,
            _ => 40
        };
        var confidence = Math.Clamp(finding.Confidence, 0m, 1m);
        var goalAlignment = CalculateGoalAlignment(template.RecommendationType, primaryGoal);
        var priority = CalculatePriority(impact, template.EffortScore, urgency, goalAlignment, confidence);

        return new GeoStrategyRecommendationDraft(
            template.RecommendationType,
            template.Category,
            template.Title,
            $"Respond to the measured change: {finding.Summary}",
            $"This recommendation is grounded in finding {finding.Id} ({finding.Severity}, confidence {confidence:P0}).",
            finding.EntityType,
            targetKey,
            template.ExpectedImpact,
            impact,
            template.EffortScore,
            urgency,
            goalAlignment,
            confidence,
            priority,
            template.ActionPlan,
            new
            {
                metricSource = finding.FindingType,
                baselineEvidence = finding.EvidenceJson,
                measurementWindowDays = 14,
                successCondition = template.SuccessCondition,
                followUp = "Run the same Citationly scan and compare against the dated baseline."
            },
            $"strategy:{template.RecommendationType}:{Slug(finding.EntityType)}:{Slug(targetKey)}");
    }

    public static decimal CalculatePriority(
        int impactScore,
        int effortScore,
        int urgencyScore,
        int goalAlignmentScore,
        decimal confidence)
    {
        impactScore = Math.Clamp(impactScore, 0, 100);
        effortScore = Math.Clamp(effortScore, 0, 100);
        urgencyScore = Math.Clamp(urgencyScore, 0, 100);
        goalAlignmentScore = Math.Clamp(goalAlignmentScore, 0, 100);
        confidence = Math.Clamp(confidence, 0m, 1m);

        var score = impactScore * 0.30m
            + confidence * 100m * 0.25m
            + (100 - effortScore) * 0.15m
            + urgencyScore * 0.20m
            + goalAlignmentScore * 0.10m;
        return decimal.Round(score, 2);
    }

    public static int CalculateGoalAlignment(string recommendationType, string primaryGoal)
    {
        if (primaryGoal == AgentStrategyGoals.Balanced) return 70;

        var matches = primaryGoal switch
        {
            AgentStrategyGoals.GrowVisibility => recommendationType is "platform-visibility" or "visibility-recovery",
            AgentStrategyGoals.ImproveCitations => recommendationType == "citation-authority",
            AgentStrategyGoals.DefendCompetitors => recommendationType == "competitive-response",
            AgentStrategyGoals.ImproveBrandAccuracy => recommendationType == "brand-accuracy",
            _ => false
        };
        return matches ? 100 : 40;
    }

    private static RecommendationTemplate ResolveTemplate(string findingType, string entityType, string targetKey)
    {
        if (findingType.StartsWith("citations.", StringComparison.Ordinal))
        {
            return new(
                "citation-authority",
                "Authority",
                "Recover citation authority and model coverage",
                "Improve the citation signals that influence AI answer sourcing and brand references.",
                55,
                [
                    "Review the cited-source evidence and identify the largest lost or weak source category.",
                    "Strengthen the target page with verifiable facts, primary references, and clear entity attribution.",
                    "Pursue the highest-authority relevant source opportunity already identified by Citationly.",
                    "Re-run Citation Intelligence after the measurement window."
                ],
                "Citation quality, citation frequency, or model coverage returns toward or above the previous measured value.");
        }

        if (findingType.StartsWith("competitors.", StringComparison.Ordinal))
        {
            return new(
                "competitive-response",
                "Competitive GEO",
                $"Build a response to {DisplayTarget(targetKey)}",
                "Close the measured competitive visibility or share-of-voice gap.",
                65,
                [
                    "Compare the competitor's observed answer presence with the brand's current evidence.",
                    "Identify one topic or entity gap where the competitor is repeatedly preferred.",
                    "Prepare a differentiated, evidence-backed page or section addressing that gap.",
                    "Measure share of voice and competitive visibility on the same prompt set."
                ],
                "Brand share of voice improves and the measured competitor gap narrows on the same prompt panel.");
        }

        if (findingType.StartsWith("brand-pulse.", StringComparison.Ordinal))
        {
            return new(
                "brand-accuracy",
                "GEO Accuracy",
                "Correct brand messaging and accuracy signals",
                "Improve consistency and reduce negative or uncertain AI portrayals of the brand.",
                45,
                [
                    "Review the affected claims, themes, and platform evidence in Brand Pulse.",
                    "Confirm the canonical business fact in the Knowledge Vault.",
                    "Update the most authoritative owned page with explicit, verifiable language.",
                    "Re-run Brand Pulse and verify sentiment and confidence movement."
                ],
                "AI confidence or brand health improves, or negative sentiment returns below the measured baseline.");
        }

        var platformSpecific = string.Equals(entityType, "platform", StringComparison.OrdinalIgnoreCase);
        return new(
            platformSpecific ? "platform-visibility" : "visibility-recovery",
            "AI Visibility",
            platformSpecific
                ? $"Improve visibility on {DisplayTarget(targetKey)}"
                : "Recover overall AI visibility",
            "Address the measured loss in AI answer visibility with evidence-backed content and entity improvements.",
            platformSpecific ? 50 : 60,
            [
                "Inspect the affected prompts, pages, and citations behind the visibility change.",
                "Select the highest-confidence content or entity gap tied to the measured decline.",
                "Prepare a focused update with direct answers, factual support, and consistent entity language.",
                "Re-run Visibility Radar on the same platform and prompt set."
            ],
            "The affected visibility metric returns toward or above its previous dated value.");
    }

    private static string ExtractTargetKey(string entityIdsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(entityIdsJson);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var first = document.RootElement.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(first.GetString()))
                    return first.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Fall through to a stable generic target.
        }
        return "overall";
    }

    private static string DisplayTarget(string value) => value == "overall" ? "the leading competitor" : value;

    private static string Slug(string value)
    {
        var characters = value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        var parts = new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "overall" : string.Join('-', parts);
    }

    private sealed record RecommendationTemplate(
        string RecommendationType,
        string Category,
        string Title,
        string ExpectedImpact,
        int EffortScore,
        IReadOnlyList<string> ActionPlan,
        string SuccessCondition);
}
