using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services;

public class MockSearchService : ISearchService
{
    public Task<IEnumerable<Competitor>> DiscoverCompetitorsAsync(Guid organizationId, string industry, string services)
    {
        var competitors = new List<Competitor>
        {
            new Competitor { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "ConsenSys", WebsiteUrl = "https://consensys.net", Industry = industry, CreatedAt = DateTime.UtcNow },
            new Competitor { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "LeewayHertz", WebsiteUrl = "https://www.leewayhertz.com", Industry = industry, CreatedAt = DateTime.UtcNow },
            new Competitor { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Vention", WebsiteUrl = "https://ventionteams.com", Industry = industry, CreatedAt = DateTime.UtcNow }
        };
        return Task.FromResult<IEnumerable<Competitor>>(competitors);
    }

    public Task<IEnumerable<AiSearchPrompt>> GeneratePromptsAsync(Guid organizationId, string industry, string services)
    {
        var prompts = new List<AiSearchPrompt>
        {
            new AiSearchPrompt { Id = Guid.NewGuid(), OrganizationId = organizationId, QueryString = $"Top {industry} companies", SearchEngine = "Google", GeneratedAt = DateTime.UtcNow },
            new AiSearchPrompt { Id = Guid.NewGuid(), OrganizationId = organizationId, QueryString = $"Best agencies for {services}", SearchEngine = "Perplexity", GeneratedAt = DateTime.UtcNow }
        };
        return Task.FromResult<IEnumerable<AiSearchPrompt>>(prompts);
    }

    public Task<IEnumerable<BrandMention>> ExecutePromptSearchAsync(AiSearchPrompt prompt, IEnumerable<Competitor> knownCompetitors, string userBrandName)
    {
        var mentions = new List<BrandMention>();
        var random = new Random();
        int position = 1;

        if (random.NextDouble() > 0.3)
        {
            mentions.Add(new BrandMention
            {
                Id = Guid.NewGuid(),
                AiSearchPromptId = prompt.Id,
                OrganizationId = prompt.OrganizationId,
                BrandName = userBrandName,
                Position = position++,
                SentimentScore = random.Next(0, 101),
                MentionedAt = DateTime.UtcNow
            });
        }

        foreach (var competitor in knownCompetitors)
        {
            if (random.NextDouble() > 0.2)
            {
                mentions.Add(new BrandMention
                {
                    Id = Guid.NewGuid(),
                    AiSearchPromptId = prompt.Id,
                    OrganizationId = prompt.OrganizationId,
                    BrandName = competitor.Name,
                    Position = position++,
                    SentimentScore = random.Next(0, 101),
                    MentionedAt = DateTime.UtcNow
                });
            }
        }
        
        return Task.FromResult<IEnumerable<BrandMention>>(mentions);
    }
}
