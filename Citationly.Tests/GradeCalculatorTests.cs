using Xunit;
using Citationly.Application.Helpers;

namespace Citationly.Tests;

public class GradeCalculatorTests
{
    [Theory]
    [InlineData(100, "A+")]
    [InlineData(97,  "A+")]
    [InlineData(96,  "A")]
    [InlineData(93,  "A")]
    [InlineData(92,  "A-")]
    [InlineData(90,  "A-")]
    [InlineData(89,  "B+")]
    [InlineData(87,  "B+")]
    [InlineData(86,  "B")]
    [InlineData(83,  "B")]
    [InlineData(82,  "B-")]
    [InlineData(80,  "B-")]
    [InlineData(79,  "C+")]
    [InlineData(77,  "C+")]
    [InlineData(76,  "C")]
    [InlineData(73,  "C")]
    [InlineData(72,  "C-")]
    [InlineData(70,  "C-")]
    [InlineData(69,  "D+")]
    [InlineData(67,  "D+")]
    [InlineData(66,  "D")]
    [InlineData(63,  "D")]
    [InlineData(62,  "D-")]
    [InlineData(60,  "D-")]
    [InlineData(59,  "F")]
    [InlineData(0,   "F")]
    public void ToGrade_ReturnsCorrectGrade(int score, string expected)
    {
        Assert.Equal(expected, GradeCalculator.ToGrade(score));
    }

    [Fact]
    public void ToGrade_NegativeScore_ReturnsF()
    {
        Assert.Equal("F", GradeCalculator.ToGrade(-10));
    }

    [Fact]
    public void ToGrade_ScoreAbove100_ReturnsAPlus()
    {
        Assert.Equal("A+", GradeCalculator.ToGrade(150));
    }
}
