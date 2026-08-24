namespace Citationly.Application.Interfaces;

/// <summary>
/// The single, server-side authority for plan-gated features and metered usage.
/// Callers must never re-implement a "planType == X" check inline (see the audit's
/// commercial-blocker finding on frontend-only/ad-hoc plan checks) - every feature
/// gate and every AI-cost-incurring action should go through this service instead,
/// so limits live in one place (PlanLimits) and are enforced the same way everywhere.
/// </summary>
public interface IEntitlementService
{
    /// <summary>Resolves the organization's active plan key (from Subscriptions if a real
    /// billing record exists, otherwise falls back to Organizations.PlanType).</summary>
    Task<string> GetPlanKeyAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>True/false feature gates (e.g. "regions_summary" is Enterprise-only).</summary>
    Task<bool> CanUseFeatureAsync(Guid organizationId, string featureKey, CancellationToken cancellationToken = default);

    /// <summary>Raw numeric plan-limit lookup (e.g. "recurring_scan_interval_days") for callers
    /// that need the configured value itself, not a pass/fail check against usage. Null means the
    /// plan/feature pair has no explicit limit (either unlimited, or not configured for this plan).</summary>
    Task<long?> GetPlanLimitValueAsync(Guid organizationId, string featureKey, CancellationToken cancellationToken = default);

    /// <summary>Checks a metered quota (e.g. "ai_calls_per_day") without consuming it.</summary>
    Task<UsageQuotaStatus> CheckQuotaAsync(Guid organizationId, string metricKey, CancellationToken cancellationToken = default);

    /// <summary>Records usage against a metered quota. Call after CheckQuotaAsync confirms
    /// the action is allowed - this does not itself enforce the limit.</summary>
    Task ConsumeUsageAsync(Guid organizationId, string metricKey, long amount = 1, CancellationToken cancellationToken = default);
}

public sealed record UsageQuotaStatus(bool IsWithinLimit, long CurrentUsage, long? Limit);
