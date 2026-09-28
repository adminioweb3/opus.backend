using Citationly.Application.Features.Assistant.Agents;
using Xunit;

namespace Citationly.Tests;

public sealed class AgentChangeDetectorTests
{
    [Fact]
    public void Evaluate_IgnoresNoiseBelowThreshold()
    {
        var result = AgentChangeDetector.Evaluate(
            "visibility", "Visibility", 70, 67, 5, AgentMetricDirection.LowerIsBad);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(70, 65, "Medium")]
    [InlineData(70, 60, "High")]
    [InlineData(70, 55, "Critical")]
    public void Evaluate_GradesAdverseDropsByThresholdMultiple(decimal previous, decimal current, string severity)
    {
        var result = AgentChangeDetector.Evaluate(
            "visibility", "Visibility", previous, current, 5, AgentMetricDirection.LowerIsBad);

        Assert.NotNull(result);
        Assert.True(result.IsAdverse);
        Assert.Equal(severity, result.Severity);
        Assert.Equal(current - previous, result.Delta);
    }

    [Fact]
    public void Evaluate_TreatsNegativeSentimentIncreaseAsAdverse()
    {
        var result = AgentChangeDetector.Evaluate(
            "negative-sentiment", "Negative sentiment", 8, 19, 5, AgentMetricDirection.HigherIsBad);

        Assert.NotNull(result);
        Assert.True(result.IsAdverse);
        Assert.Equal("High", result.Severity);
    }

    [Fact]
    public void Evaluate_ReportsOnlySubstantialImprovementsAsGood()
    {
        var smallImprovement = AgentChangeDetector.Evaluate(
            "visibility", "Visibility", 50, 56, 5, AgentMetricDirection.LowerIsBad);
        var substantialImprovement = AgentChangeDetector.Evaluate(
            "visibility", "Visibility", 50, 61, 5, AgentMetricDirection.LowerIsBad);

        Assert.Null(smallImprovement);
        Assert.NotNull(substantialImprovement);
        Assert.False(substantialImprovement.IsAdverse);
        Assert.Equal("Good", substantialImprovement.Severity);
    }

    [Fact]
    public void Evaluate_RejectsNonPositiveThresholds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentChangeDetector.Evaluate(
            "visibility", "Visibility", 50, 40, 0, AgentMetricDirection.LowerIsBad));
    }

    [Fact]
    public void EvaluateRollingAnomaly_DetectsAdverseOutlierAgainstStableHistory()
    {
        var result = AgentChangeDetector.EvaluateRollingAnomaly(
            "visibility",
            "Visibility",
            previousValue: 72,
            currentValue: 62,
            baselineValues: new decimal[] { 68, 71, 70, 72, 69, 71 },
            AgentMetricDirection.LowerIsBad);

        Assert.NotNull(result);
        Assert.True(result.IsAdverse);
        Assert.Equal("rolling-zscore", result.DetectionRule);
        Assert.NotNull(result.BaselineMean);
        Assert.NotNull(result.BaselineStandardDeviation);
    }

    [Fact]
    public void EvaluateRollingAnomaly_RequiresEnoughBaselineSamples()
    {
        var result = AgentChangeDetector.EvaluateRollingAnomaly(
            "visibility",
            "Visibility",
            previousValue: 70,
            currentValue: 40,
            baselineValues: new decimal[] { 69, 70, 71 },
            AgentMetricDirection.LowerIsBad);

        Assert.Null(result);
    }
}
