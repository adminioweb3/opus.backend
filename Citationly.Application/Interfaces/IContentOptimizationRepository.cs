using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IContentOptimizationRepository
{
    Task<ContentOptimization> CreateAsync(ContentOptimization optimization);
    Task<ContentOptimization?> GetLatestByDraftAsync(Guid contentDraftId);
}
