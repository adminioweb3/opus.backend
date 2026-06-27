using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzeRegionsCommand : IRequest<AnalyzeRegionsResult>
{
    public Guid OrganizationId { get; set; }
}

public class AnalyzeRegionsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public class RegionAnalysisResponse
{
    public RegionSummaryResponse? summary { get; set; }
    public List<RegionScoreResponse>? regionScores { get; set; }
}

public class RegionSummaryResponse
{
    public int overallGlobalVisibility { get; set; }
    public string? strongestRegion { get; set; }
    public string? weakestRegion { get; set; }
    public int averageShareOfVoice { get; set; }
}

public class RegionScoreResponse
{
    public string? region { get; set; }
    public int visibility { get; set; }
    public string? ranking { get; set; }
    public string? competitorLeader { get; set; }
    public int shareOfVoice { get; set; }
    public JsonElement contentOpportunity { get; set; }
    public string? reason { get; set; }
}

public class AnalyzeRegionsCommandHandler : IRequestHandler<AnalyzeRegionsCommand, AnalyzeRegionsResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenRouterService _openRouterService;

    public AnalyzeRegionsCommandHandler(
        IWebsiteRepository websiteRepository,
        IOpenRouterService openRouterService)
    {
        _websiteRepository = websiteRepository;
        _openRouterService = openRouterService;
    }

    public async Task<AnalyzeRegionsResult> Handle(AnalyzeRegionsCommand request, CancellationToken cancellationToken)
    {
        var existing = await _websiteRepository.GetRegionAnalysisSummaryAsync(request.OrganizationId);
        if (existing != null)
        {
            return new AnalyzeRegionsResult { Success = true };
        }

        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null) return new AnalyzeRegionsResult { Success = false, Error = "No profile" };

        var competitorsCount = await _websiteRepository.GetCompetitorCountAsync(request.OrganizationId);
        var promptsCount = await _websiteRepository.GetAiSearchPromptCountAsync(request.OrganizationId);

        var systemPrompt = "You are an expert in Generative Engine Optimization (GEO), AI Search Visibility, International SEO, Regional Market Analysis, and Competitive Intelligence.";
        
        var userPrompt = $@"Your task is to estimate the business's AI visibility across different geographic regions.

## Input

Website

{profile.WebsiteUrl}

Website Profile

{profile.RawProfileJson}

Competitor Profile

(Identified {competitorsCount} competitors)

Generated Prompts

(Generated {promptsCount} AI search prompts)

## Objective

Estimate how visible the business is across different geographic regions when users ask AI assistants such as:

- ChatGPT
- Claude
- Gemini
- Perplexity
- Google AI Overview
- Microsoft Copilot
- Meta AI
- DeepSeek
- Grok

Consider regional search behavior, local competitors, brand awareness, content relevance, language support, and market maturity.

Analyze each region independently.

Return ONLY valid JSON.

Do NOT include markdown.

Do NOT include explanations.

Do NOT wrap inside ```json.

------------------------------------------------

Analyze these regions

- USA
- India
- Canada
- UK
- Germany
- Australia
- Singapore
- Middle East
- Europe

------------------------------------------------

Scoring Rules

Visibility

0–100

Meaning

0–20 = Very Low

21–40 = Low

41–60 = Moderate

61–80 = High

81–100 = Excellent

------------------------------------------------

Ranking

Choose one

1–3
4–10
11–20
21–50
50+

------------------------------------------------

Competitor Leader

Return the competitor most likely to dominate AI-generated answers in that region.

------------------------------------------------

Share Of Voice

0–100

Estimate the percentage of AI-generated answers where this business would be represented compared to competitors.

------------------------------------------------

Content Opportunity

Return an array of recommended content opportunities that would improve regional AI visibility.

Examples

- Local landing pages
- Regional case studies
- Country-specific pricing
- Industry compliance guides
- Local customer testimonials
- Multilingual documentation
- Localized blog articles
- Regional comparison pages
- Country-specific FAQs
- Industry reports
- Local partnerships
- Regional success stories

------------------------------------------------

Regional Evaluation Factors

Estimate visibility based on

- Brand awareness in the region
- Local competitor strength
- Website localization
- Regional content relevance
- Language support
- SEO maturity
- GEO readiness
- Industry demand
- Market penetration
- Trust signals
- Citations
- Regional authority

------------------------------------------------

Return exactly this schema

{{
  ""summary"": {{
    ""overallGlobalVisibility"": 0,
    ""strongestRegion"": """",
    ""weakestRegion"": """",
    ""averageShareOfVoice"": 0
  }},
  ""regionScores"": [
    {{
      ""region"": """",
      ""visibility"": 0,
      ""ranking"": """",
      ""competitorLeader"": """",
      ""shareOfVoice"": 0,
      ""contentOpportunity"": [],
      ""reason"": """"
    }}
  ]
}}

------------------------------------------------

Region Guidance

USA

Focus on enterprise adoption, AI maturity, SaaS competition, technical authority, and strong brand recognition.

India

Focus on startups, IT services, outsourcing, software development, AI services, and competitive pricing.

Canada

Focus on enterprise software, cloud, AI consulting, and digital transformation.

UK

Focus on financial services, enterprise consulting, AI adoption, compliance, and technology consulting.

Germany

Focus on engineering excellence, manufacturing, Industry 4.0, enterprise software, and compliance.

Australia

Focus on cloud adoption, enterprise modernization, SaaS, and managed services.

Singapore

Focus on fintech, AI innovation, cloud-native businesses, and regional technology hubs.

Middle East

Focus on government digital transformation, smart cities, AI adoption, cloud migration, and enterprise modernization.

Europe

Focus on multilingual markets, GDPR compliance, enterprise software, digital transformation, and cross-border scalability.

------------------------------------------------

Recommendations

Content opportunities should be specific to the region and should improve AI visibility, authority, and market relevance.

------------------------------------------------

Finally calculate

- overallGlobalVisibility
- strongestRegion
- weakestRegion
- averageShareOfVoice

Return ONLY the JSON object.";

        try
        {
            var responseContent = await _openRouterService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: systemPrompt,
                requireJson: true,
                model: "meta-llama/llama-3.3-70b-instruct:free");

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

            var parsed = JsonSerializer.Deserialize<RegionAnalysisResponse>(responseContent, options);

            if (parsed != null && parsed.summary != null && parsed.regionScores != null)
            {
                var summary = new RegionAnalysisSummary
                {
                    OrganizationId = request.OrganizationId,
                    OverallGlobalVisibility = parsed.summary.overallGlobalVisibility,
                    StrongestRegion = parsed.summary.strongestRegion ?? "",
                    WeakestRegion = parsed.summary.weakestRegion ?? "",
                    AverageShareOfVoice = parsed.summary.averageShareOfVoice
                };

                var scores = new List<RegionScore>();
                foreach (var sc in parsed.regionScores)
                {
                    scores.Add(new RegionScore
                    {
                        Region = sc.region ?? "",
                        Visibility = sc.visibility,
                        Ranking = sc.ranking ?? "",
                        CompetitorLeader = sc.competitorLeader ?? "",
                        ShareOfVoice = sc.shareOfVoice,
                        ContentOpportunityJson = JsonSerializer.Serialize(sc.contentOpportunity, options),
                        Reason = sc.reason ?? ""
                    });
                }

                await _websiteRepository.InsertRegionAnalysisAsync(summary, scores);
                return new AnalyzeRegionsResult { Success = true };
            }
            else
            {
                return new AnalyzeRegionsResult { Success = false, Error = "Failed to parse AI response into region schema." };
            }
        }
        catch (Exception ex)
        {
            return new AnalyzeRegionsResult { Success = false, Error = ex.Message };
        }
    }
}
