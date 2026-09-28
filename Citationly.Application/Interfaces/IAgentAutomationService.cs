using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public static class AgentScanTypes
{
    public const string Visibility = "visibility";
    public const string Citations = "citations";
    public const string Competitors = "competitors";
    public const string BrandPulse = "brand-pulse";
}

public interface IAgentAutomationService
{
    /// <summary>
    /// Runs the organization-scoped monitor against the just-persisted scan and, for important
    /// findings, hands the evidence to the Intelligence Analyst. Implementations must be
    /// idempotent for the same organization, scan type, and scan date.
    /// </summary>
    Task ProcessCompletedScanAsync(
        Guid organizationId,
        string scanType,
        DateOnly scanDate,
        CancellationToken cancellationToken = default);

    Task ProcessApprovedRecommendationAsync(
        Guid organizationId,
        Guid recommendationId,
        Guid initiatedByUserId,
        CancellationToken cancellationToken = default);

    Task<AgentContentExecution?> RequestContentPublishApprovalAsync(
        Guid organizationId,
        Guid executionId,
        Guid initiatedByUserId,
        CancellationToken cancellationToken = default);

    Task ProcessApprovedContentPublishAsync(
        Guid organizationId,
        Guid executionId,
        Guid approvalId,
        CancellationToken cancellationToken = default);

    Task<AgentImpactMeasurement?> ScheduleRecommendationImpactAsync(
        Guid organizationId,
        Guid recommendationId,
        Guid initiatedByUserId,
        int monitoringWindowDays = 14,
        CancellationToken cancellationToken = default);

    Task<int> ProcessDueImpactMeasurementsAsync(
        Guid? organizationId = null,
        CancellationToken cancellationToken = default);
}
