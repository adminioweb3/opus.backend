using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.Auth;
using Citationly.Application.Interfaces;
using Dapper;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IMediator mediator, IDbConnectionFactory dbConnectionFactory, ILogger<AuthController> logger)
    {
        _mediator = mediator;
        _dbConnectionFactory = dbConnectionFactory;
        _logger = logger;
    }

    [HttpPost("sync")]
    public async Task<IActionResult> SyncUser()
    {
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value ?? $"{firebaseUid}@no-email.firebase.com";
        var displayName = User.FindFirst("name")?.Value ?? email?.Split('@').FirstOrDefault() ?? "New User";

        // Determine provider from Firebase claims
        var provider = DetermineProvider(User);

        if (string.IsNullOrEmpty(firebaseUid))
        {
            _logger.LogWarning("Missing user_id claim during SyncUser.");
            return BadRequest(new { message = "Invalid token claims: Missing user_id." });
        }

        var command = new SyncUserCommand
        {
            FirebaseUid = firebaseUid,
            Provider = provider,
            ProviderUid = firebaseUid,
            Email = email,
            DisplayName = displayName
        };

        var result = await _mediator.Send(command);

        return Ok(result);
    }

    [HttpGet("check-account")]
    [AllowAnonymous]
    public IActionResult CheckAccount([FromQuery] string email)
    {
        if (string.IsNullOrEmpty(email))
            return BadRequest(new { message = "Email is required" });

        // Generic response to prevent user enumeration
        return Ok(new { message = "If this email is registered, you will be able to log in with it." });
    }

    private string DetermineProvider(System.Security.Claims.ClaimsPrincipal user)
    {
        var firebaseClaim = user.FindFirst("firebase")?.Value;

        if (!string.IsNullOrEmpty(firebaseClaim))
        {
            if (firebaseClaim.Contains("\"sign_in_provider\":\"github.com\"") || firebaseClaim.Contains("\"sign_in_provider\": \"github.com\""))
                return "github";
            if (firebaseClaim.Contains("\"sign_in_provider\":\"google.com\"") || firebaseClaim.Contains("\"sign_in_provider\": \"google.com\""))
                return "google";
        }

        return "email"; // Default for email/password auth
    }
}
