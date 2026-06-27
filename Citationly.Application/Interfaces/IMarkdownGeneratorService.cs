using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IMarkdownGeneratorService
{
    string GenerateMarkdown(ScrapedPage page);
    string AggregateMarkdown(List<ScrapedPage> pages);
}
