using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzePersonasCommand : IRequest<PersonaAnalysisResult>
{
    public Guid OrganizationId { get; set; }
}

public class PersonaAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int PersonasAnalyzed { get; set; }
    public PersonaAnalysisSummary? Summary { get; set; }
    public List<PersonaScore>? Scores { get; set; }
}

public class PersonaAnalysisResponse
{
    public PersonaSummaryResponse? summary { get; set; }
    public List<PersonaScoreResponse>? personaScores { get; set; }
}

public class PersonaSummaryResponse
{
    public double overallVisibility { get; set; }
    public string? strongestPersona { get; set; }
    public string? weakestPersona { get; set; }
    public double averageShareOfVoice { get; set; }
}

public class PersonaScoreResponse
{
    public string? persona { get; set; }
    public double visibility { get; set; }
    public string? averageRank { get; set; }
    public double shareOfVoice { get; set; }
    public List<string>? topCompetitors { get; set; }
    public List<string>? recommendedContent { get; set; }
    public string? reason { get; set; }
}

public class AnalyzePersonasCommandHandler : IRequestHandler<AnalyzePersonasCommand, PersonaAnalysisResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openRouterService;

    public AnalyzePersonasCommandHandler(
        IWebsiteRepository websiteRepository,
        IOpenAiService openRouterService)
    {
        _websiteRepository = websiteRepository;
        _openRouterService = openRouterService;
    }

    public async Task<PersonaAnalysisResult> Handle(AnalyzePersonasCommand request, CancellationToken cancellationToken)
    {
        // 0. Check if already analyzed
        var existingSummary = await _websiteRepository.GetPersonaAnalysisSummaryAsync(request.OrganizationId);
        if (existingSummary != null)
        {
            var existingScores = await _websiteRepository.GetPersonaScoresAsync(request.OrganizationId);
            return new PersonaAnalysisResult
            {
                Success = true,
                PersonasAnalyzed = existingScores.Count(),
                Summary = existingSummary,
                Scores = existingScores.ToList()
            };
        }

        // 1. Get required data
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null)
            return new PersonaAnalysisResult { Success = false, Error = "Website profile not found." };

        var existingPrompts = await _websiteRepository.GetAiSearchPromptsAsync(request.OrganizationId);
        var platformVisibilities = await _websiteRepository.GetPlatformVisibilitiesAsync(request.OrganizationId);

        var promptAnalysisJson = JsonSerializer.Serialize(existingPrompts.Select(p => new
        {
            Query = p.QueryString,
            VisibilityScore = p.VisibilityScore,
            BrandStrength = p.BrandStrength
        }));

        var platformScoresJson = JsonSerializer.Serialize(platformVisibilities.Select(p => new
        {
            Platform = p.Platform,
            VisibilityScore = p.VisibilityScore
        }));

        var generatedPromptsJson = JsonSerializer.Serialize(existingPrompts.Select(p => p.QueryString));

        string systemPrompt = "You are an expert in Generative Engine Optimization (GEO), Buyer Journey Analysis, AI Search Visibility, SEO, Content Strategy, and B2B Marketing.";

        string userPrompt = $@"Your task is to estimate the business's AI visibility for different customer personas.

## Input

Website
{profile.WebsiteUrl}

Website Profile
{profile.RawProfileJson}

Competitor Profile
(Derived from industry data)

Generated Prompts
{generatedPromptsJson}

Prompt Analysis
{promptAnalysisJson}

Platform Scores
{platformScoresJson}

## Objective

Estimate how visible the business is for each customer persona when using AI search platforms such as ChatGPT, Claude, Gemini, Perplexity, Google AI Overview, Microsoft Copilot, Meta AI, DeepSeek, and Grok.

Evaluate each persona independently based on:

- Service relevance
- Buyer intent
- Industry fit
- Content relevance
- Brand authority
- Topical authority
- Competitor landscape
- AI visibility
- Entity recognition
- GEO readiness

Return ONLY valid JSON.

Do NOT include markdown.

Do NOT include explanations.

Do NOT wrap inside ```json.

------------------------------------------------

Analyze these personas

- Founder
- CEO
- CTO
- Developer
- Engineering Manager
- Product Manager
- Startup
- Enterprise Buyer
- Marketing Director
- Operations Manager

------------------------------------------------

Scoring Rules

Visibility

0–100

Meaning

0–20 Very Low

21–40 Low

41–60 Moderate

61–80 High

81–100 Excellent

------------------------------------------------

Average Rank

Choose one

1–3

4–10

11–20

21–50

50+

------------------------------------------------

Share Of Voice

0–100

Estimate how much of the AI-generated response this business would occupy compared to competitors for that persona.

------------------------------------------------

Top Competitors

Return up to 5 competitors most likely to appear ahead of the business for that persona.

------------------------------------------------

Recommended Content

Recommend content that would improve AI visibility for this persona.

Examples

- Technical blog posts
- Case studies
- Industry guides
- Comparison pages
- Pricing pages
- ROI calculators
- API documentation
- Whitepapers
- Architecture diagrams
- Video tutorials
- Product demos
- Customer success stories
- FAQ pages
- Benchmark reports

------------------------------------------------

Return exactly this schema

{{
  ""summary"": {{
    ""overallVisibility"": 0,
    ""strongestPersona"": """",
    ""weakestPersona"": """",
    ""averageShareOfVoice"": 0
  }},
  ""personaScores"": [
    {{
      ""persona"": """",
      ""visibility"": 0,
      ""averageRank"": """",
      ""shareOfVoice"": 0,
      ""topCompetitors"": [],
      ""recommendedContent"": [],
      ""reason"": """"
    }}
  ]
}}

Persona Guidance

Founder
Focus on business growth, startup speed, cost, MVPs, and innovation.

CEO
Focus on ROI, business outcomes, digital transformation, and strategic value.

CTO
Focus on architecture, scalability, security, cloud, AI, and engineering quality.

Developer
Focus on APIs, SDKs, documentation, code samples, integrations, and open-source resources.

Engineering Manager
Focus on delivery, team productivity, DevOps, cloud infrastructure, testing, and scalability.

Product Manager
Focus on product strategy, user experience, feature delivery, analytics, and roadmap planning.

Startup
Focus on affordability, rapid development, MVPs, fundraising support, and time-to-market.

Enterprise Buyer
Focus on compliance, security, scalability, procurement, vendor credibility, SLAs, and support.

Marketing Director
Focus on SEO, GEO, AI visibility, lead generation, content marketing, and analytics.

Operations Manager
Focus on workflow automation, efficiency, integrations, reporting, and operational excellence.

------------------------------------------------

Recommendations

Recommended content should be specific to each persona and designed to improve AI visibility and buyer confidence.

------------------------------------------------

Finally calculate

- overallVisibility
- strongestPersona
- weakestPersona
- averageShareOfVoice

Return ONLY the JSON object.";

        try
        {
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

            var result = JsonSerializer.Deserialize<PersonaAnalysisResponse>(responseContent, options);

            if (result != null && result.personaScores != null && result.summary != null)
            {
                var summaryId = Guid.NewGuid();
                var summary = new Citationly.Domain.Entities.PersonaAnalysisSummary
                {
                    Id = summaryId,
                    OrganizationId = request.OrganizationId,
                    OverallVisibility = (int)Math.Round(result.summary.overallVisibility),
                    StrongestPersona = result.summary.strongestPersona ?? "",
                    WeakestPersona = result.summary.weakestPersona ?? "",
                    AverageShareOfVoice = (int)Math.Round(result.summary.averageShareOfVoice),
                    CreatedAt = DateTime.UtcNow
                };

                var scores = result.personaScores.Select(p => new Citationly.Domain.Entities.PersonaScore
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = request.OrganizationId,
                    Persona = p.persona ?? "",
                    Visibility = (int)Math.Round(p.visibility),
                    AverageRank = p.averageRank ?? "",
                    ShareOfVoice = (int)Math.Round(p.shareOfVoice),
                    TopCompetitorsJson = JsonSerializer.Serialize(p.topCompetitors ?? new List<string>()),
                    RecommendedContentJson = JsonSerializer.Serialize(p.recommendedContent ?? new List<string>()),
                    Reason = p.reason ?? "",
                    CreatedAt = DateTime.UtcNow
                }).ToList();

                await _websiteRepository.InsertPersonaAnalysisAsync(summary, scores);

                return new PersonaAnalysisResult
                {
                    Success = true,
                    PersonasAnalyzed = scores.Count,
                    Summary = summary,
                    Scores = scores
                };
            }
            else
            {
                return new PersonaAnalysisResult { Success = false, Error = "Failed to parse AI response." };
            }
        }
        catch (Exception ex)
        {
            return new PersonaAnalysisResult { Success = false, Error = ex.Message };
        }
    }
}
