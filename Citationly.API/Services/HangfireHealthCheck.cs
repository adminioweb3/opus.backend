using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Citationly.API.Services;

public sealed class HangfireHealthCheck : IHealthCheck
{
    private readonly JobStorage _jobStorage;

    public HangfireHealthCheck(JobStorage jobStorage)
    {
        _jobStorage = jobStorage;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var monitoringApi = _jobStorage.GetMonitoringApi();
            var servers = monitoringApi.Servers();
            var statistics = monitoringApi.GetStatistics();

            var data = new Dictionary<string, object>
            {
                ["servers"] = servers.Count,
                ["enqueued"] = statistics.Enqueued,
                ["processing"] = statistics.Processing,
                ["failed"] = statistics.Failed,
                ["scheduled"] = statistics.Scheduled
            };

            return servers.Count == 0
                ? Task.FromResult(HealthCheckResult.Degraded("Hangfire storage is reachable, but no processing server is visible.", data: data))
                : Task.FromResult(HealthCheckResult.Healthy("Hangfire storage and processing server are visible.", data));
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Hangfire storage could not be queried.", exception));
        }
    }
}
