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
    await migrationConnection.ExecuteAsync(Citationly.Infrastructure.Database.SelfHealingMigrations.Sql);
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
