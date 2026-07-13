namespace Citationly.Application.Features.BrandPulse;

// Shared DTOs used both for parsing the AI's JSON response (RunBrandPulseScanCommand)
// and for shaping the final GET /Dashboard/brand-pulse API response (DashboardController).
// PascalCase properties are camelCased automatically by ASP.NET Core's default System.Text.Json settings.

public class ShareOfPerceptionItem
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public string Color { get; set; } = "#6366F1";
}

public class ModelInsightItem
{
    public string Platform { get; set; } = string.Empty;
    public int Confidence { get; set; }
    public string Sentiment { get; set; } = "neu";
    public List<string> Themes { get; set; } = new();
    public bool Flag { get; set; }
}

public class BrandAlertItem
{
    public string Type { get; set; } = "warning";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class AccuracyFlagItem
{
    public string Claim { get; set; } = string.Empty;
    public string Severity { get; set; } = "Low";
    public string Detail { get; set; } = string.Empty;
    public List<string> Models { get; set; } = new();
}

public class PromptEvidenceItem
{
    public string Prompt { get; set; } = string.Empty;
    public string Sentiment { get; set; } = "neu";
    public List<string> Sources { get; set; } = new();
}

public class MetricHistoryData
{
    public List<int> Health { get; set; } = new();
    public List<int> Confidence { get; set; } = new();
    public List<int> Messaging { get; set; } = new();
    public List<int> Trust { get; set; } = new();
}

public class SentimentMixData
{
    public int Positive { get; set; }
    public int Neutral { get; set; }
    public int Negative { get; set; }
}

/// <summary>
/// Response DTO for GET /Dashboard/brand-pulse. Mirrors the frontend's BrandPulseResponse interface verbatim
/// (camelCased on serialization).
/// </summary>
public class BrandPulseResponse
{
    public bool HasData { get; set; }
    public int BrandHealth { get; set; }
    public int AiConfidence { get; set; }
    public int MessagingConsistency { get; set; }
    public int BrandTrust { get; set; }
    public string HealthDelta { get; set; } = "0";
    public string ConfidenceDelta { get; set; } = "0";
    public string MessagingDelta { get; set; } = "0";
    public string TrustDelta { get; set; } = "0";
    public MetricHistoryData MetricHistory { get; set; } = new();
    public SentimentMixData SentimentMix { get; set; } = new();
    public List<ShareOfPerceptionItem> ShareOfPerception { get; set; } = new();
    public List<ModelInsightItem> ModelInsights { get; set; } = new();
    public List<BrandAlertItem> Alerts { get; set; } = new();
    public List<AccuracyFlagItem> AccuracyFlags { get; set; } = new();
    public List<PromptEvidenceItem> PromptEvidence { get; set; } = new();
    public string LastScanDate { get; set; } = string.Empty;
}
