using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.Team;
using Citationly.Application.Interfaces;
using System.Security.Claims;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TeamController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IUserRepository _userRepository;

    public TeamController(IMediator mediator, IUserRepository userRepository)
    {
        _mediator = mediator;
        _userRepository = userRepository;
    }

    private async Task<(Guid UserId, Guid OrganizationId, string Role)?> GetCallerAsync()
    {
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(firebaseUid)) return null;
        return await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
    }

    // Only Admins can invite, change roles, or remove members — everyone can view the list.
    private static bool CanManage(string role) => role == "Admin";

    [HttpGet("members")]
    public async Task<IActionResult> GetMembers()
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new GetTeamMembersQuery { OrganizationId = caller.Value.OrganizationId });
        return Ok(result);
    }

    [HttpPut("members/{userId}/role")]
    public async Task<IActionResult> UpdateRole(Guid userId, [FromBody] UpdateRoleRequest request)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized("User not found or unlinked.");
        if (!CanManage(caller.Value.Role)) return Forbid();
        if (userId == caller.Value.UserId) return BadRequest(new { message = "You can't change your own role." });

        var success = await _mediator.Send(new UpdateMemberRoleCommand { OrganizationId = caller.Value.OrganizationId, UserId = userId, Role = request.Role });
        if (!success) return BadRequest(new { message = "Couldn't update this member's role — an organization needs at least one Admin." });
        return Ok(new { message = "Role updated." });
    }

    [HttpDelete("members/{userId}")]
    public async Task<IActionResult> RemoveMember(Guid userId)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized("User not found or unlinked.");
        if (!CanManage(caller.Value.Role)) return Forbid();
        if (userId == caller.Value.UserId) return BadRequest(new { message = "You can't remove yourself from the organization." });

        var success = await _mediator.Send(new RemoveMemberCommand { OrganizationId = caller.Value.OrganizationId, UserId = userId });
        if (!success) return BadRequest(new { message = "Couldn't remove this member — an organization needs at least one Admin." });
        return Ok(new { message = "Member removed." });
    }

    [HttpGet("invites")]
    public async Task<IActionResult> GetInvites()
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new GetPendingInvitesQuery { OrganizationId = caller.Value.OrganizationId });
        return Ok(result);
    }

    [HttpPost("invites")]
    public async Task<IActionResult> CreateInvite([FromBody] CreateInviteRequest request)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized("User not found or unlinked.");
        if (!CanManage(caller.Value.Role)) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email is required." });

        var invite = await _mediator.Send(new CreateInviteCommand
        {
            OrganizationId = caller.Value.OrganizationId,
            Email = request.Email,
            Role = string.IsNullOrWhiteSpace(request.Role) ? "Viewer" : request.Role,
            InvitedByUserId = caller.Value.UserId
        });

        // No email-sending infra yet — the invite is fully real (a matching signup/login with
        // this email joins the org automatically via sp_CreateOrGetUser), it just needs to be
        // shared manually for now. The register page pre-fills the email from this link.
        var origin = $"{Request.Scheme}://{Request.Host}";
        var inviteLink = $"{origin}/register?email={Uri.EscapeDataString(invite.Email)}";

        return Ok(new { invite.Id, invite.Email, invite.Role, invite.ExpiresAt, InviteLink = inviteLink });
    }

    [HttpDelete("invites/{inviteId}")]
    public async Task<IActionResult> RevokeInvite(Guid inviteId)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized("User not found or unlinked.");
        if (!CanManage(caller.Value.Role)) return Forbid();

        var success = await _mediator.Send(new RevokeInviteCommand { OrganizationId = caller.Value.OrganizationId, InviteId = inviteId });
        if (!success) return NotFound();
        return Ok(new { message = "Invite revoked." });
    }
}

public class UpdateRoleRequest
{
    public string Role { get; set; } = string.Empty;
}

public class CreateInviteRequest
{
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Viewer";
}
