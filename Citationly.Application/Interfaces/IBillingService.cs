namespace Citationly.Application.Interfaces;

/// <summary>
/// Stripe-facing billing operations. Scaffolded ahead of a real Stripe account per the
/// roadmap's Phase 1 B1 decision ("build scaffolding now, wire keys later") - IsConfigured
/// is false until Stripe:ApiKey is a real key (not the "${STRIPE_API_KEY}" placeholder in
/// appsettings.json), and every other method throws BillingNotConfiguredException until then.
/// Read-only billing data (current subscription/invoices/payment methods) does not depend on
/// this - it comes straight from the local DB via IBillingRepository regardless of whether
/// Stripe is configured.
/// </summary>
public interface IBillingService
{
    bool IsConfigured { get; }

    Task<string> CreateCheckoutSessionAsync(Guid organizationId, string planKey, string successUrl, string cancelUrl, CancellationToken cancellationToken = default);

    Task<string> CreateBillingPortalSessionAsync(Guid organizationId, string returnUrl, CancellationToken cancellationToken = default);

    /// <summary>Verifies the Stripe webhook signature and applies the event to local billing
    /// state (Subscriptions/Invoices/PaymentMethods, and a PlanType sync onto Organizations).</summary>
    Task HandleWebhookEventAsync(string requestBody, string stripeSignatureHeader, CancellationToken cancellationToken = default);
}

public sealed class BillingNotConfiguredException : InvalidOperationException
{
    public BillingNotConfiguredException()
        : base("Billing is not configured yet. Set Stripe:ApiKey (and Stripe:WebhookSecret, Stripe:PriceIds) via environment variables to enable real subscriptions.")
    {
    }
}
