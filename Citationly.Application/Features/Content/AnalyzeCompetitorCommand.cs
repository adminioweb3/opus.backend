using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Content;

public class AnalyzeCompetitorCommand : IRequest<CompetitorAnalysisResult>
{
    public Guid OrganizationId { get; set; }
    public string Url { get; set; } = string.Empty;
}

public class CompetitorAnalysisResult
{
    public string? Title { get; set; }
    public int WordCount { get; set; }
    public string Opportunity { get; set; } = string.Empty;
    public List<string> GapSignals { get; set; } = new();
    public string RecommendedAngle { get; set; } = string.Empty;
}

public class AnalyzeCompetitorCommandHandler : IRequestHandler<AnalyzeCompetitorCommand, CompetitorAnalysisResult>
{
    private const int MaxContentChars = 6000;

    private readonly IScraperEngine _scraperEngine;
    private readonly IAiCompletionService _aiCompletionService;

    public AnalyzeCompetitorCommandHandler(IScraperEngine scraperEngine, IAiCompletionService aiCompletionService)
    {
        _scraperEngine = scraperEngine;
        _aiCompletionService = aiCompletionService;
    }

    public async Task<CompetitorAnalysisResult> Handle(AnalyzeCompetitorCommand request, CancellationToken cancellationToken)
    {
        var page = await _scraperEngine.ScrapeSinglePageAsync(request.Url, Guid.NewGuid());

        var content = page.Content ?? string.Empty;
        if (content.Length > MaxContentChars) content = content[..MaxContentChars];

        const string systemPrompt =
            "You are an SEO content strategist. Analyze the given competitor page content and respond with ONLY a JSON object " +
            "with these exact keys: \"opportunity\" (string, 1-2 sentences on the biggest content opportunity), " +
            "\"gapSignals\" (array of 3-5 short strings naming specific missing terms, entities, or topics), " +
            "\"recommendedAngle\" (string, 1-2 sentences recommending a structural or positioning angle to outperform it).";

        var prompt = $"COMPETITOR PAGE TITLE: {page.Title}\n\nCOMPETITOR PAGE CONTENT:\n{content}";

        var completion = await _aiCompletionService.CompleteAsync(
            request.OrganizationId,
            "content.analyze_competitor",
            prompt,
            systemPrompt,
            requireJson: true,
            preferredProviderKey: "openai",
            cancellationToken);
        if (!completion.Success)
        {
            throw new InvalidOperationException(completion.ErrorMessage ?? "Competitor content analysis failed.");
        }

        var result = ParseAnalysis(completion.Content);
        result.Title = page.Title;
        result.WordCount = page.WordCount;
        return result;
    }

    private static CompetitorAnalysisResult ParseAnalysis(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var result = new CompetitorAnalysisResult
            {
                Opportunity = root.TryGetProperty("opportunity", out var o) ? o.GetString() ?? string.Empty : string.Empty,
                RecommendedAngle = root.TryGetProperty("recommendedAngle", out var a) ? a.GetString() ?? string.Empty : string.Empty,
            };

            if (root.TryGetProperty("gapSignals", out var gaps) && gaps.ValueKind == JsonValueKind.Array)
            {
                result.GapSignals = gaps.EnumerateArray()
                    .Select(g => g.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }

            return result;
        }
        catch (JsonException)
        {
            return new CompetitorAnalysisResult { Opportunity = raw };
        }
    }
}
