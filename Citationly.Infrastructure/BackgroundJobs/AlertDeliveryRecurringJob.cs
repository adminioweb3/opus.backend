using Citationly.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Citationly.Infrastructure.BackgroundJobs;

public class AlertDeliveryRecurringJob
{
    private readonly IAlertService _alertService;
    private readonly ILogger<AlertDeliveryRecurringJob> _logger;

    public AlertDeliveryRecurringJob(IAlertService alertService, ILogger<AlertDeliveryRecurringJob> logger)
    {
        _alertService = alertService;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var delivered = await _alertService.DeliverPendingAlertsAsync();
        _logger.LogInformation("AlertDeliveryRecurringJob: delivered {Count} pending alert(s)", delivered);
    }
}
