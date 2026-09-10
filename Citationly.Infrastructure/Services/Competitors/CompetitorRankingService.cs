using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Competitors;
using Citationly.Application.Features.Competitors;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Competitors;

/// <summary>
/// Compatibility projection over the measured Competitor Watch snapshot. No AI calls and no
/// neutral category defaults: unsupported legacy categories remain zero.
/// </summary>
public class CompetitorRankingService : ICompetitorRankingService
{
    private readonly ICompetitorSnapshotRepository _snapshotRepository;

    public CompetitorRankingService(ICompetitorSnapshotRepository snapshotRepository)
    {
        _snapshotRepository = snapshotRepository;
    }

    public async Task<CompetitorRankingResult> ComputeRankingsAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var latestDate = await _snapshotRepository.GetLatestScanDateAsync(organizationId);
        if (!latestDate.HasValue) return new CompetitorRankingResult();

        var snapshots = (await _snapshotRepository.GetSnapshotsByScanDateAsync(organizationId, latestDate.Value))
            .Where(snapshot => snapshot.MeasurementSource == "openai-observed" &&
                               snapshot.MethodologyVersion == CompetitorEvidenceScorer.MethodologyVersion)
            .OrderBy(snapshot => snapshot.Rank == 0 ? int.MaxValue : snapshot.Rank)
            .ToList();
        var user = snapshots.FirstOrDefault(snapshot => snapshot.IsYou);
        if (user == null) return new CompetitorRankingResult();

        var competitors = snapshots.Where(snapshot => !snapshot.IsYou).ToList();
        var averageScore = snapshots.Average(snapshot => snapshot.Visibility);
        var leader = snapshots.Where(snapshot => snapshot.Rank > 0)
            .OrderBy(snapshot => snapshot.Rank)
            .FirstOrDefault();
        var topCompetitor = competitors.OrderByDescending(snapshot => snapshot.Visibility).FirstOrDefault();
        var closestCompetitor = competitors
            .OrderBy(snapshot => Math.Abs(snapshot.Visibility - user.Visibility))
            .FirstOrDefault();

        var metrics = BuildMetrics(snapshots);
        var categoryRankings = metrics.Select(metric => BuildCategoryRanking(metric, snapshots, user)).ToList();
        var biggestOpportunity = categoryRankings
            .Where(category => category.GapToLeader > 0)
            .OrderByDescending(category => category.GapToLeader)
            .FirstOrDefault();

        var userRank = user.Rank;
        return new CompetitorRankingResult
        {
            OverallRank = userRank,
            TotalCompanies = snapshots.Count,
            Percentile = userRank == 0
                ? 0
                : Math.Round((1d - (double)(userRank - 1) / snapshots.Count) * 100, 1),
            OverallScore = user.Visibility,
            DifferenceFromLeader = leader == null ? 0 : leader.Visibility - user.Visibility,
            DifferenceFromAverage = Math.Round(averageScore - user.Visibility, 1),
            TopCompetitor = topCompetitor?.Name,
            MostSimilarCompetitor = null,
            ClosestCompetitor = closestCompetitor?.Name,
            CompaniesOutperformed = userRank == 0 ? 0 : snapshots.Count - userRank,
            CompaniesAhead = userRank == 0 ? 0 : userRank - 1,
            BiggestOpportunity = biggestOpportunity == null
                ? null
                : $"{biggestOpportunity.Category} (gap: {biggestOpportunity.GapToLeader:F0})",
            EstimatedImprovementPotential = 0,
            QuickWins = categoryRankings
                .Where(category => category.GapToAverage > 0 && category.GapToAverage <= 15)
                .Select(category => $"Improve measured {category.Category} by {category.GapToAverage:F0} points to reach the tracked average")
                .ToList(),
            CategoryRankings = categoryRankings,
            GapAnalysis = BuildGapAnalysis(categoryRankings),
            StrengthAnalysis = BuildStrengthAnalysis(categoryRankings),
            RadarChart = categoryRankings.Select(category => new RadarChartEntry
            {
                Category = category.Category,
                UserScore = category.UserScore,
                IndustryAverage = Math.Round(category.UserScore + category.GapToAverage, 1),
                LeaderScore = category.LeaderScore
            }).ToList(),
            Leaderboard = snapshots.Select(snapshot => new LeaderboardEntry
            {
                Rank = snapshot.Rank,
                CompanyName = snapshot.Name,
                OverallScore = snapshot.Visibility,
                IsUser = snapshot.IsYou
            }).ToList(),
            ScoreComparison = snapshots.Select(snapshot => new ScoreComparisonEntry
            {
                CompanyName = snapshot.Name,
                SEO = 0,
                Content = 0,
                Trust = 0,
                Authority = 0,
                AIVisibility = snapshot.Visibility,
                Citation = Percentage(snapshot.CitationCount, snapshots.Sum(item => item.CitationCount)),
                IsUser = snapshot.IsYou
            }).ToList()
        };
    }

    private static List<MeasuredMetric> BuildMetrics(IReadOnlyCollection<CompetitorSnapshot> snapshots)
    {
        var totalCitations = snapshots.Sum(snapshot => snapshot.CitationCount);
        return new List<MeasuredMetric>
        {
            new("OpenAI Visibility", snapshot => snapshot.Visibility),
            new("Share of Voice", snapshot => snapshot.ShareOfVoice),
            new("Mention Rate", snapshot => Percentage(snapshot.MentionCount, snapshot.ResponseCount)),
            new("Recommendation Rate", snapshot => Percentage(snapshot.RecommendationCount, snapshot.ResponseCount)),
            new("Citation Share", snapshot => Percentage(snapshot.CitationCount, totalCitations))
        };
    }

    private static CategoryRanking BuildCategoryRanking(
        MeasuredMetric metric,
        IReadOnlyCollection<CompetitorSnapshot> snapshots,
        CompetitorSnapshot user)
    {
        var ranked = snapshots.OrderByDescending(metric.Value).ToList();
        var userScore = metric.Value(user);
        var leader = ranked[0];
        var average = snapshots.Average(metric.Value);
        var hasMeasuredValue = ranked.Any(snapshot => metric.Value(snapshot) > 0);
        return new CategoryRanking
        {
            Category = metric.Name,
            UserRank = hasMeasuredValue ? 1 + ranked.Count(snapshot => metric.Value(snapshot) > userScore) : 0,
            UserScore = userScore,
            Leader = hasMeasuredValue ? leader.Name : "No observed leader",
            LeaderScore = metric.Value(leader),
            GapToLeader = metric.Value(leader) - userScore,
            GapToAverage = Math.Round(average - userScore, 1)
        };
    }

    private static CompetitiveGapAnalysis BuildGapAnalysis(IEnumerable<CategoryRanking> categories)
    {
        var gaps = categories
            .Where(category => category.GapToLeader > 0)
            .OrderByDescending(category => category.GapToLeader)
            .Select(category => $"{category.Category}: {category.GapToLeader:F0} measured points behind {category.Leader}")
            .ToList();

        return new CompetitiveGapAnalysis
        {
            AIVisibilityGaps = gaps,
            Recommendations = gaps.Take(3).Select(gap => $"Close {gap}").ToList()
        };
    }

    private static StrengthAnalysis BuildStrengthAnalysis(IEnumerable<CategoryRanking> categories)
    {
        var ordered = categories.OrderByDescending(category => category.UserScore).ToList();
        return new StrengthAnalysis
        {
            TopStrengths = ordered.Take(3).Select(category => $"{category.Category}: {category.UserScore:F0}/100").ToList(),
            TopWeaknesses = ordered.TakeLast(3).Select(category => $"{category.Category}: {category.UserScore:F0}/100").ToList(),
            CompetitiveAdvantages = ordered
                .Where(category => category.GapToAverage < 0)
                .Select(category => $"{category.Category}: {-category.GapToAverage:F0} points above tracked average")
                .ToList(),
            CompetitiveDisadvantages = ordered
                .Where(category => category.GapToAverage > 0)
                .Select(category => $"{category.Category}: {category.GapToAverage:F0} points below tracked average")
                .ToList()
        };
    }

    private static double Percentage(int numerator, int denominator) =>
        denominator <= 0 ? 0 : Math.Round(numerator * 100d / denominator, 1);

    private sealed record MeasuredMetric(string Name, Func<CompetitorSnapshot, double> Value);
}
