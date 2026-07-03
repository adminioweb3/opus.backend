using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Competitors;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Competitors;

/// <summary>
/// Enriches a single competitor with detailed intelligence via a focused AI call.
/// Each enrichment is ~500 output tokens, targeted at one specific company.
/// </summary>
public class CompetitorEnrichmentService : ICompetitorEnrichmentService
{
    private readonly IOpenAiService _openAiService;

    public CompetitorEnrichmentService(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public async Task EnrichCompetitorAsync(
        Competitor competitor,
        string businessProfileJson,
        CancellationToken cancellationToken)
    {
        competitor.EnrichmentStatus = "InProgress";

        var systemPrompt = "You are a competitive intelligence analyst. Output ONLY JSON. No markdown.";

        var userPrompt = $@"Provide detailed competitive intelligence for this company.

Company: {competitor.Name}
Website: {competitor.WebsiteUrl}
Industry: {competitor.Industry}

Return a single JSON object:
{{
  ""services"": [""max 10 services""],
  ""strengths"": [""max 5""],
  ""weaknesses"": [""max 5""],
  ""employees"": ""estimate"",
  ""headquarters"": ""city, country"",
  ""founded"": ""year"",
  ""targetAudience"": [""max 5""],
  ""marketSegment"": ""segment"",
  ""estimatedTraffic"": {{""monthlyVisitors"": 0, ""confidence"": 0}},
  ""estimatedSEOStrength"": {{""score"": 0, ""confidence"": 0}},
  ""estimatedBrandAuthority"": {{""score"": 0, ""confidence"": 0}},
  ""estimatedTrustScore"": {{""score"": 0, ""confidence"": 0}},
  ""estimatedAIVisibility"": {{""score"": 0, ""confidence"": 0}},
  ""estimatedCitationScore"": {{""score"": 0, ""confidence"": 0}},
  ""estimatedContentStrength"": {{""score"": 0, ""confidence"": 0}},
  ""estimatedGEOReadiness"": {{""score"": 0, ""confidence"": 0}}
}}

Scores are 0-100. Do NOT invent facts. Estimate logically based on industry norms.";

        try
        {
            var response = await _openAiService.GenerateContentAsync(
                userPrompt, systemPrompt, true, "gpt-4o-mini");

            // Extract JSON object
            int startIdx = response.IndexOf('{');
            int endIdx = response.LastIndexOf('}');
            if (startIdx >= 0 && endIdx > startIdx)
            {
                var jsonStr = response.Substring(startIdx, endIdx - startIdx + 1);

                // Validate it's valid JSON
                using var doc = JsonDocument.Parse(jsonStr);

                competitor.EnrichedJson = jsonStr;
                competitor.EnrichmentStatus = "Completed";
                competitor.EnrichedAt = DateTime.UtcNow;

                // Extract key metrics into entity fields
                var root = doc.RootElement;
                if (root.TryGetProperty("estimatedBrandAuthority", out var auth) &&
                    auth.TryGetProperty("score", out var authScore))
                    competitor.Authority = authScore.GetInt32();

                if (root.TryGetProperty("headquarters", out var hq))
                    competitor.Country = hq.GetString() ?? competitor.Country;

                // Merge enriched data into RawJson for backward compatibility
                var mergedData = new Dictionary<string, object?>
                {
                    ["rank"] = competitor.Rank,
                    ["companyName"] = competitor.Name,
                    ["website"] = competitor.WebsiteUrl,
                    ["industry"] = competitor.Industry,
                    ["competitorType"] = competitor.CompetitorType,
                    ["description"] = competitor.Description,
                    ["similarityScore"] = competitor.SimilarityScore,
                    ["confidence"] = competitor.Confidence
                };

                // Overlay enrichment data
                foreach (var prop in root.EnumerateObject())
                {
                    mergedData[prop.Name] = prop.Value.Clone();
                }

                competitor.RawJson = JsonSerializer.Serialize(mergedData);
            }
            else
            {
                competitor.EnrichmentStatus = "Failed";
                Console.WriteLine($"[Enrichment] No valid JSON object in response for {competitor.Name}");
            }
        }
        catch (Exception ex)
        {
            competitor.EnrichmentStatus = "Failed";
            Console.WriteLine($"[Enrichment] AI call failed for {competitor.Name}: {ex.Message}");
        }
    }
}
