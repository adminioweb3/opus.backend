using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface ICrossEngineConsensusService
{
    Task<CrossEngineConsensusResult> RefreshAsync(Guid organizationId, int lookbackDays = 30, CancellationToken ct = default);
    Task<CrossEngineConsensusResult> GetAsync(Guid organizationId, int lookbackDays = 30, CancellationToken ct = default);
}

public sealed record CrossEngineConsensusResult(
    bool HasIndependentProviders,
    string Status,
    IReadOnlyList<CrossEngineConsensusInsight> Insights);
