namespace Citationly.Application.Features.GeoDashboard;

/// <summary>
/// Root response DTO for GET /dashboard/geo-dashboard.
/// Property names are PascalCase here and are camelCased automatically by
/// ASP.NET Core's default System.Text.Json serialization, matching the
/// GeoDashboardData TypeScript interface in frontend/src/lib/api/dashboardApi.ts.
/// </summary>
public class GeoDashboardData
{
    public bool HasData { get; set; }
    public GeoScoreCard Scores { get; set; } = new();
    public List<TrendPoint> Trend { get; set; } = new();
    public List<ShareOfVoiceEntry> ShareOfVoice { get; set; } = new();
    public GeoDashboardHeader Header { get; set; } = new();
    public List<GeoPillar> Pillars { get; set; } = new();
    public WeakestPillarInsight? WeakestPillarInsight { get; set; }
    public List<PromptTypeCoverageItem> PromptTypeCoverage { get; set; } = new();
    public GeoInsight? OpportunityInsight { get; set; }
    public List<WinLossEvent> WinsAndLosses { get; set; } = new();
    public GeoInsight VerifyInsight { get; set; } = new();
}

public class ScoreEntry
{
    public int Value { get; set; }
    public string Change { get; set; } = "+0%";
    public string Direction { get; set; } = "up"; // "up" | "down"
}

public class GeoScoreCard
{
    public ScoreEntry VisibilityScore { get; set; } = new();
    public ScoreEntry CitationScore { get; set; } = new();
    public ScoreEntry SentimentScore { get; set; } = new();
    public ScoreEntry CompetitorScore { get; set; } = new();
    public ScoreEntry HallucinationRisk { get; set; } = new();
    public ScoreEntry SeoHealth { get; set; } = new();
    public ScoreEntry AeoReadiness { get; set; } = new();
    public ScoreEntry GeoReadiness { get; set; } = new();
}

public class TrendPoint
{
    public int Day { get; set; }
    public int Score { get; set; }
}

public class ShareOfVoiceEntry
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public string Color { get; set; } = "#000000";
}

public class GeoDashboardHeader
{
    public int CompositeScore { get; set; }
    public string Grade { get; set; } = "—";
    public int IndustryAverage { get; set; }
    public int DeltaVsIndustry { get; set; }
    public string CompositeChange { get; set; } = "+0%";
    public int EnginesScanned { get; set; }
    public int PromptsTracked { get; set; }
    /// <summary>
    /// "live" or "stale" — the frontend hero badge checks status === "live" literally
    /// (see geo-dashboard/page.tsx), so this must stay lowercase and exactly one of
    /// those two values, not a human-readable phrase.
    /// </summary>
    public string Status { get; set; } = "stale";
}

public class GeoPillar
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Score { get; set; }
}

public class GeoInsight
{
    public string Message { get; set; } = string.Empty;
    public string CtaLabel { get; set; } = string.Empty;
    public string CtaLink { get; set; } = string.Empty;
}

public class WeakestPillarInsight
{
    public string PillarKey { get; set; } = string.Empty;
    public int Score { get; set; }
    public string Message { get; set; } = string.Empty;
    public string CtaLabel { get; set; } = string.Empty;
    public string CtaLink { get; set; } = string.Empty;
}

public class PromptTypeCoverageItem
{
    public string Type { get; set; } = string.Empty;
    public string Example { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int Percentage { get; set; }
    public string Direction { get; set; } = "up";
}

public class WinLossEvent
{
    public string Type { get; set; } = string.Empty; // "win" | "loss"
    public string Title { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
}
