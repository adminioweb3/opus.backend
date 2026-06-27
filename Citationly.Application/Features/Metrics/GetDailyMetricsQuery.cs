using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Metrics;

public class GetDailyMetricsQuery : IRequest<DailyMetricsResult>
{
    public Guid OrganizationId { get; set; }
}

public class DailyMetricsResult
{
    public int TotalWebsites { get; set; }
    public int TotalPagesCrawled { get; set; }
    public int TotalRecommendations { get; set; }
    public int HighPriorityRecommendations { get; set; }
}

public class GetDailyMetricsQueryHandler : IRequestHandler<GetDailyMetricsQuery, DailyMetricsResult>
{
    private readonly IMetricsRepository _repository;

    public GetDailyMetricsQueryHandler(IMetricsRepository repository)
    {
        _repository = repository;
    }

    public async Task<DailyMetricsResult> Handle(GetDailyMetricsQuery request, CancellationToken cancellationToken)
    {
        return await _repository.GetDailyMetricsAsync(request.OrganizationId);
    }
}
