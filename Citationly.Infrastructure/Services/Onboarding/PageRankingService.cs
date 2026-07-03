using Citationly.Application.Interfaces.Onboarding;
using Citationly.Domain.Enums;

namespace Citationly.Infrastructure.Services.Onboarding;

public class PageRankingService : IPageRankingService
{
    public int ScorePage(PageCategory category)
    {
        return category switch
        {
            PageCategory.Homepage => 100,
            PageCategory.About => 98,
            PageCategory.Services => 97,
            PageCategory.Products => 96,
            PageCategory.Solutions => 95,
            PageCategory.Industries => 94,
            PageCategory.Pricing => 93,
            PageCategory.Features => 92,
            PageCategory.Blog => 88,
            PageCategory.Resources => 86,
            PageCategory.Documentation => 84,
            PageCategory.CaseStudies => 82,
            PageCategory.Testimonials => 80,
            PageCategory.Contact => 78,
            PageCategory.FAQ => 76,
            PageCategory.KnowledgeBase => 75,
            PageCategory.Careers => 60,
            PageCategory.Partners => 55,
            PageCategory.Other => 50,
            PageCategory.Terms => 5,
            PageCategory.Privacy => 5,
            PageCategory.Cookie => 5,
            _ => 0
        };
    }
}
