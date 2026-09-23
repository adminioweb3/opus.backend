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
    public const string MethodologyVersion = "openai-observed-v5-mention-rate-ranking";

    public static IReadOnlyList<CompetitorEvidenceScore> Score(
        IReadOnlyList<CompetitorEvidenceInput> entities,
        int responseCount)
    {
        if (entities.Count == 0) return Array.Empty<CompetitorEvidenceScore>();

        responseCount = Math.Max(0, responseCount);
        var totalMentions = entities.Sum(e => e.MentionPositions.Count);
        var raw = entities.Select(entity =>
        {
            var mentionCount = entity.MentionPositions.Count;
            var mentionRate = responseCount == 0 ? 0 : mentionCount * 100d / responseCount;
            var recommendationCount = entity.RecommendationPositions.Count;
            var averagePosition = mentionCount == 0
                ? 100
                : (int)Math.Round(entity.MentionPositions.Average(position => Math.Clamp(position, 0, 100)));

            // Rank by one client-auditable signal: the percentage of captured responses that
            // mention the entity. Recommendation, position, citation, and share-of-voice metrics
            // remain available as separate evidence and cannot invisibly boost the rank.
            var score = (int)Math.Round(mentionRate);
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
