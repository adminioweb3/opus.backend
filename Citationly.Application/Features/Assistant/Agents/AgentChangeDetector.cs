namespace Citationly.Application.Features.Assistant.Agents;

public enum AgentMetricDirection
{
    LowerIsBad,
    HigherIsBad
}

public sealed record AgentMetricChange(
    string MetricKey,
    string Label,
    decimal PreviousValue,
    decimal CurrentValue,
    decimal Delta,
    decimal Threshold,
    string Severity,
    bool IsAdverse,
    string DetectionRule = "threshold",
    decimal? BaselineMean = null,
    decimal? BaselineStandardDeviation = null);

public static class AgentChangeDetector
{
    public static AgentMetricChange? Evaluate(
        string metricKey,
        string label,
        decimal previousValue,
        decimal currentValue,
        decimal threshold,
        AgentMetricDirection direction)
    {
        if (threshold <= 0) throw new ArgumentOutOfRangeException(nameof(threshold));

        var delta = currentValue - previousValue;
        var adverseMagnitude = direction == AgentMetricDirection.LowerIsBad ? -delta : delta;
        var improvementMagnitude = -adverseMagnitude;

        if (adverseMagnitude >= threshold)
        {
            var severity = adverseMagnitude >= threshold * 3
                ? "Critical"
                : adverseMagnitude >= threshold * 2
                    ? "High"
                    : "Medium";
            return new AgentMetricChange(metricKey, label, previousValue, currentValue, delta, threshold, severity, true);
        }

        if (improvementMagnitude >= threshold * 2)
        {
            return new AgentMetricChange(metricKey, label, previousValue, currentValue, delta, threshold, "Good", false);
        }

        return null;
    }

    public static AgentMetricChange? EvaluateRollingAnomaly(
        string metricKey,
        string label,
        decimal previousValue,
        decimal currentValue,
        IReadOnlyCollection<decimal> baselineValues,
        AgentMetricDirection direction,
        decimal zScoreThreshold = 2m,
        int minimumSamples = 5)
    {
        if (zScoreThreshold <= 0) throw new ArgumentOutOfRangeException(nameof(zScoreThreshold));
        if (minimumSamples < 2) throw new ArgumentOutOfRangeException(nameof(minimumSamples));
        if (baselineValues.Count < minimumSamples) return null;

        var mean = baselineValues.Average();
        var variance = baselineValues.Sum(value => (value - mean) * (value - mean)) / baselineValues.Count;
        var standardDeviation = (decimal)Math.Sqrt((double)variance);
        if (standardDeviation < 1m) return null;

        var zScore = (currentValue - mean) / standardDeviation;
        var adverseZScore = direction == AgentMetricDirection.LowerIsBad ? -zScore : zScore;
        if (adverseZScore < zScoreThreshold) return null;

        return new AgentMetricChange(
            metricKey,
            label,
            previousValue,
            currentValue,
            currentValue - previousValue,
            zScoreThreshold,
            adverseZScore >= 3m ? "Critical" : "High",
            true,
            "rolling-zscore",
            decimal.Round(mean, 4),
            decimal.Round(standardDeviation, 4));
    }
}
