using Citationly.Application.Features.Assistant.Agents;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public sealed class AgentControlPlaneRepository : IAgentControlPlaneRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public AgentControlPlaneRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task EnsureDefaultsAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            INSERT INTO AgentSettings
                (OrganizationId, AgentKey, IsEnabled, AutonomyLevel, MaxRunsPerDay, MaxCostMicroUsdPerRun, AllowedActionsJson)
            SELECT @OrganizationId, AgentKey, TRUE, DefaultAutonomyLevel, 5, 100000, CapabilitiesJson
            FROM AgentDefinitions
            WHERE IsAvailable = TRUE
            ON CONFLICT (OrganizationId, AgentKey) DO NOTHING;

            INSERT INTO AgentSchedules
                (OrganizationId, AgentKey, TriggerType, TriggerExpression, TimeZone, IsEnabled)
            SELECT @OrganizationId, AgentKey, DefaultTriggerType, DefaultSchedule, 'UTC', TRUE
            FROM AgentDefinitions
            WHERE IsAvailable = TRUE AND DefaultSchedule <> ''
            ON CONFLICT (OrganizationId, AgentKey, TriggerType, TriggerExpression) DO NOTHING;

            INSERT INTO AgentSchedules
                (OrganizationId, AgentKey, TriggerType, TriggerExpression, TimeZone, IsEnabled)
            VALUES (@OrganizationId, 'geo-strategy', 'Event', 'finding.explained', 'UTC', TRUE)
            ON CONFLICT (OrganizationId, AgentKey, TriggerType, TriggerExpression) DO NOTHING;

            INSERT INTO AgentStrategyPreferences (OrganizationId, PrimaryGoal)
            VALUES (@OrganizationId, 'Balanced')
            ON CONFLICT (OrganizationId) DO NOTHING;
            """,
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task<IReadOnlyList<AgentOverviewItem>> GetOverviewAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT
                d.AgentKey,
                d.Name,
                d.Stage,
                d.Description,
                d.Version,
                d.CapabilitiesJson::text AS CapabilitiesJson,
                COALESCE(s.IsEnabled, FALSE) AS IsEnabled,
                COALESCE(s.AutonomyLevel, d.DefaultAutonomyLevel) AS AutonomyLevel,
                COALESCE(s.MaxRunsPerDay, 0) AS MaxRunsPerDay,
                COALESCE(s.MaxCostMicroUsdPerRun, 0) AS MaxCostMicroUsdPerRun,
                COALESCE(s.AllowedActionsJson, '[]'::jsonb)::text AS AllowedActionsJson,
                COALESCE(run_counts.ActiveRunCount, 0)::int AS ActiveRunCount,
                COALESCE(finding_counts.OpenFindingCount, 0)::int AS OpenFindingCount,
                COALESCE(approval_counts.PendingApprovalCount, 0)::int AS PendingApprovalCount,
                COALESCE(last_run.Status, '') AS LastRunStatus,
                last_run.ActivityAt AS LastRunAt,
                next_schedule.NextRunAt
            FROM AgentDefinitions d
            LEFT JOIN AgentSettings s
                ON s.AgentKey = d.AgentKey AND s.OrganizationId = @OrganizationId
            LEFT JOIN LATERAL (
                SELECT
                    COUNT(*) FILTER (WHERE Status IN ('Queued', 'Running', 'WaitingForApproval', 'CancellationRequested')) AS ActiveRunCount
                FROM AgentRuns
                WHERE OrganizationId = @OrganizationId AND AgentKey = d.AgentKey
            ) run_counts ON TRUE
            LEFT JOIN LATERAL (
                SELECT COUNT(*) AS OpenFindingCount
                FROM AgentFindings
                WHERE OrganizationId = @OrganizationId AND AgentKey = d.AgentKey AND Status IN ('Open', 'Investigating', 'Explained')
            ) finding_counts ON TRUE
            LEFT JOIN LATERAL (
                SELECT COUNT(*) AS PendingApprovalCount
                FROM AgentApprovals
                WHERE OrganizationId = @OrganizationId AND AgentKey = d.AgentKey AND Status = 'Pending'
                    AND (ExpiresAt IS NULL OR ExpiresAt > CURRENT_TIMESTAMP)
            ) approval_counts ON TRUE
            LEFT JOIN LATERAL (
                SELECT Status, COALESCE(CompletedAt, StartedAt, QueuedAt) AS ActivityAt
                FROM AgentRuns
                WHERE OrganizationId = @OrganizationId AND AgentKey = d.AgentKey
                ORDER BY QueuedAt DESC
                LIMIT 1
            ) last_run ON TRUE
            LEFT JOIN LATERAL (
                SELECT NextRunAt
                FROM AgentSchedules
                WHERE OrganizationId = @OrganizationId AND AgentKey = d.AgentKey AND IsEnabled = TRUE
                ORDER BY NextRunAt NULLS LAST, UpdatedAt DESC
                LIMIT 1
            ) next_schedule ON TRUE
            WHERE d.IsAvailable = TRUE
            ORDER BY d.SortOrder, d.Name
            """,
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken);

        var items = (await connection.QueryAsync<AgentOverviewItem>(command)).AsList();
        foreach (var item in items)
        {
            item.Status = ResolveOverviewStatus(item);
        }
        return items;
    }

    public async Task<IReadOnlyList<AgentActivityItem>> GetActivityAsync(Guid organizationId, int limit = 50, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT * FROM (
                SELECT
                    e.Id,
                    'RunEvent'::text AS Kind,
                    r.AgentKey,
                    e.EventType AS Title,
                    e.Message,
                    'Info'::text AS Severity,
                    e.Status,
                    e.RunId,
                    e.Id AS ReferenceId,
                    e.DataJson::text AS DataJson,
                    e.CreatedAt AS OccurredAt
                FROM AgentRunEvents e
                INNER JOIN AgentRuns r ON r.Id = e.RunId AND r.OrganizationId = e.OrganizationId
                WHERE e.OrganizationId = @OrganizationId

                UNION ALL

                SELECT
                    f.Id,
                    'Finding'::text AS Kind,
                    f.AgentKey,
                    f.Title,
                    f.Summary AS Message,
                    f.Severity,
                    f.Status,
                    f.RunId,
                    f.Id AS ReferenceId,
                    f.EvidenceJson::text AS DataJson,
                    f.UpdatedAt AS OccurredAt
                FROM AgentFindings f
                WHERE f.OrganizationId = @OrganizationId

                UNION ALL

                SELECT
                    a.Id,
                    'Approval'::text AS Kind,
                    a.AgentKey,
                    a.Title,
                    a.Description AS Message,
                    a.RiskLevel AS Severity,
                    a.Status,
                    a.RunId,
                    a.Id AS ReferenceId,
                    a.PayloadJson::text AS DataJson,
                    COALESCE(a.DecidedAt, a.RequestedAt) AS OccurredAt
                FROM AgentApprovals a
                WHERE a.OrganizationId = @OrganizationId
            ) activity
            ORDER BY OccurredAt DESC, Id DESC
            LIMIT @Limit
            """,
            new { OrganizationId = organizationId, Limit = Math.Clamp(limit, 1, 200) },
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<AgentActivityItem>(command)).AsList();
    }

    public async Task<IReadOnlyList<AgentApproval>> GetApprovalsAsync(Guid organizationId, string? status = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentApprovals
            SET Status = 'Expired'
            WHERE OrganizationId = @OrganizationId AND Status = 'Pending'
                AND ExpiresAt IS NOT NULL AND ExpiresAt <= CURRENT_TIMESTAMP
            """,
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentRecommendations recommendation
            SET Status = 'Rejected',
                RejectionReason = 'Approval request expired.',
                UpdatedAt = CURRENT_TIMESTAMP
            FROM AgentApprovals approval
            WHERE recommendation.OrganizationId = @OrganizationId
              AND recommendation.ApprovalId = approval.Id
              AND recommendation.Status = 'AwaitingApproval'
              AND approval.Status = 'Expired'
            """,
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentContentExecutions execution
            SET Status = 'Rejected',
                ReviewNote = 'Publishing approval request expired.',
                UpdatedAt = CURRENT_TIMESTAMP
            FROM AgentApprovals approval
            WHERE execution.OrganizationId = @OrganizationId
              AND execution.PublishApprovalId = approval.Id
              AND execution.Status = 'AwaitingPublishApproval'
              AND approval.Status = 'Expired'
            """,
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentRuns run
            SET Status = 'Completed', CompletedAt = CURRENT_TIMESTAMP, UpdatedAt = CURRENT_TIMESTAMP
            WHERE run.OrganizationId = @OrganizationId
              AND run.AgentKey = 'geo-strategy'
              AND run.Status = 'WaitingForApproval'
              AND NOT EXISTS (
                  SELECT 1 FROM AgentApprovals approval
                  WHERE approval.OrganizationId = run.OrganizationId
                    AND approval.RunId = run.Id
                    AND approval.Status = 'Pending'
                    AND (approval.ExpiresAt IS NULL OR approval.ExpiresAt > CURRENT_TIMESTAMP)
              )
            """,
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentRuns run
            SET Status = 'Completed', CompletedAt = CURRENT_TIMESTAMP, UpdatedAt = CURRENT_TIMESTAMP
            WHERE run.OrganizationId = @OrganizationId
              AND run.AgentKey = 'content-execution'
              AND run.Status = 'WaitingForApproval'
              AND NOT EXISTS (
                  SELECT 1 FROM AgentApprovals approval
                  WHERE approval.OrganizationId = run.OrganizationId
                    AND approval.RunId = run.Id
                    AND approval.Status = 'Pending'
                    AND (approval.ExpiresAt IS NULL OR approval.ExpiresAt > CURRENT_TIMESTAMP)
              )
            """,
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        var command = new CommandDefinition(
            """
            SELECT Id, OrganizationId, RunId, FindingId, AgentKey, ActionType, Title, Description,
                   PayloadJson::text AS PayloadJson, RiskLevel, Status, IdempotencyKey, RequestedAt,
                   ExpiresAt, DecidedByUserId, DecidedAt, DecisionNote, ExecutedAt
            FROM AgentApprovals
            WHERE OrganizationId = @OrganizationId
              AND (@Status IS NULL OR Status = @Status)
            ORDER BY CASE WHEN Status = 'Pending' THEN 0 ELSE 1 END, RequestedAt DESC
            LIMIT @Limit
            """,
            new { OrganizationId = organizationId, Status = string.IsNullOrWhiteSpace(status) ? null : status, Limit = Math.Clamp(limit, 1, 200) },
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<AgentApproval>(command)).AsList();
    }

    public async Task<IReadOnlyList<AgentSchedule>> GetSchedulesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT Id, OrganizationId, AgentKey, TriggerType, TriggerExpression, TimeZone, IsEnabled,
                   LastRunAt, NextRunAt, CreatedAt, UpdatedAt
            FROM AgentSchedules
            WHERE OrganizationId = @OrganizationId
            ORDER BY AgentKey, TriggerType, TriggerExpression
            """,
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<AgentSchedule>(command)).AsList();
    }

    public async Task<IReadOnlyList<AgentRun>> GetRunsAsync(Guid organizationId, string? agentKey = null, string? status = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT Id, OrganizationId, AgentKey, InitiatedByUserId, ParentRunId, TriggerType, TriggerReference,
                   Status, IdempotencyKey, InputJson::text AS InputJson, OutputJson::text AS OutputJson,
                   Provider, Model, PromptTokens, CompletionTokens, CostMicroUsd, Attempt, MaxAttempts,
                   ErrorCode, ErrorMessage, QueuedAt, StartedAt, CompletedAt, CancelRequestedAt, UpdatedAt
            FROM AgentRuns
            WHERE OrganizationId = @OrganizationId
              AND (@AgentKey IS NULL OR AgentKey = @AgentKey)
              AND (@Status IS NULL OR Status = @Status)
            ORDER BY QueuedAt DESC
            LIMIT @Limit
            """,
            new
            {
                OrganizationId = organizationId,
                AgentKey = string.IsNullOrWhiteSpace(agentKey) ? null : agentKey,
                Status = string.IsNullOrWhiteSpace(status) ? null : status,
                Limit = Math.Clamp(limit, 1, 200)
            },
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<AgentRun>(command)).AsList();
    }

    public async Task<AgentRun?> GetRunAsync(Guid organizationId, Guid runId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT Id, OrganizationId, AgentKey, InitiatedByUserId, ParentRunId, TriggerType, TriggerReference,
                   Status, IdempotencyKey, InputJson::text AS InputJson, OutputJson::text AS OutputJson,
                   Provider, Model, PromptTokens, CompletionTokens, CostMicroUsd, Attempt, MaxAttempts,
                   ErrorCode, ErrorMessage, QueuedAt, StartedAt, CompletedAt, CancelRequestedAt, UpdatedAt
            FROM AgentRuns
            WHERE Id = @RunId AND OrganizationId = @OrganizationId
            """,
            new { RunId = runId, OrganizationId = organizationId },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AgentRun>(command);
    }

    public async Task<IReadOnlyList<AgentFinding>> GetFindingsAsync(Guid organizationId, string? status = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT Id, OrganizationId, RunId, AgentKey, FindingType, Severity, Title, Summary,
                   EntityType, EntityIdsJson::text AS EntityIdsJson, EvidenceJson::text AS EvidenceJson,
                   ObservationStartedAt, ObservationEndedAt, Confidence, DeduplicationKey, Status,
                   CreatedAt, UpdatedAt, ResolvedAt
            FROM AgentFindings
            WHERE OrganizationId = @OrganizationId
              AND (@Status IS NULL OR Status = @Status)
            ORDER BY UpdatedAt DESC, Id DESC
            LIMIT @Limit
            """,
            new
            {
                OrganizationId = organizationId,
                Status = string.IsNullOrWhiteSpace(status) ? null : status,
                Limit = Math.Clamp(limit, 1, 200)
            },
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<AgentFinding>(command)).AsList();
    }

    public async Task<int> CountRunsSinceAsync(Guid organizationId, string agentKey, DateTime since, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT COUNT(*)::int
            FROM AgentRuns
            WHERE OrganizationId = @OrganizationId AND AgentKey = @AgentKey AND QueuedAt >= @Since
            """,
            new { OrganizationId = organizationId, AgentKey = agentKey, Since = since },
            cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<int>(command);
    }

    public async Task<AgentSetting?> UpdateSettingAsync(AgentSetting setting, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            UPDATE AgentSettings
            SET IsEnabled = @IsEnabled,
                AutonomyLevel = @AutonomyLevel,
                MaxRunsPerDay = @MaxRunsPerDay,
                MaxCostMicroUsdPerRun = @MaxCostMicroUsdPerRun,
                AllowedActionsJson = @AllowedActionsJson::jsonb,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE OrganizationId = @OrganizationId AND AgentKey = @AgentKey
            RETURNING Id, OrganizationId, AgentKey, IsEnabled, AutonomyLevel, MaxRunsPerDay,
                      MaxCostMicroUsdPerRun, AllowedActionsJson::text AS AllowedActionsJson, CreatedAt, UpdatedAt
            """,
            setting,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AgentSetting>(command);
    }

    public async Task<AgentSchedule?> UpsertScheduleAsync(AgentSchedule schedule, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            INSERT INTO AgentSchedules
                (OrganizationId, AgentKey, TriggerType, TriggerExpression, TimeZone, IsEnabled, NextRunAt)
            VALUES
                (@OrganizationId, @AgentKey, @TriggerType, @TriggerExpression, @TimeZone, @IsEnabled, @NextRunAt)
            ON CONFLICT (OrganizationId, AgentKey, TriggerType, TriggerExpression)
            DO UPDATE SET
                TimeZone = EXCLUDED.TimeZone,
                IsEnabled = EXCLUDED.IsEnabled,
                NextRunAt = EXCLUDED.NextRunAt,
                UpdatedAt = CURRENT_TIMESTAMP
            RETURNING Id, OrganizationId, AgentKey, TriggerType, TriggerExpression, TimeZone, IsEnabled,
                      LastRunAt, NextRunAt, CreatedAt, UpdatedAt
            """,
            schedule,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AgentSchedule>(command);
    }

    public async Task<AgentApproval?> DecideApprovalAsync(Guid organizationId, Guid approvalId, Guid decidedByUserId, string decision, string decisionNote, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var current = await connection.QuerySingleOrDefaultAsync<AgentApproval>(new CommandDefinition(
            """
            SELECT Id, OrganizationId, RunId, FindingId, AgentKey, ActionType, Title, Description,
                   PayloadJson::text AS PayloadJson, RiskLevel, Status, IdempotencyKey, RequestedAt,
                   ExpiresAt, DecidedByUserId, DecidedAt, DecisionNote, ExecutedAt
            FROM AgentApprovals
            WHERE Id = @ApprovalId AND OrganizationId = @OrganizationId
            FOR UPDATE
            """,
            new { ApprovalId = approvalId, OrganizationId = organizationId },
            transaction,
            cancellationToken: cancellationToken));

        if (current == null || current.Status != AgentApprovalStatuses.Pending)
        {
            transaction.Rollback();
            return null;
        }

        if (current.ExpiresAt.HasValue && current.ExpiresAt <= DateTime.UtcNow)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE AgentApprovals SET Status = 'Expired' WHERE Id = @ApprovalId",
                new { ApprovalId = approvalId }, transaction, cancellationToken: cancellationToken));
            transaction.Commit();
            return null;
        }

        var updated = await connection.QuerySingleAsync<AgentApproval>(new CommandDefinition(
            """
            UPDATE AgentApprovals
            SET Status = @Decision,
                DecidedByUserId = @DecidedByUserId,
                DecidedAt = CURRENT_TIMESTAMP,
                DecisionNote = @DecisionNote
            WHERE Id = @ApprovalId AND OrganizationId = @OrganizationId
            RETURNING Id, OrganizationId, RunId, FindingId, AgentKey, ActionType, Title, Description,
                      PayloadJson::text AS PayloadJson, RiskLevel, Status, IdempotencyKey, RequestedAt,
                      ExpiresAt, DecidedByUserId, DecidedAt, DecisionNote, ExecutedAt
            """,
            new { ApprovalId = approvalId, OrganizationId = organizationId, Decision = decision, DecidedByUserId = decidedByUserId, DecisionNote = decisionNote },
            transaction,
            cancellationToken: cancellationToken));

        var isRecommendationApproval = current.ActionType == "recommendation.approve";
        var isContentPublishApproval = current.ActionType == "content.publish";
        if (isRecommendationApproval)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE AgentRecommendations
                SET Status = CASE WHEN @Decision = 'Approved' THEN 'Approved' ELSE 'Rejected' END,
                    ApprovedAt = CASE WHEN @Decision = 'Approved' THEN CURRENT_TIMESTAMP ELSE NULL END,
                    RejectionReason = CASE WHEN @Decision = 'Rejected' THEN @DecisionNote ELSE '' END,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE OrganizationId = @OrganizationId AND ApprovalId = @ApprovalId
                  AND Status = 'AwaitingApproval'
                """,
                new { OrganizationId = organizationId, ApprovalId = approvalId, Decision = decision, DecisionNote = decisionNote },
                transaction,
                cancellationToken: cancellationToken));
        }
        else if (isContentPublishApproval)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE AgentContentExecutions
                SET Status = CASE WHEN @Decision = 'Approved' THEN 'ApprovedForPublishing' ELSE 'Rejected' END,
                    ReviewNote = @DecisionNote,
                    ReviewedByUserId = @DecidedByUserId,
                    ReviewedAt = CURRENT_TIMESTAMP,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE OrganizationId = @OrganizationId AND PublishApprovalId = @ApprovalId
                  AND Status = 'AwaitingPublishApproval'
                """,
                new
                {
                    OrganizationId = organizationId,
                    ApprovalId = approvalId,
                    Decision = decision,
                    DecisionNote = decisionNote,
                    DecidedByUserId = decidedByUserId
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        var pendingApprovals = isRecommendationApproval
            ? await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*)::int FROM AgentApprovals
                WHERE OrganizationId = @OrganizationId AND RunId = @RunId AND Status = 'Pending'
                  AND (ExpiresAt IS NULL OR ExpiresAt > CURRENT_TIMESTAMP)
                """,
                new { OrganizationId = organizationId, RunId = current.RunId },
                transaction,
                cancellationToken: cancellationToken))
            : 0;
        var nextRunStatus = isRecommendationApproval
            ? pendingApprovals > 0 ? AgentRunStatuses.WaitingForApproval : AgentRunStatuses.Completed
            : decision == AgentApprovalStatuses.Approved ? AgentRunStatuses.Queued : AgentRunStatuses.Cancelled;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentRuns
            SET Status = @NextRunStatus,
                CompletedAt = CASE WHEN @NextRunStatus IN ('Cancelled', 'Completed') THEN CURRENT_TIMESTAMP ELSE NULL END,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @RunId AND OrganizationId = @OrganizationId AND Status = 'WaitingForApproval'
            """,
            new { RunId = current.RunId, OrganizationId = organizationId, NextRunStatus = nextRunStatus },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO AgentRunEvents (OrganizationId, RunId, EventType, Status, Message, DataJson)
            VALUES (@OrganizationId, @RunId, 'approval.resolved', @Decision, @Message, @DataJson::jsonb)
            """,
            new
            {
                OrganizationId = organizationId,
                RunId = current.RunId,
                Decision = decision,
                Message = decision == AgentApprovalStatuses.Approved ? "Action approved by an authorized user." : "Action rejected by an authorized user.",
                DataJson = System.Text.Json.JsonSerializer.Serialize(new { approvalId, decidedByUserId, decisionNote })
            },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return updated;
    }

    public async Task<bool> CancelRunAsync(Guid organizationId, Guid runId, Guid requestedByUserId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var run = await connection.QuerySingleOrDefaultAsync<AgentRun>(new CommandDefinition(
            """
            SELECT Id, OrganizationId, AgentKey, InitiatedByUserId, ParentRunId, TriggerType, TriggerReference,
                   Status, IdempotencyKey, InputJson::text AS InputJson, OutputJson::text AS OutputJson,
                   Provider, Model, PromptTokens, CompletionTokens, CostMicroUsd, Attempt, MaxAttempts,
                   ErrorCode, ErrorMessage, QueuedAt, StartedAt, CompletedAt, CancelRequestedAt, UpdatedAt
            FROM AgentRuns
            WHERE Id = @RunId AND OrganizationId = @OrganizationId
            FOR UPDATE
            """,
            new { RunId = runId, OrganizationId = organizationId }, transaction, cancellationToken: cancellationToken));

        if (run == null || !AgentControlPlanePolicy.CanCancelRun(run.Status))
        {
            transaction.Rollback();
            return false;
        }

        var nextStatus = run.Status == AgentRunStatuses.Running
            ? AgentRunStatuses.CancellationRequested
            : AgentRunStatuses.Cancelled;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentRuns
            SET Status = @NextStatus,
                CancelRequestedAt = CURRENT_TIMESTAMP,
                CompletedAt = CASE WHEN @NextStatus = 'Cancelled' THEN CURRENT_TIMESTAMP ELSE CompletedAt END,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @RunId AND OrganizationId = @OrganizationId
            """,
            new { RunId = runId, OrganizationId = organizationId, NextStatus = nextStatus },
            transaction,
            cancellationToken: cancellationToken));

        if (nextStatus == AgentRunStatuses.Cancelled)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE AgentApprovals
                SET Status = 'Rejected', DecidedByUserId = @RequestedByUserId,
                    DecidedAt = CURRENT_TIMESTAMP, DecisionNote = 'Associated agent run was cancelled.'
                WHERE OrganizationId = @OrganizationId AND RunId = @RunId AND Status = 'Pending';

                UPDATE AgentRecommendations recommendation
                SET Status = 'Rejected', RejectionReason = 'Associated agent run was cancelled.',
                    UpdatedAt = CURRENT_TIMESTAMP
                FROM AgentApprovals approval
                WHERE recommendation.OrganizationId = @OrganizationId
                  AND recommendation.ApprovalId = approval.Id
                  AND approval.RunId = @RunId
                  AND recommendation.Status = 'AwaitingApproval';

                UPDATE AgentContentExecutions execution
                SET Status = 'Rejected', ReviewNote = 'Associated agent run was cancelled.',
                    UpdatedAt = CURRENT_TIMESTAMP
                FROM AgentApprovals approval
                WHERE execution.OrganizationId = @OrganizationId
                  AND execution.PublishApprovalId = approval.Id
                  AND approval.RunId = @RunId
                  AND execution.Status = 'AwaitingPublishApproval';
                """,
                new { OrganizationId = organizationId, RunId = runId, RequestedByUserId = requestedByUserId },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO AgentRunEvents (OrganizationId, RunId, EventType, Status, Message, DataJson)
            VALUES (@OrganizationId, @RunId, 'run.cancel_requested', @Status, 'Cancellation requested by an authorized user.', @DataJson::jsonb)
            """,
            new
            {
                OrganizationId = organizationId,
                RunId = runId,
                Status = nextStatus,
                DataJson = System.Text.Json.JsonSerializer.Serialize(new { requestedByUserId })
            }, transaction, cancellationToken: cancellationToken));

        transaction.Commit();
        return true;
    }

    public async Task<AgentRun?> RetryRunAsync(Guid organizationId, Guid runId, Guid initiatedByUserId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var previous = await connection.QuerySingleOrDefaultAsync<AgentRun>(new CommandDefinition(
            """
            SELECT Id, OrganizationId, AgentKey, InitiatedByUserId, ParentRunId, TriggerType, TriggerReference,
                   Status, IdempotencyKey, InputJson::text AS InputJson, OutputJson::text AS OutputJson,
                   Provider, Model, PromptTokens, CompletionTokens, CostMicroUsd, Attempt, MaxAttempts,
                   ErrorCode, ErrorMessage, QueuedAt, StartedAt, CompletedAt, CancelRequestedAt, UpdatedAt
            FROM AgentRuns
            WHERE Id = @RunId AND OrganizationId = @OrganizationId
            FOR UPDATE
            """,
            new { RunId = runId, OrganizationId = organizationId }, transaction, cancellationToken: cancellationToken));

        if (previous == null || !AgentControlPlanePolicy.CanRetryRun(previous.Status, previous.Attempt, previous.MaxAttempts))
        {
            transaction.Rollback();
            return null;
        }

        var retryIdempotencyKey = $"retry:{previous.Id}:{previous.Attempt + 1}";
        var retry = await connection.QuerySingleAsync<AgentRun>(new CommandDefinition(
            """
            INSERT INTO AgentRuns
                (OrganizationId, AgentKey, InitiatedByUserId, ParentRunId, TriggerType, TriggerReference,
                 Status, IdempotencyKey, InputJson, Attempt, MaxAttempts)
            VALUES
                (@OrganizationId, @AgentKey, @InitiatedByUserId, @ParentRunId, 'Retry', @TriggerReference,
                 'Queued', @IdempotencyKey, @InputJson::jsonb, @Attempt, @MaxAttempts)
            ON CONFLICT (OrganizationId, IdempotencyKey)
            DO UPDATE SET UpdatedAt = AgentRuns.UpdatedAt
            RETURNING Id, OrganizationId, AgentKey, InitiatedByUserId, ParentRunId, TriggerType, TriggerReference,
                      Status, IdempotencyKey, InputJson::text AS InputJson, OutputJson::text AS OutputJson,
                      Provider, Model, PromptTokens, CompletionTokens, CostMicroUsd, Attempt, MaxAttempts,
                      ErrorCode, ErrorMessage, QueuedAt, StartedAt, CompletedAt, CancelRequestedAt, UpdatedAt
            """,
            new
            {
                OrganizationId = organizationId,
                previous.AgentKey,
                InitiatedByUserId = initiatedByUserId,
                ParentRunId = previous.Id,
                TriggerReference = previous.TriggerReference,
                IdempotencyKey = retryIdempotencyKey,
                previous.InputJson,
                Attempt = previous.Attempt + 1,
                previous.MaxAttempts
            }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO AgentRunEvents (OrganizationId, RunId, EventType, Status, Message, DataJson)
            VALUES (@OrganizationId, @RunId, 'run.retried', 'Queued', 'A retry was queued by an authorized user.', @DataJson::jsonb)
            """,
            new
            {
                OrganizationId = organizationId,
                RunId = retry.Id,
                DataJson = System.Text.Json.JsonSerializer.Serialize(new { previousRunId = previous.Id, initiatedByUserId })
            }, transaction, cancellationToken: cancellationToken));

        transaction.Commit();
        return retry;
    }

    public async Task<AgentRun> CreateRunAsync(AgentRun run, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(run.IdempotencyKey))
            throw new ArgumentException("Agent runs require an idempotency key.", nameof(run));

        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            INSERT INTO AgentRuns
                (OrganizationId, AgentKey, InitiatedByUserId, ParentRunId, TriggerType, TriggerReference,
                 Status, IdempotencyKey, InputJson, Attempt, MaxAttempts)
            SELECT
                @OrganizationId, @AgentKey, @InitiatedByUserId, @ParentRunId, @TriggerType, @TriggerReference,
                @Status, @IdempotencyKey, @InputJson::jsonb, @Attempt, @MaxAttempts
            WHERE @ParentRunId IS NULL OR EXISTS (
                SELECT 1 FROM AgentRuns parent
                WHERE parent.Id = @ParentRunId AND parent.OrganizationId = @OrganizationId
            )
            ON CONFLICT (OrganizationId, IdempotencyKey)
            DO UPDATE SET UpdatedAt = AgentRuns.UpdatedAt
            RETURNING Id, OrganizationId, AgentKey, InitiatedByUserId, ParentRunId, TriggerType, TriggerReference,
                      Status, IdempotencyKey, InputJson::text AS InputJson, OutputJson::text AS OutputJson,
                      Provider, Model, PromptTokens, CompletionTokens, CostMicroUsd, Attempt, MaxAttempts,
                      ErrorCode, ErrorMessage, QueuedAt, StartedAt, CompletedAt, CancelRequestedAt, UpdatedAt
            """,
            run,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<AgentRun>(command);
    }

    public async Task<AgentRun?> UpdateRunExecutionAsync(AgentRun run, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            UPDATE AgentRuns
            SET Status = @Status,
                OutputJson = @OutputJson::jsonb,
                Provider = @Provider,
                Model = @Model,
                PromptTokens = @PromptTokens,
                CompletionTokens = @CompletionTokens,
                CostMicroUsd = @CostMicroUsd,
                ErrorCode = @ErrorCode,
                ErrorMessage = @ErrorMessage,
                StartedAt = COALESCE(StartedAt, @StartedAt, CURRENT_TIMESTAMP),
                CompletedAt = CASE
                    WHEN @Status IN ('Completed', 'Failed', 'Cancelled') THEN COALESCE(@CompletedAt, CURRENT_TIMESTAMP)
                    ELSE NULL
                END,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @Id AND OrganizationId = @OrganizationId
              AND Status <> 'Cancelled'
            RETURNING Id, OrganizationId, AgentKey, InitiatedByUserId, ParentRunId, TriggerType, TriggerReference,
                      Status, IdempotencyKey, InputJson::text AS InputJson, OutputJson::text AS OutputJson,
                      Provider, Model, PromptTokens, CompletionTokens, CostMicroUsd, Attempt, MaxAttempts,
                      ErrorCode, ErrorMessage, QueuedAt, StartedAt, CompletedAt, CancelRequestedAt, UpdatedAt
            """,
            run,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AgentRun>(command);
    }

    public async Task<AgentRunEvent> AppendRunEventAsync(AgentRunEvent runEvent, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            INSERT INTO AgentRunEvents (OrganizationId, RunId, EventType, Status, Message, DataJson)
            SELECT @OrganizationId, Id, @EventType, @Status, @Message, @DataJson::jsonb
            FROM AgentRuns
            WHERE Id = @RunId AND OrganizationId = @OrganizationId
            RETURNING Id, OrganizationId, RunId, EventType, Status, Message, DataJson::text AS DataJson, CreatedAt
            """,
            runEvent,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<AgentRunEvent>(command);
    }

    public async Task<AgentFinding> UpsertFindingAsync(AgentFinding finding, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            INSERT INTO AgentFindings
                (OrganizationId, RunId, AgentKey, FindingType, Severity, Title, Summary, EntityType,
                 EntityIdsJson, EvidenceJson, ObservationStartedAt, ObservationEndedAt, Confidence,
                 DeduplicationKey, Status)
            SELECT
                @OrganizationId, @RunId, @AgentKey, @FindingType, @Severity, @Title, @Summary, @EntityType,
                @EntityIdsJson::jsonb, @EvidenceJson::jsonb, @ObservationStartedAt, @ObservationEndedAt,
                @Confidence, @DeduplicationKey, @Status
            WHERE @RunId IS NULL OR EXISTS (
                SELECT 1 FROM AgentRuns source_run
                WHERE source_run.Id = @RunId AND source_run.OrganizationId = @OrganizationId
            )
            ON CONFLICT (OrganizationId, DeduplicationKey)
            DO UPDATE SET
                RunId = EXCLUDED.RunId,
                Severity = EXCLUDED.Severity,
                Title = EXCLUDED.Title,
                Summary = EXCLUDED.Summary,
                EntityIdsJson = EXCLUDED.EntityIdsJson,
                EvidenceJson = EXCLUDED.EvidenceJson,
                ObservationStartedAt = EXCLUDED.ObservationStartedAt,
                ObservationEndedAt = EXCLUDED.ObservationEndedAt,
                Confidence = EXCLUDED.Confidence,
                Status = EXCLUDED.Status,
                ResolvedAt = NULL,
                UpdatedAt = CURRENT_TIMESTAMP
            RETURNING Id, OrganizationId, RunId, AgentKey, FindingType, Severity, Title, Summary, EntityType,
                      EntityIdsJson::text AS EntityIdsJson, EvidenceJson::text AS EvidenceJson,
                      ObservationStartedAt, ObservationEndedAt, Confidence, DeduplicationKey, Status,
                      CreatedAt, UpdatedAt, ResolvedAt
            """,
            finding,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<AgentFinding>(command);
    }

    public async Task<AgentApproval> CreateApprovalAsync(AgentApproval approval, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            INSERT INTO AgentApprovals
                (OrganizationId, RunId, FindingId, AgentKey, ActionType, Title, Description, PayloadJson,
                 RiskLevel, Status, IdempotencyKey, ExpiresAt)
            SELECT @OrganizationId, r.Id, @FindingId, r.AgentKey, @ActionType, @Title, @Description,
                   @PayloadJson::jsonb, @RiskLevel, 'Pending', @IdempotencyKey, @ExpiresAt
            FROM AgentRuns r
            WHERE r.Id = @RunId AND r.OrganizationId = @OrganizationId
              AND (@FindingId IS NULL OR EXISTS (
                  SELECT 1 FROM AgentFindings f
                  WHERE f.Id = @FindingId AND f.OrganizationId = @OrganizationId
              ))
            ON CONFLICT (OrganizationId, IdempotencyKey)
            DO UPDATE SET IdempotencyKey = AgentApprovals.IdempotencyKey
            RETURNING Id, OrganizationId, RunId, FindingId, AgentKey, ActionType, Title, Description,
                      PayloadJson::text AS PayloadJson, RiskLevel, Status, IdempotencyKey, RequestedAt,
                      ExpiresAt, DecidedByUserId, DecidedAt, DecisionNote, ExecutedAt
            """,
            approval,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<AgentApproval>(command);
    }

    public async Task<IReadOnlyList<AgentRecommendation>> GetRecommendationsAsync(Guid organizationId, string? status = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT r.Id, r.OrganizationId, r.RunId, r.FindingId, r.ApprovalId, r.AgentKey,
                   r.RecommendationType, r.Category, r.Title, r.Summary, r.Rationale,
                   r.TargetType, r.TargetKey, r.EvidenceJson::text AS EvidenceJson,
                   r.ActionPlanJson::text AS ActionPlanJson, r.ValidationPlanJson::text AS ValidationPlanJson,
                   r.ExpectedImpact, r.ImpactScore, r.EffortScore, r.UrgencyScore, r.GoalAlignmentScore,
                   r.Confidence, r.PriorityScore, r.Status, r.AssignedToUserId,
                   COALESCE(u.DisplayName, u.Email, '') AS AssignedToName, r.RejectionReason,
                   r.DeduplicationKey, r.CreatedAt, r.UpdatedAt, r.ApprovedAt, r.AssignedAt, r.ImplementedAt
            FROM AgentRecommendations r
            LEFT JOIN Users u ON u.Id = r.AssignedToUserId AND u.OrganizationId = r.OrganizationId
            WHERE r.OrganizationId = @OrganizationId
              AND (@Status IS NULL OR r.Status = @Status)
            ORDER BY CASE r.Status WHEN 'AwaitingApproval' THEN 0 WHEN 'Approved' THEN 1 WHEN 'Assigned' THEN 2 WHEN 'InProgress' THEN 3 ELSE 4 END,
                     r.PriorityScore DESC, r.UpdatedAt DESC
            LIMIT @Limit
            """,
            new
            {
                OrganizationId = organizationId,
                Status = string.IsNullOrWhiteSpace(status) ? null : status,
                Limit = Math.Clamp(limit, 1, 200)
            },
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<AgentRecommendation>(command)).AsList();
    }

    public async Task<AgentRecommendation?> GetRecommendationAsync(Guid organizationId, Guid recommendationId, CancellationToken cancellationToken = default)
    {
        var recommendations = await GetRecommendationsByIdAsync(organizationId, recommendationId, cancellationToken);
        return recommendations.SingleOrDefault();
    }

    public async Task<AgentRecommendation> UpsertRecommendationAsync(AgentRecommendation recommendation, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            INSERT INTO AgentRecommendations
                (OrganizationId, RunId, FindingId, AgentKey, RecommendationType, Category, Title, Summary,
                 Rationale, TargetType, TargetKey, EvidenceJson, ActionPlanJson, ValidationPlanJson,
                 ExpectedImpact, ImpactScore, EffortScore, UrgencyScore, GoalAlignmentScore, Confidence,
                 PriorityScore, Status, DeduplicationKey)
            SELECT @OrganizationId, @RunId, @FindingId, @AgentKey, @RecommendationType, @Category, @Title,
                   @Summary, @Rationale, @TargetType, @TargetKey, @EvidenceJson::jsonb, @ActionPlanJson::jsonb,
                   @ValidationPlanJson::jsonb, @ExpectedImpact, @ImpactScore, @EffortScore, @UrgencyScore,
                   @GoalAlignmentScore, @Confidence, @PriorityScore, 'AwaitingApproval', @DeduplicationKey
            WHERE EXISTS (
                SELECT 1 FROM AgentRuns run
                WHERE run.Id = @RunId AND run.OrganizationId = @OrganizationId
            ) AND EXISTS (
                SELECT 1 FROM AgentFindings finding
                WHERE finding.Id = @FindingId AND finding.OrganizationId = @OrganizationId
            )
            ON CONFLICT (OrganizationId, DeduplicationKey)
            DO UPDATE SET
                RunId = CASE
                    WHEN AgentRecommendations.Status = 'AwaitingApproval' AND AgentRecommendations.ApprovalId IS NOT NULL
                    THEN AgentRecommendations.RunId
                    ELSE EXCLUDED.RunId
                END,
                FindingId = EXCLUDED.FindingId,
                Category = EXCLUDED.Category,
                Title = EXCLUDED.Title,
                Summary = EXCLUDED.Summary,
                Rationale = EXCLUDED.Rationale,
                EvidenceJson = EXCLUDED.EvidenceJson,
                ActionPlanJson = EXCLUDED.ActionPlanJson,
                ValidationPlanJson = EXCLUDED.ValidationPlanJson,
                ExpectedImpact = EXCLUDED.ExpectedImpact,
                ImpactScore = EXCLUDED.ImpactScore,
                EffortScore = EXCLUDED.EffortScore,
                UrgencyScore = EXCLUDED.UrgencyScore,
                GoalAlignmentScore = EXCLUDED.GoalAlignmentScore,
                Confidence = EXCLUDED.Confidence,
                PriorityScore = EXCLUDED.PriorityScore,
                UpdatedAt = CURRENT_TIMESTAMP
            RETURNING Id, OrganizationId, RunId, FindingId, ApprovalId, AgentKey, RecommendationType,
                      Category, Title, Summary, Rationale, TargetType, TargetKey,
                      EvidenceJson::text AS EvidenceJson, ActionPlanJson::text AS ActionPlanJson,
                      ValidationPlanJson::text AS ValidationPlanJson, ExpectedImpact, ImpactScore,
                      EffortScore, UrgencyScore, GoalAlignmentScore, Confidence, PriorityScore, Status,
                      AssignedToUserId, RejectionReason, DeduplicationKey, CreatedAt, UpdatedAt,
                      ApprovedAt, AssignedAt, ImplementedAt
            """,
            recommendation,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<AgentRecommendation>(command);
    }

    public async Task<AgentRecommendation?> LinkRecommendationApprovalAsync(Guid organizationId, Guid recommendationId, Guid approvalId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentRecommendations recommendation
            SET ApprovalId = approval.Id, UpdatedAt = CURRENT_TIMESTAMP
            FROM AgentApprovals approval
            WHERE recommendation.Id = @RecommendationId
              AND recommendation.OrganizationId = @OrganizationId
              AND approval.Id = @ApprovalId
              AND approval.OrganizationId = recommendation.OrganizationId
              AND approval.RunId = recommendation.RunId
            """,
            new { OrganizationId = organizationId, RecommendationId = recommendationId, ApprovalId = approvalId },
            cancellationToken: cancellationToken));
        if (affected == 0) return null;
        return await GetRecommendationAsync(organizationId, recommendationId, cancellationToken);
    }

    public async Task<AgentRecommendation?> AssignRecommendationAsync(Guid organizationId, Guid recommendationId, Guid? assignedToUserId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentRecommendations
            SET AssignedToUserId = @AssignedToUserId,
                AssignedAt = CASE WHEN @AssignedToUserId IS NULL THEN NULL ELSE CURRENT_TIMESTAMP END,
                Status = CASE WHEN @AssignedToUserId IS NULL THEN 'Approved' ELSE 'Assigned' END,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @RecommendationId AND OrganizationId = @OrganizationId
              AND Status IN ('Approved', 'Assigned')
              AND (@AssignedToUserId IS NULL OR EXISTS (
                  SELECT 1 FROM Users
                  WHERE Id = @AssignedToUserId AND OrganizationId = @OrganizationId
              ))
            """,
            new { OrganizationId = organizationId, RecommendationId = recommendationId, AssignedToUserId = assignedToUserId },
            cancellationToken: cancellationToken));
        if (affected == 0) return null;
        return await GetRecommendationAsync(organizationId, recommendationId, cancellationToken);
    }

    public async Task<AgentRecommendation?> UpdateRecommendationStatusAsync(Guid organizationId, Guid recommendationId, string status, string note, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentRecommendations
            SET Status = @Status,
                RejectionReason = CASE WHEN @Status = 'Dismissed' THEN @Note ELSE RejectionReason END,
                ImplementedAt = CASE WHEN @Status = 'Implemented' THEN CURRENT_TIMESTAMP ELSE ImplementedAt END,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @RecommendationId AND OrganizationId = @OrganizationId
              AND (
                  (@Status = 'InProgress' AND Status IN ('Approved', 'Assigned'))
                  OR (@Status = 'Implemented' AND Status = 'InProgress')
                  OR (@Status = 'Dismissed' AND Status IN ('Approved', 'Assigned', 'InProgress'))
              )
            """,
            new { OrganizationId = organizationId, RecommendationId = recommendationId, Status = status, Note = note },
            cancellationToken: cancellationToken));
        if (affected == 0) return null;
        return await GetRecommendationAsync(organizationId, recommendationId, cancellationToken);
    }

    public async Task<AgentStrategyPreference> GetStrategyPreferenceAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(organizationId, cancellationToken);
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT OrganizationId, PrimaryGoal, CreatedAt, UpdatedAt
            FROM AgentStrategyPreferences
            WHERE OrganizationId = @OrganizationId
            """,
            new { OrganizationId = organizationId },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<AgentStrategyPreference>(command);
    }

    public async Task<AgentStrategyPreference> UpsertStrategyPreferenceAsync(AgentStrategyPreference preference, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            INSERT INTO AgentStrategyPreferences (OrganizationId, PrimaryGoal)
            VALUES (@OrganizationId, @PrimaryGoal)
            ON CONFLICT (OrganizationId)
            DO UPDATE SET PrimaryGoal = EXCLUDED.PrimaryGoal, UpdatedAt = CURRENT_TIMESTAMP
            RETURNING OrganizationId, PrimaryGoal, CreatedAt, UpdatedAt
            """,
            preference,
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<AgentStrategyPreference>(command);
    }

    public async Task<IReadOnlyList<AgentContentExecution>> GetContentExecutionsAsync(Guid organizationId, string? status = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT Id, OrganizationId, RunId, RecommendationId, ContentDraftId, KnowledgeBaseId,
                   PublishApprovalId, Status, BriefJson::text AS BriefJson,
                   EvidenceJson::text AS EvidenceJson, ReviewDiffJson::text AS ReviewDiffJson,
                   PolicyChecksJson::text AS PolicyChecksJson, ReviewNote, ReviewedByUserId,
                   ReviewedAt, PublishedAt, CreatedAt, UpdatedAt
            FROM AgentContentExecutions
            WHERE OrganizationId = @OrganizationId
              AND (@Status IS NULL OR Status = @Status)
            ORDER BY UpdatedAt DESC
            LIMIT @Limit
            """,
            new
            {
                OrganizationId = organizationId,
                Status = string.IsNullOrWhiteSpace(status) ? null : status,
                Limit = Math.Clamp(limit, 1, 200)
            },
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<AgentContentExecution>(command)).AsList();
    }

    public async Task<AgentContentExecution?> GetContentExecutionAsync(Guid organizationId, Guid executionId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<AgentContentExecution>(new CommandDefinition(
            """
            SELECT Id, OrganizationId, RunId, RecommendationId, ContentDraftId, KnowledgeBaseId,
                   PublishApprovalId, Status, BriefJson::text AS BriefJson,
                   EvidenceJson::text AS EvidenceJson, ReviewDiffJson::text AS ReviewDiffJson,
                   PolicyChecksJson::text AS PolicyChecksJson, ReviewNote, ReviewedByUserId,
                   ReviewedAt, PublishedAt, CreatedAt, UpdatedAt
            FROM AgentContentExecutions
            WHERE OrganizationId = @OrganizationId AND Id = @ExecutionId
            """,
            new { OrganizationId = organizationId, ExecutionId = executionId },
            cancellationToken: cancellationToken));
    }

    public async Task<AgentContentExecution?> GetContentExecutionByRecommendationAsync(Guid organizationId, Guid recommendationId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<AgentContentExecution>(new CommandDefinition(
            """
            SELECT Id, OrganizationId, RunId, RecommendationId, ContentDraftId, KnowledgeBaseId,
                   PublishApprovalId, Status, BriefJson::text AS BriefJson,
                   EvidenceJson::text AS EvidenceJson, ReviewDiffJson::text AS ReviewDiffJson,
                   PolicyChecksJson::text AS PolicyChecksJson, ReviewNote, ReviewedByUserId,
                   ReviewedAt, PublishedAt, CreatedAt, UpdatedAt
            FROM AgentContentExecutions
            WHERE OrganizationId = @OrganizationId AND RecommendationId = @RecommendationId
            """,
            new { OrganizationId = organizationId, RecommendationId = recommendationId },
            cancellationToken: cancellationToken));
    }

    public async Task<AgentContentExecution> UpsertContentExecutionAsync(AgentContentExecution execution, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<AgentContentExecution>(new CommandDefinition(
            """
            INSERT INTO AgentContentExecutions
                (OrganizationId, RunId, RecommendationId, ContentDraftId, KnowledgeBaseId,
                 PublishApprovalId, Status, BriefJson, EvidenceJson, ReviewDiffJson,
                 PolicyChecksJson, ReviewNote, ReviewedByUserId, ReviewedAt, PublishedAt)
            SELECT @OrganizationId, @RunId, recommendation.Id, @ContentDraftId, @KnowledgeBaseId,
                   @PublishApprovalId, @Status, @BriefJson::jsonb, @EvidenceJson::jsonb,
                   @ReviewDiffJson::jsonb, @PolicyChecksJson::jsonb, @ReviewNote,
                   @ReviewedByUserId, @ReviewedAt, @PublishedAt
            FROM AgentRecommendations recommendation
            WHERE recommendation.Id = @RecommendationId
              AND recommendation.OrganizationId = @OrganizationId
              AND recommendation.Status IN ('Approved', 'Assigned', 'InProgress', 'Implemented')
              AND EXISTS (
                  SELECT 1 FROM AgentRuns run
                  WHERE run.Id = @RunId AND run.OrganizationId = @OrganizationId
              )
            ON CONFLICT (OrganizationId, RecommendationId)
            DO UPDATE SET
                ContentDraftId = COALESCE(EXCLUDED.ContentDraftId, AgentContentExecutions.ContentDraftId),
                KnowledgeBaseId = COALESCE(EXCLUDED.KnowledgeBaseId, AgentContentExecutions.KnowledgeBaseId),
                PublishApprovalId = COALESCE(EXCLUDED.PublishApprovalId, AgentContentExecutions.PublishApprovalId),
                Status = EXCLUDED.Status,
                BriefJson = EXCLUDED.BriefJson,
                EvidenceJson = EXCLUDED.EvidenceJson,
                ReviewDiffJson = EXCLUDED.ReviewDiffJson,
                PolicyChecksJson = EXCLUDED.PolicyChecksJson,
                ReviewNote = EXCLUDED.ReviewNote,
                ReviewedByUserId = COALESCE(EXCLUDED.ReviewedByUserId, AgentContentExecutions.ReviewedByUserId),
                ReviewedAt = COALESCE(EXCLUDED.ReviewedAt, AgentContentExecutions.ReviewedAt),
                PublishedAt = COALESCE(EXCLUDED.PublishedAt, AgentContentExecutions.PublishedAt),
                UpdatedAt = CURRENT_TIMESTAMP
            RETURNING Id, OrganizationId, RunId, RecommendationId, ContentDraftId, KnowledgeBaseId,
                      PublishApprovalId, Status, BriefJson::text AS BriefJson,
                      EvidenceJson::text AS EvidenceJson, ReviewDiffJson::text AS ReviewDiffJson,
                      PolicyChecksJson::text AS PolicyChecksJson, ReviewNote, ReviewedByUserId,
                      ReviewedAt, PublishedAt, CreatedAt, UpdatedAt
            """,
            execution,
            cancellationToken: cancellationToken));
    }

    public async Task<AgentContentExecution?> LinkContentPublishApprovalAsync(Guid organizationId, Guid executionId, Guid approvalId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentContentExecutions execution
            SET PublishApprovalId = approval.Id,
                Status = 'AwaitingPublishApproval',
                UpdatedAt = CURRENT_TIMESTAMP
            FROM AgentApprovals approval
            WHERE execution.Id = @ExecutionId AND execution.OrganizationId = @OrganizationId
              AND execution.Status = 'ReadyForReview'
              AND approval.Id = @ApprovalId
              AND approval.OrganizationId = execution.OrganizationId
              AND approval.RunId = execution.RunId
              AND approval.ActionType = 'content.publish'
            """,
            new { OrganizationId = organizationId, ExecutionId = executionId, ApprovalId = approvalId },
            transaction,
            cancellationToken: cancellationToken));
        if (affected == 0)
        {
            transaction.Rollback();
            return null;
        }
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentRuns
            SET Status = 'WaitingForApproval', CompletedAt = NULL, UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = (SELECT RunId FROM AgentContentExecutions WHERE Id = @ExecutionId)
              AND OrganizationId = @OrganizationId
            """,
            new { OrganizationId = organizationId, ExecutionId = executionId },
            transaction,
            cancellationToken: cancellationToken));
        transaction.Commit();
        return await GetContentExecutionAsync(organizationId, executionId, cancellationToken);
    }

    public async Task<AgentContentExecution?> UpdateContentExecutionStatusAsync(Guid organizationId, Guid executionId, string status, string reviewNote, Guid? reviewedByUserId = null, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentContentExecutions
            SET Status = @Status,
                ReviewNote = CASE WHEN @ReviewNote = '' THEN ReviewNote ELSE @ReviewNote END,
                ReviewedByUserId = COALESCE(@ReviewedByUserId, ReviewedByUserId),
                ReviewedAt = CASE WHEN @ReviewedByUserId IS NULL THEN ReviewedAt ELSE CURRENT_TIMESTAMP END,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @ExecutionId AND OrganizationId = @OrganizationId
            """,
            new { OrganizationId = organizationId, ExecutionId = executionId, Status = status, ReviewNote = reviewNote, ReviewedByUserId = reviewedByUserId },
            cancellationToken: cancellationToken));
        return affected == 0 ? null : await GetContentExecutionAsync(organizationId, executionId, cancellationToken);
    }

    public async Task<bool> FinalizeContentPublishAsync(Guid organizationId, Guid executionId, Guid approvalId, bool success, string note, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentContentExecutions
            SET Status = CASE WHEN @Success THEN 'Published' ELSE 'PublishFailed' END,
                ReviewNote = CASE WHEN @Note = '' THEN ReviewNote ELSE @Note END,
                PublishedAt = CASE WHEN @Success THEN CURRENT_TIMESTAMP ELSE PublishedAt END,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @ExecutionId AND OrganizationId = @OrganizationId
              AND PublishApprovalId = @ApprovalId
              AND Status IN ('ApprovedForPublishing', 'Publishing')
            """,
            new { OrganizationId = organizationId, ExecutionId = executionId, ApprovalId = approvalId, Success = success, Note = note },
            transaction,
            cancellationToken: cancellationToken));
        if (affected == 0)
        {
            transaction.Rollback();
            return false;
        }
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentApprovals
            SET Status = CASE WHEN @Success THEN 'Executed' ELSE 'Failed' END,
                ExecutedAt = CURRENT_TIMESTAMP
            WHERE Id = @ApprovalId AND OrganizationId = @OrganizationId AND Status = 'Approved'
            """,
            new { OrganizationId = organizationId, ApprovalId = approvalId, Success = success },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE AgentRuns
            SET Status = CASE WHEN @Success THEN 'Completed' ELSE 'Failed' END,
                ErrorCode = CASE WHEN @Success THEN '' ELSE 'content_publish_failed' END,
                ErrorMessage = CASE WHEN @Success THEN '' ELSE @Note END,
                CompletedAt = CURRENT_TIMESTAMP,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = (SELECT RunId FROM AgentContentExecutions WHERE Id = @ExecutionId)
              AND OrganizationId = @OrganizationId
            """,
            new { OrganizationId = organizationId, ExecutionId = executionId, Success = success, Note = note },
            transaction,
            cancellationToken: cancellationToken));
        transaction.Commit();
        return true;
    }

    public async Task<IReadOnlyList<AgentImpactMeasurement>> GetImpactMeasurementsAsync(Guid organizationId, string? status = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return (await connection.QueryAsync<AgentImpactMeasurement>(new CommandDefinition(
            """
            SELECT Id, OrganizationId, RecommendationId, BaselineRunId, MeasurementRunId,
                   Status, Outcome, MonitoringWindowDays, BaselineCapturedAt, MeasurementDueAt,
                   MeasuredAt, BaselineJson::text AS BaselineJson, FollowupJson::text AS FollowupJson,
                   DeltaJson::text AS DeltaJson, EvidenceJson::text AS EvidenceJson,
                   ReportJson::text AS ReportJson, Confidence, ErrorMessage, CreatedAt, UpdatedAt
            FROM AgentImpactMeasurements
            WHERE OrganizationId = @OrganizationId
              AND (@Status IS NULL OR Status = @Status)
            ORDER BY CASE Status WHEN 'Pending' THEN 0 WHEN 'WaitingForData' THEN 1 ELSE 2 END,
                     MeasurementDueAt, UpdatedAt DESC
            LIMIT @Limit
            """,
            new
            {
                OrganizationId = organizationId,
                Status = string.IsNullOrWhiteSpace(status) ? null : status,
                Limit = Math.Clamp(limit, 1, 200)
            },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<AgentImpactMeasurement>> GetDueImpactMeasurementsAsync(DateTime asOf, Guid? organizationId = null, int limit = 200, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return (await connection.QueryAsync<AgentImpactMeasurement>(new CommandDefinition(
            """
            SELECT Id, OrganizationId, RecommendationId, BaselineRunId, MeasurementRunId,
                   Status, Outcome, MonitoringWindowDays, BaselineCapturedAt, MeasurementDueAt,
                   MeasuredAt, BaselineJson::text AS BaselineJson, FollowupJson::text AS FollowupJson,
                   DeltaJson::text AS DeltaJson, EvidenceJson::text AS EvidenceJson,
                   ReportJson::text AS ReportJson, Confidence, ErrorMessage, CreatedAt, UpdatedAt
            FROM AgentImpactMeasurements
            WHERE Status IN ('Pending', 'WaitingForData')
              AND MeasurementDueAt <= @AsOf
              AND (@OrganizationId IS NULL OR OrganizationId = @OrganizationId)
            ORDER BY MeasurementDueAt
            LIMIT @Limit
            """,
            new { AsOf = asOf, OrganizationId = organizationId, Limit = Math.Clamp(limit, 1, 500) },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<AgentImpactMeasurement?> GetImpactMeasurementByRecommendationAsync(Guid organizationId, Guid recommendationId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<AgentImpactMeasurement>(new CommandDefinition(
            """
            SELECT Id, OrganizationId, RecommendationId, BaselineRunId, MeasurementRunId,
                   Status, Outcome, MonitoringWindowDays, BaselineCapturedAt, MeasurementDueAt,
                   MeasuredAt, BaselineJson::text AS BaselineJson, FollowupJson::text AS FollowupJson,
                   DeltaJson::text AS DeltaJson, EvidenceJson::text AS EvidenceJson,
                   ReportJson::text AS ReportJson, Confidence, ErrorMessage, CreatedAt, UpdatedAt
            FROM AgentImpactMeasurements
            WHERE OrganizationId = @OrganizationId AND RecommendationId = @RecommendationId
            """,
            new { OrganizationId = organizationId, RecommendationId = recommendationId },
            cancellationToken: cancellationToken));
    }

    public async Task<AgentImpactMeasurement> CreateImpactMeasurementAsync(AgentImpactMeasurement measurement, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<AgentImpactMeasurement>(new CommandDefinition(
            """
            INSERT INTO AgentImpactMeasurements
                (OrganizationId, RecommendationId, BaselineRunId, Status, Outcome,
                 MonitoringWindowDays, BaselineCapturedAt, MeasurementDueAt, BaselineJson,
                 EvidenceJson, ReportJson, Confidence, ErrorMessage)
            SELECT @OrganizationId, recommendation.Id, @BaselineRunId, @Status, @Outcome,
                   @MonitoringWindowDays, @BaselineCapturedAt, @MeasurementDueAt,
                   @BaselineJson::jsonb, @EvidenceJson::jsonb, @ReportJson::jsonb,
                   @Confidence, @ErrorMessage
            FROM AgentRecommendations recommendation
            WHERE recommendation.Id = @RecommendationId
              AND recommendation.OrganizationId = @OrganizationId
              AND recommendation.Status = 'Implemented'
              AND EXISTS (
                  SELECT 1 FROM AgentRuns run
                  WHERE run.Id = @BaselineRunId AND run.OrganizationId = @OrganizationId
              )
            ON CONFLICT (OrganizationId, RecommendationId)
            DO UPDATE SET UpdatedAt = AgentImpactMeasurements.UpdatedAt
            RETURNING Id, OrganizationId, RecommendationId, BaselineRunId, MeasurementRunId,
                      Status, Outcome, MonitoringWindowDays, BaselineCapturedAt, MeasurementDueAt,
                      MeasuredAt, BaselineJson::text AS BaselineJson, FollowupJson::text AS FollowupJson,
                      DeltaJson::text AS DeltaJson, EvidenceJson::text AS EvidenceJson,
                      ReportJson::text AS ReportJson, Confidence, ErrorMessage, CreatedAt, UpdatedAt
            """,
            measurement,
            cancellationToken: cancellationToken));
    }

    public async Task<AgentImpactMeasurement?> UpdateImpactMeasurementAsync(AgentImpactMeasurement measurement, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<AgentImpactMeasurement>(new CommandDefinition(
            """
            UPDATE AgentImpactMeasurements
            SET MeasurementRunId = CASE
                    WHEN @MeasurementRunId IS NULL THEN MeasurementRunId
                    WHEN EXISTS (
                        SELECT 1 FROM AgentRuns run
                        WHERE run.Id = @MeasurementRunId AND run.OrganizationId = @OrganizationId
                    ) THEN @MeasurementRunId
                    ELSE MeasurementRunId
                END,
                Status = @Status,
                Outcome = @Outcome,
                MeasuredAt = @MeasuredAt,
                FollowupJson = @FollowupJson::jsonb,
                DeltaJson = @DeltaJson::jsonb,
                EvidenceJson = @EvidenceJson::jsonb,
                ReportJson = @ReportJson::jsonb,
                Confidence = @Confidence,
                ErrorMessage = @ErrorMessage,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @Id AND OrganizationId = @OrganizationId
            RETURNING Id, OrganizationId, RecommendationId, BaselineRunId, MeasurementRunId,
                      Status, Outcome, MonitoringWindowDays, BaselineCapturedAt, MeasurementDueAt,
                      MeasuredAt, BaselineJson::text AS BaselineJson, FollowupJson::text AS FollowupJson,
                      DeltaJson::text AS DeltaJson, EvidenceJson::text AS EvidenceJson,
                      ReportJson::text AS ReportJson, Confidence, ErrorMessage, CreatedAt, UpdatedAt
            """,
            measurement,
            cancellationToken: cancellationToken));
    }

    private async Task<IReadOnlyList<AgentRecommendation>> GetRecommendationsByIdAsync(Guid organizationId, Guid recommendationId, CancellationToken cancellationToken)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT r.Id, r.OrganizationId, r.RunId, r.FindingId, r.ApprovalId, r.AgentKey,
                   r.RecommendationType, r.Category, r.Title, r.Summary, r.Rationale,
                   r.TargetType, r.TargetKey, r.EvidenceJson::text AS EvidenceJson,
                   r.ActionPlanJson::text AS ActionPlanJson, r.ValidationPlanJson::text AS ValidationPlanJson,
                   r.ExpectedImpact, r.ImpactScore, r.EffortScore, r.UrgencyScore, r.GoalAlignmentScore,
                   r.Confidence, r.PriorityScore, r.Status, r.AssignedToUserId,
                   COALESCE(u.DisplayName, u.Email, '') AS AssignedToName, r.RejectionReason,
                   r.DeduplicationKey, r.CreatedAt, r.UpdatedAt, r.ApprovedAt, r.AssignedAt, r.ImplementedAt
            FROM AgentRecommendations r
            LEFT JOIN Users u ON u.Id = r.AssignedToUserId AND u.OrganizationId = r.OrganizationId
            WHERE r.OrganizationId = @OrganizationId AND r.Id = @RecommendationId
            """,
            new { OrganizationId = organizationId, RecommendationId = recommendationId },
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<AgentRecommendation>(command)).AsList();
    }

    private static string ResolveOverviewStatus(AgentOverviewItem item)
    {
        if (!item.IsEnabled) return "Disabled";
        if (item.PendingApprovalCount > 0) return AgentRunStatuses.WaitingForApproval;
        if (item.ActiveRunCount > 0)
        {
            return string.IsNullOrWhiteSpace(item.LastRunStatus) ? AgentRunStatuses.Running : item.LastRunStatus;
        }
        if (item.OpenFindingCount > 0) return "NeedsAttention";
        if (item.LastRunStatus == AgentRunStatuses.Failed) return AgentRunStatuses.Failed;
        return "Ready";
    }
}
