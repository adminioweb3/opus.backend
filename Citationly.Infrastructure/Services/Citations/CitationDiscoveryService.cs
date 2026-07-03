using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Citations;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Citations;

public class CitationDiscoveryService : ICitationDiscoveryService
{
    private readonly IOpenAiService _openRouterService;

    public CitationDiscoveryService(IOpenAiService openRouterService)
    {
        _openRouterService = openRouterService;
    }

    public async Task<List<CitationSource>> DiscoverCitationsAsync(
        Guid organizationId, 
        string websiteUrl, 
        string websiteProfileJson, 
        string promptAnalysisJson, 
        string platformScoresJson)
    {
        string systemPrompt = "You are an expert in Generative Engine Optimization (GEO), AI Search, SEO, Knowledge Graphs, Entity Recognition, and Competitive Intelligence.";

        string userPrompt = $@"Your task is to identify the websites and knowledge sources most likely to influence AI-generated answers for the provided business and industry.

## Input

Website
{websiteUrl}

Website Profile
{websiteProfileJson}

Prompt Analysis
{promptAnalysisJson}

Platform Scores
{platformScoresJson}

## Objective

Estimate which websites, publications, directories, documentation platforms, communities, and knowledge sources are most likely to influence AI-generated answers for this business and its competitors.

Assume AI assistants use a combination of:
- Public web content
- Knowledge graphs
- Technical documentation
- Educational resources
- High-authority publications
- Industry directories
- Community discussions
- Trusted reference websites

Generate between **30 and 75** citation sources.

Return ONLY valid JSON.
Do NOT include markdown.
Do NOT include explanations.
Do NOT wrap inside ```json.

------------------------------------------------
Source Categories
Examples include: Official Documentation, Industry Publications, Technology News, Business Directories, Review Platforms, Software Marketplaces, Developer Communities, Q&A Communities, Blogs, Knowledge Bases, Research Organizations, Standards Bodies, Open Source Platforms, Professional Networks, Educational Platforms

------------------------------------------------
Preferred Sources
Include industry-relevant websites.
Also include niche websites relevant to the provided industry whenever appropriate.

------------------------------------------------
Return exactly this schema:
{{
  ""sources"": [
    {{
      ""rank"": 1,
      ""source"": """",
      ""category"": """",
      ""reason"": """"
    }}
  ]
}}

Reason
Provide a concise explanation (maximum 20 words) describing why this source is influential for AI-generated answers.

Return ONLY the JSON object.";

        var responseContent = await _openRouterService.GenerateContentAsync(
            prompt: userPrompt,
            systemPrompt: systemPrompt,
            requireJson: true,
            model: "gpt-4o-mini");

        responseContent = responseContent.Trim();
        
        // Find the start and end of the JSON object in case there's any surrounding text
        int startIdx = responseContent.IndexOf('{');
        int endIdx = responseContent.LastIndexOf('}');
        
        if (startIdx >= 0 && endIdx >= startIdx)
        {
            responseContent = responseContent.Substring(startIdx, endIdx - startIdx + 1);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        var parsed = JsonSerializer.Deserialize<CitationDiscoveryResponseDto>(responseContent, options);

        if (parsed == null || parsed.Sources == null)
            return new List<CitationSource>();

        return parsed.Sources.Select(p => new CitationSource
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Rank = p.Rank,
            Source = p.Source ?? "",
            Category = p.Category ?? "",
            Reason = p.Reason ?? "",
            AuthorityScore = 0,
            InfluenceScore = 0,
            CitationFrequency = 0,
            CompetitorCoverage = 0,
            OpportunityScore = 0,
            MentionProbability = 0,
            IsEnriched = false,
            CreatedAt = DateTime.UtcNow
        }).ToList();
    }

    private class CitationDiscoveryResponseDto
    {
        public List<CitationSourceDto>? Sources { get; set; }
    }

    private class CitationSourceDto
    {
        public int Rank { get; set; }
        public string? Source { get; set; }
        public string? Category { get; set; }
        public string? Reason { get; set; }
    }
}
