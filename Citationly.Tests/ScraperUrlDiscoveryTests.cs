using Citationly.Infrastructure.Services.Scraping;
using HtmlAgilityPack;
using Xunit;

namespace Citationly.Tests;

public class ScraperUrlDiscoveryTests
{
    [Fact]
    public void ExtractLinks_ResolvesRelativeLinks_AndTreatsWwwAliasAsInternal()
    {
        var document = new HtmlDocument();
        document.LoadHtml("""
            <html><body>
              <a href="/about">About</a>
              <a href="pricing">Pricing</a>
              <a href="https://example.com/contact?utm_source=test#team">Contact</a>
              <a href="https://other.example/news">News</a>
              <a href="mailto:hello@example.com">Email</a>
            </body></html>
            """);

        var (internalLinks, externalLinks) = PlaywrightScraperEngine.ExtractLinks(
            document,
            "https://www.example.com/products");

        Assert.Contains("https://www.example.com/about", internalLinks);
        Assert.Contains("https://www.example.com/pricing", internalLinks);
        Assert.Contains("https://example.com/contact", internalLinks);
        Assert.Contains("https://other.example/news", externalLinks);
        Assert.Equal("example.com", PlaywrightScraperEngine.NormalizeHost("www.example.com"));
    }

    [Fact]
    public void ExtractSitemapUrls_KeepsSameSitePagesAndNestedSitemaps_Only()
    {
        const string sitemap = """
            <sitemapindex>
              <sitemap><loc>https://www.example.com/pages.xml</loc></sitemap>
              <url><loc>https://example.com/about?utm_source=sitemap</loc></url>
              <url><loc>https://example.com/assets/logo.png</loc></url>
              <url><loc>https://other.example/pricing</loc></url>
            </sitemapindex>
            """;

        var urls = PlaywrightScraperEngine.ExtractSitemapUrls(sitemap, "example.com");

        Assert.Contains("https://www.example.com/pages.xml", urls);
        Assert.Contains("https://example.com/about", urls);
        Assert.DoesNotContain(urls, value => value.Contains("logo.png", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(urls, value => value.Contains("other.example", StringComparison.OrdinalIgnoreCase));
    }
}
