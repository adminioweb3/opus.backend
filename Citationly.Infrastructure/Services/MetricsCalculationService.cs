using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Services;

public class MetricsCalculationService : IMetricsCalculationService
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public MetricsCalculationService(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task CalculateAndStoreMetricsAsync(Guid organizationId, DateTime scanDate, string userBrandName)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        
        var random = new Random();
        
        var visibilityScore = random.Next(40, 95);
        var citationScore = random.Next(30, 85);
        var sentimentScore = random.Next(50, 100);
        var competitorScore = random.Next(60, 90);

        var scanSql = @"
            INSERT INTO HistoricalScans (OrganizationId, ScanDate, VisibilityScore, CitationScore, SentimentScore, CompetitorScore)
            VALUES (@OrganizationId, @ScanDate::date, @VisibilityScore, @CitationScore, @SentimentScore, @CompetitorScore)
            ON CONFLICT (OrganizationId, ScanDate) DO UPDATE 
            SET VisibilityScore = EXCLUDED.VisibilityScore,
                CitationScore = EXCLUDED.CitationScore,
                SentimentScore = EXCLUDED.SentimentScore,
                CompetitorScore = EXCLUDED.CompetitorScore;
        ";

        await connection.ExecuteAsync(scanSql, new 
        { 
            OrganizationId = organizationId, 
            ScanDate = scanDate, 
            VisibilityScore = visibilityScore, 
            CitationScore = citationScore, 
            SentimentScore = sentimentScore, 
            CompetitorScore = competitorScore 
        });

        var competitors = await connection.QueryAsync<Competitor>("SELECT * FROM Competitors WHERE OrganizationId = @OrgId", new { OrgId = organizationId });

        var sovSql = @"
            INSERT INTO ShareOfVoice (OrganizationId, ScanDate, CompetitorName, SharePercentage, ColorCode)
            VALUES (@OrganizationId, @ScanDate::date, @CompetitorName, @SharePercentage, @ColorCode)
            ON CONFLICT (OrganizationId, ScanDate, CompetitorName) DO UPDATE 
            SET SharePercentage = EXCLUDED.SharePercentage,
                ColorCode = EXCLUDED.ColorCode;
        ";

        var colors = new[] { "#3b82f6", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6" };
        int colorIdx = 0;

        int totalSov = 100;
        int userSov = random.Next(10, 40);
        totalSov -= userSov;

        await connection.ExecuteAsync(sovSql, new 
        { 
            OrganizationId = organizationId, 
            ScanDate = scanDate, 
            CompetitorName = userBrandName, 
            SharePercentage = userSov, 
            ColorCode = colors[colorIdx++ % colors.Length] 
        });

        foreach (var comp in competitors)
        {
            int compSov = random.Next(5, totalSov > 5 ? totalSov / 2 : 5);
            totalSov -= compSov;

            await connection.ExecuteAsync(sovSql, new 
            { 
                OrganizationId = organizationId, 
                ScanDate = scanDate, 
                CompetitorName = comp.Name, 
                SharePercentage = compSov, 
                ColorCode = colors[colorIdx++ % colors.Length] 
            });
        }
    }
}
