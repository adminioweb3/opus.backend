using Opus.Application.Features.Metrics;
using Opus.Domain.Entities;

namespace Opus.Application.Interfaces;

public interface IMetricsRepository
{
    Task<DailyMetricsResult> GetDailyMetricsAsync(Guid organizationId);
    Task<IEnumerable<HistoricalScan>> GetHistoricalScansAsync(Guid organizationId, int days);
    Task<IEnumerable<ShareOfVoice>> GetShareOfVoiceAsync(Guid organizationId, DateTime date);
    Task InsertMockScanAsync(HistoricalScan scan, IEnumerable<ShareOfVoice> shareOfVoices);
}
