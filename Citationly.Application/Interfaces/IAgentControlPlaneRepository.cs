using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IAgentControlPlaneRepository
{
    Task EnsureDefaultsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentOverviewItem>> GetOverviewAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentActivityItem>> GetActivityAsync(Guid organizationId, int limit = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentApproval>> GetApprovalsAsync(Guid organizationId, string? status = null, int limit = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentSchedule>> GetSchedulesAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentRun>> GetRunsAsync(Guid organizationId, string? agentKey = null, string? status = null, int limit = 50, CancellationToken cancellationToken = default);
    Task<AgentRun?> GetRunAsync(Guid organizationId, Guid runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentFinding>> GetFindingsAsync(Guid organizationId, string? status = null, int limit = 50, CancellationToken cancellationToken = default);
    Task<int> CountRunsSinceAsync(Guid organizationId, string agentKey, DateTime since, CancellationToken cancellationToken = default);
    Task<AgentSetting?> UpdateSettingAsync(AgentSetting setting, CancellationToken cancellationToken = default);
    Task<AgentSchedule?> UpsertScheduleAsync(AgentSchedule schedule, CancellationToken cancellationToken = default);
    Task<AgentApproval?> DecideApprovalAsync(Guid organizationId, Guid approvalId, Guid decidedByUserId, string decision, string decisionNote, CancellationToken cancellationToken = default);
    Task<bool> CancelRunAsync(Guid organizationId, Guid runId, Guid requestedByUserId, CancellationToken cancellationToken = default);
    Task<AgentRun?> RetryRunAsync(Guid organizationId, Guid runId, Guid initiatedByUserId, CancellationToken cancellationToken = default);
    Task<AgentRun> CreateRunAsync(AgentRun run, CancellationToken cancellationToken = default);
    Task<AgentRun?> UpdateRunExecutionAsync(AgentRun run, CancellationToken cancellationToken = default);
    Task<AgentRunEvent> AppendRunEventAsync(AgentRunEvent runEvent, CancellationToken cancellationToken = default);
    Task<AgentFinding> UpsertFindingAsync(AgentFinding finding, CancellationToken cancellationToken = default);
    Task<AgentApproval> CreateApprovalAsync(AgentApproval approval, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentRecommendation>> GetRecommendationsAsync(Guid organizationId, string? status = null, int limit = 100, CancellationToken cancellationToken = default);
    Task<AgentRecommendation?> GetRecommendationAsync(Guid organizationId, Guid recommendationId, CancellationToken cancellationToken = default);
    Task<AgentRecommendation> UpsertRecommendationAsync(AgentRecommendation recommendation, CancellationToken cancellationToken = default);
    Task<AgentRecommendation?> LinkRecommendationApprovalAsync(Guid organizationId, Guid recommendationId, Guid approvalId, CancellationToken cancellationToken = default);
    Task<AgentRecommendation?> AssignRecommendationAsync(Guid organizationId, Guid recommendationId, Guid? assignedToUserId, CancellationToken cancellationToken = default);
    Task<AgentRecommendation?> UpdateRecommendationStatusAsync(Guid organizationId, Guid recommendationId, string status, string note, CancellationToken cancellationToken = default);
    Task<AgentStrategyPreference> GetStrategyPreferenceAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<AgentStrategyPreference> UpsertStrategyPreferenceAsync(AgentStrategyPreference preference, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentContentExecution>> GetContentExecutionsAsync(Guid organizationId, string? status = null, int limit = 100, CancellationToken cancellationToken = default);
    Task<AgentContentExecution?> GetContentExecutionAsync(Guid organizationId, Guid executionId, CancellationToken cancellationToken = default);
    Task<AgentContentExecution?> GetContentExecutionByRecommendationAsync(Guid organizationId, Guid recommendationId, CancellationToken cancellationToken = default);
    Task<AgentContentExecution> UpsertContentExecutionAsync(AgentContentExecution execution, CancellationToken cancellationToken = default);
    Task<AgentContentExecution?> LinkContentPublishApprovalAsync(Guid organizationId, Guid executionId, Guid approvalId, CancellationToken cancellationToken = default);
    Task<AgentContentExecution?> UpdateContentExecutionStatusAsync(Guid organizationId, Guid executionId, string status, string reviewNote, Guid? reviewedByUserId = null, CancellationToken cancellationToken = default);
    Task<bool> FinalizeContentPublishAsync(Guid organizationId, Guid executionId, Guid approvalId, bool success, string note, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentImpactMeasurement>> GetImpactMeasurementsAsync(Guid organizationId, string? status = null, int limit = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentImpactMeasurement>> GetDueImpactMeasurementsAsync(DateTime asOf, Guid? organizationId = null, int limit = 200, CancellationToken cancellationToken = default);
    Task<AgentImpactMeasurement?> GetImpactMeasurementByRecommendationAsync(Guid organizationId, Guid recommendationId, CancellationToken cancellationToken = default);
    Task<AgentImpactMeasurement> CreateImpactMeasurementAsync(AgentImpactMeasurement measurement, CancellationToken cancellationToken = default);
    Task<AgentImpactMeasurement?> UpdateImpactMeasurementAsync(AgentImpactMeasurement measurement, CancellationToken cancellationToken = default);
}
