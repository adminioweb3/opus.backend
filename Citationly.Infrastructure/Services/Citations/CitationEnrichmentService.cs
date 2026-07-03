using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Citations;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Citations;

public class CitationEnrichmentService : ICitationEnrichmentService
{
    private readonly IOpenAiService _openRouterService;

    public CitationEnrichmentService(IOpenAiService openRouterService)
    {
        _openRouterService = openRouterService;
    }

    public async Task<List<CitationSource>> EnrichCitationsAsync(
        Guid organizationId, 
        string websiteProfileJson, 
        List<CitationSource> sourcesToEnrich)
    {
        var sourcesJson = JsonSerializer.Serialize(sourcesToEnrich.Select(s => new
        {
            Id = s.Id,
            Source = s.Source,
            Category = s.Category
        }));

        string systemPrompt = "You are an expert in Generative Engine Optimization (GEO), AI Search, SEO, Knowledge Graphs, Entity Recognition, and Competitive Intelligence.";

        string userPrompt = $@"Your task is to enrich the provided citation sources with estimated influence and opportunity scores.

## Input

Website Profile
{websiteProfileJson}

Citation Sources To Enrich
{sourcesJson}

## Objective

For each provided source, generate predictive scores.

------------------------------------------------
Scoring Rules

Authority Score (0-100)
Estimate the authority and trustworthiness of the source.

Influence Score (0-100)
Estimate how strongly the source influences AI-generated answers.

Citation Frequency (0-100)
Estimate how frequently AI systems are likely to rely on this source when answering relevant prompts.

Competitor Coverage (0-100)
Estimate how well competitors are represented or mentioned by this source.

Opportunity Score (0-100)
Estimate the opportunity for the business to gain visibility from this source. Higher score = Better opportunity.

Mention Probability (0-100)
Estimate the probability that this source would mention or reference the business if its content and authority improve.

------------------------------------------------
Return exactly this schema array:
[
  {{
    ""id"": """", // MUST match exactly the Input Id
    ""authorityScore"": 0,
    ""influenceScore"": 0,
    ""citationFrequency"": 0,
    ""competitorCoverage"": 0,
    ""opportunityScore"": 0,
    ""mentionProbability"": 0,
    ""reason"": """"
  }}
]

Reason
Provide a concise explanation (maximum 30 words) describing why this source is influential for AI-generated answers and why it represents an opportunity (or challenge) for the business.

Return ONLY the JSON array.";

        var responseContent = await _openRouterService.GenerateContentAsync(
            prompt: userPrompt,
            systemPrompt: systemPrompt,
            requireJson: true,
            model: "gpt-4o-mini");

        responseContent = responseContent.Trim();
        if (responseContent.StartsWith("```json"))
        {
            responseContent = responseContent.Substring(7);
            if (responseContent.EndsWith("```"))
                responseContent = responseContent.Substring(0, responseContent.Length - 3);
        }
        if (responseContent.StartsWith("```"))
        {
            responseContent = responseContent.Substring(3);
            if (responseContent.EndsWith("```"))
                responseContent = responseContent.Substring(0, responseContent.Length - 3);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        var parsed = JsonSerializer.Deserialize<List<EnrichedCitationDto>>(responseContent, options);

        if (parsed == null)
            return sourcesToEnrich;

        var dict = parsed.ToDictionary(x => x.Id, x => x);

        foreach (var source in sourcesToEnrich)
        {
            if (dict.TryGetValue(source.Id.ToString(), out var enrichment))
            {
                source.AuthorityScore = enrichment.AuthorityScore;
                source.InfluenceScore = enrichment.InfluenceScore;
                source.CitationFrequency = enrichment.CitationFrequency;
                source.CompetitorCoverage = enrichment.CompetitorCoverage;
                source.OpportunityScore = enrichment.OpportunityScore;
                source.MentionProbability = enrichment.MentionProbability;
                source.Reason = enrichment.Reason ?? source.Reason;
                source.IsEnriched = true;
                source.EnrichedAt = DateTime.UtcNow;
            }
        }

        return sourcesToEnrich;
    }

    private class EnrichedCitationDto
    {
        public string Id { get; set; } = string.Empty;
        public int AuthorityScore { get; set; }
        public int InfluenceScore { get; set; }
        public int CitationFrequency { get; set; }
        public int CompetitorCoverage { get; set; }
        public int OpportunityScore { get; set; }
        public int MentionProbability { get; set; }
        public string? Reason { get; set; }
    }
}
