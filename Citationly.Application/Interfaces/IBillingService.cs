namespace Citationly.Application.Interfaces;

/// <summary>
/// Cashfree recurring-subscription operations. Secrets remain server-side; callers only receive
/// the mandate session token required by Cashfree's client-side authorization flow.
/// </summary>
public interface IBillingService
{
    bool IsConfigured { get; }

    Task<CashfreeSubscriptionSession> CreateSubscriptionSessionAsync(Guid organizationId, string planKey, string customerName, string customerEmail, string customerPhone, string returnUrl, CancellationToken cancellationToken = default);

    Task CancelSubscriptionAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>Verifies a Cashfree webhook and applies subscription status changes locally.</summary>
    Task HandleWebhookEventAsync(string requestBody, string signature, string timestamp, CancellationToken cancellationToken = default);

    /// <summary>Reserved for provider reconciliation; returns zero until Cashfree bulk fetch is enabled.</summary>
    Task<int> ReconcileSubscriptionsAsync(CancellationToken cancellationToken = default);
}

public sealed record CashfreeSubscriptionSession(string SubscriptionId, string SessionId, string Status, string Environment);

public sealed class BillingNotConfiguredException : InvalidOperationException
{
    public BillingNotConfiguredException()
        : base("Billing is not configured yet. Set Cashfree:AppId, Cashfree:SecretKey, and the Cashfree plan IDs via environment variables to enable subscriptions.")
    {
    }
}
