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
            SELECT Id, OrganizationId, StripeSubscriptionId, PlanKey, Status,
                   CurrentPeriodStart, CurrentPeriodEnd, CancelAtPeriodEnd, CreatedAt, UpdatedAt
            FROM Subscriptions
            WHERE OrganizationId = @OrganizationId
            ORDER BY UpdatedAt DESC
            LIMIT 1
            """,
            new { OrganizationId = organizationId });
    }

    public async Task<IEnumerable<Invoice>> GetInvoicesAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<Invoice>(
            """
            SELECT Id, OrganizationId, StripeInvoiceId, AmountDueCents, AmountPaidCents,
                   Currency, Status, HostedInvoiceUrl, IssuedAt, CreatedAt
            FROM Invoices
            WHERE OrganizationId = @OrganizationId
            ORDER BY IssuedAt DESC NULLS LAST, CreatedAt DESC
            """,
            new { OrganizationId = organizationId });
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
}
