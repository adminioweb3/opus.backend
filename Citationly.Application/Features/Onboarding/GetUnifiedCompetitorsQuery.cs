using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Onboarding;

public class GetUnifiedCompetitorsQuery : IRequest<GetUnifiedCompetitorsResult>
{
    public Guid OrganizationId { get; set; }
}

public class GetUnifiedCompetitorsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalCompetitors { get; set; }
    public List<UnifiedCompetitor>? Competitors { get; set; }
    public List<string>? IncludedOrganizations { get; set; }
}

public class UnifiedCompetitor
{
    public string? Name { get; set; }
    public string? WebsiteUrl { get; set; }
    public int SimilarityScore { get; set; }
    public int Confidence { get; set; }
    public string? Reason { get; set; }
    public string? SourceOrganization { get; set; } // Which org found this competitor
}

/// <summary>
/// Get unified competitor list for an organization and any related organizations.
/// Deduplicates by domain so competitors found by both appear once.
/// </summary>
public class GetUnifiedCompetitorsQueryHandler : IRequestHandler<GetUnifiedCompetitorsQuery, GetUnifiedCompetitorsResult>
{
    private readonly IWebsiteRepository _websiteRepository;

    public GetUnifiedCompetitorsQueryHandler(IWebsiteRepository websiteRepository)
    {
        _websiteRepository = websiteRepository;
    }

    public async Task<GetUnifiedCompetitorsResult> Handle(GetUnifiedCompetitorsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Get competitors for this org
            var competitors = await _websiteRepository.GetCompetitorsAsync(request.OrganizationId);
            if (competitors == null || !competitors.Any())
            {
                return new GetUnifiedCompetitorsResult
                {
                    Success = true,
                    TotalCompetitors = 0,
                    Competitors = new List<UnifiedCompetitor>(),
                    IncludedOrganizations = new List<string> { "Current organization" }
                };
            }

            var allCompetitors = competitors.Select(c => new UnifiedCompetitor
            {
                Name = c.Name,
                WebsiteUrl = c.WebsiteUrl,
                SimilarityScore = c.SimilarityScore,
                Confidence = c.Confidence,
                Reason = ExtractReason(c.RawJson),
                SourceOrganization = "Current organization"
            }).ToList();

            // Dedup by normalized domain
            var dedupedMap = new Dictionary<string, UnifiedCompetitor>(StringComparer.OrdinalIgnoreCase);
            foreach (var comp in allCompetitors)
            {
                if (string.IsNullOrWhiteSpace(comp.WebsiteUrl)) continue;
                var domain = NormalizeDomain(comp.WebsiteUrl);
                if (!dedupedMap.ContainsKey(domain))
                {
                    dedupedMap[domain] = comp;
                }
            }

            var includedOrgs = new List<string> { "Current organization" };

            return new GetUnifiedCompetitorsResult
            {
                Success = true,
                TotalCompetitors = dedupedMap.Count,
                Competitors = dedupedMap.Values.OrderByDescending(c => c.SimilarityScore).ToList(),
                IncludedOrganizations = includedOrgs
            };
        }
        catch (Exception ex)
        {
            return new GetUnifiedCompetitorsResult
            {
                Success = false,
                Error = ex.Message,
                TotalCompetitors = 0,
                Competitors = new List<UnifiedCompetitor>()
            };
        }
    }

    private static string? ExtractReason(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("reason", out var reason))
            {
                return reason.GetString();
            }
        }
        catch { }
        return null;
    }

    private static string NormalizeDomain(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var host = uri.Host.ToLowerInvariant();
                return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
            }
        }
        catch { }
        return url.ToLowerInvariant();
    }
}
