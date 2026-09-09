using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class BetaFeedbackRepository : IBetaFeedbackRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public BetaFeedbackRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Guid> CreateAsync(BetaFeedback feedback, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO BetaFeedback (
                OrganizationId, UserId, PagePath, FeedbackType, Rating, Message, ContextId, Status
            )
            VALUES (
                @OrganizationId, @UserId, @PagePath, @FeedbackType, @Rating, @Message, @ContextId, @Status
            )
            RETURNING Id
            """,
            feedback);
    }

    public async Task<IEnumerable<BetaFeedback>> GetByOrganizationAsync(Guid organizationId, int limit = 100, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<BetaFeedback>(
            """
            SELECT *
            FROM BetaFeedback
            WHERE OrganizationId = @OrganizationId
            ORDER BY CreatedAt DESC
            LIMIT @Limit
            """,
            new { OrganizationId = organizationId, Limit = Math.Clamp(limit, 1, 500) });
    }
}
