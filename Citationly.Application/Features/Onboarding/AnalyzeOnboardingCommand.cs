using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzeOnboardingCommand : IRequest<OnboardingAnalysisResult>
{
    public Guid OrganizationId { get; set; }
    public string WebsiteUrl { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;

}

public class OnboardingAnalysisResult
{
    public ConfidentString BusinessSummary { get; set; } = new();
    public ConfidentList<string> CoreServices { get; set; } = new();
    public ConfidentList<string> Products { get; set; } = new();
    public ConfidentList<string> IndustriesServed { get; set; } = new();
    public ConfidentString BusinessModel { get; set; } = new();
    public ConfidentString UniqueSellingProposition { get; set; } = new();
    public ConfidentList<string> PrimaryTechnologies { get; set; } = new();
    public ConfidentList<string> TargetCustomers { get; set; } = new();
    public ConfidentList<string> ContentCategories { get; set; } = new();
    public ConfidentSeoStrength SeoStrength { get; set; } = new();
    public ConfidentWebsiteStructure WebsiteStructure { get; set; } = new();
    public ConfidentDomainAuthority DomainAuthorityEstimate { get; set; } = new();
    public ConfidentTopicalAuthority TopicalAuthority { get; set; } = new();
    public ConfidentString BrandPositioning { get; set; } = new();
    public ConfidentToneOfVoice ToneOfVoice { get; set; } = new();
    public int OverallConfidence { get; set; }
}

public class ConfidentString { public string Value { get; set; } = string.Empty; public int Confidence { get; set; } }
public class ConfidentList<T> { public List<T> Value { get; set; } = new(); public int Confidence { get; set; } }
public class ConfidentSeoStrength { public SeoStrengthObj Value { get; set; } = new(); public int Confidence { get; set; } }
public class ConfidentWebsiteStructure { public WebsiteStructureObj Value { get; set; } = new(); public int Confidence { get; set; } }
public class ConfidentDomainAuthority { public DomainAuthorityObj Value { get; set; } = new(); public int Confidence { get; set; } }
public class ConfidentTopicalAuthority { public TopicalAuthorityObj Value { get; set; } = new(); public int Confidence { get; set; } }
public class ConfidentToneOfVoice { public ToneOfVoiceObj Value { get; set; } = new(); public int Confidence { get; set; } }

public class SeoStrengthObj { public string Overall { get; set; } = string.Empty; public int Score { get; set; } public List<string> Strengths { get; set; } = new(); public List<string> Weaknesses { get; set; } = new(); public List<string> Recommendations { get; set; } = new(); }
public class WebsiteStructureObj { public string NavigationQuality { get; set; } = string.Empty; public List<string> ImportantPages { get; set; } = new(); public bool BlogPresent { get; set; } public bool ContactPresent { get; set; } public bool PricingPresent { get; set; } public bool FaqPresent { get; set; } public string MobileFriendlyEstimate { get; set; } = string.Empty; public string OverallArchitecture { get; set; } = string.Empty; }
public class DomainAuthorityObj { public int EstimatedScore { get; set; } public string Category { get; set; } = string.Empty; public string Reason { get; set; } = string.Empty; }
public class TopicalAuthorityObj { public List<string> PrimaryTopics { get; set; } = new(); public string AuthorityLevel { get; set; } = string.Empty; public string Reason { get; set; } = string.Empty; }
public class ToneOfVoiceObj { public string PrimaryTone { get; set; } = string.Empty; public List<string> SecondaryTone { get; set; } = new(); public string WritingStyle { get; set; } = string.Empty; public string ReadingLevel { get; set; } = string.Empty; }


public class AnalyzeOnboardingCommandHandler : IRequestHandler<AnalyzeOnboardingCommand, OnboardingAnalysisResult>
{
    private readonly IOpenAiService _openRouterService;
    private readonly IScrapingJobRepository _scrapingRepository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly Citationly.Application.Interfaces.Onboarding.IPageClassificationService _pageClassificationService;
    private readonly Citationly.Application.Interfaces.Onboarding.IPageRankingService _pageRankingService;
    private readonly Citationly.Application.Interfaces.Onboarding.IContentCleaningService _contentCleaningService;
    private readonly Citationly.Application.Interfaces.Onboarding.IWebsiteContentBuilder _websiteContentBuilder;

    public AnalyzeOnboardingCommandHandler(
        IOpenAiService openRouterService,
        IScrapingJobRepository scrapingRepository,
        IWebsiteRepository websiteRepository,
        IDbConnectionFactory dbConnectionFactory,
        Citationly.Application.Interfaces.Onboarding.IPageClassificationService pageClassificationService,
        Citationly.Application.Interfaces.Onboarding.IPageRankingService pageRankingService,
        Citationly.Application.Interfaces.Onboarding.IContentCleaningService contentCleaningService,
        Citationly.Application.Interfaces.Onboarding.IWebsiteContentBuilder websiteContentBuilder)
    {
        _openRouterService = openRouterService;
        _scrapingRepository = scrapingRepository;
        _websiteRepository = websiteRepository;
        _dbConnectionFactory = dbConnectionFactory;
        _pageClassificationService = pageClassificationService;
        _pageRankingService = pageRankingService;
        _contentCleaningService = contentCleaningService;
        _websiteContentBuilder = websiteContentBuilder;
    }

    public async Task<OnboardingAnalysisResult> Handle(AnalyzeOnboardingCommand request, CancellationToken cancellationToken)
    {
        // 0. Check if WebsiteProfile already exists
        if (request.OrganizationId != Guid.Empty)
        {
            var existingProfile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
            if (existingProfile != null && (existingProfile.WebsiteUrl.Contains(request.WebsiteUrl) || request.WebsiteUrl.Contains(existingProfile.WebsiteUrl)))
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                };
                try
                {
                    var cachedResult = JsonSerializer.Deserialize<OnboardingAnalysisResult>(existingProfile.RawProfileJson, options);
                    if (cachedResult != null) return cachedResult;
                }
                catch { }
            }
        }

        // 1. Fetch scraped data and build optimized context
        string websiteContent = "";
        try
        {
            var jobs = await _scrapingRepository.GetAllJobsByOrgAsync(request.OrganizationId);
            // Get the most recent job for this URL
            var job = jobs.Where(j => j.Url.Contains(request.WebsiteUrl) || request.WebsiteUrl.Contains(j.Url))
                          .OrderByDescending(j => j.CreatedAt)
                          .FirstOrDefault();

            if (job != null)
            {
                var pages = await _scrapingRepository.GetPagesByJobIdAsync(job.Id);

                // Pipeline Step 1 & 2: Classify and Score
                var rankedPages = new List<(ScrapedPage Page, Citationly.Domain.Enums.PageCategory Category, int Score)>();
                foreach (var page in pages)
                {
                    var cat = _pageClassificationService.ClassifyPage(page);
                    var score = _pageRankingService.ScorePage(cat);
                    rankedPages.Add((page, cat, score));
                }

                // Pipeline Step 3: Select top 15 pages
                var topRanked = rankedPages.OrderByDescending(p => p.Score).Take(15).ToList();
                var topPages = topRanked.Select(p => p.Page).ToList();

                // Pipeline Step 4: Clean content
                var cleanedPages = _contentCleaningService.CleanPages(topPages);

                // Re-associate cleaned pages with their category and score for the builder
                var finalPagesForBuilder = new List<(ScrapedPage Page, Citationly.Domain.Enums.PageCategory Category, int Score)>();
                foreach (var cl in cleanedPages)
                {
                    var orig = topRanked.First(p => p.Page.Id == cl.Id);
                    finalPagesForBuilder.Add((cl, orig.Category, orig.Score));
                }

                // Pipeline Step 5: Build structured content (limit to ~8000 tokens)
                websiteContent = _websiteContentBuilder.BuildStructuredContent(finalPagesForBuilder, 8000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching scraped data: {ex.Message}");
        }

        var systemPrompt = "You are an expert Business Intelligence, SEO, and Website Analysis AI. Return JSON exactly matching the requested schema. No markdown.";
        var userPrompt = $@"Extract comprehensive business intelligence from the website content below.
        
Website: {request.WebsiteUrl}
Business Name: {request.BusinessName}
Industry: {request.Industry}
Keywords: {request.Keywords}
Target Audience: {request.TargetAudience}

[Website Content]
{websiteContent}
[/Website Content]

INSTRUCTIONS:
1. Populate all fields with rich, accurate insights.
2. Infer missing information only if there is strong evidence.
3. Every object needs a 'value' and 'confidence' (0-100).
4. Include deeper SEO (metadata, headings, internal links), structural (nav, UX), brand (mission, values), and market (ICP, pain points, tech stack) insights in the relevant fields (e.g. SEO recommendations, Brand Positioning, Strengths).
5. Only detect technologies explicitly found. Do not hallucinate.

SCHEMA (Return ONLY this JSON):
{{
  ""businessSummary"": {{""value"": """", ""confidence"": 0}},
  ""coreServices"": {{""value"": [], ""confidence"": 0}},
  ""products"": {{""value"": [], ""confidence"": 0}},
  ""industriesServed"": {{""value"": [], ""confidence"": 0}},
  ""businessModel"": {{""value"": """", ""confidence"": 0}},
  ""uniqueSellingProposition"": {{""value"": """", ""confidence"": 0}},
  ""primaryTechnologies"": {{""value"": [], ""confidence"": 0}},
  ""targetCustomers"": {{""value"": [], ""confidence"": 0}},
  ""contentCategories"": {{""value"": [], ""confidence"": 0}},
  ""seoStrength"": {{""value"": {{""overall"": """", ""score"": 0, ""strengths"": [], ""weaknesses"": [], ""recommendations"": []}}, ""confidence"": 0}},
  ""websiteStructure"": {{""value"": {{""navigationQuality"": """", ""importantPages"": [], ""blogPresent"": false, ""contactPresent"": false, ""pricingPresent"": false, ""faqPresent"": false, ""mobileFriendlyEstimate"": """", ""overallArchitecture"": """"}}, ""confidence"": 0}},
  ""domainAuthorityEstimate"": {{""value"": {{""estimatedScore"": 0, ""category"": """", ""reason"": """"}}, ""confidence"": 0}},
  ""topicalAuthority"": {{""value"": {{""primaryTopics"": [], ""authorityLevel"": """", ""reason"": """"}}, ""confidence"": 0}},
  ""brandPositioning"": {{""value"": """", ""confidence"": 0}},
  ""toneOfVoice"": {{""value"": {{""primaryTone"": """", ""secondaryTone"": [], ""writingStyle"": """", ""readingLevel"": """"}}, ""confidence"": 0}},
  ""overallConfidence"": 0
}}";

        try
        {
            var responseContent = await _openRouterService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: systemPrompt,
                requireJson: true,
                model: "gpt-4o-mini");

            // Clean up markdown just in case the LLM disobeys "no markdown wrapper"
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
            var result = JsonSerializer.Deserialize<OnboardingAnalysisResult>(responseContent, options);
            if (result != null)
            {
                // Save to database
                if (request.OrganizationId != Guid.Empty)
                {
                    try
                    {
                        var profile = new WebsiteProfile
                        {
                            OrganizationId = request.OrganizationId,
                            WebsiteUrl = request.WebsiteUrl,
                            BusinessName = request.BusinessName,
                            RawProfileJson = responseContent
                        };
                        await _websiteRepository.InsertWebsiteProfileAsync(profile);

                        // Also update the Organization name
                        using var connection = _dbConnectionFactory.CreateConnection();
                        await Dapper.SqlMapper.ExecuteAsync(
                            connection,
                            "UPDATE Organizations SET Name = @Name WHERE Id = @Id",
                            new { Name = request.BusinessName, Id = request.OrganizationId }
                        );
                    }
                    catch (Exception dbEx)
                    {
                        Console.WriteLine($"Failed to save WebsiteProfile: {dbEx.Message}");
                    }
                }

                return result;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during AI Onboarding analysis: {ex.Message}");
            throw;
        }

        throw new Exception("AI returned invalid data.");
    }
}

