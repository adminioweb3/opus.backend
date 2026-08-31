using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using System.Security.Cryptography;
using System.Text;

namespace Citationly.Infrastructure.Services;

/// <summary>Retained only as historical migration code. CashfreeBillingService is the registered provider.</summary>
public sealed class StripeBillingService
{
    private static readonly HashSet<string> SupportedPlanKeys = new(StringComparer.OrdinalIgnoreCase) { "Pro", "Enterprise" };
    private readonly IConfiguration _configuration;
    private readonly IBillingRepository _billingRepository;
    private readonly BillingRedirectUrlValidator _redirectUrlValidator;
    private readonly ILogger<StripeBillingService> _logger;
    private readonly string? _apiKey;
    private readonly string? _webhookSecret;

    public StripeBillingService(
        IConfiguration configuration,
        IBillingRepository billingRepository,
        BillingRedirectUrlValidator redirectUrlValidator,
        ILogger<StripeBillingService> logger)
    {
        _configuration = configuration;
        _billingRepository = billingRepository;
        _redirectUrlValidator = redirectUrlValidator;
        _logger = logger;

        _apiKey = ResolveConfigured(configuration["Stripe:ApiKey"]);
        _webhookSecret = ResolveConfigured(configuration["Stripe:WebhookSecret"]);
    }

    public bool IsConfigured => _apiKey is not null;

    private StripeClient Client => new(_apiKey ?? throw new BillingNotConfiguredException());

    public async Task<string> CreateCheckoutSessionAsync(Guid organizationId, string planKey, string successUrl, string cancelUrl, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new BillingNotConfiguredException();

        if (!SupportedPlanKeys.Contains(planKey))
            throw new InvalidOperationException("The selected plan is not available for Stripe checkout.");

        var priceId = ResolveConfigured(_configuration[$"Stripe:PriceIds:{planKey}"])
            ?? throw new InvalidOperationException($"No Stripe price configured for plan '{planKey}' (Stripe:PriceIds:{planKey}).");

        var customerId = await EnsureStripeCustomerAsync(organizationId, cancellationToken);
        successUrl = _redirectUrlValidator.Validate(successUrl, "SuccessUrl");
        cancelUrl = _redirectUrlValidator.Validate(cancelUrl, "CancelUrl");

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
        returnUrl = _redirectUrlValidator.Validate(returnUrl, "ReturnUrl");

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
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestBody)));
        if (!await _billingRepository.TryBeginWebhookEventAsync(stripeEvent.Id, payloadHash, stripeEvent.Type, cancellationToken))
        {
            _logger.LogInformation("StripeBillingService: skipped duplicate or in-progress event {EventId}", stripeEvent.Id);
            return;
        }

        try
        {
            switch (stripeEvent.Type)
            {
                case "customer.subscription.created":
                case "customer.subscription.updated":
                case "customer.subscription.deleted":
                    if (stripeEvent.Data.Object is Subscription subscription)
                    {
                        await UpsertSubscriptionFromStripeAsync(subscription, cancellationToken);
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

            await _billingRepository.CompleteWebhookEventAsync(stripeEvent.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            await _billingRepository.FailWebhookEventAsync(stripeEvent.Id, ex.Message, cancellationToken);
            throw;
        }
    }

    public async Task<int> ReconcileSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return 0;

        var customers = await _billingRepository.GetOrganizationsWithStripeCustomersAsync(cancellationToken);
        var subscriptionService = new SubscriptionService(Client);
        var reconciled = 0;

        foreach (var (organizationId, customerId) in customers)
        {
            try
            {
                var subscriptions = await subscriptionService.ListAsync(new SubscriptionListOptions
                {
                    Customer = customerId,
                    Status = "all",
                    Limit = 100
                }, cancellationToken: cancellationToken);
                var latest = subscriptions.Data.OrderByDescending(subscription => subscription.Created).FirstOrDefault();

                if (latest is null)
                {
                    await _billingRepository.SyncOrganizationPlanTypeAsync(organizationId, "Trial");
                }
                else
                {
                    await UpsertSubscriptionFromStripeAsync(latest, cancellationToken);
                }

                reconciled++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StripeBillingService: reconciliation failed for organization {OrganizationId}", organizationId);
            }
        }

        return reconciled;
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

        var subscriptionItem = subscription.Items?.Data?.FirstOrDefault();
        var priceId = subscriptionItem?.Price?.Id;
        var planKey = ResolvePlanKeyFromPriceId(priceId);
        if (planKey is null)
        {
            _logger.LogError("StripeBillingService: subscription {SubscriptionId} has an unrecognized price {PriceId}", subscription.Id, priceId);
            throw new InvalidOperationException("Stripe subscription references an unrecognized price ID.");
        }

        await _billingRepository.UpsertSubscriptionAsync(new Citationly.Domain.Entities.Subscription
        {
            OrganizationId = organizationId.Value,
            StripeSubscriptionId = subscription.Id,
            PlanKey = planKey,
            Status = subscription.Status,
            CurrentPeriodStart = subscriptionItem?.CurrentPeriodStart,
            CurrentPeriodEnd = subscriptionItem?.CurrentPeriodEnd,
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
