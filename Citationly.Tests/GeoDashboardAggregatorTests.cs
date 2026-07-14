using Xunit;
using Citationly.Application.Dtos;
using Citationly.Application.Features.GeoDashboard;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.GeoDashboard;
using Citationly.Domain.Entities;

namespace Citationly.Tests;

// ── Stub implementations for testing the aggregator logic ───────

internal class StubVisibilityRepository : IAiVisibilityRepository
{
    public List<HistoricalScan> Scans { get; set; } = new();
    public List<ShareOfVoice> ShareOfVoice { get; set; } = new();
    public List<Competitor> Competitors { get; set; } = new();

    public Task<List<HistoricalScan>> GetHistoricalScansByOrgAsync(Guid organizationId) => Task.FromResult(Scans);
    public Task<List<ShareOfVoice>> GetShareOfVoiceByOrgAsync(Guid organizationId) => Task.FromResult(ShareOfVoice);
    public Task<List<Competitor>> GetCompetitorsByOrgAsync(Guid organizationId) => Task.FromResult(Competitors);
    
    // Unused in aggregator tests
    public Task<Guid> InsertCompetitorAsync(Competitor competitor) => Task.FromResult(Guid.NewGuid());
    public Task DeleteCompetitorsByOrgAsync(Guid organizationId) => Task.CompletedTask;
    public Task<Guid> InsertHistoricalScanAsync(HistoricalScan scan) => Task.FromResult(Guid.NewGuid());
    public Task<Guid> InsertShareOfVoiceAsync(ShareOfVoice share) => Task.FromResult(Guid.NewGuid());
    public Task DeleteShareOfVoiceByScanDateAsync(Guid organizationId, DateOnly scanDate) => Task.CompletedTask;
    
    public Task InsertGeoPillarAsync(GeoPillar pillar) => Task.CompletedTask;
    public Task<List<GeoPillar>> GetGeoPillarsByOrgAsync(Guid orgId, DateOnly? minDate = null) => Task.FromResult(new List<GeoPillar>());
    
    public Task InsertPromptCoverageAsync(PromptCoverage coverage) => Task.CompletedTask;
    public Task<List<PromptCoverage>> GetPromptCoverageByOrgAsync(Guid orgId, DateOnly? minDate = null) => Task.FromResult(new List<PromptCoverage>());
    
    public Task InsertWinLossEventAsync(WinLossEvent wle) => Task.CompletedTask;
    public Task<List<WinLossEvent>> GetWinLossEventsByOrgAsync(Guid orgId, int limit = 10) => Task.FromResult(new List<WinLossEvent>());
    
    public Task EnsureGeoTablesCreatedAsync() => Task.CompletedTask;
}

internal class StubPillarService : IGeoPillarService
{
    public List<GeoPillarDto> Pillars { get; set; } = new();
    public Task<List<GeoPillarDto>> GetPillarsAsync(Guid organizationId, string range) => Task.FromResult(Pillars);
}

internal class StubPromptCoverageService : IPromptCoverageService
{
    public List<PromptTypeCoverageDto> Coverage { get; set; } = new();
    public Task<List<PromptTypeCoverageDto>> GetCoverageAsync(Guid organizationId, string range) => Task.FromResult(Coverage);
}

internal class StubActivityFeedService : IActivityFeedService
{
    public Task<List<WinLossEventDto>> GetRecentEventsAsync(Guid organizationId, int count = 5) =>
        Task.FromResult(new List<WinLossEventDto>
        {
            new("win", "Test win", "ChatGPT", DateTime.UtcNow.ToString("o"))
        });
}

internal class StubEngineScanService : IEngineScanService
{
    public Task<(int EnginesScanned, int PromptsTracked)> GetScanStatsAsync(Guid organizationId) =>
        Task.FromResult((7, 25));
}

// ── Tests ───────────────────────────────────────────────────────

public class GeoDashboardAggregatorTests
{
    private readonly Guid _orgId = Guid.NewGuid();

    private GeoDashboardAggregator CreateAggregator(
        StubPillarService? pillarService = null,
        StubPromptCoverageService? coverageService = null)
    {
        return new GeoDashboardAggregator(
            new StubVisibilityRepository(),
            pillarService ?? new StubPillarService
            {
                Pillars = new List<GeoPillarDto>
                {
                    new("answerReadiness", "Answer readiness", "desc", 72),
                    new("authoritySignals", "Authority signals", "desc", 58),
                    new("freshness", "Freshness", "desc", 83)
                }
            },
            coverageService ?? new StubPromptCoverageService
            {
                Coverage = new List<PromptTypeCoverageDto>
                {
                    new("Informational", "ex", "note", 81, "up"),
                    new("Transactional", "ex", "note", 43, "flat"),
                    new("Local", "ex", "note", 70, "up")
                }
            },
            new StubActivityFeedService(),
            new StubEngineScanService());
    }

    [Fact]
    public async Task WeakestPillarInsight_IdentifiesLowestScorePillar()
    {
        var aggregator = CreateAggregator();
        var result = await aggregator.BuildAsync(_orgId, "30D");

        Assert.Equal("authoritySignals", result.WeakestPillarInsight.PillarKey);
        Assert.Equal(58, result.WeakestPillarInsight.Score);
        Assert.Equal("Run Page auditor", result.WeakestPillarInsight.CtaLabel);
        Assert.Equal("/geo-engine/page-auditor", result.WeakestPillarInsight.CtaLink);
    }

    [Fact]
    public async Task OpportunityInsight_IdentifiesLowestPercentagePromptType()
    {
        var aggregator = CreateAggregator();
        var result = await aggregator.BuildAsync(_orgId, "30D");

        Assert.Contains("Transactional", result.OpportunityInsight.Message);
        Assert.Contains("43%", result.OpportunityInsight.Message);
        Assert.Equal("Open Opportunity Finder", result.OpportunityInsight.CtaLabel);
        Assert.Equal("/opportunity-finder", result.OpportunityInsight.CtaLink);
    }

    [Fact]
    public async Task WeakestPillarInsight_WhenAllTied_PicksFirst()
    {
        var pillars = new StubPillarService
        {
            Pillars = new List<GeoPillarDto>
            {
                new("a", "A", "desc", 50),
                new("b", "B", "desc", 50),
                new("c", "C", "desc", 50)
            }
        };

        var aggregator = CreateAggregator(pillarService: pillars);
        var result = await aggregator.BuildAsync(_orgId, "30D");

        // MinBy returns the first element when tied
        Assert.Equal("a", result.WeakestPillarInsight.PillarKey);
        Assert.Equal(50, result.WeakestPillarInsight.Score);
    }

    [Fact]
    public async Task OpportunityInsight_WhenAllTied_PicksFirst()
    {
        var coverage = new StubPromptCoverageService
        {
            Coverage = new List<PromptTypeCoverageDto>
            {
                new("TypeA", "ex", "note", 30, "up"),
                new("TypeB", "ex", "note", 30, "flat"),
                new("TypeC", "ex", "note", 30, "down")
            }
        };

        var aggregator = CreateAggregator(coverageService: coverage);
        var result = await aggregator.BuildAsync(_orgId, "30D");

        Assert.Contains("TypeA", result.OpportunityInsight.Message);
        Assert.Contains("30%", result.OpportunityInsight.Message);
    }

    [Fact]
    public async Task Header_CompositeScore_IsAverageOfScorecard()
    {
        var aggregator = CreateAggregator();
        var result = await aggregator.BuildAsync(_orgId, "30D");

        // Mock scores: 64, 89, 52, 68, 8, 94, 76, 81 → average = 66.5 → rounded to 66
        Assert.Equal(66, result.Header.CompositeScore);
        Assert.Equal("D", result.Header.Grade);
    }

    [Fact]
    public async Task VerifyInsight_IsStatic()
    {
        var aggregator = CreateAggregator();
        var result = await aggregator.BuildAsync(_orgId, "30D");

        Assert.Equal("Verify any of these answers live before acting on them.", result.VerifyInsight.Message);
        Assert.Equal("Test in Answer simulator", result.VerifyInsight.CtaLabel);
        Assert.Equal("/geo-engine/answer-simulator", result.VerifyInsight.CtaLink);
    }

    [Fact]
    public async Task BackwardCompat_ScoresTrendShareOfVoice_ArePresent()
    {
        var aggregator = CreateAggregator();
        var result = await aggregator.BuildAsync(_orgId, "30D");

        Assert.NotNull(result.Scores);
        Assert.NotNull(result.Trend);
        Assert.NotNull(result.ShareOfVoice);
    }
}
