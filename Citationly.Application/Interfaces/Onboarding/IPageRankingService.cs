using Citationly.Domain.Enums;

namespace Citationly.Application.Interfaces.Onboarding;

public interface IPageRankingService
{
    int ScorePage(PageCategory category);
}
