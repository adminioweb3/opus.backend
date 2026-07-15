using Microsoft.Playwright;
using HtmlAgilityPack;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Citationly.Infrastructure.Services.Scraping;

public class PlaywrightScraperEngine : IScraperEngine
{
    private readonly IMarkdownGeneratorService _markdownGenerator;

    public PlaywrightScraperEngine(IMarkdownGeneratorService markdownGenerator)
    {
        _markdownGenerator = markdownGenerator;
    }

    public async Task<ScrapedPage> ScrapeSinglePageAsync(string url, Guid jobId)
    {
        using var playwright = await Playwright.CreateAsync();
        // --no-sandbox is required to run headless Chromium as root in Docker (the default here) —
        // Chromium's sandbox needs Linux namespace privileges containers don't grant without extra
        // capabilities, and without this flag it fails to launch at all rather than just running unsandboxed.
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" }
        });
        var page = await browser.NewPageAsync();

        try
        {
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
        }
        catch
        {
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
        }

        var title = await page.TitleAsync();
        var html = await page.ContentAsync();

        return ParsePageToScrapedPage(url, jobId, title, html);
    }

    public async Task<List<ScrapedPage>> ScrapeWebsiteAsync(string startUrl, Guid jobId, int maxPages, Action<int>? progressCallback = null)
    {
        var visitedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        var results = new List<ScrapedPage>();
        var baseUri = new Uri(startUrl);

        queue.Enqueue(NormalizeUrl(startUrl));

        using var playwright = await Playwright.CreateAsync();
        // --no-sandbox is required to run headless Chromium as root in Docker (the default here) —
        // Chromium's sandbox needs Linux namespace privileges containers don't grant without extra
        // capabilities, and without this flag it fails to launch at all rather than just running unsandboxed.
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" }
        });

        while (queue.Count > 0 && results.Count < maxPages)
        {
            var url = queue.Dequeue();
            if (visitedUrls.Contains(url)) continue;
            visitedUrls.Add(url);

            try
            {
                var page = await browser.NewPageAsync();
                try
                {
                    await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
                }
                catch
                {
                    await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
                }

                var title = await page.TitleAsync();
                var html = await page.ContentAsync();
                await page.CloseAsync();

                var scrapedPage = ParsePageToScrapedPage(url, jobId, title, html);
                results.Add(scrapedPage);
                progressCallback?.Invoke(results.Count);

                // Enqueue discovered internal links
                var internalLinks = TryDeserialize<List<string>>(scrapedPage.InternalLinks) ?? new();
                foreach (var link in internalLinks)
                {
                    var normalized = NormalizeUrl(link);
                    if (string.IsNullOrEmpty(normalized)) continue;

                    // Only follow same-domain links
                    try
                    {
                        var linkUri = new Uri(normalized);
                        if (linkUri.Host != baseUri.Host) continue;
                    }
                    catch { continue; }

                    if (!visitedUrls.Contains(normalized) && !queue.Contains(normalized))
                        queue.Enqueue(normalized);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Scraper] Failed to scrape {url}: {ex.Message}");
            }
        }

        return results;
    }

    // ── Core parser: HTML → ScrapedPage with rich Markdown ──────────────────

    private ScrapedPage ParsePageToScrapedPage(string url, Guid jobId, string title, string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove noise nodes
        RemoveNodes(doc, "//script|//style|//noscript|//iframe|//svg");

        var metaDesc = doc.DocumentNode
            .SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", "");

        // Build markdown preserving full structure (links, images, headings, lists)
        var markdownSb = new StringBuilder();
        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        ConvertToMarkdown(body, markdownSb, url);

        var markdown = CleanMarkdown(markdownSb.ToString());

        // Extract plain text for word count
        var plainText = doc.DocumentNode.InnerText;
        var wordCount = plainText.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

        // Extract links
        var (internalLinks, externalLinks) = ExtractLinks(doc, url);

        // Extract images
        var images = ExtractImages(doc, url);

        // Extract headings
        var headings = ExtractHeadings(doc);

        var scrapedPage = new ScrapedPage
        {
            JobId = jobId,
            Url = url,
            Title = title,
            Description = metaDesc,
            Content = markdown,         // store the rich markdown as "content"
            HtmlContent = html,
            MarkdownContent = markdown,
            WordCount = wordCount,
            Images = JsonSerializer.Serialize(images),
            InternalLinks = JsonSerializer.Serialize(internalLinks),
            ExternalLinks = JsonSerializer.Serialize(externalLinks),
            Headings = JsonSerializer.Serialize(headings)
        };

        return scrapedPage;
    }

    // ── Markdown converter (preserves links, images, headings, lists) ────────

    private static void ConvertToMarkdown(HtmlNode node, StringBuilder sb, string baseUrl)
    {
        foreach (var child in node.ChildNodes)
        {
            var tag = child.Name.ToLower();

            switch (tag)
            {
                case "h1": AppendHeading(child, sb, 1); break;
                case "h2": AppendHeading(child, sb, 2); break;
                case "h3": AppendHeading(child, sb, 3); break;
                case "h4": AppendHeading(child, sb, 4); break;
                case "h5": AppendHeading(child, sb, 5); break;
                case "h6": AppendHeading(child, sb, 6); break;

                case "p":
                {
                    var inline = ConvertInline(child, baseUrl);
                    if (!string.IsNullOrWhiteSpace(inline))
                    {
                        sb.AppendLine(inline);
                        sb.AppendLine();
                    }
                    break;
                }

                case "ul":
                case "ol":
                    ConvertList(child, sb, baseUrl, ordered: tag == "ol");
                    sb.AppendLine();
                    break;

                case "blockquote":
                {
                    var inner = new StringBuilder();
                    ConvertToMarkdown(child, inner, baseUrl);
                    foreach (var line in inner.ToString().Split('\n'))
                        sb.AppendLine("> " + line);
                    sb.AppendLine();
                    break;
                }

                case "a":
                {
                    var inline = ConvertInline(child, baseUrl);
                    if (!string.IsNullOrWhiteSpace(inline))
                    {
                        sb.AppendLine(inline);
                        sb.AppendLine();
                    }
                    break;
                }

                case "img":
                {
                    var md = ConvertImg(child, baseUrl);
                    if (!string.IsNullOrWhiteSpace(md))
                    {
                        sb.AppendLine(md);
                        sb.AppendLine();
                    }
                    break;
                }

                case "br":
                    sb.AppendLine();
                    break;

                case "hr":
                    sb.AppendLine("---");
                    sb.AppendLine();
                    break;

                case "strong":
                case "b":
                {
                    var text = child.InnerText.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine($"**{text}**");
                        sb.AppendLine();
                    }
                    break;
                }

                case "#text":
                {
                    var text = HttpUtility.HtmlDecode(child.InnerText).Trim();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length > 2)
                    {
                        sb.AppendLine(text);
                        sb.AppendLine();
                    }
                    break;
                }

                case "div":
                case "section":
                case "article":
                case "main":
                case "header":
                case "footer":
                case "nav":
                case "aside":
                case "figure":
                case "figcaption":
                case "span":
                case "li":  // handled by ConvertList but catch stragglers
                    ConvertToMarkdown(child, sb, baseUrl);
                    break;
            }
        }
    }

    private static void AppendHeading(HtmlNode node, StringBuilder sb, int level)
    {
        var text = HttpUtility.HtmlDecode(node.InnerText).Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        sb.AppendLine();
        sb.AppendLine(new string('#', level) + " " + text);
        sb.AppendLine();
    }

    private static void ConvertList(HtmlNode listNode, StringBuilder sb, string baseUrl, bool ordered)
    {
        int idx = 1;
        foreach (var li in listNode.ChildNodes.Where(n => n.Name.ToLower() == "li"))
        {
            // Check if li has nested ul/ol
            var nestedList = li.ChildNodes.FirstOrDefault(n => n.Name.ToLower() is "ul" or "ol");
            var liText = "";

            if (nestedList != null)
            {
                // Get text before the nested list
                var textNodes = li.ChildNodes.TakeWhile(n => n.Name.ToLower() is not "ul" and not "ol");
                liText = string.Concat(textNodes.Select(n => ConvertInline(n, baseUrl))).Trim();
            }
            else
            {
                liText = ConvertInline(li, baseUrl).Trim();
            }

            if (!string.IsNullOrWhiteSpace(liText))
            {
                var prefix = ordered ? $"{idx}. " : "- ";
                sb.AppendLine(prefix + liText);
                idx++;
            }

            // Nested list with indentation
            if (nestedList != null)
            {
                var nested = new StringBuilder();
                ConvertList(nestedList, nested, baseUrl, nestedList.Name.ToLower() == "ol");
                foreach (var line in nested.ToString().Split('\n').Where(l => !string.IsNullOrEmpty(l)))
                    sb.AppendLine("  " + line);
            }
        }
    }

    private static string ConvertInline(HtmlNode node, string baseUrl)
    {
        var sb = new StringBuilder();
        foreach (var child in node.ChildNodes)
        {
            var tag = child.Name.ToLower();
            switch (tag)
            {
                case "#text":
                    var t = HttpUtility.HtmlDecode(child.InnerText);
                    if (!string.IsNullOrWhiteSpace(t)) sb.Append(t);
                    break;
                case "a":
                    var href = child.GetAttributeValue("href", "");
                    var linkText = HttpUtility.HtmlDecode(child.InnerText).Trim();
                    // Resolve relative URLs
                    if (href.StartsWith("/")) href = ResolveUrl(baseUrl, href);
                    if (!string.IsNullOrEmpty(linkText) && !string.IsNullOrEmpty(href))
                        sb.Append($"[{linkText}]({href})");
                    else if (!string.IsNullOrEmpty(linkText))
                        sb.Append(linkText);
                    break;
                case "img":
                    sb.Append(ConvertImg(child, baseUrl));
                    break;
                case "strong":
                case "b":
                    var bold = HttpUtility.HtmlDecode(child.InnerText).Trim();
                    if (!string.IsNullOrEmpty(bold)) sb.Append($"**{bold}**");
                    break;
                case "em":
                case "i":
                    var em = HttpUtility.HtmlDecode(child.InnerText).Trim();
                    if (!string.IsNullOrEmpty(em)) sb.Append($"*{em}*");
                    break;
                case "code":
                    var code = HttpUtility.HtmlDecode(child.InnerText).Trim();
                    if (!string.IsNullOrEmpty(code)) sb.Append($"`{code}`");
                    break;
                default:
                    // Recurse for spans etc.
                    sb.Append(ConvertInline(child, baseUrl));
                    break;
            }
        }
        return sb.ToString();
    }

    private static string ConvertImg(HtmlNode img, string baseUrl)
    {
        var src = img.GetAttributeValue("src", "");
        var alt = img.GetAttributeValue("alt", "image");
        if (string.IsNullOrEmpty(src)) return "";
        if (src.StartsWith("/")) src = ResolveUrl(baseUrl, src);
        return $"![{alt}]({src})";
    }

    private static string ResolveUrl(string baseUrl, string path)
    {
        try
        {
            var baseUri = new Uri(baseUrl);
            return $"{baseUri.Scheme}://{baseUri.Host}{path}";
        }
        catch { return path; }
    }

    private static string CleanMarkdown(string md)
    {
        // Collapse 3+ consecutive blank lines into 2
        var lines = md.Split('\n');
        var result = new List<string>();
        int blankCount = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                blankCount++;
                if (blankCount <= 2) result.Add("");
            }
            else
            {
                blankCount = 0;
                result.Add(line.TrimEnd());
            }
        }
        return string.Join("\n", result).Trim();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void RemoveNodes(HtmlDocument doc, string xpath)
    {
        var nodes = doc.DocumentNode.SelectNodes(xpath);
        if (nodes == null) return;
        foreach (var node in nodes.ToList()) node.Remove();
    }

    private static List<object> ExtractHeadings(HtmlDocument doc)
    {
        var headings = new List<object>();
        var nodes = doc.DocumentNode.SelectNodes("//h1|//h2|//h3|//h4|//h5|//h6");
        if (nodes == null) return headings;
        foreach (var node in nodes)
        {
            var text = HttpUtility.HtmlDecode(node.InnerText).Trim();
            if (!string.IsNullOrWhiteSpace(text))
                headings.Add(new { level = int.Parse(node.Name[1].ToString()), text });
        }
        return headings;
    }

    private static List<object> ExtractImages(HtmlDocument doc, string baseUrl)
    {
        var images = new List<object>();
        var nodes = doc.DocumentNode.SelectNodes("//img[@src]");
        if (nodes == null) return images;
        foreach (var img in nodes)
        {
            var src = img.GetAttributeValue("src", "");
            var alt = img.GetAttributeValue("alt", "");
            if (string.IsNullOrEmpty(src)) continue;
            if (src.StartsWith("/")) src = ResolveUrl(baseUrl, src);
            images.Add(new { src, alt });
        }
        return images;
    }

    private static (List<string> internalLinks, List<string> externalLinks) ExtractLinks(HtmlDocument doc, string baseUrl)
    {
        var internalLinks = new List<string>();
        var externalLinks = new List<string>();
        var baseUri = new Uri(baseUrl);
        var nodes = doc.DocumentNode.SelectNodes("//a[@href]");
        if (nodes == null) return (internalLinks, externalLinks);

        foreach (var a in nodes)
        {
            var href = a.GetAttributeValue("href", "").Trim();
            if (string.IsNullOrEmpty(href) || href.StartsWith("#") || href.StartsWith("mailto:") || href.StartsWith("tel:")) continue;

            if (href.StartsWith("/"))
            {
                var absolute = $"{baseUri.Scheme}://{baseUri.Host}{href}";
                internalLinks.Add(NormalizeUrl(absolute));
            }
            else if (href.StartsWith("http"))
            {
                try
                {
                    var linkUri = new Uri(href);
                    if (linkUri.Host == baseUri.Host)
                        internalLinks.Add(NormalizeUrl(href));
                    else
                        externalLinks.Add(href);
                }
                catch { }
            }
        }

        return (internalLinks.Distinct().ToList(), externalLinks.Distinct().ToList());
    }

    private static string NormalizeUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            // Strip fragments and common tracking params
            var normalized = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
            // Remove trailing slash unless root
            if (normalized.EndsWith("/") && uri.AbsolutePath.Length > 1)
                normalized = normalized.TrimEnd('/');
            return normalized;
        }
        catch { return url; }
    }

    private static T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return null; }
    }
}
