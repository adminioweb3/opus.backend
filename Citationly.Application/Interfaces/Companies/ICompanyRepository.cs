using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Companies;

public interface ICompanyRepository
{
    Task<Company?> GetByIdAsync(Guid id);
    Task<Company?> GetByNormalizedDomainAsync(string normalizedDomain);

    /// <summary>Case-insensitive exact match on CompanyName. Used to resolve a plain entity name
    /// (e.g. from an observed AI-response mention) to an existing graph node - deliberately exact,
    /// not fuzzy, so an observed-competitor promotion is never based on a guessed name match.</summary>
    Task<Company?> FindByNameAsync(string name);

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
