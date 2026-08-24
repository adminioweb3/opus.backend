using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;

namespace Citationly.Infrastructure.Services;

public sealed class StripeBillingService : IBillingService
{
    private readonly IConfiguration _configuration;
    private readonly IBillingRepository _billingRepository;
    private readonly ILogger<StripeBillingService> _logger;
    private readonly string? _apiKey;
    private readonly string? _webhookSecret;

    public StripeBillingService(IConfiguration configuration, IBillingRepository billingRepository, ILogger<StripeBillingService> logger)
    {
        _configuration = configuration;
        _billingRepository = billingRepository;
        _logger = logger;

        _apiKey = ResolveConfigured(configuration["Stripe:ApiKey"]);
        _webhookSecret = ResolveConfigured(configuration["Stripe:WebhookSecret"]);
    }

    public bool IsConfigured => _apiKey is not null;

    private StripeClient Client => new(_apiKey ?? throw new BillingNotConfiguredException());

    public async Task<string> CreateCheckoutSessionAsync(Guid organizationId, string planKey, string successUrl, string cancelUrl, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new BillingNotConfiguredException();

        var priceId = ResolveConfigured(_configuration[$"Stripe:PriceIds:{planKey}"])
            ?? throw new InvalidOperationException($"No Stripe price configured for plan '{planKey}' (Stripe:PriceIds:{planKey}).");

        var customerId = await EnsureStripeCustomerAsync(organizationId, cancellationToken);

        var sessionService = new SessionService(Client);
        var session = await sessionService.CreateAsync(new SessionCreateOptions
        {
            Customer = customerId,
            Mode = "subscription",
            LineItems = new List<SessionLineItemOptions>
            {
                new() { Price = priceId, Quantity = 1 }
            },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ClientReferenceId = organizationId.ToString(),
        }, cancellationToken: cancellationToken);

        return session.Url;
    }

    public async Task<string> CreateBillingPortalSessionAsync(Guid organizationId, string returnUrl, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new BillingNotConfiguredException();

        var customerId = await _billingRepository.GetStripeCustomerIdAsync(organizationId)
            ?? throw new InvalidOperationException("This organization has no Stripe customer yet - start a checkout session first.");

        var portalService = new Stripe.BillingPortal.SessionService(Client);
        var portalSession = await portalService.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = customerId,
            ReturnUrl = returnUrl,
        }, cancellationToken: cancellationToken);

        return portalSession.Url;
    }

    public async Task HandleWebhookEventAsync(string requestBody, string stripeSignatureHeader, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || _webhookSecret is null) throw new BillingNotConfiguredException();

        var stripeEvent = EventUtility.ConstructEvent(requestBody, stripeSignatureHeader, _webhookSecret);

        switch (stripeEvent.Type)
        {
            case "customer.subscription.created":
            case "customer.subscription.updated":
                if (stripeEvent.Data.Object is Subscription subscription)
                {
                    await UpsertSubscriptionFromStripeAsync(subscription, cancellationToken);
                }
                break;

            case "customer.subscription.deleted":
                if (stripeEvent.Data.Object is Subscription deletedSubscription)
                {
                    await UpsertSubscriptionFromStripeAsync(deletedSubscription, cancellationToken);
                }
                break;

            case "invoice.paid":
            case "invoice.payment_failed":
                if (stripeEvent.Data.Object is Invoice invoice)
                {
                    await UpsertInvoiceFromStripeAsync(invoice, cancellationToken);
                }
                break;

            case "payment_method.attached":
                if (stripeEvent.Data.Object is PaymentMethod paymentMethod)
                {
                    await UpsertPaymentMethodFromStripeAsync(paymentMethod, cancellationToken);
                }
                break;

            default:
                _logger.LogInformation("StripeBillingService: unhandled webhook event type {Type}", stripeEvent.Type);
                break;
        }
    }

    private async Task<string> EnsureStripeCustomerAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var existing = await _billingRepository.GetStripeCustomerIdAsync(organizationId);
        if (!string.IsNullOrWhiteSpace(existing)) return existing;

        var customerService = new CustomerService(Client);
        var customer = await customerService.CreateAsync(new CustomerCreateOptions
        {
            Metadata = new Dictionary<string, string> { ["organizationId"] = organizationId.ToString() }
        }, cancellationToken: cancellationToken);

        await _billingRepository.SetStripeCustomerIdAsync(organizationId, customer.Id);
        return customer.Id;
    }

    private async Task UpsertSubscriptionFromStripeAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var organizationId = await _billingRepository.GetOrganizationIdByStripeCustomerIdAsync(subscription.CustomerId);
        if (organizationId is null)
        {
            _logger.LogWarning("StripeBillingService: no organization found for Stripe customer {CustomerId}", subscription.CustomerId);
            return;
        }

        var priceId = subscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
        var planKey = ResolvePlanKeyFromPriceId(priceId) ?? "Pro";

        await _billingRepository.UpsertSubscriptionAsync(new Citationly.Domain.Entities.Subscription
        {
            OrganizationId = organizationId.Value,
            StripeSubscriptionId = subscription.Id,
            PlanKey = planKey,
            Status = subscription.Status,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
        });

        // Only an active/trialing subscription should promote the org's PlanType; a canceled
        // or past_due subscription falls back to Trial rather than silently keeping paid access.
        var effectivePlan = subscription.Status is "active" or "trialing" ? planKey : "Trial";
        await _billingRepository.SyncOrganizationPlanTypeAsync(organizationId.Value, effectivePlan);
    }

    private async Task UpsertInvoiceFromStripeAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var organizationId = await _billingRepository.GetOrganizationIdByStripeCustomerIdAsync(invoice.CustomerId);
        if (organizationId is null)
        {
            _logger.LogWarning("StripeBillingService: no organization found for Stripe customer {CustomerId}", invoice.CustomerId);
            return;
        }

        await _billingRepository.UpsertInvoiceAsync(new Citationly.Domain.Entities.Invoice
        {
            OrganizationId = organizationId.Value,
            StripeInvoiceId = invoice.Id,
            AmountDueCents = invoice.AmountDue,
            AmountPaidCents = invoice.AmountPaid,
            Currency = invoice.Currency,
            Status = invoice.Status,
            HostedInvoiceUrl = invoice.HostedInvoiceUrl,
            IssuedAt = invoice.Created,
        });
    }

    private async Task UpsertPaymentMethodFromStripeAsync(PaymentMethod paymentMethod, CancellationToken cancellationToken)
    {
        var organizationId = await _billingRepository.GetOrganizationIdByStripeCustomerIdAsync(paymentMethod.CustomerId);
        if (organizationId is null)
        {
            _logger.LogWarning("StripeBillingService: no organization found for Stripe customer {CustomerId}", paymentMethod.CustomerId);
            return;
        }

        await _billingRepository.UpsertPaymentMethodAsync(new Citationly.Domain.Entities.PaymentMethod
        {
            OrganizationId = organizationId.Value,
            StripePaymentMethodId = paymentMethod.Id,
            Brand = paymentMethod.Card?.Brand,
            Last4 = paymentMethod.Card?.Last4,
            ExpMonth = (int?)paymentMethod.Card?.ExpMonth,
            ExpYear = (int?)paymentMethod.Card?.ExpYear,
        });
    }

    private string? ResolvePlanKeyFromPriceId(string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId)) return null;
        foreach (var plan in new[] { "Pro", "Enterprise" })
        {
            if (string.Equals(ResolveConfigured(_configuration[$"Stripe:PriceIds:{plan}"]), priceId, StringComparison.Ordinal))
            {
                return plan;
            }
        }
        return null;
    }

    /// <summary>Treats an unresolved "${...}" placeholder (the literal value left in
    /// appsettings.json when no environment variable overrides it) the same as unset.</summary>
    private static string? ResolveConfigured(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.StartsWith("${", StringComparison.Ordinal) ? null : value;
    }
}
