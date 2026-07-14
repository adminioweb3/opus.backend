using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IKnowledgeBaseRepository
{
    Task<KnowledgeBase> CreateAsync(KnowledgeBase knowledgeBase);
    Task<IEnumerable<KnowledgeBase>> GetByOrgAsync(Guid organizationId);
    Task<KnowledgeBase?> GetByIdAsync(Guid id);
}
