using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Visibility;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Visibility;

public class RunVisibilityScanCommand : IRequest<RunVisibilityScanResult>
{
    public Guid OrganizationId { get; set; }
}

public record RunVisibilityScanResult(bool Success, string Message);

public class RunVisibilityScanCommandHandler : IRequestHandler<RunVisibilityScanCommand, RunVisibilityScanResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IVisibilityScoringService _scoringService;
    private readonly IVisibilityRankingService _rankingService;
    private readonly IVisibilitySnapshotRepository _snapshotRepo;
    private readonly IOpenAiService _openAiService;

    public RunVisibilityScanCommandHandler(
        IWebsiteRepository websiteRepository,
        IVisibilityScoringService scoringService,
        IVisibilityRankingService rankingService,
        IVisibilitySnapshotRepository snapshotRepo,
        IOpenAiService openAiService)
    {
        _websiteRepository = websiteRepository;
        _scoringService = scoringService;
        _rankingService = rankingService;
        _snapshotRepo = snapshotRepo;
        _openAiService = openAiService;
    }

    public async Task<RunVisibilityScanResult> Handle(RunVisibilityScanCommand request, CancellationToken cancellationToken)
    {
        await _snapshotRepo.EnsureTableCreatedAsync();

        var orgId = request.OrganizationId;

        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(orgId);
        var prompts = (await _websiteRepository.GetAiSearchPromptsAsync(orgId)).ToList();

        if (profile == null && prompts.Count == 0)
        {
            return new RunVisibilityScanResult(false, "No analyzed data found yet for this organization. Complete onboarding analysis first, then run a visibility scan.");
        }

        // Real, deterministic per-platform scoring — same engine used during onboarding.
        var platformScores = _scoringService.CalculatePlatformScores(orgId, prompts);
        var summary = _rankingService.CalculateOverallSummary(orgId, platformScores);

        var appearingPrompts = prompts.Count(p => p.AppearsInAnswer);
        var totalScoreWeight = Math.Max(1, platformScores.Sum(p => p.VisibilityScore));

        var signalMix = await JudgeSignalMixAsync(profile, summary.OverallVisibilityScore);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _snapshotRepo.DeleteByScanDateAsync(orgId, today);

        await _snapshotRepo.InsertSummaryAsync(new VisibilityScanSummary
        {
            OrganizationId = orgId,
            ScanDate = today,
            CompositeScore = summary.OverallVisibilityScore,
            DirectPct = signalMix.Direct,
            MentionsPct = signalMix.Mentions,
            IndirectPct = signalMix.Indirect,
            ComparativePct = signalMix.Comparative
        });

        foreach (var platform in platformScores)
        {
            // Real prompt "appears in answer" count, distributed proportionally to each
            // platform's own real deterministic score — not a random per-platform guess.
            var citations = appearingPrompts == 0
                ? 0
                : (int)Math.Round((double)platform.VisibilityScore / totalScoreWeight * appearingPrompts);

            await _snapshotRepo.InsertPlatformSnapshotAsync(new VisibilityPlatformSnapshot
            {
                OrganizationId = orgId,
                ScanDate = today,
                Platform = platform.Platform,
                Score = platform.VisibilityScore,
                Citations = Math.Max(0, citations),
                Status = StatusFor(platform.VisibilityScore)
            });
        }

        return new RunVisibilityScanResult(true, "Visibility scan complete.");
    }

    private static string StatusFor(int score) =>
        score >= 80 ? "Strong" : score >= 65 ? "Solid" : score >= 45 ? "Developing" : "Weak";

    private record SignalMix(int Direct, int Mentions, int Indirect, int Comparative);

    private async Task<SignalMix> JudgeSignalMixAsync(WebsiteProfile? profile, int compositeScore)
    {
        var systemPrompt =
            "You analyze how a brand's presence in AI search answers breaks down by citation type. " +
            "Return ONLY a JSON object with EXACTLY these keys, integers 0-100 that sum to exactly 100: " +
            "\"direct\" (the page/domain is directly cited as a source), " +
            "\"mentions\" (the brand name is mentioned without a direct citation), " +
            "\"indirect\" (referenced indirectly via a third party, review site, or aggregator), " +
            "\"comparative\" (mentioned only in a comparison/versus context).";

        var businessName = string.IsNullOrWhiteSpace(profile?.BusinessName) ? "the business" : profile!.BusinessName;
        var userPrompt = $"Business: {businessName}\nOverall AI visibility composite score: {compositeScore}/100.\nEstimate the realistic breakdown of how this visibility is composed.";

        try
        {
            var raw = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            int Get(string key, int fallback) =>
                root.TryGetProperty(key, out var v) && v.TryGetInt32(out var iv) ? Math.Clamp(iv, 0, 100) : fallback;

            var direct = Get("direct", 55);
            var mentions = Get("mentions", 25);
            var indirect = Get("indirect", 13);
            var comparative = Get("comparative", 7);

            var total = direct + mentions + indirect + comparative;
            if (total <= 0) return new SignalMix(55, 25, 13, 7);

            // Rescale to sum to exactly 100, same rounding-drift convention used elsewhere.
            var values = new[] { direct, mentions, indirect, comparative };
            var scaled = values.Select(v => (int)Math.Round((double)v / total * 100)).ToArray();
            var drift = 100 - scaled.Sum();
            if (drift != 0)
            {
                var maxIdx = Array.IndexOf(scaled, scaled.Max());
                scaled[maxIdx] += drift;
            }

            return new SignalMix(scaled[0], scaled[1], scaled[2], scaled[3]);
        }
        catch (Exception)
        {
            return new SignalMix(55, 25, 13, 7);
        }
    }
}
