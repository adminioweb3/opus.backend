using System.Security.Cryptography;
using System.Text;
using Citationly.API.Services;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/Enterprise/sso")]
public class EnterpriseSsoController : ControllerBase
{
    private readonly ICurrentOrganizationAccessor _currentOrganization;
    private readonly ISsoRepository _ssoRepository;

    public EnterpriseSsoController(ICurrentOrganizationAccessor currentOrganization, ISsoRepository ssoRepository)
    {
        _currentOrganization = currentOrganization;
        _ssoRepository = ssoRepository;
    }

    [HttpGet]
    [RequireOrgRole("Admin")]
    public async Task<IActionResult> Get()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized();

        var connection = await _ssoRepository.GetByOrganizationAsync(organizationId.Value, HttpContext.RequestAborted);
        return Ok(new
        {
            configured = connection != null,
            connection = connection == null ? null : new
            {
                connection.Id,
                connection.OrganizationId,
                connection.Provider,
                connection.Domain,
                connection.MetadataUrl,
                connection.EntityId,
                connection.IsEnabled,
                connection.ScimEnabled,
                hasScimToken = !string.IsNullOrWhiteSpace(connection.ScimTokenHash),
                connection.CreatedAt,
                connection.UpdatedAt
            },
            assertionConsumerServiceUrl = $"{Request.Scheme}://{Request.Host}/api/Enterprise/sso/acs",
            scimBaseUrl = $"{Request.Scheme}://{Request.Host}/scim/v2"
        });
    }

    [HttpPut]
    [RequireOrgRole("Admin")]
    [AuditAction("enterprise.sso.upsert", "Security", "SsoConnection")]
    public async Task<IActionResult> Upsert([FromBody] SsoConnectionRequest request)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized();

        var connection = await _ssoRepository.UpsertAsync(new SsoConnection
        {
            OrganizationId = organizationId.Value,
            Provider = string.IsNullOrWhiteSpace(request.Provider) ? "OIDC" : request.Provider.Trim(),
            Domain = request.Domain?.Trim().ToLowerInvariant() ?? string.Empty,
            MetadataUrl = request.MetadataUrl?.Trim() ?? string.Empty,
            EntityId = request.EntityId?.Trim() ?? string.Empty,
            IsEnabled = request.IsEnabled
        }, HttpContext.RequestAborted);

        return Ok(connection);
    }

    [HttpPost("scim-token")]
    [RequireOrgRole("Admin")]
    [AuditAction("enterprise.scim_token.rotate", "Security", "ScimToken")]
    public async Task<IActionResult> RotateScimToken()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized();

        var existing = await _ssoRepository.GetByOrganizationAsync(organizationId.Value, HttpContext.RequestAborted);
        if (existing == null)
        {
            return BadRequest(new { message = "Configure SSO before enabling SCIM provisioning." });
        }

        var token = "scim_" + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await _ssoRepository.SetScimTokenHashAsync(organizationId.Value, HashToken(token), HttpContext.RequestAborted);
        return Ok(new
        {
            token,
            message = "Copy this SCIM token now. It is stored hashed and will not be shown again."
        });
    }

    private static string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Base64UrlEncode(hashBytes);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public class SsoConnectionRequest
{
    public string? Provider { get; set; } = "OIDC";
    public string? Domain { get; set; }
    public string? MetadataUrl { get; set; }
    public string? EntityId { get; set; }
    public bool IsEnabled { get; set; }
}
