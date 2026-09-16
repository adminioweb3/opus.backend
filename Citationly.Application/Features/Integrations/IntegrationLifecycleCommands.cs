using Citationly.Application.Interfaces;
using MediatR;

namespace Citationly.Application.Features.Integrations;

public sealed class TestIntegrationCommand : IRequest<TestIntegrationResult>
{
    public Guid OrganizationId { get; set; }
    public Guid IntegrationId { get; set; }
}

public sealed record TestIntegrationResult(bool Found, bool Connected, string Message, DateTime? VerifiedAt);

public sealed class TestIntegrationCommandHandler : IRequestHandler<TestIntegrationCommand, TestIntegrationResult>
{
    private readonly IIntegrationRepository _repository;
    private readonly IEnumerable<ICmsIntegrationService> _cmsServices;

    public TestIntegrationCommandHandler(IIntegrationRepository repository, IEnumerable<ICmsIntegrationService> cmsServices)
    {
        _repository = repository;
        _cmsServices = cmsServices;
    }

    public async Task<TestIntegrationResult> Handle(TestIntegrationCommand request, CancellationToken cancellationToken)
    {
        var integration = await _repository.GetIntegrationByIdAsync(request.IntegrationId, request.OrganizationId);
        if (integration == null) return new TestIntegrationResult(false, false, "Integration not found.", null);

        var service = _cmsServices.FirstOrDefault(candidate => candidate.PlatformName.Equals(integration.PlatformName, StringComparison.OrdinalIgnoreCase));
        if (service == null) return new TestIntegrationResult(true, false, "This integration is not supported by the server.", null);

        var verifiedAt = DateTime.UtcNow;
        var connected = await service.ValidateCredentialsAsync(integration.ApiUrl ?? string.Empty, integration.ApiKey ?? string.Empty);
        var message = connected ? "Connection verified." : "Connection failed. Reconnect with valid credentials.";
        await _repository.UpdateHealthAsync(integration.Id, request.OrganizationId, connected ? "Connected" : "Error", connected ? null : message, verifiedAt);
        return new TestIntegrationResult(true, connected, message, verifiedAt);
    }
}

public sealed class DeleteIntegrationCommand : IRequest<bool>
{
    public Guid OrganizationId { get; set; }
    public Guid IntegrationId { get; set; }
}

public sealed class DeleteIntegrationCommandHandler : IRequestHandler<DeleteIntegrationCommand, bool>
{
    private readonly IIntegrationRepository _repository;

    public DeleteIntegrationCommandHandler(IIntegrationRepository repository)
    {
        _repository = repository;
    }

    public Task<bool> Handle(DeleteIntegrationCommand request, CancellationToken cancellationToken) =>
        _repository.DeleteIntegrationAsync(request.IntegrationId, request.OrganizationId);
}
