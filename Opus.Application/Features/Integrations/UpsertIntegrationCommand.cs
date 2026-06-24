using MediatR;
using Opus.Application.Interfaces;
using Opus.Domain.Entities;

namespace Opus.Application.Features.Integrations;

public class UpsertIntegrationCommand : IRequest<Guid>
{
    public Guid OrganizationId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public class UpsertIntegrationCommandHandler : IRequestHandler<UpsertIntegrationCommand, Guid>
{
    private readonly IIntegrationRepository _repository;
    private readonly IEnumerable<ICmsIntegrationService> _cmsServices;

    public UpsertIntegrationCommandHandler(IIntegrationRepository repository, IEnumerable<ICmsIntegrationService> cmsServices)
    {
        _repository = repository;
        _cmsServices = cmsServices;
    }

    public async Task<Guid> Handle(UpsertIntegrationCommand request, CancellationToken cancellationToken)
    {
        // 1. Validate credentials with the appropriate CMS service
        var cmsService = _cmsServices.FirstOrDefault(s => s.PlatformName.Equals(request.PlatformName, StringComparison.OrdinalIgnoreCase));
        
        if (cmsService != null)
        {
            var isValid = await cmsService.ValidateCredentialsAsync(request.ApiUrl, request.ApiKey);
            if (!isValid)
            {
                throw new InvalidOperationException("Failed to validate CMS credentials. Please check your API URL and Key.");
            }
        }
        else
        {
            throw new NotSupportedException($"Platform {request.PlatformName} is not supported.");
        }

        // 2. Save integration
        var integration = new Integration
        {
            OrganizationId = request.OrganizationId,
            PlatformName = request.PlatformName,
            ApiUrl = request.ApiUrl,
            ApiKey = request.ApiKey
        };

        var id = await _repository.UpsertIntegrationAsync(integration);
        return id;
    }
}
