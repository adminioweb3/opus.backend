using Citationly.Domain.Entities;
using Citationly.Domain.Enums;

namespace Citationly.Application.Interfaces.Onboarding;

public interface IWebsiteContentBuilder
{
    string BuildStructuredContent(List<(ScrapedPage Page, PageCategory Category, int Score)> rankedPages, int maxTokensHint = 8000);
}
