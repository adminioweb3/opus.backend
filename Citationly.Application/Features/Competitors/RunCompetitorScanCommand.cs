using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Citationly.Domain.Utils;
using MediatR;

namespace Citationly.Application.Features.Competitors;

public class RunCompetitorScanCommand : IRequest<RunCompetitorScanResult>
{
    public Guid OrganizationId { get; set; }
}

public sealed record RunCompetitorScanResult(bool Success, string Message);

public class RunCompetitorScanCommandHandler : IRequestHandler<RunCompetitorScanCommand, RunCompetitorScanResult>
{
    private const int EvidenceLookbackDays = 90;

    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly ICompetitorSnapshotRepository _snapshotRepo;
    private readonly IPromptIntelligenceRepository _promptIntelligenceRepository;

    public RunCompetitorScanCommandHandler(
        IAiVisibilityRepository visibilityRepo,
        IWebsiteRepository websiteRepository,
        ICompetitorSnapshotRepository snapshotRepo,
        IPromptIntelligenceRepository promptIntelligenceRepository)
    {
        _visibilityRepo = visibilityRepo;
        _websiteRepository = websiteRepository;
        _snapshotRepo = snapshotRepo;
        _promptIntelligenceRepository = promptIntelligenceRepository;
    }

    public async Task<RunCompetitorScanResult> Handle(RunCompetitorScanCommand request, CancellationToken cancellationToken)
    {
        await _snapshotRepo.EnsureTableCreatedAsync();

        var orgId = request.OrganizationId;
        var competitors = await _visibilityRepo.GetCompetitorsByOrgAsync(orgId);
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(orgId);
        var executiveSummary = await _websiteRepository.GetExecutiveSummaryAsync(orgId);

        if (competitors.Count == 0 && profile == null)
        {
            return new RunCompetitorScanResult(false, "No analyzed company or competitors were found. Complete onboarding first.");
        }

        var since = DateTime.UtcNow.AddDays(-EvidenceLookbackDays);
        var observations = (await _promptIntelligenceRepository
            .GetCompetitorWatchObservationDataAsync(orgId, since))
            .ToList();
        var responseCount = observations.Select(row => row.ResponseId).Distinct().Count();

        if (responseCount == 0)
        {
            return new RunCompetitorScanResult(
                false,
                "No completed OpenAI prompt responses were found. Run Prompt Intelligence before calculating Competitor Watch.");
        }

        var openAiPlatforms = observations
            .Select(row => row.Platform)
            .Where(platform => !string.IsNullOrWhiteSpace(platform))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var citations = (await _promptIntelligenceRepository.GetCitationSummaryDataAsync(orgId, since))
            .Where(citation => openAiPlatforms.Contains(citation.Platform))
            .ToList();

        var inputs = new List<CompetitorEvidenceInput>
        {
            BuildInput(
                competitorId: null,
                isYou: true,
                name: BusinessName(profile),
                websiteUrl: profile?.WebsiteUrl,
                observations,
                citations)
        };

        inputs.AddRange(competitors.Select(competitor => BuildInput(
            competitor.Id,
            isYou: false,
            competitor.Name,
            competitor.WebsiteUrl,
            observations,
            citations)));

        var scored = CompetitorEvidenceScorer.Score(inputs, responseCount);
        var previousScanDate = await _snapshotRepo.GetLatestScanDateAsync(orgId);
        var previousSnapshots = previousScanDate.HasValue
            ? await _snapshotRepo.GetSnapshotsByScanDateAsync(orgId, previousScanDate.Value)
            : new List<CompetitorSnapshot>();
        var previousYou = previousSnapshots.FirstOrDefault(snapshot => snapshot.IsYou);
        var previousByCompetitorId = previousSnapshots
            .Where(snapshot => snapshot.CompetitorId.HasValue)
            .ToDictionary(snapshot => snapshot.CompetitorId!.Value);

        var userScore = scored.First(score => score.IsYou);
        var modelUsed = ModelLabel(observations);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _snapshotRepo.DeleteByScanDateAsync(orgId, today);

        foreach (var current in scored)
        {
            var previous = current.IsYou
                ? previousYou
                : current.CompetitorId.HasValue
                    ? previousByCompetitorId.GetValueOrDefault(current.CompetitorId.Value)
                    : null;

            var competitor = current.CompetitorId.HasValue
                ? competitors.FirstOrDefault(item => item.Id == current.CompetitorId.Value)
                : null;

            await _snapshotRepo.InsertSnapshotAsync(new CompetitorSnapshot
            {
                OrganizationId = orgId,
                CompetitorId = current.CompetitorId,
                IsYou = current.IsYou,
                ScanDate = today,
                Name = current.Name,
                Score = current.Score,
                // Do not award #1 merely because the brand is first in an all-zero tie.
                Rank = HasEvidence(current)
                    ? 1 + scored.Count(other => HasEvidence(other) && other.Score > current.Score)
                    : 0,
                ShareOfVoice = current.ShareOfVoice,
                ShareOfVoiceChange = previous == null ? 0 : current.ShareOfVoice - previous.ShareOfVoice,
                Visibility = current.Score,
                VisibilityChange = previous == null ? 0 : current.Score - previous.Visibility,
                Threat = current.IsYou ? "low" : ThreatLevel(current, userScore),
                ModelsJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["OpenAI"] = current.Score }),
                Tagline = current.IsYou ? Tagline(executiveSummary) : CompetitorTagline(competitor),
                WebsiteUrl = current.WebsiteUrl,
                MentionCount = current.MentionCount,
                RecommendationCount = current.RecommendationCount,
                ResponseCount = current.ResponseCount,
                CitationCount = current.CitationCount,
                AveragePosition = current.AveragePosition,
                MeasurementSource = "openai-observed",
                MethodologyVersion = CompetitorEvidenceScorer.MethodologyVersion,
                ModelUsed = modelUsed,
                DiscoverySource = current.IsYou ? "self" : competitor?.DiscoverySource ?? "unknown"
            });
        }

        return new RunCompetitorScanResult(
            true,
            $"Competitor Watch calculated from {responseCount} measured OpenAI responses.");
    }

    private static CompetitorEvidenceInput BuildInput(
        Guid? competitorId,
        bool isYou,
        string name,
        string? websiteUrl,
        IReadOnlyList<CompetitorWatchObservationRow> observations,
        IReadOnlyList<PromptCitationSummaryRow> citations)
    {
        var positions = observations
            .Where(row => isYou
                ? row.IsBrand == true
                : row.IsBrand == false && string.Equals(row.EntityName?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(row => row.Position.HasValue)
            .GroupBy(row => row.ResponseId)
            .Select(group => group.Min(row => row.Position!.Value))
            .ToList();

        var recommendationPositions = observations
            .Where(row => isYou
                ? row.IsBrand == true
                : row.IsBrand == false && string.Equals(row.EntityName?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(row => row.IsRecommended)
            .GroupBy(row => row.ResponseId)
            .Select(group => group.Min(row => row.RecommendationPosition ?? 100))
            .ToList();

        var domain = DomainNormalizer.Normalize(websiteUrl ?? string.Empty);
        var citationCount = string.IsNullOrWhiteSpace(domain)
            ? 0
            : citations.Count(citation => DomainMatches(DomainNormalizer.Normalize(citation.Domain), domain));

        return new CompetitorEvidenceInput(
            competitorId,
            isYou,
            name,
            websiteUrl,
            positions,
            recommendationPositions,
            citationCount);
    }

    private static bool DomainMatches(string observedDomain, string trackedDomain) =>
        observedDomain.Equals(trackedDomain, StringComparison.OrdinalIgnoreCase) ||
        observedDomain.EndsWith($".{trackedDomain}", StringComparison.OrdinalIgnoreCase);

    private static bool HasEvidence(CompetitorEvidenceScore score) =>
        score.MentionCount > 0 || score.RecommendationCount > 0 || score.CitationCount > 0;

    private static string ThreatLevel(CompetitorEvidenceScore competitor, CompetitorEvidenceScore user)
    {
        if (competitor.Score >= user.Score + 15 || competitor.ShareOfVoice >= user.ShareOfVoice + 10) return "high";
        if (competitor.Score >= user.Score || competitor.ShareOfVoice > user.ShareOfVoice) return "med";
        return "low";
    }

    private static string ModelLabel(IEnumerable<CompetitorWatchObservationRow> observations)
    {
        var models = observations
            .Select(row => row.ModelUsed)
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return models.Count switch
        {
            0 => "OpenAI",
            1 => models[0]!,
            _ => "Multiple OpenAI models"
        };
    }

    private static string BusinessName(WebsiteProfile? profile) =>
        string.IsNullOrWhiteSpace(profile?.BusinessName) ? "Your Brand" : profile!.BusinessName;

    private static string? Tagline(ExecutiveSummaryData? executiveSummary)
    {
        var overview = executiveSummary?.BusinessOverview;
        if (string.IsNullOrWhiteSpace(overview)) return "Your organization";
        return overview.Length > 90 ? overview[..90] : overview;
    }

    private static string CompetitorTagline(Competitor? competitor)
    {
        if (!string.IsNullOrWhiteSpace(competitor?.Description))
            return competitor.Description.Length > 90 ? competitor.Description[..90] : competitor.Description;
        return string.IsNullOrWhiteSpace(competitor?.Industry) ? "Competitor" : competitor.Industry;
    }
}
