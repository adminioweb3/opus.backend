using Citationly.Application.Dtos;

namespace Citationly.Application.Interfaces.GeoDashboard;

public interface IActivityFeedService
{
    Task<List<WinLossEventDto>> GetRecentEventsAsync(Guid organizationId, int count = 5);
}
