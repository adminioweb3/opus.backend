using Citationly.Application.Interfaces.Onboarding;
using Citationly.Domain.Entities;
using Citationly.Domain.Enums;
using System.Text;

namespace Citationly.Infrastructure.Services.Onboarding;

public class WebsiteContentBuilder : IWebsiteContentBuilder
{
    public string BuildStructuredContent(List<(ScrapedPage Page, PageCategory Category, int Score)> rankedPages, int maxTokensHint = 8000)
    {
        var sb = new StringBuilder();
        
        // Approximate character limit based on tokens (1 token ≈ 4 chars). 
        // We leave room for the prompt instructions.
        int maxChars = maxTokensHint * 3; 

        sb.AppendLine("## Structured Website Content");
        
        foreach (var item in rankedPages.OrderByDescending(p => p.Score))
        {
            var pageCategoryStr = item.Category.ToString();
            if (item.Category == PageCategory.Other && !string.IsNullOrEmpty(item.Page.Title))
            {
                pageCategoryStr = $"Other ({item.Page.Title})";
            }

            sb.AppendLine($"\n{pageCategoryStr}");
            sb.AppendLine("=================");
            sb.AppendLine($"URL: {item.Page.Url}");
            
            if (!string.IsNullOrEmpty(item.Page.MarkdownContent))
            {
                sb.AppendLine(item.Page.MarkdownContent);
            }
            else
            {
                sb.AppendLine("[No content extracted]");
            }

            if (sb.Length > maxChars)
            {
                sb.AppendLine("\n[Content truncated due to token limits...]");
                break;
            }
        }

        return sb.ToString();
    }
}
