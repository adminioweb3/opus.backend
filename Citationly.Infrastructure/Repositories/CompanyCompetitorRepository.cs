using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class CompanyCompetitorRepository : ICompanyCompetitorRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CompanyCompetitorRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task ReplaceCompetitorsForCompanyAsync(Guid companyId, IEnumerable<CompanyCompetitor> edges)
    {
        var edgeList = edges.ToList();
        using var connection = _dbConnectionFactory.CreateConnection();
        if (connection.State != System.Data.ConnectionState.Open) connection.Open();

        await connection.ExecuteAsync(
            "ALTER TABLE CompanyCompetitor ADD COLUMN IF NOT EXISTS DiscoverySource VARCHAR(20) NOT NULL DEFAULT 'graph'");

        using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(
                "DELETE FROM CompanyCompetitor WHERE CompanyId = @CompanyId",
                new { CompanyId = companyId }, transaction: transaction);

            foreach (var edge in edgeList)
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO CompanyCompetitor (CompanyId, CompetitorCompanyId, Similarity, Confidence, Rank, Reason, Strength, Weakness, DiscoverySource, CreatedAt, UpdatedAt)
                    VALUES (@CompanyId, @CompetitorCompanyId, @Similarity, @Confidence, @Rank, @Reason, @Strength, @Weakness, @DiscoverySource, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)",
                    new
                    {
                        CompanyId = companyId,
                        edge.CompetitorCompanyId,
                        edge.Similarity,
                        edge.Confidence,
                        edge.Rank,
                        edge.Reason,
                        edge.Strength,
                        edge.Weakness,
                        edge.DiscoverySource
                    }, transaction: transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IEnumerable<CompanyCompetitor>> GetByCompanyIdAsync(Guid companyId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<CompanyCompetitor>(
            "SELECT * FROM CompanyCompetitor WHERE CompanyId = @CompanyId ORDER BY Rank",
            new { CompanyId = companyId });
    }
}
