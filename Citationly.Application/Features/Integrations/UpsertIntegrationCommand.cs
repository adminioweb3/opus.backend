using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Integrations;

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
        if (LooksLikePlaceholder(request.ApiUrl) || LooksLikePlaceholder(request.ApiKey))
        {
            throw new InvalidOperationException("Integration credentials must be real values, not placeholders or demo configuration.");
        }

        if (!Uri.TryCreate(request.ApiUrl, UriKind.Absolute, out var apiUri)
            || (apiUri.Scheme != Uri.UriSchemeHttps && !apiUri.IsLoopback))
        {
            throw new InvalidOperationException("Use a valid HTTPS WordPress site URL.");
        }

        if (request.PlatformName.Equals("WordPress", StringComparison.OrdinalIgnoreCase)
            && (!request.ApiKey.Contains(':') || request.ApiKey.StartsWith(':') || request.ApiKey.EndsWith(':')))
        {
            throw new InvalidOperationException("WordPress credentials must include a username and application password.");
        }

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
            ApiKey = request.ApiKey,
            AuthType = "application_password",
            Status = "Connected",
            LastVerifiedAt = DateTime.UtcNow
        };

        var id = await _repository.UpsertIntegrationAsync(integration);
        return id;
    }

    private static bool LooksLikePlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.StartsWith("${", StringComparison.Ordinal) && normalized.EndsWith("}", StringComparison.Ordinal))
        {
            return true;
        }

        return normalized is "demo" or "dummy" or "test" or "placeholder" or "changeme" or "change-me" or "your-api-key" or "api-key"
            || normalized.Contains("your_", StringComparison.Ordinal)
            || normalized.Contains("your-", StringComparison.Ordinal)
            || normalized.Contains("example_", StringComparison.Ordinal)
            || normalized.Contains("example-", StringComparison.Ordinal);
    }
}
