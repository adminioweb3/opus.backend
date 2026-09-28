using System.Text.Json;
using Citationly.Application.Features.Assistant.Agents;
using Citationly.Domain.Entities;
using Xunit;

namespace Citationly.Tests;

public sealed class GeoStrategyRecommendationBuilderTests
{
    [Fact]
    public void Build_MapsCitationFindingToTypedAuthorityRecommendation()
    {
        var finding = Finding("citations.metric-change", "citations", "quality", "High", 0.9m);

        var draft = GeoStrategyRecommendationBuilder.Build(finding);

        Assert.Equal("citation-authority", draft.RecommendationType);
        Assert.Equal("Authority", draft.Category);
        Assert.Equal("quality", draft.TargetKey);
        Assert.Equal(4, draft.ActionPlan.Count);
        Assert.InRange(draft.PriorityScore, 0m, 100m);
        Assert.Contains("citation-authority", draft.DeduplicationKey);
    }

    [Fact]
    public void Build_UsesStableTargetBasedDeduplicationAcrossFindings()
    {
        var first = Finding("visibility.metric-change", "platform", "ChatGPT", "High", 1m);
        var second = Finding("visibility.metric-change", "platform", "ChatGPT", "Critical", 1m);
        second.Id = Guid.NewGuid();

        var firstDraft = GeoStrategyRecommendationBuilder.Build(first);
        var secondDraft = GeoStrategyRecommendationBuilder.Build(second);

        Assert.Equal(firstDraft.DeduplicationKey, secondDraft.DeduplicationKey);
        Assert.True(secondDraft.PriorityScore > firstDraft.PriorityScore);
    }

    [Fact]
    public void CalculatePriority_RewardsImpactConfidenceUrgencyAndLowEffort()
    {
        var lowPriority = GeoStrategyRecommendationBuilder.CalculatePriority(50, 80, 40, 50, 0.5m);
        var highPriority = GeoStrategyRecommendationBuilder.CalculatePriority(90, 30, 90, 90, 0.95m);

        Assert.True(highPriority > lowPriority);
        Assert.InRange(highPriority, 0m, 100m);
    }

    [Fact]
    public void Build_UsesClientSelectedGoalInPriorityScore()
    {
        var finding = Finding("citations.metric-change", "citations", "quality", "High", 0.9m);

        var citationGoal = GeoStrategyRecommendationBuilder.Build(finding, AgentStrategyGoals.ImproveCitations);
        var visibilityGoal = GeoStrategyRecommendationBuilder.Build(finding, AgentStrategyGoals.GrowVisibility);

        Assert.Equal(100, citationGoal.GoalAlignmentScore);
        Assert.Equal(40, visibilityGoal.GoalAlignmentScore);
        Assert.True(citationGoal.PriorityScore > visibilityGoal.PriorityScore);
    }

    private static AgentFinding Finding(string type, string entityType, string target, string severity, decimal confidence) => new()
    {
        Id = Guid.NewGuid(),
        FindingType = type,
        EntityType = entityType,
        EntityIdsJson = JsonSerializer.Serialize(new[] { target }),
        Severity = severity,
        Confidence = confidence,
        Summary = "Measured signal declined against its previous dated baseline.",
        EvidenceJson = "{}"
    };
}
