using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzeCitationsCommand : IRequest<CitationAnalysisResult>
{
    public Guid OrganizationId { get; set; }
}

public class CitationAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int SourcesAnalyzed { get; set; }
}

public class CitationResponse
{
    public CitationSummaryResponse? summary { get; set; }
    public List<CitationItemResponse>? citationSources { get; set; }
}

public class CitationSummaryResponse
{
    public double totalSources { get; set; }
    public double averageAuthorityScore { get; set; }
    public double averageInfluenceScore { get; set; }
    public string? highestOpportunitySource { get; set; }
    public string? mostInfluentialSource { get; set; }
}

public class CitationItemResponse
{
    public double rank { get; set; }
    public string? source { get; set; }
    public string? category { get; set; }
    public double authorityScore { get; set; }
    public double influenceScore { get; set; }
    public double citationFrequency { get; set; }
    public double competitorCoverage { get; set; }
    public double opportunityScore { get; set; }
    public double mentionProbability { get; set; }
    public string? reason { get; set; }
}

public class AnalyzeCitationsCommandHandler : IRequestHandler<AnalyzeCitationsCommand, CitationAnalysisResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openRouterService;

    public AnalyzeCitationsCommandHandler(
        IWebsiteRepository websiteRepository,
        IOpenAiService openRouterService)
    {
        _websiteRepository = websiteRepository;
        _openRouterService = openRouterService;
    }

    public async Task<CitationAnalysisResult> Handle(AnalyzeCitationsCommand request, CancellationToken cancellationToken)
    {
        // 0. Check if already analyzed
        var existingSummary = await _websiteRepository.GetCitationSummaryAsync(request.OrganizationId);
        if (existingSummary != null)
        {
            return new CitationAnalysisResult
            {
                Success = true,
                SourcesAnalyzed = existingSummary.TotalSources
            };
        }

        // 1. Get required data
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null)
            return new CitationAnalysisResult { Success = false, Error = "Website profile not found." };

        var existingPrompts = await _websiteRepository.GetAiSearchPromptsAsync(request.OrganizationId);
        var platformVisibilities = await _websiteRepository.GetPlatformVisibilitiesAsync(request.OrganizationId);

        var promptAnalysisJson = JsonSerializer.Serialize(existingPrompts.Select(p => new {
            Query = p.QueryString,
            VisibilityScore = p.VisibilityScore,
            BrandStrength = p.BrandStrength
        }));

        var platformScoresJson = JsonSerializer.Serialize(platformVisibilities.Select(p => new {
            Platform = p.Platform,
            VisibilityScore = p.VisibilityScore
        }));

        string systemPrompt = "You are an expert in Generative Engine Optimization (GEO), AI Search, SEO, Knowledge Graphs, Entity Recognition, and Competitive Intelligence.";

        string userPrompt = $@"Your task is to identify the websites and knowledge sources most likely to influence AI-generated answers for the provided business and industry.

## Input

Website
{profile.WebsiteUrl}

Website Profile
{profile.RawProfileJson}

Competitor Profile
(Derived from industry data)

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
Source Categories
Examples include: Official Documentation, Industry Publications, Technology News, Business Directories, Review Platforms, Software Marketplaces, Developer Communities, Q&A Communities, Blogs, Knowledge Bases, Research Organizations, Standards Bodies, Open Source Platforms, Professional Networks, Educational Platforms

------------------------------------------------
Preferred Sources
Include industry-relevant websites such as: Official product/vendor websites, GitHub, Stack Overflow, Medium, Dev.to, LinkedIn, Crunchbase, G2, Capterra, Gartner, Forrester, IDC, Reddit, Hacker News, TechCrunch, VentureBeat, Wired, ZDNet, InfoWorld, CIO.com, AWS Documentation, Microsoft Learn, Google Cloud Documentation, Cloudflare Docs, Mozilla MDN, npm, PyPI
Also include niche websites relevant to the provided industry whenever appropriate.

------------------------------------------------
Ranking Rules
Sort by Influence Score descending.

------------------------------------------------
Return exactly this schema
{{
  ""summary"": {{
    ""totalSources"": 0,
    ""averageAuthorityScore"": 0,
    ""averageInfluenceScore"": 0,
    ""highestOpportunitySource"": """",
    ""mostInfluentialSource"": """"
  }},
  ""citationSources"": [
    {{
      ""rank"": 1,
      ""source"": """",
      ""category"": """",
      ""authorityScore"": 0,
      ""influenceScore"": 0,
      ""citationFrequency"": 0,
      ""competitorCoverage"": 0,
      ""opportunityScore"": 0,
      ""mentionProbability"": 0,
      ""reason"": """"
    }}
  ]
}}

Reason
Provide a concise explanation (1–2 sentences) describing why this source is influential for AI-generated answers and why it represents an opportunity (or challenge) for the business.

Finally calculate
- totalSources
- averageAuthorityScore
- averageInfluenceScore
- highestOpportunitySource
- mostInfluentialSource

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

            var result = JsonSerializer.Deserialize<CitationResponse>(responseContent, options);

            if (result != null && result.citationSources != null && result.summary != null)
            {
                var summaryId = Guid.NewGuid();
                var summary = new Citationly.Domain.Entities.CitationSummary
                {
                    Id = summaryId,
                    OrganizationId = request.OrganizationId,
                    TotalSources = (int)Math.Round(result.summary.totalSources),
                    AverageAuthorityScore = (int)Math.Round(result.summary.averageAuthorityScore),
                    AverageInfluenceScore = (int)Math.Round(result.summary.averageInfluenceScore),
                    HighestOpportunitySource = result.summary.highestOpportunitySource ?? "",
                    MostInfluentialSource = result.summary.mostInfluentialSource ?? ""
                };

                var sources = result.citationSources.Select(p => new Citationly.Domain.Entities.CitationSource
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = request.OrganizationId,
                    Rank = (int)Math.Round(p.rank),
                    Source = p.source ?? "",
                    Category = p.category ?? "",
                    AuthorityScore = (int)Math.Round(p.authorityScore),
                    InfluenceScore = (int)Math.Round(p.influenceScore),
                    CitationFrequency = (int)Math.Round(p.citationFrequency),
                    CompetitorCoverage = (int)Math.Round(p.competitorCoverage),
                    OpportunityScore = (int)Math.Round(p.opportunityScore),
                    MentionProbability = (int)Math.Round(p.mentionProbability),
                    Reason = p.reason ?? ""
                }).ToList();

                await _websiteRepository.InsertCitationsAsync(summary, sources);

                return new CitationAnalysisResult
                {
                    Success = true,
                    SourcesAnalyzed = sources.Count
                };
            }
            else
            {
                return new CitationAnalysisResult { Success = false, Error = "Failed to parse AI response." };
            }
        }
        catch (Exception ex)
        {
            return new CitationAnalysisResult { Success = false, Error = ex.Message };
        }
    }
}
