using Citationly.Application.Dtos;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.GeoDashboard;
using System.Linq;

namespace Citationly.Application.Features.GeoDashboard;

public class PromptCoverageService : IPromptCoverageService
{
    private readonly IAiVisibilityRepository _repository;

    public PromptCoverageService(IAiVisibilityRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<PromptTypeCoverageDto>> GetCoverageAsync(Guid organizationId, string range)
    {
        var fromDate = GetFromDate(range);
        var dbCoverage = await _repository.GetPromptCoverageByOrgAsync(organizationId, fromDate);

        if (!dbCoverage.Any())
            return new List<PromptTypeCoverageDto>();

        // We group by PromptType and take the most recent
        var latestCoverage = dbCoverage
            .GroupBy(c => c.PromptType)
            .Select(g => g.OrderByDescending(x => x.ScanDate).First())
            .Select(c => new PromptTypeCoverageDto(
                c.PromptType,
                c.Example,
                c.Note,
                c.Percentage,
                c.Direction))
            .ToList();

        return latestCoverage;
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
