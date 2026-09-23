using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Citationly.Infrastructure.Services.WebEvidence;

public sealed class ExaEvidenceProvider : IWebEvidenceProvider
{
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new();
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly bool _enabled;
    private readonly string _searchType;
    private readonly int _defaultResultCount;
    private readonly int _maxHighlightCharacters;
    private readonly TimeSpan _cacheFreshness;
    private readonly decimal _reservedCostPerSearchUsd;
    private readonly IAiUsageLimiter _usageLimiter;
    private readonly IAiResilienceService _resilience;
    private readonly ILogger<ExaEvidenceProvider>? _logger;

    public ExaEvidenceProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        IAiUsageLimiter usageLimiter,
        IAiResilienceService resilience,
        ILogger<ExaEvidenceProvider>? logger = null)
    {
        _httpClient = httpClient;
        _apiKey = ConfigPlaceholderHelper.Resolve(configuration["Exa:ApiKey"], "EXA_API_KEY");
        _enabled = configuration.GetValue("Exa:Enabled", false);
        _searchType = configuration["Exa:SearchType"] ?? "fast";
        _defaultResultCount = Math.Clamp(configuration.GetValue("Exa:ResultsPerQuery", 5), 1, 10);
        _maxHighlightCharacters = Math.Clamp(configuration.GetValue("Exa:MaxHighlightCharacters", 2000), 200, 10000);
        _cacheFreshness = TimeSpan.FromHours(Math.Clamp(configuration.GetValue("Exa:CacheHours", 6), 1, 168));
        _reservedCostPerSearchUsd = Math.Max(0.000001m, configuration.GetValue("Exa:ReservedCostPerSearchUsd", 0.012m));
        _usageLimiter = usageLimiter;
        _resilience = resilience;
        _logger = logger;
    }

    public string ProviderKey => "exa";
    public bool IsConfigured => _enabled && _apiKey is not null;

    public async Task<WebEvidenceResult> SearchAsync(
        Guid? organizationId,
        WebEvidenceQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return WebEvidenceResult.Unavailable(ProviderKey, "Exa is disabled or EXA_API_KEY is not configured.");
        if (string.IsNullOrWhiteSpace(query.Query))
            return WebEvidenceResult.Unavailable(ProviderKey, "A non-empty evidence query is required.");

        var normalizedQuery = query.Query.Trim();
        if (normalizedQuery.Length > 1000) normalizedQuery = normalizedQuery[..1000];
        var resultCount = Math.Clamp(query.ResultCount <= 0 ? _defaultResultCount : query.ResultCount, 1, 10);
        var cacheKey = BuildCacheKey(normalizedQuery, resultCount, query.Country, query.Language, query.PublishedAfter);
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Result with { CacheHit = true, CostUsd = null };

        await _usageLimiter.EnsureWithinLimitsAsync(organizationId, "provider:exa.search", cancellationToken);
        await _usageLimiter.RecordEstimatedCostAsync(
            organizationId,
            _reservedCostPerSearchUsd,
            "provider:exa.search",
            cancellationToken);

        var contents = new
        {
            highlights = new
            {
                query = normalizedQuery,
                maxCharacters = _maxHighlightCharacters
            }
        };
        var body = new Dictionary<string, object?>
        {
            ["query"] = normalizedQuery,
            ["type"] = _searchType,
            ["numResults"] = resultCount,
            ["contents"] = contents
        };
        if (query.PublishedAfter.HasValue)
            body["startPublishedDate"] = query.PublishedAfter.Value.UtcDateTime.ToString("O");
        if (!string.IsNullOrWhiteSpace(query.Country))
            body["userLocation"] = query.Country.Trim().ToUpperInvariant();

        try
        {
            return await _resilience.ExecuteAsync("provider:exa.search", async ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "search");
                request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, ct);
                var responseText = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                        throw new HttpRequestException($"Exa returned {(int)response.StatusCode}.");
                    return WebEvidenceResult.Unavailable(ProviderKey, $"Exa returned {(int)response.StatusCode}.");
                }

                using var doc = JsonDocument.Parse(responseText);
                var root = doc.RootElement;
                var items = new List<WebEvidenceItem>();
                var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                {
                    foreach (var result in results.EnumerateArray())
                    {
                        var url = ReadString(result, "url");
                        if (!IsSafePublicUrl(url) || !seenUrls.Add(url!)) continue;
                        items.Add(new WebEvidenceItem(
                            url!,
                            ReadString(result, "title") ?? url!,
                            ReadString(result, "publishedDate"),
                            ReadString(result, "author"),
                            ReadStringArray(result, "highlights"),
                            ReadDoubleArray(result, "highlightScores")));
                    }
                }

                var requestId = ReadString(root, "requestId");
                decimal? cost = null;
                if (root.TryGetProperty("costDollars", out var costDollars)
                    && costDollars.TryGetProperty("total", out var total)
                    && total.TryGetDecimal(out var parsedCost))
                    cost = parsedCost;

                if (cost.HasValue && cost.Value > _reservedCostPerSearchUsd)
                {
                    await _usageLimiter.RecordEstimatedCostAsync(
                        organizationId,
                        cost.Value - _reservedCostPerSearchUsd,
                        "provider:exa.search",
                        ct);
                }
                var resultValue = new WebEvidenceResult(true, ProviderKey, requestId, items, cost, false, null);
                Cache[cacheKey] = new CacheEntry(resultValue, DateTimeOffset.UtcNow.Add(_cacheFreshness));
                _logger?.LogInformation(
                    "Exa search {RequestId} returned {ResultCount} evidence results for organization {OrganizationId}",
                    requestId,
                    items.Count,
                    organizationId);
                return resultValue;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Exa search failed for organization {OrganizationId}", organizationId);
            return WebEvidenceResult.Unavailable(ProviderKey, ex.Message);
        }
    }

    private static string BuildCacheKey(string query, int resultCount, string? country, string? language, DateTimeOffset? after)
    {
        var raw = $"{query.ToLowerInvariant()}|{resultCount}|{country?.ToLowerInvariant()}|{language?.ToLowerInvariant()}|{after:O}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static bool IsSafePublicUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(uri.Host);

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : Array.Empty<string>();

    private static IReadOnlyList<double> ReadDoubleArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetDouble()).ToList()
            : Array.Empty<double>();

    private sealed record CacheEntry(WebEvidenceResult Result, DateTimeOffset ExpiresAt);
}
