using Citationly.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Citationly.Infrastructure.BackgroundJobs;

public sealed class BillingReconciliationRecurringJob
{
    private readonly IBillingService _billingService;
    private readonly ILogger<BillingReconciliationRecurringJob> _logger;

    public BillingReconciliationRecurringJob(IBillingService billingService, ILogger<BillingReconciliationRecurringJob> logger)
    {
        _billingService = billingService;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        if (!_billingService.IsConfigured)
        {
            _logger.LogDebug("Billing reconciliation skipped because Cashfree is not configured.");
            return;
        }

        var reconciled = await _billingService.ReconcileSubscriptionsAsync();
        _logger.LogInformation("Billing reconciliation refreshed {Count} Cashfree subscription(s)", reconciled);
    }
}
