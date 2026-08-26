using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class AlertRepository : IAlertRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public AlertRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Alert?> UpsertAlertAsync(Alert alert)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Alert>(
            @"INSERT INTO Alerts
                (OrganizationId, DedupKey, Type, Title, Message, Severity, Source, ActionUrl, EvidenceJson)
              VALUES
                (@OrganizationId, @DedupKey, @Type, @Title, @Message, @Severity, @Source, @ActionUrl, @EvidenceJson::jsonb)
              ON CONFLICT (OrganizationId, DedupKey) DO NOTHING
              RETURNING *",
            alert);
    }

    public async Task<IEnumerable<Alert>> GetAlertsAsync(Guid organizationId, int limit = 50, bool unreadOnly = false)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<Alert>(
            @"SELECT *
              FROM Alerts
              WHERE OrganizationId = @OrganizationId
                AND (@UnreadOnly = FALSE OR IsRead = FALSE)
              ORDER BY CreatedAt DESC
              LIMIT @Limit",
            new { OrganizationId = organizationId, Limit = Math.Clamp(limit, 1, 200), UnreadOnly = unreadOnly });
    }

    public async Task<int> MarkReadAsync(Guid organizationId, Guid? alertId = null)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteAsync(
            @"UPDATE Alerts
              SET IsRead = TRUE
              WHERE OrganizationId = @OrganizationId
                AND (@AlertId IS NULL OR Id = @AlertId)",
            new { OrganizationId = organizationId, AlertId = alertId });
    }

    public async Task<IEnumerable<AlertThreshold>> GetThresholdsAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<AlertThreshold>(
            "SELECT * FROM AlertThresholds WHERE OrganizationId = @OrganizationId ORDER BY AlertType",
            new { OrganizationId = organizationId });
    }

    public async Task UpsertThresholdAsync(AlertThreshold threshold)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"INSERT INTO AlertThresholds
                (OrganizationId, AlertType, ThresholdValue, EmailEnabled, WebhookEnabled, WebhookUrl, UpdatedAt)
              VALUES
                (@OrganizationId, @AlertType, @ThresholdValue, @EmailEnabled, @WebhookEnabled, @WebhookUrl, CURRENT_TIMESTAMP)
              ON CONFLICT (OrganizationId, AlertType) DO UPDATE SET
                ThresholdValue = EXCLUDED.ThresholdValue,
                EmailEnabled = EXCLUDED.EmailEnabled,
                WebhookEnabled = EXCLUDED.WebhookEnabled,
                WebhookUrl = EXCLUDED.WebhookUrl,
                UpdatedAt = CURRENT_TIMESTAMP",
            threshold);
    }

    public async Task<IEnumerable<Alert>> GetPendingDeliveriesAsync(int limit = 100)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<Alert>(
            @"SELECT *
              FROM Alerts
              WHERE DeliveryStatus = 'Pending'
              ORDER BY CreatedAt ASC
              LIMIT @Limit",
            new { Limit = Math.Clamp(limit, 1, 500) });
    }

    public async Task MarkDeliveryAsync(Guid alertId, string deliveryStatus, DateTime? deliveredAt = null)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"UPDATE Alerts
              SET DeliveryStatus = @DeliveryStatus,
                  DeliveredAt = @DeliveredAt
              WHERE Id = @AlertId",
            new { AlertId = alertId, DeliveryStatus = deliveryStatus, DeliveredAt = deliveredAt });
    }
}
