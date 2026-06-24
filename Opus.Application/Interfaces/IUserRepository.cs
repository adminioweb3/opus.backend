namespace Opus.Application.Interfaces;

public interface IUserRepository
{
    Task<(Guid UserId, Guid OrganizationId, string Role)> CreateOrGetUserAsync(string firebaseUid, string email, string displayName);
    Task<(Guid UserId, Guid OrganizationId, string Role)?> GetUserByFirebaseUidAsync(string firebaseUid);
}
