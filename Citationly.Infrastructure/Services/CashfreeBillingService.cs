using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Citationly.Infrastructure.Services;

/// <summary>Server-side Cashfree subscription client. It never exposes merchant credentials.</summary>
public sealed class CashfreeBillingService : IBillingService
{
    private static readonly HashSet<string> SupportedPlans = new(StringComparer.OrdinalIgnoreCase) { "Pro", "Enterprise" };
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IBillingRepository _billingRepository;
    private readonly BillingRedirectUrlValidator _redirectUrlValidator;
    private readonly CashfreeWebhookSignatureVerifier _webhookVerifier;
    private readonly ILogger<CashfreeBillingService> _logger;
    private readonly string? _appId;
    private readonly string? _secretKey;

    public CashfreeBillingService(IHttpClientFactory httpClientFactory, IConfiguration configuration, IBillingRepository billingRepository,
        BillingRedirectUrlValidator redirectUrlValidator, CashfreeWebhookSignatureVerifier webhookVerifier, ILogger<CashfreeBillingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _billingRepository = billingRepository;
        _redirectUrlValidator = redirectUrlValidator;
        _webhookVerifier = webhookVerifier;
        _logger = logger;
        _appId = ResolveConfigured(configuration["Cashfree:AppId"]);
        _secretKey = ResolveConfigured(configuration["Cashfree:SecretKey"]);
    }

    public bool IsConfigured => _appId is not null && _secretKey is not null;

    public async Task<CashfreeSubscriptionSession> CreateSubscriptionSessionAsync(Guid organizationId, string planKey, string customerName,
        string customerEmail, string customerPhone, string returnUrl, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new BillingNotConfiguredException();
        if (!SupportedPlans.Contains(planKey)) throw new InvalidOperationException("The selected plan is not available.");
        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerEmail) || string.IsNullOrWhiteSpace(customerPhone))
            throw new InvalidOperationException("Name, email, and phone are required to authorize a recurring payment.");

        var planId = ResolveConfigured(_configuration[$"Cashfree:Plans:{planKey}:PlanId"])
            ?? throw new InvalidOperationException($"No Cashfree plan ID is configured for '{planKey}'.");
        returnUrl = _redirectUrlValidator.Validate(returnUrl, "ReturnUrl");

        var merchantSubscriptionId = $"cit-{organizationId:N}-{Guid.NewGuid():N}";
        var request = new
        {
            subscription_id = merchantSubscriptionId,
            customer_details = new { customer_name = customerName.Trim(), customer_email = customerEmail.Trim(), customer_phone = customerPhone.Trim() },
            plan_details = new { plan_id = planId },
            authorization_details = new { payment_methods = new[] { "upi", "enach", "card" } },
            subscription_meta = new { return_url = returnUrl, notification_channel = new[] { "EMAIL", "SMS" } },
            subscription_tags = new { organization_id = organizationId.ToString(), plan_key = planKey }
        };

        var environment = string.Equals(_configuration["Cashfree:Environment"], "Production", StringComparison.OrdinalIgnoreCase)
            ? "production" : "sandbox";
        var baseUrl = environment == "production" ? "https://api.cashfree.com" : "https://sandbox.cashfree.com";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/pg/subscriptions") { Content = JsonContent.Create(request) };
        httpRequest.Headers.Add("x-client-id", _appId);
        httpRequest.Headers.Add("x-client-secret", _secretKey);
        httpRequest.Headers.Add("x-api-version", _configuration["Cashfree:ApiVersion"] ?? "2025-01-01");
        httpRequest.Headers.Add("x-request-id", Guid.NewGuid().ToString());
        httpRequest.Headers.Add("x-idempotency-key", Guid.NewGuid().ToString());

        var client = _httpClientFactory.CreateClient("Cashfree");
        using var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Cashfree subscription creation failed with HTTP {StatusCode}", (int)response.StatusCode);
            throw new InvalidOperationException("Cashfree could not start the subscription authorization. Please try again.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var subscriptionId = GetString(root, "subscription_id") ?? merchantSubscriptionId;
        var sessionId = GetString(root, "subscription_session_id")
            ?? throw new InvalidOperationException("Cashfree did not return a subscription session.");
        var status = GetString(root, "subscription_status") ?? "INITIALIZED";

        await _billingRepository.UpsertCashfreeSubscriptionAsync(new Subscription
        {
            OrganizationId = organizationId,
            CashfreeSubscriptionId = subscriptionId,
            PlanKey = planKey,
            Status = status
        });
        return new CashfreeSubscriptionSession(subscriptionId, sessionId, status, environment);
    }

    public async Task HandleWebhookEventAsync(string requestBody, string signature, string timestamp, CancellationToken cancellationToken = default)
    {
        if (!_webhookVerifier.Verify(requestBody, timestamp, signature)) throw new InvalidOperationException("Invalid Cashfree webhook signature.");
        using var document = JsonDocument.Parse(requestBody);
        var root = document.RootElement;
        var eventType = GetString(root, "type") ?? GetString(root, "event_type") ?? "SUBSCRIPTION_UPDATE";
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestBody)));
        var eventId = GetString(root, "event_id") ?? payloadHash;
        if (!await _billingRepository.TryBeginCashfreeWebhookEventAsync(eventId, payloadHash, eventType, cancellationToken))
        {
            _logger.LogInformation("Cashfree webhook {EventId} is already completed or being processed", eventId);
            return;
        }

        try
        {
            var subscription = root.TryGetProperty("data", out var data) ? data : root;
        var subscriptionId = GetString(subscription, "subscription_id");
        var status = GetString(subscription, "subscription_status");
        if (string.IsNullOrWhiteSpace(subscriptionId) || string.IsNullOrWhiteSpace(status))
        {
            _logger.LogInformation("Cashfree webhook did not include subscription state.");
                await _billingRepository.CompleteCashfreeWebhookEventAsync(eventId, cancellationToken);
            return;
        }
            var existing = await _billingRepository.GetCashfreeSubscriptionAsync(subscriptionId);
            if (existing is null)
            {
                _logger.LogWarning("Cashfree webhook references unknown subscription {SubscriptionId}", subscriptionId);
                await _billingRepository.CompleteCashfreeWebhookEventAsync(eventId, cancellationToken);
                return;
            }
            var organizationId = existing.OrganizationId;
        existing.Status = status;
        await _billingRepository.UpsertCashfreeSubscriptionAsync(existing);
        await _billingRepository.SyncOrganizationPlanTypeAsync(organizationId, status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase) ? existing.PlanKey : "Trial");
            await _billingRepository.CompleteCashfreeWebhookEventAsync(eventId, cancellationToken);
        }
        catch (Exception ex)
        {
            await _billingRepository.FailCashfreeWebhookEventAsync(eventId, ex.Message, cancellationToken);
            throw;
        }
    }

    public async Task CancelSubscriptionAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new BillingNotConfiguredException();
        var subscription = await _billingRepository.GetCurrentCashfreeSubscriptionAsync(organizationId)
            ?? throw new InvalidOperationException("No Cashfree subscription is available to cancel.");
        if (string.IsNullOrWhiteSpace(subscription.CashfreeSubscriptionId))
            throw new InvalidOperationException("The current subscription cannot be cancelled through Cashfree.");

        var environment = string.Equals(_configuration["Cashfree:Environment"], "Production", StringComparison.OrdinalIgnoreCase)
            ? "production" : "sandbox";
        var baseUrl = environment == "production" ? "https://api.cashfree.com" : "https://sandbox.cashfree.com";
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{baseUrl}/pg/subscriptions/{Uri.EscapeDataString(subscription.CashfreeSubscriptionId)}/manage")
        {
            Content = JsonContent.Create(new { subscription_id = subscription.CashfreeSubscriptionId, action = "CANCEL" })
        };
        request.Headers.Add("x-client-id", _appId);
        request.Headers.Add("x-client-secret", _secretKey);
        request.Headers.Add("x-api-version", _configuration["Cashfree:ApiVersion"] ?? "2025-01-01");
        request.Headers.Add("x-request-id", Guid.NewGuid().ToString());
        request.Headers.Add("x-idempotency-key", Guid.NewGuid().ToString());

        using var response = await _httpClientFactory.CreateClient("Cashfree").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Cashfree cancellation failed with HTTP {StatusCode}", (int)response.StatusCode);
            throw new InvalidOperationException("Cashfree could not cancel this subscription. Please contact support if the issue persists.");
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        subscription.Status = GetString(document.RootElement, "subscription_status") ?? "CANCELLED";
        subscription.CancelAtPeriodEnd = false;
        await _billingRepository.UpsertCashfreeSubscriptionAsync(subscription);
        await _billingRepository.SyncOrganizationPlanTypeAsync(organizationId, "Trial");
    }

    public async Task<int> ReconcileSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return 0;
        var subscriptions = await _billingRepository.GetCashfreeSubscriptionsAsync(cancellationToken);
        var reconciled = 0;
        foreach (var subscription in subscriptions)
        {
            try
            {
                var status = await FetchSubscriptionStatusAsync(subscription.CashfreeSubscriptionId!, cancellationToken);
                if (status is null) continue;
                subscription.Status = status;
                await _billingRepository.UpsertCashfreeSubscriptionAsync(subscription);
                await _billingRepository.SyncOrganizationPlanTypeAsync(subscription.OrganizationId,
                    status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase) ? subscription.PlanKey : "Trial");
                reconciled++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cashfree reconciliation failed for subscription {SubscriptionId}", subscription.CashfreeSubscriptionId);
            }
        }
        return reconciled;
    }

    private async Task<string?> FetchSubscriptionStatusAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        var environment = string.Equals(_configuration["Cashfree:Environment"], "Production", StringComparison.OrdinalIgnoreCase)
            ? "production" : "sandbox";
        var baseUrl = environment == "production" ? "https://api.cashfree.com" : "https://sandbox.cashfree.com";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/pg/subscriptions/{Uri.EscapeDataString(subscriptionId)}");
        request.Headers.Add("x-client-id", _appId);
        request.Headers.Add("x-client-secret", _secretKey);
        request.Headers.Add("x-api-version", _configuration["Cashfree:ApiVersion"] ?? "2025-01-01");
        using var response = await _httpClientFactory.CreateClient("Cashfree").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Cashfree subscription lookup returned HTTP {(int)response.StatusCode}.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return GetString(document.RootElement, "subscription_status");
    }

    private static string? GetString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? ResolveConfigured(string? value) => string.IsNullOrWhiteSpace(value) || value.StartsWith("${", StringComparison.Ordinal) ? null : value;
}
