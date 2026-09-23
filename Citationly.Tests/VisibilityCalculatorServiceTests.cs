using Citationly.Application.Features.PromptIntelligence.Services;
using Citationly.Domain.Entities;
using Xunit;

namespace Citationly.Tests;

public class VisibilityCalculatorServiceTests
{
    [Fact]
    public void Metrics_AreCalculatedFromRecommendationRanksAndCapturedCitations()
    {
        var analysisId = Guid.NewGuid();
        var responses = Enumerable.Range(0, 3)
            .Select(index => new PromptResponse
            {
                Id = Guid.NewGuid(),
                PromptAnalysisId = analysisId,
                Platform = $"Provider {index + 1}",
                ResponseText = index == 2 ? "Rival is recommended." : "Acme and Rival are options.",
            })
            .ToList();

        var mentions = new[]
        {
            Mention(analysisId, responses[0], "Acme", true, 1),
            Mention(analysisId, responses[0], "Rival", false, 2),
            Mention(analysisId, responses[1], "Acme", true, 3),
            Mention(analysisId, responses[2], "Rival", false, 1),
        };
        var citations = new[]
        {
            Citation(analysisId, responses[0], "acme.example", "Owned"),
            Citation(analysisId, responses[0], "review.example", "ReviewPlatform"),
            Citation(analysisId, responses[2], "rival.example", "Competitor"),
        };

        var calculator = new VisibilityCalculatorService();
        var (visibility, _, comparisons) = calculator.CalculateVisibilityMetrics(
            analysisId, responses, "Acme", new[] { "Rival" }, mentions, citations);

        Assert.Equal(67, visibility.MentionFrequency);
        Assert.Equal(2, visibility.AveragePosition);
        Assert.Equal(67, visibility.OverallVisibilityScore);
        Assert.Equal(50, visibility.ShareOfVoice);
        Assert.Equal(1, visibility.VisibilityRank);
        Assert.Equal(1, visibility.CitationCount);
        Assert.Equal(33, visibility.CitationShare);
        Assert.Equal(3, visibility.SampleCount);
        Assert.Equal("prompt-visibility:v5-search-grounded-sampled", visibility.MethodologyVersion);
        Assert.Equal("Preliminary", visibility.MeasurementStatus);

        var rival = Assert.Single(comparisons);
        Assert.Equal(67, rival.VisibilityScore);
        Assert.Equal(50, rival.ShareOfVoice);
    }

    [Fact]
    public void ExtractMentions_DoesNotMatchEntityInsideAnotherWord()
    {
        var analysisId = Guid.NewGuid();
        var response = new PromptResponse
        {
            Id = Guid.NewGuid(),
            PromptAnalysisId = analysisId,
            Platform = "Test",
            ResponseText = "The recommendation is notional, not Notion.",
        };

        var mentions = new VisibilityCalculatorService()
            .ExtractMentions(analysisId, new[] { response }, "Notion", Array.Empty<string>())
            .ToList();

        Assert.Single(mentions);
        Assert.Contains("not Notion", mentions[0].ContextSnippet);
    }

    [Fact]
    public void ExtractMentions_AssignsPositionByTrackedBrandMentionOrder()
    {
        var analysisId = Guid.NewGuid();
        var response = new PromptResponse
        {
            Id = Guid.NewGuid(),
            PromptAnalysisId = analysisId,
            Platform = "Test",
            ResponseText = "Rival is a common choice, while Acme is another suitable provider.",
        };

        var mentions = new VisibilityCalculatorService()
            .ExtractMentions(analysisId, new[] { response }, "Acme", new[] { "Rival" })
            .ToList();

        Assert.Equal(1, Assert.Single(mentions, mention => mention.EntityName == "Rival").Position);
        Assert.Equal(2, Assert.Single(mentions, mention => mention.EntityName == "Acme").Position);
    }

    [Fact]
    public void Metrics_ReturnUnrankedWhenNoTrackedEntityWasObserved()
    {
        var analysisId = Guid.NewGuid();
        var response = new PromptResponse
        {
            Id = Guid.NewGuid(),
            PromptAnalysisId = analysisId,
            Platform = "Test",
            ResponseText = "No tracked company is present in this answer.",
        };

        var calculator = new VisibilityCalculatorService();
        var (visibility, _, _) = calculator.CalculateVisibilityMetrics(
            analysisId, new[] { response }, "Acme", new[] { "Rival" });

        Assert.Equal(0, visibility.VisibilityRank);
        Assert.Equal(0, visibility.OverallVisibilityScore);
    }

    [Theory]
    [InlineData("Aya Data | Managed Data Solutions", "Aya Data is a specialist provider.")]
    [InlineData("Aya Data Pvt. Ltd.", "Teams often evaluate Aya Data for this work.")]
    public void ExtractMentions_MatchesSafeBrandNameAliases(string storedBrandName, string responseText)
    {
        var analysisId = Guid.NewGuid();
        var response = new PromptResponse
        {
            Id = Guid.NewGuid(),
            PromptAnalysisId = analysisId,
            Platform = "Test",
            ResponseText = responseText,
        };

        var mentions = new VisibilityCalculatorService()
            .ExtractMentions(analysisId, new[] { response }, storedBrandName, Array.Empty<string>())
            .ToList();

        var mention = Assert.Single(mentions);
        Assert.True(mention.IsBrand);
        Assert.Equal(storedBrandName, mention.EntityName);
    }

    [Fact]
    public void ExtractMentions_MatchesVerifiedDomainAlias()
    {
        var analysisId = Guid.NewGuid();
        var response = new PromptResponse
        {
            Id = Guid.NewGuid(),
            PromptAnalysisId = analysisId,
            Platform = "Test",
            ResponseText = "For this project, teams can also consider loweb3.com."
        };

        var mentions = new VisibilityCalculatorService()
            .ExtractMentions(
                analysisId,
                new[] { response },
                "Loweb 3 Technologies Private Limited",
                Array.Empty<string>(),
                new[] { "loweb3.com", "loweb3" })
            .ToList();

        Assert.True(Assert.Single(mentions).IsBrand);
    }

    [Fact]
    public void ZeroOfFive_IsMeasuredZeroWithHonestUncertaintyInterval()
    {
        var visibility = new PromptVisibility { SampleCount = 5, MentionFrequency = 0 };

        Assert.Equal("Measured", visibility.MeasurementStatus);
        Assert.Equal(0, visibility.MentionedSampleCount);
        Assert.Equal(0, visibility.ConfidenceLow);
        Assert.InRange(visibility.ConfidenceHigh, 42, 44);
    }

    private static PromptMention Mention(Guid analysisId, PromptResponse response, string name, bool isBrand, int rank) => new()
    {
        PromptAnalysisId = analysisId,
        PromptResponseId = response.Id,
        Platform = response.Platform,
        EntityName = name,
        IsBrand = isBrand,
        IsRecommended = true,
        RecommendationPosition = rank,
        Position = rank,
    };

    private static PromptCitation Citation(Guid analysisId, PromptResponse response, string domain, string category) => new()
    {
        PromptAnalysisId = analysisId,
        PromptResponseId = response.Id,
        Platform = response.Platform,
        Domain = domain,
        Url = $"https://{domain}",
        Category = category,
    };
}
