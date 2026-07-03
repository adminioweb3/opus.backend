using Citationly.Application.Interfaces.Onboarding;
using Citationly.Domain.Entities;
using Citationly.Domain.Enums;

namespace Citationly.Infrastructure.Services.Onboarding;

public class PageClassificationService : IPageClassificationService
{
    public PageCategory ClassifyPage(ScrapedPage page)
    {
        var url = page.Url?.ToLowerInvariant() ?? string.Empty;
        var title = page.Title?.ToLowerInvariant() ?? string.Empty;
        var headings = page.Headings?.ToLowerInvariant() ?? string.Empty;

        // Normalize URL to just the path
        var path = "/";
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                path = uri.AbsolutePath.ToLowerInvariant().TrimEnd('/');
                if (string.IsNullOrEmpty(path)) path = "/";
            }
        }
        catch { }

        // Homepage detection
        if (path == "/" || path == "/home" || path == "/index" || path == "/default") return PageCategory.Homepage;

        // Exact path matching
        if (path.Contains("/about") || title.Contains("about us") || title.Contains("who we are")) return PageCategory.About;
        if (path.Contains("/pricing") || path.Contains("/plans") || title.Contains("pricing")) return PageCategory.Pricing;
        if (path.Contains("/contact") || title.Contains("contact us")) return PageCategory.Contact;
        if (path.Contains("/faq") || title.Contains("frequently asked questions")) return PageCategory.FAQ;
        if (path.Contains("/terms") || title.Contains("terms of service") || title.Contains("terms & conditions")) return PageCategory.Terms;
        if (path.Contains("/privacy") || title.Contains("privacy policy")) return PageCategory.Privacy;
        if (path.Contains("/cookie") || title.Contains("cookie policy")) return PageCategory.Cookie;
        if (path.Contains("/blog") || path.Contains("/news") || path.Contains("/article")) return PageCategory.Blog;
        if (path.Contains("/careers") || path.Contains("/jobs") || title.Contains("careers")) return PageCategory.Careers;
        if (path.Contains("/partners") || title.Contains("partners")) return PageCategory.Partners;
        if (path.Contains("/case-study") || path.Contains("/case-studies") || path.Contains("/customers") || title.Contains("case studies")) return PageCategory.CaseStudies;
        if (path.Contains("/testimonial") || title.Contains("testimonials") || title.Contains("what our customers say")) return PageCategory.Testimonials;
        if (path.Contains("/docs") || path.Contains("/documentation")) return PageCategory.Documentation;
        if (path.Contains("/help") || path.Contains("/support")) return PageCategory.Support;
        if (path.Contains("/knowledge-base") || path.Contains("/kb")) return PageCategory.KnowledgeBase;
        
        // Priority checks
        if (path.Contains("/services") || title.Contains("services")) return PageCategory.Services;
        if (path.Contains("/products") || title.Contains("products")) return PageCategory.Products;
        if (path.Contains("/solutions") || title.Contains("solutions")) return PageCategory.Solutions;
        if (path.Contains("/industries") || path.Contains("/sectors")) return PageCategory.Industries;
        if (path.Contains("/features") || title.Contains("features")) return PageCategory.Features;
        if (path.Contains("/resources")) return PageCategory.Resources;

        return PageCategory.Other;
    }
}
