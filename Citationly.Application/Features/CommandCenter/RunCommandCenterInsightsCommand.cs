using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.CommandCenter;

public class RunCommandCenterInsightsCommand : IRequest<RunCommandCenterInsightsResult>
{
    public Guid OrganizationId { get; set; }

    /// <summary>Number of days the sibling metrics' history/trend arrays should be bounded to (7/30/90). Defaults to 30.</summary>
    public int RangeDays { get; set; } = 30;
}

public class RunCommandCenterInsightsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<string> Insights { get; set; } = new();
    public CommandCenterSiblingData? SiblingData { get; set; }
}

internal class InsightsAiResponse
{
    public List<string>? Insights { get; set; }
}

public class RunCommandCenterInsightsCommandHandler : IRequestHandler<RunCommandCenterInsightsCommand, RunCommandCenterInsightsResult>
{
    private readonly ICommandCenterRepository _commandCenterRepository;
    private readonly IOpenAiService _openAiService;

    private static readonly List<string> FallbackInsights = new()
    {
        "Your AI visibility metrics are being tracked across visibility, citation quality, brand health and share of voice.",
        "Run more scans across your feature dashboards to unlock deeper, data-driven executive insights here."
    };

    public RunCommandCenterInsightsCommandHandler(
        ICommandCenterRepository commandCenterRepository,
        IOpenAiService openAiService)
    {
        _commandCenterRepository = commandCenterRepository;
        _openAiService = openAiService;
    }

    public async Task<RunCommandCenterInsightsResult> Handle(RunCommandCenterInsightsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var siblingData = await _commandCenterRepository.GetSiblingSnapshotsAsync(request.OrganizationId, request.RangeDays);

            if (!siblingData.AnyDataAvailable)
            {
                return new RunCommandCenterInsightsResult
                {
                    Success = true,
                    Insights = new List<string>(),
                    SiblingData = siblingData
                };
            }

            var insights = await GenerateInsightsAsync(siblingData);

            var scanDate = DateOnly.FromDateTime(DateTime.UtcNow);
            await _commandCenterRepository.SaveInsightsAsync(request.OrganizationId, scanDate, insights);

            return new RunCommandCenterInsightsResult
            {
                Success = true,
                Insights = insights,
                SiblingData = siblingData
            };
        }
        catch (Exception ex)
        {
            return new RunCommandCenterInsightsResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<List<string>> GenerateInsightsAsync(CommandCenterSiblingData siblingData)
    {
        try
        {
            var systemPrompt = "You are an expert AI visibility and Generative Engine Optimization (GEO) executive analyst. " +
                "You write short, concrete, executive-level narrative insights grounded strictly in the numbers you are given.";

            var userPrompt = $@"Here are the current AI visibility metrics for this organization, compared to their previous scan:

- AI Visibility (composite score, 0-100): current = {siblingData.Visibility.Current:F1}, previous = {siblingData.Visibility.Previous:F1}, data available = {siblingData.Visibility.HasData}
- Citation Quality (composite score, 0-100): current = {siblingData.CitationQuality.Current:F1}, previous = {siblingData.CitationQuality.Previous:F1}, data available = {siblingData.CitationQuality.HasData}
- Brand Health (score, 0-100): current = {siblingData.BrandHealth.Current:F1}, previous = {siblingData.BrandHealth.Previous:F1}, data available = {siblingData.BrandHealth.HasData}
- Share of Voice (%): current = {siblingData.ShareOfVoice.Current:F1}, previous = {siblingData.ShareOfVoice.Previous:F1}, data available = {siblingData.ShareOfVoice.HasData}

Write 3 to 5 short (one sentence each), concrete, executive-level narrative insight bullets synthesizing what is improving or declining and why it matters to the business. Only reference metrics where data available = true. Ground every claim in the real numbers above — do not invent unrelated claims, competitor names, or facts not present here.

Return ONLY a JSON object in exactly this schema, with no markdown fences and no extra commentary:
{{
  ""insights"": [""...."", ""....""]
}}";

            var responseContent = await _openAiService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: systemPrompt,
                requireJson: true,
                model: "gpt-4o-mini");

            responseContent = StripJsonFences(responseContent);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var parsed = JsonSerializer.Deserialize<InsightsAiResponse>(responseContent, options);

            if (parsed?.Insights != null && parsed.Insights.Count > 0)
            {
                var cleaned = parsed.Insights
                    .Where(i => !string.IsNullOrWhiteSpace(i))
                    .Select(i => i.Trim())
                    .Take(5)
                    .ToList();

                if (cleaned.Count > 0)
                {
                    return cleaned;
                }
            }

            return new List<string>(FallbackInsights);
        }
        catch
        {
            return new List<string>(FallbackInsights);
        }
    }

    private static string StripJsonFences(string content)
    {
        content = content.Trim();
        if (content.StartsWith("```json"))
        {
            content = content.Substring(7);
        }
        else if (content.StartsWith("```"))
        {
            content = content.Substring(3);
        }

        if (content.EndsWith("```"))
        {
            content = content.Substring(0, content.Length - 3);
        }

        return content.Trim();
    }
}
