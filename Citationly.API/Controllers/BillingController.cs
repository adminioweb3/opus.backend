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

    public BillingController(ICurrentOrganizationAccessor currentOrg, IBillingRepository billingRepository, IBillingService billingService)
    {
        _currentOrg = currentOrg;
        _billingRepository = billingRepository;
        _billingService = billingService;
    }

    // Real DB reads regardless of whether Stripe is configured yet - an org's PlanType and
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

    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices()
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        var invoices = await _billingRepository.GetInvoicesAsync(orgId.Value);
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

    [HttpPost("checkout-session")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateCheckoutSessionRequest request)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        if (!_billingService.IsConfigured)
        {
            return StatusCode(501, new { message = "Billing is not configured yet. Set Stripe:ApiKey to enable checkout." });
        }

        try
        {
            var url = await _billingService.CreateCheckoutSessionAsync(orgId.Value, request.PlanKey, request.SuccessUrl, request.CancelUrl);
            return Ok(new { url });
        }
        catch (Exception ex) when (ex is not BillingNotConfiguredException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("portal-session")]
    public async Task<IActionResult> CreatePortalSession([FromBody] CreatePortalSessionRequest request)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        if (!_billingService.IsConfigured)
        {
            return StatusCode(501, new { message = "Billing is not configured yet." });
        }

        try
        {
            var url = await _billingService.CreateBillingPortalSessionAsync(orgId.Value, request.ReturnUrl);
            return Ok(new { url });
        }
        catch (Exception ex) when (ex is not BillingNotConfiguredException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Stripe calls this directly - no user JWT, verified instead by the webhook signature.
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        if (!_billingService.IsConfigured)
        {
            return StatusCode(501, new { message = "Billing is not configured yet." });
        }

        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"].ToString();

        try
        {
            await _billingService.HandleWebhookEventAsync(json, signature);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public class CreateCheckoutSessionRequest
{
    public string PlanKey { get; set; } = "Pro";
    public string SuccessUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
}

public class CreatePortalSessionRequest
{
    public string ReturnUrl { get; set; } = string.Empty;
}
