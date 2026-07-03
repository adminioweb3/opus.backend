using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Onboarding;

public interface IContentCleaningService
{
    List<ScrapedPage> CleanPages(List<ScrapedPage> pages);
}
