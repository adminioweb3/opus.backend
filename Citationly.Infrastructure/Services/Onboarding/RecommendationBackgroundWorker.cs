using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Citationly.Infrastructure.Services.Onboarding;

public class EnrichmentResponse
{
    public string ExpandedGuidance { get; set; } = string.Empty;
    public string BusinessImpact { get; set; } = string.Empty;
    public JsonElement ActionItems { get; set; }
    public JsonElement ExampleResources { get; set; }
    public JsonElement ReferenceLinks { get; set; }
}

public class RecommendationBackgroundWorker
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openAiService;
    private readonly ILogger<RecommendationBackgroundWorker> _logger;

    public RecommendationBackgroundWorker(
        IWebsiteRepository websiteRepository, 
        IOpenAiService openAiService,
        ILogger<RecommendationBackgroundWorker> logger)
    {
        _websiteRepository = websiteRepository;
        _openAiService = openAiService;
        _logger = logger;
    }

    public async Task EnrichRecommendationsAsync(Guid organizationId)
    {
        _logger.LogInformation("Starting background enrichment for Organization {OrgId}", organizationId);

        // Fetch up to 10 unenriched recommendations at a time to prevent timeout/exhaustion
        var pendingRecs = await _websiteRepository.GetGeoRecommendationsForEnrichmentAsync(organizationId, limit: 10);
        
        if (!pendingRecs.Any())
        {
            _logger.LogInformation("No pending enrichments found for {OrgId}", organizationId);
            return;
        }

        foreach (var rec in pendingRecs)
        {
            try
            {
                await EnrichSingleRecommendationAsync(rec);
                await _websiteRepository.UpdateGeoRecommendationAsync(rec);
                // Respect rate limits
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enrich recommendation {RecId}", rec.RecommendationId);
            }
        }
        
        // Re-queue if there are more
        var stillPending = await _websiteRepository.GetGeoRecommendationsForEnrichmentAsync(organizationId, limit: 1);
        if (stillPending.Any())
        {
            // Hangfire job client would typically be used here to re-enqueue. 
            // In this architecture, it is acceptable if the cron/scheduler or subsequent pipeline triggers it.
            _logger.LogInformation("More pending enrichments exist. Continuing in next batch.");
        }
    }

    private async Task EnrichSingleRecommendationAsync(GeoRecommendation rec)
    {
        var systemPrompt = "You are an expert Technical SEO and GEO implementation specialist. Provide actionable, step-by-step guidance for a specific recommendation.";

        var userPrompt = $@"Enrich the following GEO Recommendation with implementation details.

## Recommendation
Title: {rec.Title}
Category: {rec.Category}
Description: {rec.Description}

## Objective
Provide:
1. Expanded Guidance (1-2 paragraphs of deep dive explanation)
2. Business Impact (Why this matters)
3. Action Items (List of specific string steps to execute this)
4. Example Resources (List of tools, websites, or reference examples as strings)
5. Reference Links (List of URLs to official docs or guides as strings)

Return ONLY valid JSON matching this schema exactly. No markdown blocks.

{{
  ""expandedGuidance"": """",
  ""businessImpact"": """",
  ""actionItems"": [""Step 1"", ""Step 2""],
  ""exampleResources"": [""Tool A"", ""Guide B""],
  ""referenceLinks"": [""https://..."", ""https://...""]
}}";

        var responseContent = await _openAiService.GenerateContentAsync(
            prompt: userPrompt,
            systemPrompt: systemPrompt,
            requireJson: true,
            model: "gpt-4o-mini");

        responseContent = responseContent.Trim();
        if (responseContent.StartsWith("```json"))
            responseContent = responseContent.Substring(7).TrimEnd('`').Trim();
        else if (responseContent.StartsWith("```"))
            responseContent = responseContent.Substring(3).TrimEnd('`').Trim();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var parsed = JsonSerializer.Deserialize<EnrichmentResponse>(responseContent, options);

        if (parsed != null)
        {
            rec.ExpandedGuidance = parsed.ExpandedGuidance ?? string.Empty;
            rec.BusinessImpact = parsed.BusinessImpact ?? string.Empty;
            rec.ActionItemsJson = parsed.ActionItems.ValueKind != JsonValueKind.Undefined ? JsonSerializer.Serialize(parsed.ActionItems, options) : "[]";
            rec.ExampleResourcesJson = parsed.ExampleResources.ValueKind != JsonValueKind.Undefined ? JsonSerializer.Serialize(parsed.ExampleResources, options) : "[]";
            rec.ReferenceLinksJson = parsed.ReferenceLinks.ValueKind != JsonValueKind.Undefined ? JsonSerializer.Serialize(parsed.ReferenceLinks, options) : "[]";
            rec.IsEnriched = true;
            rec.EnrichedAt = DateTime.UtcNow;
        }
    }
}
