using Citationly.Application.Dtos;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.GeoDashboard;
using System.Linq;

namespace Citationly.Application.Features.GeoDashboard;

public class ActivityFeedService : IActivityFeedService
{
    private readonly IAiVisibilityRepository _repository;

    public ActivityFeedService(IAiVisibilityRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<WinLossEventDto>> GetRecentEventsAsync(Guid organizationId, int count = 5)
    {
        var dbEvents = await _repository.GetWinLossEventsByOrgAsync(organizationId, count);

        if (!dbEvents.Any())
            return new List<WinLossEventDto>();

        return dbEvents.Select(e => new WinLossEventDto(
            e.Type,
            e.Title,
            e.Engine,
            e.Timestamp.ToString("o")
        )).ToList();
    }
}
