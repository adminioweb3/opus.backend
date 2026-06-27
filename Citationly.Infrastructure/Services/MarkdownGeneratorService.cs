using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services;

public class MarkdownGeneratorService : IMarkdownGeneratorService
{
    /// <summary>
    /// Returns the already-generated rich markdown from the scraper engine,
    /// prepended with a YAML-style frontmatter header for metadata.
    /// </summary>
    public string GenerateMarkdown(ScrapedPage page)
    {
        var sb = new StringBuilder();

        // Metadata header
        sb.AppendLine($"# {page.Title ?? "Untitled Page"}");
        sb.AppendLine();
        sb.AppendLine($"> **Source:** [{page.Url}]({page.Url})");
        if (!string.IsNullOrWhiteSpace(page.Description))
        {
            sb.AppendLine();
            sb.AppendLine($"> {page.Description}");
        }
        sb.AppendLine();

        var internalCount = CountJsonArray(page.InternalLinks);
        var externalCount = CountJsonArray(page.ExternalLinks);
        sb.AppendLine($"**Words:** {page.WordCount:N0} | **Internal Links:** {internalCount} | **External Links:** {externalCount}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // The rich body markdown already produced by PlaywrightScraperEngine
        if (!string.IsNullOrWhiteSpace(page.Content))
        {
            sb.AppendLine(page.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public string AggregateMarkdown(List<ScrapedPage> pages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Website Crawl Results");
        sb.AppendLine();
        sb.AppendLine($"**Total Pages:** {pages.Count} | **Total Words:** {pages.Sum(p => p.WordCount):N0}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // Table of contents
        if (pages.Count > 1)
        {
            sb.AppendLine("## Table of Contents");
            sb.AppendLine();
            for (int i = 0; i < pages.Count; i++)
            {
                var p = pages[i];
                sb.AppendLine($"{i + 1}. **{p.Title ?? p.Url}** — [{p.Url}]({p.Url})");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        int counter = 1;
        foreach (var page in pages)
        {
            sb.AppendLine($"<!-- Page {counter}/{pages.Count} -->");
            sb.AppendLine();
            sb.AppendLine(page.MarkdownContent ?? GenerateMarkdown(page));
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            counter++;
        }

        return sb.ToString();
    }

    private static int CountJsonArray(string? json)
    {
        if (string.IsNullOrEmpty(json)) return 0;
        try { return JsonSerializer.Deserialize<List<string>>(json)?.Count ?? 0; }
        catch { return 0; }
    }
}
