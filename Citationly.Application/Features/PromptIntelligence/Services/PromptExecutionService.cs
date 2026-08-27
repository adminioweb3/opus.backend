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
    private readonly ISentimentClassifierService _sentimentClassifier;
    private readonly ICitationExtractorService _citationExtractor;

    public PromptExecutionService(
        IPromptIntelligenceRepository repo,
        IWebsiteRepository websiteRepo,
        ILLMRunnerService llmRunner,
        IVisibilityCalculatorService calculator,
        IRecommendationEngineService recommendationEngine,
        ISentimentClassifierService sentimentClassifier,
        ICitationExtractorService citationExtractor)
    {
        _repo = repo;
        _websiteRepo = websiteRepo;
        _llmRunner = llmRunner;
        _calculator = calculator;
        _recommendationEngine = recommendationEngine;
        _sentimentClassifier = sentimentClassifier;
        _citationExtractor = citationExtractor;
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
        string? ownDomain = TryGetHost(profile?.WebsiteUrl);

        // Create Analysis Record
        var analysis = new PromptAnalysis
        {
            PromptQuestionId = questionId,
            Status = "Running"
        };
        var analysisId = await _repo.CreateAnalysisAsync(analysis);

        yield return "{\"step\": \"Running against AI Models...\", \"progress\": 20}";

        var personaText = string.IsNullOrWhiteSpace(question.Persona) ? "a prospective customer" : question.Persona;
        var regionText = string.IsNullOrWhiteSpace(question.Region) || string.Equals(question.Region, "Global", StringComparison.OrdinalIgnoreCase) ? "" : $" based in {question.Region}";
        
        // Deliberately does NOT disclose brandName/profile — telling the model up front "the
        // business running this evaluation is X" made every response echo X regardless of the
        // question, which is what saturated visibility/citation scores at 100% even on genuinely
        // competitive prompts. The model must answer exactly as it would for a real, unaffiliated
        // user; VisibilityCalculatorService scores the resulting text blind, after the fact.
        string personaSystemPrompt =
            $"You are an AI search assistant answering for {personaText}{regionText}. " +
            "Give a concise, well-structured answer naming specific real products, brands, or sources " +
            "where relevant, as a real AI search engine would for this person. Answer naturally and " +
            "objectively — recommend whichever real companies or products genuinely fit the question best.";

        // Run LLMs
        var responses = (await _llmRunner.RunPromptAcrossModelsAsync(analysisId, question.PromptText, ct, personaSystemPrompt)).ToList();
        
        await _repo.InsertResponsesAsync(responses);

        var successfulResponses = responses.Where(r => !r.IsError).ToList();
        if (successfulResponses.Count == 0)
        {
            var errMsg = responses.FirstOrDefault()?.ErrorMessage ?? "All AI providers failed.";
            await _repo.UpdateAnalysisStatusAsync(analysisId, "Failed", errMsg);
            yield return $"{{\"error\": \"Analysis failed: {errMsg.Replace("\"", "'").Replace("\n", " ")}\"}}";
            yield break;
        }

        yield return "{\"step\": \"Extracting Mentions & Citations...\", \"progress\": 50}";

        // Calculate Visibility
        var trackedCompetitors = await _websiteRepo.GetCompetitorsAsync(organizationId);
        var competitors = trackedCompetitors.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var competitorDomains = trackedCompetitors.Select(c => c.WebsiteUrl).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        var (visibility, mentions, compComparisons) = _calculator.CalculateVisibilityMetrics(analysisId, successfulResponses, brandName, competitors);

        // Real citation extraction from the actual captured response text
        var citations = successfulResponses.SelectMany(r => _citationExtractor.ExtractCitations(analysisId, r.Platform, r.ResponseText, ownDomain, competitorDomains)).ToList();
        
        // Use real citation count
        if (ownDomain != null)
        {
            visibility.CitationCount = citations.Count(c => c.Domain.Contains(ownDomain, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            visibility.CitationCount = 0;
        }

        await _repo.InsertMentionsAsync(mentions);
        await _repo.InsertVisibilityAsync(visibility);
        await _repo.InsertCompetitorComparisonsAsync(compComparisons);
        await _repo.InsertCitationsAsync(citations);

        // Real LLM sentiment classification, scoped to responses that actually mentioned the brand.
        var brandMentionedPlatforms = mentions.Where(m => m.IsBrand).Select(m => m.Platform).ToHashSet();
        foreach (var response in successfulResponses.Where(r => brandMentionedPlatforms.Contains(r.Platform)))
        {
            var (sentiment, quote) = await _sentimentClassifier.ClassifyAsync(organizationId, response.ResponseText, brandName, ct);
            if (sentiment != null)
            {
                await _repo.UpdateResponseSentimentAsync(analysisId, response.Platform, sentiment, quote);
            }
        }

        yield return "{\"step\": \"Generating Recommendations...\", \"progress\": 80}";

        // Recommendations
        var recommendations = await _recommendationEngine.GenerateRecommendationsAsync(analysisId, visibility, compComparisons, citations, ct);
        await _repo.InsertRecommendationsAsync(recommendations);

        // Update status
        await _repo.UpdateAnalysisStatusAsync(analysisId, "Completed");

        yield return "{\"step\": \"Preparing Report...\", \"progress\": 100, \"analysisId\": \"" + analysisId.ToString() + "\"}";
    }

    private static string? TryGetHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var candidate = url.Contains("://") ? url : $"https://{url}";
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host)
            : null;
    }
}
