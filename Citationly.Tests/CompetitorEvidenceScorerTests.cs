using Citationly.Application.Features.Competitors;
using Xunit;

namespace Citationly.Tests;

public class CompetitorEvidenceScorerTests
{
    [Fact]
    public void Score_UsesObservedMentionsPositionsAndCitations()
    {
        var inputs = new List<CompetitorEvidenceInput>
        {
            new(null, true, "Your Brand", "you.example", new[] { 10, 30 }, new[] { 1 }, 2),
            new(Guid.NewGuid(), false, "Rival", "rival.example", new[] { 5, 20, 50 }, new[] { 1, 2 }, 1)
        };

        var result = CompetitorEvidenceScorer.Score(inputs, responseCount: 4);

        var user = Assert.Single(result, score => score.IsYou);
        var rival = Assert.Single(result, score => !score.IsYou);

        Assert.Equal(2, user.MentionCount);
        Assert.Equal(4, user.ResponseCount);
        Assert.Equal(20, user.AveragePosition);
        Assert.Equal(40, user.ShareOfVoice);
        Assert.Equal(48, user.Score);
        Assert.Equal(1, user.RecommendationCount);

        Assert.Equal(3, rival.MentionCount);
        Assert.Equal(60, rival.ShareOfVoice);
        Assert.Equal(65, rival.Score);
        Assert.Equal(2, rival.RecommendationCount);
        Assert.Equal("Rival", result[0].Name);
    }

    [Fact]
    public void Score_ReturnsZeroInsteadOfInventingValuesWhenEvidenceIsMissing()
    {
        var inputs = new List<CompetitorEvidenceInput>
        {
            new(null, true, "Your Brand", null, Array.Empty<int>(), Array.Empty<int>(), 0),
            new(Guid.NewGuid(), false, "Rival", null, Array.Empty<int>(), Array.Empty<int>(), 0)
        };

        var result = CompetitorEvidenceScorer.Score(inputs, responseCount: 0);

        Assert.All(result, score =>
        {
            Assert.Equal(0, score.Score);
            Assert.Equal(0, score.ShareOfVoice);
            Assert.Equal(0, score.MentionCount);
            Assert.Equal(0, score.ResponseCount);
            Assert.Equal(100, score.AveragePosition);
        });
    }

    [Fact]
    public void Score_CorrectsRoundedShareOfVoiceToOneHundredPercent()
    {
        var inputs = Enumerable.Range(1, 3)
            .Select(index => new CompetitorEvidenceInput(
                Guid.NewGuid(),
                false,
                $"Competitor {index}",
                null,
                new[] { index * 10 },
                Array.Empty<int>(),
                0))
            .ToList();

        var result = CompetitorEvidenceScorer.Score(inputs, responseCount: 3);

        Assert.Equal(100, result.Sum(score => score.ShareOfVoice));
    }
}
