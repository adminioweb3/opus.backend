using Citationly.Application.Features.Assistant.Agents;
using Xunit;

namespace Citationly.Tests;

public sealed class AgentImpactEvaluatorTests
{
    [Fact]
    public void Evaluate_ClassifiesBroadPositiveMovementAsImproved()
    {
        var baseline = Snapshot(visibility: 50, citations: 8, shareOfVoice: 20, position: 8, brand: 60);
        var followup = Snapshot(visibility: 57, citations: 11, shareOfVoice: 24, position: 5, brand: 64);

        var result = AgentImpactEvaluator.Evaluate(baseline, followup);

        Assert.Equal("Improved", result.Outcome);
        Assert.Equal(3, result.Delta.AveragePosition);
        Assert.True(result.Confidence > 0.5m);
        Assert.Contains("not proof", result.Summary);
    }

    [Fact]
    public void Evaluate_TreatsHigherAveragePositionNumberAsRegression()
    {
        var baseline = Snapshot(visibility: 50, citations: 8, shareOfVoice: 20, position: 3, brand: 60);
        var followup = Snapshot(visibility: 49, citations: 7, shareOfVoice: 18, position: 9, brand: 59);

        var result = AgentImpactEvaluator.Evaluate(baseline, followup);

        Assert.Equal("Regressed", result.Outcome);
        Assert.Equal(-6, result.Delta.AveragePosition);
    }

    [Fact]
    public void Evaluate_IsInconclusiveWithoutComparableMetrics()
    {
        var empty = new AgentImpactSnapshot(null, null, null, null, null, null, null, null, null, null, null);

        var result = AgentImpactEvaluator.Evaluate(empty, empty);

        Assert.Equal("Inconclusive", result.Outcome);
        Assert.Equal(0m, result.Confidence);
        Assert.Equal(0, result.ComparableMetricCount);
    }

    [Fact]
    public void Evaluate_TreatsSmallMixedMovementAsNeutral()
    {
        var baseline = Snapshot(visibility: 50, citations: 8, shareOfVoice: 20, position: 5, brand: 60);
        var followup = Snapshot(visibility: 51, citations: 8, shareOfVoice: 21, position: 5, brand: 59);

        var result = AgentImpactEvaluator.Evaluate(baseline, followup);

        Assert.Equal("Neutral", result.Outcome);
        Assert.Equal(0, result.DirectionalScore);
    }

    [Theory]
    [InlineData("citation-authority", true)]
    [InlineData("competitive-response", false)]
    [InlineData("brand-accuracy", false)]
    [InlineData("visibility-recovery", false)]
    public void HasRelevantMetric_UsesRecommendationSpecificSignal(string type, bool expected)
    {
        var citationOnly = new AgentImpactSnapshot(
            null, null,
            new DateOnly(2026, 9, 25), 70, 12,
            null, null, null, null,
            null, null);

        Assert.Equal(expected, AgentImpactEvaluator.HasRelevantMetric(type, citationOnly));
    }

    private static AgentImpactSnapshot Snapshot(
        int visibility,
        int citations,
        int shareOfVoice,
        int position,
        int brand) =>
        new(
            new DateOnly(2026, 9, 1), visibility,
            new DateOnly(2026, 9, 1), citations * 5, citations,
            new DateOnly(2026, 9, 1), shareOfVoice, position, citations,
            new DateOnly(2026, 9, 1), brand);
}
