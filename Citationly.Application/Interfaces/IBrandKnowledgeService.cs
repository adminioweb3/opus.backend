using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IBrandKnowledgeService
{
    Task<BrandKnowledgeResult> RefreshAsync(Guid organizationId, int lookbackDays = 30, CancellationToken ct = default);
    Task<BrandKnowledgeResult> GetAsync(Guid organizationId, int lookbackDays = 30, CancellationToken ct = default);
}

public sealed record BrandKnowledgeResult(
    bool HasData,
    IReadOnlyList<BrandClaim> Claims,
    IReadOnlyList<BrandFactCheck> FactChecks,
    int IncorrectCount,
    int UnverifiedCount);
