using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services;

public class DummyAiAnalysisService : IAiAnalysisService
{
    public async Task<IEnumerable<Recommendation>> AnalyzePageAsync(CrawledPage page)
    {
        // Simulate LLM delay
        await Task.Delay(1000);

        return new List<Recommendation>
        {
            new Recommendation
            {
                Title = "Missing Target Keywords",
                Description = $"The page {page.Title} is missing secondary AEO keywords. Consider adding 'AI search optimization'.",
                ActionType = "Content Update",
                Priority = "High"
            },
            new Recommendation
            {
                Title = "Add FAQ Section",
                Description = "LLMs prefer structured Q&A. Add an FAQ section to improve Answer Engine optimization.",
                ActionType = "Schema Markup",
                Priority = "Medium"
            }
        };
    }

    public Task<double[]> GenerateEmbeddingAsync(string text)
    {
        // Generate a deterministic pseudo-random embedding based on text hash
        var random = new Random(text.GetHashCode());
        var vector = new double[1536]; // Simulate text-embedding-3-small
        
        for (int i = 0; i < 1536; i++)
        {
            vector[i] = random.NextDouble() * 2 - 1; // values between -1 and 1
        }
        
        return Task.FromResult(vector);
    }
}
