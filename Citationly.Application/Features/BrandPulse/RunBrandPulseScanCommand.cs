using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.BrandPulse;

public class RunBrandPulseScanCommand : IRequest<RunBrandPulseScanResult>
{
    public Guid OrganizationId { get; set; }
}

public class RunBrandPulseScanResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public BrandPulseScanSummary? Summary { get; set; }
}

/// <summary>
/// Internal shape used only to deserialize the single AI JSON response.
/// </summary>
internal class BrandPulseAiResult
{
    public int BrandHealth { get; set; } = 65;
    public int AiConfidence { get; set; } = 60;
    public int MessagingConsistency { get; set; } = 60;
    public int BrandTrust { get; set; } = 60;
    public int SentimentPositive { get; set; } = 50;
    public int SentimentNeutral { get; set; } = 35;
    public int SentimentNegative { get; set; } = 15;
    public List<ShareOfPerceptionItem>? SharePerception { get; set; }
    public List<ModelInsightItem>? ModelInsights { get; set; }
    public List<BrandAlertItem>? Alerts { get; set; }
    public List<AccuracyFlagItem>? AccuracyFlags { get; set; }
    public List<PromptEvidenceItem>? PromptEvidence { get; set; }
}

public class RunBrandPulseScanCommandHandler : IRequestHandler<RunBrandPulseScanCommand, RunBrandPulseScanResult>
{
    private readonly IBrandPulseSnapshotRepository _brandPulseSnapshotRepository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openAiService;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public RunBrandPulseScanCommandHandler(
        IBrandPulseSnapshotRepository brandPulseSnapshotRepository,
        IWebsiteRepository websiteRepository,
        IOpenAiService openAiService,
        IDbConnectionFactory dbConnectionFactory)
    {
        _brandPulseSnapshotRepository = brandPulseSnapshotRepository;
        _websiteRepository = websiteRepository;
        _openAiService = openAiService;
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<RunBrandPulseScanResult> Handle(RunBrandPulseScanCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Ground in the org's real website profile / domain where available. Tolerate absence.
            string websiteUrl = "unknown";
            string businessName = "the organization";
            string websiteProfileContext = "No detailed website profile is available yet.";

            var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
            if (profile != null)
            {
                websiteUrl = string.IsNullOrWhiteSpace(profile.WebsiteUrl) ? websiteUrl : profile.WebsiteUrl;
                businessName = string.IsNullOrWhiteSpace(profile.BusinessName) ? businessName : profile.BusinessName;
                websiteProfileContext = string.IsNullOrWhiteSpace(profile.RawProfileJson) ? websiteProfileContext : profile.RawProfileJson;
            }

            // Optional: ground the "you" share of voice from the competitive analysis snapshot if it exists.
            // The competitorsnapshots table may not exist yet for a brand-new org — tolerate that.
            string competitorContext = "No prior competitive share-of-voice snapshot is available.";
            try
            {
                using var connection = _dbConnectionFactory.CreateConnection();
                var youSnapshot = await connection.QueryFirstOrDefaultAsync<CompetitorSnapshot>(@"
                    SELECT * FROM CompetitorSnapshots
                    WHERE OrganizationId = @OrganizationId AND IsYou = true
                    ORDER BY ScanDate DESC
                    LIMIT 1",
                    new { OrganizationId = request.OrganizationId });

                if (youSnapshot != null)
                {
                    competitorContext = $"The organization's most recent competitive-analysis share of voice was {youSnapshot.ShareOfVoice}% " +
                                         $"(visibility score {youSnapshot.Visibility}, threat level context: {youSnapshot.Threat}).";
                }
            }
            catch
            {
                // Table doesn't exist yet or query failed — this is optional context only, ignore.
            }

            var systemPrompt = "You are an expert brand strategist and AI-search reputation analyst who evaluates how a brand is perceived, " +
                                "represented, and trusted across generative AI platforms like ChatGPT, Claude, Gemini, and Perplexity.";

            var userPrompt = $@"Analyze the AI-perceived brand health of the following business and produce a ""Brand Pulse"" report.

## Business
Name: {businessName}
Website: {websiteUrl}

## Website Profile Context
{websiteProfileContext}

## Competitive Context
{competitorContext}

## Objective
Estimate how this brand is currently perceived and represented across AI models (ChatGPT, Claude, Gemini, Perplexity), covering
overall brand health, how confidently AI models discuss the brand, how consistent the brand messaging is across models,
how much the brand is trusted, the sentiment mix in AI-generated answers, how perceived share is split between this brand
and its competitors, per-model insights, risk/warning/win alerts, factual accuracy flags, and example prompt evidence.

## Instructions
1. Ground your estimates in the business context provided; never invent specific factual claims you cannot support.
2. Return ONLY valid JSON. Do not wrap in markdown or backticks. Do not include any explanation text.
3. sentimentPositive + sentimentNeutral + sentimentNegative should sum to approximately 100.
4. sharePerception must contain 4-5 items: the organization itself plus 3-4 competitors (use real competitor names if inferable
   from the competitive context, otherwise use generic labels like ""Competitor A"", ""Competitor B"", ""Competitor C""). Each item's
   value is a percentage share; values across all items should sum to approximately 100. Use distinct hex color strings for ""color"".
5. modelInsights must contain exactly 4 items, one each for ""ChatGPT"", ""Claude"", ""Gemini"", and ""Perplexity"".
6. alerts must contain 1-3 items with type one of ""risk"", ""warning"", ""win"".
7. accuracyFlags must contain 0-2 items (empty array if nothing notable) with severity one of ""High"", ""Medium"", ""Low"".
8. promptEvidence must contain 2-3 items representing realistic example prompts a customer might ask an AI model about this brand.

Return exactly this JSON schema:
{{
  ""brandHealth"": 0,
  ""aiConfidence"": 0,
  ""messagingConsistency"": 0,
  ""brandTrust"": 0,
  ""sentimentPositive"": 0,
  ""sentimentNeutral"": 0,
  ""sentimentNegative"": 0,
  ""sharePerception"": [
    {{ ""name"": """", ""value"": 0, ""color"": ""#000000"" }}
  ],
  ""modelInsights"": [
    {{ ""platform"": ""ChatGPT"", ""confidence"": 0, ""sentiment"": ""pos"", ""themes"": [""""], ""flag"": false }}
  ],
  ""alerts"": [
    {{ ""type"": ""warning"", ""title"": """", ""message"": """" }}
  ],
  ""accuracyFlags"": [
    {{ ""claim"": """", ""severity"": ""Medium"", ""detail"": """", ""models"": [""""] }}
  ],
  ""promptEvidence"": [
    {{ ""prompt"": """", ""sentiment"": ""pos"", ""sources"": [""""] }}
  ]
}}

Return ONLY the JSON object.";

            var aiResult = new BrandPulseAiResult();

            try
            {
                var responseContent = await _openAiService.GenerateContentAsync(
                    prompt: userPrompt,
                    systemPrompt: systemPrompt,
                    requireJson: true,
                    model: "gpt-4o-mini");

                responseContent = StripJsonFences(responseContent);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };

                var parsed = JsonSerializer.Deserialize<BrandPulseAiResult>(responseContent, options);
                if (parsed != null)
                {
                    aiResult = parsed;
                }
            }
            catch
            {
                // Malformed AI response — fall back to the sane defaults already set on aiResult.
            }

            // Defensive normalization / fallback defaults per field.
            var summary = new BrandPulseScanSummary
            {
                Id = Guid.NewGuid(),
                OrganizationId = request.OrganizationId,
                ScanDate = DateOnly.FromDateTime(DateTime.UtcNow),
                BrandHealth = ClampScore(aiResult.BrandHealth, 65),
                AiConfidence = ClampScore(aiResult.AiConfidence, 60),
                MessagingConsistency = ClampScore(aiResult.MessagingConsistency, 60),
                BrandTrust = ClampScore(aiResult.BrandTrust, 60),
                SentimentPositive = ClampScore(aiResult.SentimentPositive, 50),
                SentimentNeutral = ClampScore(aiResult.SentimentNeutral, 35),
                SentimentNegative = ClampScore(aiResult.SentimentNegative, 15),
                SharePerceptionJson = SerializeOrDefault(aiResult.SharePerception, DefaultSharePerception(businessName)),
                ModelInsightsJson = SerializeOrDefault(aiResult.ModelInsights, DefaultModelInsights()),
                AlertsJson = SerializeOrDefault(aiResult.Alerts, DefaultAlerts()),
                AccuracyFlagsJson = SerializeOrDefault(aiResult.AccuracyFlags, new List<AccuracyFlagItem>()),
                PromptEvidenceJson = SerializeOrDefault(aiResult.PromptEvidence, DefaultPromptEvidence(businessName)),
                CreatedAt = DateTime.UtcNow
            };

            await _brandPulseSnapshotRepository.SaveSnapshotAsync(summary);

            return new RunBrandPulseScanResult
            {
                Success = true,
                Summary = summary
            };
        }
        catch (Exception ex)
        {
            return new RunBrandPulseScanResult { Success = false, Error = ex.Message };
        }
    }

    private static string StripJsonFences(string content)
    {
        content = content.Trim();
        if (content.StartsWith("```json"))
        {
            content = content.Substring(7);
            if (content.EndsWith("```"))
                content = content.Substring(0, content.Length - 3);
        }
        else if (content.StartsWith("```"))
        {
            content = content.Substring(3);
            if (content.EndsWith("```"))
                content = content.Substring(0, content.Length - 3);
        }
        return content.Trim();
    }

    private static int ClampScore(int value, int fallback)
    {
        if (value < 0 || value > 100) return fallback;
        return value;
    }

    private static string SerializeOrDefault<T>(T? value, T fallback) where T : class
    {
        try
        {
            var toSerialize = value ?? fallback;
            return JsonSerializer.Serialize(toSerialize);
        }
        catch
        {
            return JsonSerializer.Serialize(fallback);
        }
    }

    private static List<ShareOfPerceptionItem> DefaultSharePerception(string businessName) => new()
    {
        new ShareOfPerceptionItem { Name = businessName, Value = 30, Color = "#6366F1" },
        new ShareOfPerceptionItem { Name = "Competitor A", Value = 25, Color = "#F59E0B" },
        new ShareOfPerceptionItem { Name = "Competitor B", Value = 25, Color = "#10B981" },
        new ShareOfPerceptionItem { Name = "Competitor C", Value = 20, Color = "#EF4444" }
    };

    private static List<ModelInsightItem> DefaultModelInsights() => new()
    {
        new ModelInsightItem { Platform = "ChatGPT", Confidence = 60, Sentiment = "neu", Themes = new() { "General awareness" }, Flag = false },
        new ModelInsightItem { Platform = "Claude", Confidence = 55, Sentiment = "neu", Themes = new() { "Limited data" }, Flag = false },
        new ModelInsightItem { Platform = "Gemini", Confidence = 55, Sentiment = "neu", Themes = new() { "Limited data" }, Flag = false },
        new ModelInsightItem { Platform = "Perplexity", Confidence = 55, Sentiment = "neu", Themes = new() { "Limited data" }, Flag = false }
    };

    private static List<BrandAlertItem> DefaultAlerts() => new()
    {
        new BrandAlertItem
        {
            Type = "warning",
            Title = "Limited AI visibility data",
            Message = "We don't yet have enough grounded data to fully assess this brand's AI perception. Re-scan after connecting more content sources."
        }
    };

    private static List<PromptEvidenceItem> DefaultPromptEvidence(string businessName) => new()
    {
        new PromptEvidenceItem
        {
            Prompt = $"What do you know about {businessName}?",
            Sentiment = "neu",
            Sources = new() { "General knowledge" }
        }
    };
}
