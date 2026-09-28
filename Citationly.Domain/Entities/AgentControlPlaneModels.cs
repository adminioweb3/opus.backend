namespace Citationly.Domain.Entities;

public static class CitationlyAgentKeys
{
    public const string VisibilityMonitor = "visibility-monitor";
    public const string IntelligenceAnalyst = "intelligence-analyst";
    public const string GeoStrategy = "geo-strategy";
    public const string ContentExecution = "content-execution";
    public const string ImpactReporting = "impact-reporting";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        VisibilityMonitor,
        IntelligenceAnalyst,
        GeoStrategy,
        ContentExecution,
        ImpactReporting
    };
}

public static class AgentAutonomyLevels
{
    public const string Observe = "Observe";
    public const string Assist = "Assist";
    public const string Autopilot = "Autopilot";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Observe,
        Assist,
        Autopilot
    };
}

public static class AgentRunStatuses
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string WaitingForApproval = "WaitingForApproval";
    public const string CancellationRequested = "CancellationRequested";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public static class AgentApprovalStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Expired = "Expired";
}

public static class AgentRecommendationStatuses
{
    public const string AwaitingApproval = "AwaitingApproval";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Assigned = "Assigned";
    public const string InProgress = "InProgress";
    public const string Implemented = "Implemented";
    public const string Dismissed = "Dismissed";
}

public static class AgentContentExecutionStatuses
{
    public const string Preparing = "Preparing";
    public const string NeedsEvidence = "NeedsEvidence";
    public const string ReadyForReview = "ReadyForReview";
    public const string AwaitingPublishApproval = "AwaitingPublishApproval";
    public const string ApprovedForPublishing = "ApprovedForPublishing";
    public const string Publishing = "Publishing";
    public const string Published = "Published";
    public const string Rejected = "Rejected";
    public const string PublishFailed = "PublishFailed";
    public const string Failed = "Failed";
}

public static class AgentImpactMeasurementStatuses
{
    public const string Pending = "Pending";
    public const string WaitingForData = "WaitingForData";
    public const string NeedsBaseline = "NeedsBaseline";
    public const string Measured = "Measured";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public static class AgentImpactOutcomes
{
    public const string Pending = "Pending";
    public const string Improved = "Improved";
    public const string Neutral = "Neutral";
    public const string Regressed = "Regressed";
    public const string Inconclusive = "Inconclusive";
}

public static class AgentStrategyGoals
{
    public const string Balanced = "Balanced";
    public const string GrowVisibility = "GrowVisibility";
    public const string ImproveCitations = "ImproveCitations";
    public const string DefendCompetitors = "DefendCompetitors";
    public const string ImproveBrandAccuracy = "ImproveBrandAccuracy";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Balanced,
        GrowVisibility,
        ImproveCitations,
        DefendCompetitors,
        ImproveBrandAccuracy
    };
}

public sealed class AgentDefinition
{
    public string AgentKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string DefaultAutonomyLevel { get; set; } = AgentAutonomyLevels.Assist;
    public string DefaultTriggerType { get; set; } = "Event";
    public string DefaultSchedule { get; set; } = string.Empty;
    public string CapabilitiesJson { get; set; } = "[]";
    public bool IsAvailable { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentSetting
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string AgentKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string AutonomyLevel { get; set; } = AgentAutonomyLevels.Assist;
    public int MaxRunsPerDay { get; set; } = 5;
    public long MaxCostMicroUsdPerRun { get; set; } = 100_000;
    public string AllowedActionsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentSchedule
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string AgentKey { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "Event";
    public string TriggerExpression { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "UTC";
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentRun
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string AgentKey { get; set; } = string.Empty;
    public Guid? InitiatedByUserId { get; set; }
    public Guid? ParentRunId { get; set; }
    public string TriggerType { get; set; } = "Event";
    public string TriggerReference { get; set; } = string.Empty;
    public string Status { get; set; } = AgentRunStatuses.Queued;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string InputJson { get; set; } = "{}";
    public string OutputJson { get; set; } = "{}";
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public long CostMicroUsd { get; set; }
    public int Attempt { get; set; } = 1;
    public int MaxAttempts { get; set; } = 3;
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelRequestedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentRunEvent
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid RunId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentFinding
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? RunId { get; set; }
    public string AgentKey { get; set; } = string.Empty;
    public string FindingType { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityIdsJson { get; set; } = "[]";
    public string EvidenceJson { get; set; } = "[]";
    public DateTime? ObservationStartedAt { get; set; }
    public DateTime? ObservationEndedAt { get; set; }
    public decimal Confidence { get; set; }
    public string DeduplicationKey { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

public sealed class AgentApproval
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid RunId { get; set; }
    public Guid? FindingId { get; set; }
    public string AgentKey { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string RiskLevel { get; set; } = "Medium";
    public string Status { get; set; } = AgentApprovalStatuses.Pending;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string DecisionNote { get; set; } = string.Empty;
    public DateTime? ExecutedAt { get; set; }
}

public sealed class AgentRecommendation
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid RunId { get; set; }
    public Guid FindingId { get; set; }
    public Guid? ApprovalId { get; set; }
    public string AgentKey { get; set; } = CitationlyAgentKeys.GeoStrategy;
    public string RecommendationType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetKey { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public string ActionPlanJson { get; set; } = "[]";
    public string ValidationPlanJson { get; set; } = "{}";
    public string ExpectedImpact { get; set; } = string.Empty;
    public int ImpactScore { get; set; }
    public int EffortScore { get; set; }
    public int UrgencyScore { get; set; }
    public int GoalAlignmentScore { get; set; }
    public decimal Confidence { get; set; }
    public decimal PriorityScore { get; set; }
    public string Status { get; set; } = AgentRecommendationStatuses.AwaitingApproval;
    public Guid? AssignedToUserId { get; set; }
    public string AssignedToName { get; set; } = string.Empty;
    public string RejectionReason { get; set; } = string.Empty;
    public string DeduplicationKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? ImplementedAt { get; set; }
}

public sealed class AgentStrategyPreference
{
    public Guid OrganizationId { get; set; }
    public string PrimaryGoal { get; set; } = AgentStrategyGoals.Balanced;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentContentExecution
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid RunId { get; set; }
    public Guid RecommendationId { get; set; }
    public Guid? ContentDraftId { get; set; }
    public Guid? KnowledgeBaseId { get; set; }
    public Guid? PublishApprovalId { get; set; }
    public string Status { get; set; } = AgentContentExecutionStatuses.Preparing;
    public string BriefJson { get; set; } = "{}";
    public string EvidenceJson { get; set; } = "[]";
    public string ReviewDiffJson { get; set; } = "{}";
    public string PolicyChecksJson { get; set; } = "[]";
    public string ReviewNote { get; set; } = string.Empty;
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentImpactMeasurement
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid RecommendationId { get; set; }
    public Guid BaselineRunId { get; set; }
    public Guid? MeasurementRunId { get; set; }
    public string Status { get; set; } = AgentImpactMeasurementStatuses.Pending;
    public string Outcome { get; set; } = AgentImpactOutcomes.Pending;
    public int MonitoringWindowDays { get; set; } = 14;
    public DateTime BaselineCapturedAt { get; set; } = DateTime.UtcNow;
    public DateTime MeasurementDueAt { get; set; }
    public DateTime? MeasuredAt { get; set; }
    public string BaselineJson { get; set; } = "{}";
    public string FollowupJson { get; set; } = "{}";
    public string DeltaJson { get; set; } = "{}";
    public string EvidenceJson { get; set; } = "{}";
    public string ReportJson { get; set; } = "{}";
    public decimal Confidence { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AgentOverviewItem
{
    public string AgentKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string CapabilitiesJson { get; set; } = "[]";
    public bool IsEnabled { get; set; }
    public string AutonomyLevel { get; set; } = AgentAutonomyLevels.Assist;
    public int MaxRunsPerDay { get; set; }
    public long MaxCostMicroUsdPerRun { get; set; }
    public string AllowedActionsJson { get; set; } = "[]";
    public string Status { get; set; } = "Ready";
    public int ActiveRunCount { get; set; }
    public int OpenFindingCount { get; set; }
    public int PendingApprovalCount { get; set; }
    public string LastRunStatus { get; set; } = string.Empty;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
}

public sealed class AgentActivityItem
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string AgentKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string Status { get; set; } = string.Empty;
    public Guid? RunId { get; set; }
    public Guid? ReferenceId { get; set; }
    public string DataJson { get; set; } = "{}";
    public DateTime OccurredAt { get; set; }
}
