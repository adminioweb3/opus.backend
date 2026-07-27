using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Companies;

public interface ICompanySimilarityService
{
    /// <summary>
    /// Real cosine similarity over stored embeddings — top N most similar Company rows to
    /// targetCompanyId, excluding itself. Companies with no embedding yet are not candidates.
    /// </summary>
    Task<List<(Company Company, double CosineSimilarity)>> GetTopSimilarAsync(Guid targetCompanyId, int topN = 100);
}
