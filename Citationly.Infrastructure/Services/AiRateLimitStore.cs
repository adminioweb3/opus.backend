using Citationly.Application.Interfaces;
using Dapper;

namespace Citationly.Infrastructure.Services;

public sealed class AiRateLimitStore : IAiRateLimitStore
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public AiRateLimitStore(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<UsageQuotaStatus> TryConsumeAsync(
        string scopeKey,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        long limit,
        long amount = 1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scopeKey)) throw new ArgumentException("Scope key is required.", nameof(scopeKey));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "Rate-limit ceiling must be positive.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Rate-limit amount must be positive.");

        using var connection = _dbConnectionFactory.CreateConnection();

        var reservedCount = await connection.ExecuteScalarAsync<long?>(
            """
            WITH reserved AS (
                INSERT INTO AiRateLimitCounters (ScopeKey, PeriodStart, PeriodEnd, Count, UpdatedAt)
                SELECT @ScopeKey, @PeriodStart, @PeriodEnd, @Amount, CURRENT_TIMESTAMP
                WHERE @Amount <= @Limit
                ON CONFLICT (ScopeKey, PeriodStart)
                DO UPDATE SET Count = AiRateLimitCounters.Count + @Amount, PeriodEnd = @PeriodEnd, UpdatedAt = CURRENT_TIMESTAMP
                WHERE AiRateLimitCounters.Count + @Amount <= @Limit
                RETURNING Count
            )
            SELECT Count FROM reserved
            """,
            new { ScopeKey = scopeKey, PeriodStart = periodStartUtc, PeriodEnd = periodEndUtc, Amount = amount, Limit = limit });

        if (reservedCount.HasValue)
        {
            return new UsageQuotaStatus(true, reservedCount.Value, limit);
        }

        var currentUsage = await connection.ExecuteScalarAsync<long?>(
            """
            SELECT Count
            FROM AiRateLimitCounters
            WHERE ScopeKey = @ScopeKey AND PeriodStart = @PeriodStart
            """,
            new { ScopeKey = scopeKey, PeriodStart = periodStartUtc }) ?? 0;

        return new UsageQuotaStatus(false, currentUsage, limit);
    }
}
