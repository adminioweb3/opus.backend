namespace Citationly.Application.Interfaces;

public interface IAlertService
{
    Task<int> GenerateCommandCenterAlertsAsync(Guid organizationId, CancellationToken ct = default);
    Task<int> DeliverPendingAlertsAsync(CancellationToken ct = default);
}
