using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.OpportunityFinder;

public class RunOpportunityScanCommand : IRequest<OpportunityScanResult>
{
    public Guid OrganizationId { get; set; }
}

public class OpportunityScanResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<OpportunitySnapshot> Opportunities { get; set; } = new();
}

/// <summary>
/// AI-facing shape for a single opportunity. Only the fields the model should invent live here —
/// Difficulty/Quadrant/Priority/Badge are deterministically derived in C# from Score/Effort, never
/// trusted from the AI response.
/// </summary>
internal class OpportunityAiItem
{
    public string? Category { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? WhyItMatters { get; set; }
    public double Score { get; set; }
    public double Effort { get; set; }
    public double EstimatedGainPct { get; set; }
    public string? Eta { get; set; }
    public string? CompetitorContext { get; set; }
    public List<string>? Checklist { get; set; }
}

internal class OpportunityAiResponse
{
    public List<OpportunityAiItem>? Opportunities { get; set; }
}

public class RunOpportunityScanCommandHandler : IRequestHandler<RunOpportunityScanCommand, OpportunityScanResult>
{
    private readonly IOpportunitySnapshotRepository _opportunityRepository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openAiService;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public RunOpportunityScanCommandHandler(
        IOpportunitySnapshotRepository opportunityRepository,
        IWebsiteRepository websiteRepository,
        IOpenAiService openAiService,
        IDbConnectionFactory dbConnectionFactory)
    {
        _opportunityRepository = opportunityRepository;
        _websiteRepository = websiteRepository;
        _openAiService = openAiService;
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<OpportunityScanResult> Handle(RunOpportunityScanCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Gather optional real signals (best-effort, each source isolated so one failure
            //    never blocks the scan — these tables may not exist yet on a fresh environment).
            string? competitorThreatContext = await TryGetTopCompetitorThreatAsync(request.OrganizationId);
            string? weakPlatformContext = await TryGetWeakestPlatformAsync(request.OrganizationId);
            string? citationOpportunityContext = await TryGetTopCitationOpportunityAsync(request.OrganizationId);

            string? profileContext = null;
            try
            {
                var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
                if (profile != null)
                {
                    profileContext = $"Website: {profile.WebsiteUrl}\nBusiness: {profile.BusinessName}\nProfile: {Truncate(profile.RawProfileJson, 2000)}";
                }
            }
            catch
            {
                profileContext = null;
            }

            var signalsBuilder = new List<string>();
            if (!string.IsNullOrWhiteSpace(profileContext)) signalsBuilder.Add(profileContext!);
            if (!string.IsNullOrWhiteSpace(competitorThreatContext)) signalsBuilder.Add(competitorThreatContext!);
            if (!string.IsNullOrWhiteSpace(weakPlatformContext)) signalsBuilder.Add(weakPlatformContext!);
            if (!string.IsNullOrWhiteSpace(citationOpportunityContext)) signalsBuilder.Add(citationOpportunityContext!);

            string signalsText = signalsBuilder.Any()
                ? string.Join("\n\n", signalsBuilder)
                : "No prior scan data is available for this organization yet. Generate plausible, generic-but-useful opportunities for a business trying to improve its AI/GEO search visibility.";

            var opportunities = await GenerateOpportunitiesAsync(signalsText);

            var snapshots = opportunities.Select(o => BuildSnapshot(o)).ToList();

            var saved = await _opportunityRepository.SaveScanAsync(request.OrganizationId, snapshots);

            return new OpportunityScanResult
            {
                Success = true,
                Opportunities = saved
            };
        }
        catch (Exception ex)
        {
            return new OpportunityScanResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<List<OpportunityAiItem>> GenerateOpportunitiesAsync(string signalsText)
    {
        var systemPrompt = "You are an expert Generative Engine Optimization (GEO), AI Search Visibility, SEO, and Competitive Strategy consultant who identifies concrete, prioritized growth opportunities for a business.";

        var userPrompt = $@"Based on the following real signals gathered about a business, generate a prioritized list of 6 to 9 distinct growth opportunities that would help the business improve its AI/GEO search visibility, competitive position, and citation authority.

## Signals
{signalsText}

## Categories to draw from (mix across these where relevant)
Content Gap, Technical SEO, Citation Building, Competitor Response, AI Optimization

## Instructions
1. Generate between 6 and 9 opportunities.
2. Each opportunity must be concrete and actionable, not generic fluff.
3. Ground opportunities in the signals above where possible; otherwise infer plausible, useful opportunities from the business context.
4. score: 0-100, higher means more valuable/impactful if pursued.
5. effort: 0-100, higher means more work/resources required to execute.
6. estimatedGainPct: plausible estimated percentage improvement in visibility/citations if this opportunity is executed (0-100).
7. eta: a short realistic timeframe string, e.g. ""1-2 weeks"", ""3-4 weeks"", ""1-2 months"".
8. competitorContext: one sentence tying this opportunity to competitive positioning.
9. checklist: 3 to 5 short, concrete action items as strings.
10. Return ONLY valid JSON. No markdown. No ```json fences. No explanations.

Return exactly this JSON schema:
{{
  ""opportunities"": [
    {{
      ""category"": """",
      ""title"": """",
      ""summary"": """",
      ""whyItMatters"": """",
      ""score"": 0,
      ""effort"": 0,
      ""estimatedGainPct"": 0,
      ""eta"": """",
      ""competitorContext"": """",
      ""checklist"": [""""]
    }}
  ]
}}

Return ONLY the JSON object.";

        try
        {
            var responseContent = await _openAiService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: systemPrompt,
                requireJson: true,
                model: "gpt-4o-mini");

            var cleaned = StripFences(responseContent);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };

            var parsed = JsonSerializer.Deserialize<OpportunityAiResponse>(cleaned, options);

            if (parsed?.Opportunities != null && parsed.Opportunities.Count > 0)
            {
                return parsed.Opportunities
                    .Where(o => !string.IsNullOrWhiteSpace(o.Title))
                    .Select(NormalizeItem)
                    .ToList();
            }
        }
        catch
        {
            // fall through to fallback opportunities below
        }

        return BuildFallbackOpportunities();
    }

    private static OpportunityAiItem NormalizeItem(OpportunityAiItem item)
    {
        item.Category = string.IsNullOrWhiteSpace(item.Category) ? "AI Optimization" : item.Category;
        item.Title = string.IsNullOrWhiteSpace(item.Title) ? "Untitled Opportunity" : item.Title;
        item.Summary = item.Summary ?? string.Empty;
        item.WhyItMatters = item.WhyItMatters ?? string.Empty;
        item.Score = Math.Clamp(item.Score, 0, 100);
        item.Effort = Math.Clamp(item.Effort, 0, 100);
        item.EstimatedGainPct = Math.Clamp(item.EstimatedGainPct, 0, 100);
        item.Eta = string.IsNullOrWhiteSpace(item.Eta) ? "2-4 weeks" : item.Eta;
        item.CompetitorContext = item.CompetitorContext ?? string.Empty;
        item.Checklist = (item.Checklist == null || item.Checklist.Count == 0)
            ? new List<string> { "Audit current state", "Implement changes", "Measure impact" }
            : item.Checklist.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        return item;
    }

    private static List<OpportunityAiItem> BuildFallbackOpportunities()
    {
        return new List<OpportunityAiItem>
        {
            new()
            {
                Category = "Content Gap",
                Title = "Publish an authoritative FAQ hub covering top customer questions",
                Summary = "Create a structured FAQ hub targeting the questions AI assistants are most often asked about this industry.",
                WhyItMatters = "AI answer engines strongly favor clearly structured Q&A content when generating responses.",
                Score = 78,
                Effort = 35,
                EstimatedGainPct = 12,
                Eta = "2-3 weeks",
                CompetitorContext = "Most competitors have not structured their content specifically for AI answer extraction.",
                Checklist = new List<string> { "Identify top 20 customer questions", "Write concise, structured answers", "Add FAQ schema markup", "Publish and internally link" }
            },
            new()
            {
                Category = "Technical SEO",
                Title = "Improve crawlability and structured data for key pages",
                Summary = "Audit and fix technical barriers preventing AI crawlers from fully indexing key pages.",
                WhyItMatters = "Poor technical foundations limit how much of the site AI systems can actually read and cite.",
                Score = 64,
                Effort = 42,
                EstimatedGainPct = 8,
                Eta = "3-4 weeks",
                CompetitorContext = "Competitors with cleaner technical implementations are more frequently cited.",
                Checklist = new List<string> { "Run a technical crawl audit", "Fix robots.txt and sitemap issues", "Add schema.org markup", "Verify AI crawler access" }
            },
            new()
            {
                Category = "Citation Building",
                Title = "Pursue placements on high-authority industry directories",
                Summary = "Target a shortlist of high-authority sources known to be cited by AI models in this industry.",
                WhyItMatters = "Third-party citations materially increase the likelihood of being mentioned in AI-generated answers.",
                Score = 70,
                Effort = 55,
                EstimatedGainPct = 10,
                Eta = "1-2 months",
                CompetitorContext = "Leading competitors have secured more third-party mentions on authoritative sources.",
                Checklist = new List<string> { "Identify top 10 authoritative sources", "Prepare outreach materials", "Submit or pitch for inclusion", "Track resulting citations" }
            }
        };
    }

    private OpportunitySnapshot BuildSnapshot(OpportunityAiItem item)
    {
        return new OpportunitySnapshot
        {
            Category = item.Category!,
            Title = item.Title!,
            Summary = item.Summary,
            WhyItMatters = item.WhyItMatters,
            Score = (int)Math.Round(item.Score),
            Effort = (int)Math.Round(item.Effort),
            EstimatedGainPct = item.EstimatedGainPct,
            Eta = item.Eta,
            CompetitorContext = item.CompetitorContext,
            ChecklistJson = JsonSerializer.Serialize(item.Checklist ?? new List<string>())
        };
    }

    private static string StripFences(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(7);
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed.Substring(3);
        }
        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 3);
        }
        return trimmed.Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
        return value.Substring(0, maxLength);
    }

    private async Task<string?> TryGetTopCompetitorThreatAsync(Guid organizationId)
    {
        try
        {
            using var connection = _dbConnectionFactory.CreateConnection();
            var exists = await TableExistsAsync(connection, "competitorsnapshots");
            if (!exists) return null;

            var row = await connection.QueryFirstOrDefaultAsync<(string Name, string Threat, int Rank)>(@"
                SELECT Name, Threat, Rank FROM CompetitorSnapshots
                WHERE OrganizationId = @OrganizationId
                  AND ScanDate = (SELECT MAX(ScanDate) FROM CompetitorSnapshots WHERE OrganizationId = @OrganizationId)
                  AND LOWER(Threat) = 'high'
                  AND IsYou = FALSE
                ORDER BY Rank ASC
                LIMIT 1",
                new { OrganizationId = organizationId });

            if (row.Name == null) return null;

            return $"Top competitive threat: \"{row.Name}\" is rated a HIGH threat (rank #{row.Rank}) in the most recent competitive scan.";
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryGetWeakestPlatformAsync(Guid organizationId)
    {
        try
        {
            using var connection = _dbConnectionFactory.CreateConnection();
            var exists = await TableExistsAsync(connection, "visibilityplatformsnapshots");
            if (!exists) return null;

            var row = await connection.QueryFirstOrDefaultAsync<(string Platform, int Score)>(@"
                SELECT Platform, Score FROM VisibilityPlatformSnapshots
                WHERE OrganizationId = @OrganizationId
                  AND ScanDate = (SELECT MAX(ScanDate) FROM VisibilityPlatformSnapshots WHERE OrganizationId = @OrganizationId)
                ORDER BY Score ASC
                LIMIT 1",
                new { OrganizationId = organizationId });

            if (row.Platform == null) return null;

            return $"Weakest AI platform: \"{row.Platform}\" has the lowest visibility score ({row.Score}/100) in the most recent visibility scan.";
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryGetTopCitationOpportunityAsync(Guid organizationId)
    {
        try
        {
            using var connection = _dbConnectionFactory.CreateConnection();
            var exists = await TableExistsAsync(connection, "citationsourcesnapshots");
            if (!exists) return null;

            var row = await connection.QueryFirstOrDefaultAsync<(string Source, int OpportunityScore)>(@"
                SELECT Source, OpportunityScore FROM CitationSourceSnapshots
                WHERE OrganizationId = @OrganizationId
                  AND ScanDate = (SELECT MAX(ScanDate) FROM CitationSourceSnapshots WHERE OrganizationId = @OrganizationId)
                ORDER BY OpportunityScore DESC
                LIMIT 1",
                new { OrganizationId = organizationId });

            if (row.Source == null) return null;

            return $"Top citation opportunity: \"{row.Source}\" has the highest untapped citation opportunity score ({row.OpportunityScore}/100).";
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TableExistsAsync(IDbConnection connection, string tableName)
    {
        return await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = @TableName)",
            new { TableName = tableName });
    }
}
