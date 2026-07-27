using MediatR;
using Citationly.Application.Interfaces;
using Dapper;

namespace Citationly.Application.Features.Auth;

public class SyncUserCommand : IRequest<SyncUserResult>
{
    public string FirebaseUid { get; set; } = string.Empty;
    public string Provider { get; set; } = "email"; // "email", "google", "github"
    public string ProviderUid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class SyncUserResult
{
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string WebsiteDomain { get; set; } = string.Empty;

    /// <summary>True when this organization has no analyzed website yet — the frontend should
    /// route the user to onboarding instead of the dashboard.</summary>
    public bool NeedsOnboarding { get; set; }
    public string PlanType { get; set; } = "Trial";
    public DateTime? TrialEndsAt { get; set; }
    public bool IsTrialExpired { get; set; }
    public string? Industry { get; set; }
    public bool IsNewUser { get; set; }
}

public class SyncUserCommandHandler : IRequestHandler<SyncUserCommand, SyncUserResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SyncUserCommandHandler(IWebsiteRepository websiteRepository, IDbConnectionFactory dbConnectionFactory)
    {
        _websiteRepository = websiteRepository;
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<SyncUserResult> Handle(SyncUserCommand request, CancellationToken cancellationToken)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        // Use the new v2 stored procedure that handles multi-auth
        var result = await connection.QuerySingleAsync<(Guid UserId, Guid OrganizationId, string Role, bool IsNewUser)>(
            "SELECT userid, organizationid, role, isnewuser FROM sp_CreateOrGetUserV2(@FirebaseUid, @Provider, @ProviderUid, @Email, @DisplayName)",
            new { FirebaseUid = request.FirebaseUid, Provider = request.Provider, ProviderUid = request.ProviderUid, Email = request.Email, DisplayName = request.DisplayName });

        // Fetch the Organization's Name, trial state, and Industry
        var org = await connection.QueryFirstOrDefaultAsync<(string Name, string PlanType, DateTime? TrialEndsAt, string? Industry)>(
            "SELECT Name, PlanType, TrialEndsAt, Industry FROM Organizations WHERE Id = @Id",
            new { Id = result.OrganizationId });

        // WebsiteProfiles is the reliable signal for "has this org completed onboarding"
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(result.OrganizationId);

        var isTrialExpired = org.TrialEndsAt.HasValue && org.TrialEndsAt.Value < DateTime.UtcNow;

        return new SyncUserResult
        {
            UserId = result.UserId,
            OrganizationId = result.OrganizationId,
            Role = result.Role,
            OrganizationName = org.Name ?? string.Empty,
            WebsiteDomain = profile?.WebsiteUrl ?? string.Empty,
            NeedsOnboarding = profile == null,
            PlanType = org.PlanType ?? "Trial",
            TrialEndsAt = org.TrialEndsAt,
            IsTrialExpired = isTrialExpired,
            Industry = org.Industry,
            IsNewUser = result.IsNewUser
        };
    }
}
