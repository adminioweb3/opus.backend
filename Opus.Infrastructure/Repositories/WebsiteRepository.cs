using Dapper;
using Opus.Application.Interfaces;
using Opus.Domain.Entities;

namespace Opus.Infrastructure.Repositories;

public class WebsiteRepository : IWebsiteRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public WebsiteRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Guid> GetOrInsertWebsiteAsync(Guid organizationId, string domainUrl)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var websiteId = await connection.ExecuteScalarAsync<Guid?>(
            "SELECT Id FROM Websites WHERE DomainUrl = @DomainUrl AND OrganizationId = @OrganizationId",
            new { DomainUrl = domainUrl, OrganizationId = organizationId });

        if (websiteId == null || websiteId == Guid.Empty)
        {
            websiteId = await connection.ExecuteScalarAsync<Guid>(
                "INSERT INTO Websites (OrganizationId, DomainUrl) VALUES (@OrganizationId, @DomainUrl) RETURNING Id",
                new { OrganizationId = organizationId, DomainUrl = domainUrl });
        }
        
        return websiteId.Value;
    }

    public async Task<IEnumerable<Website>> GetAllWebsitesAsync()
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<Website>("SELECT * FROM Websites");
    }

    public async Task<IEnumerable<Website>> GetWebsitesByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<Website>(
            "SELECT * FROM Websites WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC",
            new { OrganizationId = organizationId });
    }

    public async Task<Website> ConnectWebsiteAsync(Guid organizationId, string domainUrl, string platformName)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var id = await connection.ExecuteScalarAsync<Guid>(
            @"INSERT INTO Websites (OrganizationId, DomainUrl, PlatformName, HealthScore, VisibilityScore, Status) 
              VALUES (@OrganizationId, @DomainUrl, @PlatformName, 100, 0, 'Connected') 
              RETURNING Id",
            new { OrganizationId = organizationId, DomainUrl = domainUrl, PlatformName = platformName });

        return new Website
        {
            Id = id,
            OrganizationId = organizationId,
            DomainUrl = domainUrl,
            PlatformName = platformName,
            HealthScore = 100,
            VisibilityScore = 0,
            Status = "Connected",
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<Guid> InsertCrawledPageAsync(CrawledPage page)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(
            "SELECT sp_InsertCrawledPage(@WebsiteId, @Url, @Title, @Content)",
            new { page.WebsiteId, page.Url, page.Title, page.Content });
    }

    public async Task<Guid> InsertRecommendationAsync(Recommendation rec)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(
            "SELECT sp_InsertRecommendation(@WebsiteId, @CrawledPageId, @Title, @Description, @ActionType, @Priority)",
            new { rec.WebsiteId, rec.CrawledPageId, rec.Title, rec.Description, rec.ActionType, rec.Priority });
    }
}
