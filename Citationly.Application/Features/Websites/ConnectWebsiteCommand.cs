using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Websites;

public class ConnectWebsiteCommand : IRequest<Website>
{
    public Guid OrganizationId { get; set; }
    public string DomainUrl { get; set; } = string.Empty;
    public string PlatformName { get; set; } = "Custom";
}

public class ConnectWebsiteCommandHandler : IRequestHandler<ConnectWebsiteCommand, Website>
{
    private readonly IWebsiteRepository _repository;

    public ConnectWebsiteCommandHandler(IWebsiteRepository repository)
    {
        _repository = repository;
    }

    public async Task<Website> Handle(ConnectWebsiteCommand request, CancellationToken cancellationToken)
    {
        return await _repository.ConnectWebsiteAsync(request.OrganizationId, request.DomainUrl, request.PlatformName);
    }
}
