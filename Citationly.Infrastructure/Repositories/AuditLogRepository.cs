using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public AuditLogRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Guid> CreateAsync(AuditLog log, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO AuditLogs (
                OrganizationId, ActorUserId, ActorEmail, ActorType, Action, Category, Outcome,
                TargetType, TargetId, IpAddress, UserAgent, MetadataJson
            )
            VALUES (
                @OrganizationId, @ActorUserId, @ActorEmail, @ActorType, @Action, @Category, @Outcome,
                @TargetType, @TargetId, @IpAddress, @UserAgent, CAST(@MetadataJson AS jsonb)
            )
            RETURNING Id
            """,
            log);
    }

    public async Task<IEnumerable<AuditLog>> GetByOrganizationAsync(Guid organizationId, int limit = 100, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<AuditLog>(
            """
            SELECT *
            FROM AuditLogs
            WHERE OrganizationId = @OrganizationId
            ORDER BY CreatedAt DESC
            LIMIT @Limit
            """,
            new { OrganizationId = organizationId, Limit = Math.Clamp(limit, 1, 500) });
    }
}
