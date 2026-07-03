using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Competitors;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Competitors;

/// <summary>
/// Deterministic C# ranking engine. Zero AI calls.
/// Parses enriched competitor data and computes weighted composite scores.
/// </summary>
public class CompetitorRankingService : ICompetitorRankingService
{
    private readonly IWebsiteRepository _websiteRepository;

    // Configurable weights (sum = 1.0)
    private static readonly Dictionary<string, double> Weights = new()
    {
        ["SEO"] = 0.15,
        ["Content"] = 0.12,
        ["Trust"] = 0.12,
        ["Authority"] = 0.13,
        ["AIVisibility"] = 0.15,
        ["Citation"] = 0.10,
        ["GEO"] = 0.08,
        ["Technology"] = 0.05,
        ["TopicalAuthority"] = 0.05,
        ["BusinessCompleteness"] = 0.05
    };

    public CompetitorRankingService(IWebsiteRepository websiteRepository)
    {
        _websiteRepository = websiteRepository;
    }

    public async Task<CompetitorRankingResult> ComputeRankingsAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var competitors = (await _websiteRepository.GetCompetitorsAsync(organizationId)).ToList();
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(organizationId);

        // Parse scores for all enriched competitors
        var scoredEntries = new List<ScoredCompany>();

        foreach (var comp in competitors)
        {
            var scores = ExtractCategoryScores(comp.EnrichedJson ?? comp.RawJson);
            var overall = ComputeWeightedScore(scores);
            scoredEntries.Add(new ScoredCompany
            {
                Name = comp.Name,
                IsUser = false,
                CompetitorId = comp.Id,
                CategoryScores = scores,
                OverallScore = overall
            });
        }

        // Insert the user's own business
        var userScores = ExtractUserScores(profile?.RawProfileJson);
        var userOverall = ComputeWeightedScore(userScores);
        var userEntry = new ScoredCompany
        {
            Name = profile?.BusinessName ?? "Your Business",
            IsUser = true,
            CategoryScores = userScores,
            OverallScore = userOverall
        };
        scoredEntries.Add(userEntry);

        // Sort by overall score descending
        var ranked = scoredEntries.OrderByDescending(s => s.OverallScore).ToList();
        int userRank = ranked.FindIndex(s => s.IsUser) + 1;
        double leaderScore = ranked.First().OverallScore;
        double avgScore = ranked.Average(s => s.OverallScore);

        // Category rankings
        var categories = Weights.Keys.ToList();
        var categoryRankings = categories.Select(cat =>
        {
            var catRanked = ranked.OrderByDescending(s => s.CategoryScores.GetValueOrDefault(cat, 0)).ToList();
            int catUserRank = catRanked.FindIndex(s => s.IsUser) + 1;
            var leader = catRanked.First();
            double catAvg = catRanked.Average(s => s.CategoryScores.GetValueOrDefault(cat, 0));
            return new CategoryRanking
            {
                Category = cat,
                UserRank = catUserRank,
                UserScore = userScores.GetValueOrDefault(cat, 0),
                Leader = leader.Name,
                LeaderScore = leader.CategoryScores.GetValueOrDefault(cat, 0),
                GapToLeader = leader.CategoryScores.GetValueOrDefault(cat, 0) - userScores.GetValueOrDefault(cat, 0),
                GapToAverage = catAvg - userScores.GetValueOrDefault(cat, 0)
            };
        }).ToList();

        // Gap analysis
        var gapAnalysis = ComputeGapAnalysis(categoryRankings, userScores);

        // Strength analysis
        var strengthAnalysis = ComputeStrengthAnalysis(categoryRankings, userScores);

        // Find special competitors
        var mostSimilar = competitors.OrderByDescending(c => c.SimilarityScore).FirstOrDefault();
        var closestByScore = scoredEntries
            .Where(s => !s.IsUser)
            .OrderBy(s => Math.Abs(s.OverallScore - userOverall))
            .FirstOrDefault();

        // Quick wins: categories where user is close to average but below
        var quickWins = categoryRankings
            .Where(cr => cr.GapToAverage > 0 && cr.GapToAverage < 15)
            .Select(cr => $"Improve {cr.Category} by {cr.GapToAverage:F0} points to match industry average")
            .Take(5)
            .ToList();

        // Chart data
        var radarChart = categories.Select(cat => new RadarChartEntry
        {
            Category = cat,
            UserScore = userScores.GetValueOrDefault(cat, 0),
            IndustryAverage = ranked.Where(s => !s.IsUser).Average(s => s.CategoryScores.GetValueOrDefault(cat, 0)),
            LeaderScore = ranked.Max(s => s.CategoryScores.GetValueOrDefault(cat, 0))
        }).ToList();

        var leaderboard = ranked.Select((s, i) => new LeaderboardEntry
        {
            Rank = i + 1,
            CompanyName = s.Name,
            OverallScore = Math.Round(s.OverallScore, 1),
            IsUser = s.IsUser
        }).ToList();

        var scoreComparison = ranked.Take(11).Select(s => new ScoreComparisonEntry
        {
            CompanyName = s.Name,
            SEO = s.CategoryScores.GetValueOrDefault("SEO", 0),
            Content = s.CategoryScores.GetValueOrDefault("Content", 0),
            Trust = s.CategoryScores.GetValueOrDefault("Trust", 0),
            Authority = s.CategoryScores.GetValueOrDefault("Authority", 0),
            AIVisibility = s.CategoryScores.GetValueOrDefault("AIVisibility", 0),
            Citation = s.CategoryScores.GetValueOrDefault("Citation", 0),
            IsUser = s.IsUser
        }).ToList();

        // Biggest opportunity
        var biggestOp = categoryRankings.OrderByDescending(cr => cr.GapToLeader).FirstOrDefault();

        return new CompetitorRankingResult
        {
            OverallRank = userRank,
            TotalCompanies = ranked.Count,
            Percentile = Math.Round((1.0 - (double)(userRank - 1) / ranked.Count) * 100, 1),
            OverallScore = Math.Round(userOverall, 1),
            DifferenceFromLeader = Math.Round(leaderScore - userOverall, 1),
            DifferenceFromAverage = Math.Round(avgScore - userOverall, 1),
            TopCompetitor = ranked.First(s => !s.IsUser).Name,
            MostSimilarCompetitor = mostSimilar?.Name,
            ClosestCompetitor = closestByScore?.Name,
            CompaniesOutperformed = ranked.Count - userRank,
            CompaniesAhead = userRank - 1,
            BiggestOpportunity = biggestOp != null ? $"{biggestOp.Category} (gap: {biggestOp.GapToLeader:F0})" : null,
            EstimatedImprovementPotential = Math.Round(categoryRankings.Sum(cr => Math.Max(0, cr.GapToAverage)) * 0.5, 1),
            QuickWins = quickWins,
            CategoryRankings = categoryRankings,
            GapAnalysis = gapAnalysis,
            StrengthAnalysis = strengthAnalysis,
            RadarChart = radarChart,
            Leaderboard = leaderboard,
            ScoreComparison = scoreComparison
        };
    }

    private double ComputeWeightedScore(Dictionary<string, double> scores)
    {
        double total = 0;
        foreach (var (cat, weight) in Weights)
            total += scores.GetValueOrDefault(cat, 0) * weight;
        return total;
    }

    private Dictionary<string, double> ExtractCategoryScores(string? json)
    {
        var scores = new Dictionary<string, double>
        {
            ["SEO"] = 50, ["Content"] = 50, ["Trust"] = 50, ["Authority"] = 50,
            ["AIVisibility"] = 50, ["Citation"] = 50, ["GEO"] = 50,
            ["Technology"] = 50, ["TopicalAuthority"] = 50, ["BusinessCompleteness"] = 50
        };

        if (string.IsNullOrEmpty(json)) return scores;

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            scores["SEO"] = GetNestedScore(root, "estimatedSEOStrength");
            scores["Content"] = GetNestedScore(root, "estimatedContentStrength");
            scores["Trust"] = GetNestedScore(root, "estimatedTrustScore");
            scores["Authority"] = GetNestedScore(root, "estimatedBrandAuthority");
            scores["AIVisibility"] = GetNestedScore(root, "estimatedAIVisibility");
            scores["Citation"] = GetNestedScore(root, "estimatedCitationScore");
            scores["GEO"] = GetNestedScore(root, "estimatedGEOReadiness");

            // Derived scores
            if (root.TryGetProperty("services", out var svc) && svc.ValueKind == JsonValueKind.Array)
                scores["BusinessCompleteness"] = Math.Min(100, svc.GetArrayLength() * 10);

            if (root.TryGetProperty("strengths", out var str) && str.ValueKind == JsonValueKind.Array)
                scores["TopicalAuthority"] = Math.Min(100, str.GetArrayLength() * 20);
        }
        catch { }

        return scores;
    }

    private Dictionary<string, double> ExtractUserScores(string? profileJson)
    {
        // For user's own business, derive scores from profile completeness
        var scores = new Dictionary<string, double>
        {
            ["SEO"] = 40, ["Content"] = 40, ["Trust"] = 40, ["Authority"] = 40,
            ["AIVisibility"] = 30, ["Citation"] = 30, ["GEO"] = 30,
            ["Technology"] = 50, ["TopicalAuthority"] = 35, ["BusinessCompleteness"] = 60
        };

        if (string.IsNullOrEmpty(profileJson)) return scores;

        try
        {
            var doc = JsonDocument.Parse(profileJson);
            var root = doc.RootElement;
            int completeness = 0;

            if (root.TryGetProperty("coreServices", out _)) completeness += 15;
            if (root.TryGetProperty("products", out _)) completeness += 10;
            if (root.TryGetProperty("targetCustomers", out _)) completeness += 10;
            if (root.TryGetProperty("uniqueSellingProposition", out _)) completeness += 15;
            if (root.TryGetProperty("brandPositioning", out _)) completeness += 10;
            if (root.TryGetProperty("industriesServed", out _)) completeness += 10;
            if (root.TryGetProperty("businessModel", out _)) completeness += 10;

            scores["BusinessCompleteness"] = Math.Min(100, completeness + 20);
            // Slightly boost other scores based on profile completeness
            double boost = completeness * 0.3;
            scores["SEO"] = Math.Min(100, 40 + boost);
            scores["Content"] = Math.Min(100, 40 + boost);
        }
        catch { }

        return scores;
    }

    private static double GetNestedScore(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Object && prop.TryGetProperty("score", out var score))
                return score.GetDouble();
            if (prop.ValueKind == JsonValueKind.Number)
                return prop.GetDouble();
        }
        return 50; // default
    }

    private CompetitiveGapAnalysis ComputeGapAnalysis(
        List<CategoryRanking> rankings, Dictionary<string, double> userScores)
    {
        var gaps = new CompetitiveGapAnalysis();

        foreach (var r in rankings.Where(r => r.GapToLeader > 10))
        {
            var gap = $"{r.Category}: User {r.UserScore:F0} vs Leader {r.LeaderScore:F0} (gap: {r.GapToLeader:F0})";
            switch (r.Category)
            {
                case "SEO": gaps.SEOGaps.Add(gap); break;
                case "Content": gaps.ContentGaps.Add(gap); break;
                case "AIVisibility": gaps.AIVisibilityGaps.Add(gap); break;
                case "Citation": gaps.CitationGaps.Add(gap); break;
                case "Trust": gaps.TrustSignalGaps.Add(gap); break;
                case "Technology": gaps.TechnologyGaps.Add(gap); break;
                default: gaps.MissingFeatures.Add(gap); break;
            }
        }

        // Generate recommendations for top gaps
        gaps.Recommendations = rankings
            .Where(r => r.GapToLeader > 5)
            .OrderByDescending(r => r.GapToLeader)
            .Take(5)
            .Select(r => $"Priority: Close {r.Category} gap of {r.GapToLeader:F0} points (current: {r.UserScore:F0}, leader: {r.LeaderScore:F0})")
            .ToList();

        return gaps;
    }

    private StrengthAnalysis ComputeStrengthAnalysis(
        List<CategoryRanking> rankings, Dictionary<string, double> userScores)
    {
        var analysis = new StrengthAnalysis();

        var sorted = rankings.OrderByDescending(r => r.UserScore).ToList();
        analysis.TopStrengths = sorted.Take(3).Select(r => $"{r.Category}: {r.UserScore:F0}/100").ToList();
        analysis.TopWeaknesses = sorted.TakeLast(3).Select(r => $"{r.Category}: {r.UserScore:F0}/100").ToList();

        analysis.CompetitiveAdvantages = rankings
            .Where(r => r.GapToAverage < -5)
            .Select(r => $"{r.Category}: {Math.Abs(r.GapToAverage):F0} points above average")
            .ToList();

        analysis.CompetitiveDisadvantages = rankings
            .Where(r => r.GapToAverage > 10)
            .Select(r => $"{r.Category}: {r.GapToAverage:F0} points below average")
            .ToList();

        analysis.Opportunities = rankings
            .Where(r => r.GapToLeader > 15)
            .Select(r => $"Close {r.Category} gap to gain {r.GapToLeader:F0} competitive points")
            .ToList();

        analysis.Threats = rankings
            .Where(r => r.GapToAverage > 20)
            .Select(r => $"Critical: {r.Category} is {r.GapToAverage:F0} points below industry average")
            .ToList();

        return analysis;
    }

    private class ScoredCompany
    {
        public string Name { get; set; } = string.Empty;
        public bool IsUser { get; set; }
        public Guid? CompetitorId { get; set; }
        public Dictionary<string, double> CategoryScores { get; set; } = new();
        public double OverallScore { get; set; }
    }
}
