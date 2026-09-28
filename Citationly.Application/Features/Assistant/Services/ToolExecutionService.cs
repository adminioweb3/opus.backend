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
    private readonly IVisibilitySnapshotRepository _visibilitySnapshotRepository;
    private readonly ICitationScanSnapshotRepository _citationSnapshotRepository;
    private readonly ICompetitorSnapshotRepository _competitorSnapshotRepository;
    private readonly IAgentControlPlaneRepository _agentRepository;

    public ToolExecutionService(
        IWebsiteRepository websiteRepository,
        IMetricsRepository metricsRepository,
        IScrapingJobRepository scrapingRepository,
        IAlertRepository alertRepository,
        IKnowledgeBaseRepository knowledgeBaseRepository,
        IContentDraftRepository contentDraftRepository,
        IOpportunitySnapshotRepository opportunityRepository,
        IPromptIntelligenceRepository promptRepository,
        IVisibilitySnapshotRepository visibilitySnapshotRepository,
        ICitationScanSnapshotRepository citationSnapshotRepository,
        ICompetitorSnapshotRepository competitorSnapshotRepository,
        IAgentControlPlaneRepository agentRepository)
    {
        _websiteRepository = websiteRepository;
        _metricsRepository = metricsRepository;
        _scrapingRepository = scrapingRepository;
        _alertRepository = alertRepository;
        _knowledgeBaseRepository = knowledgeBaseRepository;
        _contentDraftRepository = contentDraftRepository;
        _opportunityRepository = opportunityRepository;
        _promptRepository = promptRepository;
        _visibilitySnapshotRepository = visibilitySnapshotRepository;
        _citationSnapshotRepository = citationSnapshotRepository;
        _competitorSnapshotRepository = competitorSnapshotRepository;
        _agentRepository = agentRepository;
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

            var visibilityHistory = await _visibilitySnapshotRepository.GetRecentSummaryHistoryAsync(organizationId.Value, 8);
            rawData["visibilityHistory"] = visibilityHistory.Select(item => new
            {
                item.ScanDate,
                item.CompositeScore,
                item.DirectPct,
                item.MentionsPct,
                item.IndirectPct,
                item.ComparativePct
            }).ToList();
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

            var recommendations = await _websiteRepository.GetGeoRecommendationsAsync(organizationId.Value);
            rawData["geoRecommendations"] = recommendations.Take(15).Select(item => new
            {
                item.RecommendationId,
                item.Category,
                item.Title,
                item.Priority,
                item.ExpectedOutcome,
                item.SuccessMetric,
                item.CreatedAt
            }).ToList();
        }

        if (loadWorkspaceSummary || requiredTools.Contains("Prompt Intelligence Tool"))
        {
            var since = DateTime.UtcNow.AddDays(-30);
            var topics = await _promptRepository.GetTopicsAsync(organizationId.Value);
            var promptVisibility = await _promptRepository.GetVisibilitySummaryDataAsync(organizationId.Value, since);
            rawData["promptTopics"] = topics.Take(20).Select(t => new { t.Id, t.Name, t.Description }).ToList();
            rawData["promptVisibility"] = promptVisibility.OrderByDescending(v => v.RunAt).Take(20)
                .Select(v => new { v.TopicName, v.QuestionId, v.Region, v.Persona, v.OverallVisibilityScore, v.ShareOfVoice, v.AveragePosition, v.CitationCount, v.RunAt }).ToList();

            var citationEvidence = await _promptRepository.GetCitationSummaryDataAsync(organizationId.Value, since);
            rawData["citationEvidence"] = citationEvidence.OrderByDescending(item => item.RunAt).Take(30)
                .Select(item => new { item.AnalysisId, item.Platform, item.Domain, item.Url, item.Category, item.RunAt }).ToList();

            var impactHistory = await _promptRepository.GetRecommendationImpactHistoryAsync(organizationId.Value, string.Empty, 1);
            rawData["recommendationImpactHistory"] = impactHistory.Select(item => new
            {
                item.Category,
                item.SampleCount,
                item.AverageVisibilityDelta,
                item.AverageCitationDelta
            }).ToList();
        }

        if (loadWorkspaceSummary || loadVisibility || loadCompetitors || requiredTools.Contains("Agent Evidence Tool"))
        {
            await _agentRepository.EnsureDefaultsAsync(organizationId.Value, ct);
            var findings = await _agentRepository.GetFindingsAsync(organizationId.Value, limit: 20, cancellationToken: ct);
            rawData["agentFindings"] = findings.Select(item => new
            {
                item.Id,
                item.AgentKey,
                item.FindingType,
                item.Severity,
                item.Title,
                item.Summary,
                item.EntityType,
                Evidence = ParseEvidence(item.EvidenceJson),
                item.ObservationStartedAt,
                item.ObservationEndedAt,
                item.Confidence,
                item.Status,
                item.UpdatedAt
            }).ToList();

            var agentRecommendations = await _agentRepository.GetRecommendationsAsync(organizationId.Value, limit: 20, cancellationToken: ct);
            rawData["agentRecommendations"] = agentRecommendations.Select(item => new
            {
                item.Id,
                item.FindingId,
                item.RecommendationType,
                item.Category,
                item.Title,
                item.Summary,
                item.Rationale,
                item.TargetType,
                item.TargetKey,
                Evidence = ParseEvidence(item.EvidenceJson),
                ActionPlan = ParseEvidence(item.ActionPlanJson),
                ValidationPlan = ParseEvidence(item.ValidationPlanJson),
                item.ExpectedImpact,
                item.ImpactScore,
                item.EffortScore,
                item.UrgencyScore,
                item.GoalAlignmentScore,
                item.Confidence,
                item.PriorityScore,
                item.Status,
                item.AssignedToName,
                item.UpdatedAt
            }).ToList();

            var impactMeasurements = await _agentRepository.GetImpactMeasurementsAsync(
                organizationId.Value, limit: 20, cancellationToken: ct);
            rawData["agentImpactMeasurements"] = impactMeasurements.Select(item => new
            {
                item.Id,
                item.RecommendationId,
                item.Status,
                item.Outcome,
                item.MonitoringWindowDays,
                item.BaselineCapturedAt,
                item.MeasurementDueAt,
                item.MeasuredAt,
                Baseline = ParseEvidence(item.BaselineJson),
                Followup = ParseEvidence(item.FollowupJson),
                Delta = ParseEvidence(item.DeltaJson),
                Evidence = ParseEvidence(item.EvidenceJson),
                Report = ParseEvidence(item.ReportJson),
                item.Confidence,
                item.ErrorMessage
            }).ToList();

            var citationHistory = await _citationSnapshotRepository.GetRecentSummaryHistoryAsync(organizationId.Value, 8);
            rawData["citationHistory"] = citationHistory.Select(item => new
            {
                item.ScanDate,
                item.CompositeQualityScore,
                item.AverageAuthorityScore,
                item.AverageInfluenceScore,
                item.CitationSignal,
                item.ModelsReferencingCount,
                item.ModelsTrackedCount
            }).ToList();

            var competitorHistory = await _competitorSnapshotRepository.GetRecentHistoryAsync(organizationId.Value, 4);
            rawData["competitorHistory"] = competitorHistory.Select(item => new
            {
                item.ScanDate,
                item.Name,
                item.IsYou,
                item.Rank,
                item.ShareOfVoice,
                item.Visibility,
                item.Threat,
                item.MeasurementSource,
                item.MethodologyVersion
            }).ToList();
        }
        
        return rawData;
    }

    private static JsonElement ParseEvidence(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { raw = json });
        }
    }
}
