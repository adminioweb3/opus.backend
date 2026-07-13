using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Hangfire;
using Hangfire.PostgreSql;

using Citationly.Application;
using Citationly.Infrastructure;
using Citationly.Infrastructure.Data;
using Citationly.Infrastructure.Database;

// Dapper 2.1.79 has no built-in support for writing a DateOnly value as a query parameter
// (it reads DateOnly columns fine, but throws NotSupportedException on INSERT/UPDATE) — every
// snapshot/scan-summary repository's "ScanDate" column hits this. Register once, globally.
Dapper.SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
Dapper.SqlMapper.AddTypeHandler(new NullableDateOnlyTypeHandler());

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();

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
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
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

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // Dev-only convenience bypass — must never be reachable in production,
                    // or anyone could authenticate as demo-user-id with a fixed, public token.
                    // Read the raw header directly: at this point in the pipeline the framework
                    // hasn't populated context.Token yet (that extraction happens AFTER
                    // OnMessageReceived returns, only if still empty), so comparing against
                    // context.Token here would never match.
                    var rawAuthHeader = context.Request.Headers["Authorization"].ToString();
                    var bearerToken = rawAuthHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        ? rawAuthHeader["Bearer ".Length..].Trim()
                        : null;

                    if (builder.Environment.IsDevelopment() && bearerToken == "demo-token")
                    {
                        var claims = new[]
                        {
                            new System.Security.Claims.Claim(
                                System.Security.Claims.ClaimTypes.NameIdentifier,
                                "demo-user-id"
                            ),
                            new System.Security.Claims.Claim(
                                System.Security.Claims.ClaimTypes.Email,
                                "demo@example.com"
                            )
                        };

                        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Demo");
                        context.Principal = new System.Security.Claims.ClaimsPrincipal(identity);
                        context.Success();
                    }

                    return Task.CompletedTask;
                }
            };
        });
}

var app = builder.Build();

// Bootstrap the database schema on every startup — safe to run repeatedly (every statement
// is CREATE ... IF NOT EXISTS / CREATE OR REPLACE), and is what lets a brand-new production
// database create its own schema on first boot instead of 500ing on every request because
// the tables were never created. Logs and continues on failure rather than crashing startup.
if (!string.IsNullOrEmpty(connectionString))
{
    var migrationLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SchemaMigrator");
    await SchemaMigrator.RunAsync(connectionString, migrationLogger);
}
else
{
    app.Logger.LogWarning("No DefaultConnection connection string configured — skipping schema bootstrap and expecting every database call to fail.");
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
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        if (exceptionFeature is not null)
        {
            app.Logger.LogError(exceptionFeature.Error, "Unhandled exception on {Path}", exceptionFeature.Path);
        }

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
// than exposing job control to anyone who can reach the production URL. Ask if you want this
// reachable in production too; it should be put behind its own auth filter first if so.
if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");
}

// Middleware pipeline
app.UseHttpsRedirection();
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => "Citationly API is running");

// Daily recurring scans — keep every org's dashboard data fresh (7-day cadence) even for
// orgs that don't visit the page; each GET endpoint also bootstraps/refreshes on-demand, so
// this is a belt-and-braces background pass, visible/triggerable manually via /hangfire.
RecurringJob.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.DashboardScanRecurringJobs>(
    "visibility-radar-daily-scan", job => job.RunVisibilityScansAsync(), Cron.Daily);
RecurringJob.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.DashboardScanRecurringJobs>(
    "citation-intelligence-daily-scan", job => job.RunCitationScansAsync(), Cron.Daily);
RecurringJob.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.DashboardScanRecurringJobs>(
    "brand-pulse-daily-scan", job => job.RunBrandPulseScansAsync(), Cron.Daily);
RecurringJob.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.DashboardScanRecurringJobs>(
    "command-center-daily-insights", job => job.RunCommandCenterInsightsAsync(), Cron.Daily);
RecurringJob.AddOrUpdate<Citationly.Infrastructure.BackgroundJobs.DashboardScanRecurringJobs>(
    "competitor-watch-daily-scan", job => job.RunCompetitorScansAsync(), Cron.Daily);

app.Run();
