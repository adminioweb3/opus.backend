using System.Text.Json;
using MediatR;
using Opus.Application.Interfaces;
using Dapper;

namespace Opus.Application.Features.Onboarding;

public class CompleteOnboardingCommand : IRequest<bool>
{
    public Guid OrganizationId { get; set; }
    public string WebsiteUrl { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string Competitors { get; set; } = string.Empty;
    public int VisibilityScore { get; set; }
    public int BrandAuthority { get; set; }
    public int ContentStrength { get; set; }
    public int CitationScore { get; set; }
}

public class CompleteOnboardingCommandHandler : IRequestHandler<CompleteOnboardingCommand, bool>
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CompleteOnboardingCommandHandler(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<bool> Handle(CompleteOnboardingCommand request, CancellationToken cancellationToken)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        
        // 1. Update Organization Name
        if (!string.IsNullOrWhiteSpace(request.BusinessName))
        {
            await connection.ExecuteAsync(
                "UPDATE Organizations SET Name = @BusinessName WHERE Id = @OrganizationId",
                new { request.BusinessName, request.OrganizationId });
        }

        // 2. Insert or get Website
        Guid websiteId = Guid.Empty;
        if (!string.IsNullOrWhiteSpace(request.WebsiteUrl))
        {
            websiteId = await connection.QueryFirstOrDefaultAsync<Guid>(
                "SELECT Id FROM Websites WHERE OrganizationId = @OrganizationId AND DomainUrl = @WebsiteUrl",
                new { request.OrganizationId, request.WebsiteUrl });

            if (websiteId == Guid.Empty)
            {
                websiteId = await connection.QuerySingleAsync<Guid>(
                    "INSERT INTO Websites (OrganizationId, DomainUrl) VALUES (@OrganizationId, @WebsiteUrl) RETURNING Id",
                    new { request.OrganizationId, request.WebsiteUrl });
            }
        }

        // 3. Insert initial HistoricalScan
        await connection.ExecuteAsync(@"
            INSERT INTO HistoricalScans 
            (OrganizationId, ScanDate, VisibilityScore, CitationScore, SentimentScore, CompetitorScore) 
            VALUES 
            (@OrganizationId, CURRENT_DATE, @VisibilityScore, @CitationScore, @ContentStrength, @BrandAuthority)",
            new
            {
                request.OrganizationId,
                request.VisibilityScore,
                request.CitationScore,
                request.ContentStrength,
                request.BrandAuthority
            });

        // 4. Insert ShareOfVoice for each competitor
        if (!string.IsNullOrWhiteSpace(request.Competitors))
        {
            var competitors = request.Competitors.Split(',')
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            int share = competitors.Count > 0 ? 100 / (competitors.Count + 1) : 0;
            var random = new Random();

            foreach (var competitor in competitors)
            {
                var color = $"#{random.Next(0x1000000):X6}";
                await connection.ExecuteAsync(@"
                    INSERT INTO ShareOfVoice 
                    (OrganizationId, ScanDate, CompetitorName, SharePercentage, ColorCode) 
                    VALUES 
                    (@OrganizationId, CURRENT_DATE, @CompetitorName, @SharePercentage, @ColorCode)
                    ON CONFLICT ON CONSTRAINT shareofvoice_organizationid_scandate_competitorname_key DO NOTHING",
                    new
                    {
                        request.OrganizationId,
                        CompetitorName = competitor,
                        SharePercentage = share,
                        ColorCode = color
                    });
            }
            
            // Add self share
            if (share > 0)
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO ShareOfVoice 
                    (OrganizationId, ScanDate, CompetitorName, SharePercentage, ColorCode) 
                    VALUES 
                    (@OrganizationId, CURRENT_DATE, @CompetitorName, @SharePercentage, '#3b82f6')
                    ON CONFLICT ON CONSTRAINT shareofvoice_organizationid_scandate_competitorname_key DO NOTHING",
                    new
                    {
                        request.OrganizationId,
                        CompetitorName = string.IsNullOrWhiteSpace(request.BusinessName) ? "You" : request.BusinessName,
                        SharePercentage = 100 - (share * competitors.Count)
                    });
            }
        }

        return true;
    }
}
