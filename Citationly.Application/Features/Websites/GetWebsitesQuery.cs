using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Websites;

public class GetWebsitesQuery : IRequest<IEnumerable<Website>>
{
    public Guid OrganizationId { get; set; }
}

public class GetWebsitesQueryHandler : IRequestHandler<GetWebsitesQuery, IEnumerable<Website>>
{
    private readonly IWebsiteRepository _repository;

    public GetWebsitesQueryHandler(IWebsiteRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Website>> Handle(GetWebsitesQuery request, CancellationToken cancellationToken)
    {
        return await _repository.GetWebsitesByOrgAsync(request.OrganizationId);
    }
}
