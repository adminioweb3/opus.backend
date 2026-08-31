using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class BillingRepository : IBillingRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public BillingRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Subscription?> GetActiveSubscriptionAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Subscription>(
            """
            SELECT Id, OrganizationId, StripeSubscriptionId, CashfreeSubscriptionId, PlanKey, Status,
                   CurrentPeriodStart, CurrentPeriodEnd, CancelAtPeriodEnd, CreatedAt, UpdatedAt
            FROM Subscriptions
            WHERE OrganizationId = @OrganizationId
            ORDER BY UpdatedAt DESC
            LIMIT 1
            """,
            new { OrganizationId = organizationId });
    }

    public async Task<IEnumerable<Invoice>> GetInvoicesAsync(Guid organizationId, int limit = 100)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        limit = Math.Clamp(limit, 1, 500);
        return await connection.QueryAsync<Invoice>(
            """
            SELECT Id, OrganizationId, StripeInvoiceId, AmountDueCents, AmountPaidCents,
                   Currency, Status, HostedInvoiceUrl, IssuedAt, CreatedAt
            FROM Invoices
            WHERE OrganizationId = @OrganizationId
            ORDER BY IssuedAt DESC NULLS LAST, CreatedAt DESC
            LIMIT @Limit
            """,
            new { OrganizationId = organizationId, Limit = limit });
    }

    public async Task<IEnumerable<PaymentMethod>> GetPaymentMethodsAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<PaymentMethod>(
            """
            SELECT Id, OrganizationId, StripePaymentMethodId, Brand, Last4, ExpMonth, ExpYear, IsDefault, CreatedAt
            FROM PaymentMethods
            WHERE OrganizationId = @OrganizationId
            ORDER BY IsDefault DESC, CreatedAt DESC
            """,
            new { OrganizationId = organizationId });
    }

    public async Task<string?> GetStripeCustomerIdAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<string?>(
            "SELECT StripeCustomerId FROM Organizations WHERE Id = @OrganizationId",
            new { OrganizationId = organizationId });
    }

    public async Task SetStripeCustomerIdAsync(Guid organizationId, string stripeCustomerId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE Organizations SET StripeCustomerId = @StripeCustomerId WHERE Id = @OrganizationId",
            new { OrganizationId = organizationId, StripeCustomerId = stripeCustomerId });
    }

    public async Task UpsertSubscriptionAsync(Subscription subscription)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO Subscriptions (OrganizationId, StripeSubscriptionId, PlanKey, Status,
                                        CurrentPeriodStart, CurrentPeriodEnd, CancelAtPeriodEnd, UpdatedAt)
            VALUES (@OrganizationId, @StripeSubscriptionId, @PlanKey, @Status,
                    @CurrentPeriodStart, @CurrentPeriodEnd, @CancelAtPeriodEnd, CURRENT_TIMESTAMP)
            ON CONFLICT (StripeSubscriptionId) DO UPDATE SET
                PlanKey = @PlanKey,
                Status = @Status,
                CurrentPeriodStart = @CurrentPeriodStart,
                CurrentPeriodEnd = @CurrentPeriodEnd,
                CancelAtPeriodEnd = @CancelAtPeriodEnd,
                UpdatedAt = CURRENT_TIMESTAMP
            """,
            subscription);
    }

    public async Task UpsertCashfreeSubscriptionAsync(Subscription subscription)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO Subscriptions (OrganizationId, CashfreeSubscriptionId, PlanKey, Status,
                CurrentPeriodStart, CurrentPeriodEnd, CancelAtPeriodEnd)
            VALUES (@OrganizationId, @CashfreeSubscriptionId, @PlanKey, @Status,
                @CurrentPeriodStart, @CurrentPeriodEnd, @CancelAtPeriodEnd)
            ON CONFLICT (CashfreeSubscriptionId) DO UPDATE SET
                PlanKey = @PlanKey,
                Status = @Status,
                CurrentPeriodStart = @CurrentPeriodStart,
                CurrentPeriodEnd = @CurrentPeriodEnd,
                CancelAtPeriodEnd = @CancelAtPeriodEnd,
                UpdatedAt = CURRENT_TIMESTAMP
            """, subscription);
    }

    public async Task UpsertInvoiceAsync(Invoice invoice)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO Invoices (OrganizationId, StripeInvoiceId, AmountDueCents, AmountPaidCents,
                                   Currency, Status, HostedInvoiceUrl, IssuedAt)
            VALUES (@OrganizationId, @StripeInvoiceId, @AmountDueCents, @AmountPaidCents,
                    @Currency, @Status, @HostedInvoiceUrl, @IssuedAt)
            ON CONFLICT (StripeInvoiceId) DO UPDATE SET
                AmountDueCents = @AmountDueCents,
                AmountPaidCents = @AmountPaidCents,
                Status = @Status,
                HostedInvoiceUrl = @HostedInvoiceUrl,
                IssuedAt = @IssuedAt
            """,
            invoice);
    }

    public async Task UpsertPaymentMethodAsync(PaymentMethod paymentMethod)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO PaymentMethods (OrganizationId, StripePaymentMethodId, Brand, Last4, ExpMonth, ExpYear, IsDefault)
            VALUES (@OrganizationId, @StripePaymentMethodId, @Brand, @Last4, @ExpMonth, @ExpYear, @IsDefault)
            ON CONFLICT (StripePaymentMethodId) DO UPDATE SET
                Brand = @Brand,
                Last4 = @Last4,
                ExpMonth = @ExpMonth,
                ExpYear = @ExpYear,
                IsDefault = @IsDefault
            """,
            paymentMethod);
    }

    public async Task SyncOrganizationPlanTypeAsync(Guid organizationId, string planKey)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE Organizations SET PlanType = @PlanKey WHERE Id = @OrganizationId",
            new { OrganizationId = organizationId, PlanKey = planKey });
    }

    public async Task<Guid?> GetOrganizationIdByStripeCustomerIdAsync(string stripeCustomerId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid?>(
            "SELECT Id FROM Organizations WHERE StripeCustomerId = @StripeCustomerId",
            new { StripeCustomerId = stripeCustomerId });
    }

    public async Task<Guid?> GetOrganizationIdByCashfreeSubscriptionIdAsync(string cashfreeSubscriptionId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid?>(
            "SELECT OrganizationId FROM Subscriptions WHERE CashfreeSubscriptionId = @CashfreeSubscriptionId",
            new { CashfreeSubscriptionId = cashfreeSubscriptionId });
    }

    public async Task<Subscription?> GetCashfreeSubscriptionAsync(string cashfreeSubscriptionId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Subscription>(
            """
            SELECT Id, OrganizationId, StripeSubscriptionId, CashfreeSubscriptionId, PlanKey, Status,
                   CurrentPeriodStart, CurrentPeriodEnd, CancelAtPeriodEnd, CreatedAt, UpdatedAt
            FROM Subscriptions WHERE CashfreeSubscriptionId = @CashfreeSubscriptionId
            """, new { CashfreeSubscriptionId = cashfreeSubscriptionId });
    }

    public async Task<Subscription?> GetCurrentCashfreeSubscriptionAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Subscription>(
            """
            SELECT Id, OrganizationId, StripeSubscriptionId, CashfreeSubscriptionId, PlanKey, Status,
                   CurrentPeriodStart, CurrentPeriodEnd, CancelAtPeriodEnd, CreatedAt, UpdatedAt
            FROM Subscriptions
            WHERE OrganizationId = @OrganizationId AND CashfreeSubscriptionId IS NOT NULL
            ORDER BY UpdatedAt DESC
            LIMIT 1
            """, new { OrganizationId = organizationId });
    }

    public async Task<IReadOnlyList<Subscription>> GetCashfreeSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var subscriptions = await connection.QueryAsync<Subscription>(
            """
            SELECT Id, OrganizationId, StripeSubscriptionId, CashfreeSubscriptionId, PlanKey, Status,
                   CurrentPeriodStart, CurrentPeriodEnd, CancelAtPeriodEnd, CreatedAt, UpdatedAt
            FROM Subscriptions WHERE CashfreeSubscriptionId IS NOT NULL AND CashfreeSubscriptionId <> ''
            """);
        return subscriptions.ToList();
    }

    public async Task<IReadOnlyList<(Guid OrganizationId, string StripeCustomerId)>> GetOrganizationsWithStripeCustomersAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<StripeCustomerRow>(
            "SELECT Id AS OrganizationId, StripeCustomerId FROM Organizations WHERE StripeCustomerId IS NOT NULL AND StripeCustomerId <> ''");
        return rows.Select(row => (row.OrganizationId, row.StripeCustomerId)).ToList();
    }

    public async Task<bool> TryBeginWebhookEventAsync(string stripeEventId, string payloadHash, string eventType, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var claimed = await connection.ExecuteScalarAsync<Guid?>(
            """
            INSERT INTO StripeWebhookEvents (StripeEventId, PayloadHash, EventType, Status, AttemptCount, ReceivedAt, LastAttemptAt)
            VALUES (@StripeEventId, @PayloadHash, @EventType, 'Processing', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (StripeEventId) DO UPDATE SET
                Status = 'Processing',
                AttemptCount = StripeWebhookEvents.AttemptCount + 1,
                LastAttemptAt = CURRENT_TIMESTAMP,
                FailureReason = NULL
            WHERE StripeWebhookEvents.PayloadHash = EXCLUDED.PayloadHash
              AND (StripeWebhookEvents.Status = 'Failed'
                   OR (StripeWebhookEvents.Status = 'Processing' AND StripeWebhookEvents.LastAttemptAt < CURRENT_TIMESTAMP - INTERVAL '5 minutes'))
            RETURNING Id
            """,
            new { StripeEventId = stripeEventId, PayloadHash = payloadHash, EventType = eventType });
        return claimed.HasValue;
    }

    public async Task CompleteWebhookEventAsync(string stripeEventId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            UPDATE StripeWebhookEvents
            SET Status = 'Completed', CompletedAt = CURRENT_TIMESTAMP, FailureReason = NULL
            WHERE StripeEventId = @StripeEventId
            """,
            new { StripeEventId = stripeEventId });
    }

    public async Task FailWebhookEventAsync(string stripeEventId, string failureReason, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            UPDATE StripeWebhookEvents
            SET Status = 'Failed', FailureReason = @FailureReason, LastAttemptAt = CURRENT_TIMESTAMP
            WHERE StripeEventId = @StripeEventId
            """,
            new { StripeEventId = stripeEventId, FailureReason = failureReason.Length <= 2000 ? failureReason : failureReason[..2000] });
    }

    public async Task<bool> TryBeginCashfreeWebhookEventAsync(string eventId, string payloadHash, string eventType, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var claimed = await connection.ExecuteScalarAsync<Guid?>(
            """
            INSERT INTO CashfreeWebhookEvents (CashfreeEventId, PayloadHash, EventType, Status, AttemptCount, ReceivedAt, LastAttemptAt)
            VALUES (@EventId, @PayloadHash, @EventType, 'Processing', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (CashfreeEventId) DO UPDATE SET
                Status = 'Processing', AttemptCount = CashfreeWebhookEvents.AttemptCount + 1,
                LastAttemptAt = CURRENT_TIMESTAMP, FailureReason = NULL
            WHERE CashfreeWebhookEvents.PayloadHash = EXCLUDED.PayloadHash
              AND (CashfreeWebhookEvents.Status = 'Failed'
                   OR (CashfreeWebhookEvents.Status = 'Processing' AND CashfreeWebhookEvents.LastAttemptAt < CURRENT_TIMESTAMP - INTERVAL '5 minutes'))
            RETURNING Id
            """, new { EventId = eventId, PayloadHash = payloadHash, EventType = eventType });
        return claimed.HasValue;
    }

    public async Task CompleteCashfreeWebhookEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync("UPDATE CashfreeWebhookEvents SET Status = 'Completed', CompletedAt = CURRENT_TIMESTAMP, FailureReason = NULL WHERE CashfreeEventId = @EventId", new { EventId = eventId });
    }

    public async Task FailCashfreeWebhookEventAsync(string eventId, string failureReason, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync("UPDATE CashfreeWebhookEvents SET Status = 'Failed', FailureReason = @FailureReason, LastAttemptAt = CURRENT_TIMESTAMP WHERE CashfreeEventId = @EventId",
            new { EventId = eventId, FailureReason = failureReason.Length <= 2000 ? failureReason : failureReason[..2000] });
    }

    private sealed class StripeCustomerRow
    {
        public Guid OrganizationId { get; init; }
        public string StripeCustomerId { get; init; } = string.Empty;
    }
}
