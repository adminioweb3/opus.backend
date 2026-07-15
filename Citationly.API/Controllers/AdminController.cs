using Dapper;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Interfaces;
using Citationly.Infrastructure.Database;

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

    public AdminController(IDbConnectionFactory dbConnectionFactory, IConfiguration configuration, ILogger<AdminController> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _configuration = configuration;
        _logger = logger;
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
}
