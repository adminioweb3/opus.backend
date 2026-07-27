using Citationly.Application.Interfaces;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface IPromptIntelligenceFirstRunService
{
    Task RunFirstBatchAsync(Guid organizationId);
}

/// <summary>
/// Fired once, fire-and-forget, at the end of onboarding (CompleteOnboardingCommand) so Answer
/// Atlas already has real data the first time a user opens it, instead of starting empty until
/// someone manually clicks Analyze. Seeds PromptTopic/PromptQuestion from the org's generated
/// AiSearchPrompt rows, then runs real analysis (real per-engine LLM calls) on a small, bounded
/// batch — not every prompt, to keep onboarding-time cost/latency reasonable.
///
/// Not idempotent across retries (each run creates new PromptAnalysis rows for whatever it
/// picks), so automatic retry is disabled at the call site — a failed individual question is
/// caught and skipped rather than failing the whole batch.
/// </summary>
public class PromptIntelligenceFirstRunService : IPromptIntelligenceFirstRunService
{
    private const int MaxQuestionsPerTopic = 2;
    private const int MaxTotalQuestions = 10;

    private readonly IPromptTopicSeedingService _seeding;
    private readonly IPromptIntelligenceRepository _repo;
    private readonly IPromptExecutionService _executionService;
    private readonly ILogger<PromptIntelligenceFirstRunService> _logger;

    public PromptIntelligenceFirstRunService(
        IPromptTopicSeedingService seeding,
        IPromptIntelligenceRepository repo,
        IPromptExecutionService executionService,
        ILogger<PromptIntelligenceFirstRunService> logger)
    {
        _seeding = seeding;
        _repo = repo;
        _executionService = executionService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task RunFirstBatchAsync(Guid organizationId)
    {
        await _seeding.EnsureSeededAsync(organizationId);

        var topics = await _repo.GetTopicsAsync(organizationId);
        var toAnalyze = new List<Guid>();

        foreach (var topic in topics)
        {
            var questions = (await _repo.GetQuestionsByTopicAsync(topic.Id))
                .Where(q => q.IsActive)
                .Take(MaxQuestionsPerTopic);

            foreach (var question in questions)
            {
                toAnalyze.Add(question.Id);
                if (toAnalyze.Count >= MaxTotalQuestions) break;
            }
            if (toAnalyze.Count >= MaxTotalQuestions) break;
        }

        foreach (var questionId in toAnalyze)
        {
            try
            {
                await foreach (var _ in _executionService.ExecutePromptAnalysisAsync(organizationId, questionId, CancellationToken.None))
                {
                    // Draining progress events silently — this runs in the background with no
                    // client listening.
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "First-run analysis failed for org {OrganizationId}, question {QuestionId}", organizationId, questionId);
            }
        }
    }
}
