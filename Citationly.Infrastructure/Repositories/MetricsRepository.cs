using Dapper;
using Citationly.Application.Features.Metrics;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class MetricsRepository : IMetricsRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public MetricsRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<DailyMetricsResult> GetDailyMetricsAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        
        var sql = @"
            SELECT 
                (SELECT COUNT(*) FROM Websites WHERE OrganizationId = @OrgId) as TotalWebsites,
                (SELECT COUNT(*) FROM CrawledPages c JOIN Websites w ON c.WebsiteId = w.Id WHERE w.OrganizationId = @OrgId) as TotalPagesCrawled,
                (SELECT COUNT(*) FROM Recommendations r JOIN Websites w ON r.WebsiteId = w.Id WHERE w.OrganizationId = @OrgId) as TotalRecommendations,
                (SELECT COUNT(*) FROM Recommendations r JOIN Websites w ON r.WebsiteId = w.Id WHERE w.OrganizationId = @OrgId AND r.Priority = 'High') as HighPriorityRecommendations
        ";

        var metrics = await connection.QuerySingleOrDefaultAsync<DailyMetricsResult>(sql, new { OrgId = organizationId });
        
        return metrics ?? new DailyMetricsResult();
    }

    public async Task<IEnumerable<HistoricalScan>> GetHistoricalScansAsync(Guid organizationId, int days)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<HistoricalScan>(
            "SELECT Id, OrganizationId, ScanDate::timestamp as ScanDate, VisibilityScore, CitationScore, SentimentScore, CompetitorScore, CreatedAt FROM HistoricalScans WHERE OrganizationId = @OrgId AND ScanDate >= CURRENT_DATE - @Days::integer ORDER BY ScanDate ASC",
            new { OrgId = organizationId, Days = days });
    }

    public async Task<IEnumerable<ShareOfVoice>> GetShareOfVoiceAsync(Guid organizationId, DateTime date)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<ShareOfVoice>(
            "SELECT Id, OrganizationId, ScanDate::timestamp as ScanDate, CompetitorName, SharePercentage, ColorCode, CreatedAt FROM ShareOfVoice WHERE OrganizationId = @OrgId AND ScanDate = @Date ORDER BY SharePercentage DESC",
            new { OrgId = organizationId, Date = date.Date });
    }

    public async Task InsertMockScanAsync(HistoricalScan scan, IEnumerable<ShareOfVoice> shareOfVoices)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        
        // Upsert HistoricalScan
        var scanSql = @"
            INSERT INTO HistoricalScans (OrganizationId, ScanDate, VisibilityScore, CitationScore, SentimentScore, CompetitorScore)
            VALUES (@OrganizationId, @ScanDate::date, @VisibilityScore, @CitationScore, @SentimentScore, @CompetitorScore)
            ON CONFLICT (OrganizationId, ScanDate) DO UPDATE 
            SET VisibilityScore = EXCLUDED.VisibilityScore,
                CitationScore = EXCLUDED.CitationScore,
                SentimentScore = EXCLUDED.SentimentScore,
                CompetitorScore = EXCLUDED.CompetitorScore;
        ";
        await connection.ExecuteAsync(scanSql, scan);

        // Upsert new SoV
        var sovSql = @"
            INSERT INTO ShareOfVoice (OrganizationId, ScanDate, CompetitorName, SharePercentage, ColorCode)
            VALUES (@OrganizationId, @ScanDate::date, @CompetitorName, @SharePercentage, @ColorCode)
            ON CONFLICT (OrganizationId, ScanDate, CompetitorName) DO UPDATE 
            SET SharePercentage = EXCLUDED.SharePercentage,
                ColorCode = EXCLUDED.ColorCode;
        ";
        await connection.ExecuteAsync(sovSql, shareOfVoices);
    }
}
