using System.Text.Json;
using System.Text;
using Citationly.Application.Features.Content;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Citationly.Application.Features.Assistant.Agents;

public sealed class AgentAutomationService : IAgentAutomationService
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly IAgentControlPlaneRepository _agents;
    private readonly IVisibilitySnapshotRepository _visibility;
    private readonly ICitationScanSnapshotRepository _citations;
    private readonly ICompetitorSnapshotRepository _competitors;
    private readonly IBrandPulseSnapshotRepository _brandPulse;
    private readonly IAlertRepository _alerts;
    private readonly IAiCompletionService _ai;
    private readonly IKnowledgeBaseRepository _knowledgeBases;
    private readonly IScrapingJobRepository _scrapingJobs;
    private readonly IContentDraftRepository _contentDrafts;
    private readonly IMediator _mediator;
    private readonly ILogger<AgentAutomationService> _logger;

    public AgentAutomationService(
        IAgentControlPlaneRepository agents,
        IVisibilitySnapshotRepository visibility,
        ICitationScanSnapshotRepository citations,
        ICompetitorSnapshotRepository competitors,
        IBrandPulseSnapshotRepository brandPulse,
        IAlertRepository alerts,
        IAiCompletionService ai,
        IKnowledgeBaseRepository knowledgeBases,
        IScrapingJobRepository scrapingJobs,
        IContentDraftRepository contentDrafts,
        IMediator mediator,
        ILogger<AgentAutomationService> logger)
    {
        _agents = agents;
        _visibility = visibility;
        _citations = citations;
        _competitors = competitors;
        _brandPulse = brandPulse;
        _alerts = alerts;
        _ai = ai;
        _knowledgeBases = knowledgeBases;
        _scrapingJobs = scrapingJobs;
        _contentDrafts = contentDrafts;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task ProcessCompletedScanAsync(
        Guid organizationId,
        string scanType,
        DateOnly scanDate,
        CancellationToken cancellationToken = default)
    {
        if (!KnownScanType(scanType))
        {
            _logger.LogWarning("Visibility Monitor ignored unknown scan type {ScanType}", scanType);
            return;
        }

        AgentRun? monitorRun = null;
        try
        {
            await _agents.EnsureDefaultsAsync(organizationId, cancellationToken);
            try
            {
                await ProcessDueImpactMeasurementsAsync(organizationId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception impactError)
            {
                _logger.LogError(
                    impactError,
                    "Due impact processing failed after a completed {ScanType} scan; visibility monitoring will continue.",
                    scanType);
            }
            if (!await CanRunAsync(organizationId, CitationlyAgentKeys.VisibilityMonitor, "scan.completed", cancellationToken))
                return;

            monitorRun = await _agents.CreateRunAsync(new AgentRun
            {
                OrganizationId = organizationId,
                AgentKey = CitationlyAgentKeys.VisibilityMonitor,
                TriggerType = "Event",
                TriggerReference = $"{scanType}.scan.completed",
                Status = AgentRunStatuses.Running,
                IdempotencyKey = $"monitor:{scanType}:{scanDate:yyyy-MM-dd}",
                InputJson = JsonSerializer.Serialize(new { scanType, scanDate }),
                StartedAt = DateTime.UtcNow
            }, cancellationToken);

            // A repeated delivery of the same scan completion event returns the existing run.
            if (monitorRun.Status is AgentRunStatuses.Completed or AgentRunStatuses.Failed or AgentRunStatuses.Cancelled)
                return;

            await AddEventAsync(monitorRun, "run.started", AgentRunStatuses.Running,
                $"Comparing the {scanType} scan with its previous dated snapshot.", null, cancellationToken);

            var observations = await DetectChangesAsync(organizationId, scanType, scanDate);
            var findings = new List<AgentFinding>();
            foreach (var observation in observations)
            {
                var finding = await PersistFindingAsync(monitorRun, scanType, scanDate, observation, cancellationToken);
                findings.Add(finding);

                await AddEventAsync(monitorRun, "finding.created", AgentRunStatuses.Running,
                    finding.Title, new { finding.Id, finding.Severity, finding.FindingType }, cancellationToken);

                if (observation.Change.IsAdverse)
                    await CreateAlertAsync(organizationId, finding, observation.ActionUrl);
            }

            monitorRun.Status = AgentRunStatuses.Completed;
            monitorRun.OutputJson = JsonSerializer.Serialize(new
            {
                scanType,
                scanDate,
                materialChangeDetected = observations.Count > 0,
                findingCount = findings.Count,
                importantFindingCount = findings.Count(IsImportant),
                findingIds = findings.Select(f => f.Id)
            });
            monitorRun.CompletedAt = DateTime.UtcNow;
            await _agents.UpdateRunExecutionAsync(monitorRun, cancellationToken);
            await AddEventAsync(monitorRun, "run.completed", AgentRunStatuses.Completed,
                findings.Count == 0
                    ? "No material change crossed the configured deterministic thresholds."
                    : $"Created {findings.Count} evidence-linked finding(s).",
                new { findingIds = findings.Select(f => f.Id) }, cancellationToken);

            var important = findings.Where(IsImportant).ToList();
            if (important.Count > 0)
            {
                try
                {
                    await RunAnalystAsync(organizationId, scanType, scanDate, monitorRun, important, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception analystError)
                {
                    _logger.LogError(analystError,
                        "Intelligence Analyst failed for monitor run {RunId}; the completed monitor result is preserved.",
                        monitorRun.Id);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (monitorRun != null && monitorRun.Status != AgentRunStatuses.Completed)
            {
                monitorRun.Status = AgentRunStatuses.Cancelled;
                monitorRun.ErrorCode = "monitor_cancelled";
                monitorRun.ErrorMessage = "The scan-triggered monitor run was cancelled.";
                monitorRun.CompletedAt = DateTime.UtcNow;
                await _agents.UpdateRunExecutionAsync(monitorRun, CancellationToken.None);
            }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Agent automation failed after {ScanType} scan for organization {OrganizationId}",
                scanType, organizationId);

            if (monitorRun != null && monitorRun.Status != AgentRunStatuses.Completed)
            {
                monitorRun.Status = AgentRunStatuses.Failed;
                monitorRun.ErrorCode = "monitor_failed";
                monitorRun.ErrorMessage = SafeError(ex.Message);
                monitorRun.CompletedAt = DateTime.UtcNow;
                try
                {
                    await _agents.UpdateRunExecutionAsync(monitorRun, CancellationToken.None);
                    await AddEventAsync(monitorRun, "run.failed", AgentRunStatuses.Failed,
                        "The monitor could not complete its comparison.", new { monitorRun.ErrorCode }, CancellationToken.None);
                }
                catch (Exception persistenceError)
                {
                    _logger.LogError(persistenceError, "Could not persist failed monitor run {RunId}", monitorRun.Id);
                }
            }
        }
    }

    public async Task ProcessApprovedRecommendationAsync(
        Guid organizationId,
        Guid recommendationId,
        Guid initiatedByUserId,
        CancellationToken cancellationToken = default)
    {
        await _agents.EnsureDefaultsAsync(organizationId, cancellationToken);
        if (!await CanRunAsync(organizationId, CitationlyAgentKeys.ContentExecution, "recommendation.approved", cancellationToken, requiresAi: true))
            return;

        var recommendation = await _agents.GetRecommendationAsync(organizationId, recommendationId, cancellationToken);
        if (recommendation == null || recommendation.Status is not (
                AgentRecommendationStatuses.Approved or
                AgentRecommendationStatuses.Assigned or
                AgentRecommendationStatuses.InProgress))
            return;

        var existingExecution = await _agents.GetContentExecutionByRecommendationAsync(organizationId, recommendationId, cancellationToken);
        if (existingExecution is { Status: not AgentContentExecutionStatuses.Failed })
            return;

        var run = await _agents.CreateRunAsync(new AgentRun
        {
            OrganizationId = organizationId,
            AgentKey = CitationlyAgentKeys.ContentExecution,
            InitiatedByUserId = initiatedByUserId,
            ParentRunId = recommendation.RunId,
            TriggerType = "Event",
            TriggerReference = "recommendation.approved",
            Status = AgentRunStatuses.Running,
            IdempotencyKey = $"content:{recommendation.Id:N}",
            InputJson = JsonSerializer.Serialize(new { recommendationId = recommendation.Id, recommendation.ApprovalId }),
            StartedAt = DateTime.UtcNow
        }, cancellationToken);
        if (run.Status is AgentRunStatuses.Completed or AgentRunStatuses.WaitingForApproval or AgentRunStatuses.Cancelled)
            return;

        AgentContentExecution? execution = null;
        try
        {
            await AddEventAsync(run, "run.started", AgentRunStatuses.Running,
                "Preparing a Knowledge Vault-grounded brief and draft from the approved recommendation.",
                new { recommendationId }, cancellationToken);

            execution = await _agents.UpsertContentExecutionAsync(new AgentContentExecution
            {
                OrganizationId = organizationId,
                RunId = run.Id,
                RecommendationId = recommendation.Id,
                Status = AgentContentExecutionStatuses.Preparing
            }, cancellationToken);

            var knowledgeBases = (await _knowledgeBases.GetByOrgAsync(organizationId)).ToList();
            KnowledgeBase? selectedKnowledgeBase = null;
            IReadOnlyList<ContentGroundingSource> selectedSources = [];
            var bestScore = int.MinValue;
            foreach (var knowledgeBase in knowledgeBases)
            {
                var pages = await _scrapingJobs.GetPagesByKnowledgeBaseAsync(organizationId, knowledgeBase.Id, 100);
                var sources = ContentExecutionGrounding.SelectSources(pages, recommendation);
                if (sources.Count == 0) continue;
                var score = sources.Sum(source => source.RelevanceScore);
                if (selectedKnowledgeBase == null || score > bestScore)
                {
                    selectedKnowledgeBase = knowledgeBase;
                    selectedSources = sources;
                    bestScore = score;
                }
            }

            if (selectedKnowledgeBase == null || selectedSources.Count == 0)
            {
                var noEvidencePolicyChecks = ContentExecutionGrounding.EvaluatePolicy(string.Empty, string.Empty, selectedSources);
                execution.Status = AgentContentExecutionStatuses.NeedsEvidence;
                execution.PolicyChecksJson = SerializePolicyChecks(noEvidencePolicyChecks);
                execution.ReviewNote = "Add or crawl relevant sources in Knowledge Vault, then retry content execution.";
                execution = await _agents.UpsertContentExecutionAsync(execution, cancellationToken);

                run.Status = AgentRunStatuses.Completed;
                run.OutputJson = JsonSerializer.Serialize(new { executionId = execution.Id, execution.Status, sourceCount = 0 });
                run.CompletedAt = DateTime.UtcNow;
                await _agents.UpdateRunExecutionAsync(run, cancellationToken);
                await AddEventAsync(run, "content.evidence_required", AgentRunStatuses.Completed,
                    "Drafting was safely blocked because no indexed Knowledge Vault evidence was available.",
                    new { executionId = execution.Id }, cancellationToken);
                return;
            }

            var sourceContext = new StringBuilder();
            for (var index = 0; index < selectedSources.Count; index++)
            {
                var source = selectedSources[index];
                sourceContext.AppendLine($"Source {index + 1}: {source.Title}");
                sourceContext.AppendLine($"URL: {source.Url}");
                sourceContext.AppendLine(source.Excerpt);
                sourceContext.AppendLine("---");
            }

            const string systemPrompt =
                "You are Citationly's Content Execution Agent. Create a content brief and a full Markdown draft " +
                "using ONLY facts supported by the supplied Knowledge Vault sources. Every factual claim must cite " +
                "one or more supplied sources using the exact marker [Source N]. Never invent statistics, customers, " +
                "features, quotes, or outcomes. Return ONLY JSON with: title (string), brief (object with objective, " +
                "audience, searchIntent, primaryKeyword, supportingKeywords array, outline array, callToAction, " +
                "claimsToVerify array), and contentMarkdown (string). The Markdown must start with one H1.";
            var userPrompt = JsonSerializer.Serialize(new
            {
                approvedRecommendation = new
                {
                    recommendation.Title,
                    recommendation.Summary,
                    recommendation.Rationale,
                    recommendation.TargetType,
                    recommendation.TargetKey,
                    recommendation.ExpectedImpact,
                    actionPlan = ParseJsonEvidence(recommendation.ActionPlanJson),
                    evidence = ParseJsonEvidence(recommendation.EvidenceJson)
                },
                knowledgeVault = new
                {
                    selectedKnowledgeBase.Id,
                    selectedKnowledgeBase.Name,
                    sources = sourceContext.ToString()
                }
            });

            var completion = await _ai.CompleteAsync(
                organizationId,
                "agent.content_execution.draft",
                userPrompt,
                systemPrompt,
                requireJson: true,
                preferredProviderKey: "openai",
                cancellationToken);
            run.Provider = completion.ProviderKey ?? completion.UpstreamProvider ?? string.Empty;
            run.Model = completion.ModelUsed ?? string.Empty;
            run.PromptTokens = Math.Max(0, completion.PromptTokens ?? 0);
            run.CompletionTokens = Math.Max(0, completion.CompletionTokens ?? 0);
            run.CostMicroUsd = completion.CostUsd.HasValue
                ? Math.Max(0, (long)decimal.Round(completion.CostUsd.Value * 1_000_000m))
                : 0;
            if (!completion.Success)
                throw new InvalidOperationException(completion.ErrorMessage ?? "The content model was unavailable.");

            using var completionDocument = JsonDocument.Parse(completion.Content);
            var root = completionDocument.RootElement;
            var title = root.TryGetProperty("title", out var titleElement)
                ? titleElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            var markdown = root.TryGetProperty("contentMarkdown", out var contentElement)
                ? contentElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(markdown))
                throw new InvalidOperationException("The content model returned no draft.");

            var draft = await _contentDrafts.CreateAsync(new ContentDraft
            {
                OrganizationId = organizationId,
                Title = string.IsNullOrWhiteSpace(title) ? recommendation.Title : title,
                ContentType = recommendation.RecommendationType,
                Content = markdown,
                WordCount = CountContentWords(markdown),
                Status = "Draft",
                RequestJson = JsonSerializer.Serialize(new
                {
                    source = "content-execution-agent",
                    recommendationId = recommendation.Id,
                    runId = run.Id,
                    knowledgeBaseId = selectedKnowledgeBase.Id
                })
            });

            var policyChecks = ContentExecutionGrounding.EvaluatePolicy(draft.Title, draft.Content, selectedSources);
            execution.ContentDraftId = draft.Id;
            execution.KnowledgeBaseId = selectedKnowledgeBase.Id;
            execution.BriefJson = root.TryGetProperty("brief", out var briefElement)
                ? briefElement.GetRawText()
                : "{}";
            execution.EvidenceJson = JsonSerializer.Serialize(selectedSources.Select((source, index) => new
            {
                sourceNumber = index + 1,
                source.PageId,
                source.Title,
                source.Url,
                source.RelevanceScore
            }));
            execution.ReviewDiffJson = JsonSerializer.Serialize(new
            {
                baseline = "New content",
                changes = ContentExecutionGrounding.BuildReviewDiff(markdown)
            });
            execution.PolicyChecksJson = SerializePolicyChecks(policyChecks);
            execution.Status = ContentExecutionGrounding.HasBlockingFailure(policyChecks)
                ? AgentContentExecutionStatuses.Failed
                : AgentContentExecutionStatuses.ReadyForReview;
            execution.ReviewNote = execution.Status == AgentContentExecutionStatuses.Failed
                ? "The draft failed one or more blocking grounding checks and cannot be submitted for publishing."
                : string.Empty;
            execution = await _agents.UpsertContentExecutionAsync(execution, cancellationToken);

            run.Status = execution.Status == AgentContentExecutionStatuses.Failed
                ? AgentRunStatuses.Failed
                : AgentRunStatuses.Completed;
            run.ErrorCode = run.Status == AgentRunStatuses.Failed ? "content_policy_failed" : string.Empty;
            run.ErrorMessage = run.Status == AgentRunStatuses.Failed ? execution.ReviewNote : string.Empty;
            run.OutputJson = JsonSerializer.Serialize(new
            {
                executionId = execution.Id,
                contentDraftId = draft.Id,
                execution.Status,
                sourceCount = selectedSources.Count,
                policyChecks
            });
            run.CompletedAt = DateTime.UtcNow;
            await _agents.UpdateRunExecutionAsync(run, cancellationToken);
            await AddEventAsync(run, "content.draft_created", run.Status,
                execution.Status == AgentContentExecutionStatuses.ReadyForReview
                    ? "Created a grounded draft for review; live publishing still requires separate approval."
                    : "Created a draft, but blocking grounding policy checks prevented publishing review.",
                new { executionId = execution.Id, contentDraftId = draft.Id }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            run.Status = AgentRunStatuses.Failed;
            run.ErrorCode = "content_execution_failed";
            run.ErrorMessage = SafeError(ex.Message);
            run.CompletedAt = DateTime.UtcNow;
            await _agents.UpdateRunExecutionAsync(run, CancellationToken.None);
            if (execution != null)
                await _agents.UpdateContentExecutionStatusAsync(
                    organizationId, execution.Id, AgentContentExecutionStatuses.Failed, run.ErrorMessage, cancellationToken: CancellationToken.None);
            await AddEventAsync(run, "run.failed", AgentRunStatuses.Failed,
                "The Content Execution Agent could not produce a reviewable grounded draft.",
                new { run.ErrorCode }, CancellationToken.None);
            _logger.LogError(ex, "Content execution failed for recommendation {RecommendationId}", recommendationId);
        }
    }

    public async Task<AgentContentExecution?> RequestContentPublishApprovalAsync(
        Guid organizationId,
        Guid executionId,
        Guid initiatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var execution = await _agents.GetContentExecutionAsync(organizationId, executionId, cancellationToken);
        if (execution == null) return null;
        if (execution.PublishApprovalId.HasValue) return execution;
        if (execution.Status != AgentContentExecutionStatuses.ReadyForReview || !execution.ContentDraftId.HasValue)
            return null;
        if (HasBlockingPolicyFailure(execution.PolicyChecksJson))
            return null;

        var draft = await _contentDrafts.GetByIdAsync(execution.ContentDraftId.Value);
        if (draft == null || draft.OrganizationId != organizationId) return null;
        var recommendation = await _agents.GetRecommendationAsync(organizationId, execution.RecommendationId, cancellationToken);
        if (recommendation == null) return null;

        var approval = await _agents.CreateApprovalAsync(new AgentApproval
        {
            OrganizationId = organizationId,
            RunId = execution.RunId,
            FindingId = recommendation.FindingId,
            AgentKey = CitationlyAgentKeys.ContentExecution,
            ActionType = "content.publish",
            Title = $"Publish to WordPress: {draft.Title}",
            Description = "Approving this action authorizes the agent to publish this reviewed draft to the connected WordPress site.",
            PayloadJson = JsonSerializer.Serialize(new
            {
                executionId = execution.Id,
                contentDraftId = draft.Id,
                destination = "WordPress",
                draft.Title,
                requestedByUserId = initiatedByUserId
            }),
            RiskLevel = "High",
            IdempotencyKey = $"content-publish:{execution.Id:N}",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        }, cancellationToken);
        execution = await _agents.LinkContentPublishApprovalAsync(
            organizationId, execution.Id, approval.Id, cancellationToken);
        if (execution != null)
        {
            await AddEventAsync(new AgentRun { Id = execution.RunId, OrganizationId = organizationId },
                "content.publish_approval_requested", AgentRunStatuses.WaitingForApproval,
                "A manager must approve the exact WordPress publishing action before any live-site mutation.",
                new { executionId, approvalId = approval.Id, draftId = draft.Id }, cancellationToken);
        }
        return execution;
    }

    public async Task ProcessApprovedContentPublishAsync(
        Guid organizationId,
        Guid executionId,
        Guid approvalId,
        CancellationToken cancellationToken = default)
    {
        var execution = await _agents.GetContentExecutionAsync(organizationId, executionId, cancellationToken);
        if (execution == null || execution.PublishApprovalId != approvalId ||
            execution.Status != AgentContentExecutionStatuses.ApprovedForPublishing ||
            !execution.ContentDraftId.HasValue)
            return;

        await _agents.UpdateContentExecutionStatusAsync(
            organizationId, execution.Id, AgentContentExecutionStatuses.Publishing, string.Empty, cancellationToken: cancellationToken);
        try
        {
            var result = await _mediator.Send(new PublishContentDraftCommand
            {
                OrganizationId = organizationId,
                DraftId = execution.ContentDraftId.Value
            }, cancellationToken);

            await _agents.FinalizeContentPublishAsync(
                organizationId,
                execution.Id,
                approvalId,
                result.Success,
                result.Message,
                cancellationToken);
            await AddEventAsync(new AgentRun { Id = execution.RunId, OrganizationId = organizationId },
                result.Success ? "content.published" : "content.publish_failed",
                result.Success ? AgentRunStatuses.Completed : AgentRunStatuses.Failed,
                result.Message,
                new { executionId, execution.ContentDraftId, result.PublishedUrl }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"Publish failed: {SafeError(ex.Message)}";
            await _agents.FinalizeContentPublishAsync(
                organizationId, execution.Id, approvalId, false, message, CancellationToken.None);
            await AddEventAsync(new AgentRun { Id = execution.RunId, OrganizationId = organizationId },
                "content.publish_failed", AgentRunStatuses.Failed, message,
                new { executionId, execution.ContentDraftId }, CancellationToken.None);
            _logger.LogError(ex, "Approved content publish failed for execution {ExecutionId}", executionId);
        }
    }

    public async Task<AgentImpactMeasurement?> ScheduleRecommendationImpactAsync(
        Guid organizationId,
        Guid recommendationId,
        Guid initiatedByUserId,
        int monitoringWindowDays = 14,
        CancellationToken cancellationToken = default)
    {
        await _agents.EnsureDefaultsAsync(organizationId, cancellationToken);
        var existing = await _agents.GetImpactMeasurementByRecommendationAsync(
            organizationId, recommendationId, cancellationToken);
        if (existing != null) return existing;
        if (!await CanRunAsync(organizationId, CitationlyAgentKeys.ImpactReporting, "recommendation.implemented", cancellationToken))
            return null;

        var recommendation = await _agents.GetRecommendationAsync(organizationId, recommendationId, cancellationToken);
        if (recommendation?.Status != AgentRecommendationStatuses.Implemented)
            return null;

        monitoringWindowDays = Math.Clamp(monitoringWindowDays, 1, 90);
        var run = await _agents.CreateRunAsync(new AgentRun
        {
            OrganizationId = organizationId,
            AgentKey = CitationlyAgentKeys.ImpactReporting,
            InitiatedByUserId = initiatedByUserId,
            ParentRunId = recommendation.RunId,
            TriggerType = "Event",
            TriggerReference = "recommendation.implemented",
            Status = AgentRunStatuses.Running,
            IdempotencyKey = $"impact-baseline:{recommendation.Id:N}",
            InputJson = JsonSerializer.Serialize(new { recommendationId, monitoringWindowDays }),
            StartedAt = DateTime.UtcNow
        }, cancellationToken);
        if (run.Status is AgentRunStatuses.Completed or AgentRunStatuses.WaitingForApproval or AgentRunStatuses.Cancelled)
            return await _agents.GetImpactMeasurementByRecommendationAsync(organizationId, recommendationId, cancellationToken);

        try
        {
            await AddEventAsync(run, "impact.baseline_started", AgentRunStatuses.Running,
                "Capturing the latest persisted metrics before the follow-up measurement window.",
                new { recommendationId, monitoringWindowDays }, cancellationToken);

            var capturedAt = DateTime.UtcNow;
            var baseline = await CaptureImpactSnapshotAsync(organizationId, notBefore: null);
            var hasRelevantBaseline = AgentImpactEvaluator.HasRelevantMetric(recommendation.RecommendationType, baseline);
            var measurement = await _agents.CreateImpactMeasurementAsync(new AgentImpactMeasurement
            {
                OrganizationId = organizationId,
                RecommendationId = recommendation.Id,
                BaselineRunId = run.Id,
                Status = hasRelevantBaseline
                    ? AgentImpactMeasurementStatuses.Pending
                    : AgentImpactMeasurementStatuses.NeedsBaseline,
                Outcome = hasRelevantBaseline ? AgentImpactOutcomes.Pending : AgentImpactOutcomes.Inconclusive,
                MonitoringWindowDays = monitoringWindowDays,
                BaselineCapturedAt = capturedAt,
                MeasurementDueAt = capturedAt.AddDays(monitoringWindowDays),
                BaselineJson = JsonSerializer.Serialize(baseline, WebJson),
                EvidenceJson = JsonSerializer.Serialize(new
                {
                    recommendationId = recommendation.Id,
                    recommendation.RecommendationType,
                    recommendation.Category,
                    capturedAt,
                    observationDates = ImpactObservationDates(baseline),
                    attribution = "Baseline is a persisted observation. Later before/after movement is an association and does not prove causation."
                }),
                ReportJson = JsonSerializer.Serialize(new
                {
                    executiveSummary = hasRelevantBaseline
                        ? $"Baseline captured. Follow-up measurement is due in {monitoringWindowDays} days."
                        : "Impact measurement is inconclusive because no relevant pre-implementation snapshot was available.",
                    agencySummary = "No external report has been delivered. This record is available for client review in Agent Center."
                }),
                Confidence = hasRelevantBaseline ? 0.15m : 0m,
                ErrorMessage = hasRelevantBaseline
                    ? string.Empty
                    : "No relevant persisted baseline existed when the recommendation was marked implemented."
            }, cancellationToken);

            run.Status = AgentRunStatuses.Completed;
            run.OutputJson = JsonSerializer.Serialize(new
            {
                measurementId = measurement.Id,
                measurement.Status,
                measurement.MeasurementDueAt,
                hasRelevantBaseline
            });
            run.CompletedAt = DateTime.UtcNow;
            await _agents.UpdateRunExecutionAsync(run, cancellationToken);
            await AddEventAsync(run, "impact.baseline_captured", AgentRunStatuses.Completed,
                hasRelevantBaseline
                    ? $"Baseline captured; follow-up is due {measurement.MeasurementDueAt:yyyy-MM-dd}."
                    : "No relevant baseline was available, so the impact result is explicitly inconclusive.",
                new { measurementId = measurement.Id }, cancellationToken);
            return measurement;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            run.Status = AgentRunStatuses.Failed;
            run.ErrorCode = "impact_baseline_failed";
            run.ErrorMessage = SafeError(ex.Message);
            run.CompletedAt = DateTime.UtcNow;
            await _agents.UpdateRunExecutionAsync(run, CancellationToken.None);
            await AddEventAsync(run, "run.failed", AgentRunStatuses.Failed,
                "The Impact & Reporting Agent could not capture its baseline.",
                new { run.ErrorCode }, CancellationToken.None);
            _logger.LogError(ex, "Impact baseline failed for recommendation {RecommendationId}", recommendationId);
            return null;
        }
    }

    public async Task<int> ProcessDueImpactMeasurementsAsync(
        Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var due = await _agents.GetDueImpactMeasurementsAsync(
            DateTime.UtcNow, organizationId, 200, cancellationToken);
        var measured = 0;
        foreach (var dueMeasurement in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var measurement = dueMeasurement;
            await _agents.EnsureDefaultsAsync(measurement.OrganizationId, cancellationToken);
            if (!await CanRunAsync(
                    measurement.OrganizationId,
                    CitationlyAgentKeys.ImpactReporting,
                    "recommendation.implemented",
                    cancellationToken))
                continue;

            var recommendation = await _agents.GetRecommendationAsync(
                measurement.OrganizationId, measurement.RecommendationId, cancellationToken);
            if (recommendation == null)
            {
                measurement.Status = AgentImpactMeasurementStatuses.Failed;
                measurement.Outcome = AgentImpactOutcomes.Inconclusive;
                measurement.ErrorMessage = "The linked recommendation no longer exists.";
                await _agents.UpdateImpactMeasurementAsync(measurement, cancellationToken);
                continue;
            }

            AgentImpactSnapshot baseline;
            try
            {
                baseline = JsonSerializer.Deserialize<AgentImpactSnapshot>(measurement.BaselineJson, WebJson)
                    ?? throw new JsonException("Baseline JSON was empty.");
            }
            catch (JsonException ex)
            {
                measurement.Status = AgentImpactMeasurementStatuses.Failed;
                measurement.Outcome = AgentImpactOutcomes.Inconclusive;
                measurement.ErrorMessage = SafeError(ex.Message);
                await _agents.UpdateImpactMeasurementAsync(measurement, cancellationToken);
                continue;
            }

            var followup = await CaptureImpactSnapshotAsync(
                measurement.OrganizationId, measurement.MeasurementDueAt);
            if (!AgentImpactEvaluator.HasRelevantMetric(recommendation.RecommendationType, followup))
            {
                measurement.Status = AgentImpactMeasurementStatuses.WaitingForData;
                measurement.ErrorMessage = "The measurement window has elapsed, but no relevant post-window scan is available yet.";
                await _agents.UpdateImpactMeasurementAsync(measurement, cancellationToken);
                continue;
            }

            var evaluation = AgentImpactEvaluator.Evaluate(baseline, followup);
            var run = await _agents.CreateRunAsync(new AgentRun
            {
                OrganizationId = measurement.OrganizationId,
                AgentKey = CitationlyAgentKeys.ImpactReporting,
                ParentRunId = measurement.BaselineRunId,
                TriggerType = "Event",
                TriggerReference = "measurement.due",
                Status = AgentRunStatuses.Running,
                IdempotencyKey = $"impact-measure:{measurement.Id:N}",
                InputJson = JsonSerializer.Serialize(new
                {
                    measurementId = measurement.Id,
                    measurement.RecommendationId,
                    measurement.MeasurementDueAt
                }),
                StartedAt = DateTime.UtcNow
            }, cancellationToken);
            if (run.Status is AgentRunStatuses.Completed or AgentRunStatuses.WaitingForApproval or AgentRunStatuses.Cancelled)
                continue;

            try
            {
                await AddEventAsync(run, "impact.measurement_started", AgentRunStatuses.Running,
                    "Comparing the persisted baseline with post-window snapshots.",
                    new { measurementId = measurement.Id }, cancellationToken);

                var evidence = new
                {
                    recommendationId = recommendation.Id,
                    recommendation.RecommendationType,
                    recommendation.Category,
                    baseline,
                    followup,
                    delta = evaluation.Delta,
                    evaluation.DirectionalScore,
                    evaluation.ComparableMetricCount,
                    attribution = "Observed after implementation; correlation does not establish that the recommendation caused the change."
                };
                var report = new
                {
                    title = $"Impact report: {recommendation.Title}",
                    outcome = evaluation.Outcome,
                    confidence = evaluation.Confidence,
                    executiveSummary = evaluation.Summary,
                    agencySummary = BuildAgencyImpactSummary(recommendation, measurement, evaluation),
                    measurementWindow = new
                    {
                        baselineCapturedAt = measurement.BaselineCapturedAt,
                        dueAt = measurement.MeasurementDueAt,
                        measuredAt = DateTime.UtcNow
                    },
                    metrics = BuildImpactMetricRows(baseline, followup, evaluation.Delta),
                    delivery = new
                    {
                        status = "Prepared",
                        externalDelivery = false,
                        note = "External report delivery is not automatic and remains configurable."
                    }
                };

                measurement.MeasurementRunId = run.Id;
                measurement.Status = AgentImpactMeasurementStatuses.Measured;
                measurement.Outcome = evaluation.Outcome;
                measurement.MeasuredAt = DateTime.UtcNow;
                measurement.FollowupJson = JsonSerializer.Serialize(followup, WebJson);
                measurement.DeltaJson = JsonSerializer.Serialize(evaluation.Delta, WebJson);
                measurement.EvidenceJson = JsonSerializer.Serialize(evidence, WebJson);
                measurement.ReportJson = JsonSerializer.Serialize(report, WebJson);
                measurement.Confidence = evaluation.Confidence;
                measurement.ErrorMessage = string.Empty;
                measurement = await _agents.UpdateImpactMeasurementAsync(measurement, cancellationToken)
                    ?? measurement;

                var finding = await _agents.UpsertFindingAsync(new AgentFinding
                {
                    OrganizationId = measurement.OrganizationId,
                    RunId = run.Id,
                    AgentKey = CitationlyAgentKeys.ImpactReporting,
                    FindingType = $"impact.{evaluation.Outcome.ToLowerInvariant()}",
                    Severity = evaluation.Outcome switch
                    {
                        AgentImpactOutcomes.Improved => "Good",
                        AgentImpactOutcomes.Regressed => "High",
                        _ => "Info"
                    },
                    Title = $"{evaluation.Outcome}: {recommendation.Title}",
                    Summary = evaluation.Summary,
                    EntityType = "agent-recommendation-impact",
                    EntityIdsJson = JsonSerializer.Serialize(new[] { recommendation.Id, measurement.Id }),
                    EvidenceJson = measurement.EvidenceJson,
                    ObservationStartedAt = measurement.BaselineCapturedAt,
                    ObservationEndedAt = measurement.MeasuredAt,
                    Confidence = evaluation.Confidence,
                    DeduplicationKey = $"impact:{measurement.Id:N}",
                    Status = "Explained"
                }, cancellationToken);

                AgentRecommendation? followupRecommendation = null;
                if (evaluation.Outcome == AgentImpactOutcomes.Regressed)
                {
                    followupRecommendation = await CreateImpactFollowupRecommendationAsync(
                        recommendation, measurement, finding, run, evaluation, cancellationToken);
                }

                run.Status = followupRecommendation?.Status == AgentRecommendationStatuses.AwaitingApproval
                    ? AgentRunStatuses.WaitingForApproval
                    : AgentRunStatuses.Completed;
                run.OutputJson = JsonSerializer.Serialize(new
                {
                    measurementId = measurement.Id,
                    measurement.Outcome,
                    measurement.Confidence,
                    findingId = finding.Id,
                    followupRecommendationId = followupRecommendation?.Id
                });
                run.CompletedAt = run.Status == AgentRunStatuses.Completed ? DateTime.UtcNow : null;
                await _agents.UpdateRunExecutionAsync(run, cancellationToken);
                await AddEventAsync(run, "impact.measured", run.Status,
                    evaluation.Summary,
                    new { measurementId = measurement.Id, findingId = finding.Id }, cancellationToken);
                measured++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                run.Status = AgentRunStatuses.Failed;
                run.ErrorCode = "impact_measurement_failed";
                run.ErrorMessage = SafeError(ex.Message);
                run.CompletedAt = DateTime.UtcNow;
                await _agents.UpdateRunExecutionAsync(run, CancellationToken.None);
                measurement.Status = AgentImpactMeasurementStatuses.Failed;
                measurement.Outcome = AgentImpactOutcomes.Inconclusive;
                measurement.ErrorMessage = run.ErrorMessage;
                await _agents.UpdateImpactMeasurementAsync(measurement, CancellationToken.None);
                await AddEventAsync(run, "run.failed", AgentRunStatuses.Failed,
                    "The Impact & Reporting Agent could not complete the due comparison.",
                    new { run.ErrorCode }, CancellationToken.None);
                _logger.LogError(ex, "Impact measurement failed for {MeasurementId}", measurement.Id);
            }
        }

        return measured;
    }

    private async Task<AgentImpactSnapshot> CaptureImpactSnapshotAsync(Guid organizationId, DateTime? notBefore)
    {
        var visibilityHistory = await _visibility.GetRecentSummaryHistoryAsync(organizationId, 50);
        var citationHistory = await _citations.GetRecentSummaryHistoryAsync(organizationId, 50);
        var competitorHistory = await _competitors.GetRecentHistoryAsync(organizationId, 50);
        var brandHistory = await _brandPulse.GetRecentSummaryHistoryAsync(organizationId, 50);

        var visibility = visibilityHistory
            .Where(item => !notBefore.HasValue || item.CreatedAt >= notBefore.Value)
            .OrderByDescending(item => item.ScanDate)
            .FirstOrDefault();
        var citations = citationHistory
            .Where(item => !notBefore.HasValue || item.CreatedAt >= notBefore.Value)
            .OrderByDescending(item => item.ScanDate)
            .FirstOrDefault();
        var ownCompetitor = competitorHistory
            .Where(item => item.IsYou && (!notBefore.HasValue || item.CreatedAt >= notBefore.Value))
            .OrderByDescending(item => item.ScanDate)
            .FirstOrDefault();
        var brand = brandHistory
            .Where(item => !notBefore.HasValue || item.CreatedAt >= notBefore.Value)
            .OrderByDescending(item => item.ScanDate)
            .FirstOrDefault();

        return new AgentImpactSnapshot(
            visibility?.ScanDate,
            visibility?.CompositeScore,
            citations?.ScanDate,
            citations?.CompositeQualityScore,
            citations?.CitationSignal,
            ownCompetitor?.ScanDate,
            ownCompetitor?.ShareOfVoice,
            ownCompetitor?.AveragePosition,
            ownCompetitor?.CitationCount,
            brand?.ScanDate,
            brand?.BrandHealth);
    }

    private async Task<AgentRecommendation?> CreateImpactFollowupRecommendationAsync(
        AgentRecommendation original,
        AgentImpactMeasurement measurement,
        AgentFinding finding,
        AgentRun run,
        AgentImpactEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        var recommendation = await _agents.UpsertRecommendationAsync(new AgentRecommendation
        {
            OrganizationId = original.OrganizationId,
            RunId = run.Id,
            FindingId = finding.Id,
            AgentKey = CitationlyAgentKeys.GeoStrategy,
            RecommendationType = original.RecommendationType,
            Category = original.Category,
            Title = $"Reassess after regression: {original.Title}",
            Summary = "The post-implementation measurement regressed. Reassess the evidence and implementation before making another live change.",
            Rationale = evaluation.Summary,
            TargetType = original.TargetType,
            TargetKey = original.TargetKey,
            EvidenceJson = measurement.EvidenceJson,
            ActionPlanJson = JsonSerializer.Serialize(new[]
            {
                "Review the measured baseline, follow-up snapshot dates, and metric deltas.",
                "Check whether the implementation matched the approved recommendation.",
                "Inspect external or seasonal factors before attributing the regression.",
                "Prepare a revised evidence-backed action and define a fresh validation window."
            }),
            ValidationPlanJson = JsonSerializer.Serialize(new
            {
                monitoringWindowDays = measurement.MonitoringWindowDays,
                required = "A new persisted scan after the revised implementation.",
                attribution = "Treat before/after movement as association, not causal proof."
            }),
            ExpectedImpact = "Return the regressed measured signals toward or above the captured baseline.",
            ImpactScore = Math.Max(80, original.ImpactScore),
            EffortScore = original.EffortScore,
            UrgencyScore = Math.Max(85, original.UrgencyScore),
            GoalAlignmentScore = original.GoalAlignmentScore,
            Confidence = evaluation.Confidence,
            PriorityScore = Math.Min(100m, Math.Max(85m, original.PriorityScore + 5m)),
            Status = AgentRecommendationStatuses.AwaitingApproval,
            DeduplicationKey = $"impact-followup:{original.Id:N}:{measurement.Id:N}"
        }, cancellationToken);

        if (recommendation.Status != AgentRecommendationStatuses.AwaitingApproval)
            return recommendation;
        if (recommendation.ApprovalId.HasValue)
            return recommendation;

        var approval = await _agents.CreateApprovalAsync(new AgentApproval
        {
            OrganizationId = original.OrganizationId,
            RunId = run.Id,
            FindingId = finding.Id,
            AgentKey = CitationlyAgentKeys.GeoStrategy,
            ActionType = "recommendation.approve",
            Title = $"Approve follow-up: {recommendation.Title}",
            Description = $"Measured regression with {evaluation.Confidence:P0} confidence. Review the evidence before authorizing new work.",
            PayloadJson = JsonSerializer.Serialize(new
            {
                recommendationId = recommendation.Id,
                sourceRecommendationId = original.Id,
                measurementId = measurement.Id,
                measurement.Outcome,
                measurement.Confidence
            }),
            RiskLevel = "Medium",
            IdempotencyKey = $"impact-followup-approval:{recommendation.Id:N}",
            ExpiresAt = DateTime.UtcNow.AddDays(14)
        }, cancellationToken);
        return await _agents.LinkRecommendationApprovalAsync(
            original.OrganizationId, recommendation.Id, approval.Id, cancellationToken) ?? recommendation;
    }

    private static object ImpactObservationDates(AgentImpactSnapshot snapshot) => new
    {
        visibility = snapshot.VisibilityObservedAt,
        citations = snapshot.CitationObservedAt,
        competitors = snapshot.CompetitorObservedAt,
        brand = snapshot.BrandObservedAt
    };

    private static string BuildAgencyImpactSummary(
        AgentRecommendation recommendation,
        AgentImpactMeasurement measurement,
        AgentImpactEvaluation evaluation) =>
        $"{recommendation.Category} recommendation measured after a {measurement.MonitoringWindowDays}-day window. " +
        $"{evaluation.Summary} Review the attached snapshot dates and deltas before communicating attribution to a client.";

    private static IReadOnlyList<object> BuildImpactMetricRows(
        AgentImpactSnapshot baseline,
        AgentImpactSnapshot followup,
        AgentImpactDelta delta)
    {
        var rows = new List<object>();
        AddImpactMetric(rows, "Visibility score", baseline.VisibilityScore, followup.VisibilityScore, delta.VisibilityScore, baseline.VisibilityObservedAt, followup.VisibilityObservedAt);
        AddImpactMetric(rows, "Citation quality", baseline.CitationQuality, followup.CitationQuality, delta.CitationQuality, baseline.CitationObservedAt, followup.CitationObservedAt);
        AddImpactMetric(rows, "Citation signal", baseline.CitationSignal, followup.CitationSignal, delta.CitationSignal, baseline.CitationObservedAt, followup.CitationObservedAt);
        AddImpactMetric(rows, "Share of voice", baseline.ShareOfVoice, followup.ShareOfVoice, delta.ShareOfVoice, baseline.CompetitorObservedAt, followup.CompetitorObservedAt);
        AddImpactMetric(rows, "Average position", baseline.AveragePosition, followup.AveragePosition, delta.AveragePosition, baseline.CompetitorObservedAt, followup.CompetitorObservedAt, lowerIsBetter: true);
        AddImpactMetric(rows, "Citation count", baseline.CitationCount, followup.CitationCount, delta.CitationCount, baseline.CompetitorObservedAt, followup.CompetitorObservedAt);
        AddImpactMetric(rows, "Brand health", baseline.BrandHealth, followup.BrandHealth, delta.BrandHealth, baseline.BrandObservedAt, followup.BrandObservedAt);
        return rows;
    }

    private static void AddImpactMetric(
        ICollection<object> rows,
        string label,
        int? baseline,
        int? followup,
        int? delta,
        DateOnly? baselineObservedAt,
        DateOnly? followupObservedAt,
        bool lowerIsBetter = false)
    {
        if (!baseline.HasValue || !followup.HasValue || !delta.HasValue) return;
        rows.Add(new
        {
            label,
            baseline,
            followup,
            delta,
            lowerIsBetter,
            baselineObservedAt,
            followupObservedAt
        });
    }

    private async Task<bool> CanRunAsync(Guid organizationId, string agentKey, string triggerExpression, CancellationToken ct, bool requiresAi = false)
    {
        var overview = await _agents.GetOverviewAsync(organizationId, ct);
        var setting = overview.FirstOrDefault(item => item.AgentKey == agentKey);
        if (setting == null || !setting.IsEnabled || setting.MaxRunsPerDay <= 0) return false;
        if (requiresAi && setting.MaxCostMicroUsdPerRun <= 0) return false;

        var schedules = await _agents.GetSchedulesAsync(organizationId, ct);
        var eventSchedule = schedules.FirstOrDefault(schedule =>
            schedule.AgentKey == agentKey &&
            schedule.TriggerType == "Event" &&
            schedule.TriggerExpression == triggerExpression);
        if (eventSchedule is { IsEnabled: false }) return false;

        var today = DateTime.UtcNow.Date;
        var runsToday = await _agents.CountRunsSinceAsync(organizationId, agentKey, today, ct);
        return runsToday < setting.MaxRunsPerDay;
    }

    private async Task<List<DetectedObservation>> DetectChangesAsync(Guid organizationId, string scanType, DateOnly scanDate)
    {
        return scanType switch
        {
            AgentScanTypes.Visibility => await DetectVisibilityAsync(organizationId, scanDate),
            AgentScanTypes.Citations => await DetectCitationsAsync(organizationId, scanDate),
            AgentScanTypes.Competitors => await DetectCompetitorsAsync(organizationId, scanDate),
            AgentScanTypes.BrandPulse => await DetectBrandPulseAsync(organizationId, scanDate),
            _ => []
        };
    }

    private async Task<List<DetectedObservation>> DetectVisibilityAsync(Guid organizationId, DateOnly scanDate)
    {
        var history = await _visibility.GetRecentSummaryHistoryAsync(organizationId, 8);
        var pair = Pair(history, scanDate, item => item.ScanDate);
        if (pair == null) return [];

        var observations = new List<DetectedObservation>();
        AddMetric(observations, AgentScanTypes.Visibility, "composite-score", "Overall visibility score",
            pair.Value.Previous.CompositeScore, pair.Value.Current.CompositeScore, 5,
            AgentMetricDirection.LowerIsBad, pair.Value.Previous.ScanDate, scanDate,
            "visibility", "overall", "/dashboard/visibility-radar",
            history.Where(item => item.ScanDate < scanDate).Select(item => (decimal)item.CompositeScore).ToList());

        var platformHistory = await _visibility.GetRecentPlatformHistoryAsync(organizationId, 8);
        var previousDate = pair.Value.Previous.ScanDate;
        var previousByPlatform = platformHistory.Where(item => item.ScanDate == previousDate)
            .GroupBy(item => item.Platform, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var current in platformHistory.Where(item => item.ScanDate == scanDate))
        {
            if (!previousByPlatform.TryGetValue(current.Platform, out var previous)) continue;
            AddMetric(observations, AgentScanTypes.Visibility, $"platform:{Slug(current.Platform)}", $"{current.Platform} visibility",
                previous.Score, current.Score, 8, AgentMetricDirection.LowerIsBad,
                previousDate, scanDate, "platform", current.Platform, "/dashboard/visibility-radar",
                platformHistory.Where(item => item.ScanDate < scanDate && string.Equals(item.Platform, current.Platform, StringComparison.OrdinalIgnoreCase))
                    .Select(item => (decimal)item.Score).ToList());
        }
        return observations;
    }

    private async Task<List<DetectedObservation>> DetectCitationsAsync(Guid organizationId, DateOnly scanDate)
    {
        var history = await _citations.GetRecentSummaryHistoryAsync(organizationId, 8);
        var pair = Pair(history, scanDate, item => item.ScanDate);
        if (pair == null) return [];

        var observations = new List<DetectedObservation>();
        AddMetric(observations, AgentScanTypes.Citations, "quality-score", "Citation quality score",
            pair.Value.Previous.CompositeQualityScore, pair.Value.Current.CompositeQualityScore, 5,
            AgentMetricDirection.LowerIsBad, pair.Value.Previous.ScanDate, scanDate,
            "citations", "quality", "/dashboard/citation-intelligence",
            history.Where(item => item.ScanDate < scanDate).Select(item => (decimal)item.CompositeQualityScore).ToList());
        AddMetric(observations, AgentScanTypes.Citations, "citation-signal", "Citation frequency signal",
            pair.Value.Previous.CitationSignal, pair.Value.Current.CitationSignal, 5,
            AgentMetricDirection.LowerIsBad, pair.Value.Previous.ScanDate, scanDate,
            "citations", "frequency", "/dashboard/citation-intelligence",
            history.Where(item => item.ScanDate < scanDate).Select(item => (decimal)item.CitationSignal).ToList());
        AddMetric(observations, AgentScanTypes.Citations, "models-referencing", "Models referencing the brand",
            pair.Value.Previous.ModelsReferencingCount, pair.Value.Current.ModelsReferencingCount, 1,
            AgentMetricDirection.LowerIsBad, pair.Value.Previous.ScanDate, scanDate,
            "citations", "model-coverage", "/dashboard/citation-intelligence",
            history.Where(item => item.ScanDate < scanDate).Select(item => (decimal)item.ModelsReferencingCount).ToList());
        return observations;
    }

    private async Task<List<DetectedObservation>> DetectCompetitorsAsync(Guid organizationId, DateOnly scanDate)
    {
        var history = await _competitors.GetRecentHistoryAsync(organizationId, 2);
        var dates = history.Select(item => item.ScanDate).Distinct().OrderBy(date => date).ToList();
        if (dates.Count < 2 || dates[^1] != scanDate) return [];

        var previousDate = dates[^2];
        var currentRows = history.Where(item => item.ScanDate == scanDate).ToList();
        var previousRows = history.Where(item => item.ScanDate == previousDate).ToList();
        var observations = new List<DetectedObservation>();
        var currentBrand = currentRows.FirstOrDefault(item => item.IsYou);
        var previousBrand = previousRows.FirstOrDefault(item => item.IsYou);
        if (currentBrand != null && previousBrand != null)
        {
            AddMetric(observations, AgentScanTypes.Competitors, "brand-share-of-voice", "Brand share of voice",
                previousBrand.ShareOfVoice, currentBrand.ShareOfVoice, 5,
                AgentMetricDirection.LowerIsBad, previousDate, scanDate,
                "competitor-panel", currentBrand.Name, "/dashboard/competitor-watch");
            AddMetric(observations, AgentScanTypes.Competitors, "brand-visibility", "Competitive visibility score",
                previousBrand.Visibility, currentBrand.Visibility, 8,
                AgentMetricDirection.LowerIsBad, previousDate, scanDate,
                "competitor-panel", currentBrand.Name, "/dashboard/competitor-watch");
        }

        var previousByKey = previousRows.Where(item => !item.IsYou)
            .GroupBy(CompetitorKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var current in currentRows.Where(item => !item.IsYou && item.Threat == "high"))
        {
            previousByKey.TryGetValue(CompetitorKey(current), out var previous);
            if (previous == null) continue;
            if (previous.Threat == "high" && current.ShareOfVoice - previous.ShareOfVoice < 5) continue;

            AddMetric(observations, AgentScanTypes.Competitors, $"high-threat:{Slug(current.Name)}", $"{current.Name} competitive threat",
                previous.ShareOfVoice, current.ShareOfVoice, 5, AgentMetricDirection.HigherIsBad,
                previousDate, scanDate, "competitor", current.Name, "/dashboard/competitor-watch");
        }
        return observations;
    }

    private async Task<List<DetectedObservation>> DetectBrandPulseAsync(Guid organizationId, DateOnly scanDate)
    {
        var history = await _brandPulse.GetRecentSummaryHistoryAsync(organizationId, 8);
        var pair = Pair(history, scanDate, item => item.ScanDate);
        if (pair == null) return [];

        var observations = new List<DetectedObservation>();
        AddMetric(observations, AgentScanTypes.BrandPulse, "brand-health", "Brand health",
            pair.Value.Previous.BrandHealth, pair.Value.Current.BrandHealth, 5,
            AgentMetricDirection.LowerIsBad, pair.Value.Previous.ScanDate, scanDate,
            "brand", "health", "/dashboard/brand-pulse",
            history.Where(item => item.ScanDate < scanDate).Select(item => (decimal)item.BrandHealth).ToList());
        AddMetric(observations, AgentScanTypes.BrandPulse, "ai-confidence", "AI confidence",
            pair.Value.Previous.AiConfidence, pair.Value.Current.AiConfidence, 8,
            AgentMetricDirection.LowerIsBad, pair.Value.Previous.ScanDate, scanDate,
            "brand", "ai-confidence", "/dashboard/brand-pulse",
            history.Where(item => item.ScanDate < scanDate).Select(item => (decimal)item.AiConfidence).ToList());
        AddMetric(observations, AgentScanTypes.BrandPulse, "negative-sentiment", "Negative sentiment",
            pair.Value.Previous.SentimentNegative, pair.Value.Current.SentimentNegative, 5,
            AgentMetricDirection.HigherIsBad, pair.Value.Previous.ScanDate, scanDate,
            "brand", "sentiment", "/dashboard/brand-pulse",
            history.Where(item => item.ScanDate < scanDate).Select(item => (decimal)item.SentimentNegative).ToList());
        return observations;
    }

    private async Task<AgentFinding> PersistFindingAsync(
        AgentRun run,
        string scanType,
        DateOnly scanDate,
        DetectedObservation observation,
        CancellationToken ct)
    {
        var change = observation.Change;
        var direction = change.Delta > 0 ? "increased" : "decreased";
        var evidence = new
        {
            source = $"{scanType}-snapshot",
            metric = change.MetricKey,
            previous = new { observedAt = observation.PreviousDate, value = change.PreviousValue },
            current = new { observedAt = observation.CurrentDate, value = change.CurrentValue },
            change = new { absolute = change.Delta, threshold = change.Threshold, direction },
            deterministicRule = change.DetectionRule,
            ruleThreshold = change.Threshold,
            rollingBaseline = change.BaselineMean.HasValue
                ? new { mean = change.BaselineMean, standardDeviation = change.BaselineStandardDeviation }
                : null,
            actionUrl = observation.ActionUrl
        };

        return await _agents.UpsertFindingAsync(new AgentFinding
        {
            OrganizationId = run.OrganizationId,
            RunId = run.Id,
            AgentKey = CitationlyAgentKeys.VisibilityMonitor,
            FindingType = $"{scanType}.metric-change",
            Severity = change.Severity,
            Title = $"{change.Label} {direction} by {Math.Abs(change.Delta):0.##}",
            Summary = $"{change.Label} moved from {change.PreviousValue:0.##} to {change.CurrentValue:0.##} between {observation.PreviousDate:yyyy-MM-dd} and {observation.CurrentDate:yyyy-MM-dd}.",
            EntityType = observation.EntityType,
            EntityIdsJson = JsonSerializer.Serialize(new[] { observation.EntityKey }),
            EvidenceJson = JsonSerializer.Serialize(evidence),
            ObservationStartedAt = Utc(observation.PreviousDate),
            ObservationEndedAt = Utc(observation.CurrentDate),
            Confidence = 1m,
            DeduplicationKey = $"monitor:{scanType}:{scanDate:yyyy-MM-dd}:{change.MetricKey}",
            Status = "Open"
        }, ct);
    }

    private async Task CreateAlertAsync(Guid organizationId, AgentFinding finding, string actionUrl)
    {
        await _alerts.UpsertAlertAsync(new Alert
        {
            OrganizationId = organizationId,
            DedupKey = $"agent-finding:{finding.DeduplicationKey}",
            Type = finding.FindingType,
            Title = finding.Title,
            Message = finding.Summary,
            Severity = finding.Severity,
            Source = "Visibility Monitor",
            ActionUrl = actionUrl,
            EvidenceJson = finding.EvidenceJson
        });
    }

    private async Task RunAnalystAsync(
        Guid organizationId,
        string scanType,
        DateOnly scanDate,
        AgentRun parentRun,
        IReadOnlyList<AgentFinding> sourceFindings,
        CancellationToken ct)
    {
        if (!await CanRunAsync(organizationId, CitationlyAgentKeys.IntelligenceAnalyst, "finding.important", ct, requiresAi: true))
            return;

        var run = await _agents.CreateRunAsync(new AgentRun
        {
            OrganizationId = organizationId,
            AgentKey = CitationlyAgentKeys.IntelligenceAnalyst,
            ParentRunId = parentRun.Id,
            TriggerType = "Event",
            TriggerReference = "finding.important",
            Status = AgentRunStatuses.Running,
            IdempotencyKey = $"analyst:{scanType}:{scanDate:yyyy-MM-dd}",
            InputJson = JsonSerializer.Serialize(new { scanType, scanDate, sourceFindingIds = sourceFindings.Select(item => item.Id) }),
            StartedAt = DateTime.UtcNow
        }, ct);
        if (run.Status is AgentRunStatuses.Completed or AgentRunStatuses.Failed or AgentRunStatuses.Cancelled) return;

        try
        {
            await AddEventAsync(run, "run.started", AgentRunStatuses.Running,
                $"Explaining {sourceFindings.Count} important evidence-linked finding(s).", null, ct);

            var systemPrompt =
                "You are Citationly's Intelligence Analyst. Explain only the supplied measured changes. " +
                "Do not invent causes or claim causation. Separate evidence from hypotheses. Return ONLY JSON with: " +
                "headline (string), explanation (string), likelyCauses (array of strings explicitly framed as hypotheses), " +
                "recommendedNextStep (string), confidence (number 0-1).";
            var userPrompt = JsonSerializer.Serialize(new
            {
                scanType,
                observationDate = scanDate,
                findings = sourceFindings.Select(item => new
                {
                    item.Id,
                    item.Title,
                    item.Summary,
                    item.Severity,
                    evidence = JsonDocument.Parse(item.EvidenceJson).RootElement.Clone()
                })
            });

            var completion = await _ai.CompleteAsync(
                organizationId,
                "agent.intelligence_analyst.explain",
                userPrompt,
                systemPrompt,
                requireJson: true,
                preferredProviderKey: "openai",
                ct);

            run.Provider = completion.ProviderKey ?? completion.UpstreamProvider ?? string.Empty;
            run.Model = completion.ModelUsed ?? string.Empty;
            run.PromptTokens = Math.Max(0, completion.PromptTokens ?? 0);
            run.CompletionTokens = Math.Max(0, completion.CompletionTokens ?? 0);
            run.CostMicroUsd = completion.CostUsd.HasValue
                ? Math.Max(0, (long)decimal.Round(completion.CostUsd.Value * 1_000_000m))
                : 0;

            if (!completion.Success)
            {
                run.Status = AgentRunStatuses.Failed;
                run.ErrorCode = "analyst_completion_failed";
                run.ErrorMessage = SafeError(completion.ErrorMessage ?? "AI completion was unavailable.");
                run.CompletedAt = DateTime.UtcNow;
                await _agents.UpdateRunExecutionAsync(run, ct);
                await AddEventAsync(run, "run.failed", AgentRunStatuses.Failed,
                    "The analyst could not produce a grounded explanation.", new { run.ErrorCode }, ct);
                return;
            }

            var summary = ExtractJsonString(completion.Content, "explanation")
                ?? "The Intelligence Analyst produced an evidence-linked explanation for the detected change.";
            var finding = await _agents.UpsertFindingAsync(new AgentFinding
            {
                OrganizationId = organizationId,
                RunId = run.Id,
                AgentKey = CitationlyAgentKeys.IntelligenceAnalyst,
                FindingType = $"{scanType}.analysis",
                Severity = sourceFindings.Any(item => item.Severity == "Critical") ? "Critical" : "High",
                Title = ExtractJsonString(completion.Content, "headline") ?? $"{ScanLabel(scanType)} change analysis",
                Summary = summary,
                EntityType = "agent-findings",
                EntityIdsJson = JsonSerializer.Serialize(sourceFindings.Select(item => item.Id)),
                EvidenceJson = JsonSerializer.Serialize(new
                {
                    sourceFindingIds = sourceFindings.Select(item => item.Id),
                    sourceEvidence = sourceFindings.Select(item => JsonDocument.Parse(item.EvidenceJson).RootElement.Clone()),
                    analysis = ParseJsonEvidence(completion.Content),
                    completion.Citations,
                    provider = run.Provider,
                    model = run.Model,
                    generatedAt = DateTime.UtcNow
                }),
                ObservationStartedAt = sourceFindings.Min(item => item.ObservationStartedAt),
                ObservationEndedAt = sourceFindings.Max(item => item.ObservationEndedAt),
                Confidence = ExtractConfidence(completion.Content),
                DeduplicationKey = $"analyst:{scanType}:{scanDate:yyyy-MM-dd}",
                Status = "Explained"
            }, ct);

            run.Status = AgentRunStatuses.Completed;
            run.OutputJson = JsonSerializer.Serialize(new { findingId = finding.Id, sourceFindingIds = sourceFindings.Select(item => item.Id) });
            run.CompletedAt = DateTime.UtcNow;
            await _agents.UpdateRunExecutionAsync(run, ct);
            await AddEventAsync(run, "run.completed", AgentRunStatuses.Completed,
                "Created an evidence-linked explanation without taking external action.", new { finding.Id }, ct);

            try
            {
                await RunGeoStrategyAsync(organizationId, scanType, scanDate, run, finding, sourceFindings, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception strategyError)
            {
                _logger.LogError(strategyError,
                    "GEO Strategy failed for analyst finding {FindingId}; the completed analysis is preserved.",
                    finding.Id);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.Status = AgentRunStatuses.Cancelled;
            run.ErrorCode = "analyst_cancelled";
            run.ErrorMessage = "The evidence analysis was cancelled.";
            run.CompletedAt = DateTime.UtcNow;
            await _agents.UpdateRunExecutionAsync(run, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            if (run.Status != AgentRunStatuses.Completed)
            {
                run.Status = AgentRunStatuses.Failed;
                run.ErrorCode = "analyst_failed";
                run.ErrorMessage = SafeError(ex.Message);
                run.CompletedAt = DateTime.UtcNow;
                await _agents.UpdateRunExecutionAsync(run, CancellationToken.None);
                await AddEventAsync(run, "run.failed", AgentRunStatuses.Failed,
                    "The analyst could not complete its evidence-linked explanation.", new { run.ErrorCode }, CancellationToken.None);
            }
            throw;
        }
    }

    private async Task RunGeoStrategyAsync(
        Guid organizationId,
        string scanType,
        DateOnly scanDate,
        AgentRun parentRun,
        AgentFinding analystFinding,
        IReadOnlyList<AgentFinding> sourceFindings,
        CancellationToken ct)
    {
        if (!await CanRunAsync(organizationId, CitationlyAgentKeys.GeoStrategy, "finding.explained", ct))
            return;

        var run = await _agents.CreateRunAsync(new AgentRun
        {
            OrganizationId = organizationId,
            AgentKey = CitationlyAgentKeys.GeoStrategy,
            ParentRunId = parentRun.Id,
            TriggerType = "Event",
            TriggerReference = "finding.explained",
            Status = AgentRunStatuses.Running,
            IdempotencyKey = $"strategy:{analystFinding.Id:N}",
            InputJson = JsonSerializer.Serialize(new
            {
                scanType,
                scanDate,
                analystFindingId = analystFinding.Id,
                sourceFindingIds = sourceFindings.Select(item => item.Id)
            }),
            StartedAt = DateTime.UtcNow
        }, ct);
        if (run.Status is AgentRunStatuses.Completed or AgentRunStatuses.WaitingForApproval or AgentRunStatuses.Failed or AgentRunStatuses.Cancelled)
            return;

        try
        {
            await AddEventAsync(run, "run.started", AgentRunStatuses.Running,
                $"Prioritizing {sourceFindings.Count} explained finding(s) into typed GEO recommendations.",
                new { analystFindingId = analystFinding.Id }, ct);

            var strategyPreference = await _agents.GetStrategyPreferenceAsync(organizationId, ct);
            var existingRecommendations = (await _agents.GetRecommendationsAsync(organizationId, limit: 200, cancellationToken: ct))
                .ToDictionary(item => item.DeduplicationKey, StringComparer.Ordinal);
            var recommendations = new List<AgentRecommendation>();
            var pendingApprovalCount = 0;
            foreach (var source in sourceFindings)
            {
                var draft = GeoStrategyRecommendationBuilder.Build(source, strategyPreference.PrimaryGoal);
                if (existingRecommendations.TryGetValue(draft.DeduplicationKey, out var existing))
                {
                    recommendations.Add(existing);
                    continue;
                }

                var recommendation = await _agents.UpsertRecommendationAsync(new AgentRecommendation
                {
                    OrganizationId = organizationId,
                    RunId = run.Id,
                    FindingId = source.Id,
                    AgentKey = CitationlyAgentKeys.GeoStrategy,
                    RecommendationType = draft.RecommendationType,
                    Category = draft.Category,
                    Title = draft.Title,
                    Summary = draft.Summary,
                    Rationale = draft.Rationale,
                    TargetType = draft.TargetType,
                    TargetKey = draft.TargetKey,
                    EvidenceJson = JsonSerializer.Serialize(new
                    {
                        analystFindingId = analystFinding.Id,
                        sourceFindingId = source.Id,
                        sourceFindingType = source.FindingType,
                        source.Severity,
                        source.Confidence,
                        source.ObservationStartedAt,
                        source.ObservationEndedAt,
                        evidence = ParseJsonEvidence(source.EvidenceJson)
                    }),
                    ActionPlanJson = JsonSerializer.Serialize(draft.ActionPlan),
                    ValidationPlanJson = JsonSerializer.Serialize(draft.ValidationPlan),
                    ExpectedImpact = draft.ExpectedImpact,
                    ImpactScore = draft.ImpactScore,
                    EffortScore = draft.EffortScore,
                    UrgencyScore = draft.UrgencyScore,
                    GoalAlignmentScore = draft.GoalAlignmentScore,
                    Confidence = draft.Confidence,
                    PriorityScore = draft.PriorityScore,
                    Status = AgentRecommendationStatuses.AwaitingApproval,
                    DeduplicationKey = draft.DeduplicationKey
                }, ct);
                recommendations.Add(recommendation);
                existingRecommendations[draft.DeduplicationKey] = recommendation;

                if (recommendation.Status != AgentRecommendationStatuses.AwaitingApproval)
                    continue;

                if (recommendation.ApprovalId.HasValue)
                    continue;

                pendingApprovalCount++;

                var approval = await _agents.CreateApprovalAsync(new AgentApproval
                {
                    OrganizationId = organizationId,
                    RunId = run.Id,
                    FindingId = source.Id,
                    AgentKey = CitationlyAgentKeys.GeoStrategy,
                    ActionType = "recommendation.approve",
                    Title = $"Approve recommendation: {recommendation.Title}",
                    Description = $"Priority {recommendation.PriorityScore:0.##}/100. {recommendation.Summary}",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        recommendationId = recommendation.Id,
                        recommendation.RecommendationType,
                        recommendation.TargetType,
                        recommendation.TargetKey,
                        recommendation.PriorityScore,
                        recommendation.ExpectedImpact
                    }),
                    RiskLevel = recommendation.PriorityScore >= 85 ? "Medium" : "Low",
                    IdempotencyKey = $"recommendation-approval:{recommendation.Id:N}:{source.Id:N}",
                    ExpiresAt = DateTime.UtcNow.AddDays(14)
                }, ct);
                recommendation = await _agents.LinkRecommendationApprovalAsync(
                    organizationId, recommendation.Id, approval.Id, ct) ?? recommendation;
            }

            run.Status = pendingApprovalCount > 0
                ? AgentRunStatuses.WaitingForApproval
                : AgentRunStatuses.Completed;
            run.OutputJson = JsonSerializer.Serialize(new
            {
                recommendationCount = recommendations.Count,
                pendingApprovalCount,
                primaryGoal = strategyPreference.PrimaryGoal,
                recommendations = recommendations.OrderByDescending(item => item.PriorityScore)
                    .Select(item => new { item.Id, item.Title, item.PriorityScore, item.Status })
            });
            run.CompletedAt = run.Status == AgentRunStatuses.Completed ? DateTime.UtcNow : null;
            await _agents.UpdateRunExecutionAsync(run, ct);
            await AddEventAsync(run, "recommendations.proposed", run.Status,
                $"Created or refreshed {recommendations.Count} deduplicated recommendation(s); {pendingApprovalCount} await approval.",
                new { recommendationIds = recommendations.Select(item => item.Id) }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.Status = AgentRunStatuses.Cancelled;
            run.ErrorCode = "strategy_cancelled";
            run.ErrorMessage = "Recommendation strategy generation was cancelled.";
            run.CompletedAt = DateTime.UtcNow;
            await _agents.UpdateRunExecutionAsync(run, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            if (run.Status is not AgentRunStatuses.Completed and not AgentRunStatuses.WaitingForApproval)
            {
                run.Status = AgentRunStatuses.Failed;
                run.ErrorCode = "strategy_failed";
                run.ErrorMessage = SafeError(ex.Message);
                run.CompletedAt = DateTime.UtcNow;
                await _agents.UpdateRunExecutionAsync(run, CancellationToken.None);
                await AddEventAsync(run, "run.failed", AgentRunStatuses.Failed,
                    "The GEO Strategy Agent could not persist its recommendations.",
                    new { run.ErrorCode }, CancellationToken.None);
            }
            throw;
        }
    }

    private async Task AddEventAsync(AgentRun run, string eventType, string status, string message, object? data, CancellationToken ct)
    {
        await _agents.AppendRunEventAsync(new AgentRunEvent
        {
            OrganizationId = run.OrganizationId,
            RunId = run.Id,
            EventType = eventType,
            Status = status,
            Message = message,
            DataJson = JsonSerializer.Serialize(data ?? new { })
        }, ct);
    }

    private static void AddMetric(
        ICollection<DetectedObservation> target,
        string scanType,
        string metricKey,
        string label,
        decimal previous,
        decimal current,
        decimal threshold,
        AgentMetricDirection direction,
        DateOnly previousDate,
        DateOnly currentDate,
        string entityType,
        string entityKey,
        string actionUrl,
        IReadOnlyCollection<decimal>? rollingBaseline = null)
    {
        var change = AgentChangeDetector.Evaluate(metricKey, label, previous, current, threshold, direction)
            ?? (rollingBaseline == null
                ? null
                : AgentChangeDetector.EvaluateRollingAnomaly(
                    metricKey, label, previous, current, rollingBaseline, direction));
        if (change != null)
            target.Add(new DetectedObservation(scanType, change, previousDate, currentDate, entityType, entityKey, actionUrl));
    }

    private static (T Previous, T Current)? Pair<T>(IEnumerable<T> history, DateOnly scanDate, Func<T, DateOnly> date)
    {
        var ordered = history.OrderBy(date).ToList();
        if (ordered.Count < 2 || date(ordered[^1]) != scanDate) return null;
        return (ordered[^2], ordered[^1]);
    }

    private static string CompetitorKey(CompetitorSnapshot item) =>
        item.CompetitorId?.ToString("N") ?? item.Name.Trim().ToLowerInvariant();

    private static bool IsImportant(AgentFinding finding) => finding.Severity is "High" or "Critical";

    private static bool KnownScanType(string scanType) => scanType is
        AgentScanTypes.Visibility or AgentScanTypes.Citations or AgentScanTypes.Competitors or AgentScanTypes.BrandPulse;

    private static DateTime Utc(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    private static string Slug(string value) =>
        string.Join('-', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string ScanLabel(string scanType) => scanType switch
    {
        AgentScanTypes.BrandPulse => "Brand pulse",
        AgentScanTypes.Citations => "Citation",
        AgentScanTypes.Competitors => "Competitor",
        _ => "Visibility"
    };

    private static string SafeError(string value) => value.Length <= 1000 ? value : value[..1000];

    private static int CountContentWords(string value) =>
        value.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static string SerializePolicyChecks(IEnumerable<ContentPolicyCheck> checks) =>
        JsonSerializer.Serialize(checks.Select(check => new
        {
            key = check.Key,
            label = check.Label,
            status = check.Status,
            message = check.Message,
            blocking = check.Blocking
        }));

    private static bool HasBlockingPolicyFailure(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array &&
                   document.RootElement.EnumerateArray().Any(item =>
                       item.TryGetProperty("blocking", out var blocking) && blocking.GetBoolean() &&
                       item.TryGetProperty("status", out var status) &&
                       status.GetString() == "Failed");
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static string? ExtractJsonString(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal ExtractConfidence(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("confidence", out var value) && value.TryGetDecimal(out var confidence))
                return Math.Clamp(confidence, 0m, 1m);
        }
        catch (JsonException)
        {
            // Completion JSON validation is handled by the provider; use a conservative fallback.
        }
        return 0.7m;
    }

    private static JsonElement ParseJsonEvidence(string json)
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

    private sealed record DetectedObservation(
        string ScanType,
        AgentMetricChange Change,
        DateOnly PreviousDate,
        DateOnly CurrentDate,
        string EntityType,
        string EntityKey,
        string ActionUrl);
}
