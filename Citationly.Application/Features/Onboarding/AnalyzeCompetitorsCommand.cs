using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzeCompetitorsCommand : IRequest<CompetitorAnalysisResult>
{
    public Guid OrganizationId { get; set; }
}

public class CompetitorAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalCompetitors { get; set; }
}

public class CompAnalysisResponse
{
    public CompBusiness? business { get; set; }
    public List<CompCompetitor>? competitors { get; set; }
    public CompSummary? summary { get; set; }
}

public class CompBusiness
{
    public string? name { get; set; }
    public string? website { get; set; }
    public string? industry { get; set; }
}

public class CompSummary
{
    public int totalCompetitors { get; set; }
    public int directCompetitors { get; set; }
    public int indirectCompetitors { get; set; }
    public string? industry { get; set; }
    public string? marketOverview { get; set; }
}

public class CompCompetitor
{
    public int rank { get; set; }
    public string? companyName { get; set; }
    public string? website { get; set; }
    public string? industry { get; set; }
    public string? description { get; set; }
    public JsonElement? services { get; set; }
    public string? employees { get; set; }
    public string? headquarters { get; set; }
    public string? founded { get; set; }
    public string? marketSegment { get; set; }
    public JsonElement? targetAudience { get; set; }
    public JsonElement? strengths { get; set; }
    public JsonElement? weaknesses { get; set; }
    public JsonElement? estimatedTraffic { get; set; }
    public JsonElement? estimatedBrandAuthority { get; set; }
    public JsonElement? estimatedAIVisibility { get; set; }
    public JsonElement? estimatedCitationScore { get; set; }
    public JsonElement? estimatedContentStrength { get; set; }
    public JsonElement? estimatedGEOReadiness { get; set; }
    public JsonElement? estimatedSEOStrength { get; set; }
    public JsonElement? estimatedTrustScore { get; set; }
    public int similarityScore { get; set; }
    public int confidence { get; set; }
}


public class AnalyzeCompetitorsCommandHandler : IRequestHandler<AnalyzeCompetitorsCommand, CompetitorAnalysisResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IScrapingJobRepository _scrapingRepository;
    private readonly IOpenRouterService _openRouterService;

    public AnalyzeCompetitorsCommandHandler(
        IWebsiteRepository websiteRepository,
        IScrapingJobRepository scrapingRepository,
        IOpenRouterService openRouterService)
    {
        _websiteRepository = websiteRepository;
        _scrapingRepository = scrapingRepository;
        _openRouterService = openRouterService;
    }

    public async Task<CompetitorAnalysisResult> Handle(AnalyzeCompetitorsCommand request, CancellationToken cancellationToken)
    {
        // 0. Check if competitors already exist for this organization
        int existingCount = await _websiteRepository.GetCompetitorCountAsync(request.OrganizationId);
        if (existingCount > 0)
        {
            // Already processed! Return success immediately
            return new CompetitorAnalysisResult
            {
                Success = true,
                TotalCompetitors = existingCount
            };
        }

        // 1. Get the latest Website Profile
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null)
        {
            return new CompetitorAnalysisResult { Success = false, Error = "No website profile found for this organization. Please run the analysis step first." };
        }

        // 2. We can either get the raw text from the profile's scraped data, or query the scraping jobs.
        // Let's get the scraped pages for this website.
        string websiteContent = "";
        try
        {
            var jobs = await _scrapingRepository.GetAllJobsByOrgAsync(request.OrganizationId);
            var latestJob = jobs.Where(j => j.Url.Contains(profile.WebsiteUrl) || profile.WebsiteUrl.Contains(j.Url))
                                .OrderByDescending(j => j.CreatedAt)
                                .FirstOrDefault();

            if (latestJob != null)
            {
                var pages = await _scrapingRepository.GetPagesByJobIdAsync(latestJob.Id);
                var topPages = pages.Take(5).ToList();
                var sb = new System.Text.StringBuilder();
                foreach (var page in topPages)
                {
                    sb.AppendLine($"--- PAGE: {page.Url} ---");
                    sb.AppendLine(page.MarkdownContent ?? page.Content ?? "");
                    sb.AppendLine();
                }
                websiteContent = sb.ToString();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching scraped data for competitors: {ex.Message}");
        }

        // Parse the initial analysis to get context
        string keywords = "Unknown";
        string targetAudience = "Unknown";
        string industry = "Unknown";

        if (!string.IsNullOrEmpty(profile.RawProfileJson))
        {
            try
            {
                var doc = JsonDocument.Parse(profile.RawProfileJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("business", out var biz))
                {
                    if (biz.TryGetProperty("industry", out var ind)) industry = ind.GetString() ?? "Unknown";
                    if (biz.TryGetProperty("keywords", out var kw)) keywords = kw.GetString() ?? "Unknown";
                    if (biz.TryGetProperty("targetAudience", out var ta)) targetAudience = ta.GetString() ?? "Unknown";
                }
            }
            catch { }
        }


        // 3. Prepare the massive prompt
        var systemPrompt = "You are an expert Competitive Intelligence, Market Research, SEO, and Business Analysis AI.";

        var userPrompt = $@"Your task is to identify the most relevant competitors for the provided business and generate a comprehensive competitor intelligence report.

## Input

Website: {profile.WebsiteUrl}
Business Name: {profile.BusinessName}
Industry: {industry}
Keywords: {keywords}
TargetAudience: {targetAudience}

[Scraped Website Content]
{websiteContent}
[/Scraped Website Content]

## Objective

Identify between **10 and 15** companies that compete directly or indirectly with this business.

Rank competitors from the most similar to the least similar.

Use your understanding of the business, website content, industry, products, services, target audience, pricing model, and market positioning.

Prefer direct competitors over indirect competitors.

## Important Rules

1. Return ONLY valid JSON.
2. Do NOT use markdown.
3. Do NOT include explanations.
4. Do NOT wrap the response inside ```json.
5. If a value cannot be verified, use: null, [], or ""Unknown"".
6. Never invent specific facts.
7. Fields marked as ""Estimated"" should be reasonable estimates based on public knowledge and industry characteristics.
8. Rankings should primarily consider: Similar products, Similar services, Similar audience, Similar pricing, Similar market, Similar business model, Similar positioning
9. Every company must include a confidence score between 0 and 100.
10. Similarity Score is from 0-100.
11. Estimated metric scores are integers from 0 to 100.
12. All arrays must contain unique values.
13. Do not duplicate competitors.
14. Sort competitors by Similarity Score descending.
15. CRITICAL: You MUST return a minimum of 10 competitors. Do NOT stop early. You must generate at least 10 distinct competitor objects in the array!

Return exactly this JSON schema.

{{
  ""business"": {{
    ""name"": """",
    ""website"": """",
    ""industry"": """"
  }},
  ""competitors"": [
    {{
      ""rank"": 1,
      ""companyName"": """",
      ""website"": """",
      ""industry"": """",
      ""description"": """",
      ""services"": [],
      ""employees"": ""Unknown"",
      ""headquarters"": ""Unknown"",
      ""founded"": ""Unknown"",
      ""marketSegment"": """",
      ""targetAudience"": [],
      ""strengths"": [],
      ""weaknesses"": [],
      ""estimatedTraffic"": {{
        ""monthlyVisitors"": ""Unknown"",
        ""confidence"": 0
      }},
      ""estimatedBrandAuthority"": {{
        ""score"": 0,
        ""confidence"": 0
      }},
      ""estimatedAIVisibility"": {{
        ""score"": 0,
        ""confidence"": 0
      }},
      ""estimatedCitationScore"": {{
        ""score"": 0,
        ""confidence"": 0
      }},
      ""estimatedContentStrength"": {{
        ""score"": 0,
        ""confidence"": 0
      }},
      ""estimatedGEOReadiness"": {{
        ""score"": 0,
        ""confidence"": 0
      }},
      ""estimatedSEOStrength"": {{
        ""score"": 0,
        ""confidence"": 0
      }},
      ""estimatedTrustScore"": {{
        ""score"": 0,
        ""confidence"": 0
      }},
      ""similarityScore"": 0,
      ""confidence"": 0
    }}
  ],
  ""summary"": {{
    ""totalCompetitors"": 0,
    ""directCompetitors"": 0,
    ""indirectCompetitors"": 0,
    ""industry"": """",
    ""marketOverview"": """"
  }}
}}

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

            var result = JsonSerializer.Deserialize<CompAnalysisResponse>(responseContent, options);

            if (result != null && result.competitors != null && result.competitors.Any())
            {
                var entities = new List<Competitor>();
                foreach (var c in result.competitors)
                {
                    var rawJson = JsonSerializer.Serialize(c, options);

                    entities.Add(new Competitor
                    {
                        OrganizationId = request.OrganizationId,
                        Name = c.companyName ?? "Unknown",
                        WebsiteUrl = c.website ?? "",
                        Industry = c.industry ?? "",
                        Description = c.description ?? "",
                        Category = "",
                        Country = c.headquarters ?? "",
                        SimilarityScore = c.similarityScore,
                        Rank = c.rank,
                        RawJson = rawJson,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                await _websiteRepository.InsertCompetitorsAsync(entities);

                return new CompetitorAnalysisResult
                {
                    Success = true,
                    TotalCompetitors = entities.Count
                };
            }
            else
            {
                return new CompetitorAnalysisResult { Success = false, Error = "Failed to parse AI response into competitors schema." };
            }
        }
        catch (Exception ex)
        {
            return new CompetitorAnalysisResult { Success = false, Error = ex.Message };
        }
    }
}
