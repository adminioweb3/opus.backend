using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services;

public class CrossEngineConsensusService : ICrossEngineConsensusService
{
    private readonly IPromptIntelligenceRepository _repository;

    public CrossEngineConsensusService(IPromptIntelligenceRepository repository)
    {
        _repository = repository;
    }

    public async Task<CrossEngineConsensusResult> RefreshAsync(Guid organizationId, int lookbackDays = 30, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(lookbackDays, 1, 365));
        var rows = (await _repository.GetCrossEngineSourceRowsAsync(organizationId, since)).ToList();
        var providerCount = rows.Select(r => r.ProviderKey)
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (providerCount < 2)
        {
            return new CrossEngineConsensusResult(false, "Consensus analysis requires at least two independent configured providers with stored responses.", Array.Empty<CrossEngineConsensusInsight>());
        }

        var insights = rows
            .GroupBy(r => r.PromptAnalysisId)
            .SelectMany(group => BuildInsights(organizationId, group))
            .ToList();

        await _repository.UpsertCrossEngineConsensusInsightsAsync(insights);
        return await GetAsync(organizationId, lookbackDays, ct);
    }

    public async Task<CrossEngineConsensusResult> GetAsync(Guid organizationId, int lookbackDays = 30, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(lookbackDays, 1, 365));
        var rows = await _repository.GetCrossEngineSourceRowsAsync(organizationId, since);
        var providerCount = rows.Select(r => r.ProviderKey)
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var insights = providerCount < 2
            ? Array.Empty<CrossEngineConsensusInsight>()
            : (await _repository.GetCrossEngineConsensusInsightsAsync(organizationId, since)).ToArray();

        return new CrossEngineConsensusResult(
            providerCount >= 2,
            providerCount >= 2 ? "Consensus analysis is available for independent provider evidence." : "Consensus analysis requires at least two independent configured providers with stored responses.",
            insights);
    }

    private static IEnumerable<CrossEngineConsensusInsight> BuildInsights(Guid organizationId, IEnumerable<CrossEngineSourceRow> group)
    {
        var rows = group.ToList();
        if (rows.Select(r => r.ProviderKey).Where(provider => !string.IsNullOrWhiteSpace(provider)).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
        {
            yield break;
        }

        var citationMentions = rows
            .Select(r => new
            {
                Row = r,
                Cites = r.ResponseText.Contains("http", StringComparison.OrdinalIgnoreCase) ||
                        r.ResponseText.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                        r.ResponseText.Contains("according to", StringComparison.OrdinalIgnoreCase)
            })
            .ToList();

        if (citationMentions.Select(x => x.Cites).Distinct().Count() > 1)
        {
            yield return new CrossEngineConsensusInsight
            {
                OrganizationId = organizationId,
                PromptAnalysisId = rows[0].PromptAnalysisId,
                InsightType = "CitationDisagreement",
                Summary = "Engines disagree on whether this prompt needs cited/source-backed answers.",
                PlatformsJson = JsonSerializer.Serialize(rows.Select(r => r.Platform).Distinct().ToArray()),
                EvidenceJson = JsonSerializer.Serialize(citationMentions.Select(x => new
                {
                    x.Row.Platform,
                    provider = x.Row.ProviderKey,
                    citesSources = x.Cites,
                    excerpt = Excerpt(x.Row.ResponseText)
                }))
            };
        }

        var lengths = rows.Select(r => new { r.Platform, Length = r.ResponseText.Length }).ToList();
        if (lengths.Count > 1 && lengths.Max(l => l.Length) >= Math.Max(600, lengths.Min(l => l.Length) * 2))
        {
            yield return new CrossEngineConsensusInsight
            {
                OrganizationId = organizationId,
                PromptAnalysisId = rows[0].PromptAnalysisId,
                InsightType = "DepthDisagreement",
                Summary = "Engines disagree on answer depth/detail for the same prompt.",
                PlatformsJson = JsonSerializer.Serialize(rows.Select(r => r.Platform).Distinct().ToArray()),
                EvidenceJson = JsonSerializer.Serialize(lengths)
            };
        }
    }

    private static string Excerpt(string text)
    {
        var cleaned = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return cleaned.Length <= 220 ? cleaned : cleaned[..220];
    }
}
