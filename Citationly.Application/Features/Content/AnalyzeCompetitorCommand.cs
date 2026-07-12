using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Content;

public class AnalyzeCompetitorCommand : IRequest<CompetitorAnalysisResult>
{
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
    private readonly IOpenAiService _openAiService;

    public AnalyzeCompetitorCommandHandler(IScraperEngine scraperEngine, IOpenAiService openAiService)
    {
        _scraperEngine = scraperEngine;
        _openAiService = openAiService;
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

        var raw = await _openAiService.GenerateContentAsync(prompt, systemPrompt, requireJson: true);

        var result = ParseAnalysis(raw);
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
