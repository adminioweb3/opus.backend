using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IIntegrationRepository
{
    Task<Guid> UpsertIntegrationAsync(Integration integration);
    Task<IEnumerable<Integration>> GetIntegrationsByOrgAsync(Guid organizationId);

    /// <summary>
    /// Server-side only: includes the real ApiKey, unlike <see cref="GetIntegrationsByOrgAsync"/>
    /// which intentionally omits it. Never expose this result directly to a client response.
    /// </summary>
    Task<Integration?> GetIntegrationByOrgAndPlatformAsync(Guid organizationId, string platformName);

    /// <summary>Server-side only: includes the real ApiKey. Scoped to organizationId for tenant safety.</summary>
    Task<Integration?> GetIntegrationByIdAsync(Guid id, Guid organizationId);
}

public interface IApiKeyRepository
{
    Task<IEnumerable<ApiKey>> GetApiKeysByOrgAsync(Guid organizationId);
    Task<ApiKey?> GetActiveApiKeyByHashAsync(string keyHash);
    Task<Guid> CreateApiKeyAsync(ApiKey apiKey);
    Task<bool> RevokeApiKeyAsync(Guid id, Guid organizationId, DateTime revokedAtUtc);
}
