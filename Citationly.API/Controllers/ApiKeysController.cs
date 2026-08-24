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
[Route("api/[controller]")]
public class ApiKeysController : ControllerBase
{
    private readonly ICurrentOrganizationAccessor _currentOrg;
    private readonly IApiKeyRepository _apiKeyRepository;

    public ApiKeysController(ICurrentOrganizationAccessor currentOrg, IApiKeyRepository apiKeyRepository)
    {
        _currentOrg = currentOrg;
        _apiKeyRepository = apiKeyRepository;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var keys = await _apiKeyRepository.GetApiKeysByOrgAsync(orgId.Value);
        return Ok(keys.Select(k => new ApiKeyListItem
        {
            Id = k.Id,
            Name = k.Name,
            KeyPrefix = k.KeyPrefix,
            Last4 = k.Last4,
            CreatedAt = k.CreatedAt,
            RevokedAt = k.RevokedAt,
            IsActive = k.RevokedAt == null
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Generate([FromBody] GenerateApiKeyRequest request)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "API key name is required." });
        }

        var rawKey = GenerateRawKey();
        var record = new ApiKey
        {
            OrganizationId = orgId.Value,
            Name = name,
            KeyPrefix = rawKey[..Math.Min(12, rawKey.Length)],
            KeyHash = HashKey(rawKey),
            Last4 = rawKey.Length >= 4 ? rawKey[^4..] : rawKey,
        };

        try
        {
            var id = await _apiKeyRepository.CreateApiKeyAsync(record);
            return Ok(new GenerateApiKeyResponse
            {
                Message = "API key generated successfully.",
                ApiKey = new ApiKeyCreated
                {
                    Id = id,
                    Name = record.Name,
                    Prefix = record.KeyPrefix,
                    Key = rawKey,
                    Last4 = record.Last4,
                    CreatedAt = DateTime.UtcNow
                }
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var revoked = await _apiKeyRepository.RevokeApiKeyAsync(id, orgId.Value, DateTime.UtcNow);
        return revoked ? Ok(new { message = "API key revoked." }) : NotFound(new { message = "API key not found." });
    }

    private static string GenerateRawKey()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return $"ck_live_{Base64UrlEncode(bytes)}";
    }

    private static string HashKey(string rawKey)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
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

public class GenerateApiKeyRequest
{
    public string Name { get; set; } = string.Empty;
}

public class ApiKeyListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsActive { get; set; }
}

public class ApiKeyCreated
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class GenerateApiKeyResponse
{
    public string Message { get; set; } = string.Empty;
    public ApiKeyCreated ApiKey { get; set; } = new();
}
