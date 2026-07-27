using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Citationly.Application.Interfaces.Companies;

namespace Citationly.Infrastructure.Services.Companies;

public class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public string ModelName => "text-embedding-3-small";

    public OpenAiEmbeddingService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OpenAI:ApiKey"] ?? string.Empty;
    }

    public async Task<double[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrWhiteSpace(text)) return null;

        // text-embedding-3-small's context window is 8191 tokens — a generous char cap keeps
        // callers from having to think about tokenization themselves.
        var input = text.Length > 24000 ? text[..24000] : text;

        var requestBody = new { model = ModelName, input };
        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        try
        {
            var response = await _httpClient.PostAsync("https://api.openai.com/v1/embeddings", content, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseString);
            var vectorElement = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");
            return vectorElement.EnumerateArray().Select(e => e.GetDouble()).ToArray();
        }
        catch
        {
            return null;
        }
    }
}
