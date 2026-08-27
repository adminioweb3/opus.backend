using Citationly.Application.Interfaces;
using Dapper;

namespace Citationly.Infrastructure.Services;

public sealed class EntitlementService : IEntitlementService
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public EntitlementService(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<string> GetPlanKeyAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        // A real Subscription row (once Stripe is wired in) is the authoritative source;
        // Organizations.PlanType is the fallback for orgs with no billing record yet.
        var subscriptionPlan = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT PlanKey FROM Subscriptions
            WHERE OrganizationId = @OrganizationId AND Status IN ('active', 'trialing')
            ORDER BY UpdatedAt DESC
            LIMIT 1
            """,
            new { OrganizationId = organizationId });

        if (!string.IsNullOrWhiteSpace(subscriptionPlan))
        {
            return subscriptionPlan;
        }

        var orgPlanType = await connection.ExecuteScalarAsync<string?>(
            "SELECT PlanType FROM Organizations WHERE Id = @OrganizationId",
            new { OrganizationId = organizationId });

        return string.IsNullOrWhiteSpace(orgPlanType) ? "Trial" : orgPlanType;
    }

    public async Task<bool> CanUseFeatureAsync(Guid organizationId, string featureKey, CancellationToken cancellationToken = default)
    {
        var planKey = await GetPlanKeyAsync(organizationId, cancellationToken);
        using var connection = _dbConnectionFactory.CreateConnection();

        var row = await connection.QueryFirstOrDefaultAsync<PlanLimitRow>(
            "SELECT LimitValue FROM PlanLimits WHERE PlanKey = @PlanKey AND FeatureKey = @FeatureKey",
            new { PlanKey = planKey, FeatureKey = featureKey });

        // No row for this plan/feature pair - deny by default rather than silently allowing
        // a feature nobody explicitly granted to this plan. An explicit NULL LimitValue on an
        // existing row means "unlimited/allowed".
        if (row is null) return false;
        return row.LimitValue is null || row.LimitValue >= 1;
    }

    public async Task<long?> GetPlanLimitValueAsync(Guid organizationId, string featureKey, CancellationToken cancellationToken = default)
    {
        var planKey = await GetPlanKeyAsync(organizationId, cancellationToken);
        using var connection = _dbConnectionFactory.CreateConnection();

        return await connection.ExecuteScalarAsync<long?>(
            "SELECT LimitValue FROM PlanLimits WHERE PlanKey = @PlanKey AND FeatureKey = @FeatureKey",
            new { PlanKey = planKey, FeatureKey = featureKey });
    }

    public async Task<UsageQuotaStatus> CheckQuotaAsync(Guid organizationId, string metricKey, CancellationToken cancellationToken = default)
    {
        var limit = await GetPlanLimitValueAsync(organizationId, metricKey, cancellationToken);
        using var connection = _dbConnectionFactory.CreateConnection();

        var (periodStart, _) = GetCurrentDailyPeriod();
        var currentUsage = await connection.ExecuteScalarAsync<long?>(
            "SELECT Count FROM UsageCounters WHERE OrganizationId = @OrganizationId AND MetricKey = @MetricKey AND PeriodStart = @PeriodStart",
            new { OrganizationId = organizationId, MetricKey = metricKey, PeriodStart = periodStart }) ?? 0;

        var withinLimit = limit is null || currentUsage < limit;
        return new UsageQuotaStatus(withinLimit, currentUsage, limit);
    }

    public async Task ConsumeUsageAsync(Guid organizationId, string metricKey, long amount = 1, CancellationToken cancellationToken = default)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Usage amount must be positive.");

        var (periodStart, periodEnd) = GetCurrentDailyPeriod();
        using var connection = _dbConnectionFactory.CreateConnection();

        await connection.ExecuteAsync(
            """
            INSERT INTO UsageCounters (OrganizationId, MetricKey, PeriodStart, PeriodEnd, Count, UpdatedAt)
            VALUES (@OrganizationId, @MetricKey, @PeriodStart, @PeriodEnd, @Amount, CURRENT_TIMESTAMP)
            ON CONFLICT (OrganizationId, MetricKey, PeriodStart)
            DO UPDATE SET Count = UsageCounters.Count + @Amount, UpdatedAt = CURRENT_TIMESTAMP
            """,
            new { OrganizationId = organizationId, MetricKey = metricKey, PeriodStart = periodStart, PeriodEnd = periodEnd, Amount = amount });
    }

    public async Task<UsageQuotaStatus> TryConsumeUsageAsync(Guid organizationId, string metricKey, long amount = 1, CancellationToken cancellationToken = default)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Usage amount must be positive.");

        var limit = await GetPlanLimitValueAsync(organizationId, metricKey, cancellationToken);
        var (periodStart, periodEnd) = GetCurrentDailyPeriod();
        using var connection = _dbConnectionFactory.CreateConnection();

        var reservedCount = await connection.ExecuteScalarAsync<long?>(
            """
            WITH reserved AS (
                INSERT INTO UsageCounters (OrganizationId, MetricKey, PeriodStart, PeriodEnd, Count, UpdatedAt)
                SELECT @OrganizationId, @MetricKey, @PeriodStart, @PeriodEnd, @Amount, CURRENT_TIMESTAMP
                WHERE @Limit IS NULL OR @Amount <= @Limit
                ON CONFLICT (OrganizationId, MetricKey, PeriodStart)
                DO UPDATE SET Count = UsageCounters.Count + @Amount, PeriodEnd = @PeriodEnd, UpdatedAt = CURRENT_TIMESTAMP
                WHERE @Limit IS NULL OR UsageCounters.Count + @Amount <= @Limit
                RETURNING Count
            )
            SELECT Count FROM reserved
            """,
            new { OrganizationId = organizationId, MetricKey = metricKey, PeriodStart = periodStart, PeriodEnd = periodEnd, Amount = amount, Limit = limit });

        if (reservedCount.HasValue)
        {
            return new UsageQuotaStatus(true, reservedCount.Value, limit);
        }

        var currentUsage = await connection.ExecuteScalarAsync<long?>(
            "SELECT Count FROM UsageCounters WHERE OrganizationId = @OrganizationId AND MetricKey = @MetricKey AND PeriodStart = @PeriodStart",
            new { OrganizationId = organizationId, MetricKey = metricKey, PeriodStart = periodStart }) ?? 0;

        return new UsageQuotaStatus(false, currentUsage, limit);
    }

    private static (DateTime PeriodStart, DateTime PeriodEnd) GetCurrentDailyPeriod()
    {
        var todayUtc = DateTime.UtcNow.Date;
        return (todayUtc, todayUtc.AddDays(1));
    }

    private sealed class PlanLimitRow
    {
        public long? LimitValue { get; set; }
    }
}
