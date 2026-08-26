using System.Security.Claims;
using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Citationly.API.Services;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AuditActionAttribute : Attribute, IAsyncActionFilter
{
    public AuditActionAttribute(string action, string category = "General", string targetType = "")
    {
        Action = action;
        Category = category;
        TargetType = targetType;
    }

    public string Action { get; }
    public string Category { get; }
    public string TargetType { get; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        var audit = context.HttpContext.RequestServices.GetRequiredService<IAuditLogService>();
        var currentOrg = context.HttpContext.RequestServices.GetService<ICurrentOrganizationAccessor>();
        var userRepo = context.HttpContext.RequestServices.GetService<IUserRepository>();
        var user = context.HttpContext.User;

        Guid? organizationId = null;
        Guid? actorUserId = null;
        var actorEmail = user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value ?? string.Empty;
        var actorType = user.IsInRole("Admin") ? "PlatformAdmin" : "User";

        if (currentOrg != null && user.Identity?.IsAuthenticated == true && !user.IsInRole("Admin"))
        {
            try
            {
                organizationId = await currentOrg.GetOrganizationIdAsync(user, context.HttpContext.RequestAborted);
            }
            catch
            {
                // Audit logging must never break the primary request.
            }
        }

        var firebaseUid = user.FindFirst("user_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(firebaseUid) && userRepo != null && !user.IsInRole("Admin"))
        {
            try
            {
                var caller = await userRepo.GetUserByFirebaseUidAsync(firebaseUid);
                actorUserId = caller?.UserId;
                organizationId ??= caller?.OrganizationId;
            }
            catch
            {
                // Best-effort enrichment only.
            }
        }

        var statusCode = executed.HttpContext.Response.StatusCode;
        var outcome = statusCode >= 200 && statusCode < 400 ? "Success" : $"Http{statusCode}";
        var routeTarget = context.RouteData.Values.LastOrDefault(v => v.Key.EndsWith("id", StringComparison.OrdinalIgnoreCase)).Value?.ToString() ?? string.Empty;

        await audit.RecordAsync(
            Action,
            Category,
            outcome,
            organizationId,
            actorUserId,
            actorEmail,
            actorType,
            TargetType,
            routeTarget,
            "{}",
            context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.HttpContext.Request.Headers.UserAgent.ToString(),
            context.HttpContext.RequestAborted);
    }
}
