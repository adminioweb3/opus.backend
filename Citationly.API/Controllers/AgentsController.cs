using System.Text.Json;
using System.Text.RegularExpressions;
using Citationly.API.Services;
using Citationly.Application.Features.Assistant.Agents;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/agents")]
public partial class AgentsController : ControllerBase
{
    private static readonly IReadOnlySet<string> TriggerTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Event", "Cron", "Manual"
    };

    private readonly ICurrentOrganizationAccessor _currentOrganization;
    private readonly IAgentControlPlaneRepository _agents;
    private readonly IEntitlementService _entitlements;
    private readonly ITeamRepository _team;
    private readonly IAgentAutomationService _automation;
    private readonly IContentDraftRepository _contentDrafts;

    public AgentsController(
        ICurrentOrganizationAccessor currentOrganization,
        IAgentControlPlaneRepository agents,
        IEntitlementService entitlements,
        ITeamRepository team,
        IAgentAutomationService automation,
        IContentDraftRepository contentDrafts)
    {
        _currentOrganization = currentOrganization;
        _agents = agents;
        _entitlements = entitlements;
        _team = team;
        _automation = automation;
        _contentDrafts = contentDrafts;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken)
    {
        var caller = await _currentOrganization.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();

        await _agents.EnsureDefaultsAsync(caller.Value.OrganizationId, cancellationToken);
        var agents = await _agents.GetOverviewAsync(caller.Value.OrganizationId, cancellationToken);
        var autonomyLevels = agents.Select(agent => agent.AutonomyLevel).Distinct(StringComparer.Ordinal).ToArray();

        return Ok(new
        {
            autonomyLevel = autonomyLevels.Length == 1 ? autonomyLevels[0] : "Mixed",
            totals = new
            {
                activeRuns = agents.Sum(agent => agent.ActiveRunCount),
                openFindings = agents.Sum(agent => agent.OpenFindingCount),
                pendingApprovals = agents.Sum(agent => agent.PendingApprovalCount),
                failedAgents = agents.Count(agent => agent.Status == AgentRunStatuses.Failed)
            },
            agents = agents.Select(agent => new
            {
                agent.AgentKey,
                agent.Name,
                agent.Stage,
                agent.Description,
                agent.Version,
                capabilities = ParseJsonArray(agent.CapabilitiesJson),
                agent.IsEnabled,
                agent.AutonomyLevel,
                agent.MaxRunsPerDay,
                agent.MaxCostMicroUsdPerRun,
                allowedActions = ParseJsonArray(agent.AllowedActionsJson),
                agent.Status,
                agent.ActiveRunCount,
                agent.OpenFindingCount,
                agent.PendingApprovalCount,
                agent.LastRunStatus,
                agent.LastRunAt,
                agent.NextRunAt
            })
        });
    }

    [HttpGet("activity")]
    public async Task<IActionResult> GetActivity([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized();

        var activity = await _agents.GetActivityAsync(orgId.Value, limit, cancellationToken);
        return Ok(activity.Select(item => new
        {
            item.Id,
            item.Kind,
            item.AgentKey,
            item.Title,
            item.Message,
            item.Severity,
            item.Status,
            item.RunId,
            item.ReferenceId,
            data = ParseJsonObjectOrArray(item.DataJson),
            item.OccurredAt
        }));
    }

    [HttpGet("approvals")]
    public async Task<IActionResult> GetApprovals([FromQuery] string? status = null, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized();

        var approvals = await _agents.GetApprovalsAsync(orgId.Value, status, limit, cancellationToken);
        return Ok(approvals.Select(ToApprovalResponse));
    }

    [HttpPost("approvals/{id:guid}/decision")]
    [RequireOrgRole("Manager")]
    [AuditAction("agent.approval.decide", "Agents", "AgentApproval")]
    public async Task<IActionResult> DecideApproval(Guid id, [FromBody] DecideAgentApprovalRequest request, CancellationToken cancellationToken)
    {
        var caller = await _currentOrganization.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();

        var decision = request.Decision?.Trim() ?? string.Empty;
        if (!AgentControlPlanePolicy.IsValidApprovalDecision(decision))
            return BadRequest(new { message = "Decision must be Approved or Rejected." });
        if ((request.Note?.Length ?? 0) > 1_000)
            return BadRequest(new { message = "Decision note cannot exceed 1,000 characters." });

        var approval = await _agents.DecideApprovalAsync(
            caller.Value.OrganizationId,
            id,
            caller.Value.UserId,
            decision,
            request.Note?.Trim() ?? string.Empty,
            cancellationToken);

        if (approval == null)
            return Conflict(new { message = "This approval is missing, expired, or already decided." });

        if (decision == AgentApprovalStatuses.Approved &&
            approval.ActionType == "recommendation.approve" &&
            TryGetPayloadGuid(approval.PayloadJson, "recommendationId", out var recommendationId))
        {
            var recommendation = await _agents.GetRecommendationAsync(
                caller.Value.OrganizationId, recommendationId, cancellationToken);
            if (recommendation?.ApprovalId == approval.Id)
            {
                await _automation.ProcessApprovedRecommendationAsync(
                    caller.Value.OrganizationId, recommendationId, caller.Value.UserId, cancellationToken);
            }
        }
        else if (decision == AgentApprovalStatuses.Approved &&
                 approval.ActionType == "content.publish" &&
                 TryGetPayloadGuid(approval.PayloadJson, "executionId", out var executionId))
        {
            await _automation.ProcessApprovedContentPublishAsync(
                caller.Value.OrganizationId, executionId, approval.Id, cancellationToken);
        }

        return Ok(ToApprovalResponse(approval));
    }

    [HttpGet("schedules")]
    public async Task<IActionResult> GetSchedules(CancellationToken cancellationToken)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized();

        await _agents.EnsureDefaultsAsync(orgId.Value, cancellationToken);
        return Ok(await _agents.GetSchedulesAsync(orgId.Value, cancellationToken));
    }

    [HttpPut("schedules/{agentKey}")]
    [RequireOrgRole("Manager")]
    [AuditAction("agent.schedule.update", "Agents", "AgentSchedule")]
    public async Task<IActionResult> UpsertSchedule(string agentKey, [FromBody] UpdateAgentScheduleRequest request, CancellationToken cancellationToken)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized();
        if (!AgentControlPlanePolicy.IsKnownAgent(agentKey)) return NotFound(new { message = "Unknown agent." });

        var triggerType = request.TriggerType?.Trim() ?? string.Empty;
        var triggerExpression = request.TriggerExpression?.Trim() ?? string.Empty;
        if (!TriggerTypes.Contains(triggerType))
            return BadRequest(new { message = "Trigger type must be Event, Cron, or Manual." });
        if (!IsValidTriggerExpression(triggerType, triggerExpression))
            return BadRequest(new { message = "Trigger expression is invalid for the selected trigger type." });
        if (string.IsNullOrWhiteSpace(request.TimeZone) || request.TimeZone.Length > 100)
            return BadRequest(new { message = "A valid time zone identifier is required." });

        await _agents.EnsureDefaultsAsync(orgId.Value, cancellationToken);
        var schedule = await _agents.UpsertScheduleAsync(new AgentSchedule
        {
            OrganizationId = orgId.Value,
            AgentKey = agentKey,
            TriggerType = triggerType,
            TriggerExpression = triggerExpression,
            TimeZone = request.TimeZone.Trim(),
            IsEnabled = request.IsEnabled,
            NextRunAt = request.NextRunAt
        }, cancellationToken);

        return schedule == null ? NotFound() : Ok(schedule);
    }

    [HttpPut("settings/{agentKey}")]
    [RequireOrgRole("Manager")]
    [AuditAction("agent.settings.update", "Agents", "AgentSetting")]
    public async Task<IActionResult> UpdateSettings(string agentKey, [FromBody] UpdateAgentSettingsRequest request, CancellationToken cancellationToken)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized();
        if (!AgentControlPlanePolicy.IsKnownAgent(agentKey)) return NotFound(new { message = "Unknown agent." });

        var autonomyLevel = request.AutonomyLevel?.Trim() ?? string.Empty;
        if (!AgentControlPlanePolicy.IsValidAutonomyLevel(autonomyLevel))
            return BadRequest(new { message = "Autonomy level must be Observe, Assist, or Autopilot." });
        if (autonomyLevel == AgentAutonomyLevels.Autopilot
            && !await _entitlements.CanUseFeatureAsync(orgId.Value, "agent_autopilot", cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Autopilot is not available on this plan." });

        var runLimit = await _entitlements.GetPlanLimitValueAsync(orgId.Value, "agent_runs_per_day", cancellationToken);
        if (request.MaxRunsPerDay < 0 || (runLimit.HasValue && request.MaxRunsPerDay > runLimit.Value))
            return BadRequest(new { message = $"Max runs per day must be between 0 and {runLimit ?? 10_000}." });

        var costLimit = await _entitlements.GetPlanLimitValueAsync(orgId.Value, "agent_cost_micro_usd_per_run", cancellationToken);
        if (request.MaxCostMicroUsdPerRun < 0 || (costLimit.HasValue && request.MaxCostMicroUsdPerRun > costLimit.Value))
            return BadRequest(new { message = $"Per-run budget cannot exceed {costLimit ?? long.MaxValue} micro-USD for this plan." });

        await _agents.EnsureDefaultsAsync(orgId.Value, cancellationToken);
        var overview = await _agents.GetOverviewAsync(orgId.Value, cancellationToken);
        var definition = overview.SingleOrDefault(agent => agent.AgentKey == agentKey);
        if (definition == null) return NotFound(new { message = "Unknown agent." });

        var capabilities = ParseJsonArray(definition.CapabilitiesJson)
            .EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.String)
            .Select(element => element.GetString() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        var allowedActions = (request.AllowedActions ?? Array.Empty<string>())
            .Select(action => action.Trim())
            .Where(action => action.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unknownActions = allowedActions.Where(action => !capabilities.Contains(action)).ToArray();
        if (unknownActions.Length > 0)
            return BadRequest(new { message = "One or more allowed actions are not supported by this agent.", unknownActions });

        var setting = await _agents.UpdateSettingAsync(new AgentSetting
        {
            OrganizationId = orgId.Value,
            AgentKey = agentKey,
            IsEnabled = request.IsEnabled,
            AutonomyLevel = autonomyLevel,
            MaxRunsPerDay = request.MaxRunsPerDay,
            MaxCostMicroUsdPerRun = request.MaxCostMicroUsdPerRun,
            AllowedActionsJson = JsonSerializer.Serialize(allowedActions)
        }, cancellationToken);

        return setting == null ? NotFound() : Ok(new
        {
            setting.Id,
            setting.AgentKey,
            setting.IsEnabled,
            setting.AutonomyLevel,
            setting.MaxRunsPerDay,
            setting.MaxCostMicroUsdPerRun,
            allowedActions,
            setting.UpdatedAt
        });
    }

    [HttpGet("runs")]
    public async Task<IActionResult> GetRuns([FromQuery] string? agentKey = null, [FromQuery] string? status = null, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized();
        if (!string.IsNullOrWhiteSpace(agentKey) && !AgentControlPlanePolicy.IsKnownAgent(agentKey))
            return BadRequest(new { message = "Unknown agent." });

        var runs = await _agents.GetRunsAsync(orgId.Value, agentKey, status, limit, cancellationToken);
        return Ok(runs.Select(run => new
        {
            run.Id,
            run.AgentKey,
            run.ParentRunId,
            run.TriggerType,
            run.TriggerReference,
            run.Status,
            run.Provider,
            run.Model,
            run.PromptTokens,
            run.CompletionTokens,
            run.CostMicroUsd,
            run.Attempt,
            run.MaxAttempts,
            run.ErrorCode,
            run.ErrorMessage,
            run.QueuedAt,
            run.StartedAt,
            run.CompletedAt,
            run.CancelRequestedAt,
            run.UpdatedAt
        }));
    }

    [HttpGet("recommendations")]
    public async Task<IActionResult> GetRecommendations([FromQuery] string? status = null, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized();

        var recommendations = await _agents.GetRecommendationsAsync(orgId.Value, status, limit, cancellationToken);
        return Ok(recommendations.Select(ToRecommendationResponse));
    }

    [HttpGet("strategy-preference")]
    public async Task<IActionResult> GetStrategyPreference(CancellationToken cancellationToken)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized();
        return Ok(await _agents.GetStrategyPreferenceAsync(orgId.Value, cancellationToken));
    }

    [HttpGet("content-executions")]
    public async Task<IActionResult> GetContentExecutions([FromQuery] string? status = null, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized();

        var executions = await _agents.GetContentExecutionsAsync(orgId.Value, status, limit, cancellationToken);
        var responses = new List<object>(executions.Count);
        foreach (var execution in executions)
        {
            var draft = execution.ContentDraftId.HasValue
                ? await _contentDrafts.GetByIdAsync(execution.ContentDraftId.Value)
                : null;
            responses.Add(ToContentExecutionResponse(execution, draft));
        }
        return Ok(responses);
    }

    [HttpGet("impact-measurements")]
    public async Task<IActionResult> GetImpactMeasurements([FromQuery] string? status = null, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized();

        var measurements = await _agents.GetImpactMeasurementsAsync(orgId.Value, status, limit, cancellationToken);
        return Ok(measurements.Select(ToImpactMeasurementResponse));
    }

    [HttpPost("impact-measurements/process-due")]
    [RequireOrgRole("Manager")]
    [AuditAction("agent.impact.process_due", "Agents", "AgentImpactMeasurement")]
    public async Task<IActionResult> ProcessDueImpactMeasurements(CancellationToken cancellationToken)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized();
        var measured = await _automation.ProcessDueImpactMeasurementsAsync(orgId.Value, cancellationToken);
        return Ok(new { measured });
    }

    [HttpPost("content-executions/{id:guid}/request-publish")]
    [RequireOrgRole("Manager")]
    [AuditAction("agent.content.publish_request", "Agents", "AgentContentExecution")]
    public async Task<IActionResult> RequestContentPublish(Guid id, CancellationToken cancellationToken)
    {
        var caller = await _currentOrganization.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();

        var execution = await _automation.RequestContentPublishApprovalAsync(
            caller.Value.OrganizationId, id, caller.Value.UserId, cancellationToken);
        return execution == null
            ? Conflict(new { message = "This draft is not ready for publishing approval or has blocking policy checks." })
            : Ok(ToContentExecutionResponse(
                execution,
                execution.ContentDraftId.HasValue
                    ? await _contentDrafts.GetByIdAsync(execution.ContentDraftId.Value)
                    : null));
    }

    [HttpPut("strategy-preference")]
    [RequireOrgRole("Manager")]
    [AuditAction("agent.strategy_preference.update", "Agents", "AgentStrategyPreference")]
    public async Task<IActionResult> UpdateStrategyPreference([FromBody] UpdateAgentStrategyPreferenceRequest request, CancellationToken cancellationToken)
    {
        var orgId = await _currentOrganization.GetOrganizationIdAsync(User, cancellationToken);
        if (orgId == null) return Unauthorized();

        var primaryGoal = request.PrimaryGoal?.Trim() ?? string.Empty;
        if (!AgentStrategyGoals.All.Contains(primaryGoal))
            return BadRequest(new { message = "Unknown strategy goal." });

        var preference = await _agents.UpsertStrategyPreferenceAsync(new AgentStrategyPreference
        {
            OrganizationId = orgId.Value,
            PrimaryGoal = primaryGoal
        }, cancellationToken);
        return Ok(preference);
    }

    [HttpPut("recommendations/{id:guid}/assignment")]
    [RequireOrgRole("Manager")]
    [AuditAction("agent.recommendation.assign", "Agents", "AgentRecommendation")]
    public async Task<IActionResult> AssignRecommendation(Guid id, [FromBody] AssignAgentRecommendationRequest request, CancellationToken cancellationToken)
    {
        var caller = await _currentOrganization.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();

        var current = await _agents.GetRecommendationAsync(caller.Value.OrganizationId, id, cancellationToken);
        if (current == null) return NotFound();
        if (!AgentControlPlanePolicy.CanAssignRecommendation(current.Status))
            return Conflict(new { message = "Only approved recommendations can be assigned." });

        if (request.AssignedToUserId.HasValue)
        {
            var members = await _team.GetMembersByOrgAsync(caller.Value.OrganizationId);
            if (members.All(member => member.Id != request.AssignedToUserId.Value))
                return BadRequest(new { message = "The assignee must be an active member of this workspace." });
        }

        var updated = await _agents.AssignRecommendationAsync(
            caller.Value.OrganizationId, id, request.AssignedToUserId, cancellationToken);
        return updated == null ? Conflict(new { message = "Recommendation assignment could not be updated." }) : Ok(ToRecommendationResponse(updated));
    }

    [HttpPut("recommendations/{id:guid}/status")]
    [RequireOrgRole("Manager")]
    [AuditAction("agent.recommendation.status_update", "Agents", "AgentRecommendation")]
    public async Task<IActionResult> UpdateRecommendationStatus(Guid id, [FromBody] UpdateAgentRecommendationStatusRequest request, CancellationToken cancellationToken)
    {
        var caller = await _currentOrganization.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();

        var current = await _agents.GetRecommendationAsync(caller.Value.OrganizationId, id, cancellationToken);
        if (current == null) return NotFound();

        var nextStatus = request.Status?.Trim() ?? string.Empty;
        if (!AgentControlPlanePolicy.CanTransitionRecommendation(current.Status, nextStatus))
            return Conflict(new { message = $"Recommendation cannot move from {current.Status} to {nextStatus}." });
        if ((request.Note?.Length ?? 0) > 1_000)
            return BadRequest(new { message = "Status note cannot exceed 1,000 characters." });

        var updated = await _agents.UpdateRecommendationStatusAsync(
            caller.Value.OrganizationId, id, nextStatus, request.Note?.Trim() ?? string.Empty, cancellationToken);
        if (updated == null)
            return Conflict(new { message = "Recommendation status could not be updated." });
        if (nextStatus == AgentRecommendationStatuses.Implemented)
        {
            await _automation.ScheduleRecommendationImpactAsync(
                caller.Value.OrganizationId, updated.Id, caller.Value.UserId, 14, cancellationToken);
        }
        return Ok(ToRecommendationResponse(updated));
    }

    [HttpPost("runs/{id:guid}/cancel")]
    [RequireOrgRole("Manager")]
    [AuditAction("agent.run.cancel", "Agents", "AgentRun")]
    public async Task<IActionResult> CancelRun(Guid id, CancellationToken cancellationToken)
    {
        var caller = await _currentOrganization.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();

        var cancelled = await _agents.CancelRunAsync(caller.Value.OrganizationId, id, caller.Value.UserId, cancellationToken);
        return cancelled ? Ok(new { cancelled = true }) : Conflict(new { message = "Run is missing or cannot be cancelled in its current state." });
    }

    [HttpPost("runs/{id:guid}/retry")]
    [RequireOrgRole("Manager")]
    [AuditAction("agent.run.retry", "Agents", "AgentRun")]
    public async Task<IActionResult> RetryRun(Guid id, CancellationToken cancellationToken)
    {
        var caller = await _currentOrganization.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();

        var previous = await _agents.GetRunAsync(caller.Value.OrganizationId, id, cancellationToken);
        if (previous == null) return NotFound();
        if (!AgentControlPlanePolicy.CanRetryRun(previous.Status, previous.Attempt, previous.MaxAttempts))
            return Conflict(new { message = "Run is not retryable or has reached its attempt limit." });

        await _agents.EnsureDefaultsAsync(caller.Value.OrganizationId, cancellationToken);
        var overview = await _agents.GetOverviewAsync(caller.Value.OrganizationId, cancellationToken);
        var setting = overview.Single(agent => agent.AgentKey == previous.AgentKey);
        var runsToday = await _agents.CountRunsSinceAsync(caller.Value.OrganizationId, previous.AgentKey, DateTime.UtcNow.Date, cancellationToken);
        if (runsToday >= setting.MaxRunsPerDay)
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "This agent has reached its configured daily run limit.", currentUsage = runsToday, limit = setting.MaxRunsPerDay });

        var quota = await _entitlements.CheckQuotaAsync(caller.Value.OrganizationId, "agent_runs_per_day", cancellationToken);
        if (!quota.IsWithinLimit)
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Daily agent-run limit reached.", quota.CurrentUsage, quota.Limit });

        var retry = await _agents.RetryRunAsync(caller.Value.OrganizationId, id, caller.Value.UserId, cancellationToken);
        return retry == null ? Conflict(new { message = "Run could not be retried." }) : Ok(retry);
    }

    private static object ToApprovalResponse(AgentApproval approval) => new
    {
        approval.Id,
        approval.RunId,
        approval.FindingId,
        approval.AgentKey,
        approval.ActionType,
        approval.Title,
        approval.Description,
        payload = ParseJsonObjectOrArray(approval.PayloadJson),
        approval.RiskLevel,
        approval.Status,
        approval.RequestedAt,
        approval.ExpiresAt,
        approval.DecidedByUserId,
        approval.DecidedAt,
        approval.DecisionNote,
        approval.ExecutedAt
    };

    private static object ToRecommendationResponse(AgentRecommendation recommendation) => new
    {
        recommendation.Id,
        recommendation.RunId,
        recommendation.FindingId,
        recommendation.ApprovalId,
        recommendation.AgentKey,
        recommendation.RecommendationType,
        recommendation.Category,
        recommendation.Title,
        recommendation.Summary,
        recommendation.Rationale,
        recommendation.TargetType,
        recommendation.TargetKey,
        evidence = ParseJsonObjectOrArray(recommendation.EvidenceJson),
        actionPlan = ParseJsonArray(recommendation.ActionPlanJson),
        validationPlan = ParseJsonObjectOrArray(recommendation.ValidationPlanJson),
        recommendation.ExpectedImpact,
        recommendation.ImpactScore,
        recommendation.EffortScore,
        recommendation.UrgencyScore,
        recommendation.GoalAlignmentScore,
        recommendation.Confidence,
        recommendation.PriorityScore,
        recommendation.Status,
        recommendation.AssignedToUserId,
        recommendation.AssignedToName,
        recommendation.RejectionReason,
        recommendation.CreatedAt,
        recommendation.UpdatedAt,
        recommendation.ApprovedAt,
        recommendation.AssignedAt,
        recommendation.ImplementedAt
    };

    private static object ToContentExecutionResponse(AgentContentExecution execution, ContentDraft? draft) => new
    {
        execution.Id,
        execution.RunId,
        execution.RecommendationId,
        execution.ContentDraftId,
        execution.KnowledgeBaseId,
        execution.PublishApprovalId,
        execution.Status,
        brief = ParseJsonObjectOrArray(execution.BriefJson),
        evidence = ParseJsonArray(execution.EvidenceJson),
        reviewDiff = ParseJsonObjectOrArray(execution.ReviewDiffJson),
        policyChecks = ParseJsonArray(execution.PolicyChecksJson),
        execution.ReviewNote,
        execution.ReviewedByUserId,
        execution.ReviewedAt,
        execution.PublishedAt,
        execution.CreatedAt,
        execution.UpdatedAt,
        draft = draft == null ? null : new
        {
            draft.Id,
            draft.Title,
            draft.ContentType,
            draft.WordCount,
            draft.Status,
            draft.PublishedUrl,
            draft.PublishedAt
        }
    };

    private static object ToImpactMeasurementResponse(AgentImpactMeasurement measurement) => new
    {
        measurement.Id,
        measurement.RecommendationId,
        measurement.BaselineRunId,
        measurement.MeasurementRunId,
        measurement.Status,
        measurement.Outcome,
        measurement.MonitoringWindowDays,
        measurement.BaselineCapturedAt,
        measurement.MeasurementDueAt,
        measurement.MeasuredAt,
        baseline = ParseJsonObjectOrArray(measurement.BaselineJson),
        followup = ParseJsonObjectOrArray(measurement.FollowupJson),
        delta = ParseJsonObjectOrArray(measurement.DeltaJson),
        evidence = ParseJsonObjectOrArray(measurement.EvidenceJson),
        report = ParseJsonObjectOrArray(measurement.ReportJson),
        measurement.Confidence,
        measurement.ErrorMessage,
        measurement.CreatedAt,
        measurement.UpdatedAt
    };

    private static JsonElement ParseJsonArray(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.Clone()
                : EmptyJsonArray();
        }
        catch (JsonException)
        {
            return EmptyJsonArray();
        }
    }

    private static JsonElement ParseJsonObjectOrArray(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? document.RootElement.Clone()
                : EmptyJsonObject();
        }
        catch (JsonException)
        {
            return EmptyJsonObject();
        }
    }

    private static JsonElement EmptyJsonArray()
    {
        using var document = JsonDocument.Parse("[]");
        return document.RootElement.Clone();
    }

    private static JsonElement EmptyJsonObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static bool TryGetPayloadGuid(string json, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.String &&
                   Guid.TryParse(property.GetString(), out value);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidTriggerExpression(string triggerType, string expression)
    {
        if (expression.Length is < 1 or > 255) return false;
        if (triggerType == "Manual") return expression == "manual";
        if (triggerType == "Event") return EventNameRegex().IsMatch(expression);
        return CronExpressionRegex().IsMatch(expression) && expression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 5;
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex EventNameRegex();

    [GeneratedRegex("^[0-9*/?,\\- ]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CronExpressionRegex();
}

public sealed class DecideAgentApprovalRequest
{
    public string Decision { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public sealed class UpdateAgentScheduleRequest
{
    public string TriggerType { get; set; } = "Event";
    public string TriggerExpression { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "UTC";
    public bool IsEnabled { get; set; } = true;
    public DateTime? NextRunAt { get; set; }
}

public sealed class UpdateAgentSettingsRequest
{
    public bool IsEnabled { get; set; } = true;
    public string AutonomyLevel { get; set; } = AgentAutonomyLevels.Assist;
    public int MaxRunsPerDay { get; set; } = 5;
    public long MaxCostMicroUsdPerRun { get; set; } = 100_000;
    public string[]? AllowedActions { get; set; }
}

public sealed class AssignAgentRecommendationRequest
{
    public Guid? AssignedToUserId { get; set; }
}

public sealed class UpdateAgentRecommendationStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public sealed class UpdateAgentStrategyPreferenceRequest
{
    public string PrimaryGoal { get; set; } = AgentStrategyGoals.Balanced;
}
