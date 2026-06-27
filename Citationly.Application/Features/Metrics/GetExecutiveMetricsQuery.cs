using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Metrics;

public class GetExecutiveMetricsQuery : IRequest<ExecutiveMetricsResult>
{
    public Guid OrganizationId { get; set; }
}

public class ExecutiveMetricsResult
{
    public int VisibilityScore { get; set; }
    public int VisibilityChange { get; set; }
    public int CitationScore { get; set; }
    public int CitationChange { get; set; }
    public int SentimentScore { get; set; }
    public int SentimentChange { get; set; }
    public int CompetitorScore { get; set; }
    public int CompetitorChange { get; set; }
    
    public IEnumerable<TrendData> Trend { get; set; } = new List<TrendData>();
    public IEnumerable<ShareOfVoiceData> ShareOfVoice { get; set; } = new List<ShareOfVoiceData>();
}

public class TrendData
{
    public string Date { get; set; } = string.Empty;
    public int Citations { get; set; }
}

public class ShareOfVoiceData
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public string Color { get; set; } = string.Empty;
}

public class GetExecutiveMetricsQueryHandler : IRequestHandler<GetExecutiveMetricsQuery, ExecutiveMetricsResult>
{
    private readonly IMetricsRepository _repository;

    public GetExecutiveMetricsQueryHandler(IMetricsRepository repository)
    {
        _repository = repository;
    }

    public async Task<ExecutiveMetricsResult> Handle(GetExecutiveMetricsQuery request, CancellationToken cancellationToken)
    {
        var historicalScans = (await _repository.GetHistoricalScansAsync(request.OrganizationId, 30)).ToList();
        var result = new ExecutiveMetricsResult();

        if (historicalScans.Any())
        {
            var latest = historicalScans.Last();
            var previous = historicalScans.Count > 1 ? historicalScans[^2] : latest;

            result.VisibilityScore = latest.VisibilityScore;
            result.VisibilityChange = latest.VisibilityScore - previous.VisibilityScore;
            
            result.CitationScore = latest.CitationScore;
            result.CitationChange = latest.CitationScore - previous.CitationScore;
            
            result.SentimentScore = latest.SentimentScore;
            result.SentimentChange = latest.SentimentScore - previous.SentimentScore;
            
            result.CompetitorScore = latest.CompetitorScore;
            result.CompetitorChange = latest.CompetitorScore - previous.CompetitorScore;

            result.Trend = historicalScans.Select(s => new TrendData 
            { 
                Date = s.ScanDate.ToString("yyyy-MM-dd"), 
                Citations = s.VisibilityScore 
            });

            var shareOfVoice = await _repository.GetShareOfVoiceAsync(request.OrganizationId, latest.ScanDate.ToDateTime(TimeOnly.MinValue));
            result.ShareOfVoice = shareOfVoice.Select(sov => new ShareOfVoiceData
            {
                Name = sov.CompetitorName,
                Value = sov.SharePercentage,
                Color = sov.ColorCode
            });
        }
        else
        {
            var shareOfVoice = await _repository.GetShareOfVoiceAsync(request.OrganizationId, DateTime.UtcNow);
            result.ShareOfVoice = shareOfVoice.Select(sov => new ShareOfVoiceData
            {
                Name = sov.CompetitorName,
                Value = sov.SharePercentage,
                Color = sov.ColorCode
            });
        }

        return result;
    }
}
