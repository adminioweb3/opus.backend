using System.Text.Json;
using System.Text.RegularExpressions;
using Citationly.Application.Interfaces.GeoAudit;
using HtmlAgilityPack;

namespace Citationly.Infrastructure.Services.GeoAudit;

public sealed class GeoTechnicalAuditService : IGeoTechnicalAuditService
{
    private static readonly string[] AiBotUserAgents = { "GPTBot", "ChatGPT-User", "ClaudeBot", "Google-Extended", "PerplexityBot" };
    private readonly HttpClient _httpClient;

    public GeoTechnicalAuditService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GeoTechnicalAuditResult> AuditAsync(string websiteUrl, CancellationToken cancellationToken = default)
    {
        var homeUrl = NormalizeHomeUrl(websiteUrl);
        var checks = new List<GeoTechnicalCheck>();
        var notes = new List<string>();

        string html = string.Empty;
        try
        {
            html = await _httpClient.GetStringAsync(homeUrl, cancellationToken);
            notes.Add($"Fetched homepage HTML from {homeUrl}.");
        }
        catch (Exception ex)
        {
            checks.Add(new GeoTechnicalCheck("homepage_fetch", "Homepage fetch", 0, false, $"Homepage fetch failed: {ex.Message}"));
            return Empty(homeUrl, checks, notes);
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var robots = await TryFetchAsync(new Uri(new Uri(homeUrl), "/robots.txt").ToString(), cancellationToken);
        var sitemap = await TryFetchAsync(new Uri(new Uri(homeUrl), "/sitemap.xml").ToString(), cancellationToken);

        checks.Add(CheckRobots(robots));
        checks.Add(CheckSitemap(sitemap));
        checks.Add(CheckCanonical(doc, homeUrl));
        checks.Add(CheckStructuredData(doc));
        checks.Add(CheckFaqSchema(doc));
        checks.Add(CheckHeadings(doc));
        checks.Add(CheckMetaTitleDescription(doc));
        checks.Add(CheckSsrContent(html, doc));
        checks.Add(CheckExtractability(doc));
        checks.Add(CheckFreshness(doc));
        checks.Add(CheckEntityClarity(doc));
        checks.Add(CheckAuthoritySignals(doc));

        var pillarScores = new Dictionary<string, int>
        {
            ["answerReadiness"] = Average(CheckScore(checks, "faq_schema"), CheckScore(checks, "extractability")),
            ["schemaCoverage"] = Average(CheckScore(checks, "structured_data"), CheckScore(checks, "faq_schema")),
            ["extractability"] = Average(CheckScore(checks, "heading_structure"), CheckScore(checks, "ssr_content"), CheckScore(checks, "extractability")),
            ["freshness"] = CheckScore(checks, "freshness"),
            ["entityClarity"] = Average(CheckScore(checks, "metadata"), CheckScore(checks, "entity_clarity")),
            ["authoritySignals"] = CheckScore(checks, "authority_signals"),
        };

        var seoHealth = WeightedAverage(
            (CheckScore(checks, "robots_ai_access"), 20),
            (CheckScore(checks, "sitemap"), 20),
            (CheckScore(checks, "canonical"), 15),
            (CheckScore(checks, "heading_structure"), 15),
            (CheckScore(checks, "metadata"), 20),
            (CheckScore(checks, "ssr_content"), 10));

        var aeoReadiness = WeightedAverage(
            (pillarScores["answerReadiness"], 30),
            (pillarScores["schemaCoverage"], 25),
            (pillarScores["extractability"], 25),
            (pillarScores["entityClarity"], 20));

        var overall = WeightedAverage(
            (CheckScore(checks, "robots_ai_access"), 15),
            (CheckScore(checks, "sitemap"), 10),
            (pillarScores["schemaCoverage"], 20),
            (pillarScores["extractability"], 20),
            (pillarScores["answerReadiness"], 15),
            (pillarScores["entityClarity"], 10),
            (pillarScores["authoritySignals"], 10));

        return new GeoTechnicalAuditResult(homeUrl, overall, seoHealth, aeoReadiness, pillarScores, checks, notes);
    }

    private async Task<string?> TryFetchAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static GeoTechnicalCheck CheckRobots(string? robots)
    {
        if (string.IsNullOrWhiteSpace(robots))
        {
            return new("robots_ai_access", "AI bot access in robots.txt", 40, false, "robots.txt was not found or could not be fetched.");
        }

        var blockedBots = AiBotUserAgents.Where(bot => IsBotBlocked(robots, bot)).ToList();
        if (blockedBots.Count == 0)
        {
            return new("robots_ai_access", "AI bot access in robots.txt", 100, true, "No explicit AI bot disallow rules found.");
        }

        var score = Math.Max(0, 100 - blockedBots.Count * 20);
        return new("robots_ai_access", "AI bot access in robots.txt", score, false, $"Blocked AI crawlers: {string.Join(", ", blockedBots)}.");
    }

    private static bool IsBotBlocked(string robots, string bot)
    {
        var lines = robots.Split('\n').Select(l => l.Trim()).ToList();
        var applies = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("#") || line.Length == 0) continue;
            if (line.StartsWith("user-agent:", StringComparison.OrdinalIgnoreCase))
            {
                var agent = line.Split(':', 2)[1].Trim();
                applies = agent == "*" || agent.Equals(bot, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (applies && line.StartsWith("disallow:", StringComparison.OrdinalIgnoreCase))
            {
                var path = line.Split(':', 2)[1].Trim();
                if (path == "/" || path.StartsWith("/", StringComparison.Ordinal)) return true;
            }
        }

        return false;
    }

    private static GeoTechnicalCheck CheckSitemap(string? sitemap)
    {
        if (string.IsNullOrWhiteSpace(sitemap)) return new("sitemap", "Sitemap", 0, false, "sitemap.xml was not found.");
        var hasUrlSet = sitemap.Contains("<urlset", StringComparison.OrdinalIgnoreCase) || sitemap.Contains("<sitemapindex", StringComparison.OrdinalIgnoreCase);
        return new("sitemap", "Sitemap", hasUrlSet ? 100 : 50, hasUrlSet, hasUrlSet ? "XML sitemap structure detected." : "Fetched sitemap, but XML sitemap structure was unclear.");
    }

    private static GeoTechnicalCheck CheckCanonical(HtmlDocument doc, string homeUrl)
    {
        var canonical = doc.DocumentNode.SelectSingleNode("//link[translate(@rel,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='canonical']")?.GetAttributeValue("href", "");
        var passed = !string.IsNullOrWhiteSpace(canonical);
        return new("canonical", "Canonical URL", passed ? 100 : 0, passed, passed ? $"Canonical tag points to {canonical}." : $"No canonical tag found on {homeUrl}.");
    }

    private static GeoTechnicalCheck CheckStructuredData(HtmlDocument doc)
    {
        var jsonLdNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']")?.ToList() ?? new List<HtmlNode>();
        if (jsonLdNodes.Count == 0) return new("structured_data", "Schema.org structured data", 0, false, "No JSON-LD schema blocks found.");

        var types = jsonLdNodes
            .SelectMany(n => ExtractSchemaTypes(n.InnerText))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var score = types.Count == 0 ? 60 : Math.Min(100, 70 + types.Count * 10);
        return new("structured_data", "Schema.org structured data", score, true, types.Count == 0 ? $"{jsonLdNodes.Count} JSON-LD block(s) found." : $"Schema types: {string.Join(", ", types.Take(6))}.");
    }

    private static GeoTechnicalCheck CheckFaqSchema(HtmlDocument doc)
    {
        var text = string.Join("\n", doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']")?.Select(n => n.InnerText) ?? Enumerable.Empty<string>());
        var hasFaqSchema = text.Contains("FAQPage", StringComparison.OrdinalIgnoreCase);
        var hasFaqContent = Regex.IsMatch(doc.DocumentNode.InnerText, @"\b(faq|frequently asked questions)\b", RegexOptions.IgnoreCase);
        var score = hasFaqSchema ? 100 : hasFaqContent ? 60 : 20;
        return new("faq_schema", "FAQ/answer schema", score, hasFaqSchema, hasFaqSchema ? "FAQPage schema detected." : hasFaqContent ? "FAQ-like content exists but no FAQPage schema was detected." : "No FAQPage schema or obvious FAQ section detected.");
    }

    private static GeoTechnicalCheck CheckHeadings(HtmlDocument doc)
    {
        var h1Count = doc.DocumentNode.SelectNodes("//h1")?.Count ?? 0;
        var h2Count = doc.DocumentNode.SelectNodes("//h2")?.Count ?? 0;
        var passed = h1Count == 1 && h2Count >= 2;
        var score = passed ? 100 : h1Count >= 1 && h2Count >= 1 ? 70 : h1Count >= 1 ? 50 : 10;
        return new("heading_structure", "Heading hierarchy", score, passed, $"Found {h1Count} H1 and {h2Count} H2 headings.");
    }

    private static GeoTechnicalCheck CheckMetaTitleDescription(HtmlDocument doc)
    {
        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "";
        var description = doc.DocumentNode.SelectSingleNode("//meta[translate(@name,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='description']")?.GetAttributeValue("content", "").Trim() ?? "";
        var score = 0;
        if (title.Length is >= 20 and <= 70) score += 45;
        else if (title.Length > 0) score += 25;
        if (description.Length is >= 70 and <= 170) score += 55;
        else if (description.Length > 0) score += 30;
        return new("metadata", "Title and meta description", score, score >= 75, $"Title length {title.Length}; meta description length {description.Length}.");
    }

    private static GeoTechnicalCheck CheckSsrContent(string html, HtmlDocument doc)
    {
        var bodyText = doc.DocumentNode.SelectSingleNode("//body")?.InnerText ?? doc.DocumentNode.InnerText;
        var words = WordCount(bodyText);
        var rootShellPenalty = html.Contains("<div id=\"__next\"></div>", StringComparison.OrdinalIgnoreCase) || html.Contains("<div id=\"root\"></div>", StringComparison.OrdinalIgnoreCase);
        var score = words >= 400 ? 100 : words >= 150 ? 75 : words >= 50 ? 45 : 10;
        if (rootShellPenalty) score = Math.Min(score, 25);
        return new("ssr_content", "Server-rendered content", score, score >= 75, $"{words} words present in initial HTML{(rootShellPenalty ? "; app shell detected" : "")}.");
    }

    private static GeoTechnicalCheck CheckExtractability(HtmlDocument doc)
    {
        var lists = doc.DocumentNode.SelectNodes("//ul|//ol")?.Count ?? 0;
        var tables = doc.DocumentNode.SelectNodes("//table")?.Count ?? 0;
        var shortParagraphs = doc.DocumentNode.SelectNodes("//p")
            ?.Count(p => WordCount(p.InnerText) is >= 8 and <= 80) ?? 0;
        var score = Math.Min(100, lists * 15 + tables * 20 + shortParagraphs * 5);
        return new("extractability", "Extractable answer structure", score, score >= 60, $"Found {lists} lists, {tables} tables, and {shortParagraphs} concise paragraphs.");
    }

    private static GeoTechnicalCheck CheckFreshness(HtmlDocument doc)
    {
        var hasTime = doc.DocumentNode.SelectSingleNode("//time") != null;
        var hasDateText = Regex.IsMatch(doc.DocumentNode.InnerText, @"\b(20[2-9][0-9]|updated|last modified|published)\b", RegexOptions.IgnoreCase);
        var score = hasTime ? 100 : hasDateText ? 65 : 25;
        return new("freshness", "Freshness signals", score, hasTime || hasDateText, hasTime ? "A <time> element was found." : hasDateText ? "Date/update text was found." : "No visible freshness signal found.");
    }

    private static GeoTechnicalCheck CheckEntityClarity(HtmlDocument doc)
    {
        var jsonLdText = string.Join("\n", doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']")?.Select(n => n.InnerText) ?? Enumerable.Empty<string>());
        var hasOrgSchema = jsonLdText.Contains("Organization", StringComparison.OrdinalIgnoreCase) || jsonLdText.Contains("LocalBusiness", StringComparison.OrdinalIgnoreCase);
        var hasOgSiteName = doc.DocumentNode.SelectSingleNode("//meta[@property='og:site_name']") != null;
        var score = (hasOrgSchema ? 70 : 0) + (hasOgSiteName ? 30 : 0);
        return new("entity_clarity", "Entity clarity", score, score >= 70, $"{(hasOrgSchema ? "Organization schema detected" : "No Organization schema detected")}; {(hasOgSiteName ? "og:site_name detected" : "no og:site_name")}.");
    }

    private static GeoTechnicalCheck CheckAuthoritySignals(HtmlDocument doc)
    {
        var outboundLinks = doc.DocumentNode.SelectNodes("//a[@href]")?
            .Select(a => a.GetAttributeValue("href", ""))
            .Count(h => h.StartsWith("http", StringComparison.OrdinalIgnoreCase)) ?? 0;
        var sourceWords = Regex.Matches(doc.DocumentNode.InnerText, @"\b(source|study|research|report|according to|citation|references)\b", RegexOptions.IgnoreCase).Count;
        var score = Math.Min(100, outboundLinks * 8 + sourceWords * 10);
        return new("authority_signals", "Authority signals", score, score >= 50, $"Found {outboundLinks} outbound links and {sourceWords} source/reference phrases.");
    }

    private static IEnumerable<string> ExtractSchemaTypes(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ExtractTypes(doc.RootElement).ToList();
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    private static IEnumerable<string> ExtractTypes(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("@type", out var type))
            {
                if (type.ValueKind == JsonValueKind.String) yield return type.GetString() ?? "";
                if (type.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in type.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String))
                    {
                        yield return item.GetString() ?? "";
                    }
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                foreach (var nested in ExtractTypes(prop.Value)) yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in ExtractTypes(item)) yield return nested;
            }
        }
    }

    private static GeoTechnicalAuditResult Empty(string url, IReadOnlyList<GeoTechnicalCheck> checks, IReadOnlyList<string> notes)
    {
        var pillars = new Dictionary<string, int>
        {
            ["answerReadiness"] = 0,
            ["schemaCoverage"] = 0,
            ["extractability"] = 0,
            ["freshness"] = 0,
            ["entityClarity"] = 0,
            ["authoritySignals"] = 0,
        };
        return new GeoTechnicalAuditResult(url, 0, 0, 0, pillars, checks, notes);
    }

    private static string NormalizeHomeUrl(string websiteUrl)
    {
        var trimmed = websiteUrl.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }
        return trimmed;
    }

    private static int CheckScore(IEnumerable<GeoTechnicalCheck> checks, string key) =>
        checks.FirstOrDefault(c => c.Key == key)?.Score ?? 0;

    private static int Average(params int[] values) =>
        values.Length == 0 ? 0 : (int)Math.Round(values.Average());

    private static int WeightedAverage(params (int Score, int Weight)[] scores)
    {
        var totalWeight = scores.Sum(s => s.Weight);
        if (totalWeight == 0) return 0;
        return (int)Math.Round(scores.Sum(s => s.Score * s.Weight) / (double)totalWeight);
    }

    private static int WordCount(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : Regex.Matches(text, @"\b[\p{L}\p{N}'-]+\b").Count;
}
