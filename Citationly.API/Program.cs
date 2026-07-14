using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Hangfire;
using Hangfire.PostgreSql;
using Dapper;

using Citationly.Application;
using Citationly.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
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

// Configure CORS
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
                    // Read the raw header directly: at this point in the pipeline the framework
                    // hasn't populated context.Token yet (that extraction happens AFTER
                    // OnMessageReceived returns, only if still empty), so comparing against
                    // context.Token here would never match.
                    OnMessageReceived = context =>
                    {
                        var rawAuthHeader = context.Request.Headers["Authorization"].ToString();
                        var bearerToken = rawAuthHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                            ? rawAuthHeader["Bearer ".Length..].Trim()
                            : null;

                        if (bearerToken == "demo-token")
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
// before this feature existed), backfills Websites.DomainUrl on databases created before that
// column existed (production was 500ing with "column domainurl does not exist" — WebsiteRepository
// has always required it, so the live table just predates the current schema), and re-applies
// sp_CreateOrGetUser with its advisory-lock fix, since init.sql only runs against a fresh
// database, not this live one.
using (var migrationScope = app.Services.CreateScope())
{
    var dbConnectionFactory = migrationScope.ServiceProvider.GetRequiredService<Citationly.Application.Interfaces.IDbConnectionFactory>();
    using var migrationConnection = dbConnectionFactory.CreateConnection();
    await migrationConnection.ExecuteAsync(@"
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS PlanType VARCHAR(50) NOT NULL DEFAULT 'Trial';
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS TrialEndsAt TIMESTAMP WITH TIME ZONE;
        UPDATE Organizations SET TrialEndsAt = CreatedAt + INTERVAL '7 days' WHERE TrialEndsAt IS NULL;

        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS DomainUrl VARCHAR(255) NOT NULL DEFAULT '';

        -- Postgres can't CREATE OR REPLACE a function whose RETURNS TABLE shape changed (only body
        -- changes are allowed in place) — drop first so this heals databases left over from an
        -- earlier version of this function with a different return signature.
        DROP FUNCTION IF EXISTS sp_CreateOrGetUser(VARCHAR, VARCHAR, VARCHAR);

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
            -- Lock on Email, not FirebaseUid: Email carries the actual unique constraint being
            -- protected here, including the email-fallback lookup below.
            PERFORM pg_advisory_xact_lock(hashtext(p_Email));

            SELECT u.Id, u.OrganizationId, u.Role INTO v_UserId, v_OrganizationId, v_Role
            FROM Users u WHERE u.FirebaseUid = p_FirebaseUid;

            IF v_UserId IS NULL THEN
                -- Firebase can issue a different UID for an email that already has a Users row
                -- (account recreated, a different sign-in provider linked, manual Firebase console
                -- edits). Email is the durable identity — re-link FirebaseUid to the existing row
                -- instead of INSERTing a second one and hitting users_email_key.
                SELECT u.Id, u.OrganizationId, u.Role INTO v_UserId, v_OrganizationId, v_Role
                FROM Users u WHERE u.Email = p_Email;

                IF v_UserId IS NOT NULL THEN
                    UPDATE Users SET FirebaseUid = p_FirebaseUid WHERE Id = v_UserId;
                END IF;
            END IF;

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

// Trust forwarded headers from Nginx
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto
});

// Global exception handling — without this, any unhandled exception (e.g. a DB connection
// failure) reaches the client as a bare 500 with no body and is never logged anywhere,
// making production failures nearly impossible to diagnose from outside.
app.UseExceptionHandler(errorApp =>
{
    // Re-apply CORS here: an unhandled exception clears the response (including any headers
    // UseCors already set), which is why the browser reports a CORS failure on top of the real 500.
    errorApp.UseCors("AllowFrontend");
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerPathFeature>();
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(exceptionFeature?.Error, "Unhandled exception on {Path}", exceptionFeature?.Path);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "An unexpected error occurred.",
            // Only leak exception detail outside Production — never expose stack traces to
            // real users, but make local/staging debugging easier.
            detail = app.Environment.IsDevelopment() ? exceptionFeature?.Error.Message : null,
        });
    });
});

// API docs — available in every environment (explicitly requested), not just Development.
app.MapOpenApi();
app.MapScalarApiReference();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Citationly API v1");
    c.RoutePrefix = "swagger";
});

// Hangfire's dashboard can trigger/delete background jobs — keep it development-only rather
// than exposing job control to anyone who can reach the production URL.
if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");
}

using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

    recurringJobManager.RemoveIfExists("visibility-radar-daily-scan");
    recurringJobManager.RemoveIfExists("citation-intelligence-daily-scan");
    recurringJobManager.RemoveIfExists("brand-pulse-daily-scan");
    recurringJobManager.RemoveIfExists("command-center-daily-insights");
    recurringJobManager.RemoveIfExists("competitor-watch-daily-scan");

    recurringJobManager.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.GeoScanRecurringJob>(
        "geo-scan-7-day-check",
        job => job.RunAsync(),
        Cron.Daily);

    recurringJobManager.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.CompetitorScanRecurringJob>(
        "competitor-scan-7-day-check",
        job => job.RunAsync(),
        Cron.Daily);

    recurringJobManager.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.VisibilityScanRecurringJob>(
        "visibility-scan-7-day-check",
        job => job.RunAsync(),
        Cron.Daily);

    recurringJobManager.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.CitationScanRecurringJob>(
        "citation-scan-7-day-check",
        job => job.RunAsync(),
        Cron.Daily);

    recurringJobManager.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.BrandPulseScanRecurringJob>(
        "brand-pulse-scan-7-day-check",
        job => job.RunAsync(),
        Cron.Daily);

    recurringJobManager.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.CommandCenterInsightsRecurringJob>(
        "command-center-insights-7-day-check",
        job => job.RunAsync(),
        Cron.Daily);

    recurringJobManager.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.OpportunityScanRecurringJob>(
        "opportunity-scan-7-day-check",
        job => job.RunAsync(),
        Cron.Daily);
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => "Citationly API is running");

app.Run();
