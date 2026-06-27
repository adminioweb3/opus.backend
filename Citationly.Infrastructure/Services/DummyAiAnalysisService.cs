using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text;

namespace Citationly.Infrastructure.Services;

public class DummyAiAnalysisService : IAiAnalysisService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public DummyAiAnalysisService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

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

    public async Task<List<ShareOfVoice>> GenerateCompetitorsAsync(string domainUrl, Guid orgId)
    {
        var apiKey = _configuration["OpenRouter:ApiKey"];
        
        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_OPEN_ROUTER_API_KEY")
        {
            // Fallback to mock data if API key is missing
            return new List<ShareOfVoice>
            {
                new ShareOfVoice { OrganizationId = orgId, ScanDate = DateOnly.FromDateTime(DateTime.UtcNow), CompetitorName = "Competitor A", SharePercentage = 45, ColorCode = "#3b82f6" },
                new ShareOfVoice { OrganizationId = orgId, ScanDate = DateOnly.FromDateTime(DateTime.UtcNow), CompetitorName = "Competitor B", SharePercentage = 30, ColorCode = "#10b981" },
                new ShareOfVoice { OrganizationId = orgId, ScanDate = DateOnly.FromDateTime(DateTime.UtcNow), CompetitorName = domainUrl, SharePercentage = 25, ColorCode = "#f59e0b" }
            };
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:3000"); 
            httpClient.DefaultRequestHeaders.Add("X-Title", "Citationly Onboarding");

            var systemPrompt = $@"You are an AI SEO and Answer Engine Optimization analyst. 
Your task is to identify 2-3 real-world competitors for the website: {domainUrl}.
You must also estimate a realistic 'Share of Voice' percentage (totaling 100% across all including the target domain).
You must output EXACTLY a JSON array of objects with the following format and nothing else:
[
  {{
    ""competitorName"": ""Name of competitor"",
    ""sharePercentage"": 45,
    ""colorCode"": ""#hexcode""
  }}
]
Make sure one of the entries is for {domainUrl} itself. Ensure valid JSON.";

            var payload = new
            {
                model = "openai/gpt-3.5-turbo", 
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"Analyze {domainUrl}" }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);

            if (response.IsSuccessStatusCode)
            {
                var resultString = await response.Content.ReadAsStringAsync();
                using var jsonDoc = JsonDocument.Parse(resultString);
                
                var replyMessage = jsonDoc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "";

                // Extract JSON array from Markdown blocks if present
                if (replyMessage.Contains("```json"))
                {
                    var start = replyMessage.IndexOf("```json") + 7;
                    var end = replyMessage.IndexOf("```", start);
                    replyMessage = replyMessage.Substring(start, end - start).Trim();
                }
                else if (replyMessage.Contains("```"))
                {
                    var start = replyMessage.IndexOf("```") + 3;
                    var end = replyMessage.IndexOf("```", start);
                    replyMessage = replyMessage.Substring(start, end - start).Trim();
                }

                var parsedOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var competitors = JsonSerializer.Deserialize<List<CompetitorDto>>(replyMessage, parsedOptions);
                
                if (competitors != null && competitors.Any())
                {
                    return competitors.Select(c => new ShareOfVoice
                    {
                        OrganizationId = orgId,
                        ScanDate = DateOnly.FromDateTime(DateTime.UtcNow),
                        CompetitorName = c.CompetitorName ?? "Unknown",
                        SharePercentage = c.SharePercentage,
                        ColorCode = c.ColorCode ?? "#cccccc"
                    }).ToList();
                }
            }
        }
        catch (Exception)
        {
            // Silently swallow errors during onboarding and return fallback
        }

        // Fallback
        return new List<ShareOfVoice>
        {
            new ShareOfVoice { OrganizationId = orgId, ScanDate = DateOnly.FromDateTime(DateTime.UtcNow), CompetitorName = "Competitor A", SharePercentage = 45, ColorCode = "#3b82f6" },
            new ShareOfVoice { OrganizationId = orgId, ScanDate = DateOnly.FromDateTime(DateTime.UtcNow), CompetitorName = "Competitor B", SharePercentage = 30, ColorCode = "#10b981" },
            new ShareOfVoice { OrganizationId = orgId, ScanDate = DateOnly.FromDateTime(DateTime.UtcNow), CompetitorName = domainUrl, SharePercentage = 25, ColorCode = "#f59e0b" }
        };
    }

    private class CompetitorDto
    {
        public string CompetitorName { get; set; } = string.Empty;
        public int SharePercentage { get; set; }
        public string ColorCode { get; set; } = string.Empty;
    }
}
