using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Onboarding;

public class GenerateExecutiveSummaryCommand : IRequest<GenerateExecutiveSummaryResult>
{
    public Guid OrganizationId { get; set; }
}

public class GenerateExecutiveSummaryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public ExecutiveSummaryResponseWrapper? data { get; set; }
}

public class ExecutiveSummaryScoresResponse
{
    public int overallGEOScore { get; set; }
    public int overallAIVisibilityScore { get; set; }
    public int overallSEOScore { get; set; }
    public int overallBrandAuthority { get; set; }
    public int overallContentScore { get; set; }
}

public class ExecutiveSummaryExecResponse
{
    public string? overallAssessment { get; set; }
    public string? topPriorityRecommendation { get; set; }
    public string? expectedBusinessImpact { get; set; }
    public JsonElement nextSteps { get; set; }
}

public class ExecutiveSummaryResponseWrapper
{
    public string? businessOverview { get; set; }
    public string? currentAIVisibility { get; set; }
    public string? competitorPosition { get; set; }
    public string? platformPerformance { get; set; }
    public string? topicPerformance { get; set; }
    public string? promptPerformance { get; set; }
    public string? citationSummary { get; set; }
    public JsonElement strengths { get; set; }
    public JsonElement weaknesses { get; set; }
    public JsonElement opportunities { get; set; }
    public JsonElement threats { get; set; }
    public ExecutiveSummaryScoresResponse? scores { get; set; }
    public ExecutiveSummaryExecResponse? executiveSummary { get; set; }
}

public class GenerateExecutiveSummaryCommandHandler : IRequestHandler<GenerateExecutiveSummaryCommand, GenerateExecutiveSummaryResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openRouterService;

    public GenerateExecutiveSummaryCommandHandler(
        IWebsiteRepository websiteRepository,
        IOpenAiService openRouterService)
    {
        _websiteRepository = websiteRepository;
        _openRouterService = openRouterService;
    }

    public async Task<GenerateExecutiveSummaryResult> Handle(GenerateExecutiveSummaryCommand request, CancellationToken cancellationToken)
    {
        var existing = await _websiteRepository.GetExecutiveSummaryAsync(request.OrganizationId);
        if (existing != null)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var existingData = new ExecutiveSummaryResponseWrapper
            {
                businessOverview = existing.BusinessOverview,
                currentAIVisibility = existing.CurrentAIVisibility,
                competitorPosition = existing.CompetitorPosition,
                platformPerformance = existing.PlatformPerformance,
                topicPerformance = existing.TopicPerformance,
                promptPerformance = existing.PromptPerformance,
                citationSummary = existing.CitationSummary,
                strengths = !string.IsNullOrEmpty(existing.StrengthsJson) ? JsonDocument.Parse(existing.StrengthsJson).RootElement : default,
                weaknesses = !string.IsNullOrEmpty(existing.WeaknessesJson) ? JsonDocument.Parse(existing.WeaknessesJson).RootElement : default,
                opportunities = !string.IsNullOrEmpty(existing.OpportunitiesJson) ? JsonDocument.Parse(existing.OpportunitiesJson).RootElement : default,
                threats = !string.IsNullOrEmpty(existing.ThreatsJson) ? JsonDocument.Parse(existing.ThreatsJson).RootElement : default,
                scores = new ExecutiveSummaryScoresResponse
                {
                    overallGEOScore = existing.OverallGEOScore,
                    overallAIVisibilityScore = existing.OverallAIVisibilityScore,
                    overallSEOScore = existing.OverallSEOScore,
                    overallBrandAuthority = existing.OverallBrandAuthority,
                    overallContentScore = existing.OverallContentScore
                },
                executiveSummary = new ExecutiveSummaryExecResponse
                {
                    overallAssessment = existing.OverallAssessment,
                    topPriorityRecommendation = existing.TopPriorityRecommendation,
                    expectedBusinessImpact = existing.ExpectedBusinessImpact,
                    nextSteps = !string.IsNullOrEmpty(existing.NextStepsJson) ? JsonDocument.Parse(existing.NextStepsJson).RootElement : default
                }
            };
            return new GenerateExecutiveSummaryResult { Success = true, data = existingData };
        }

        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null) return new GenerateExecutiveSummaryResult { Success = false, Error = "No profile" };

        var competitorsCount = await _websiteRepository.GetCompetitorCountAsync(request.OrganizationId);
        var promptsCount = await _websiteRepository.GetAiSearchPromptCountAsync(request.OrganizationId);

        var visibilitySum = await _websiteRepository.GetVisibilitySummaryAsync(request.OrganizationId);
        var platVis = await _websiteRepository.GetPlatformVisibilitiesAsync(request.OrganizationId);
        var citationsSum = await _websiteRepository.GetCitationSummaryAsync(request.OrganizationId);
        var personasSum = await _websiteRepository.GetPersonaAnalysisSummaryAsync(request.OrganizationId);
        var regionsSum = await _websiteRepository.GetRegionAnalysisSummaryAsync(request.OrganizationId);
        var recsSum = await _websiteRepository.GetGeoRecommendationSummaryAsync(request.OrganizationId);

        var systemPrompt = "You are an expert in Generative Engine Optimization (GEO), AI Search Visibility, Executive Reporting, SEO, Competitive Intelligence, and Digital Strategy.";

        var userPrompt = $@"Your task is to create a professional executive summary based on all previous analyses.

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

Recommendations

Total Recommendations: {recsSum?.TotalRecommendations ?? 0}
Critical Priority: {recsSum?.CriticalRecommendations ?? 0}
Overall Priority: {recsSum?.OverallPriority ?? ""}
Overall Impact: {recsSum?.EstimatedOverallImpact ?? ""}

## Objective

Generate an executive-level business summary that explains the company's current AI visibility, competitive position, strengths, weaknesses, and opportunities.

The report should be suitable for:

- CEO
- Founder
- CTO
- CMO
- Marketing Director
- Board Members
- Investors

Summarize all previous analyses into concise, actionable insights.

Return ONLY valid JSON.

Do NOT include markdown.

Do NOT include explanations.

Do NOT wrap inside ```json.

------------------------------------------------

Include

• Business Overview
• Current AI Visibility
• Competitor Position
• Platform Performance
• Topic Performance
• Prompt Performance
• Citation Summary
• Strengths
• Weaknesses
• Opportunities
• Threats
• Overall GEO Score
• Overall AI Visibility Score
• Overall SEO Score
• Overall Brand Authority
• Overall Content Score

------------------------------------------------

Scoring Rules

Overall GEO Score

0–100

Represents overall readiness for Generative Engine Optimization.

------------------------------------------------

Overall AI Visibility Score

0–100

Represents expected visibility across AI platforms.

------------------------------------------------

Overall SEO Score

0–100

Estimate based on

- Technical SEO
- Content quality
- Structured data
- Internal linking
- Website architecture
- EEAT
- Topical authority

------------------------------------------------

Overall Brand Authority

0–100

Estimate based on

- Brand recognition
- Entity recognition
- Reputation
- Trust
- Market awareness
- Industry authority

------------------------------------------------

Overall Content Score

0–100

Estimate based on

- Content depth
- Topical coverage
- Freshness
- Documentation
- Case studies
- Educational resources
- Blog quality

------------------------------------------------

Business Overview

Provide a concise 2–4 sentence summary describing:

- Business
- Services
- Target customers
- Market position

------------------------------------------------

Current AI Visibility

Summarize

- AI platform presence
- Prompt coverage
- Estimated share of voice
- Overall visibility

------------------------------------------------

Competitor Position

Summarize

- Relative market position
- Competitive strengths
- Competitive gaps
- Top competitors

------------------------------------------------

Platform Performance

Provide a concise summary of performance across

- ChatGPT
- Claude
- Gemini
- Perplexity
- Google AI Overview
- Microsoft Copilot
- Meta AI
- DeepSeek
- Grok

Mention strongest and weakest platforms.

------------------------------------------------

Topic Performance

Summarize visibility across major topics such as

- AI Development
- Software Development
- Product Engineering
- Cloud
- DevOps
- Generative AI
- Enterprise AI
- Automation
- Technology Consulting
- Web Development
- Mobile Development
- UI/UX

Highlight strongest and weakest topics.

------------------------------------------------

Prompt Performance

Summarize

- Prompt coverage
- High-performing prompts
- Low-performing prompts
- customer intent coverage

------------------------------------------------

Citation Summary

Summarize

- Citation strength
- Authority
- Key citation sources
- Missing opportunities

------------------------------------------------

Strengths

Return an array containing the business's strongest advantages.

Examples

- Strong technical expertise
- High topical authority
- Quality service pages
- Comprehensive documentation
- Strong AI focus
- Excellent engineering capabilities
- Good content quality

------------------------------------------------

Weaknesses

Return an array of key limitations reducing AI visibility.

------------------------------------------------

Opportunities

Return an array of the highest-impact opportunities identified across all analyses.

------------------------------------------------

Threats

Return an array of external risks such as

- Strong competitors
- Weak brand awareness
- Limited citations
- Regional competition
- Rapidly evolving AI search
- Low topical coverage

------------------------------------------------

Return exactly this schema

{{
  ""businessOverview"": """",
  ""currentAIVisibility"": """",
  ""competitorPosition"": """",
  ""platformPerformance"": """",
  ""topicPerformance"": """",
  ""promptPerformance"": """",
  ""citationSummary"": """",
  ""strengths"": [],
  ""weaknesses"": [],
  ""opportunities"": [],
  ""threats"": [],
  ""scores"": {{
    ""overallGEOScore"": 0,
    ""overallAIVisibilityScore"": 0,
    ""overallSEOScore"": 0,
    ""overallBrandAuthority"": 0,
    ""overallContentScore"": 0
  }},
  ""executiveSummary"": {{
    ""overallAssessment"": """",
    ""topPriorityRecommendation"": """",
    ""expectedBusinessImpact"": """",
    ""nextSteps"": []
  }}
}}

------------------------------------------------

Executive Summary Guidelines

- Use concise executive language.
- Focus on strategic insights rather than technical details.
- Every conclusion must be supported by the provided analyses.
- Do not invent facts beyond reasonable estimation.
- Keep narrative sections brief (2–5 sentences each).
- Ensure the scores are internally consistent with previous analyses.

Finally generate:

- overallAssessment
- topPriorityRecommendation
- expectedBusinessImpact
- nextSteps

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

            var parsed = JsonSerializer.Deserialize<ExecutiveSummaryResponseWrapper>(responseContent, options);

            if (parsed != null && parsed.scores != null && parsed.executiveSummary != null)
            {
                var summary = new ExecutiveSummaryData
                {
                    OrganizationId = request.OrganizationId,
                    BusinessOverview = parsed.businessOverview ?? "",
                    CurrentAIVisibility = parsed.currentAIVisibility ?? "",
                    CompetitorPosition = parsed.competitorPosition ?? "",
                    PlatformPerformance = parsed.platformPerformance ?? "",
                    TopicPerformance = parsed.topicPerformance ?? "",
                    PromptPerformance = parsed.promptPerformance ?? "",
                    CitationSummary = parsed.citationSummary ?? "",
                    StrengthsJson = JsonSerializer.Serialize(parsed.strengths, options),
                    WeaknessesJson = JsonSerializer.Serialize(parsed.weaknesses, options),
                    OpportunitiesJson = JsonSerializer.Serialize(parsed.opportunities, options),
                    ThreatsJson = JsonSerializer.Serialize(parsed.threats, options),
                    OverallGEOScore = parsed.scores.overallGEOScore,
                    OverallAIVisibilityScore = parsed.scores.overallAIVisibilityScore,
                    OverallSEOScore = parsed.scores.overallSEOScore,
                    OverallBrandAuthority = parsed.scores.overallBrandAuthority,
                    OverallContentScore = parsed.scores.overallContentScore,
                    OverallAssessment = parsed.executiveSummary.overallAssessment ?? "",
                    TopPriorityRecommendation = parsed.executiveSummary.topPriorityRecommendation ?? "",
                    ExpectedBusinessImpact = parsed.executiveSummary.expectedBusinessImpact ?? "",
                    NextStepsJson = JsonSerializer.Serialize(parsed.executiveSummary.nextSteps, options)
                };

                await _websiteRepository.InsertExecutiveSummaryAsync(summary);
                return new GenerateExecutiveSummaryResult { Success = true, data = parsed };
            }
            else
            {
                return new GenerateExecutiveSummaryResult { Success = false, Error = "Failed to parse AI response into executive summary schema." };
            }
        }
        catch (Exception ex)
        {
            return new GenerateExecutiveSummaryResult { Success = false, Error = ex.Message };
        }
    }
}
