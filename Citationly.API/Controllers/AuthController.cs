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

    public AuthController(IMediator mediator, IDbConnectionFactory dbConnectionFactory)
    {
        _mediator = mediator;
        _dbConnectionFactory = dbConnectionFactory;
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
            var claims = string.Join(", ", User.Claims.Select(c => $"{c.Type}: {c.Value}"));
            Console.WriteLine($"[AUTH ERROR] Missing user_id claim. Found claims: {claims}");
            return BadRequest($"Invalid token claims: Missing user_id. Claims: {claims}");
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
    public async Task<IActionResult> CheckAccount([FromQuery] string email)
    {
        if (string.IsNullOrEmpty(email))
            return BadRequest("Email is required");

        using var connection = _dbConnectionFactory.CreateConnection();

        var linkedProviders = (await connection.QueryAsync<string>(
            @"SELECT DISTINCT Provider FROM AuthProviders ap
              JOIN Users u ON ap.UserId = u.Id
              WHERE LOWER(u.Email) = @Email
              ORDER BY Provider",
            new { Email = email.ToLower().Trim() })).ToList();

        return Ok(new { exists = linkedProviders.Count > 0, email = email.ToLower(), linkedProviders });
    }

    private string DetermineProvider(System.Security.Claims.ClaimsPrincipal user)
    {
        // Check for provider-specific claims
        var aud = user.FindFirst("aud")?.Value ?? "";
        var issuer = user.FindFirst("iss")?.Value ?? "";

        if (aud.Contains("github") || issuer.Contains("github"))
            return "github";
        if (aud.Contains("google") || issuer.Contains("accounts.google"))
            return "google";

        return "email"; // Default for email/password auth
    }
}
