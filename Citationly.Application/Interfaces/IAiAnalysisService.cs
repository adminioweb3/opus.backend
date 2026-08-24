using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IAiAnalysisService
{
    Task<IEnumerable<Recommendation>> AnalyzePageAsync(CrawledPage page);

    /// <summary>Null means embedding generation failed — never fabricate a vector for a real failure.</summary>
    Task<double[]?> GenerateEmbeddingAsync(string text);
}
