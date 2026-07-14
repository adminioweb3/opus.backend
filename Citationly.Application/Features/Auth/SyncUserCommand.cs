using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Auth;

public class SyncUserCommand : IRequest<SyncUserResult>
{
    public string FirebaseUid { get; set; } = string.Empty;
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
}

public class SyncUserCommandHandler : IRequestHandler<SyncUserCommand, SyncUserResult>
{
    private readonly IUserRepository _repository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SyncUserCommandHandler(IUserRepository repository, IWebsiteRepository websiteRepository, IDbConnectionFactory dbConnectionFactory)
    {
        _repository = repository;
        _websiteRepository = websiteRepository;
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<SyncUserResult> Handle(SyncUserCommand request, CancellationToken cancellationToken)
    {
        var result = await _repository.CreateOrGetUserAsync(request.FirebaseUid, request.Email, request.DisplayName);

        using var connection = _dbConnectionFactory.CreateConnection();

        // Fetch the Organization's Name + trial state
        var org = await Dapper.SqlMapper.QueryFirstOrDefaultAsync<(string Name, string PlanType, DateTime? TrialEndsAt)>(
            connection,
            "SELECT Name, PlanType, TrialEndsAt FROM Organizations WHERE Id = @Id",
            new { Id = result.OrganizationId });

        // WebsiteProfiles (populated at the real "/onboarding/analyze" step, not the later
        // checkout step) is the reliable signal used everywhere else in the app for "has this
        // org completed onboarding" — Websites.DomainUrl is only conditionally set at checkout
        // and isn't a safe gate.
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
            IsTrialExpired = isTrialExpired
        };
    }
}
