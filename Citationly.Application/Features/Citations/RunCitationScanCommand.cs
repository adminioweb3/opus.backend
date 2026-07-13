using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Citations;

public class RunCitationScanCommand : IRequest<RunCitationScanResult>
{
    public Guid OrganizationId { get; set; }
}

public class RunCitationScanResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public CitationScanSummary? Summary { get; set; }
    public List<CitationSourceSnapshot> Sources { get; set; } = new();
}

// ---- Shapes used purely to deserialize the AI's JSON response ----

internal class CitationScanAiResponse
{
    public int CompositeQualityScore { get; set; }
    public int AverageAuthorityScore { get; set; }
    public int AverageInfluenceScore { get; set; }
    public int CitationSignal { get; set; }
    public int ModelsReferencingCount { get; set; }
    public int ModelsTrackedCount { get; set; }
    public List<CitationAiPlatform>? Platforms { get; set; }
    public List<CitationAiSource>? Sources { get; set; }
}

internal class CitationAiPlatform
{
    public string? Name { get; set; }
    public int Citations { get; set; }
    public int Visibility { get; set; }
    public int Quality { get; set; }
    public double GrowthPct { get; set; }
    public string? Status { get; set; }
}

internal class CitationAiSource
{
    public string? Source { get; set; }
    public string? Category { get; set; }
    public int AuthorityScore { get; set; }
    public int InfluenceScore { get; set; }
    public int CitationFrequency { get; set; }
    public int OpportunityScore { get; set; }
    public int CompetitorCoverage { get; set; }
    public int MentionProbability { get; set; }
    public string? Reason { get; set; }
}

public class RunCitationScanCommandHandler : IRequestHandler<RunCitationScanCommand, RunCitationScanResult>
{
    private readonly ICitationScanSnapshotRepository _citationRepository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openAiService;

    private static readonly string[] DefaultPlatformNames = { "ChatGPT", "Claude", "Gemini", "Perplexity" };

    public RunCitationScanCommandHandler(
        ICitationScanSnapshotRepository citationRepository,
        IWebsiteRepository websiteRepository,
        IOpenAiService openAiService)
    {
        _citationRepository = citationRepository;
        _websiteRepository = websiteRepository;
        _openAiService = openAiService;
    }

    public async Task<RunCitationScanResult> Handle(RunCitationScanCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
            var websiteUrl = profile?.WebsiteUrl ?? "an unspecified website";
            var businessName = profile?.BusinessName ?? "the business";
            var profileJson = string.IsNullOrWhiteSpace(profile?.RawProfileJson) ? "{}" : profile!.RawProfileJson;

            var systemPrompt = "You are an expert Generative Engine Optimization (GEO) and AI Citation Intelligence analyst. " +
                "You estimate how well a business's content is cited, referenced, and trusted as a source across major AI " +
                "search/answer platforms, and identify realistic third-party sources (publications, review sites, docs/wikis, " +
                "directories) that could cite or already cite this business.";

            var userPrompt = $@"Business: {businessName}
Website: {websiteUrl}
Website Profile (JSON, may be partial): {profileJson}

Analyze this business's ""citation quality"" — how authoritatively and how often it is cited/referenced by AI models when
answering questions in its industry — and identify realistic citation sources relevant to its domain.

Return ONLY a single valid JSON object (no markdown fences, no commentary) with EXACTLY this shape:
{{
  ""compositeQualityScore"": 0-100 integer, overall citation quality composite score,
  ""averageAuthorityScore"": 0-100 integer, average authority score across sources,
  ""averageInfluenceScore"": 0-100 integer, average influence score across sources,
  ""citationSignal"": 0-100 integer, strength of the citation signal AI models pick up on,
  ""modelsReferencingCount"": integer, how many of the tracked AI models currently reference this business (out of modelsTrackedCount),
  ""modelsTrackedCount"": integer, total number of AI models tracked (use 4),
  ""platforms"": [
    {{
      ""name"": one of ""ChatGPT"", ""Claude"", ""Gemini"", ""Perplexity"" (include all 4, exactly once each, in this order),
      ""citations"": integer count of citations observed/estimated on this platform,
      ""visibility"": 0-100 integer visibility score on this platform,
      ""quality"": 0-100 integer citation quality score on this platform,
      ""growthPct"": number, estimated percentage growth/decline trend (can be negative),
      ""status"": short status label such as ""Strong"", ""Improving"", ""Stable"", ""Declining"", or ""Developing""
    }}
  ],
  ""sources"": [
    {{
      ""source"": realistic source name (industry publication, review site, docs/wiki, or directory relevant to this business's domain),
      ""category"": short category label e.g. ""Industry Publication"", ""Review Site"", ""Documentation"", ""Directory"", ""Forum"",
      ""authorityScore"": 0-100 integer,
      ""influenceScore"": 0-100 integer,
      ""citationFrequency"": 0-100 integer,
      ""opportunityScore"": 0-100 integer representing how valuable it would be to get cited more by this source,
      ""competitorCoverage"": 0-100 integer representing how much competitors are already covered by this source,
      ""mentionProbability"": 0-100 integer,
      ""reason"": concise 1-2 sentence explanation grounded in the business's actual domain/industry
    }}
  ]
}}

Include exactly 4 items in ""platforms"" (ChatGPT, Claude, Gemini, Perplexity, in that order) and between 6 and 8 items in
""sources"". Ground every source and score in the business's real domain/industry context from the website profile above.
Never invent specific factual claims about the business — scores are predictive estimates only. Return ONLY the JSON object.";

            var responseContent = await _openAiService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: systemPrompt,
                requireJson: true,
                model: "gpt-4o-mini");

            var parsed = ParseAiResponse(responseContent);

            var scanDate = DateOnly.FromDateTime(DateTime.UtcNow);

            var platforms = NormalizePlatforms(parsed.Platforms);

            var summary = new CitationScanSummary
            {
                Id = Guid.NewGuid(),
                OrganizationId = request.OrganizationId,
                ScanDate = scanDate,
                CompositeQualityScore = Clamp(parsed.CompositeQualityScore),
                AverageAuthorityScore = Clamp(parsed.AverageAuthorityScore),
                AverageInfluenceScore = Clamp(parsed.AverageInfluenceScore),
                CitationSignal = Clamp(parsed.CitationSignal),
                ModelsReferencingCount = Math.Max(0, parsed.ModelsReferencingCount),
                ModelsTrackedCount = parsed.ModelsTrackedCount > 0 ? parsed.ModelsTrackedCount : 4,
                PlatformsJson = JsonSerializer.Serialize(platforms),
                CreatedAt = DateTime.UtcNow
            };

            var sources = NormalizeSources(parsed.Sources, request.OrganizationId, scanDate);

            await _citationRepository.SaveSnapshotAsync(summary, sources);

            return new RunCitationScanResult
            {
                Success = true,
                Summary = summary,
                Sources = sources
            };
        }
        catch (Exception ex)
        {
            return new RunCitationScanResult { Success = false, Error = ex.Message };
        }
    }

    private static CitationScanAiResponse ParseAiResponse(string? rawResponse)
    {
        var fallback = new CitationScanAiResponse
        {
            CompositeQualityScore = 50,
            AverageAuthorityScore = 50,
            AverageInfluenceScore = 50,
            CitationSignal = 50,
            ModelsReferencingCount = 1,
            ModelsTrackedCount = 4,
            Platforms = new List<CitationAiPlatform>(),
            Sources = new List<CitationAiSource>()
        };

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return fallback;
        }

        try
        {
            var content = rawResponse.Trim();

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

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };

            var parsed = JsonSerializer.Deserialize<CitationScanAiResponse>(content, options);
            return parsed ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static List<CitationAiPlatform> NormalizePlatforms(List<CitationAiPlatform>? aiPlatforms)
    {
        var byName = (aiPlatforms ?? new List<CitationAiPlatform>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var result = new List<CitationAiPlatform>();
        foreach (var name in DefaultPlatformNames)
        {
            if (byName.TryGetValue(name, out var match))
            {
                result.Add(new CitationAiPlatform
                {
                    Name = name,
                    Citations = Math.Max(0, match.Citations),
                    Visibility = Clamp(match.Visibility),
                    Quality = Clamp(match.Quality),
                    GrowthPct = match.GrowthPct,
                    Status = string.IsNullOrWhiteSpace(match.Status) ? "Developing" : match.Status
                });
            }
            else
            {
                result.Add(new CitationAiPlatform
                {
                    Name = name,
                    Citations = 0,
                    Visibility = 0,
                    Quality = 0,
                    GrowthPct = 0,
                    Status = "Developing"
                });
            }
        }
        return result;
    }

    private static List<CitationSourceSnapshot> NormalizeSources(List<CitationAiSource>? aiSources, Guid organizationId, DateOnly scanDate)
    {
        var sources = aiSources ?? new List<CitationAiSource>();

        var normalized = sources
            .Where(s => !string.IsNullOrWhiteSpace(s.Source))
            .Select(s => new CitationSourceSnapshot
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ScanDate = scanDate,
                Source = s.Source!.Trim(),
                Category = string.IsNullOrWhiteSpace(s.Category) ? null : s.Category!.Trim(),
                AuthorityScore = Clamp(s.AuthorityScore),
                InfluenceScore = Clamp(s.InfluenceScore),
                CitationFrequency = Clamp(s.CitationFrequency),
                CompetitorCoverage = Clamp(s.CompetitorCoverage),
                OpportunityScore = Clamp(s.OpportunityScore),
                MentionProbability = Clamp(s.MentionProbability),
                Reason = s.Reason,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        if (normalized.Count == 0)
        {
            // Sane fallback so the dashboard never shows a completely empty state after a scan.
            normalized.Add(new CitationSourceSnapshot
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ScanDate = scanDate,
                Source = "Industry Publications",
                Category = "Industry Publication",
                AuthorityScore = 50,
                InfluenceScore = 50,
                CitationFrequency = 30,
                CompetitorCoverage = 40,
                OpportunityScore = 50,
                MentionProbability = 30,
                Reason = "Fallback source generated because the AI response could not be parsed.",
                CreatedAt = DateTime.UtcNow
            });
        }

        return normalized;
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}
