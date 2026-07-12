using System.Collections.Generic;

namespace Citationly.Application.Features.GeoOptimizer;

public class GeoOptimizationRequest
{
    public string? Url { get; set; }
    public string? Content { get; set; }
    public string TargetKeyword { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
}

public class GeoOptimizationResponse
{
    public int Score { get; set; }
    public string Verdict { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public List<GeoSubMetric> SubMetrics { get; set; } = new();
    public List<GeoFixRecommendation> FixRecommendations { get; set; } = new();
    public List<GeoCompetitorGap> CompetitorGap { get; set; } = new();
    public List<GeoPromptCoverageItem> PromptCoverage { get; set; } = new();
    public List<GeoCitationSignal> CitationGap { get; set; } = new();
}

public class GeoSubMetric
{
    public string Label { get; set; } = string.Empty;
    public int Score { get; set; } // 0-100
}

public class GeoFixRecommendation
{
    public string Title { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty; // "High", "Medium", "Low"
    public string Icon { get; set; } = string.Empty; // Tabler icon suffix, e.g. "ti-quote"
    public string Description { get; set; } = string.Empty;
    public string Delta { get; set; } = string.Empty; // e.g. "+18 GEO score"
}

public class GeoCompetitorGap
{
    public string Name { get; set; } = string.Empty;
    public string Coverage { get; set; } = string.Empty; // e.g., "85%"
    public string Status { get; set; } = string.Empty; // "Strong", "Moderate", "Weak"
}

public class GeoPromptCoverageItem
{
    public string Question { get; set; } = string.Empty;
    public string Coverage { get; set; } = string.Empty; // "Full", "Partial", "None"
    public string Note { get; set; } = string.Empty;
}

public class GeoCitationSignal
{
    public string Icon { get; set; } = string.Empty; // Tabler icon suffix, e.g. "ti-chart-bar"
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // e.g. "1 of ~7 expected"
    public int Score { get; set; } // 0-100
}

public class SchemaGenerationRequest
{
    public string? Url { get; set; }
    public string? Content { get; set; }
    public string SchemaType { get; set; } = string.Empty;
}

public class SchemaGenerationResponse
{
    public string JsonLd { get; set; } = string.Empty;
}
