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
    private readonly IOpenRouterService _openRouterService;
    private readonly IScrapingJobRepository _scrapingRepository;
    private readonly IWebsiteRepository _websiteRepository;

    public AnalyzeOnboardingCommandHandler(
        IOpenRouterService openRouterService,
        IScrapingJobRepository scrapingRepository,
        IWebsiteRepository websiteRepository)
    {
        _openRouterService = openRouterService;
        _scrapingRepository = scrapingRepository;
        _websiteRepository = websiteRepository;
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

        // 1. Fetch scraped data
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
                // Limit to top 5 pages to avoid massive prompt
                var topPages = pages.Take(5).ToList();
                foreach (var page in topPages)
                {
                    websiteContent += $"\n--- PAGE: {page.Url} ---\n{page.MarkdownContent}\n";
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching scraped data: {ex.Message}");
        }

        var systemPrompt = "You are an expert Business Intelligence, SEO, and Website Analysis AI.";
        var userPrompt = $@"Your task is to analyze the provided business information and website to create a comprehensive business intelligence profile.

## Input

Website: {request.WebsiteUrl}
Business Name: {request.BusinessName}
Industry: {request.Industry}
Keywords: {request.Keywords}
Target Audience: {request.TargetAudience}

[Scraped Website Content]
{websiteContent}
[/Scraped Website Content]

## Instructions

1. Analyze the entire website, including:
   - Homepage
   - About page
   - Services
   - Products
   - Blog
   - Solutions
   - Industries
   - Pricing
   - Contact page
   - Footer
   - Metadata
   - Structured data (if available)

2. Infer missing information only when there is strong evidence from the website content.

3. Never hallucinate or invent facts.
If information cannot be reasonably inferred from the provided website content, return:
- null
- []
- ""Unknown""

4. Every section must include:
   - value
   - confidence (0-100)

5. Confidence Score Rules

100 = Explicitly stated multiple times
90 = Clearly stated
80 = Strong inference
70 = Reasonable inference
60 = Weak inference
Below 60 = Unknown or insufficient evidence

6. Keep summaries concise.

7. Return ONLY valid JSON.
Do NOT include markdown.
Do NOT include explanations.
Do NOT wrap inside ```json.

Return using the following schema exactly:

{{
  ""businessSummary"": {{
    ""value"": """",
    ""confidence"": 0
  }},
  ""coreServices"": {{
    ""value"": [],
    ""confidence"": 0
  }},
  ""products"": {{
    ""value"": [],
    ""confidence"": 0
  }},
  ""industriesServed"": {{
    ""value"": [],
    ""confidence"": 0
  }},
  ""businessModel"": {{
    ""value"": """",
    ""confidence"": 0
  }},
  ""uniqueSellingProposition"": {{
    ""value"": """",
    ""confidence"": 0
  }},
  ""primaryTechnologies"": {{
    ""value"": [],
    ""confidence"": 0
  }},
  ""targetCustomers"": {{
    ""value"": [],
    ""confidence"": 0
  }},
  ""contentCategories"": {{
    ""value"": [],
    ""confidence"": 0
  }},
  ""seoStrength"": {{
    ""value"": {{
      ""overall"": """",
      ""score"": 0,
      ""strengths"": [],
      ""weaknesses"": [],
      ""recommendations"": []
    }},
    ""confidence"": 0
  }},
  ""websiteStructure"": {{
    ""value"": {{
      ""navigationQuality"": """",
      ""importantPages"": [],
      ""blogPresent"": false,
      ""contactPresent"": false,
      ""pricingPresent"": false,
      ""faqPresent"": false,
      ""mobileFriendlyEstimate"": """",
      ""overallArchitecture"": """"
    }},
    ""confidence"": 0
  }},
  ""domainAuthorityEstimate"": {{
    ""value"": {{
      ""estimatedScore"": 0,
      ""category"": """",
      ""reason"": """"
    }},
    ""confidence"": 0
  }},
  ""topicalAuthority"": {{
    ""value"": {{
      ""primaryTopics"": [],
      ""authorityLevel"": """",
      ""reason"": """"
    }},
    ""confidence"": 0
  }},
  ""brandPositioning"": {{
    ""value"": """",
    ""confidence"": 0
  }},
  ""toneOfVoice"": {{
    ""value"": {{
      ""primaryTone"": """",
      ""secondaryTone"": [],
      ""writingStyle"": """",
      ""readingLevel"": """"
    }},
    ""confidence"": 0
  }},
  ""overallConfidence"": 0
}}

Additional Analysis Guidelines

Business Summary
- 2-4 sentences describing the company.

Core Services
- List major services.

Products
- List software, platforms, products, or offerings.

Industries Served
- List all industries explicitly mentioned or strongly implied.

Business Model
Choose one:
- B2B
- B2C
- D2C
- Marketplace
- SaaS
- Enterprise
- Agency
- E-commerce
- Consulting
- Hybrid
- Unknown

Unique Selling Proposition
Summarize the company's key differentiator.

Primary Technologies
Only identify technologies that are explicitly detected from the website content,
HTML, JavaScript bundles, metadata, script tags, CSS classes, or other technical indicators.

Do NOT guess technologies based solely on appearance.

Target Customers
Examples:
- Small Businesses
- Startups
- Enterprises
- Healthcare Providers
- Retailers
- Manufacturers
- Agencies
- Developers
- Consumers

Content Categories
Examples:
- Blogs
- Case Studies
- Whitepapers
- Product Updates
- Documentation
- Guides
- News
- Testimonials
- Careers

SEO Strength
Evaluate:
- Metadata quality
- Heading hierarchy
- Content depth
- Internal linking
- Keyword optimization
- Structured data
- Technical SEO indicators

Website Structure
Evaluate:
- Navigation quality
- Information architecture
- Important pages
- User experience
- Mobile friendliness estimate

Domain Authority Estimate
Estimate:
0-20 = Very Low
21-40 = Low
41-60 = Medium
61-80 = High
81-100 = Very High

Base the estimate on:
- Content quality
- Website maturity
- Backlink likelihood
- Brand recognition
- SEO signals

Topical Authority
Evaluate expertise in the website's primary subject areas.

Brand Positioning
Describe how the company positions itself relative to competitors.

Tone of Voice
Identify:
- Professional
- Friendly
- Technical
- Luxury
- Innovative
- Corporate
- Casual
- Authoritative
- Educational
- Conversational

Finally calculate an overallConfidence value between 0 and 100 based on the average confidence across all sections.

All arrays must contain unique values.

Do not repeat similar items.

Do not include empty strings.

The JSON must be valid and parsable.

Every field defined in the schema must be present.

Return ONLY the JSON object.";

        try
        {
            var responseContent = await _openRouterService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: systemPrompt,
                requireJson: true,
                model: "meta-llama/llama-3.3-70b-instruct:free");

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

