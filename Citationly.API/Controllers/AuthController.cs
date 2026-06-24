using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.Auth;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("sync")]
    public async Task<IActionResult> SyncUser()
    {
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value ?? $"{firebaseUid}@no-email.firebase.com";
        var displayName = User.FindFirst("name")?.Value ?? email?.Split('@').FirstOrDefault() ?? "New User";

        if (string.IsNullOrEmpty(firebaseUid))
        {
            var claims = string.Join(", ", User.Claims.Select(c => $"{c.Type}: {c.Value}"));
            Console.WriteLine($"[AUTH ERROR] Missing user_id claim. Found claims: {claims}");
            return BadRequest($"Invalid token claims: Missing user_id. Claims: {claims}");
        }

        var command = new SyncUserCommand
        {
            FirebaseUid = firebaseUid,
            Email = email,
            DisplayName = displayName
        };

        var result = await _mediator.Send(command);

        return Ok(result);
    }
}
