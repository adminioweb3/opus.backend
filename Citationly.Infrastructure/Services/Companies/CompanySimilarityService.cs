using System.Numerics.Tensors;
using Citationly.Application.Interfaces.Companies;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Companies;

/// <summary>
/// Real internal similarity search over the Company Knowledge Graph — app-side cosine similarity
/// (no pgvector) over real OpenAI embeddings. Fine at the current/foreseeable company count
/// (hundreds, not 100k); revisit only if the graph grows into the tens of thousands.
/// </summary>
public class CompanySimilarityService : ICompanySimilarityService
{
    private readonly ICompanyRepository _companyRepository;

    public CompanySimilarityService(ICompanyRepository companyRepository)
    {
        _companyRepository = companyRepository;
    }

    public async Task<List<(Company Company, double CosineSimilarity)>> GetTopSimilarAsync(Guid targetCompanyId, int topN = 100)
    {
        var target = await _companyRepository.GetByIdAsync(targetCompanyId);
        if (target?.Embedding == null) return new List<(Company, double)>();

        var candidates = await _companyRepository.GetAllWithEmbeddingsAsync();

        return candidates
            .Where(c => c.Id != targetCompanyId && c.Embedding != null)
            .Select(c => (Company: c, CosineSimilarity: TensorPrimitives.CosineSimilarity<double>(target.Embedding!, c.Embedding!)))
            .OrderByDescending(x => x.CosineSimilarity)
            .Take(topN)
            .ToList();
    }
}
