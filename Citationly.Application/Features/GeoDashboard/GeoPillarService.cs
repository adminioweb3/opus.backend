using Citationly.Application.Dtos;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.GeoDashboard;
using System.Linq;

namespace Citationly.Application.Features.GeoDashboard;

public class GeoPillarService : IGeoPillarService
{
    private readonly IAiVisibilityRepository _repository;

    public GeoPillarService(IAiVisibilityRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<GeoPillarDto>> GetPillarsAsync(Guid organizationId, string range)
    {
        var fromDate = GetFromDate(range);
        var dbPillars = await _repository.GetGeoPillarsByOrgAsync(organizationId, fromDate);

        if (!dbPillars.Any())
            return new List<GeoPillarDto>();

        // We group by PillarKey and take the most recent for each
        var latestPillars = dbPillars
            .GroupBy(p => p.PillarKey)
            .Select(g => g.OrderByDescending(x => x.ScanDate).First())
            .Select(p => new GeoPillarDto(
                p.PillarKey,
                p.Label,
                p.Description,
                p.Score))
            .ToList();

        return latestPillars;
    }

    private DateOnly GetFromDate(string range)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return range switch
        {
            "7D" => today.AddDays(-7),
            "30D" => today.AddDays(-30),
            "90D" => today.AddDays(-90),
            "1Y" => today.AddMonths(-12),
            _ => today.AddDays(-30)
        };
    }
}
