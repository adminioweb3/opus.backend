using Opus.Domain.Entities;

namespace Opus.Application.Interfaces;

public interface IAiAnalysisService
{
    Task<IEnumerable<Recommendation>> AnalyzePageAsync(CrawledPage page);
    Task<double[]> GenerateEmbeddingAsync(string text);
}
