using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Citationly.API.Services;
using Citationly.Application.Features.Onboarding;
using Citationly.Application.Features.PromptIntelligence.Services;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Citationly.Infrastructure.Database;
using Dapper;
using FirebaseAdmin.Auth;
using Hangfire;
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
    private const string ClearDatabaseConfirmation = "CLEAR_DATABASE";
    private const string ResetDatabaseConfirmation = "RESET_DATABASE";
    private const string DeleteOrganizationConfirmation = "DELETE_ORGANIZATION_DATA";
    private const string RetryFailedJobConfirmation = "RETRY_FAILED_JOB";
    private static readonly string[] OrganizationDeletionTables =
    {
        "AlertThresholds",
        "Alerts",
        "ApiKeys",
        "AuditLogs",
        "BrandClaims",
        "BrandFactChecks",
        "BrandPulseScanSummaries",
        "CitationScanSummaries",
        "CitationSourceSnapshots",
        "Competitors",
        "CompetitorSnapshots",
        "ContentDrafts",
        "ContentOptimizations",
        "CrossEngineConsensusInsights",
        "DashboardSnapshots",
        "DataDeletionRequests",
        "Embeddings",
        "GeoPillars",
        "HistoricalScans",
        "Invites",
        "KnowledgeBases",
        "OpportunitySnapshots",
        "PromptTopics",
        "RecommendationImplementations",
        "Reports",
        "RetentionPolicies",
        "SsoConnections",
        "UsageCounters",
        "Websites",
        "WebsiteProfiles"
    };

    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;
    private readonly IPromptIntelligenceFirstRunService _firstRunService;
    private readonly IMediator _mediator;
    private readonly IMemoryCache _cache;
    private readonly IScrapingJobRepository _scrapingJobRepository;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public AdminController(
        IDbConnectionFactory dbConnectionFactory,
        IConfiguration configuration,
        ILogger<AdminController> logger,
        IPromptIntelligenceFirstRunService firstRunService,
        IMediator mediator,
        IMemoryCache cache,
        IScrapingJobRepository scrapingJobRepository,
        IBackgroundJobClient backgroundJobClient)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _configuration = configuration;
        _logger = logger;
        _firstRunService = firstRunService;
        _mediator = mediator;
        _cache = cache;
        _scrapingJobRepository = scrapingJobRepository;
        _backgroundJobClient = backgroundJobClient;
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

        if (!string.Equals(request.Username?.Trim(), configuredUsername.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _cache.Set(cacheKey, attempts + 1, TimeSpan.FromMinutes(15));
            return Unauthorized(new { message = "Invalid admin credentials." });
        }

        bool passwordMatches;
        try
        {
            passwordMatches = BCrypt.Net.BCrypt.Verify(request.Password, configuredPasswordHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin password hash is invalid or unreadable.");
            return StatusCode(500, new { message = "Admin password hash is invalid on the server." });
        }

        if (!passwordMatches)
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
        if (!HasDestructiveConfirmation(ClearDatabaseConfirmation))
        {
            return StatusCode(428, new { message = $"Missing X-Admin-Confirm: {ClearDatabaseConfirmation}." });
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
        if (!HasDestructiveConfirmation(ResetDatabaseConfirmation))
        {
            return StatusCode(428, new { message = $"Missing X-Admin-Confirm: {ResetDatabaseConfirmation}." });
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

    [HttpGet("support/organizations")]
    public async Task<IActionResult> GetSupportOrganizations([FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        using var connection = _dbConnectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AdminSupportOrganizationRow>(
            """
            WITH user_counts AS (
                SELECT OrganizationId, COUNT(*)::int AS UserCount
                FROM Users
                GROUP BY OrganizationId
            ),
            website_counts AS (
                SELECT OrganizationId, COUNT(*)::int AS WebsiteCount
                FROM Websites
                GROUP BY OrganizationId
            ),
            latest_subscriptions AS (
                SELECT DISTINCT ON (OrganizationId)
                    OrganizationId,
                    Status
                FROM Subscriptions
                WHERE Status IN ('active', 'trialing', 'past_due', 'ACTIVE', 'TRIALING', 'PAST_DUE')
                ORDER BY OrganizationId, UpdatedAt DESC NULLS LAST, CreatedAt DESC
            ),
            usage_today AS (
                SELECT OrganizationId, SUM(Count)::bigint AS UsageToday
                FROM UsageCounters
                WHERE PeriodStart >= CURRENT_DATE
                GROUP BY OrganizationId
            ),
            scraping_job_counts AS (
                SELECT
                    OrganizationId,
                    COUNT(*) FILTER (WHERE Status IN ('Pending', 'Processing'))::int AS ActiveJobCount,
                    COUNT(*) FILTER (WHERE Status = 'Failed')::int AS FailedJobCount
                FROM ScrapingJobs
                GROUP BY OrganizationId
            ),
            provider_failures AS (
                SELECT
                    pt.OrganizationId,
                    COUNT(pr.Id)::int AS ProviderFailureCount,
                    MAX(pr.CreatedAt) AS LastProviderFailureAt
                FROM PromptResponses pr
                JOIN PromptAnalysis pa ON pa.Id = pr.PromptAnalysisId
                JOIN PromptQuestions pq ON pq.Id = pa.PromptQuestionId
                JOIN PromptTopics pt ON pt.Id = pq.PromptTopicId
                WHERE pr.IsError = TRUE
                GROUP BY pt.OrganizationId
            )
            SELECT
                o.Id AS OrganizationId,
                o.Name AS OrganizationName,
                o.PlanType,
                o.CreatedAt AS OrganizationCreatedAt,
                COALESCE(uc.UserCount, 0) AS UserCount,
                COALESCE(wc.WebsiteCount, 0) AS WebsiteCount,
                COALESCE(ls.Status, 'none') AS SubscriptionStatus,
                COALESCE(ut.UsageToday, 0)::bigint AS UsageToday,
                COALESCE(sjc.ActiveJobCount, 0) AS ActiveJobCount,
                COALESCE(sjc.FailedJobCount, 0) AS FailedJobCount,
                COALESCE(pf.ProviderFailureCount, 0) AS ProviderFailureCount,
                pf.LastProviderFailureAt
            FROM Organizations o
            LEFT JOIN user_counts uc ON uc.OrganizationId = o.Id
            LEFT JOIN website_counts wc ON wc.OrganizationId = o.Id
            LEFT JOIN latest_subscriptions ls ON ls.OrganizationId = o.Id
            LEFT JOIN usage_today ut ON ut.OrganizationId = o.Id
            LEFT JOIN scraping_job_counts sjc ON sjc.OrganizationId = o.Id
            LEFT JOIN provider_failures pf ON pf.OrganizationId = o.Id
            ORDER BY o.CreatedAt DESC
            LIMIT @Limit
            """,
            new { Limit = limit });

        return Ok(rows);
    }

    [HttpGet("support/scraping-jobs")]
    public async Task<IActionResult> GetSupportScrapingJobs(
        [FromQuery] Guid? organizationId = null,
        [FromQuery] string? status = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0)
    {
        var jobs = await _scrapingJobRepository.GetOperatorJobsAsync(
            organizationId,
            status,
            Math.Clamp(limit, 1, 500),
            Math.Max(offset, 0));

        return Ok(jobs.Select(SupportScrapingJobRow.FromJob));
    }

    [HttpPost("support/scraping-jobs/{id}/retry")]
    [AuditAction("admin.scraping-job.retry", "Operations", "ScrapingJob")]
    public async Task<IActionResult> RetryFailedScrapingJob(Guid id)
    {
        if (!HasDestructiveConfirmation(RetryFailedJobConfirmation))
        {
            return StatusCode(428, new { message = $"Missing X-Admin-Confirm: {RetryFailedJobConfirmation}." });
        }

        var job = await _scrapingJobRepository.GetJobAsync(id);
        if (job is null)
        {
            return NotFound(new { message = "Scraping job not found." });
        }

        if (!string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Only failed scraping jobs can be manually retried." });
        }

        job.Status = "Pending";
        job.StartedAt = null;
        job.CompletedAt = null;
        await _scrapingJobRepository.UpdateJobAsync(job);
        var hangfireJobId = _backgroundJobClient.Enqueue<IScrapingJobService>(svc => svc.ProcessJobAsync(id));

        return Ok(new { job.Id, job.OrganizationId, job.Status, HangfireJobId = hangfireJobId });
    }

    [HttpDelete("users/{id}")]
    [AuditAction("admin.user.delete", "Destructive", "User")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        if (!HasDestructiveConfirmation(DeleteOrganizationConfirmation))
        {
            return StatusCode(428, new { message = $"Missing X-Admin-Confirm: {DeleteOrganizationConfirmation}." });
        }

        using var connection = _dbConnectionFactory.CreateConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        // Find the organization this user belongs to
        var orgId = await connection.QuerySingleOrDefaultAsync<Guid>(
            "SELECT OrganizationId FROM Users WHERE Id = @Id", new { Id = id });

        if (orgId == Guid.Empty)
        {
            return NotFound(new { message = "User not found." });
        }

        try
        {
            using var transaction = connection.BeginTransaction();
            foreach (var table in await GetExistingOrganizationScopedTablesAsync(connection, transaction))
            {
                var quotedTable = "\"" + table.Replace("\"", "\"\"") + "\"";
                await connection.ExecuteAsync(
                    $"DELETE FROM {quotedTable} WHERE OrganizationId = @OrgId",
                    new { OrgId = orgId },
                    transaction);
            }

            await connection.ExecuteAsync("DELETE FROM Users WHERE OrganizationId = @OrgId", new { OrgId = orgId }, transaction);
            await connection.ExecuteAsync("DELETE FROM Organizations WHERE Id = @OrgId", new { OrgId = orgId }, transaction);
            transaction.Commit();

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

    private bool HasDestructiveConfirmation(string expected)
    {
        return string.Equals(Request.Headers["X-Admin-Confirm"].ToString(), expected, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<string>> GetExistingOrganizationScopedTablesAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction)
    {
        var existing = await connection.QueryAsync<string>(
            """
            SELECT table_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND LOWER(column_name) = 'organizationid'
              AND table_name = ANY(@Tables)
            """,
            new { Tables = OrganizationDeletionTables },
            transaction);

        return existing
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(table => table.Equals("PromptTopics", StringComparison.OrdinalIgnoreCase))
            .ThenBy(table => table)
            .ToList();
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

file class AdminSupportOrganizationRow
{
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public DateTime OrganizationCreatedAt { get; set; }
    public int UserCount { get; set; }
    public int WebsiteCount { get; set; }
    public string SubscriptionStatus { get; set; } = string.Empty;
    public long UsageToday { get; set; }
    public int ActiveJobCount { get; set; }
    public int FailedJobCount { get; set; }
    public int ProviderFailureCount { get; set; }
    public DateTime? LastProviderFailureAt { get; set; }
}

file record SupportScrapingJobRow(
    Guid Id,
    Guid OrganizationId,
    Guid? WebsiteId,
    Guid? KnowledgeBaseId,
    string Url,
    string Status,
    string ScrapeType,
    int ProcessedPages,
    int SuccessfulPages,
    int FailedPages,
    int TotalPages,
    int MaxPages,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime CreatedAt)
{
    public static SupportScrapingJobRow FromJob(ScrapingJob job) => new(
        job.Id,
        job.OrganizationId,
        job.WebsiteId,
        job.KnowledgeBaseId,
        job.Url,
        job.Status,
        job.ScrapeType,
        job.ProcessedPages,
        job.SuccessfulPages,
        job.FailedPages,
        job.TotalPages,
        job.MaxPages,
        job.StartedAt,
        job.CompletedAt,
        job.CreatedAt);
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
