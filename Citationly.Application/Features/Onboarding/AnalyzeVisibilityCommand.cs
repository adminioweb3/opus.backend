using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using System.Text.Json.Serialization;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzeVisibilityCommand : IRequest<VisibilityAnalysisResult>
{
    public Guid OrganizationId { get; set; }
}

public class VisibilityAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalPromptsAnalyzed { get; set; }
    public List<AiSearchPrompt>? Prompts { get; set; }
}

public class VisibilityResponse
{
    public VisibilitySummary? summary { get; set; }
    public List<VisibilityAnalysisItem>? analysis { get; set; }
}

public class VisibilitySummary
{
    public double totalPrompts { get; set; }
    public double averageVisibilityScore { get; set; }
    public double averageMentionProbability { get; set; }
    public double averageShareOfVoice { get; set; }
    public double highVisibilityPrompts { get; set; }
    public double mediumVisibilityPrompts { get; set; }
    public double lowVisibilityPrompts { get; set; }
}

public class VisibilityAnalysisItem
{
    public string? promptId { get; set; }
    public string? prompt { get; set; }
    public string? topic { get; set; }
    public double visibilityScore { get; set; }
    public string? estimatedRank { get; set; }
    public double confidence { get; set; }
    public bool appearsInAnswer { get; set; }
    public double shareOfVoiceContribution { get; set; }
    public double mentionProbability { get; set; }
    public double brandStrength { get; set; }
    public double contentStrength { get; set; }
    public double citationStrength { get; set; }
    public string? reason { get; set; }
}

public class AnalyzeVisibilityCommandHandler : IRequestHandler<AnalyzeVisibilityCommand, VisibilityAnalysisResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openRouterService;
    private const int BATCH_SIZE = 5;
    private const int MAX_CONCURRENT_BATCHES = 5;

    public AnalyzeVisibilityCommandHandler(
        IWebsiteRepository websiteRepository,
        IOpenAiService openRouterService)
    {
        _websiteRepository = websiteRepository;
        _openRouterService = openRouterService;
    }

    public async Task<VisibilityAnalysisResult> Handle(AnalyzeVisibilityCommand request, CancellationToken cancellationToken)
    {
        const int MAX_PROMPTS_TO_ANALYZE = 50;

        var existingPrompts = await _websiteRepository.GetAiSearchPromptsAsync(request.OrganizationId);
        if (existingPrompts == null || !existingPrompts.Any())
        {
            return new VisibilityAnalysisResult { Success = false, Error = "No AI Search Prompts found for this organization. Generate them first." };
        }

        // Limit to max 50 prompts for performance
        var promptsToAnalyze = existingPrompts.Take(MAX_PROMPTS_TO_ANALYZE).ToList();

        if (existingPrompts.Any(p => !string.IsNullOrEmpty(p.VisibilityReason)))
        {
            return new VisibilityAnalysisResult
            {
                Success = true,
                TotalPromptsAnalyzed = promptsToAnalyze.Count,
                Prompts = promptsToAnalyze
            };
        }

        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null)
        {
            return new VisibilityAnalysisResult { Success = false, Error = "Website profile not found for this organization." };
        }

        string websiteUrl = profile.WebsiteUrl;
        string websiteProfile = profile.RawProfileJson;

        try
        {
            var promptBatches = new List<List<AiSearchPrompt>>();
            for (int i = 0; i < promptsToAnalyze.Count; i += BATCH_SIZE)
            {
                promptBatches.Add(promptsToAnalyze.Skip(i).Take(BATCH_SIZE).ToList());
            }

            var semaphore = new SemaphoreSlim(MAX_CONCURRENT_BATCHES);
            var tasks = new List<Task<List<AiSearchPrompt>>>();

            foreach (var batch in promptBatches)
            {
                tasks.Add(ProcessBatchWithSemaphoreAsync(batch, websiteUrl, websiteProfile, semaphore, cancellationToken));
            }

            var batchResults = await Task.WhenAll(tasks);
            var allUpdatedPrompts = batchResults.SelectMany(x => x).ToList();

            if (allUpdatedPrompts.Any())
            {
                await _websiteRepository.UpdateAiSearchPromptsVisibilityAsync(allUpdatedPrompts);
            }

            return new VisibilityAnalysisResult
            {
                Success = true,
                TotalPromptsAnalyzed = allUpdatedPrompts.Count,
                Prompts = allUpdatedPrompts
            };
        }
        catch (Exception ex)
        {
            return new VisibilityAnalysisResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<List<AiSearchPrompt>> ProcessBatchWithSemaphoreAsync(
        List<AiSearchPrompt> batch,
        string websiteUrl,
        string websiteProfile,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await ProcessPromptBatchAsync(batch, websiteUrl, websiteProfile, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Batch processing error: {ex.Message}");
            return new List<AiSearchPrompt>();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<List<AiSearchPrompt>> ProcessPromptBatchAsync(
        List<AiSearchPrompt> promptBatch,
        string websiteUrl,
        string websiteProfile,
        CancellationToken cancellationToken)
    {
        var promptsForAi = promptBatch.Select(p => new
        {
            Id = p.Id,
            Prompt = p.QueryString,
            Topic = p.Topic
        }).ToList();

        string promptsJson = JsonSerializer.Serialize(promptsForAi);

        var systemPrompt = "You are an expert Generative Engine Optimization (GEO) and AI Search Visibility Analyst. Analyze prompts for AI visibility and return ONLY valid JSON.";

        var userPrompt = $@"Analyze these {promptBatch.Count} search prompts for AI visibility.

Website: {websiteUrl}
Profile: {websiteProfile}

Prompts: {promptsJson}

For each prompt, estimate:
- visibilityScore (0-100): How likely the business appears
- estimatedRank: 1-3, 4-10, 11-20, 21+, or 'Not Likely'
- confidence (0-100): Confidence in prediction
- appearsInAnswer: true/false
- shareOfVoiceContribution (0-100): % of answer
- mentionProbability (0-100): Likelihood company is mentioned
- brandStrength (0-100)
- contentStrength (0-100)
- citationStrength (0-100)
- reason: 1-2 sentence explanation

Return exactly this JSON:
{{
  ""summary"": {{
    ""totalPrompts"": 0,
    ""averageVisibilityScore"": 0,
    ""averageMentionProbability"": 0,
    ""averageShareOfVoice"": 0,
    ""highVisibilityPrompts"": 0,
    ""mediumVisibilityPrompts"": 0,
    ""lowVisibilityPrompts"": 0
  }},
  ""analysis"": [
    {{
      ""promptId"": """",
      ""prompt"": """",
      ""topic"": """",
      ""visibilityScore"": 0,
      ""estimatedRank"": """",
      ""confidence"": 0,
      ""appearsInAnswer"": false,
      ""shareOfVoiceContribution"": 0,
      ""mentionProbability"": 0,
      ""brandStrength"": 0,
      ""contentStrength"": 0,
      ""citationStrength"": 0,
      ""reason"": """"
    }}
  ]
}}

Return ONLY JSON, no markdown.";

        var responseContent = await _openRouterService.GenerateContentAsync(
            prompt: userPrompt,
            systemPrompt: systemPrompt,
            requireJson: true,
            model: "gpt-4o-mini");

        responseContent = CleanJsonResponse(responseContent);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        try
        {
            var result = JsonSerializer.Deserialize<VisibilityResponse>(responseContent, options);

            if (result?.analysis != null && result.analysis.Any())
            {
                var promptMap = promptBatch.ToDictionary(p => p.Id.ToString(), p => p, StringComparer.OrdinalIgnoreCase);
                var updatedPrompts = new List<AiSearchPrompt>();

                foreach (var item in result.analysis)
                {
                    if (item.promptId != null && promptMap.TryGetValue(item.promptId, out var dbPrompt))
                    {
                        dbPrompt.VisibilityScore = (int)Math.Round(item.visibilityScore);
                        dbPrompt.EstimatedRank = item.estimatedRank;
                        dbPrompt.Confidence = (int)Math.Round(item.confidence);
                        dbPrompt.AppearsInAnswer = item.appearsInAnswer;
                        dbPrompt.ShareOfVoiceContribution = (int)Math.Round(item.shareOfVoiceContribution);
                        dbPrompt.MentionProbability = (int)Math.Round(item.mentionProbability);
                        dbPrompt.BrandStrength = (int)Math.Round(item.brandStrength);
                        dbPrompt.ContentStrength = (int)Math.Round(item.contentStrength);
                        dbPrompt.CitationStrength = (int)Math.Round(item.citationStrength);
                        dbPrompt.VisibilityReason = item.reason;

                        updatedPrompts.Add(dbPrompt);
                    }
                }

                return updatedPrompts;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JSON parsing error: {ex.Message}");
        }

        return new List<AiSearchPrompt>();
    }

    private static string CleanJsonResponse(string response)
    {
        response = response.Trim();
        if (response.StartsWith("```json"))
            response = response.Substring(7);
        if (response.StartsWith("```"))
            response = response.Substring(3);
        if (response.EndsWith("```"))
            response = response.Substring(0, response.Length - 3);
        return response.Trim();
    }
}
