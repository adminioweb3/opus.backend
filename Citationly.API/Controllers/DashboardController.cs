using Citationly.Application.Interfaces;
using Citationly.Application.Features.GeoDashboard;
using Citationly.Application.Features.Competitors;
using Citationly.Application.Features.Visibility;
using Citationly.Application.Features.Citations;
using Citationly.Application.Features.BrandPulse;
using Citationly.Application.Features.CommandCenter;
using Citationly.Application.Features.Opportunities;
using Citationly.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Dapper;
using System.Text.Json;
using Citationly.API.Services;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IAiVisibilityRepository _visibilityRepository;
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IMemoryCache _cache;
    private readonly GeoDashboardAggregator _aggregator;
    private readonly ICompetitorSnapshotRepository _snapshotRepository;
    private readonly IVisibilitySnapshotRepository _visibilitySnapshotRepository;
    private readonly ICitationScanSnapshotRepository _citationSnapshotRepository;
    private readonly IBrandPulseSnapshotRepository _brandPulseSnapshotRepository;
    private readonly CommandCenterAggregator _commandCenterAggregator;
    private readonly OpportunityFinderAggregator _opportunityAggregator;
    private readonly IOpportunitySnapshotRepository _opportunitySnapshotRepository;
    private readonly IMediator _mediator;
    private readonly ICurrentOrganizationAccessor _currentOrganization;

    public DashboardController(
        IAiVisibilityRepository visibilityRepository,
        IDbConnectionFactory dbConnectionFactory,
        IMemoryCache cache,
        GeoDashboardAggregator aggregator,
        ICompetitorSnapshotRepository snapshotRepository,
        IVisibilitySnapshotRepository visibilitySnapshotRepository,
        ICitationScanSnapshotRepository citationSnapshotRepository,
        IBrandPulseSnapshotRepository brandPulseSnapshotRepository,
        CommandCenterAggregator commandCenterAggregator,
        OpportunityFinderAggregator opportunityAggregator,
        IOpportunitySnapshotRepository opportunitySnapshotRepository,
        IMediator mediator,
        ICurrentOrganizationAccessor currentOrganization)
    {
        _visibilityRepository = visibilityRepository;
        _dbConnectionFactory = dbConnectionFactory;
        _cache = cache;
        _aggregator = aggregator;
        _snapshotRepository = snapshotRepository;
        _visibilitySnapshotRepository = visibilitySnapshotRepository;
        _citationSnapshotRepository = citationSnapshotRepository;
        _brandPulseSnapshotRepository = brandPulseSnapshotRepository;
        _commandCenterAggregator = commandCenterAggregator;
        _opportunityAggregator = opportunityAggregator;
        _opportunitySnapshotRepository = opportunitySnapshotRepository;
        _mediator = mediator;
        _currentOrganization = currentOrganization;
    }

    [HttpGet("visibility-summary")]
    public async Task<IActionResult> GetVisibilitySummary()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized();

        var scans = await _visibilityRepository.GetHistoricalScansByOrgAsync(organizationId.Value);
        var latestScan = scans.LastOrDefault();

        if (latestScan == null)
            return Ok(new { message = "No data yet. Scan might be running." });

        var competitors = await _visibilityRepository.GetCompetitorsByOrgAsync(organizationId.Value);

        return Ok(new
        {
            VisibilityScore = latestScan.VisibilityScore,
            CitationScore = latestScan.CitationScore,
            SentimentScore = latestScan.SentimentScore,
            CompetitorScore = latestScan.CompetitorScore,
            CurrentRank = 1, // Example calculation based on competitor scores vs self
            CompetitorCount = competitors.Count,
            LastScanDate = latestScan.ScanDate
        });
    }

    [HttpGet("top-competitors")]
    public async Task<IActionResult> GetTopCompetitors()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized();

        var competitors = await _visibilityRepository.GetCompetitorsByOrgAsync(organizationId.Value);
        return Ok(competitors);
    }

    [HttpGet("visibility-trend")]
    public async Task<IActionResult> GetVisibilityTrend()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized();

        var scans = await _visibilityRepository.GetHistoricalScansByOrgAsync(organizationId.Value);
        
        var trend = scans.Select(s => new
        {
            Date = s.ScanDate.ToString("MMM dd"),
            s.VisibilityScore,
            s.CitationScore
        }).ToList();

        return Ok(trend);
    }
    
    [HttpGet("share-of-voice")]
    public async Task<IActionResult> GetShareOfVoice()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized();

        var shareOfVoice = await _visibilityRepository.GetShareOfVoiceByOrgAsync(organizationId.Value);
        
        // Group by scan date and return the latest
        var latestScanDate = shareOfVoice.OrderByDescending(s => s.ScanDate).FirstOrDefault()?.ScanDate;
        
        if (latestScanDate == null) return Ok(new List<object>());

        var currentShare = shareOfVoice
            .Where(s => s.ScanDate == latestScanDate)
            .Select(s => new
            {
                name = s.CompetitorName,
                value = s.SharePercentage,
                color = s.ColorCode
            }).ToList();

        return Ok(currentShare);
    }

    private static readonly string[] CompetitorPalette = { "#7C3AED", "#2563EB", "#14B8A6", "#D97706", "#DB2777" };

    [HttpGet("competitor-watch")]
    public async Task<IActionResult> GetCompetitorWatch()
    {
        var orgGuid = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (orgGuid is null) return Unauthorized();

        await _snapshotRepository.EnsureTableCreatedAsync();

        var latestScanDate = await _snapshotRepository.GetLatestScanDateAsync(orgGuid.Value);
        if (latestScanDate == null)
        {
            await _mediator.Send(new RunCompetitorScanCommand { OrganizationId = orgGuid.Value });
        }

        return await BuildCompetitorWatchResponseAsync(orgGuid.Value);
    }

    [HttpPost("competitor-watch/rescan")]
    public async Task<IActionResult> RescanCompetitorWatch()
    {
        var orgGuid = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (orgGuid is null) return Unauthorized();

        await _snapshotRepository.EnsureTableCreatedAsync();

        var result = await _mediator.Send(new RunCompetitorScanCommand { OrganizationId = orgGuid.Value });
        if (!result.Success)
        {
            return BadRequest(new { message = result.Message });
        }

        return await BuildCompetitorWatchResponseAsync(orgGuid.Value);
    }

    private async Task<IActionResult> BuildCompetitorWatchResponseAsync(Guid orgGuid)
    {
        var latestScanDate = await _snapshotRepository.GetLatestScanDateAsync(orgGuid);
        if (latestScanDate == null)
        {
            // Nothing to scan yet (org hasn't completed onboarding analysis at all).
            return Ok(new { you = (object?)null, comps = Array.Empty<object>() });
        }

        var latest = await _snapshotRepository.GetSnapshotsByScanDateAsync(orgGuid, latestScanDate.Value);
        var youSnap = latest.FirstOrDefault(s => s.IsYou);
        // Was capped at 4 — the scan itself (RunCompetitorScanCommand) snapshots every tracked
        // competitor with no limit, so this was truncating an already-larger real dataset down to
        // 4 before it ever reached the client. Match CompetitorDiscoveryService's TopSelectionCount
        // (40) so Competitor Watch reflects the same competitor set as the rest of the product, and
        // so there's actually more than a page's worth for the frontend's "More" modal to reveal.
        var compSnaps = latest.Where(s => !s.IsYou).OrderBy(s => s.Rank).Take(40).ToList();

        var history = await _snapshotRepository.GetRecentHistoryAsync(orgGuid, 12);

        List<int> BuildTrend(Guid? competitorId, bool isYou)
        {
            var points = history
                .Where(s => isYou ? s.IsYou : (!s.IsYou && s.CompetitorId == competitorId))
                .OrderBy(s => s.ScanDate)
                .Select(s => s.Visibility)
                .ToList();

            if (points.Count == 0) return Enumerable.Repeat(50, 12).ToList();
            if (points.Count < 12)
            {
                var padded = Enumerable.Repeat(points[0], 12 - points.Count).ToList();
                padded.AddRange(points);
                return padded;
            }
            return points.TakeLast(12).ToList();
        }

        Dictionary<string, int> ParseModels(string modelsJson)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, int>>(modelsJson) ?? new();
            }
            catch
            {
                return new();
            }
        }

        object? you = youSnap == null ? null : new
        {
            id = "brand",
            name = youSnap.Name,
            logo = string.IsNullOrEmpty(youSnap.Name) ? "Y" : youSnap.Name[..1].ToUpper(),
            color = "#6366F1",
            you = true,
            sov = youSnap.ShareOfVoice,
            sovChg = youSnap.ShareOfVoiceChange,
            vis = youSnap.Visibility,
            visChg = youSnap.VisibilityChange,
            threat = youSnap.Threat,
            rank = youSnap.Rank,
            tagline = youSnap.Tagline ?? "Your organization",
            websiteUrl = youSnap.WebsiteUrl,
            models = ParseModels(youSnap.ModelsJson),
            citations = new { total = (youSnap.Score * 25).ToString("N0"), share = $"{youSnap.ShareOfVoice}%" },
            content = new { velocity = $"{Math.Max(1, youSnap.Score / 8)} / wk" },
            trend = BuildTrend(null, true)
        };

        var compsList = compSnaps.Select((s, i) => (object)new
        {
            id = s.CompetitorId?.ToString() ?? s.Name,
            name = s.Name,
            logo = string.IsNullOrEmpty(s.Name) ? "C" : s.Name[..1].ToUpper(),
            color = CompetitorPalette[i % CompetitorPalette.Length],
            you = false,
            sov = s.ShareOfVoice,
            sovChg = s.ShareOfVoiceChange,
            vis = s.Visibility,
            visChg = s.VisibilityChange,
            threat = s.Threat,
            rank = s.Rank,
            tagline = s.Tagline ?? "Competitor",
            websiteUrl = s.WebsiteUrl,
            models = ParseModels(s.ModelsJson),
            citations = new { total = (s.Score * 25).ToString("N0"), share = $"{s.ShareOfVoice}%" },
            content = new { velocity = $"{Math.Max(1, s.Score / 8)} / wk" },
            trend = BuildTrend(s.CompetitorId, false)
        }).ToList();

        return Ok(new { you, comps = compsList });
    }

    [HttpGet("geo-dashboard")]
    public async Task<IActionResult> GetGeoDashboard(
        [FromQuery] string range = "30D")
    {
        var orgGuid = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (orgGuid is null) return Unauthorized();

        // Validate range
        var validRanges = new HashSet<string> { "7D", "30D", "90D", "1Y" };
        if (!validRanges.Contains(range)) range = "30D";

        // Self-healing migration for missing columns
        try 
        {
            await _visibilityRepository.GetHistoricalScansByOrgAsync(orgGuid.Value);
        }
        catch (Exception)
        {
            try
            {
                using var connection = _dbConnectionFactory.CreateConnection();
                var alterSql = @"ALTER TABLE HistoricalScans 
                                 ADD COLUMN IF NOT EXISTS HallucinationRisk INT DEFAULT 0, 
                                 ADD COLUMN IF NOT EXISTS SeoHealth INT DEFAULT 0, 
                                 ADD COLUMN IF NOT EXISTS AeoReadiness INT DEFAULT 0, 
                                 ADD COLUMN IF NOT EXISTS GeoReadiness INT DEFAULT 0;";
                await connection.ExecuteAsync(alterSql);
            }
            catch { /* Ignore, next call will fail gracefully if it's a real issue */ }
        }

        // Cache key = authenticated organization + range, 30-second TTL
        var cacheKey = $"geo-dash:{orgGuid.Value}:{range}";

        var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await _aggregator.BuildAsync(orgGuid.Value, range);
        });

        return Ok(result);
    }

    [HttpGet("visibility-radar")]
    public async Task<IActionResult> GetVisibilityRadar(
        [FromQuery] string range = "30D")
    {
        var orgGuid = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (orgGuid is null) return Unauthorized();

        var validRanges = new HashSet<string> { "7D", "30D", "90D" };
        if (!validRanges.Contains(range)) range = "30D";
        var lookbackDays = range switch { "7D" => 7, "90D" => 90, _ => 30 };

        await _visibilitySnapshotRepository.EnsureTableCreatedAsync();

        var latestScanDate = await _visibilitySnapshotRepository.GetLatestScanDateAsync(orgGuid.Value);
        if (latestScanDate == null)
        {
            await _mediator.Send(new RunVisibilityScanCommand { OrganizationId = orgGuid.Value });
            latestScanDate = await _visibilitySnapshotRepository.GetLatestScanDateAsync(orgGuid.Value);
        }

        if (latestScanDate == null)
        {
            return Ok(new { hasData = false });
        }

        var latestSummary = await _visibilitySnapshotRepository.GetSummaryByScanDateAsync(orgGuid.Value, latestScanDate.Value);
        var latestPlatforms = await _visibilitySnapshotRepository.GetPlatformSnapshotsByScanDateAsync(orgGuid.Value, latestScanDate.Value);

        var summaryHistory = await _visibilitySnapshotRepository.GetRecentSummaryHistoryAsync(orgGuid.Value, 20);
        var platformHistory = await _visibilitySnapshotRepository.GetRecentPlatformHistoryAsync(orgGuid.Value, 20);

        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-lookbackDays);
        var summaryInRange = summaryHistory.Where(s => s.ScanDate >= cutoff).OrderBy(s => s.ScanDate).ToList();
        if (summaryInRange.Count == 0 && summaryHistory.Count > 0) summaryInRange = new List<VisibilityScanSummary> { summaryHistory.Last() };

        var scoreHistory = summaryInRange.Select(s => new { date = s.ScanDate.ToString("MMM dd"), score = s.CompositeScore }).ToList();

        int? deltaValue = summaryInRange.Count > 1
            ? summaryInRange.Last().CompositeScore - summaryInRange.First().CompositeScore
            : null;

        var compositeDelta = deltaValue == null
            ? "First scan this period"
            : $"{(deltaValue.Value >= 0 ? "+" : "")}{deltaValue.Value} pts vs {lookbackDays} days ago";

        var platforms = latestPlatforms.Select(p =>
        {
            var sparkline = platformHistory
                .Where(h => h.Platform == p.Platform && h.ScanDate >= cutoff)
                .OrderBy(h => h.ScanDate)
                .Select(h => h.Score)
                .ToList();
            if (sparkline.Count == 0) sparkline = new List<int> { p.Score };

            return new
            {
                name = p.Platform,
                score = p.Score,
                citations = p.Citations,
                status = p.Status,
                sparkline
            };
        }).OrderByDescending(p => p.score).ToList();

        return Ok(new
        {
            hasData = true,
            compositeScore = latestSummary?.CompositeScore ?? 0,
            compositeDelta,
            lastScanDate = latestScanDate.Value.ToString("yyyy-MM-dd"),
            platforms,
            signalMix = new[]
            {
                new { label = "Direct citations", pct = latestSummary?.DirectPct ?? 0 },
                new { label = "Brand mentions", pct = latestSummary?.MentionsPct ?? 0 },
                new { label = "Indirect references", pct = latestSummary?.IndirectPct ?? 0 },
                new { label = "Comparative mentions", pct = latestSummary?.ComparativePct ?? 0 }
            },
            scoreHistory
        });
    }

    [HttpGet("citation-intelligence")]
    public async Task<IActionResult> GetCitationIntelligence(
        [FromQuery] string range = "30D")
    {
        var orgGuid = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (orgGuid is null) return Unauthorized();

        var validRanges = new HashSet<string> { "7D", "30D", "90D" };
        if (!validRanges.Contains(range)) range = "30D";
        var lookbackDays = range switch { "7D" => 7, "90D" => 90, _ => 30 };

        await _citationSnapshotRepository.EnsureTableCreatedAsync();

        var latestScanDate = await _citationSnapshotRepository.GetLatestScanDateAsync(orgGuid.Value);
        if (latestScanDate == null)
        {
            await _mediator.Send(new RunCitationScanCommand { OrganizationId = orgGuid.Value });
            latestScanDate = await _citationSnapshotRepository.GetLatestScanDateAsync(orgGuid.Value);
        }

        if (latestScanDate == null)
        {
            return Ok(new { hasData = false });
        }

        var latestSummary = await _citationSnapshotRepository.GetSummaryByScanDateAsync(orgGuid.Value, latestScanDate.Value);
        var latestSources = await _citationSnapshotRepository.GetSourceSnapshotsByScanDateAsync(orgGuid.Value, latestScanDate.Value);

        var summaryHistory = await _citationSnapshotRepository.GetRecentSummaryHistoryAsync(orgGuid.Value, 20);
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-lookbackDays);
        var summaryInRange = summaryHistory.Where(s => s.ScanDate >= cutoff).OrderBy(s => s.ScanDate).ToList();
        if (summaryInRange.Count == 0 && summaryHistory.Count > 0) summaryInRange = new List<CitationScanSummary> { summaryHistory.Last() };

        var qualityHistory = summaryInRange.Select(s => new { date = s.ScanDate.ToString("MMM dd"), score = s.CompositeQualityScore }).ToList();
        var signalHistory = summaryInRange.Select(s => new { date = s.ScanDate.ToString("MMM dd"), score = s.CitationSignal }).ToList();

        string DeltaText(int? latest, int? first, string unit)
        {
            if (latest == null || first == null || latest == first) return $"Steady vs {lookbackDays} days ago";
            var diff = latest.Value - first.Value;
            return $"{(diff >= 0 ? "+" : "")}{diff} {unit} vs {lookbackDays} days ago";
        }

        var qualityDelta = summaryInRange.Count > 1
            ? DeltaText(summaryInRange.Last().CompositeQualityScore, summaryInRange.First().CompositeQualityScore, "pts")
            : "First scan this period";
        var signalDelta = summaryInRange.Count > 1
            ? DeltaText(summaryInRange.Last().CitationSignal, summaryInRange.First().CitationSignal, "pts")
            : "First scan this period";

        // Reuses the sibling Visibility scan's own weekly per-platform snapshots for the
        // platform-distribution section, rather than maintaining a duplicate table.
        var visLatestDate = await _visibilitySnapshotRepository.GetLatestScanDateAsync(orgGuid.Value);
        var visPlatforms = visLatestDate.HasValue
            ? await _visibilitySnapshotRepository.GetPlatformSnapshotsByScanDateAsync(orgGuid.Value, visLatestDate.Value)
            : new List<VisibilityPlatformSnapshot>();
        var visPlatformHistory = await _visibilitySnapshotRepository.GetRecentPlatformHistoryAsync(orgGuid.Value, 20);

        var platforms = visPlatforms.Select(p =>
        {
            var history = visPlatformHistory.Where(h => h.Platform == p.Platform && h.ScanDate >= cutoff).OrderBy(h => h.ScanDate).ToList();
            var growthPct = 0.0;
            if (history.Count > 1 && history.First().Score > 0)
            {
                growthPct = Math.Round((history.Last().Score - history.First().Score) / (double)history.First().Score * 100, 1);
            }
            var quality = (int)Math.Round(0.5 * p.Score + 0.5 * (latestSummary?.CompositeQualityScore ?? p.Score));

            return new
            {
                name = p.Platform,
                citations = p.Citations,
                visibility = p.Score,
                quality,
                growthPct,
                status = p.Status
            };
        }).OrderByDescending(p => p.citations).ToList();

        var topSources = latestSources
            .OrderByDescending(s => s.InfluenceScore)
            .Take(9)
            .Select(s => new
            {
                source = s.Source,
                category = s.Category,
                authorityScore = s.AuthorityScore,
                influenceScore = s.InfluenceScore,
                citationFrequency = s.CitationFrequency,
                reason = s.Reason
            }).ToList();

        var opportunities = latestSources
            .OrderByDescending(s => s.OpportunityScore)
            .Take(4)
            .Select(s => new
            {
                source = s.Source,
                category = s.Category,
                opportunityScore = s.OpportunityScore,
                competitorCoverage = s.CompetitorCoverage,
                reason = s.Reason
            }).ToList();

        var avgOpportunityScore = latestSources.Count > 0 ? (int)Math.Round(latestSources.Average(s => s.OpportunityScore)) : 0;

        return Ok(new
        {
            hasData = true,
            compositeQualityScore = latestSummary?.CompositeQualityScore ?? 0,
            qualityDelta,
            citationSignal = latestSummary?.CitationSignal ?? 0,
            signalDelta,
            modelsReferencing = latestSummary?.ModelsReferencingCount ?? 0,
            modelsTracked = latestSummary?.ModelsTrackedCount ?? 9,
            avgOpportunityScore,
            lastScanDate = latestScanDate.Value.ToString("yyyy-MM-dd"),
            qualityHistory,
            signalHistory,
            platforms,
            topSources,
            opportunities
        });
    }

    [HttpGet("brand-pulse")]
    public async Task<IActionResult> GetBrandPulse(
        [FromQuery] string range = "30D")
    {
        var orgGuid = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (orgGuid is null) return Unauthorized();

        var validRanges = new HashSet<string> { "7D", "30D", "90D" };
        if (!validRanges.Contains(range)) range = "30D";
        var lookbackDays = range switch { "7D" => 7, "90D" => 90, _ => 30 };

        await _brandPulseSnapshotRepository.EnsureTableCreatedAsync();

        var latestScanDate = await _brandPulseSnapshotRepository.GetLatestScanDateAsync(orgGuid.Value);
        if (latestScanDate == null)
        {
            await _mediator.Send(new RunBrandPulseScanCommand { OrganizationId = orgGuid.Value });
            latestScanDate = await _brandPulseSnapshotRepository.GetLatestScanDateAsync(orgGuid.Value);
        }

        if (latestScanDate == null)
        {
            return Ok(new { hasData = false });
        }

        var latestSummary = await _brandPulseSnapshotRepository.GetSummaryByScanDateAsync(orgGuid.Value, latestScanDate.Value);
        if (latestSummary == null)
        {
            return Ok(new { hasData = false });
        }

        var history = await _brandPulseSnapshotRepository.GetRecentSummaryHistoryAsync(orgGuid.Value, 20);
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-lookbackDays);
        var inRange = history.Where(s => s.ScanDate >= cutoff).OrderBy(s => s.ScanDate).ToList();
        if (inRange.Count == 0) inRange = new List<BrandPulseScanSummary> { latestSummary };

        string DeltaText(int latest, int first)
        {
            var diff = latest - first;
            if (diff == 0) return "Steady vs last period";
            return $"{(diff > 0 ? "+" : "")}{diff} pts vs last period";
        }

        var first = inRange.First();
        var healthDelta = inRange.Count > 1 ? DeltaText(latestSummary.BrandHealth, first.BrandHealth) : "First scan this period";
        var confidenceDelta = inRange.Count > 1 ? DeltaText(latestSummary.AiConfidence, first.AiConfidence) : "First scan this period";
        var messagingDelta = inRange.Count > 1 ? DeltaText(latestSummary.MessagingConsistency, first.MessagingConsistency) : "First scan this period";
        var trustDelta = inRange.Count > 1 ? DeltaText(latestSummary.BrandTrust, first.BrandTrust) : "First scan this period";

        var metricHistory = new
        {
            health = inRange.Select(s => s.BrandHealth).ToList(),
            confidence = inRange.Select(s => s.AiConfidence).ToList(),
            messaging = inRange.Select(s => s.MessagingConsistency).ToList(),
            trust = inRange.Select(s => s.BrandTrust).ToList()
        };

        // Real share-of-perception, reused directly from the sibling Competitor scan.
        var competitorScanDate = await _snapshotRepository.GetLatestScanDateAsync(orgGuid.Value);
        var competitorSnapshots = competitorScanDate.HasValue
            ? await _snapshotRepository.GetSnapshotsByScanDateAsync(orgGuid.Value, competitorScanDate.Value)
            : new List<CompetitorSnapshot>();

        var shareOfPerception = new List<object>();
        if (competitorSnapshots.Count > 0)
        {
            var you = competitorSnapshots.FirstOrDefault(s => s.IsYou);
            if (you != null) shareOfPerception.Add(new { name = "Your brand", value = you.ShareOfVoice, color = "#6366F1" });

            var comps = competitorSnapshots.Where(s => !s.IsYou).OrderByDescending(s => s.ShareOfVoice).Take(4).ToList();
            for (int i = 0; i < comps.Count; i++)
            {
                shareOfPerception.Add(new { name = comps[i].Name, value = comps[i].ShareOfVoice, color = CompetitorPalette[i % CompetitorPalette.Length] });
            }

            var accountedFor = shareOfPerception.Sum(s => (int)s.GetType().GetProperty("value")!.GetValue(s)!);
            if (accountedFor < 100)
            {
                shareOfPerception.Add(new { name = "Others", value = 100 - accountedFor, color = "#CBD5E1" });
            }
        }

        object ParseJsonOrEmpty(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<object>(json) ?? new List<object>();
            }
            catch
            {
                return new List<object>();
            }
        }

        return Ok(new
        {
            hasData = true,
            brandHealth = latestSummary.BrandHealth,
            aiConfidence = latestSummary.AiConfidence,
            messagingConsistency = latestSummary.MessagingConsistency,
            brandTrust = latestSummary.BrandTrust,
            healthDelta,
            confidenceDelta,
            messagingDelta,
            trustDelta,
            metricHistory,
            sentimentMix = new
            {
                positive = latestSummary.SentimentPositive,
                neutral = latestSummary.SentimentNeutral,
                negative = latestSummary.SentimentNegative
            },
            shareOfPerception,
            modelInsights = ParseJsonOrEmpty(latestSummary.ModelInsightsJson),
            alerts = ParseJsonOrEmpty(latestSummary.AlertsJson),
            accuracyFlags = ParseJsonOrEmpty(latestSummary.AccuracyFlagsJson),
            promptEvidence = ParseJsonOrEmpty(latestSummary.PromptEvidenceJson),
            lastScanDate = latestScanDate.Value.ToString("yyyy-MM-dd")
        });
    }

    [HttpGet("command-center")]
    public async Task<IActionResult> GetCommandCenter(
        [FromQuery] string range = "30D")
    {
        var orgGuid = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (orgGuid is null) return Unauthorized();

        var validRanges = new HashSet<string> { "7D", "30D", "90D" };
        if (!validRanges.Contains(range)) range = "30D";

        var result = await _commandCenterAggregator.BuildAsync(orgGuid.Value, range);
        return Ok(result);
    }

    [HttpGet("opportunity-finder")]
    public async Task<IActionResult> GetOpportunityFinder(
        [FromQuery] string range = "30D")
    {
        var orgGuid = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (orgGuid is null) return Unauthorized();

        var validRanges = new HashSet<string> { "7D", "30D", "90D" };
        if (!validRanges.Contains(range)) range = "30D";

        var result = await _opportunityAggregator.BuildAsync(orgGuid.Value, range);
        return Ok(result);
    }

    [HttpPost("opportunity-finder/deep-scan")]
    public async Task<IActionResult> RunOpportunityDeepScan()
    {
        var orgGuid = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (orgGuid is null) return Unauthorized();

        await _opportunitySnapshotRepository.EnsureTableCreatedAsync();

        // Authoritative server-side cooldown check — the frontend also disables the button,
        // but this is what actually prevents re-running before 7 days have passed.
        var latestScanDate = await _opportunitySnapshotRepository.GetLatestScanDateAsync(orgGuid.Value);
        if (latestScanDate != null)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var daysSince = today.DayNumber - latestScanDate.Value.DayNumber;
            if (daysSince < 7)
            {
                var nextEligible = latestScanDate.Value.AddDays(7);
                return BadRequest(new { message = $"Deep scan already run this week. Next scan available on {nextEligible:yyyy-MM-dd}.", nextEligibleDate = nextEligible.ToString("yyyy-MM-dd") });
            }
        }

        var result = await _mediator.Send(new RunOpportunityScanCommand { OrganizationId = orgGuid.Value });
        if (!result.Success)
        {
            return BadRequest(new { message = result.Message });
        }

        var fresh = await _opportunityAggregator.BuildAsync(orgGuid.Value, "30D");
        return Ok(fresh);
    }
}
