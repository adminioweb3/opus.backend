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
}

public class SyncUserCommandHandler : IRequestHandler<SyncUserCommand, SyncUserResult>
{
    private readonly IUserRepository _repository;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SyncUserCommandHandler(IUserRepository repository, IDbConnectionFactory dbConnectionFactory)
    {
        _repository = repository;
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

        return new SyncUserResult
        {
            UserId = result.UserId,
            OrganizationId = result.OrganizationId,
            Role = result.Role,
            OrganizationName = orgName ?? string.Empty,
            WebsiteDomain = websiteDomain ?? string.Empty
        };
    }
}
