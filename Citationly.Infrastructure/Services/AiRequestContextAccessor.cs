using System.Threading;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Services;

public sealed class AiRequestContextAccessor : IAiRequestContextAccessor
{
    private static readonly AsyncLocal<Guid?> CurrentOrganization = new();

    public Guid? OrganizationId
    {
        get => CurrentOrganization.Value;
        set => CurrentOrganization.Value = value;
    }
}
