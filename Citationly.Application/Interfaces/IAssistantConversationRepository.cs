using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IAssistantConversationRepository
{
    Task<IReadOnlyList<AssistantThread>> GetThreadsAsync(Guid organizationId, Guid userId, int limit = 50, CancellationToken cancellationToken = default);
    Task<AssistantThread?> GetThreadAsync(Guid threadId, Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssistantMessage>> GetMessagesAsync(Guid threadId, Guid organizationId, Guid userId, int limit = 100, CancellationToken cancellationToken = default);
    Task<AssistantThread> CreateThreadAsync(Guid organizationId, Guid userId, string title, CancellationToken cancellationToken = default);
    Task<bool> RenameThreadAsync(Guid threadId, Guid organizationId, Guid userId, string title, CancellationToken cancellationToken = default);
    Task<bool> DeleteThreadAsync(Guid threadId, Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    Task<AssistantMessage?> AddMessageAsync(Guid threadId, Guid organizationId, Guid userId, string role, string content, CancellationToken cancellationToken = default);
}
