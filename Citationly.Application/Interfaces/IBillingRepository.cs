using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IBillingRepository
{
    Task<Subscription?> GetActiveSubscriptionAsync(Guid organizationId);
    Task<IEnumerable<Invoice>> GetInvoicesAsync(Guid organizationId, int limit = 100);
    Task<IEnumerable<PaymentMethod>> GetPaymentMethodsAsync(Guid organizationId);

    Task<string?> GetStripeCustomerIdAsync(Guid organizationId);
    Task SetStripeCustomerIdAsync(Guid organizationId, string stripeCustomerId);

    Task UpsertSubscriptionAsync(Subscription subscription);
    Task UpsertInvoiceAsync(Invoice invoice);
    Task UpsertPaymentMethodAsync(PaymentMethod paymentMethod);

    /// <summary>Mirrors a subscription's plan onto Organizations.PlanType so existing
    /// PlanType-reading code (entitlements, recurring jobs) sees the change immediately -
    /// Subscriptions stays the source of truth, this is a denormalized read cache.</summary>
    Task SyncOrganizationPlanTypeAsync(Guid organizationId, string planKey);

    Task<Guid?> GetOrganizationIdByStripeCustomerIdAsync(string stripeCustomerId);
}
