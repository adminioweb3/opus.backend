using Citationly.API.Services;
using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BillingController : ControllerBase
{
    private readonly ICurrentOrganizationAccessor _currentOrg;
    private readonly IBillingRepository _billingRepository;
    private readonly IBillingService _billingService;
    private readonly IEntitlementService _entitlementService;

    public BillingController(
        ICurrentOrganizationAccessor currentOrg,
        IBillingRepository billingRepository,
        IBillingService billingService,
        IEntitlementService entitlementService)
    {
        _currentOrg = currentOrg;
        _billingRepository = billingRepository;
        _billingService = billingService;
        _entitlementService = entitlementService;
    }

    // Real DB reads regardless of whether Cashfree is configured yet - an org's PlanType and
    // any locally-recorded Subscription/Invoice/PaymentMethod rows are always accurate.
    [HttpGet("subscription")]
    public async Task<IActionResult> GetSubscription()
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        var subscription = await _billingRepository.GetActiveSubscriptionAsync(orgId.Value);
        return Ok(new
        {
            billingConfigured = _billingService.IsConfigured,
            subscription
        });
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage()
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        var planKey = await _entitlementService.GetPlanKeyAsync(orgId.Value, HttpContext.RequestAborted);
        var aiCalls = await _entitlementService.CheckQuotaAsync(orgId.Value, "ai_calls_per_day", HttpContext.RequestAborted);
        var aiSpend = await _entitlementService.CheckQuotaAsync(orgId.Value, "ai_spend_micro_usd_per_day", HttpContext.RequestAborted);
        var publicApiCalls = await _entitlementService.CheckQuotaAsync(orgId.Value, "public_api_calls_per_day", HttpContext.RequestAborted);
        var recurringScanInterval = await _entitlementService.GetPlanLimitValueAsync(orgId.Value, "recurring_scan_interval_days", HttpContext.RequestAborted);

        var periodStart = DateTime.UtcNow.Date;
        var periodEnd = periodStart.AddDays(1);

        return Ok(new BillingUsageResponse(
            planKey,
            periodStart,
            periodEnd,
            new BillingUsageMetric("AI calls", "ai_calls_per_day", aiCalls.CurrentUsage, aiCalls.Limit, "calls today"),
            new BillingUsageMetric("Estimated AI spend", "ai_spend_micro_usd_per_day", aiSpend.CurrentUsage, aiSpend.Limit, "micro-USD today"),
            new BillingUsageMetric("Public API calls", "public_api_calls_per_day", publicApiCalls.CurrentUsage, publicApiCalls.Limit, "calls today"),
            recurringScanInterval));
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices([FromQuery] int limit = 100)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        var invoices = await _billingRepository.GetInvoicesAsync(orgId.Value, Math.Clamp(limit, 1, 500));
        return Ok(invoices);
    }

    [HttpGet("payment-methods")]
    public async Task<IActionResult> GetPaymentMethods()
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        var methods = await _billingRepository.GetPaymentMethodsAsync(orgId.Value);
        return Ok(methods);
    }

    [HttpPost("subscription-session")]
    [RequireOrgRole("Admin")]
    [AuditAction("billing.subscription_session.create", "Billing", "Subscription")]
    public async Task<IActionResult> CreateSubscriptionSession([FromBody] CreateSubscriptionSessionRequest request)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        if (!_billingService.IsConfigured)
        {
            return StatusCode(501, new { message = "Billing is not configured yet. Configure Cashfree credentials and plan IDs to enable subscriptions." });
        }

        try
        {
            var session = await _billingService.CreateSubscriptionSessionAsync(orgId.Value, request.PlanKey, request.CustomerName,
                request.CustomerEmail, request.CustomerPhone, request.ReturnUrl, HttpContext.RequestAborted);
            return Ok(session);
        }
        catch (Exception ex) when (ex is not BillingNotConfiguredException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("subscription/cancel")]
    [RequireOrgRole("Admin")]
    [AuditAction("billing.subscription.cancel", "Billing", "Subscription")]
    public async Task<IActionResult> CancelSubscription()
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();
        if (!_billingService.IsConfigured) return StatusCode(501, new { message = "Billing is not configured yet." });
        try
        {
            await _billingService.CancelSubscriptionAsync(orgId.Value, HttpContext.RequestAborted);
            return NoContent();
        }
        catch (Exception ex) when (ex is not BillingNotConfiguredException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Cashfree calls this directly - no user JWT, verified against its raw-body HMAC signature.
    [HttpPost("cashfree/webhook")]
    [AllowAnonymous]
    [AuditAction("billing.webhook.received", "Billing", "CashfreeWebhook")]
    public async Task<IActionResult> Webhook()
    {
        if (!_billingService.IsConfigured)
        {
            return StatusCode(501, new { message = "Billing is not configured yet." });
        }

        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync();
        var signature = Request.Headers["x-webhook-signature"].ToString();
        var timestamp = Request.Headers["x-webhook-timestamp"].ToString();

        try
        {
            await _billingService.HandleWebhookEventAsync(json, signature, timestamp, HttpContext.RequestAborted);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public class CreateSubscriptionSessionRequest
{
    public string PlanKey { get; set; } = "Pro";
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
}

public sealed record BillingUsageResponse(
    string PlanKey,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    BillingUsageMetric AiCalls,
    BillingUsageMetric EstimatedAiSpend,
    BillingUsageMetric PublicApiCalls,
    long? RecurringScanIntervalDays);

public sealed record BillingUsageMetric(
    string Label,
    string MetricKey,
    long CurrentUsage,
    long? Limit,
    string Unit);
