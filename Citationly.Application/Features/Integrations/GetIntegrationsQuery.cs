using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Integrations;

public class GetIntegrationsQuery : IRequest<IEnumerable<Integration>>
{
    public Guid OrganizationId { get; set; }
}

public class GetIntegrationsQueryHandler : IRequestHandler<GetIntegrationsQuery, IEnumerable<Integration>>
{
    private readonly IIntegrationRepository _repository;

    public GetIntegrationsQueryHandler(IIntegrationRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Integration>> Handle(GetIntegrationsQuery request, CancellationToken cancellationToken)
    {
        return await _repository.GetIntegrationsByOrgAsync(request.OrganizationId);
    }
}
