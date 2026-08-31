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
    Task UpsertCashfreeSubscriptionAsync(Subscription subscription);
    Task UpsertInvoiceAsync(Invoice invoice);
    Task UpsertPaymentMethodAsync(PaymentMethod paymentMethod);

    /// <summary>Mirrors a subscription's plan onto Organizations.PlanType so existing
    /// PlanType-reading code (entitlements, recurring jobs) sees the change immediately -
    /// Subscriptions stays the source of truth, this is a denormalized read cache.</summary>
    Task SyncOrganizationPlanTypeAsync(Guid organizationId, string planKey);

    Task<Guid?> GetOrganizationIdByStripeCustomerIdAsync(string stripeCustomerId);
    Task<Guid?> GetOrganizationIdByCashfreeSubscriptionIdAsync(string cashfreeSubscriptionId);
    Task<Subscription?> GetCashfreeSubscriptionAsync(string cashfreeSubscriptionId);
    Task<Subscription?> GetCurrentCashfreeSubscriptionAsync(Guid organizationId);
    Task<IReadOnlyList<Subscription>> GetCashfreeSubscriptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(Guid OrganizationId, string StripeCustomerId)>> GetOrganizationsWithStripeCustomersAsync(CancellationToken cancellationToken = default);

    /// <summary>Claims a Stripe event for processing. Returns false for completed, concurrent,
    /// or payload-mismatched duplicate deliveries; failed events may be retried.</summary>
    Task<bool> TryBeginWebhookEventAsync(string stripeEventId, string payloadHash, string eventType, CancellationToken cancellationToken = default);
    Task CompleteWebhookEventAsync(string stripeEventId, CancellationToken cancellationToken = default);
    Task FailWebhookEventAsync(string stripeEventId, string failureReason, CancellationToken cancellationToken = default);

    Task<bool> TryBeginCashfreeWebhookEventAsync(string eventId, string payloadHash, string eventType, CancellationToken cancellationToken = default);
    Task CompleteCashfreeWebhookEventAsync(string eventId, CancellationToken cancellationToken = default);
    Task FailCashfreeWebhookEventAsync(string eventId, string failureReason, CancellationToken cancellationToken = default);
}
