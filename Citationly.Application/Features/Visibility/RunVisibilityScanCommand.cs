using MediatR;
using System.Text.Json;
using System.Text.Json.Serialization;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Visibility;

public class RunVisibilityScanCommand : IRequest<RunVisibilityScanResult>
{
    public Guid OrganizationId { get; set; }
}

public class RunVisibilityScanResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public VisibilityScanSummary? Summary { get; set; }
    public List<VisibilityPlatformSnapshot>? Platforms { get; set; }
}

internal class VisibilityScanAiPlatform
{
    public string? Platform { get; set; }
    public double Score { get; set; }
    public int Citations { get; set; }
    public string? Status { get; set; }
}

internal class VisibilityScanAiResponse
{
    public double CompositeScore { get; set; }
    public double DirectPct { get; set; }
    public double MentionsPct { get; set; }
    public double IndirectPct { get; set; }
    public double ComparativePct { get; set; }
    public List<VisibilityScanAiPlatform>? Platforms { get; set; }
}

public class RunVisibilityScanCommandHandler : IRequestHandler<RunVisibilityScanCommand, RunVisibilityScanResult>
{
    private static readonly string[] RequiredPlatforms = { "ChatGPT", "Claude", "Gemini", "Perplexity" };

    private readonly IVisibilitySnapshotRepository _visibilitySnapshotRepository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openAiService;

    public RunVisibilityScanCommandHandler(
        IVisibilitySnapshotRepository visibilitySnapshotRepository,
        IWebsiteRepository websiteRepository,
        IOpenAiService openAiService)
    {
        _visibilitySnapshotRepository = visibilitySnapshotRepository;
        _websiteRepository = websiteRepository;
        _openAiService = openAiService;
    }

    public async Task<RunVisibilityScanResult> Handle(RunVisibilityScanCommand request, CancellationToken cancellationToken)
    {
        try
        {
            string businessContext = "No detailed business profile is available for this organization.";
            string competitorContext = "No competitor data is available for this organization.";

            try
            {
                var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
                if (profile != null)
                {
                    businessContext = $"Business Name: {profile.BusinessName}\nWebsite: {profile.WebsiteUrl}\nProfile Details: {profile.RawProfileJson}";
                }
            }
            catch
            {
                // Optional context only - proceed without it.
            }

            try
            {
                var competitors = await _websiteRepository.GetCompetitorsAsync(request.OrganizationId);
                if (competitors != null && competitors.Any())
                {
                    var names = competitors.Take(8).Select(c => $"{c.Name} ({c.Industry})");
                    competitorContext = "Known competitors: " + string.Join(", ", names);
                }
            }
            catch
            {
                // Optional context only - proceed without it.
            }

            var systemPrompt = "You are an expert Generative Engine Optimization (GEO) and AI-search visibility analyst. " +
                "You produce plausible, realistic AI-visibility assessments grounded in the business context provided. " +
                "Return ONLY a valid JSON object, no markdown fences, no explanations.";

            var userPrompt = $@"Assess this business's current AI visibility across 4 AI platforms: ChatGPT, Claude, Gemini, Perplexity.

## Business Context
{businessContext}

## Competitor Context
{competitorContext}

## Objective
Estimate a current AI-visibility snapshot for this business. Provide:
- An overall compositeScore (0-100) reflecting how visible/discoverable this business is across AI-generated answers.
- A signal mix breakdown of directPct, mentionsPct, indirectPct, comparativePct (0-100 each) representing what portion of the business's AI visibility comes from direct answers, brand mentions, indirect topical relevance, and comparative/competitor mentions respectively. These four values must sum to approximately 100.
- For each of the 4 platforms (ChatGPT, Claude, Gemini, Perplexity), estimate a score (0-100), an approximate number of citations (integer, realistic small number e.g. 0-50), and a status label of exactly ""Strong"", ""Developing"", or ""Weak"" based on the score (Strong >= 70, Developing 40-69, Weak < 40).

Return exactly this JSON schema:
{{
  ""compositeScore"": 0,
  ""directPct"": 0,
  ""mentionsPct"": 0,
  ""indirectPct"": 0,
  ""comparativePct"": 0,
  ""platforms"": [
    {{ ""platform"": ""ChatGPT"", ""score"": 0, ""citations"": 0, ""status"": ""Developing"" }},
    {{ ""platform"": ""Claude"", ""score"": 0, ""citations"": 0, ""status"": ""Developing"" }},
    {{ ""platform"": ""Gemini"", ""score"": 0, ""citations"": 0, ""status"": ""Developing"" }},
    {{ ""platform"": ""Perplexity"", ""score"": 0, ""citations"": 0, ""status"": ""Developing"" }}
  ]
}}

Return ONLY the JSON object.";

            VisibilityScanAiResponse? aiResult = null;
            try
            {
                var responseContent = await _openAiService.GenerateContentAsync(
                    prompt: userPrompt,
                    systemPrompt: systemPrompt,
                    requireJson: true,
                    model: "gpt-4o-mini");

                aiResult = ParseAiResponse(responseContent);
            }
            catch
            {
                aiResult = null;
            }

            var (summary, platforms) = BuildSnapshot(request.OrganizationId, aiResult);

            await _visibilitySnapshotRepository.SaveSnapshotAsync(summary, platforms);

            return new RunVisibilityScanResult
            {
                Success = true,
                Summary = summary,
                Platforms = platforms
            };
        }
        catch (Exception ex)
        {
            return new RunVisibilityScanResult { Success = false, Error = ex.Message };
        }
    }

    private static VisibilityScanAiResponse? ParseAiResponse(string? responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        var content = responseContent.Trim();
        if (content.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
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
        content = content.Trim();

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };

            return JsonSerializer.Deserialize<VisibilityScanAiResponse>(content, options);
        }
        catch
        {
            return null;
        }
    }

    private static (VisibilityScanSummary summary, List<VisibilityPlatformSnapshot> platforms) BuildSnapshot(
        Guid organizationId, VisibilityScanAiResponse? aiResult)
    {
        int compositeScore = Clamp((int)Math.Round(aiResult?.CompositeScore ?? 50), 0, 100);
        int directPct = Clamp((int)Math.Round(aiResult?.DirectPct ?? 25), 0, 100);
        int mentionsPct = Clamp((int)Math.Round(aiResult?.MentionsPct ?? 25), 0, 100);
        int indirectPct = Clamp((int)Math.Round(aiResult?.IndirectPct ?? 25), 0, 100);
        int comparativePct = Clamp((int)Math.Round(aiResult?.ComparativePct ?? 25), 0, 100);

        var summary = new VisibilityScanSummary
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ScanDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CompositeScore = compositeScore,
            DirectPct = directPct,
            MentionsPct = mentionsPct,
            IndirectPct = indirectPct,
            ComparativePct = comparativePct,
            CreatedAt = DateTime.UtcNow
        };

        var platforms = new List<VisibilityPlatformSnapshot>();
        var aiPlatformsByName = aiResult?.Platforms?
            .Where(p => !string.IsNullOrWhiteSpace(p.Platform))
            .GroupBy(p => p.Platform!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, VisibilityScanAiPlatform>(StringComparer.OrdinalIgnoreCase);

        foreach (var platformName in RequiredPlatforms)
        {
            if (aiPlatformsByName.TryGetValue(platformName, out var aiPlatform))
            {
                int score = Clamp((int)Math.Round(aiPlatform.Score), 0, 100);
                int citations = Math.Max(0, aiPlatform.Citations);
                string status = NormalizeStatus(aiPlatform.Status, score);

                platforms.Add(new VisibilityPlatformSnapshot
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    ScanDate = summary.ScanDate,
                    Platform = platformName,
                    Score = score,
                    Citations = citations,
                    Status = status,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                platforms.Add(new VisibilityPlatformSnapshot
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    ScanDate = summary.ScanDate,
                    Platform = platformName,
                    Score = 50,
                    Citations = 0,
                    Status = "Developing",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        return (summary, platforms);
    }

    private static string NormalizeStatus(string? status, int score)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            var trimmed = status.Trim();
            if (trimmed.Equals("Strong", StringComparison.OrdinalIgnoreCase)) return "Strong";
            if (trimmed.Equals("Developing", StringComparison.OrdinalIgnoreCase)) return "Developing";
            if (trimmed.Equals("Weak", StringComparison.OrdinalIgnoreCase)) return "Weak";
        }

        if (score >= 70) return "Strong";
        if (score >= 40) return "Developing";
        return "Weak";
    }

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
}
