namespace Citationly.Application.Interfaces;

public class TeamMemberDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class TeamInviteDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public interface ITeamRepository
{
    Task<List<TeamMemberDto>> GetMembersByOrgAsync(Guid organizationId);
    Task<int> GetMemberCountAsync(Guid organizationId);
    Task<string?> GetMemberRoleAsync(Guid organizationId, Guid userId);
    Task<bool> UpdateMemberRoleAsync(Guid organizationId, Guid userId, string role);
    Task<bool> RemoveMemberAsync(Guid organizationId, Guid userId);

    Task<TeamInviteDto> CreateInviteAsync(Guid organizationId, string email, string role, Guid invitedByUserId);
    Task<List<TeamInviteDto>> GetPendingInvitesByOrgAsync(Guid organizationId);
    Task<bool> RevokeInviteAsync(Guid organizationId, Guid inviteId);
}
