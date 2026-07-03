using System.Text.Json;
using Citationly.Application.Features.Onboarding;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Competitors;

namespace Citationly.Infrastructure.Services.Competitors;

/// <summary>
/// Lightweight competitor discovery service.
/// Returns only names, URLs, types, and scores — no heavy enrichment data.
/// Target: ~2,000 output tokens, response in 10–20 seconds.
/// </summary>
public class CompetitorDiscoveryService : ICompetitorDiscoveryService
{
    private readonly ISearchService _searchService;
    private readonly IOpenAiService _openAiService;
    private readonly ITokenBudgetManager _tokenBudgetManager;
    private readonly ICompetitorScoringEngine _scoringEngine;

    public CompetitorDiscoveryService(
        ISearchService searchService,
        IOpenAiService openAiService,
        ITokenBudgetManager tokenBudgetManager,
        ICompetitorScoringEngine scoringEngine)
    {
        _searchService = searchService;
        _openAiService = openAiService;
        _tokenBudgetManager = tokenBudgetManager;
        _scoringEngine = scoringEngine;
    }

    public async Task<List<CompCompetitor>> DiscoverCompetitorsAsync(
        string rawProfileJson,
        string websiteUrl,
        string businessName,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Extract BI context
        var biContext = ExtractBiContext(rawProfileJson);

        // 2. Hybrid Discovery: seed from search engine
        var searchHints = new List<string>();
        try
        {
            var discovered = await _searchService.DiscoverCompetitorsAsync(
                organizationId, biContext.Industry, biContext.Services);
            if (discovered != null)
                searchHints.AddRange(discovered.Select(c => c.Name));
        }
        catch { /* gracefully fallback */ }

        // 3. Ultra-lightweight AI prompt (8 fields only)
        var systemPrompt = "You are a competitive intelligence AI. Output ONLY a JSON array. No markdown. No explanations.";

        var userPrompt = $@"Identify 40-50 competitors for this business. Return ONLY a JSON array.

Business: {businessName}
URL: {websiteUrl}
Industry: {biContext.Industry}
Services: {biContext.Services}
Audience: {biContext.TargetAudience}
Model: {biContext.BusinessModel}
USP: {biContext.USP}
{(searchHints.Any() ? $"Known competitors: {string.Join(", ", searchHints)}" : "")}

Rules:
- 40-50 unique competitors. NO duplicates.
- Mix: Direct, Indirect, Emerging, Enterprise Alternative, Niche Alternative.
- Description max 20 words.
- similarityScore and confidence: 0-100.

Schema: [{{""rank"":1,""companyName"":"""",""website"":"""",""industry"":"""",""competitorType"":""Direct"",""description"":"""",""similarityScore"":0,""confidence"":0}}]";

        int promptTokens = _tokenBudgetManager.EstimateTokens(userPrompt);
        Console.WriteLine($"[Discovery] Prompt tokens: {promptTokens} (target: <500)");

        // 4. Query AI
        var responseContent = await _openAiService.GenerateContentAsync(
            userPrompt, systemPrompt, true, "gpt-4o-mini");

        Console.WriteLine($"[Discovery] AI responded in {sw.ElapsedMilliseconds}ms");

        // 5. Extract JSON array robustly
        int startIndex = responseContent.IndexOf('[');
        int endIndex = responseContent.LastIndexOf(']');
        if (startIndex >= 0 && endIndex > startIndex)
        {
            responseContent = responseContent.Substring(startIndex, endIndex - startIndex + 1);
        }
        else
        {
            Console.WriteLine("[Discovery] Warning: AI output did not contain a valid JSON array.");
            return new List<CompCompetitor>();
        }

        // 6. Parse
        List<CompCompetitor> aiCompetitors = new();
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            aiCompetitors = JsonSerializer.Deserialize<List<CompCompetitor>>(responseContent, options) ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discovery] JSON parse failed: {ex.Message}");
            return new List<CompCompetitor>();
        }

        // 7. Deduplicate by website domain
        var uniqueCompetitors = aiCompetitors
            .GroupBy(c =>
            {
                var webKey = c.website?.Replace("https://", "").Replace("http://", "")
                    .Replace("www.", "").Trim().TrimEnd('/').ToLowerInvariant() ?? "";
                var nameKey = c.companyName?.Trim().ToLowerInvariant() ?? "";
                return !string.IsNullOrEmpty(webKey) ? webKey :
                    (string.IsNullOrEmpty(nameKey) ? Guid.NewGuid().ToString() : nameKey);
            })
            .Select(g => g.First())
            .ToList();

        // 8. Score and rank
        var rankedCompetitors = _scoringEngine.RankCompetitors(
            uniqueCompetitors,
            biContext.Industry,
            biContext.TargetAudience,
            biContext.Products,
            biContext.Services);

        for (int i = 0; i < rankedCompetitors.Count; i++)
            rankedCompetitors[i].rank = i + 1;

        sw.Stop();
        Console.WriteLine($"[Discovery] Total pipeline: {sw.ElapsedMilliseconds}ms, {rankedCompetitors.Count} competitors");

        return rankedCompetitors;
    }

    private (string Industry, string Services, string TargetAudience, string BusinessModel,
        string Products, string USP, string BrandPositioning) ExtractBiContext(string rawJson)
    {
        string ind = "Unknown", svc = "Unknown", aud = "Unknown", mod = "Unknown",
            prod = "Unknown", usp = "Unknown", brand = "Unknown";
        if (string.IsNullOrEmpty(rawJson)) return (ind, svc, aud, mod, prod, usp, brand);

        try
        {
            var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("industriesServed", out var iVal) && iVal.TryGetProperty("value", out var iArr))
                ind = string.Join(", ", iArr.EnumerateArray().Select(x => x.GetString()));

            if (root.TryGetProperty("coreServices", out var sVal) && sVal.TryGetProperty("value", out var sArr))
                svc = string.Join(", ", sArr.EnumerateArray().Select(x => x.GetString()));

            if (root.TryGetProperty("targetCustomers", out var tVal) && tVal.TryGetProperty("value", out var tArr))
                aud = string.Join(", ", tArr.EnumerateArray().Select(x => x.GetString()));

            if (root.TryGetProperty("businessModel", out var mVal) && mVal.TryGetProperty("value", out var mStr))
                mod = mStr.GetString() ?? "Unknown";

            if (root.TryGetProperty("products", out var pVal) && pVal.TryGetProperty("value", out var pArr))
                prod = string.Join(", ", pArr.EnumerateArray().Select(x => x.GetString()));

            if (root.TryGetProperty("uniqueSellingProposition", out var uVal) && uVal.TryGetProperty("value", out var uStr))
                usp = uStr.GetString() ?? "Unknown";

            if (root.TryGetProperty("brandPositioning", out var bVal) && bVal.TryGetProperty("value", out var bStr))
                brand = bStr.GetString() ?? "Unknown";
        }
        catch { }

        return (ind, svc, aud, mod, prod, usp, brand);
    }
}
