using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IBetaFeedbackRepository
{
    Task<Guid> CreateAsync(BetaFeedback feedback, CancellationToken cancellationToken = default);
    Task<IEnumerable<BetaFeedback>> GetByOrganizationAsync(Guid organizationId, int limit = 100, CancellationToken cancellationToken = default);
}
