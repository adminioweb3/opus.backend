namespace Citationly.Application.Features.Competitors;

public sealed record CompetitorEvidenceInput(
    Guid? CompetitorId,
    bool IsYou,
    string Name,
    string? WebsiteUrl,
    IReadOnlyList<int> MentionPositions,
    IReadOnlyList<int> RecommendationPositions,
    int CitationCount);

public sealed record CompetitorEvidenceScore(
    Guid? CompetitorId,
    bool IsYou,
    string Name,
    string? WebsiteUrl,
    int Score,
    int ShareOfVoice,
    int MentionCount,
    int RecommendationCount,
    int ResponseCount,
    int CitationCount,
    int AveragePosition);

public static class CompetitorEvidenceScorer
{
    public const string MethodologyVersion = "openai-observed-v3-unranked-zero-evidence";

    public static IReadOnlyList<CompetitorEvidenceScore> Score(
        IReadOnlyList<CompetitorEvidenceInput> entities,
        int responseCount)
    {
        if (entities.Count == 0) return Array.Empty<CompetitorEvidenceScore>();

        responseCount = Math.Max(0, responseCount);
        var totalMentions = entities.Sum(e => e.MentionPositions.Count);
        var totalCitations = entities.Sum(e => Math.Max(0, e.CitationCount));

        var raw = entities.Select(entity =>
        {
            var mentionCount = entity.MentionPositions.Count;
            var mentionRate = responseCount == 0 ? 0 : mentionCount * 100d / responseCount;
            var recommendationCount = entity.RecommendationPositions.Count;
            var recommendationRate = responseCount == 0 ? 0 : recommendationCount * 100d / responseCount;
            var averagePosition = mentionCount == 0
                ? 100
                : (int)Math.Round(entity.MentionPositions.Average(position => Math.Clamp(position, 0, 100)));
            var prominence = mentionCount == 0 ? 0 : 100 - averagePosition;
            var citationShare = totalCitations == 0 ? 0 : Math.Max(0, entity.CitationCount) * 100d / totalCitations;

            // Every component is derived from stored OpenAI responses. When the response panel has
            // no tracked-domain citations, redistribute that unavailable signal instead of giving
            // every company a hidden ten-point penalty.
            var score = totalCitations == 0
                ? (int)Math.Round((mentionRate * 0.60) + (recommendationRate * 0.30) + (prominence * 0.10))
                : (int)Math.Round((mentionRate * 0.55) + (recommendationRate * 0.25) + (prominence * 0.10) + (citationShare * 0.10));
            var shareOfVoice = totalMentions == 0 ? 0 : (int)Math.Round(mentionCount * 100d / totalMentions);

            return new CompetitorEvidenceScore(
                entity.CompetitorId,
                entity.IsYou,
                entity.Name,
                entity.WebsiteUrl,
                Math.Clamp(score, 0, 100),
                shareOfVoice,
                mentionCount,
                recommendationCount,
                responseCount,
                Math.Max(0, entity.CitationCount),
                averagePosition);
        }).ToList();

        CorrectRoundingDrift(raw, score => score.ShareOfVoice, (score, value) => score with { ShareOfVoice = value });
        return raw
            .OrderByDescending(score => score.Score)
            .ThenByDescending(score => score.MentionCount)
            .ThenBy(score => score.AveragePosition)
            .ToList();
    }

    private static void CorrectRoundingDrift(
        List<CompetitorEvidenceScore> scores,
        Func<CompetitorEvidenceScore, int> selector,
        Func<CompetitorEvidenceScore, int, CompetitorEvidenceScore> update)
    {
        if (scores.Sum(score => score.MentionCount) == 0) return;

        var drift = 100 - scores.Sum(selector);
        if (drift == 0) return;

        var index = scores.FindIndex(score => score.MentionCount == scores.Max(item => item.MentionCount));
        scores[index] = update(scores[index], selector(scores[index]) + drift);
    }
}
