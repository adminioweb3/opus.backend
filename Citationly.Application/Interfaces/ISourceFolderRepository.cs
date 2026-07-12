using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface ISourceFolderRepository
{
    Task<SourceFolder> CreateAsync(SourceFolder folder);
    Task<IEnumerable<SourceFolder>> GetByKnowledgeBaseAsync(Guid knowledgeBaseId);
}
