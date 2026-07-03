using Citationly.Domain.Entities;
using Citationly.Domain.Enums;

namespace Citationly.Application.Interfaces.Onboarding;

public interface IPageClassificationService
{
    PageCategory ClassifyPage(ScrapedPage page);
}
