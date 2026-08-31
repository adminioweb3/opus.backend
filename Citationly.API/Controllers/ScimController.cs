using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

namespace Citationly.API.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("GeneralAuth")]
[Route("scim/v2")]
public class ScimController : ControllerBase
{
    private readonly ISsoRepository _ssoRepository;
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IAuditLogService _auditLogService;

    public ScimController(ISsoRepository ssoRepository, IDbConnectionFactory dbConnectionFactory, IAuditLogService auditLogService)
    {
        _ssoRepository = ssoRepository;
        _dbConnectionFactory = dbConnectionFactory;
        _auditLogService = auditLogService;
    }

    [HttpGet("ServiceProviderConfig")]
    public IActionResult ServiceProviderConfig()
    {
        return Ok(new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig" },
            patch = new { supported = true },
            bulk = new { supported = false },
            filter = new { supported = true, maxResults = 100 },
            changePassword = new { supported = false },
            sort = new { supported = false },
            etag = new { supported = false },
            authenticationSchemes = new[]
            {
                new { type = "oauthbearertoken", name = "Bearer token", description = "Use the org-scoped SCIM token generated in Enterprise SSO settings." }
            }
        });
    }

    [HttpGet("Users")]
    public async Task<IActionResult> GetUsers([FromQuery(Name = "filter")] string? filter = null, [FromQuery] int startIndex = 1, [FromQuery] int count = 100)
    {
        var sso = await AuthenticateScimAsync();
        if (sso.Result != null) return sso.Result;

        var emailFilter = TryExtractEmailFilter(filter);
        using var connection = _dbConnectionFactory.CreateConnection();
        var users = (await connection.QueryAsync<ScimUserRow>(
            """
            SELECT Id, Email, DisplayName, Role, CreatedAt
            FROM Users
            WHERE OrganizationId = @OrganizationId
              AND (@Email = '' OR LOWER(Email) = LOWER(@Email))
            ORDER BY CreatedAt DESC
            LIMIT @Count OFFSET @Offset
            """,
            new
            {
                OrganizationId = sso.Connection!.OrganizationId,
                Email = emailFilter ?? string.Empty,
                Count = Math.Clamp(count, 1, 100),
                Offset = Math.Max(startIndex - 1, 0)
            })).ToList();

        return Ok(new
        {
            schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:ListResponse" },
            totalResults = users.Count,
            startIndex = Math.Max(startIndex, 1),
            itemsPerPage = users.Count,
            Resources = users.Select(ToScimUser)
        });
    }

    [HttpGet("Users/{id:guid}")]
    public async Task<IActionResult> GetUser(Guid id)
    {
        var sso = await AuthenticateScimAsync();
        if (sso.Result != null) return sso.Result;

        using var connection = _dbConnectionFactory.CreateConnection();
        var user = await connection.QueryFirstOrDefaultAsync<ScimUserRow>(
            """
            SELECT Id, Email, DisplayName, Role, CreatedAt
            FROM Users
            WHERE Id = @Id AND OrganizationId = @OrganizationId
            """,
            new { Id = id, sso.Connection!.OrganizationId });

        return user == null ? NotFound() : Ok(ToScimUser(user));
    }

    [HttpGet("Groups")]
    public async Task<IActionResult> GetGroups([FromQuery] int startIndex = 1, [FromQuery] int count = 100)
    {
        var sso = await AuthenticateScimAsync();
        if (sso.Result != null) return sso.Result;

        return Ok(new
        {
            schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:ListResponse" },
            totalResults = 0,
            startIndex = Math.Max(startIndex, 1),
            itemsPerPage = 0,
            Resources = Array.Empty<object>()
        });
    }

    [HttpPost("Users")]
    public async Task<IActionResult> CreateUser([FromBody] ScimUserRequest request)
    {
        var sso = await AuthenticateScimAsync();
        if (sso.Result != null) return sso.Result;

        var email = request.UserName?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) return BadRequest(new { detail = "userName/email is required." });

        using var connection = _dbConnectionFactory.CreateConnection();
        var displayName = request.DisplayName ?? request.Name?.Formatted ?? email;
        var role = NormalizeRole(request.Role);

        var existingInOrganization = await connection.QueryFirstOrDefaultAsync<ScimUserRow>(
            """
            SELECT Id, Email, DisplayName, Role, CreatedAt
            FROM Users
            WHERE OrganizationId = @OrganizationId AND LOWER(Email) = LOWER(@Email)
            """,
            new { sso.Connection!.OrganizationId, Email = email });

        Guid id;
        if (existingInOrganization != null)
        {
            id = existingInOrganization.Id;
            await connection.ExecuteAsync(
                """
                UPDATE Users
                SET DisplayName = @DisplayName, Role = @Role
                WHERE Id = @Id AND OrganizationId = @OrganizationId
                """,
                new { Id = id, sso.Connection.OrganizationId, DisplayName = displayName, Role = role });
        }
        else
        {
            var existingEmailOwner = await connection.ExecuteScalarAsync<Guid?>(
                "SELECT OrganizationId FROM Users WHERE LOWER(Email) = LOWER(@Email) LIMIT 1",
                new { Email = email });

            if (existingEmailOwner.HasValue && existingEmailOwner.Value != sso.Connection.OrganizationId)
            {
                return Conflict(new { detail = "A user with this email already exists outside this SCIM organization." });
            }

            try
            {
                id = await connection.ExecuteScalarAsync<Guid>(
                    """
                    INSERT INTO Users (OrganizationId, FirebaseUid, Email, DisplayName, Role)
                    VALUES (@OrganizationId, @FirebaseUid, @Email, @DisplayName, @Role)
                    RETURNING Id
                    """,
                    new
                    {
                        sso.Connection.OrganizationId,
                        FirebaseUid = $"scim:{sso.Connection.OrganizationId}:{email}",
                        Email = email,
                        DisplayName = displayName,
                        Role = role
                    });
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return Conflict(new { detail = "A user with this email already exists outside this SCIM organization." });
            }
        }

        await _auditLogService.RecordAsync("scim.user.upsert", "Security", "Success", sso.Connection.OrganizationId, targetType: "User", targetId: id.ToString(), actorType: "ScimClient", ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, userAgent: Request.Headers.UserAgent.ToString(), cancellationToken: HttpContext.RequestAborted);

        return Created($"/scim/v2/Users/{id}", ToScimUser(new ScimUserRow { Id = id, Email = email, DisplayName = displayName, Role = role, CreatedAt = DateTime.UtcNow }));
    }

    [HttpPatch("Users/{id:guid}")]
    public async Task<IActionResult> PatchUser(Guid id, [FromBody] ScimPatchRequest request)
    {
        var sso = await AuthenticateScimAsync();
        if (sso.Result != null) return sso.Result;

        var activeOperation = request.Operations?
            .FirstOrDefault(o => string.Equals(o.Path, "active", StringComparison.OrdinalIgnoreCase));
        if (activeOperation != null && TryReadBoolean(activeOperation.Value, out var active) && !active)
        {
            return await DeleteUser(id);
        }

        var requestedRole = request.Operations?
            .FirstOrDefault(o => string.Equals(o.Path, "role", StringComparison.OrdinalIgnoreCase))
            ?.Value?.ToString();

        if (string.IsNullOrWhiteSpace(requestedRole))
        {
            return Ok(new { id, message = "No supported SCIM patch operation supplied." });
        }

        using var connection = _dbConnectionFactory.CreateConnection();
        var rows = await connection.ExecuteAsync(
            "UPDATE Users SET Role = @Role WHERE Id = @Id AND OrganizationId = @OrganizationId",
            new { Id = id, sso.Connection!.OrganizationId, Role = NormalizeRole(requestedRole) });

        await _auditLogService.RecordAsync("scim.user.patch", "Security", rows > 0 ? "Success" : "NotFound", sso.Connection.OrganizationId, targetType: "User", targetId: id.ToString(), actorType: "ScimClient", ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, userAgent: Request.Headers.UserAgent.ToString(), cancellationToken: HttpContext.RequestAborted);
        return rows == 0 ? NotFound() : NoContent();
    }

    [HttpDelete("Users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var sso = await AuthenticateScimAsync();
        if (sso.Result != null) return sso.Result;

        using var connection = _dbConnectionFactory.CreateConnection();
        var rows = await connection.ExecuteAsync(
            "DELETE FROM Users WHERE Id = @Id AND OrganizationId = @OrganizationId",
            new { Id = id, sso.Connection!.OrganizationId });

        await _auditLogService.RecordAsync("scim.user.deprovision", "Security", rows > 0 ? "Success" : "NotFound", sso.Connection.OrganizationId, targetType: "User", targetId: id.ToString(), actorType: "ScimClient", ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, userAgent: Request.Headers.UserAgent.ToString(), cancellationToken: HttpContext.RequestAborted);
        return rows == 0 ? NotFound() : NoContent();
    }

    private async Task<(Citationly.Domain.Entities.SsoConnection? Connection, IActionResult? Result)> AuthenticateScimAsync()
    {
        var auth = Request.Headers.Authorization.ToString();
        var token = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth["Bearer ".Length..].Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(token)) return (null, Unauthorized());

        var connection = await _ssoRepository.GetByScimTokenHashAsync(HashToken(token), HttpContext.RequestAborted);
        return connection == null ? (null, Unauthorized()) : (connection, null);
    }

    private static object ToScimUser(ScimUserRow user) => new
    {
        schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
        id = user.Id,
        userName = user.Email,
        displayName = user.DisplayName,
        active = true,
        roles = new[] { new { value = user.Role, primary = true } },
        meta = new { resourceType = "User", created = user.CreatedAt }
    };

    private static string NormalizeRole(string? role)
    {
        return role?.Trim() switch
        {
            "Owner" => "Owner",
            "Admin" => "Admin",
            "Manager" => "Manager",
            "Editor" => "Editor",
            _ => "Viewer"
        };
    }

    private static string? TryExtractEmailFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return null;
        const string marker = "userName eq ";
        var idx = filter.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        return filter[(idx + marker.Length)..].Trim().Trim('"', '\'');
    }

    private static bool TryReadBoolean(object? value, out bool result)
    {
        switch (value)
        {
            case bool direct:
                result = direct;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                result = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                result = false;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element
                when bool.TryParse(element.GetString(), out var parsed):
                result = parsed;
                return true;
            case string text when bool.TryParse(text, out var parsed):
                result = parsed;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

public class ScimUserRow
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "Viewer";
    public DateTime CreatedAt { get; set; }
}

public class ScimUserRequest
{
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public ScimName? Name { get; set; }
    public string? Role { get; set; }
}

public class ScimName
{
    public string? Formatted { get; set; }
}

public class ScimPatchRequest
{
    public List<ScimPatchOperation>? Operations { get; set; }
}

public class ScimPatchOperation
{
    public string? Op { get; set; }
    public string? Path { get; set; }
    public object? Value { get; set; }
}
