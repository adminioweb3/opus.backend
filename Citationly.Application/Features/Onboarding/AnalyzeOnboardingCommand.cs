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
    public string WhoDoYouSellTo { get; set; } = string.Empty;
    public string KnownCompetitors { get; set; } = string.Empty;
    public string MainOffering { get; set; } = string.Empty;

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

    /// <summary>
    /// Value is always one of "Startup", "SMB", "Mid-Market", "Enterprise" — used downstream by
    /// CompetitorDiscoveryService to keep generated competitors at a comparable scale, instead of
    /// defaulting to whichever companies are most famous in the industry regardless of whether
    /// this business is actually anywhere near their size.
    /// </summary>
    public ConfidentString CompanyScale { get; set; } = new();

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
    private readonly IAiCompletionService _aiCompletionService;
    private readonly IScrapingJobRepository _scrapingRepository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly Citationly.Application.Interfaces.Onboarding.IPageClassificationService _pageClassificationService;
    private readonly Citationly.Application.Interfaces.Onboarding.IPageRankingService _pageRankingService;
    private readonly Citationly.Application.Interfaces.Onboarding.IContentCleaningService _contentCleaningService;
    private readonly Citationly.Application.Interfaces.Onboarding.IWebsiteContentBuilder _websiteContentBuilder;

    public AnalyzeOnboardingCommandHandler(
        IAiCompletionService aiCompletionService,
        IScrapingJobRepository scrapingRepository,
        IWebsiteRepository websiteRepository,
        IDbConnectionFactory dbConnectionFactory,
        Citationly.Application.Interfaces.Onboarding.IPageClassificationService pageClassificationService,
        Citationly.Application.Interfaces.Onboarding.IPageRankingService pageRankingService,
        Citationly.Application.Interfaces.Onboarding.IContentCleaningService contentCleaningService,
        Citationly.Application.Interfaces.Onboarding.IWebsiteContentBuilder websiteContentBuilder)
    {
        _aiCompletionService = aiCompletionService;
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
            try
            {
                var existingProfile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
                if (existingProfile != null && (existingProfile.WebsiteUrl.Contains(request.WebsiteUrl) || request.WebsiteUrl.Contains(existingProfile.WebsiteUrl)))
                {
                    try
                    {
                        var cachedResult = JsonSerializer.Deserialize<OnboardingAnalysisResult>(existingProfile.RawProfileJson, CreateJsonSerializerOptions());
                        if (cachedResult != null) return cachedResult;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching cached onboarding profile: {ex.Message}");
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
Main Offering: {request.MainOffering}
Who They Sell To: {request.WhoDoYouSellTo}
Known Competitors: {request.KnownCompetitors}

[Website Content]
{websiteContent}
[/Website Content]

INSTRUCTIONS:
1. Populate all fields with rich, accurate insights.
2. Use the submitted business inputs as authoritative when website crawl content is sparse or unavailable.
3. Every object needs a 'value' and 'confidence' (0-100).
4. Include deeper SEO (metadata, headings, internal links), structural (nav, UX), brand (mission, values), and market (ICP, pain points, tech stack) insights in the relevant fields (e.g. SEO recommendations, Brand Positioning, Strengths).
5. Only detect technologies explicitly found in crawl content or supplied business inputs. Do not hallucinate.
6. For companyScale, judge from real signals on the site — team/about page size, number of case
   studies or logos, funding/press mentions, pricing tier language (built for small teams vs
   enterprise-grade). Value must be exactly one of: ""Startup"", ""SMB"", ""Mid-Market"", ""Enterprise"".
   Default to ""Startup"" only if there is genuinely no signal either way — do not default to a
   bigger tier just because the industry has famous large players.

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
  ""companyScale"": {{""value"": ""Startup|SMB|Mid-Market|Enterprise"", ""confidence"": 0}},
  ""overallConfidence"": 0
}}";

        AiCompletionResult completion;
        try
        {
            completion = await _aiCompletionService.CompleteAsync(
                request.OrganizationId == Guid.Empty ? null : request.OrganizationId,
                "onboarding.website_analysis",
                userPrompt,
                systemPrompt,
                requireJson: true,
                preferredProviderKey: "openai",
                cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during AI Onboarding analysis: {ex.Message}");
            return await PersistFallbackAnalysisResultAsync(request);
        }

        if (!completion.Success)
        {
            return await PersistFallbackAnalysisResultAsync(request);
        }

        // Clean up markdown just in case the LLM disobeys "no markdown wrapper"
        var responseContent = completion.Content.Trim();
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

        try
        {
            var result = JsonSerializer.Deserialize<OnboardingAnalysisResult>(responseContent, CreateJsonSerializerOptions());
            if (result != null)
            {
                await TryPersistAnalysisResultAsync(request, responseContent);

                return result;
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Invalid AI Onboarding analysis JSON: {ex.Message}");
        }

        return await PersistFallbackAnalysisResultAsync(request);
    }

    private async Task<OnboardingAnalysisResult> PersistFallbackAnalysisResultAsync(AnalyzeOnboardingCommand request)
    {
        var result = CreateFallbackAnalysisResult(request);
        var json = JsonSerializer.Serialize(result, CreateJsonSerializerOptions());
        await TryPersistAnalysisResultAsync(request, json);
        return result;
    }

    private async Task TryPersistAnalysisResultAsync(AnalyzeOnboardingCommand request, string rawProfileJson)
    {
        try
        {
            await PersistAnalysisResultAsync(request, rawProfileJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error persisting onboarding analysis: {ex.Message}");
        }
    }

    private async Task PersistAnalysisResultAsync(AnalyzeOnboardingCommand request, string rawProfileJson)
    {
        if (request.OrganizationId == Guid.Empty) return;

        var profile = new WebsiteProfile
        {
            OrganizationId = request.OrganizationId,
            WebsiteUrl = request.WebsiteUrl,
            BusinessName = request.BusinessName,
            RawProfileJson = rawProfileJson
        };
        await _websiteRepository.InsertWebsiteProfileAsync(profile);

        using var connection = _dbConnectionFactory.CreateConnection();
        await Dapper.SqlMapper.ExecuteAsync(
            connection,
            @"UPDATE Organizations SET Name = @Name, Industry = @Industry,
              WhoDoYouSellTo = @WhoDoYouSellTo, KnownCompetitors = @KnownCompetitors, MainOffering = @MainOffering
              WHERE Id = @Id",
            new
            {
                Name = request.BusinessName,
                Industry = request.Industry,
                WhoDoYouSellTo = request.WhoDoYouSellTo,
                KnownCompetitors = request.KnownCompetitors,
                MainOffering = request.MainOffering,
                Id = request.OrganizationId
            }
        );
    }

    private static JsonSerializerOptions CreateJsonSerializerOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private static OnboardingAnalysisResult CreateFallbackAnalysisResult(AnalyzeOnboardingCommand request)
    {
        var keywords = request.Keywords
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        var offering = string.IsNullOrWhiteSpace(request.MainOffering)
            ? "Core product or service"
            : request.MainOffering.Trim();
        var industry = string.IsNullOrWhiteSpace(request.Industry)
            ? "Unspecified"
            : request.Industry.Trim();
        var audience = string.IsNullOrWhiteSpace(request.WhoDoYouSellTo)
            ? request.TargetAudience
            : request.WhoDoYouSellTo;

        return new OnboardingAnalysisResult
        {
            BusinessSummary = new ConfidentString
            {
                Value = $"{request.BusinessName} offers {offering} for {audience}.",
                Confidence = 45
            },
            CoreServices = new ConfidentList<string>
            {
                Value = new List<string> { offering },
                Confidence = 45
            },
            Products = new ConfidentList<string>
            {
                Value = new List<string> { offering },
                Confidence = 40
            },
            IndustriesServed = new ConfidentList<string>
            {
                Value = new List<string> { industry },
                Confidence = 50
            },
            BusinessModel = new ConfidentString
            {
                Value = industry,
                Confidence = 35
            },
            UniqueSellingProposition = new ConfidentString
            {
                Value = offering,
                Confidence = 35
            },
            PrimaryTechnologies = new ConfidentList<string>
            {
                Value = new List<string>(),
                Confidence = 0
            },
            TargetCustomers = new ConfidentList<string>
            {
                Value = audience.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(6).ToList(),
                Confidence = 45
            },
            ContentCategories = new ConfidentList<string>
            {
                Value = keywords.Count > 0 ? keywords : new List<string> { offering },
                Confidence = 35
            },
            SeoStrength = new ConfidentSeoStrength
            {
                Value = new SeoStrengthObj
                {
                    Overall = "Needs review",
                    Score = 35,
                    Recommendations = new List<string> { "Complete the website scan and rerun analysis for evidence-backed recommendations." }
                },
                Confidence = 25
            },
            WebsiteStructure = new ConfidentWebsiteStructure
            {
                Value = new WebsiteStructureObj
                {
                    NavigationQuality = "Unknown",
                    ImportantPages = new List<string>(),
                    MobileFriendlyEstimate = "Unknown",
                    OverallArchitecture = "Website scan was unavailable during onboarding."
                },
                Confidence = 20
            },
            DomainAuthorityEstimate = new ConfidentDomainAuthority
            {
                Value = new DomainAuthorityObj
                {
                    EstimatedScore = 25,
                    Category = "Unverified",
                    Reason = "Fallback estimate generated because live AI analysis was unavailable."
                },
                Confidence = 20
            },
            TopicalAuthority = new ConfidentTopicalAuthority
            {
                Value = new TopicalAuthorityObj
                {
                    PrimaryTopics = keywords,
                    AuthorityLevel = "Unverified",
                    Reason = "Fallback topics are based on supplied onboarding keywords."
                },
                Confidence = 30
            },
            BrandPositioning = new ConfidentString
            {
                Value = offering,
                Confidence = 35
            },
            ToneOfVoice = new ConfidentToneOfVoice
            {
                Value = new ToneOfVoiceObj
                {
                    PrimaryTone = "Unknown",
                    SecondaryTone = new List<string>(),
                    WritingStyle = "Unknown",
                    ReadingLevel = "Unknown"
                },
                Confidence = 10
            },
            CompanyScale = new ConfidentString
            {
                Value = "Startup",
                Confidence = 10
            },
            OverallConfidence = 30
        };
    }
}

