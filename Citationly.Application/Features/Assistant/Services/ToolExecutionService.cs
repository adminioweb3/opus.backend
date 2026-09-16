using System.Text.Json;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Assistant.Services;

public class ToolExecutionService
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IMetricsRepository _metricsRepository;
    private readonly IScrapingJobRepository _scrapingRepository;
    private readonly IAlertRepository _alertRepository;
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository;
    private readonly IContentDraftRepository _contentDraftRepository;
    private readonly IOpportunitySnapshotRepository _opportunityRepository;
    private readonly IPromptIntelligenceRepository _promptRepository;

    public ToolExecutionService(
        IWebsiteRepository websiteRepository,
        IMetricsRepository metricsRepository,
        IScrapingJobRepository scrapingRepository,
        IAlertRepository alertRepository,
        IKnowledgeBaseRepository knowledgeBaseRepository,
        IContentDraftRepository contentDraftRepository,
        IOpportunitySnapshotRepository opportunityRepository,
        IPromptIntelligenceRepository promptRepository)
    {
        _websiteRepository = websiteRepository;
        _metricsRepository = metricsRepository;
        _scrapingRepository = scrapingRepository;
        _alertRepository = alertRepository;
        _knowledgeBaseRepository = knowledgeBaseRepository;
        _contentDraftRepository = contentDraftRepository;
        _opportunityRepository = opportunityRepository;
        _promptRepository = promptRepository;
    }

    public async Task<Dictionary<string, object>> ExecuteToolsAsync(Guid? organizationId, string[] requiredTools, CancellationToken ct)
    {
        var rawData = new Dictionary<string, object>();
        
        if (!organizationId.HasValue) 
            return rawData;

        // Base data - always load websites
        var websites = await _websiteRepository.GetWebsitesByOrgAsync(organizationId.Value);
        rawData["websites"] = websites.Select(w => new { w.DomainUrl, w.HealthScore, w.VisibilityScore, w.PlatformName }).ToList();

        // Load Scraping Context for Website Intelligence
        var jobs = await _scrapingRepository.GetAllJobsByOrgAsync(organizationId.Value);
        var latestJob = jobs.OrderByDescending(j => j.CreatedAt).FirstOrDefault();
        
        if (latestJob != null)
        {
            var pages = await _scrapingRepository.GetPagesByJobIdAsync(latestJob.Id);
            var topPages = pages.Where(p => !string.IsNullOrEmpty(p.Content)).Take(5).ToList();
            rawData["scrapedContext"] = topPages.Select(p => new {
                p.Url,
                p.Title,
                Snippet = string.Join(" ", p.Content?.Split(' ').Take(100) ?? Array.Empty<string>())
            }).ToList();
        }

        // If intent detection failed or no tools required, fallback to loading core datasets so the AI has context
        bool loadCompetitors = requiredTools.Contains("Competitor Tool") || requiredTools.Length == 0;
        bool loadVisibility = requiredTools.Contains("Visibility Tool") || requiredTools.Length == 0;

        if (loadCompetitors)
        {
            var shareOfVoices = await _metricsRepository.GetShareOfVoiceAsync(organizationId.Value, DateTime.UtcNow.Date);
            rawData["competitors"] = shareOfVoices.Select(s => new { s.CompetitorName, s.SharePercentage }).ToList();
        }

        if (loadVisibility)
        {
            var visibilitySum = await _websiteRepository.GetVisibilitySummaryAsync(organizationId.Value);
            if (visibilitySum != null)
            {
                rawData["visibilitySummary"] = new { visibilitySum.OverallVisibilityScore, visibilitySum.BestPlatform, visibilitySum.WeakestPlatform, visibilitySum.AverageMentionRate };
            }

            var platVis = await _websiteRepository.GetPlatformVisibilitiesAsync(organizationId.Value);
            if (platVis != null && platVis.Any())
            {
                rawData["platformVisibilities"] = platVis.Select(p => new { p.Platform, p.VisibilityScore, p.MentionRate, p.PromptCoverage }).ToList();
            }
        }

        var loadWorkspaceSummary = requiredTools.Length == 0;

        if (loadWorkspaceSummary || requiredTools.Contains("Alerts Tool"))
        {
            var alerts = await _alertRepository.GetAlertsAsync(organizationId.Value, 10);
            rawData["alerts"] = alerts.Select(a => new { a.Type, a.Title, a.Message, a.Severity, a.IsRead, a.CreatedAt }).ToList();
        }

        if (loadWorkspaceSummary || requiredTools.Contains("Knowledge Base Tool"))
        {
            var knowledgeBases = await _knowledgeBaseRepository.GetByOrgAsync(organizationId.Value);
            rawData["knowledgeBases"] = knowledgeBases.Select(k => new { k.Id, k.Name, k.Description, k.UpdatedAt }).ToList();
        }

        if (loadWorkspaceSummary || requiredTools.Contains("Content Tool"))
        {
            var drafts = await _contentDraftRepository.GetByOrgAsync(organizationId.Value);
            rawData["contentDrafts"] = drafts.OrderByDescending(d => d.UpdatedAt).Take(10)
                .Select(d => new { d.Id, d.Title, d.ContentType, d.WordCount, d.Status, d.PublishedUrl, d.UpdatedAt }).ToList();
        }

        if (loadWorkspaceSummary || requiredTools.Contains("Opportunity Tool"))
        {
            var latestScan = await _opportunityRepository.GetLatestScanDateAsync(organizationId.Value);
            if (latestScan.HasValue)
            {
                var opportunities = await _opportunityRepository.GetSnapshotsByScanDateAsync(organizationId.Value, latestScan.Value);
                rawData["opportunities"] = opportunities.OrderByDescending(o => o.Score).Take(10)
                    .Select(o => new { o.Category, o.Title, o.Summary, o.Score, o.Effort, o.EstimatedGainPct, o.Eta, o.ScanDate }).ToList();
            }
        }

        if (loadWorkspaceSummary || requiredTools.Contains("Prompt Intelligence Tool"))
        {
            var since = DateTime.UtcNow.AddDays(-30);
            var topics = await _promptRepository.GetTopicsAsync(organizationId.Value);
            var promptVisibility = await _promptRepository.GetVisibilitySummaryDataAsync(organizationId.Value, since);
            rawData["promptTopics"] = topics.Take(20).Select(t => new { t.Id, t.Name, t.Description }).ToList();
            rawData["promptVisibility"] = promptVisibility.OrderByDescending(v => v.RunAt).Take(20)
                .Select(v => new { v.TopicName, v.QuestionId, v.Region, v.Persona, v.OverallVisibilityScore, v.ShareOfVoice, v.AveragePosition, v.CitationCount, v.RunAt }).ToList();
        }
        
        return rawData;
    }
}
