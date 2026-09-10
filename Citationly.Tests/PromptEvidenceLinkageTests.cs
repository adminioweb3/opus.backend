using Citationly.Application.Features.PromptIntelligence.Services;
using Citationly.Domain.Entities;
using Xunit;

namespace Citationly.Tests;

public class PromptEvidenceLinkageTests
{
    [Fact]
    public void MentionAndCitationEvidence_RemainsLinkedToItsExactSample()
    {
        var analysisId = Guid.NewGuid();
        var firstResponseId = Guid.NewGuid();
        var secondResponseId = Guid.NewGuid();
        var responses = new[]
        {
            new PromptResponse
            {
                Id = firstResponseId,
                PromptAnalysisId = analysisId,
                Platform = "ChatGPT",
                ResponseText = "Acme is a strong option. See https://acme.example/pricing"
            },
            new PromptResponse
            {
                Id = secondResponseId,
                PromptAnalysisId = analysisId,
                Platform = "ChatGPT",
                ResponseText = "Rival is another option. See https://rival.example/features"
            }
        };

        var calculator = new VisibilityCalculatorService();
        var (_, mentions, _) = calculator.CalculateVisibilityMetrics(
            analysisId,
            responses,
            "Acme",
            new[] { "Rival" });

        Assert.Contains(mentions, mention => mention.EntityName == "Acme" && mention.PromptResponseId == firstResponseId);
        Assert.Contains(mentions, mention => mention.EntityName == "Rival" && mention.PromptResponseId == secondResponseId);

        var extractor = new CitationExtractorService();
        var firstCitations = extractor.ExtractCitations(
            analysisId,
            firstResponseId,
            "ChatGPT",
            responses[0].ResponseText,
            "acme.example",
            new[] { "rival.example" });

        Assert.All(firstCitations, citation => Assert.Equal(firstResponseId, citation.PromptResponseId));
    }

    [Fact]
    public void ProviderCitationMetadata_IsCapturedWhenAnswerUsesNumericMarkersOnly()
    {
        var analysisId = Guid.NewGuid();
        var responseId = Guid.NewGuid();
        var citations = new CitationExtractorService().ExtractCitations(
            analysisId,
            responseId,
            "Perplexity",
            "Acme is one option.[1]",
            "acme.example",
            Array.Empty<string>(),
            new[] { "https://acme.example/review", "https://independent.example/report" })
            .ToList();

        Assert.Equal(2, citations.Count);
        Assert.Contains(citations, citation => citation.Category == "Owned" && citation.Domain == "acme.example");
        Assert.All(citations, citation => Assert.Equal(responseId, citation.PromptResponseId));
    }
}
