using System.Reflection;
using Citationly.Application.Interfaces;
using MediatR;

namespace Citationly.Application.Behaviors;

public sealed class AiRequestContextBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IAiRequestContextAccessor _contextAccessor;

    public AiRequestContextBehavior(IAiRequestContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var previous = _contextAccessor.OrganizationId;
        _contextAccessor.OrganizationId = TryGetOrganizationId(request);

        try
        {
            return await next();
        }
        finally
        {
            _contextAccessor.OrganizationId = previous;
        }
    }

    private static Guid? TryGetOrganizationId(TRequest request)
    {
        var property = typeof(TRequest).GetProperty("OrganizationId", BindingFlags.Instance | BindingFlags.Public);
        if (property == null)
            return null;

        var value = property.GetValue(request);
        return value is Guid guid && guid != Guid.Empty ? guid : null;
    }
}
