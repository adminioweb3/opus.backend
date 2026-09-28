using Citationly.Application.Features.Assistant.Agents;
using Citationly.Domain.Entities;
using Xunit;

namespace Citationly.Tests;

public sealed class ContentExecutionGroundingTests
{
    [Fact]
    public void SelectSources_RanksRecommendationRelevantKnowledgeFirst()
    {
        var recommendation = new AgentRecommendation
        {
            Title = "Explain enterprise security controls",
            Summary = "Create a page that documents SSO and audit logging.",
            TargetKey = "enterprise security",
            ActionPlanJson = "[]"
        };
        var relevant = new ScrapedPage
        {
            Id = Guid.NewGuid(),
            Url = "https://example.test/security",
            Title = "Enterprise security",
            MarkdownContent = "Enterprise security includes SSO, audit logging, and access controls."
        };
        var unrelated = new ScrapedPage
        {
            Id = Guid.NewGuid(),
            Url = "https://example.test/about",
            Title = "About us",
            MarkdownContent = "Our team was founded to build useful software."
        };

        var result = ContentExecutionGrounding.SelectSources([unrelated, relevant], recommendation, 2);

        Assert.Equal(relevant.Id, result[0].PageId);
        Assert.True(result[0].RelevanceScore > result[1].RelevanceScore);
    }

    [Fact]
    public void EvaluatePolicy_BlocksDraftWithoutCitations()
    {
        var source = new ContentGroundingSource(
            Guid.NewGuid(), "Security", "https://example.test/security", "SSO is supported.", 5);

        var checks = ContentExecutionGrounding.EvaluatePolicy(
            "Enterprise security", "# Enterprise security\n\nSSO is supported.", [source]);

        Assert.True(ContentExecutionGrounding.HasBlockingFailure(checks));
        Assert.Contains(checks, check => check.Key == "source_citations" && check.Status == "Failed" && check.Blocking);
    }

    [Fact]
    public void EvaluatePolicy_AcceptsOnlyValidSuppliedSourceReferences()
    {
        var sources = new[]
        {
            new ContentGroundingSource(Guid.NewGuid(), "Security", "https://example.test/security", "SSO is supported.", 5),
            new ContentGroundingSource(Guid.NewGuid(), "Audit logs", "https://example.test/audit", "Logs are retained.", 4)
        };

        var valid = ContentExecutionGrounding.EvaluatePolicy(
            "Enterprise security", "# Enterprise security\n\nSSO is supported. [Source 1]", sources);
        var invalid = ContentExecutionGrounding.EvaluatePolicy(
            "Enterprise security", "# Enterprise security\n\nSSO is supported. [Source 3]", sources);

        Assert.False(ContentExecutionGrounding.HasBlockingFailure(valid));
        Assert.True(ContentExecutionGrounding.HasBlockingFailure(invalid));
    }
}
