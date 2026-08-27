using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Citationly.API.Services;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireOrgRoleAttribute : Attribute, IAsyncAuthorizationFilter
{
    private static readonly Dictionary<string, int> RoleRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Viewer"] = 0,
        ["Editor"] = 1,
        ["Manager"] = 2,
        ["Admin"] = 3,
        ["Owner"] = 4
    };

    public RequireOrgRoleAttribute(string minimumRole)
    {
        MinimumRole = minimumRole;
    }

    public string MinimumRole { get; }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var currentOrganization = context.HttpContext.RequestServices.GetRequiredService<ICurrentOrganizationAccessor>();
        var caller = await currentOrganization.GetCurrentUserAsync(user, context.HttpContext.RequestAborted);
        if (caller == null)
        {
            context.Result = new UnauthorizedObjectResult(new { message = "User not found or unlinked." });
            return;
        }

        var actualRank = RoleRank.GetValueOrDefault(caller.Value.Role, -1);
        var requiredRank = RoleRank.GetValueOrDefault(MinimumRole, int.MaxValue);
        if (actualRank < requiredRank)
        {
            context.Result = new ForbidResult();
        }
    }
}
