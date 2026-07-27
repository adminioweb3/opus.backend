using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class CompanyRepository : ICompanyRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CompanyRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Company?> GetByIdAsync(Guid id)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "SELECT * FROM Company WHERE Id = @Id",
            new { Id = id });
    }

    public async Task<Company?> GetByNormalizedDomainAsync(string normalizedDomain)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Company>(
            "SELECT * FROM Company WHERE NormalizedDomain = @NormalizedDomain",
            new { NormalizedDomain = normalizedDomain });
    }

    public async Task<Company> UpsertAsync(Company company)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<Company>(@"
            INSERT INTO Company (NormalizedDomain, Website, CompanyName, Industry, BusinessProfileJson, SourceOrganizationId, LastAnalyzedAt, CreatedAt, UpdatedAt)
            VALUES (@NormalizedDomain, @Website, @CompanyName, @Industry, @BusinessProfileJson::jsonb, @SourceOrganizationId, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT (NormalizedDomain) DO UPDATE SET
                Website = EXCLUDED.Website,
                CompanyName = EXCLUDED.CompanyName,
                Industry = EXCLUDED.Industry,
                BusinessProfileJson = EXCLUDED.BusinessProfileJson,
                LastAnalyzedAt = CURRENT_TIMESTAMP,
                UpdatedAt = CURRENT_TIMESTAMP
            RETURNING *;",
            new
            {
                company.NormalizedDomain,
                company.Website,
                company.CompanyName,
                company.Industry,
                company.BusinessProfileJson,
                company.SourceOrganizationId
            });
    }

    public async Task<IEnumerable<Company>> GetAllWithEmbeddingsAsync()
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<Company>(
            "SELECT * FROM Company WHERE Embedding IS NOT NULL");
    }

    public async Task<IEnumerable<Company>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return Enumerable.Empty<Company>();

        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<Company>(
            "SELECT * FROM Company WHERE Id = ANY(@Ids)",
            new { Ids = idList });
    }

    public async Task UpdateEmbeddingAsync(Guid companyId, double[] embedding, string model)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            UPDATE Company
            SET Embedding = @Embedding, EmbeddingModel = @Model, EmbeddingUpdatedAt = CURRENT_TIMESTAMP, UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @Id",
            new { Id = companyId, Embedding = embedding, Model = model });
    }
}
