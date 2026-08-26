using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface ISsoRepository
{
    Task<SsoConnection?> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<SsoConnection> UpsertAsync(SsoConnection connection, CancellationToken cancellationToken = default);
    Task<SsoConnection?> GetByScimTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task SetScimTokenHashAsync(Guid organizationId, string tokenHash, CancellationToken cancellationToken = default);
}
