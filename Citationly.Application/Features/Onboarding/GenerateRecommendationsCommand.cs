using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Onboarding;

public class GenerateRecommendationsCommand : IRequest<GenerateRecommendationsResult>
{
    public Guid OrganizationId { get; set; }
}

public class GenerateRecommendationsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public class GeoRecommendationResponseWrapper
{
    public GeoRecommendationSummaryResponse? summary { get; set; }
    public List<GeoRecommendationResponse>? recommendations { get; set; }
}

public class GeoRecommendationSummaryResponse
{
    public string? overallPriority { get; set; }
    public string? estimatedOverallImpact { get; set; }
    public string? estimatedImplementationTime { get; set; }
    public int totalRecommendations { get; set; }
    public int criticalRecommendations { get; set; }
    public int highPriorityRecommendations { get; set; }
}

public class GeoRecommendationResponse
{
    public string? recommendationId { get; set; }
    public string? category { get; set; }
    public string? title { get; set; }
    public string? description { get; set; }
    public string? priority { get; set; }
    public string? estimatedImpact { get; set; }
    public string? estimatedDifficulty { get; set; }
    public string? implementationTime { get; set; }
    public string? expectedOutcome { get; set; }
    public string? successMetric { get; set; }
    public JsonElement actionItems { get; set; }
}

public class GenerateRecommendationsCommandHandler : IRequestHandler<GenerateRecommendationsCommand, GenerateRecommendationsResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenRouterService _openRouterService;

    public GenerateRecommendationsCommandHandler(
        IWebsiteRepository websiteRepository,
        IOpenRouterService openRouterService)
    {
        _websiteRepository = websiteRepository;
        _openRouterService = openRouterService;
    }

    public async Task<GenerateRecommendationsResult> Handle(GenerateRecommendationsCommand request, CancellationToken cancellationToken)
    {
        var existing = await _websiteRepository.GetGeoRecommendationSummaryAsync(request.OrganizationId);
        if (existing != null)
        {
            return new GenerateRecommendationsResult { Success = true };
        }

        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null) return new GenerateRecommendationsResult { Success = false, Error = "No profile" };

        var competitorsCount = await _websiteRepository.GetCompetitorCountAsync(request.OrganizationId);
        var promptsCount = await _websiteRepository.GetAiSearchPromptCountAsync(request.OrganizationId);
        
        var visibilitySum = await _websiteRepository.GetVisibilitySummaryAsync(request.OrganizationId);
        var platVis = await _websiteRepository.GetPlatformVisibilitiesAsync(request.OrganizationId);
        var citationsSum = await _websiteRepository.GetCitationSummaryAsync(request.OrganizationId);
        var personasSum = await _websiteRepository.GetPersonaAnalysisSummaryAsync(request.OrganizationId);
        var regionsSum = await _websiteRepository.GetRegionAnalysisSummaryAsync(request.OrganizationId);

        var systemPrompt = "You are an expert in Generative Engine Optimization (GEO), AI Search Optimization, Technical SEO, Content Strategy, Digital Marketing, and Competitive Intelligence.";
        
        var userPrompt = $@"Your task is to analyze all previous reports and generate a comprehensive GEO optimization roadmap for the business.

## Input

Website

{profile.WebsiteUrl}

Website Profile

{profile.RawProfileJson}

Competitor Profile

(Identified {competitorsCount} competitors)

Generated Prompts

(Generated {promptsCount} AI search prompts)

Prompt Analysis

Global Search Visibility: {visibilitySum?.OverallVisibilityScore ?? 0}
Estimated Brand Mention Rate: {visibilitySum?.AverageMentionRate ?? 0}

Platform Scores

(Analyzed {platVis.Count()} platforms)

Citation Analysis

Overall Citation Authority: {citationsSum?.AverageAuthorityScore ?? 0}
Top Citation Source: {citationsSum?.MostInfluentialSource ?? ""}

Persona Scores

Overall Persona Visibility: {personasSum?.OverallVisibility ?? 0}
Strongest Persona: {personasSum?.StrongestPersona ?? ""}

Region Scores

Overall Global Visibility: {regionsSum?.OverallGlobalVisibility ?? 0}
Strongest Region: {regionsSum?.StrongestRegion ?? ""}

## Objective

Generate a prioritized implementation roadmap that will maximize the business's visibility across AI-powered search platforms including:

- ChatGPT
- Claude
- Gemini
- Perplexity
- Google AI Overview
- Microsoft Copilot
- Meta AI
- DeepSeek
- Grok

Analyze all previous outputs and identify the highest-impact improvements.

Return ONLY valid JSON.

Do NOT include markdown.

Do NOT include explanations.

Do NOT wrap inside ```json.

------------------------------------------------

Generate recommendations for

- Critical Issues
- Quick Wins
- Technical SEO
- Content Improvements
- Schema Improvements
- Citation Opportunities
- Brand Authority Improvements
- Prompt Coverage Improvements
- Competitor Weaknesses

------------------------------------------------

Recommendation Rules

Each recommendation must contain

- recommendationId
- category
- title
- description
- priority
- estimatedImpact
- estimatedDifficulty
- implementationTime
- expectedOutcome
- successMetric
- actionItems

------------------------------------------------

Priority

Choose one

- Critical
- High
- Medium
- Low

------------------------------------------------

Estimated Impact

Choose one

- Very High
- High
- Medium
- Low

Represents the expected improvement in AI visibility if implemented.

------------------------------------------------

Estimated Difficulty

Choose one

- Easy
- Moderate
- Difficult
- Very Difficult

------------------------------------------------

Implementation Time

Choose one

- Less than 1 week
- 1–2 weeks
- 2–4 weeks
- 1–3 months
- 3–6 months
- 6+ months

------------------------------------------------

Action Items

Return a list of concrete implementation steps.

Examples

- Publish AI-focused landing pages
- Create FAQ schema
- Add Organization schema
- Improve service pages
- Build comparison pages
- Create technical documentation
- Publish case studies
- Acquire high-authority backlinks
- Improve EEAT signals
- Expand blog content
- Optimize internal linking
- Add author profiles
- Create industry-specific landing pages
- Improve entity consistency
- Add structured data
- Build knowledge hub
- Create API documentation
- Publish benchmark reports

------------------------------------------------

Expected Outcome

Describe the expected business or AI visibility improvement.

------------------------------------------------

Success Metric

Examples

- Increase AI mention rate
- Increase Share of Voice
- Improve Prompt Coverage
- Increase Citation Strength
- Improve Platform Visibility
- Increase Organic Traffic
- Increase Qualified Leads
- Improve Entity Recognition

------------------------------------------------

Category Guidance

Critical Issues

Identify blockers that significantly reduce AI visibility.

Quick Wins

Low-effort, high-impact improvements that can be implemented quickly.

Technical SEO

Recommendations related to crawlability, indexing, structured data, Core Web Vitals, metadata, internal linking, canonicalization, XML sitemaps, robots.txt, and technical architecture.

Content Improvements

Recommendations related to blog content, landing pages, FAQs, documentation, comparisons, industry pages, thought leadership, and topical authority.

Schema Improvements

Recommend structured data such as:

- Organization
- LocalBusiness
- Person
- Service
- Product
- FAQ
- HowTo
- Breadcrumb
- Article
- BlogPosting
- WebSite
- WebPage
- SoftwareApplication
- Review
- AggregateRating
- VideoObject

Citation Opportunities

Identify high-authority websites, directories, publications, communities, documentation sites, and industry resources where the business should improve visibility.

Brand Authority Improvements

Recommendations to improve trust, credibility, entity recognition, PR, reviews, case studies, partnerships, awards, speaking engagements, and social proof.

Prompt Coverage Improvements

Identify missing customer intents, industries, personas, buyer stages, and AI prompts that should be targeted with new content.

Competitor Weaknesses

Identify areas where competitors are weak and recommend strategies to outperform them.

------------------------------------------------

Return exactly this schema

{{
  ""summary"": {{
    ""overallPriority"": """",
    ""estimatedOverallImpact"": """",
    ""estimatedImplementationTime"": """",
    ""totalRecommendations"": 0,
    ""criticalRecommendations"": 0,
    ""highPriorityRecommendations"": 0
  }},
  ""recommendations"": [
    {{
      ""recommendationId"": ""REC-001"",
      ""category"": """",
      ""title"": """",
      ""description"": """",
      ""priority"": """",
      ""estimatedImpact"": """",
      ""estimatedDifficulty"": """",
      ""implementationTime"": """",
      ""expectedOutcome"": """",
      ""successMetric"": """",
      ""actionItems"": []
    }}
  ]
}}

------------------------------------------------

Roadmap Guidelines

Generate between **30 and 60** recommendations.

Distribute recommendations approximately as follows:

- Critical Issues: 10%
- Quick Wins: 15%
- Technical SEO: 15%
- Content Improvements: 20%
- Schema Improvements: 10%
- Citation Opportunities: 10%
- Brand Authority Improvements: 10%
- Prompt Coverage Improvements: 5%
- Competitor Weaknesses: 5%

Sort recommendations by:

1. Priority
2. Estimated Impact
3. Estimated Difficulty (Easy first)

Each recommendation should be specific, actionable, measurable, and directly supported by insights from the provided analyses.

Finally calculate:

- overallPriority
- estimatedOverallImpact
- estimatedImplementationTime
- totalRecommendations
- criticalRecommendations
- highPriorityRecommendations

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

            var parsed = JsonSerializer.Deserialize<GeoRecommendationResponseWrapper>(responseContent, options);

            if (parsed != null && parsed.summary != null && parsed.recommendations != null)
            {
                var summary = new GeoRecommendationSummary
                {
                    OrganizationId = request.OrganizationId,
                    OverallPriority = parsed.summary.overallPriority ?? "",
                    EstimatedOverallImpact = parsed.summary.estimatedOverallImpact ?? "",
                    EstimatedImplementationTime = parsed.summary.estimatedImplementationTime ?? "",
                    TotalRecommendations = parsed.summary.totalRecommendations,
                    CriticalRecommendations = parsed.summary.criticalRecommendations,
                    HighPriorityRecommendations = parsed.summary.highPriorityRecommendations
                };

                var recs = new List<GeoRecommendation>();
                foreach (var rec in parsed.recommendations)
                {
                    recs.Add(new GeoRecommendation
                    {
                        RecommendationId = rec.recommendationId ?? "",
                        Category = rec.category ?? "",
                        Title = rec.title ?? "",
                        Description = rec.description ?? "",
                        Priority = rec.priority ?? "",
                        EstimatedImpact = rec.estimatedImpact ?? "",
                        EstimatedDifficulty = rec.estimatedDifficulty ?? "",
                        ImplementationTime = rec.implementationTime ?? "",
                        ExpectedOutcome = rec.expectedOutcome ?? "",
                        SuccessMetric = rec.successMetric ?? "",
                        ActionItemsJson = JsonSerializer.Serialize(rec.actionItems, options)
                    });
                }

                await _websiteRepository.InsertGeoRecommendationsAsync(summary, recs);
                return new GenerateRecommendationsResult { Success = true };
            }
            else
            {
                return new GenerateRecommendationsResult { Success = false, Error = "Failed to parse AI response into recommendation schema." };
            }
        }
        catch (Exception ex)
        {
            return new GenerateRecommendationsResult { Success = false, Error = ex.Message };
        }
    }
}
