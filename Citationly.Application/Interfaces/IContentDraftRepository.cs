using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IContentDraftRepository
{
    Task<ContentDraft> CreateAsync(ContentDraft draft);
    Task<ContentDraft?> GetByIdAsync(Guid id);
    Task<List<ContentDraft>> GetByOrgAsync(Guid organizationId);
    Task UpdateAsync(ContentDraft draft);
}
