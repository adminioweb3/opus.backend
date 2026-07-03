using Citationly.Application.Interfaces.Onboarding;
using Citationly.Domain.Entities;
using System.Text.RegularExpressions;

namespace Citationly.Infrastructure.Services.Onboarding;

public class ContentCleaningService : IContentCleaningService
{
    public List<ScrapedPage> CleanPages(List<ScrapedPage> pages)
    {
        if (pages == null || !pages.Any()) return new List<ScrapedPage>();

        // We want to identify lines/blocks that repeat across many pages
        // e.g. duplicate navigation, footers, cookie banners.
        
        var blockFrequency = new Dictionary<string, int>();
        var pageBlocks = new Dictionary<Guid, List<string>>();

        // Pre-process and hash paragraphs
        foreach (var page in pages)
        {
            var md = page.MarkdownContent ?? string.Empty;
            var blocks = md.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(b => b.Trim())
                           .Where(b => b.Length > 0)
                           .ToList();

            pageBlocks[page.Id] = blocks;

            // Only consider blocks that have at least some text (not just single characters)
            var uniqueBlocksInPage = new HashSet<string>();
            foreach (var block in blocks)
            {
                if (block.Length > 10)
                {
                    uniqueBlocksInPage.Add(block);
                }
            }

            foreach (var b in uniqueBlocksInPage)
            {
                if (!blockFrequency.ContainsKey(b)) blockFrequency[b] = 0;
                blockFrequency[b]++;
            }
        }

        // Determine boilerplate threshold (e.g. appears in > 50% of pages, if we have more than 2 pages)
        int threshold = pages.Count > 2 ? Math.Max(2, (int)(pages.Count * 0.5)) : 3;

        var cleanedPages = new List<ScrapedPage>();

        foreach (var page in pages)
        {
            var originalBlocks = pageBlocks[page.Id];
            var cleanedBlocks = new List<string>();

            foreach (var block in originalBlocks)
            {
                // Preserve headings regardless of frequency
                if (block.StartsWith("#") || block.StartsWith("##") || block.StartsWith("###"))
                {
                    cleanedBlocks.Add(block);
                    continue;
                }

                // Remove known noise patterns
                var lowerBlock = block.ToLowerInvariant();
                if (lowerBlock.Contains("we use cookies") || lowerBlock.Contains("accept all cookies") || lowerBlock.Contains("privacy policy"))
                {
                    if (block.Length < 300) continue; // Likely a cookie banner or privacy footer
                }

                if (lowerBlock.StartsWith("©") || lowerBlock.Contains("all rights reserved"))
                {
                    if (block.Length < 200) continue; // Copyright footer
                }

                // Check frequency
                if (block.Length > 10 && blockFrequency.ContainsKey(block) && blockFrequency[block] >= threshold)
                {
                    continue; // Skip global boilerplate
                }

                cleanedBlocks.Add(block);
            }

            var newPage = new ScrapedPage
            {
                Id = page.Id,
                Url = page.Url,
                Title = page.Title,
                MarkdownContent = string.Join("\n\n", cleanedBlocks),
                Headings = page.Headings
            };
            
            cleanedPages.Add(newPage);
        }

        return cleanedPages;
    }
}
