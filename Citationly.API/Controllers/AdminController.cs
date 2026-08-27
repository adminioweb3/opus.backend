using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Citationly.API.Services;
using Citationly.Application.Features.Onboarding;
using Citationly.Application.Features.PromptIntelligence.Services;
using Citationly.Application.Interfaces;
using Citationly.Infrastructure.Database;
using Dapper;
using FirebaseAdmin.Auth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace Citationly.API.Controllers;

[Authorize(AuthenticationSchemes = "AdminJwt", Roles = "Admin")]
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;
    private readonly IPromptIntelligenceFirstRunService _firstRunService;
    private readonly IMediator _mediator;
    private readonly IMemoryCache _cache;

    public AdminController(
        IDbConnectionFactory dbConnectionFactory,
        IConfiguration configuration,
        ILogger<AdminController> logger,
        IPromptIntelligenceFirstRunService firstRunService,
        IMediator mediator,
        IMemoryCache cache)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _configuration = configuration;
        _logger = logger;
        _firstRunService = firstRunService;
        _mediator = mediator;
        _cache = cache;
    }

    [AllowAnonymous]
    [EnableRateLimiting("AdminLogin")]
    [HttpPost("login")]
    [AuditAction("admin.login", "Authentication", "AdminSession")]
    public IActionResult Login([FromBody] AdminLoginRequest request)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cacheKey = $"admin_login_attempts_{clientIp}_{request.Username}";
        var attempts = _cache.GetOrCreate(cacheKey, entry => 
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            return 0;
        });

        if (attempts >= 5)
        {
            return StatusCode(429, new { message = "Account locked out due to too many failed attempts. Try again later." });
        }

        var configuredUsername = _configuration["Admin:Username"];
        var configuredPasswordHash = _configuration["Admin:PasswordHash"];
        var signingKey = _configuration["Admin:JwtSigningKey"];
        var issuer = _configuration["Admin:JwtIssuer"] ?? "Citationly.Admin";
        var audience = _configuration["Admin:JwtAudience"] ?? "Citationly.Admin.Panel";

        if (string.IsNullOrWhiteSpace(configuredUsername) || string.IsNullOrWhiteSpace(configuredPasswordHash) || string.IsNullOrWhiteSpace(signingKey))
            return StatusCode(500, new { message = "Admin authentication is not configured on the server." });

        if (!string.Equals(request.Username?.Trim(), configuredUsername.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !BCrypt.Net.BCrypt.Verify(request.Password, configuredPasswordHash))
        {
            _cache.Set(cacheKey, attempts + 1, TimeSpan.FromMinutes(15));
            return Unauthorized(new { message = "Invalid admin credentials." });
        }

        _cache.Remove(cacheKey);
        var expiresAt = DateTime.UtcNow.AddHours(1);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, request.Username!.Trim()),
            new Claim(ClaimTypes.Name, request.Username!.Trim()),
            new Claim(ClaimTypes.Role, "Admin")
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return Ok(new AdminLoginResponse
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = expiresAt,
            Role = "Admin"
        });
    }

    [Authorize(AuthenticationSchemes = "AdminJwt", Roles = "Admin")]
    [HttpGet("session")]
    public IActionResult Session()
    {
        return Ok(new
        {
            authenticated = User.Identity?.IsAuthenticated == true,
            username = User.Identity?.Name,
            role = User.FindFirstValue(ClaimTypes.Role)
        });
    }

    // Wipes every row from every application table but leaves the schema (tables, columns,
    // functions) exactly as-is. Use this for "same shape, fresh data" testing resets.
    [HttpPost("database/clear")]
    [AuditAction("admin.database.clear", "Destructive", "Database")]
    public async Task<IActionResult> ClearDatabase()
    {
        if (IsProductionDestructiveDatabaseActionDisabled())
        {
            return StatusCode(403, new { message = "Destructive database actions are disabled in production." });
        }

        using var connection = _dbConnectionFactory.CreateConnection();

        // Only the app's own schema â€” Hangfire keeps its tables in a separate "hangfire" schema,
        // so its job/queue state is untouched by this.
        var tables = (await connection.QueryAsync<string>(
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public'")).ToList();

        if (tables.Count == 0)
            return Ok(new { message = "No tables found â€” nothing to clear." });

        var truncateSql = $"TRUNCATE TABLE {string.Join(", ", tables.Select(t => $"\"{t}\""))} RESTART IDENTITY CASCADE;";
        await connection.ExecuteAsync(truncateSql);

        _logger.LogWarning("Database CLEARED via /api/Admin/database/clear â€” {Count} tables truncated: {Tables}", tables.Count, string.Join(", ", tables));
        return Ok(new { message = $"Cleared {tables.Count} tables. Schema unchanged.", tables });
    }

    // Drops everything and recreates the schema from scratch â€” equivalent to a brand-new
    // database. Runs init.sql (the canonical schema) followed by the same self-healing
    // migration Program.cs applies on every startup, so tables added after init.sql was last
    // updated (GEO dashboard tables, Content Studio, Team invites, etc.) still get created.
    [HttpPost("database/reset")]
    [AuditAction("admin.database.reset", "Destructive", "Database")]
    public async Task<IActionResult> ResetDatabase()
    {
        if (IsProductionDestructiveDatabaseActionDisabled())
        {
            return StatusCode(403, new { message = "Destructive database actions are disabled in production." });
        }

        var assembly = typeof(SelfHealingMigrations).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("init.sql", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
            return StatusCode(500, new { message = "init.sql embedded resource not found." });

        string initSql;
        await using (var stream = assembly.GetManifestResourceStream(resourceName)!)
        using (var reader = new StreamReader(stream))
        {
            initSql = await reader.ReadToEndAsync();
        }

        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(initSql);

        var migrationRunner = HttpContext.RequestServices.GetRequiredService<DatabaseMigrationRunner>();
        await migrationRunner.RunPendingAsync(HttpContext.RequestAborted);

        _logger.LogWarning("Database RESET via /api/Admin/database/reset â€” full schema drop & recreate.");
        return Ok(new { message = "Database reset â€” fresh schema created from init.sql, all data gone." });
    }

    // Manually (re-)runs Answer Atlas's first-analysis batch for one org â€” the same job
    // CompleteOnboardingCommand enqueues automatically for newly onboarding orgs. Exists so an
    // already-onboarded org (from before that hook existed) can be backfilled with real data on
    // demand, without needing that org's own user session/token. Awaited (not enqueued) so the
    // caller sees the real outcome immediately instead of firing blind.
    [HttpPost("prompt-intelligence/run-first-batch/{organizationId}")]
    [AuditAction("admin.prompt_intelligence.run_first_batch", "AdminAction", "Organization")]
    public async Task<IActionResult> RunFirstBatch(Guid organizationId)
    {
        await _firstRunService.RunFirstBatchAsync(organizationId);

        _logger.LogWarning("Prompt Intelligence first-run batch manually triggered via Admin API for org {OrganizationId}", organizationId);
        return Ok(new { message = $"First-run batch completed for org {organizationId}." });
    }

    // Manually forces an immediate Company Knowledge Graph refresh + competitor re-discovery for
    // one org, bypassing the normal 30-day staleness window â€” for backfill/testing only. Runs
    // the exact same AnalyzeCompetitorsCommand the /onboarding/analyze-competitors endpoint uses.
    [HttpPost("companies/refresh/{organizationId}")]
    [AuditAction("admin.companies.refresh", "AdminAction", "Organization")]
    public async Task<IActionResult> RefreshCompany(Guid organizationId)
    {
        var result = await _mediator.Send(new AnalyzeCompetitorsCommand { OrganizationId = organizationId });

        _logger.LogWarning("Company Knowledge Graph refresh manually triggered via Admin API for org {OrganizationId}", organizationId);
        return Ok(result);
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            SELECT
                u.Id,
                u.Email,
                u.DisplayName,
                u.Role,
                u.CreatedAt as UserCreatedAt,
                o.Id as OrganizationId,
                o.Name as OrganizationName,
                o.PlanType,
                o.CreatedAt as OrganizationCreatedAt
            FROM Users u
            JOIN Organizations o ON u.OrganizationId = o.Id
            ORDER BY u.CreatedAt DESC
            LIMIT @Limit;
        ";
        var users = await connection.QueryAsync<AdminUserRow>(sql, new { Limit = limit });
        return Ok(users);
    }

    [HttpGet("users/all")]
    public async Task<IActionResult> GetAllUsersIncludingFirebase([FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        var allUsers = new List<AdminUserRow>();

        try
        {
            using var connection = _dbConnectionFactory.CreateConnection();
            var dbUsers = await connection.QueryAsync<AdminUserRow>(@"
                SELECT
                    u.Id,
                    u.Email,
                    u.DisplayName,
                    u.Role,
                    u.CreatedAt as UserCreatedAt,
                    o.Id as OrganizationId,
                    o.Name as OrganizationName,
                    o.PlanType,
                    o.CreatedAt as OrganizationCreatedAt
                FROM Users u
                JOIN Organizations o ON u.OrganizationId = o.Id
                ORDER BY u.CreatedAt DESC
                LIMIT @Limit;
            ", new { Limit = limit });

            allUsers.AddRange(dbUsers);
            _logger.LogInformation("Fetched {Count} database users", dbUsers.Count());

            try
            {
                var firebaseUsersList = new List<AdminUserRow>();

                if (FirebaseAuth.DefaultInstance == null)
                {
                    _logger.LogWarning("Firebase Admin SDK not initialized - GOOGLE_APPLICATION_CREDENTIALS not set");
                }
                else
                {
                    _logger.LogInformation("Firebase Admin SDK is initialized, fetching users...");
                    var pagedEnumerable = FirebaseAuth.DefaultInstance.ListUsersAsync(null);
                    var firebaseCount = 0;

                    await foreach (var fbUser in pagedEnumerable)
                    {
                        firebaseCount++;
                        var existingUser = allUsers.FirstOrDefault(u => string.Equals(u.Email, fbUser.Email, StringComparison.OrdinalIgnoreCase));
                        if (existingUser == null)
                        {
                            firebaseUsersList.Add(new AdminUserRow
                            {
                                Id = Guid.NewGuid(),
                                Email = fbUser.Email ?? "No Email",
                                DisplayName = fbUser.DisplayName ?? fbUser.Email?.Split('@').FirstOrDefault() ?? "Firebase User",
                                Role = "User",
                                UserCreatedAt = DateTime.UtcNow,
                                OrganizationId = Guid.Empty,
                                OrganizationName = "[No Organization]",
                                PlanType = "Trial",
                                OrganizationCreatedAt = DateTime.UtcNow
                            });
                        }

                        if (allUsers.Count + firebaseUsersList.Count >= limit)
                        {
                            break;
                        }
                    }

                    _logger.LogInformation("Fetched {TotalFirebase} Firebase users, {NewCount} not in database", firebaseCount, firebaseUsersList.Count);
                    allUsers.AddRange(firebaseUsersList);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not fetch Firebase users: {Message} | {Type}", ex.Message, ex.GetType().Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all users");
            return StatusCode(500, new { message = "Error fetching users." });
        }

        return Ok(allUsers.OrderByDescending(u => u.UserCreatedAt).Take(limit));
    }

    [HttpDelete("users/{id}")]
    [AuditAction("admin.user.delete", "Destructive", "User")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        // Find the organization this user belongs to
        var orgId = await connection.QuerySingleOrDefaultAsync<Guid>(
            "SELECT OrganizationId FROM Users WHERE Id = @Id", new { Id = id });

        if (orgId == Guid.Empty)
        {
            return NotFound(new { message = "User not found." });
        }

        try
        {
            // Delete in order to respect foreign key constraints
            // Only delete from tables that exist and have OrganizationId

            // 1. Delete competitor snapshots
            try { await connection.ExecuteAsync("DELETE FROM CompetitorSnapshots WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }

            // 2. Delete competitors
            try { await connection.ExecuteAsync("DELETE FROM Competitors WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }

            // 3. Delete websites and related data
            try { await connection.ExecuteAsync("DELETE FROM Websites WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }
            try { await connection.ExecuteAsync("DELETE FROM WebsiteProfiles WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }

            // 4. Delete content studio data
            try { await connection.ExecuteAsync("DELETE FROM ContentDrafts WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }
            try { await connection.ExecuteAsync("DELETE FROM ContentOptimizations WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }
            try { await connection.ExecuteAsync("DELETE FROM KnowledgeBases WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }
            try { await connection.ExecuteAsync("DELETE FROM SourceFolders WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }

            // 5. Delete reports, prompts, and other data
            try { await connection.ExecuteAsync("DELETE FROM Reports WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }
            try { await connection.ExecuteAsync("DELETE FROM Prompts WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }
            try { await connection.ExecuteAsync("DELETE FROM CompetitorComparisons WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }

            // 6. Delete geo/visibility data
            try { await connection.ExecuteAsync("DELETE FROM GeoPillars WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }
            try { await connection.ExecuteAsync("DELETE FROM PromptCoverages WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }
            try { await connection.ExecuteAsync("DELETE FROM WinLossEvents WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }

            // 7. Delete team invites
            try { await connection.ExecuteAsync("DELETE FROM Invites WHERE OrganizationId = @OrgId", new { OrgId = orgId }); } catch { }

            // 8. Delete all users in this organization
            await connection.ExecuteAsync("DELETE FROM Users WHERE OrganizationId = @OrgId", new { OrgId = orgId });

            // 9. Finally delete the organization itself
            await connection.ExecuteAsync("DELETE FROM Organizations WHERE Id = @OrgId", new { OrgId = orgId });

            _logger.LogWarning("Admin API deleted user {UserId} and wiped their organization {OrgId} with all related data", id, orgId);
            return Ok(new { message = "User and all associated organization data wiped successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", id);
            return StatusCode(500, new { message = "Failed to delete user." });
        }
    }

    private bool IsProductionDestructiveDatabaseActionDisabled()
    {
        var environmentName = _configuration["ASPNETCORE_ENVIRONMENT"] ?? _configuration["DOTNET_ENVIRONMENT"];
        var isProduction = string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);
        return isProduction && !_configuration.GetValue<bool>("Admin:AllowDestructiveDatabaseActions");
    }
}

file class AdminUserRow
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime UserCreatedAt { get; set; }
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public DateTime OrganizationCreatedAt { get; set; }
}

public class AdminLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AdminLoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string Role { get; set; } = string.Empty;
}
