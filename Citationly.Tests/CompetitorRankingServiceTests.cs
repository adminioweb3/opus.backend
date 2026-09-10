using Citationly.Application.Interfaces;
using Citationly.Application.Features.Competitors;
using Citationly.Domain.Entities;
using Citationly.Infrastructure.Services.Competitors;
using Xunit;

namespace Citationly.Tests;

public class CompetitorRankingServiceTests
{
    [Fact]
    public async Task ComputeRankings_ProjectsOnlyMeasuredSnapshots()
    {
        var organizationId = Guid.NewGuid();
        var scanDate = new DateOnly(2026, 9, 9);
        var repository = new SnapshotRepositoryStub(scanDate, new List<CompetitorSnapshot>
        {
            Snapshot(organizationId, scanDate, "Rival", false, 70, 1, 65, 8, 5, 10),
            Snapshot(organizationId, scanDate, "Acme", true, 50, 2, 35, 6, 3, 10),
            new()
            {
                OrganizationId = organizationId,
                ScanDate = scanDate,
                Name = "Legacy",
                MeasurementSource = "legacy-estimated",
                Visibility = 99,
                Rank = 1
            }
        });
        var service = new CompetitorRankingService(repository);

        var result = await service.ComputeRankingsAsync(organizationId, CancellationToken.None);

        Assert.Equal(2, result.TotalCompanies);
        Assert.Equal(2, result.OverallRank);
        Assert.Equal(50, result.OverallScore);
        Assert.Equal("Rival", result.TopCompetitor);
        Assert.DoesNotContain(result.Leaderboard, entry => entry.CompanyName == "Legacy");
        Assert.Contains(result.CategoryRankings, category => category.Category == "Recommendation Rate");
    }

    [Fact]
    public async Task ComputeRankings_DoesNotAwardFirstPlaceForAllZeroEvidence()
    {
        var organizationId = Guid.NewGuid();
        var scanDate = new DateOnly(2026, 9, 10);
        var repository = new SnapshotRepositoryStub(scanDate, new List<CompetitorSnapshot>
        {
            Snapshot(organizationId, scanDate, "Acme", true, 0, 0, 0, 0, 0, 8),
            Snapshot(organizationId, scanDate, "Rival", false, 0, 0, 0, 0, 0, 8),
        });

        var result = await new CompetitorRankingService(repository)
            .ComputeRankingsAsync(organizationId, CancellationToken.None);

        Assert.Equal(0, result.OverallRank);
        Assert.Equal(0, result.Percentile);
        Assert.All(result.CategoryRankings, category => Assert.Equal(0, category.UserRank));
    }

    private static CompetitorSnapshot Snapshot(
        Guid organizationId,
        DateOnly scanDate,
        string name,
        bool isYou,
        int visibility,
        int rank,
        int shareOfVoice,
        int mentions,
        int recommendations,
        int responses) => new()
        {
            OrganizationId = organizationId,
            ScanDate = scanDate,
            Name = name,
            IsYou = isYou,
            Visibility = visibility,
            Score = visibility,
            Rank = rank,
            ShareOfVoice = shareOfVoice,
            MentionCount = mentions,
            RecommendationCount = recommendations,
            ResponseCount = responses,
            MeasurementSource = "openai-observed",
            MethodologyVersion = CompetitorEvidenceScorer.MethodologyVersion
        };

    private sealed class SnapshotRepositoryStub : ICompetitorSnapshotRepository
    {
        private readonly DateOnly _scanDate;
        private readonly List<CompetitorSnapshot> _snapshots;

        public SnapshotRepositoryStub(DateOnly scanDate, List<CompetitorSnapshot> snapshots)
        {
            _scanDate = scanDate;
            _snapshots = snapshots;
        }

        public Task EnsureTableCreatedAsync() => Task.CompletedTask;
        public Task<Guid> InsertSnapshotAsync(CompetitorSnapshot snapshot) => Task.FromResult(Guid.NewGuid());
        public Task DeleteByScanDateAsync(Guid organizationId, DateOnly scanDate) => Task.CompletedTask;
        public Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId) => Task.FromResult<DateOnly?>(_scanDate);
        public Task<List<CompetitorSnapshot>> GetSnapshotsByScanDateAsync(Guid organizationId, DateOnly scanDate) => Task.FromResult(_snapshots);
        public Task<List<CompetitorSnapshot>> GetRecentHistoryAsync(Guid organizationId, int maxScanDates = 12) => Task.FromResult(_snapshots);
    }
}
