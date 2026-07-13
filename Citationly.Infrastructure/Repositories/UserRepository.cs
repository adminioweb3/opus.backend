using Dapper;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public UserRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<(Guid UserId, Guid OrganizationId, string Role, string PlanType, DateTime? TrialEndsAt)> CreateOrGetUserAsync(string firebaseUid, string email, string displayName)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var result = await connection.QuerySingleOrDefaultAsync<(Guid UserId, Guid OrganizationId, string Role, string PlanType, DateTime? TrialEndsAt)>(
            "SELECT * FROM sp_CreateOrGetUser(@FirebaseUid, @Email, @DisplayName)",
            new { FirebaseUid = firebaseUid, Email = email, DisplayName = displayName });

        return result;
    }

    public async Task<(Guid UserId, Guid OrganizationId, string Role)?> GetUserByFirebaseUidAsync(string firebaseUid)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var result = await connection.QuerySingleOrDefaultAsync<(Guid UserId, Guid OrganizationId, string Role)>(
            "SELECT Id as UserId, OrganizationId, Role FROM Users WHERE FirebaseUid = @FirebaseUid",
            new { FirebaseUid = firebaseUid });

        if (result.UserId == Guid.Empty) return null;
        
        return result;
    }
}
