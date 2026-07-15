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
            "http://localhost:3000",
            "http://localhost:3010"
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
// has always required it, so the live table just predates the current schema), creates the GEO
// dashboard tables (GeoPillars/PromptCoverages/WinLossEvents) unconditionally — their only other
// creation path is RunScanCommand, which is gated behind "org has zero historical scans yet", so
// any already-onboarded org was hitting "relation does not exist" on GET /dashboard/geo-dashboard
// — creates the Content Studio / Knowledge Vault tables (KnowledgeBases, SourceFolders,
// ContentDrafts, ContentOptimizations), which init.sql defines but which never ran against this
// live database either (GET /api/Content and /api/KnowledgeBase were both 500ing with "relation
// does not exist") — creates Invites (Team Management) — and re-applies sp_CreateOrGetUser with
// its advisory-lock fix and invite-matching, since init.sql only runs against a fresh database,
// not this live one.
//
// NOTE: init.sql itself opens with `DROP TABLE ... CASCADE` for a from-scratch dev reset — never
// run that file's full contents here, only ever hand-pick idempotent CREATE/ALTER statements.
using (var migrationScope = app.Services.CreateScope())
{
    var dbConnectionFactory = migrationScope.ServiceProvider.GetRequiredService<Citationly.Application.Interfaces.IDbConnectionFactory>();
    using var migrationConnection = dbConnectionFactory.CreateConnection();
    await migrationConnection.ExecuteAsync(@"
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS PlanType VARCHAR(50) NOT NULL DEFAULT 'Trial';
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS TrialEndsAt TIMESTAMP WITH TIME ZONE;
        UPDATE Organizations SET TrialEndsAt = CreatedAt + INTERVAL '7 days' WHERE TrialEndsAt IS NULL;

        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS DomainUrl VARCHAR(255) NOT NULL DEFAULT '';

        CREATE TABLE IF NOT EXISTS KnowledgeBases (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            Name VARCHAR(255) NOT NULL,
            Icon VARCHAR(100) DEFAULT 'Building2',
            Tint VARCHAR(50) DEFAULT '#6366F1',
            Bg VARCHAR(50) DEFAULT '#EEEEFE',
            Description TEXT,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS SourceFolders (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            KnowledgeBaseId UUID REFERENCES KnowledgeBases(Id) ON DELETE CASCADE,
            Name VARCHAR(255) NOT NULL,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS Integrations (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            PlatformName VARCHAR(100) NOT NULL,
            ApiUrl VARCHAR(2048),
            ApiKey VARCHAR(1024),
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(OrganizationId, PlatformName)
        );

        CREATE TABLE IF NOT EXISTS ContentDrafts (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            Title VARCHAR(512) NOT NULL DEFAULT '',
            ContentType VARCHAR(100) NOT NULL DEFAULT '',
            Content TEXT NOT NULL DEFAULT '',
            WordCount INT NOT NULL DEFAULT 0,
            Status VARCHAR(50) NOT NULL DEFAULT 'Draft',
            RequestJson JSONB NOT NULL DEFAULT '{}'::jsonb,
            CompetitorUrl VARCHAR(2048),
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        ALTER TABLE ContentDrafts ADD COLUMN IF NOT EXISTS PublishedUrl VARCHAR(2048);
        ALTER TABLE ContentDrafts ADD COLUMN IF NOT EXISTS PublishedAt TIMESTAMP WITH TIME ZONE;
        ALTER TABLE ContentDrafts ADD COLUMN IF NOT EXISTS IntegrationId UUID REFERENCES Integrations(Id) ON DELETE SET NULL;
        ALTER TABLE ContentDrafts ADD COLUMN IF NOT EXISTS PublishError TEXT;

        CREATE TABLE IF NOT EXISTS ContentOptimizations (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            ContentDraftId UUID REFERENCES ContentDrafts(Id) ON DELETE CASCADE,
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            SeoScore INT NOT NULL DEFAULT 0,
            ReadabilityScore INT NOT NULL DEFAULT 0,
            HumanizedScore INT NOT NULL DEFAULT 0,
            AiScore INT NOT NULL DEFAULT 0,
            KeywordDensity NUMERIC(5,2) NOT NULL DEFAULT 0,
            PrimaryKeyword VARCHAR(255) NOT NULL DEFAULT '',
            RecommendationsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
            InternalLinksJson JSONB NOT NULL DEFAULT '[]'::jsonb,
            CitationRecsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
            OptimizedContent TEXT NOT NULL DEFAULT '',
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS GeoPillars (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID NOT NULL,
            ScanDate DATE NOT NULL,
            PillarKey TEXT NOT NULL,
            Label TEXT NOT NULL,
            Description TEXT NOT NULL,
            Score INT NOT NULL,
            UNIQUE (OrganizationId, ScanDate, PillarKey)
        );

        CREATE TABLE IF NOT EXISTS PromptCoverages (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID NOT NULL,
            ScanDate DATE NOT NULL,
            PromptType TEXT NOT NULL,
            Example TEXT NOT NULL,
            Note TEXT NOT NULL,
            Percentage INT NOT NULL,
            Direction TEXT NOT NULL,
            UNIQUE (OrganizationId, ScanDate, PromptType)
        );

        CREATE TABLE IF NOT EXISTS WinLossEvents (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID NOT NULL,
            Timestamp TIMESTAMPTZ NOT NULL,
            Type TEXT NOT NULL,
            Title TEXT NOT NULL,
            Engine TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Invites (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            Email VARCHAR(255) NOT NULL,
            Role VARCHAR(50) NOT NULL DEFAULT 'Viewer',
            Token VARCHAR(64) NOT NULL UNIQUE,
            InvitedByUserId UUID REFERENCES Users(Id) ON DELETE SET NULL,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            ExpiresAt TIMESTAMP WITH TIME ZONE NOT NULL,
            AcceptedAt TIMESTAMP WITH TIME ZONE
        );

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

            -- Genuinely new user: check for a pending team invite before creating a brand new
            -- organization. Invites are matched purely by email — anyone who registers/logs in
            -- with the invited address is automatically joined to that org at the invited role.
            IF v_UserId IS NULL THEN
                DECLARE
                    v_InviteId UUID;
                    v_InviteOrgId UUID;
                    v_InviteRole VARCHAR;
                BEGIN
                    SELECT i.Id, i.OrganizationId, i.Role INTO v_InviteId, v_InviteOrgId, v_InviteRole
                    FROM Invites i
                    WHERE LOWER(i.Email) = LOWER(p_Email) AND i.AcceptedAt IS NULL AND i.ExpiresAt > CURRENT_TIMESTAMP
                    ORDER BY i.CreatedAt DESC
                    LIMIT 1;

                    IF v_InviteId IS NOT NULL THEN
                        INSERT INTO Users (OrganizationId, FirebaseUid, Email, DisplayName, Role)
                        VALUES (v_InviteOrgId, p_FirebaseUid, p_Email, p_DisplayName, v_InviteRole)
                        RETURNING Id INTO v_UserId;

                        UPDATE Invites SET AcceptedAt = CURRENT_TIMESTAMP WHERE Id = v_InviteId;

                        v_OrganizationId := v_InviteOrgId;
                        v_Role := v_InviteRole;
                    END IF;
                END;
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
