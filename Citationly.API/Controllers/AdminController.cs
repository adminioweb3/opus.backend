using Dapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Interfaces;
using Citationly.Application.Features.Onboarding;
using Citationly.Application.Features.PromptIntelligence.Services;
using Citationly.Infrastructure.Database;
using FirebaseAdmin.Auth;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Citationly.API.Controllers;

// Deliberately NOT [Authorize] — this must work even against a database with zero Users rows
// (a fresh reset, or before anyone has ever synced). Protected instead by a shared secret that
// only you know, set via the Admin__ResetSecret environment variable on the server. Never
// reachable without it, regardless of ASPNETCORE_ENVIRONMENT.
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;
    private readonly IPromptIntelligenceFirstRunService _firstRunService;
    private readonly IMediator _mediator;

    public AdminController(
        IDbConnectionFactory dbConnectionFactory,
        IConfiguration configuration,
        ILogger<AdminController> logger,
        IPromptIntelligenceFirstRunService firstRunService,
        IMediator mediator)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _configuration = configuration;
        _logger = logger;
        _firstRunService = firstRunService;
        _mediator = mediator;
    }

    private bool IsAuthorized(string? providedSecret)
    {
        var configuredSecret = _configuration["Admin:ResetSecret"];
        // Fail closed: if no secret is configured on the server, these endpoints are unusable
        // rather than silently open to anyone.
        return !string.IsNullOrEmpty(configuredSecret) && providedSecret == configuredSecret;
    }

    // Wipes every row from every application table but leaves the schema (tables, columns,
    // functions) exactly as-is. Use this for "same shape, fresh data" testing resets.
    [HttpPost("database/clear")]
    public async Task<IActionResult> ClearDatabase([FromHeader(Name = "X-Admin-Secret")] string? secret)
    {
        if (!IsAuthorized(secret))
            return Unauthorized(new { message = "Missing or invalid X-Admin-Secret header." });

        using var connection = _dbConnectionFactory.CreateConnection();

        // Only the app's own schema — Hangfire keeps its tables in a separate "hangfire" schema,
        // so its job/queue state is untouched by this.
        var tables = (await connection.QueryAsync<string>(
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public'")).ToList();

        if (tables.Count == 0)
            return Ok(new { message = "No tables found — nothing to clear." });

        var truncateSql = $"TRUNCATE TABLE {string.Join(", ", tables.Select(t => $"\"{t}\""))} RESTART IDENTITY CASCADE;";
        await connection.ExecuteAsync(truncateSql);

        _logger.LogWarning("Database CLEARED via /api/Admin/database/clear — {Count} tables truncated: {Tables}", tables.Count, string.Join(", ", tables));
        return Ok(new { message = $"Cleared {tables.Count} tables. Schema unchanged.", tables });
    }

    // Drops everything and recreates the schema from scratch — equivalent to a brand-new
    // database. Runs init.sql (the canonical schema) followed by the same self-healing
    // migration Program.cs applies on every startup, so tables added after init.sql was last
    // updated (GEO dashboard tables, Content Studio, Team invites, etc.) still get created.
    [HttpPost("database/reset")]
    public async Task<IActionResult> ResetDatabase([FromHeader(Name = "X-Admin-Secret")] string? secret)
    {
        if (!IsAuthorized(secret))
            return Unauthorized(new { message = "Missing or invalid X-Admin-Secret header." });

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
        await connection.ExecuteAsync(SelfHealingMigrations.Sql);

        _logger.LogWarning("Database RESET via /api/Admin/database/reset — full schema drop & recreate.");
        return Ok(new { message = "Database reset — fresh schema created from init.sql, all data gone." });
    }

    // Manually (re-)runs Answer Atlas's first-analysis batch for one org — the same job
    // CompleteOnboardingCommand enqueues automatically for newly onboarding orgs. Exists so an
    // already-onboarded org (from before that hook existed) can be backfilled with real data on
    // demand, without needing that org's own user session/token. Awaited (not enqueued) so the
    // caller sees the real outcome immediately instead of firing blind.
    [HttpPost("prompt-intelligence/run-first-batch/{organizationId}")]
    public async Task<IActionResult> RunFirstBatch(Guid organizationId, [FromHeader(Name = "X-Admin-Secret")] string? secret)
    {
        if (!IsAuthorized(secret))
            return Unauthorized(new { message = "Missing or invalid X-Admin-Secret header." });

        await _firstRunService.RunFirstBatchAsync(organizationId);

        _logger.LogWarning("Prompt Intelligence first-run batch manually triggered via Admin API for org {OrganizationId}", organizationId);
        return Ok(new { message = $"First-run batch completed for org {organizationId}." });
    }

    // Manually forces an immediate Company Knowledge Graph refresh + competitor re-discovery for
    // one org, bypassing the normal 30-day staleness window — for backfill/testing only. Runs
    // the exact same AnalyzeCompetitorsCommand the /onboarding/analyze-competitors endpoint uses.
    [HttpPost("companies/refresh/{organizationId}")]
    public async Task<IActionResult> RefreshCompany(Guid organizationId, [FromHeader(Name = "X-Admin-Secret")] string? secret)
    {
        if (!IsAuthorized(secret))
            return Unauthorized(new { message = "Missing or invalid X-Admin-Secret header." });

        var result = await _mediator.Send(new AnalyzeCompetitorsCommand { OrganizationId = organizationId });

        _logger.LogWarning("Company Knowledge Graph refresh manually triggered via Admin API for org {OrganizationId}", organizationId);
        return Ok(result);
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromHeader(Name = "X-Admin-Secret")] string? secret)
    {
        if (!IsAuthorized(secret))
            return Unauthorized(new { message = "Missing or invalid X-Admin-Secret header." });

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
            ORDER BY u.CreatedAt DESC;
        ";
        var users = await connection.QueryAsync<AdminUserRow>(sql);
        return Ok(users);
    }

    [HttpGet("users/all")]
    public async Task<IActionResult> GetAllUsersIncludingFirebase([FromHeader(Name = "X-Admin-Secret")] string? secret)
    {
        if (!IsAuthorized(secret))
            return Unauthorized(new { message = "Missing or invalid X-Admin-Secret header." });

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
                ORDER BY u.CreatedAt DESC;
            ");

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
                        var existingUser = allUsers.FirstOrDefault(u => u.Email == fbUser.Email);
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
            _logger.LogError("Error fetching all users: {Message}", ex.Message);
            return StatusCode(500, new { message = "Error fetching users", error = ex.Message });
        }

        return Ok(allUsers.OrderByDescending(u => u.UserCreatedAt));
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(Guid id, [FromHeader(Name = "X-Admin-Secret")] string? secret)
    {
        if (!IsAuthorized(secret))
            return Unauthorized(new { message = "Missing or invalid X-Admin-Secret header." });

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
            _logger.LogError("Error deleting user {UserId}: {Message}", id, ex.Message);
            return StatusCode(500, new { message = "Failed to delete user", error = ex.Message });
        }
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
