using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services;

public class RecommendationImpactService : IRecommendationImpactService
{
    private readonly IPromptIntelligenceRepository _repository;

    public RecommendationImpactService(IPromptIntelligenceRepository repository)
    {
        _repository = repository;
    }

    public Task<RecommendationImplementation?> MarkImplementedAsync(
        Guid organizationId,
        Guid recommendationId,
        int monitoringWindowDays = 14,
        CancellationToken ct = default)
    {
        return _repository.MarkRecommendationImplementedAsync(organizationId, recommendationId, monitoringWindowDays);
    }

    public async Task<int> ProcessDueMeasurementsAsync(Guid? organizationId = null, CancellationToken ct = default)
    {
        var due = (await _repository.GetDueRecommendationImplementationsAsync(DateTime.UtcNow, limit: 200, organizationId)).ToList();
        var measured = 0;

        foreach (var implementation in due)
        {
            ct.ThrowIfCancellationRequested();

            var followup = await _repository.GetLatestFollowupVisibilityAsync(
                implementation.PromptQuestionId,
                implementation.MeasurementDueAt);

            if (followup == null)
            {
                continue;
            }

            var deltaVisibility = followup.OverallVisibilityScore - implementation.BaselineVisibilityScore;
            var deltaShareOfVoice = followup.ShareOfVoice - implementation.BaselineShareOfVoice;
            var deltaAveragePosition = implementation.BaselineAveragePosition - followup.AveragePosition;
            var deltaCitationCount = followup.CitationCount - implementation.BaselineCitationCount;
            var impactStatus = ScoreImpact(deltaVisibility, deltaShareOfVoice, deltaAveragePosition, deltaCitationCount);

            var evidenceJson = JsonSerializer.Serialize(new
            {
                implementation.PromptRecommendationId,
                implementation.PromptQuestionId,
                baseline = new
                {
                    implementation.PromptAnalysisId,
                    implementation.BaselineVisibilityScore,
                    implementation.BaselineShareOfVoice,
                    implementation.BaselineAveragePosition,
                    implementation.BaselineCitationCount,
                    implementation.MarkedImplementedAt
                },
                followup = new
                {
                    followup.PromptAnalysisId,
                    followup.OverallVisibilityScore,
                    followup.ShareOfVoice,
                    followup.AveragePosition,
                    followup.CitationCount
                },
                delta = new
                {
                    visibility = deltaVisibility,
                    shareOfVoice = deltaShareOfVoice,
                    averagePosition = deltaAveragePosition,
                    citations = deltaCitationCount
                }
            });

            await _repository.CompleteRecommendationImpactAsync(
                implementation.Id,
                followup.PromptAnalysisId,
                deltaVisibility,
                deltaShareOfVoice,
                deltaAveragePosition,
                deltaCitationCount,
                impactStatus,
                evidenceJson);

            measured++;
        }

        return measured;
    }

    private static string ScoreImpact(int visibilityDelta, int shareDelta, int positionDelta, int citationDelta)
    {
        var score = visibilityDelta + shareDelta + positionDelta + (citationDelta * 4);
        if (score >= 5) return "Improved";
        if (score <= -5) return "Regressed";
        return "Neutral";
    }
}
