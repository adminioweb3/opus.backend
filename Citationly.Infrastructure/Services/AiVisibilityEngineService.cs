using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;
using Citationly.Application.Interfaces.Competitors;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services;

public class AiVisibilityEngineService : IAiVisibilityEngineService
{
    private readonly IAiVisibilityRepository _repository;
    private readonly IOpenAiService _openRouterService;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICompanyGraphService _companyGraphService;
    private readonly ICompetitorDiscoveryService _discoveryService;
    private readonly ICompanyCompetitorRepository _companyCompetitorRepository;
    private readonly ICompetitorGraphSyncService _syncService;

    public AiVisibilityEngineService(
        IAiVisibilityRepository repository,
        IOpenAiService openRouterService,
        IWebsiteRepository websiteRepository,
        IUserRepository userRepository,
        ICompanyGraphService companyGraphService,
        ICompetitorDiscoveryService discoveryService,
        ICompanyCompetitorRepository companyCompetitorRepository,
        ICompetitorGraphSyncService syncService)
    {
        _repository = repository;
        _openRouterService = openRouterService;
        _websiteRepository = websiteRepository;
        _userRepository = userRepository;
        _companyGraphService = companyGraphService;
        _discoveryService = discoveryService;
        _companyCompetitorRepository = companyCompetitorRepository;
        _syncService = syncService;
    }

    public async Task RunAnalysisAsync(Guid organizationId)
    {
        Console.WriteLine($"Starting AI Visibility Analysis for Org: {organizationId}");

        var websites = await _websiteRepository.GetWebsitesByOrgAsync(organizationId);
        var mainWebsite = websites.FirstOrDefault();
        if (mainWebsite == null)
        {
            Console.WriteLine("No website found for org. Aborting analysis.");
            return;
        }

        // For discovery, we need the business name. It's stored in Organizations (which we don't have a direct repo for, but we can query it or pass it).
        // For now, let's use the DomainUrl as the business name if not known.
        var domainName = new Uri(mainWebsite.DomainUrl).Host.Replace("www.", "");

        // Step 1: Discover Competitors and Industry — real Company Knowledge Graph, no
        // invention. This is the same pipeline AnalyzeCompetitorsCommandHandler uses; funneling
        // both entry points through one sync service is what closes the old bug where this
        // service and the onboarding endpoint each independently overwrote the other's rows.
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(organizationId);
        List<Competitor> competitors;
        if (profile == null)
        {
            Console.WriteLine("No website profile found yet — skipping competitor discovery for this run.");
            competitors = new List<Competitor>();
        }
        else
        {
            var company = await _companyGraphService.EnsureCompanyAsync(
                organizationId, profile.WebsiteUrl, profile.BusinessName, profile.RawProfileJson);
            var edges = await _discoveryService.DiscoverCompetitorsAsync(
                organizationId, company.Id, profile.BusinessName, profile.RawProfileJson, CancellationToken.None);
            await _companyCompetitorRepository.ReplaceCompetitorsForCompanyAsync(company.Id, edges);
            competitors = await _syncService.SyncOrgCompetitorsAsync(organizationId, company.Id);
        }

        // Step 2: Run AI Prompts
        var industry = competitors.FirstOrDefault()?.Industry ?? "technology";
        var scores = await EvaluateVisibilityScoresAsync(domainName, industry, competitors);
        if (scores == null)
        {
            var message = $"AI visibility scan incomplete for org {organizationId}; retry later.";
            Console.WriteLine(message);
            throw new InvalidOperationException(message);
        }

        // Step 3: Save Historical Scans
        var scan = new HistoricalScan
        {
            OrganizationId = organizationId,
            ScanDate = DateOnly.FromDateTime(DateTime.UtcNow),
            VisibilityScore = scores.VisibilityScore,
            CitationScore = scores.CitationScore,
            SentimentScore = scores.SentimentScore,
            CompetitorScore = scores.CompetitorScore
        };
        await _repository.InsertHistoricalScanAsync(scan);

        // Step 4: Save Share of Voice
        await _repository.DeleteShareOfVoiceByScanDateAsync(organizationId, scan.ScanDate);
        
        var random = new Random();
        foreach (var c in competitors.Take(4))
        {
            var color = $"#{random.Next(0x1000000):X6}";
            await _repository.InsertShareOfVoiceAsync(new ShareOfVoice
            {
                OrganizationId = organizationId,
                ScanDate = scan.ScanDate,
                CompetitorName = c.Name,
                SharePercentage = c.Popularity,
                ColorCode = color
            });
        }
        
        // Add self
        await _repository.InsertShareOfVoiceAsync(new ShareOfVoice
        {
            OrganizationId = organizationId,
            ScanDate = scan.ScanDate,
            CompetitorName = domainName,
            SharePercentage = scores.VisibilityScore,
            ColorCode = "#3b82f6" // Primary blue
        });

        Console.WriteLine("AI Visibility Analysis Completed.");
    }

    private async Task<ScoreResult?> EvaluateVisibilityScoresAsync(string domainName, string industry, List<Competitor> competitors)
    {
        var competitorNames = string.Join(", ", competitors.Select(c => c.Name));
        var prompt = $@"
You are an Answer Engine simulating a user query.
Query: ""What are the top solutions/companies in {industry}?""

Provide a realistic response. Then, at the very end of your response, output a JSON block evaluating the visibility of '{domainName}' and its competitors ({competitorNames}).
Format the JSON exactly like this:
```json
{{
  ""visibilityScore"": 45,
  ""citationScore"": 30,
  ""sentimentScore"": 60,
  ""competitorScore"": 75
}}
```
";

        try
        {
            var responseContent = await _openRouterService.GenerateContentAsync(prompt);
            
            // Extract the JSON block
            var jsonStart = responseContent.LastIndexOf("```json");
            if (jsonStart != -1)
            {
                var jsonEnd = responseContent.IndexOf("```", jsonStart + 7);
                if (jsonEnd != -1)
                {
                    var jsonStr = responseContent.Substring(jsonStart + 7, jsonEnd - (jsonStart + 7)).Trim();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<ScoreResult>(jsonStr, options);
                    if (result != null) return result;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Visibility evaluation failed: {ex.Message}");
            return null;
        }

        return null;
    }

    private class ScoreResult
    {
        public int VisibilityScore { get; set; }
        public int CitationScore { get; set; }
        public int SentimentScore { get; set; }
        public int CompetitorScore { get; set; }
    }
}
