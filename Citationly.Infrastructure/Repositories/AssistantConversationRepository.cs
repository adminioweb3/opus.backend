using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public sealed class AssistantConversationRepository : IAssistantConversationRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public AssistantConversationRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<IReadOnlyList<AssistantThread>> GetThreadsAsync(Guid organizationId, Guid userId, int limit = 50, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT Id, OrganizationId, UserId, Title, CreatedAt, UpdatedAt
            FROM AssistantThreads
            WHERE OrganizationId = @OrganizationId AND UserId = @UserId
            ORDER BY UpdatedAt DESC
            LIMIT @Limit
            """,
            new { OrganizationId = organizationId, UserId = userId, Limit = Math.Clamp(limit, 1, 100) },
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<AssistantThread>(command)).AsList();
    }

    public async Task<AssistantThread?> GetThreadAsync(Guid threadId, Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT Id, OrganizationId, UserId, Title, CreatedAt, UpdatedAt
            FROM AssistantThreads
            WHERE Id = @ThreadId AND OrganizationId = @OrganizationId AND UserId = @UserId
            """,
            new { ThreadId = threadId, OrganizationId = organizationId, UserId = userId },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AssistantThread>(command);
    }

    public async Task<IReadOnlyList<AssistantMessage>> GetMessagesAsync(Guid threadId, Guid organizationId, Guid userId, int limit = 100, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            SELECT m.Id, m.ThreadId, m.Role, m.Content, m.CreatedAt
            FROM AssistantMessages m
            INNER JOIN AssistantThreads t ON t.Id = m.ThreadId
            WHERE m.ThreadId = @ThreadId
              AND t.OrganizationId = @OrganizationId
              AND t.UserId = @UserId
            ORDER BY m.CreatedAt ASC, m.Id ASC
            LIMIT @Limit
            """,
            new { ThreadId = threadId, OrganizationId = organizationId, UserId = userId, Limit = Math.Clamp(limit, 1, 200) },
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<AssistantMessage>(command)).AsList();
    }

    public async Task<AssistantThread> CreateThreadAsync(Guid organizationId, Guid userId, string title, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            INSERT INTO AssistantThreads (OrganizationId, UserId, Title)
            VALUES (@OrganizationId, @UserId, @Title)
            RETURNING Id, OrganizationId, UserId, Title, CreatedAt, UpdatedAt
            """,
            new { OrganizationId = organizationId, UserId = userId, Title = NormalizeTitle(title) },
            cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<AssistantThread>(command);
    }

    public async Task<bool> RenameThreadAsync(Guid threadId, Guid organizationId, Guid userId, string title, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            """
            UPDATE AssistantThreads
            SET Title = @Title, UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @ThreadId AND OrganizationId = @OrganizationId AND UserId = @UserId
            """,
            new { ThreadId = threadId, OrganizationId = organizationId, UserId = userId, Title = NormalizeTitle(title) },
            cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command) == 1;
    }

    public async Task<bool> DeleteThreadAsync(Guid threadId, Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var command = new CommandDefinition(
            "DELETE FROM AssistantThreads WHERE Id = @ThreadId AND OrganizationId = @OrganizationId AND UserId = @UserId",
            new { ThreadId = threadId, OrganizationId = organizationId, UserId = userId },
            cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command) == 1;
    }

    public async Task<AssistantMessage?> AddMessageAsync(Guid threadId, Guid organizationId, Guid userId, string role, string content, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var insert = new CommandDefinition(
            """
            INSERT INTO AssistantMessages (ThreadId, Role, Content)
            SELECT Id, @Role, @Content
            FROM AssistantThreads
            WHERE Id = @ThreadId AND OrganizationId = @OrganizationId AND UserId = @UserId
            RETURNING Id, ThreadId, Role, Content, CreatedAt
            """,
            new { ThreadId = threadId, OrganizationId = organizationId, UserId = userId, Role = role, Content = content },
            transaction,
            cancellationToken: cancellationToken);
        var message = await connection.QuerySingleOrDefaultAsync<AssistantMessage>(insert);

        if (message != null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE AssistantThreads SET UpdatedAt = CURRENT_TIMESTAMP WHERE Id = @ThreadId",
                new { ThreadId = threadId }, transaction, cancellationToken: cancellationToken));
        }

        transaction.Commit();
        return message;
    }

    private static string NormalizeTitle(string title)
    {
        var normalized = string.IsNullOrWhiteSpace(title) ? "New conversation" : title.Trim();
        return normalized.Length <= 80 ? normalized : normalized[..77] + "...";
    }
}
