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

        // Fetch the Organization Name
        var orgName = await Dapper.SqlMapper.QueryFirstOrDefaultAsync<string>(
            connection,
            "SELECT Name FROM Organizations WHERE Id = @Id",
            new { Id = result.OrganizationId });

        // Fetch the first Website Domain
        var websiteDomain = await Dapper.SqlMapper.QueryFirstOrDefaultAsync<string>(
            connection,
            "SELECT DomainUrl FROM Websites WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt ASC LIMIT 1",
            new { OrganizationId = result.OrganizationId });

        // The real "has completed onboarding" signal is a WebsiteProfile (created early, during
        // /onboarding/analyze) — NOT Websites.DomainUrl, which is only populated conditionally,
        // late, at the checkout step, and would falsely mark real returning users as needing
        // onboarding again.
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(result.OrganizationId);
        var needsOnboarding = profile == null;

        var trialEndsAt = result.TrialEndsAt;
        var isTrialExpired = trialEndsAt.HasValue && trialEndsAt.Value < DateTime.UtcNow;

        return new SyncUserResult
        {
            UserId = result.UserId,
            OrganizationId = result.OrganizationId,
            Role = result.Role,
            OrganizationName = orgName ?? string.Empty,
            WebsiteDomain = websiteDomain ?? string.Empty,
            NeedsOnboarding = needsOnboarding,
            PlanType = result.PlanType ?? "Trial",
            TrialEndsAt = trialEndsAt,
            IsTrialExpired = isTrialExpired,
        };
    }
}
