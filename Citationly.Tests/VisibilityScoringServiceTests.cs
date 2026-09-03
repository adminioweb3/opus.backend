using Citationly.Domain.Entities;
using Citationly.Infrastructure.Services.Visibility;
using Xunit;

namespace Citationly.Tests;

public class VisibilityScoringServiceTests
{
    [Fact]
    public void CalculatePlatformScores_UsesOnlyOrganicVisibilityEligiblePrompts_WhenClassificationExists()
    {
        var organizationId = Guid.NewGuid();
        var prompts = new List<AiSearchPrompt>
        {
            new()
            {
                OrganizationId = organizationId,
                QueryString = "Which companies provide custom software development?",
                PromptClass = "Discovery",
                MetricBucket = "OrganicVisibility",
                IsOrganicVisibilityEligible = true,
                AppearsInAnswer = true,
                MentionProbability = 80,
                BrandStrength = 80,
                ContentStrength = 80,
                CitationStrength = 80,
                VisibilityScore = 80
            },
            new()
            {
                OrganizationId = organizationId,
                QueryString = "What should I consider when choosing a provider?",
                PromptClass = "ProviderEvaluation",
                MetricBucket = "AnswerReadiness",
                IsOrganicVisibilityEligible = false,
                AppearsInAnswer = false,
                MentionProbability = 0,
                BrandStrength = 0,
                ContentStrength = 0,
                CitationStrength = 0
            },
            new()
            {
                OrganizationId = organizationId,
                QueryString = "Citationly vs another provider",
                PromptClass = "BrandedComparison",
                MetricBucket = "BrandedPresence",
                IsBranded = true,
                IsOrganicVisibilityEligible = false,
                AppearsInAnswer = false,
                MentionProbability = 0,
                BrandStrength = 0,
                ContentStrength = 0,
                CitationStrength = 0
            }
        };

        var results = new VisibilityScoringService().CalculatePlatformScores(organizationId, prompts);

        Assert.All(results, result =>
        {
            Assert.Equal(80, result.VisibilityScore);
            Assert.Equal(80, result.MentionRate);
            Assert.Equal(100, result.PromptCoverage);
        });
    }
}
