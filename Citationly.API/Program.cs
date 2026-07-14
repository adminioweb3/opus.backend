using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Hangfire;
using Hangfire.PostgreSql;
using Dapper;

using Citationly.Application;
using Citationly.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

// OpenAPI & Swagger
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// Add Clean Architecture Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// Configure Hangfire
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
            "https://citationly.ai",
            "https://www.citationly.ai",
            "http://localhost:3000"
        )
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

// Configure Firebase Authentication
var firebaseProjectId = builder.Configuration["Firebase:ProjectId"];
if (!string.IsNullOrEmpty(firebaseProjectId))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://securetoken.google.com/{firebaseProjectId}";
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = $"https://securetoken.google.com/{firebaseProjectId}",
                ValidateAudience = true,
                ValidAudience = firebaseProjectId,
                ValidateLifetime = true
            };
            // The "demo-token" bypass below is only wired up in Development — it's a hardcoded
            // skeleton key (any bearer "demo-token" authenticates as demo-user-id) and must never
            // be reachable outside local/dev environments.
            if (builder.Environment.IsDevelopment())
            {
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Token == "demo-token")
                        {
                            var claims = new[]
                            {
                                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "demo-user-id"),
                                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, "demo@example.com")
                            };
                            var identity = new System.Security.Claims.ClaimsIdentity(claims, "Demo");
                            context.Principal = new System.Security.Claims.ClaimsPrincipal(identity);
                            context.Success();
                        }
                        return Task.CompletedTask;
                    }
                };
            }
        });
}

var app = builder.Build();

// Self-healing migration: adds trial-subscription columns to Organizations (for orgs created
// before this feature existed) and re-applies sp_CreateOrGetUser with its advisory-lock fix,
// since init.sql only runs against a fresh database, not this live one.
using (var migrationScope = app.Services.CreateScope())
{
    var dbConnectionFactory = migrationScope.ServiceProvider.GetRequiredService<Citationly.Application.Interfaces.IDbConnectionFactory>();
    using var migrationConnection = dbConnectionFactory.CreateConnection();
    await migrationConnection.ExecuteAsync(@"
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS PlanType VARCHAR(50) NOT NULL DEFAULT 'Trial';
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS TrialEndsAt TIMESTAMP WITH TIME ZONE;
        UPDATE Organizations SET TrialEndsAt = CreatedAt + INTERVAL '7 days' WHERE TrialEndsAt IS NULL;

        CREATE OR REPLACE FUNCTION sp_CreateOrGetUser(
            p_FirebaseUid VARCHAR,
            p_Email VARCHAR,
            p_DisplayName VARCHAR
        )
        RETURNS TABLE (
            UserId UUID,
            OrganizationId UUID,
            Role VARCHAR
        ) AS $$
        DECLARE
            v_UserId UUID;
            v_OrganizationId UUID;
            v_Role VARCHAR;
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtext(p_FirebaseUid));

            SELECT u.Id, u.OrganizationId, u.Role INTO v_UserId, v_OrganizationId, v_Role
            FROM Users u WHERE u.FirebaseUid = p_FirebaseUid;

            IF v_UserId IS NULL THEN
                INSERT INTO Organizations (Name, PlanType, TrialEndsAt)
                VALUES (p_DisplayName || '''s Org', 'Trial', CURRENT_TIMESTAMP + INTERVAL '7 days')
                RETURNING Id INTO v_OrganizationId;

                INSERT INTO Users (OrganizationId, FirebaseUid, Email, DisplayName, Role)
                VALUES (v_OrganizationId, p_FirebaseUid, p_Email, p_DisplayName, 'Admin')
                RETURNING Id INTO v_UserId;

                v_Role := 'Admin';
            END IF;

            RETURN QUERY SELECT v_UserId, v_OrganizationId, v_Role;
        END;
        $$ LANGUAGE plpgsql;
    ");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Citationly API v1");
        c.RoutePrefix = "swagger"; // Serves Swagger UI at /swagger
    });
    app.UseHangfireDashboard("/hangfire");
}

// Daily check: re-runs a GEO scan for any organization whose last scan is 7+ days old
// (or has none yet), so scans stay fresh automatically without a manual "Run GEO scan" click.
RecurringJob.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.GeoScanRecurringJob>(
    "geo-scan-7-day-check",
    job => job.RunAsync(),
    Cron.Daily);

// Daily check: re-scans an organization's competitors + own rank once its last CompetitorSnapshot
// is 7+ days old (or missing), so Competitor Watch stays fresh automatically.
RecurringJob.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.CompetitorScanRecurringJob>(
    "competitor-scan-7-day-check",
    job => job.RunAsync(),
    Cron.Daily);

// Daily check: re-scans an organization's per-platform AI visibility once its last
// VisibilityScanSummary is 7+ days old (or missing), so Visibility Radar stays fresh automatically.
RecurringJob.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.VisibilityScanRecurringJob>(
    "visibility-scan-7-day-check",
    job => job.RunAsync(),
    Cron.Daily);

// Daily check: re-scans an organization's citation sources once its last CitationScanSummary
// is 7+ days old (or missing), so Citation Intelligence stays fresh automatically.
RecurringJob.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.CitationScanRecurringJob>(
    "citation-scan-7-day-check",
    job => job.RunAsync(),
    Cron.Daily);

// Daily check: re-scans an organization's brand pulse once its last BrandPulseScanSummary
// is 7+ days old (or missing), so Brand Pulse stays fresh automatically.
RecurringJob.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.BrandPulseScanRecurringJob>(
    "brand-pulse-scan-7-day-check",
    job => job.RunAsync(),
    Cron.Daily);

// Daily check: regenerates Command Center's business-insights summary once its last
// CommandCenterInsightSnapshot is 7+ days old (or missing).
RecurringJob.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.CommandCenterInsightsRecurringJob>(
    "command-center-insights-7-day-check",
    job => job.RunAsync(),
    Cron.Daily);

// Daily check: re-scans an organization's opportunities once its last OpportunitySnapshot
// is 7+ days old (or missing), so Opportunity Finder stays fresh automatically.
RecurringJob.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.OpportunityScanRecurringJob>(
    "opportunity-scan-7-day-check",
    job => job.RunAsync(),
    Cron.Daily);

app.UseExceptionHandler(errorApp =>
{
    // Re-apply CORS here: an unhandled exception clears the response
    // (including any headers UseCors already set), which is why the
    // browser reports a CORS failure on top of the real 500.
    errorApp.UseCors("AllowAll");
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(exceptionFeature?.Error, "Unhandled exception on {Path}", exceptionFeature?.Path);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { message = exceptionFeature?.Error?.ToString() ?? "An unexpected error occurred." });
    });
});

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
