using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IAiAnalysisService
{
    Task<IEnumerable<Recommendation>> AnalyzePageAsync(CrawledPage page);
    Task<double[]> GenerateEmbeddingAsync(string text);
    Task<List<ShareOfVoice>> GenerateCompetitorsAsync(string domainUrl, Guid orgId);
}
