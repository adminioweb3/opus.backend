using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Competitors;

/// <summary>
/// Deterministic C# ranking engine. No AI calls.
/// Reads enriched competitor data and computes weighted composite scores.
/// </summary>
public interface ICompetitorRankingService
{
    /// <summary>
    /// Computes rankings across all enriched competitors for an organization,
    /// including the user's own business. Returns a comprehensive ranking result.
    /// </summary>
    Task<CompetitorRankingResult> ComputeRankingsAsync(Guid organizationId, CancellationToken cancellationToken);
}

public class CompetitorRankingResult
{
    // Overall
    public int OverallRank { get; set; }
    public int TotalCompanies { get; set; }
    public double Percentile { get; set; }
    public double OverallScore { get; set; }
    public double DifferenceFromLeader { get; set; }
    public double DifferenceFromAverage { get; set; }

    // Dashboard Summary
    public string? TopCompetitor { get; set; }
    public string? MostSimilarCompetitor { get; set; }
    public string? ClosestCompetitor { get; set; }
    public int CompaniesOutperformed { get; set; }
    public int CompaniesAhead { get; set; }
    public string? BiggestOpportunity { get; set; }
    public double EstimatedImprovementPotential { get; set; }
    public List<string> QuickWins { get; set; } = new();

    // Category Rankings
    public List<CategoryRanking> CategoryRankings { get; set; } = new();

    // Gap Analysis
    public CompetitiveGapAnalysis? GapAnalysis { get; set; }

    // SWOT
    public StrengthAnalysis? StrengthAnalysis { get; set; }

    // Chart Data (frontend-ready)
    public List<RadarChartEntry> RadarChart { get; set; } = new();
    public List<LeaderboardEntry> Leaderboard { get; set; } = new();
    public List<ScoreComparisonEntry> ScoreComparison { get; set; } = new();
}

public class CategoryRanking
{
    public string Category { get; set; } = string.Empty;
    public int UserRank { get; set; }
    public double UserScore { get; set; }
    public string? Leader { get; set; }
    public double LeaderScore { get; set; }
    public double GapToLeader { get; set; }
    public double GapToAverage { get; set; }
}

public class CompetitiveGapAnalysis
{
    public List<string> MissingFeatures { get; set; } = new();
    public List<string> SEOGaps { get; set; } = new();
    public List<string> ContentGaps { get; set; } = new();
    public List<string> AIVisibilityGaps { get; set; } = new();
    public List<string> TechnologyGaps { get; set; } = new();
    public List<string> CitationGaps { get; set; } = new();
    public List<string> TrustSignalGaps { get; set; } = new();
    public List<string> BrandPositioningGaps { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

public class StrengthAnalysis
{
    public List<string> TopStrengths { get; set; } = new();
    public List<string> TopWeaknesses { get; set; } = new();
    public List<string> CompetitiveAdvantages { get; set; } = new();
    public List<string> CompetitiveDisadvantages { get; set; } = new();
    public List<string> Opportunities { get; set; } = new();
    public List<string> Threats { get; set; } = new();
}

public class RadarChartEntry
{
    public string Category { get; set; } = string.Empty;
    public double UserScore { get; set; }
    public double IndustryAverage { get; set; }
    public double LeaderScore { get; set; }
}

public class LeaderboardEntry
{
    public int Rank { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public double OverallScore { get; set; }
    public bool IsUser { get; set; }
}

public class ScoreComparisonEntry
{
    public string CompanyName { get; set; } = string.Empty;
    public double SEO { get; set; }
    public double Content { get; set; }
    public double Trust { get; set; }
    public double Authority { get; set; }
    public double AIVisibility { get; set; }
    public double Citation { get; set; }
    public bool IsUser { get; set; }
}
