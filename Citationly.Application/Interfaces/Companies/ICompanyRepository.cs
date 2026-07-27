using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Companies;

public interface ICompanyRepository
{
    Task<Company?> GetByIdAsync(Guid id);
    Task<Company?> GetByNormalizedDomainAsync(string normalizedDomain);

    /// <summary>
    /// Insert-or-update by NormalizedDomain (the unique identity for a Company). Does not touch
    /// Embedding/EmbeddingModel/EmbeddingUpdatedAt — those are refreshed separately via
    /// UpdateEmbeddingAsync once the (possibly newly upserted) row's Id is known.
    /// </summary>
    Task<Company> UpsertAsync(Company company);

    Task<IEnumerable<Company>> GetAllWithEmbeddingsAsync();
    Task<IEnumerable<Company>> GetByIdsAsync(IEnumerable<Guid> ids);
    Task UpdateEmbeddingAsync(Guid companyId, double[] embedding, string model);
}
