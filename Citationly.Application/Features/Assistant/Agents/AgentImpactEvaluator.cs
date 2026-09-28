namespace Citationly.Application.Features.Assistant.Agents;

public sealed record AgentImpactSnapshot(
    DateOnly? VisibilityObservedAt,
    int? VisibilityScore,
    DateOnly? CitationObservedAt,
    int? CitationQuality,
    int? CitationSignal,
    DateOnly? CompetitorObservedAt,
    int? ShareOfVoice,
    int? AveragePosition,
    int? CitationCount,
    DateOnly? BrandObservedAt,
    int? BrandHealth);

public sealed record AgentImpactDelta(
    int? VisibilityScore,
    int? CitationQuality,
    int? CitationSignal,
    int? ShareOfVoice,
    int? AveragePosition,
    int? CitationCount,
    int? BrandHealth);

public sealed record AgentImpactEvaluation(
    string Outcome,
    decimal Confidence,
    int ComparableMetricCount,
    int DirectionalScore,
    AgentImpactDelta Delta,
    string Summary);

public static class AgentImpactEvaluator
{
    public static bool HasBaseline(AgentImpactSnapshot snapshot) =>
        snapshot.VisibilityScore.HasValue ||
        snapshot.CitationQuality.HasValue ||
        snapshot.CitationSignal.HasValue ||
        snapshot.ShareOfVoice.HasValue ||
        snapshot.AveragePosition.HasValue ||
        snapshot.CitationCount.HasValue ||
        snapshot.BrandHealth.HasValue;

    public static bool HasRelevantMetric(string recommendationType, AgentImpactSnapshot snapshot) =>
        recommendationType switch
        {
            "citation-authority" => snapshot.CitationQuality.HasValue || snapshot.CitationSignal.HasValue || snapshot.CitationCount.HasValue,
            "competitive-response" => snapshot.ShareOfVoice.HasValue || snapshot.AveragePosition.HasValue,
            "brand-accuracy" => snapshot.BrandHealth.HasValue,
            _ => snapshot.VisibilityScore.HasValue
        };

    public static AgentImpactEvaluation Evaluate(AgentImpactSnapshot baseline, AgentImpactSnapshot followup)
    {
        var delta = new AgentImpactDelta(
            Difference(baseline.VisibilityScore, followup.VisibilityScore),
            Difference(baseline.CitationQuality, followup.CitationQuality),
            Difference(baseline.CitationSignal, followup.CitationSignal),
            Difference(baseline.ShareOfVoice, followup.ShareOfVoice),
            PositionImprovement(baseline.AveragePosition, followup.AveragePosition),
            Difference(baseline.CitationCount, followup.CitationCount),
            Difference(baseline.BrandHealth, followup.BrandHealth));

        var comparable = new int?[]
        {
            delta.VisibilityScore,
            delta.CitationQuality,
            delta.CitationSignal,
            delta.ShareOfVoice,
            delta.AveragePosition,
            delta.CitationCount,
            delta.BrandHealth
        }.Count(value => value.HasValue);
        if (comparable == 0)
        {
            return new AgentImpactEvaluation(
                "Inconclusive", 0m, 0, 0, delta,
                "No comparable post-implementation snapshot was available.");
        }

        var score =
            Direction(delta.VisibilityScore, minimumAbsoluteChange: 3) +
            Direction(delta.CitationQuality, minimumAbsoluteChange: 3) +
            Direction(delta.CitationSignal, minimumAbsoluteChange: 2) +
            Direction(delta.ShareOfVoice, minimumAbsoluteChange: 2) +
            Direction(delta.AveragePosition, minimumAbsoluteChange: 1) +
            Direction(delta.CitationCount, minimumAbsoluteChange: 1, weight: 2) +
            Direction(delta.BrandHealth, minimumAbsoluteChange: 3);
        var outcome = score >= 2 ? "Improved" : score <= -2 ? "Regressed" : "Neutral";
        var confidence = Math.Clamp(comparable / 7m, 0.15m, 1m);
        var changed = DescribeChangedMetrics(delta);

        return new AgentImpactEvaluation(
            outcome,
            decimal.Round(confidence, 4),
            comparable,
            score,
            delta,
            $"{outcome} across {comparable} comparable metric(s){changed}. This is an observed before/after association, not proof that the recommendation caused the change.");
    }

    private static int? Difference(int? baseline, int? followup) =>
        baseline.HasValue && followup.HasValue ? followup.Value - baseline.Value : null;

    private static int? PositionImprovement(int? baseline, int? followup) =>
        baseline.HasValue && followup.HasValue ? baseline.Value - followup.Value : null;

    private static int Direction(int? value, int minimumAbsoluteChange, int weight = 1) =>
        !value.HasValue || Math.Abs(value.Value) < minimumAbsoluteChange
            ? 0
            : value.Value > 0 ? weight : -weight;

    private static string DescribeChangedMetrics(AgentImpactDelta delta)
    {
        var changes = new List<string>();
        Add(changes, "visibility", delta.VisibilityScore);
        Add(changes, "citation quality", delta.CitationQuality);
        Add(changes, "citation signal", delta.CitationSignal);
        Add(changes, "share of voice", delta.ShareOfVoice);
        Add(changes, "average position", delta.AveragePosition);
        Add(changes, "citations", delta.CitationCount);
        Add(changes, "brand health", delta.BrandHealth);
        return changes.Count == 0 ? string.Empty : $": {string.Join(", ", changes)}";
    }

    private static void Add(ICollection<string> changes, string label, int? value)
    {
        if (!value.HasValue || value.Value == 0) return;
        changes.Add($"{label} {(value.Value > 0 ? "+" : string.Empty)}{value.Value}");
    }
}
