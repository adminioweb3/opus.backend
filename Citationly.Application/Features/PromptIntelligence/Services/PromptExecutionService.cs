using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface IPromptExecutionService
{
    IAsyncEnumerable<string> ExecutePromptAnalysisAsync(Guid organizationId, Guid questionId, CancellationToken ct);
}

public class PromptExecutionService : IPromptExecutionService
{
    private readonly IPromptIntelligenceRepository _repo;
    private readonly IWebsiteRepository _websiteRepo;
    private readonly ILLMRunnerService _llmRunner;
    private readonly IVisibilityCalculatorService _calculator;
    private readonly IRecommendationEngineService _recommendationEngine;

    public PromptExecutionService(
        IPromptIntelligenceRepository repo,
        IWebsiteRepository websiteRepo,
        ILLMRunnerService llmRunner,
        IVisibilityCalculatorService calculator,
        IRecommendationEngineService recommendationEngine)
    {
        _repo = repo;
        _websiteRepo = websiteRepo;
        _llmRunner = llmRunner;
        _calculator = calculator;
        _recommendationEngine = recommendationEngine;
    }

    public async IAsyncEnumerable<string> ExecutePromptAnalysisAsync(Guid organizationId, Guid questionId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        yield return "{\"step\": \"Initializing\", \"progress\": 5}";

        var question = await _repo.GetQuestionAsync(questionId);
        if (question == null)
        {
            yield return "{\"error\": \"Question not found\"}";
            yield break;
        }

        var profile = await _websiteRepo.GetLatestWebsiteProfileAsync(organizationId);
        string brandName = profile?.BusinessName ?? "Your Brand";

        // Create Analysis Record
        var analysis = new PromptAnalysis
        {
            PromptQuestionId = questionId,
            Status = "Running"
        };
        var analysisId = await _repo.CreateAnalysisAsync(analysis);

        yield return "{\"step\": \"Running against AI Models...\", \"progress\": 20}";

        // Run LLMs
        var responses = await _llmRunner.RunPromptAcrossModelsAsync(analysisId, question.PromptText, ct);
        await _repo.InsertResponsesAsync(responses);

        yield return "{\"step\": \"Extracting Mentions & Citations...\", \"progress\": 50}";

        // Calculate Visibility
        // Simulated competitors
        var competitors = new List<string> { "Competitor A", "Competitor B", "Alternative C" }; 
        var (visibility, mentions, compComparisons) = _calculator.CalculateVisibilityMetrics(analysisId, responses, brandName, competitors);

        await _repo.InsertMentionsAsync(mentions);
        await _repo.InsertVisibilityAsync(visibility);
        await _repo.InsertCompetitorComparisonsAsync(compComparisons);

        yield return "{\"step\": \"Generating Recommendations...\", \"progress\": 80}";

        // Recommendations
        var recommendations = await _recommendationEngine.GenerateRecommendationsAsync(analysisId, visibility, compComparisons, ct);
        await _repo.InsertRecommendationsAsync(recommendations);

        // Update status
        await _repo.UpdateAnalysisStatusAsync(analysisId, "Completed");

        yield return "{\"step\": \"Preparing Report...\", \"progress\": 100, \"analysisId\": \"" + analysisId.ToString() + "\"}";
    }
}
