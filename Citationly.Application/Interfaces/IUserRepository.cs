namespace Citationly.Application.Interfaces;

public interface IUserRepository
{
    Task<(Guid UserId, Guid OrganizationId, string Role, string PlanType, DateTime? TrialEndsAt)> CreateOrGetUserAsync(string firebaseUid, string email, string displayName);
    Task<(Guid UserId, Guid OrganizationId, string Role)?> GetUserByFirebaseUidAsync(string firebaseUid);
}
