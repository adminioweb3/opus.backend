using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IAlertRepository
{
    Task<Alert?> UpsertAlertAsync(Alert alert);
    Task<IEnumerable<Alert>> GetAlertsAsync(Guid organizationId, int limit = 50, bool unreadOnly = false);
    Task<int> MarkReadAsync(Guid organizationId, Guid? alertId = null);
    Task<IEnumerable<AlertThreshold>> GetThresholdsAsync(Guid organizationId);
    Task UpsertThresholdAsync(AlertThreshold threshold);
    Task<IEnumerable<Alert>> GetPendingDeliveriesAsync(int limit = 100);
    Task MarkDeliveryAsync(Guid alertId, string deliveryStatus, DateTime? deliveredAt = null);
}
