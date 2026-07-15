using Dapper;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Repositories;

public class TeamRepository : ITeamRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public TeamRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<List<TeamMemberDto>> GetMembersByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<TeamMemberDto>(
            @"SELECT Id, COALESCE(DisplayName, '') AS DisplayName, Email, Role, CreatedAt
              FROM Users WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt ASC",
            new { OrganizationId = organizationId });
        return results.ToList();
    }

    public async Task<int> GetMemberCountAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Users WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });
    }

    public async Task<string?> GetMemberRoleAsync(Guid organizationId, Guid userId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT Role FROM Users WHERE OrganizationId = @OrganizationId AND Id = @UserId",
            new { OrganizationId = organizationId, UserId = userId });
    }

    public async Task<bool> UpdateMemberRoleAsync(Guid organizationId, Guid userId, string role)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        // Never allow demoting the last Admin — the org would become unmanageable.
        if (role != "Admin")
        {
            var currentRole = await connection.QuerySingleOrDefaultAsync<string?>(
                "SELECT Role FROM Users WHERE OrganizationId = @OrganizationId AND Id = @UserId",
                new { OrganizationId = organizationId, UserId = userId });
            if (currentRole == null) return false;

            if (currentRole == "Admin")
            {
                var adminCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM Users WHERE OrganizationId = @OrganizationId AND Role = 'Admin'",
                    new { OrganizationId = organizationId });
                if (adminCount <= 1) return false;
            }
        }

        var rows = await connection.ExecuteAsync(
            "UPDATE Users SET Role = @Role WHERE OrganizationId = @OrganizationId AND Id = @UserId",
            new { OrganizationId = organizationId, UserId = userId, Role = role });
        return rows > 0;
    }

    public async Task<bool> RemoveMemberAsync(Guid organizationId, Guid userId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        // Never allow removing the last Admin — the org would become unmanageable.
        var role = await connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT Role FROM Users WHERE OrganizationId = @OrganizationId AND Id = @UserId",
            new { OrganizationId = organizationId, UserId = userId });
        if (role == null) return false;

        if (role == "Admin")
        {
            var adminCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Users WHERE OrganizationId = @OrganizationId AND Role = 'Admin'",
                new { OrganizationId = organizationId });
            if (adminCount <= 1) return false;
        }

        var rows = await connection.ExecuteAsync(
            "DELETE FROM Users WHERE OrganizationId = @OrganizationId AND Id = @UserId",
            new { OrganizationId = organizationId, UserId = userId });
        return rows > 0;
    }

    public async Task<TeamInviteDto> CreateInviteAsync(Guid organizationId, string email, string role, Guid invitedByUserId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        var invite = await connection.QuerySingleAsync<TeamInviteDto>(
            @"INSERT INTO Invites (OrganizationId, Email, Role, Token, InvitedByUserId, ExpiresAt)
              VALUES (@OrganizationId, @Email, @Role, @Token, @InvitedByUserId, CURRENT_TIMESTAMP + INTERVAL '14 days')
              RETURNING Id, Email, Role, Token, CreatedAt, ExpiresAt",
            new { OrganizationId = organizationId, Email = email, Role = role, Token = token, InvitedByUserId = invitedByUserId });

        return invite;
    }

    public async Task<List<TeamInviteDto>> GetPendingInvitesByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<TeamInviteDto>(
            @"SELECT Id, Email, Role, Token, CreatedAt, ExpiresAt FROM Invites
              WHERE OrganizationId = @OrganizationId AND AcceptedAt IS NULL AND ExpiresAt > CURRENT_TIMESTAMP
              ORDER BY CreatedAt DESC",
            new { OrganizationId = organizationId });
        return results.ToList();
    }

    public async Task<bool> RevokeInviteAsync(Guid organizationId, Guid inviteId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var rows = await connection.ExecuteAsync(
            "DELETE FROM Invites WHERE OrganizationId = @OrganizationId AND Id = @InviteId",
            new { OrganizationId = organizationId, InviteId = inviteId });
        return rows > 0;
    }
}
