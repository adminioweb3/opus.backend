using Citationly.Application.Features.Competitors;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.GeoDashboard;

public sealed record GeoScoreEvidenceSnapshot(
    int AnalysisCount,
    int OwnedCitationAnalysisCount,
    int ClassifiedResponseCount,
    int CompetitorResponseCount,
    int RankedBrandCount);

public interface IGeoScoreEvidenceService
{
    Task<GeoScoreEvidenceSnapshot> GetAsync(Guid organizationId, DateTime since);
}

/// <summary>
/// Reconstructs the evidence denominators behind the persisted GEO scorecard. This service never
/// estimates missing values: a count of zero means no matching stored evidence was found.
/// </summary>
public sealed class GeoScoreEvidenceService : IGeoScoreEvidenceService
{
    private readonly IPromptIntelligenceRepository _promptRepository;
    private readonly ICompetitorSnapshotRepository _snapshotRepository;

    public GeoScoreEvidenceService(
        IPromptIntelligenceRepository promptRepository,
        ICompetitorSnapshotRepository snapshotRepository)
    {
        _promptRepository = promptRepository;
        _snapshotRepository = snapshotRepository;
    }

    public async Task<GeoScoreEvidenceSnapshot> GetAsync(Guid organizationId, DateTime since)
    {
        var visibilityTask = _promptRepository.GetVisibilitySummaryDataAsync(organizationId, since);
        var citationTask = _promptRepository.GetCitationSummaryDataAsync(organizationId, since);
        var sentimentTask = _promptRepository.GetSentimentSummaryDataAsync(organizationId, since);
        var latestSnapshotDateTask = _snapshotRepository.GetLatestScanDateAsync(organizationId);

        await Task.WhenAll(visibilityTask, citationTask, sentimentTask, latestSnapshotDateTask);

        var visibility = (await visibilityTask).ToList();
        var citations = (await citationTask).ToList();
        var sentiments = (await sentimentTask)
            .Where(row => !string.IsNullOrWhiteSpace(row.Sentiment))
            .ToList();

        var snapshots = latestSnapshotDateTask.Result.HasValue
            ? await _snapshotRepository.GetSnapshotsByScanDateAsync(organizationId, latestSnapshotDateTask.Result.Value)
            : new List<Citationly.Domain.Entities.CompetitorSnapshot>();
        var measuredSnapshots = snapshots
            .Where(snapshot => snapshot.MeasurementSource == "openai-observed" &&
                               snapshot.MethodologyVersion == CompetitorEvidenceScorer.MethodologyVersion)
            .ToList();

        return new GeoScoreEvidenceSnapshot(
            AnalysisCount: visibility.Select(row => row.QuestionId).Count(),
            OwnedCitationAnalysisCount: citations
                .Where(row => row.Category == "Owned")
                .Select(row => row.AnalysisId)
                .Distinct()
                .Count(),
            ClassifiedResponseCount: sentiments.Count,
            CompetitorResponseCount: measuredSnapshots.Count == 0 ? 0 : measuredSnapshots.Max(row => row.ResponseCount),
            RankedBrandCount: measuredSnapshots.Count(row => row.Rank > 0));
    }
}
