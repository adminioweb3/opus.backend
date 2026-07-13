using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using System.Text.Json;
using Citationly.Application.Features.Visibility;
using Citationly.Application.Features.Citations;
using Citationly.Application.Features.BrandPulse;
using Citationly.Application.Features.CommandCenter;
using Citationly.Application.Features.OpportunityFinder;
using Citationly.Application.Features.CompetitorWatch;
using Citationly.Application.Features.GeoDashboard;
using Citationly.Domain.Entities;
using Citationly.Infrastructure.Repositories;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IAiVisibilityRepository _visibilityRepository;
    private readonly IMediator _mediator;
    private readonly IVisibilitySnapshotRepository _visibilitySnapshotRepository;
    private readonly ICitationScanSnapshotRepository _citationScanSnapshotRepository;
    private readonly IBrandPulseSnapshotRepository _brandPulseSnapshotRepository;
    private readonly IOpportunitySnapshotRepository _opportunitySnapshotRepository;
    private readonly ICompetitorSnapshotRepository _competitorSnapshotRepository;
    private readonly GeoDashboardAggregator _geoDashboardAggregator;

    public DashboardController(
        IAiVisibilityRepository visibilityRepository,
        IMediator mediator,
        IVisibilitySnapshotRepository visibilitySnapshotRepository,
        ICitationScanSnapshotRepository citationScanSnapshotRepository,
        IBrandPulseSnapshotRepository brandPulseSnapshotRepository,
        IOpportunitySnapshotRepository opportunitySnapshotRepository,
        ICompetitorSnapshotRepository competitorSnapshotRepository,
        GeoDashboardAggregator geoDashboardAggregator)
    {
        _visibilityRepository = visibilityRepository;
        _mediator = mediator;
        _visibilitySnapshotRepository = visibilitySnapshotRepository;
        _citationScanSnapshotRepository = citationScanSnapshotRepository;
        _brandPulseSnapshotRepository = brandPulseSnapshotRepository;
        _opportunitySnapshotRepository = opportunitySnapshotRepository;
        _competitorSnapshotRepository = competitorSnapshotRepository;
        _geoDashboardAggregator = geoDashboardAggregator;
    }

    [HttpGet("visibility-summary")]
    public async Task<IActionResult> GetVisibilitySummary([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");

        var scans = await _visibilityRepository.GetHistoricalScansByOrgAsync(organizationId);
        var latestScan = scans.LastOrDefault();

        if (latestScan == null)
            return Ok(new { message = "No data yet. Scan might be running." });

        var competitors = await _visibilityRepository.GetCompetitorsByOrgAsync(organizationId);

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
    public async Task<IActionResult> GetTopCompetitors([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");

        var competitors = await _visibilityRepository.GetCompetitorsByOrgAsync(organizationId);
        return Ok(competitors);
    }

    [HttpGet("visibility-trend")]
    public async Task<IActionResult> GetVisibilityTrend([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");

        var scans = await _visibilityRepository.GetHistoricalScansByOrgAsync(organizationId);

        var trend = scans.Select(s => new
        {
            Date = s.ScanDate.ToString("MMM dd"),
            s.VisibilityScore,
            s.CitationScore
        }).ToList();

        return Ok(trend);
    }

    [HttpGet("share-of-voice")]
    public async Task<IActionResult> GetShareOfVoice([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");

        var shareOfVoice = await _visibilityRepository.GetShareOfVoiceByOrgAsync(organizationId);

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

    // ===================== Visibility Radar =====================

    /// <summary>
    /// Returns the Visibility Radar snapshot for an organization: composite AI-visibility score,
    /// per-platform breakdown with sparklines, signal-mix distribution, and score history.
    /// Automatically bootstraps a scan if none exists yet, and refreshes if the latest scan is 7+ days old.
    /// </summary>
    [HttpGet("visibility-radar")]
    public async Task<IActionResult> GetVisibilityRadar([FromQuery] Guid organizationId, [FromQuery] string range = "30d")
    {
        if (organizationId == Guid.Empty)
        {
            return Ok(new VisibilityRadarResponse
            {
                HasData = false,
                CompositeScore = 0,
                CompositeDelta = "0",
                LastScanDate = string.Empty,
                Platforms = new List<VisibilityRadarPlatform>(),
                SignalMix = new List<VisibilityRadarSignal>(),
                ScoreHistory = new List<VisibilityRadarHistoryPoint>()
            });
        }

        int days = range switch
        {
            "7d" => 7,
            "30d" => 30,
            "90d" => 90,
            _ => 30
        };

        var latestScanDate = await _visibilitySnapshotRepository.GetLatestScanDateAsync(organizationId);

        if (latestScanDate == null)
        {
            var bootstrapResult = await _mediator.Send(new RunVisibilityScanCommand { OrganizationId = organizationId });
            if (!bootstrapResult.Success)
            {
                return Ok(new VisibilityRadarResponse
                {
                    HasData = false,
                    CompositeScore = 0,
                    CompositeDelta = "0",
                    LastScanDate = string.Empty,
                    Platforms = new List<VisibilityRadarPlatform>(),
                    SignalMix = new List<VisibilityRadarSignal>(),
                    ScoreHistory = new List<VisibilityRadarHistoryPoint>()
                });
            }
            latestScanDate = await _visibilitySnapshotRepository.GetLatestScanDateAsync(organizationId);
        }
        else if (latestScanDate.Value.ToDateTime(TimeOnly.MinValue) <= DateTime.UtcNow.Date.AddDays(-7))
        {
            await _mediator.Send(new RunVisibilityScanCommand { OrganizationId = organizationId });
        }

        var summary = await _visibilitySnapshotRepository.GetLatestSummaryAsync(organizationId);
        if (summary == null)
        {
            return Ok(new VisibilityRadarResponse
            {
                HasData = false,
                CompositeScore = 0,
                CompositeDelta = "0",
                LastScanDate = string.Empty,
                Platforms = new List<VisibilityRadarPlatform>(),
                SignalMix = new List<VisibilityRadarSignal>(),
                ScoreHistory = new List<VisibilityRadarHistoryPoint>()
            });
        }

        var history = await _visibilitySnapshotRepository.GetHistoryAsync(organizationId, days);
        var latestPlatforms = await _visibilitySnapshotRepository.GetLatestPlatformsAsync(organizationId);
        var sparklines = await _visibilitySnapshotRepository.GetPlatformSparklinesAsync(organizationId, days);

        string compositeDelta = "0";
        var previousSummary = history
            .Where(h => h.ScanDate < summary.ScanDate)
            .OrderByDescending(h => h.ScanDate)
            .FirstOrDefault();
        if (previousSummary != null)
        {
            int diff = summary.CompositeScore - previousSummary.CompositeScore;
            compositeDelta = diff > 0 ? $"+{diff}" : diff.ToString();
        }

        var platforms = latestPlatforms.Select(p => new VisibilityRadarPlatform
        {
            Name = p.Platform,
            Score = p.Score,
            Citations = p.Citations,
            Status = p.Status,
            Sparkline = sparklines.TryGetValue(p.Platform, out var points) ? points : new List<int>()
        }).ToList();

        var signalMix = new List<VisibilityRadarSignal>
        {
            new VisibilityRadarSignal { Label = "Direct", Pct = summary.DirectPct },
            new VisibilityRadarSignal { Label = "Mentions", Pct = summary.MentionsPct },
            new VisibilityRadarSignal { Label = "Indirect", Pct = summary.IndirectPct },
            new VisibilityRadarSignal { Label = "Comparative", Pct = summary.ComparativePct }
        };

        var scoreHistory = history.Select(h => new VisibilityRadarHistoryPoint
        {
            Date = h.ScanDate.ToString("yyyy-MM-dd"),
            Score = h.CompositeScore
        }).ToList();

        return Ok(new VisibilityRadarResponse
        {
            HasData = true,
            CompositeScore = summary.CompositeScore,
            CompositeDelta = compositeDelta,
            LastScanDate = summary.ScanDate.ToString("yyyy-MM-dd"),
            Platforms = platforms,
            SignalMix = signalMix,
            ScoreHistory = scoreHistory
        });
    }

    /// <summary>Response DTO for GET /Dashboard/visibility-radar.</summary>
    public class VisibilityRadarResponse
    {
        public bool HasData { get; set; }
        public int CompositeScore { get; set; }
        public string CompositeDelta { get; set; } = "0";
        public string LastScanDate { get; set; } = string.Empty;
        public List<VisibilityRadarPlatform> Platforms { get; set; } = new();
        public List<VisibilityRadarSignal> SignalMix { get; set; } = new();
        public List<VisibilityRadarHistoryPoint> ScoreHistory { get; set; } = new();
    }

    public class VisibilityRadarPlatform
    {
        public string Name { get; set; } = string.Empty;
        public int Score { get; set; }
        public int Citations { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<int> Sparkline { get; set; } = new();
    }

    public class VisibilityRadarSignal
    {
        public string Label { get; set; } = string.Empty;
        public int Pct { get; set; }
    }

    public class VisibilityRadarHistoryPoint
    {
        public string Date { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    // ===================== Citation Intelligence =====================

    /// <summary>
    /// Citation Intelligence — bootstraps on first visit and refreshes if the latest scan is 7+ days stale,
    /// then returns the composite citation quality metrics, history, per-platform breakdown, and top/opportunity sources.
    /// </summary>
    [HttpGet("citation-intelligence")]
    public async Task<IActionResult> GetCitationIntelligence([FromQuery] Guid organizationId, [FromQuery] string range = "30d")
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");

        var days = range switch
        {
            "7d" => 7,
            "90d" => 90,
            _ => 30
        };

        var latestScanDate = await _citationScanSnapshotRepository.GetLatestScanDateAsync(organizationId);
        var isStale = latestScanDate == null || latestScanDate.Value <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));

        if (isStale)
        {
            var scanResult = await _mediator.Send(new RunCitationScanCommand { OrganizationId = organizationId });
            if (!scanResult.Success && latestScanDate == null)
            {
                // No prior data and the scan failed — surface a clean empty response rather than an error.
                return Ok(new CitationIntelligenceResponse { HasData = false });
            }
        }

        var summary = await _citationScanSnapshotRepository.GetLatestSummaryAsync(organizationId);
        if (summary == null)
        {
            return Ok(new CitationIntelligenceResponse { HasData = false });
        }

        var previousSummary = await _citationScanSnapshotRepository.GetPreviousSummaryAsync(organizationId);
        var history = (await _citationScanSnapshotRepository.GetHistoryAsync(organizationId, days))
            .OrderBy(h => h.ScanDate)
            .ToList();
        var latestSources = (await _citationScanSnapshotRepository.GetLatestSourcesAsync(organizationId)).ToList();

        var platforms = new List<CitationPlatformDto>();
        try
        {
            var deserialized = JsonSerializer.Deserialize<List<CitationPlatformDto>>(
                summary.PlatformsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (deserialized != null) platforms = deserialized;
        }
        catch
        {
            platforms = new List<CitationPlatformDto>();
        }

        var avgOpportunityScore = latestSources.Count > 0
            ? (int)Math.Round(latestSources.Average(s => s.OpportunityScore))
            : 0;

        var qualityDelta = FormatCitationDelta(summary.CompositeQualityScore, previousSummary?.CompositeQualityScore);
        var signalDelta = FormatCitationDelta(summary.CitationSignal, previousSummary?.CitationSignal);

        var response = new CitationIntelligenceResponse
        {
            HasData = true,
            CompositeQualityScore = summary.CompositeQualityScore,
            QualityDelta = qualityDelta,
            CitationSignal = summary.CitationSignal,
            SignalDelta = signalDelta,
            ModelsReferencing = summary.ModelsReferencingCount,
            ModelsTracked = summary.ModelsTrackedCount,
            AvgOpportunityScore = avgOpportunityScore,
            LastScanDate = summary.ScanDate.ToString("yyyy-MM-dd"),
            QualityHistory = history.Select(h => new CitationHistoryPointDto
            {
                Date = h.ScanDate.ToString("yyyy-MM-dd"),
                Score = h.CompositeQualityScore
            }).ToList(),
            SignalHistory = history.Select(h => new CitationHistoryPointDto
            {
                Date = h.ScanDate.ToString("yyyy-MM-dd"),
                Score = h.CitationSignal
            }).ToList(),
            Platforms = platforms,
            TopSources = latestSources
                .OrderByDescending(s => s.AuthorityScore)
                .Take(6)
                .Select(s => new CitationTopSourceDto
                {
                    Source = s.Source,
                    Category = s.Category ?? "General",
                    AuthorityScore = s.AuthorityScore,
                    InfluenceScore = s.InfluenceScore,
                    CitationFrequency = s.CitationFrequency,
                    Reason = s.Reason ?? string.Empty
                }).ToList(),
            Opportunities = latestSources
                .OrderByDescending(s => s.OpportunityScore)
                .Take(6)
                .Select(s => new CitationOpportunityDto
                {
                    Source = s.Source,
                    Category = s.Category ?? "General",
                    OpportunityScore = s.OpportunityScore,
                    CompetitorCoverage = s.CompetitorCoverage,
                    Reason = s.Reason ?? string.Empty
                }).ToList()
        };

        return Ok(response);
    }

    private static string FormatCitationDelta(int current, int? previous)
    {
        if (previous == null) return "0";
        var diff = current - previous.Value;
        if (diff > 0) return $"+{diff}";
        if (diff < 0) return diff.ToString();
        return "0";
    }

    // ---- Response DTOs for GET /Dashboard/citation-intelligence ----

    public class CitationIntelligenceResponse
    {
        public bool HasData { get; set; }
        public int CompositeQualityScore { get; set; }
        public string QualityDelta { get; set; } = "0";
        public int CitationSignal { get; set; }
        public string SignalDelta { get; set; } = "0";
        public int ModelsReferencing { get; set; }
        public int ModelsTracked { get; set; }
        public int AvgOpportunityScore { get; set; }
        public string LastScanDate { get; set; } = string.Empty;
        public List<CitationHistoryPointDto> QualityHistory { get; set; } = new();
        public List<CitationHistoryPointDto> SignalHistory { get; set; } = new();
        public List<CitationPlatformDto> Platforms { get; set; } = new();
        public List<CitationTopSourceDto> TopSources { get; set; } = new();
        public List<CitationOpportunityDto> Opportunities { get; set; } = new();
    }

    public class CitationHistoryPointDto
    {
        public string Date { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    public class CitationPlatformDto
    {
        public string Name { get; set; } = string.Empty;
        public int Citations { get; set; }
        public int Visibility { get; set; }
        public int Quality { get; set; }
        public double GrowthPct { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class CitationTopSourceDto
    {
        public string Source { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int AuthorityScore { get; set; }
        public int InfluenceScore { get; set; }
        public int CitationFrequency { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class CitationOpportunityDto
    {
        public string Source { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int OpportunityScore { get; set; }
        public int CompetitorCoverage { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    // ===================== Brand Pulse =====================

    /// <summary>
    /// Returns the Brand Pulse dashboard data for the given organization: overall AI-perceived brand health,
    /// confidence/messaging/trust scores with deltas vs. the previous scan, historical trend arrays, sentiment mix,
    /// perceived share vs. competitors, per-model insights, alerts, accuracy flags, and example prompt evidence.
    /// Bootstraps a scan on first visit for an organization and automatically refreshes if the latest scan is 7+ days stale.
    /// </summary>
    [HttpGet("brand-pulse")]
    public async Task<IActionResult> GetBrandPulse([FromQuery] Guid organizationId, [FromQuery] string range = "30d")
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");

        try
        {
            var days = range switch
            {
                "7d" => 7,
                "90d" => 90,
                _ => 30
            };

            var latestScanDate = await _brandPulseSnapshotRepository.GetLatestScanDateAsync(organizationId);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var needsScan = latestScanDate == null || (today.DayNumber - latestScanDate.Value.DayNumber) >= 7;

            if (needsScan)
            {
                var scanResult = await _mediator.Send(new RunBrandPulseScanCommand { OrganizationId = organizationId });
                if (!scanResult.Success && latestScanDate == null)
                {
                    // No prior data and the bootstrap scan failed — report no data rather than erroring out the dashboard.
                    return Ok(new BrandPulseResponse { HasData = false });
                }
            }

            var latest = await _brandPulseSnapshotRepository.GetLatestSummaryAsync(organizationId);
            if (latest == null)
            {
                return Ok(new BrandPulseResponse { HasData = false });
            }

            var previous = await _brandPulseSnapshotRepository.GetPreviousSummaryAsync(organizationId, latest.ScanDate);
            var history = (await _brandPulseSnapshotRepository.GetHistoryAsync(organizationId, days)).ToList();

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            List<ShareOfPerceptionItem> shareOfPerception;
            try
            {
                shareOfPerception = JsonSerializer.Deserialize<List<ShareOfPerceptionItem>>(latest.SharePerceptionJson, jsonOptions) ?? new();
            }
            catch { shareOfPerception = new(); }

            List<ModelInsightItem> modelInsights;
            try
            {
                modelInsights = JsonSerializer.Deserialize<List<ModelInsightItem>>(latest.ModelInsightsJson, jsonOptions) ?? new();
            }
            catch { modelInsights = new(); }

            List<BrandAlertItem> alerts;
            try
            {
                alerts = JsonSerializer.Deserialize<List<BrandAlertItem>>(latest.AlertsJson, jsonOptions) ?? new();
            }
            catch { alerts = new(); }

            List<AccuracyFlagItem> accuracyFlags;
            try
            {
                accuracyFlags = JsonSerializer.Deserialize<List<AccuracyFlagItem>>(latest.AccuracyFlagsJson, jsonOptions) ?? new();
            }
            catch { accuracyFlags = new(); }

            List<PromptEvidenceItem> promptEvidence;
            try
            {
                promptEvidence = JsonSerializer.Deserialize<List<PromptEvidenceItem>>(latest.PromptEvidenceJson, jsonOptions) ?? new();
            }
            catch { promptEvidence = new(); }

            static string FormatDelta(int current, int? previousValue)
            {
                if (previousValue == null) return "0";
                var diff = current - previousValue.Value;
                if (diff > 0) return $"+{diff}";
                return diff.ToString();
            }

            var response = new BrandPulseResponse
            {
                HasData = true,
                BrandHealth = latest.BrandHealth,
                AiConfidence = latest.AiConfidence,
                MessagingConsistency = latest.MessagingConsistency,
                BrandTrust = latest.BrandTrust,
                HealthDelta = FormatDelta(latest.BrandHealth, previous?.BrandHealth),
                ConfidenceDelta = FormatDelta(latest.AiConfidence, previous?.AiConfidence),
                MessagingDelta = FormatDelta(latest.MessagingConsistency, previous?.MessagingConsistency),
                TrustDelta = FormatDelta(latest.BrandTrust, previous?.BrandTrust),
                MetricHistory = new MetricHistoryData
                {
                    Health = history.Select(h => h.BrandHealth).ToList(),
                    Confidence = history.Select(h => h.AiConfidence).ToList(),
                    Messaging = history.Select(h => h.MessagingConsistency).ToList(),
                    Trust = history.Select(h => h.BrandTrust).ToList()
                },
                SentimentMix = new SentimentMixData
                {
                    Positive = latest.SentimentPositive,
                    Neutral = latest.SentimentNeutral,
                    Negative = latest.SentimentNegative
                },
                ShareOfPerception = shareOfPerception,
                ModelInsights = modelInsights,
                Alerts = alerts,
                AccuracyFlags = accuracyFlags,
                PromptEvidence = promptEvidence,
                LastScanDate = latest.ScanDate.ToString("yyyy-MM-dd")
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ===================== Command Center (Overview) =====================

    /// <summary>
    /// Command Center (Overview): aggregates live KPIs computed on-the-fly from sibling feature
    /// scan tables (visibility, citation quality, brand health, share of voice), plus a periodically
    /// refreshed AI-generated narrative insights list. KPIs/breakdown/actionItems/alerts are always
    /// computed fresh from current sibling data; only the narrative "insights" array is cached and
    /// refreshed at most once every 7 days (or immediately, on first visit for a brand-new org).
    /// </summary>
    [HttpGet("command-center")]
    public async Task<IActionResult> GetCommandCenter(
        [FromServices] MediatR.IMediator mediator,
        [FromServices] ICommandCenterRepository commandCenterRepository,
        [FromQuery] Guid organizationId,
        [FromQuery] string range = "30d")
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");

        int rangeDays = range switch
        {
            "7d" => 7,
            "90d" => 90,
            "30d" => 30,
            _ => 30
        };

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var latestInsightsScanDate = await commandCenterRepository.GetLatestScanDateAsync(organizationId);
        var needsInsightsRefresh = latestInsightsScanDate == null
            || (today.DayNumber - latestInsightsScanDate.Value.DayNumber) >= 7;

        CommandCenterSiblingData siblingData;
        List<string> insights;

        if (needsInsightsRefresh)
        {
            var refreshResult = await mediator.Send(new RunCommandCenterInsightsCommand
            {
                OrganizationId = organizationId,
                RangeDays = rangeDays
            });

            if (refreshResult.Success && refreshResult.SiblingData != null)
            {
                siblingData = refreshResult.SiblingData;
                insights = refreshResult.Insights;
            }
            else
            {
                // AI/persist path failed unexpectedly - fall back to live sibling data + whatever insights are on file.
                siblingData = await commandCenterRepository.GetSiblingSnapshotsAsync(organizationId, rangeDays);
                insights = await commandCenterRepository.GetLatestInsightsAsync(organizationId);
            }
        }
        else
        {
            siblingData = await commandCenterRepository.GetSiblingSnapshotsAsync(organizationId, rangeDays);
            insights = await commandCenterRepository.GetLatestInsightsAsync(organizationId);
        }

        bool hasData = siblingData.AnyDataAvailable;

        static double Round1(double v) => Math.Round(v, 1);

        var kpis = new[]
        {
            new
            {
                Label = "AI Visibility",
                Val = Round1(siblingData.Visibility.Current),
                Suffix = "",
                Delta = Round1(siblingData.Visibility.Current - siblingData.Visibility.Previous),
                Spark = siblingData.Visibility.History
            },
            new
            {
                Label = "Citation Quality",
                Val = Round1(siblingData.CitationQuality.Current),
                Suffix = "",
                Delta = Round1(siblingData.CitationQuality.Current - siblingData.CitationQuality.Previous),
                Spark = siblingData.CitationQuality.History
            },
            new
            {
                Label = "Brand Health",
                Val = Round1(siblingData.BrandHealth.Current),
                Suffix = "",
                Delta = Round1(siblingData.BrandHealth.Current - siblingData.BrandHealth.Previous),
                Spark = siblingData.BrandHealth.History
            },
            new
            {
                Label = "Share of Voice",
                Val = Round1(siblingData.ShareOfVoice.Current),
                Suffix = "%",
                Delta = Round1(siblingData.ShareOfVoice.Current - siblingData.ShareOfVoice.Previous),
                Spark = siblingData.ShareOfVoice.History
            }
        };

        static string BreakdownInsight(double cur, double prev)
        {
            var diff = Math.Round(cur - prev, 1);
            return diff >= 0
                ? $"Up {Math.Abs(diff)} pts vs last scan"
                : $"Down {Math.Abs(diff)} pts vs last scan";
        }

        var breakdown = new[]
        {
            new
            {
                Name = "AI Visibility",
                Cur = Round1(siblingData.Visibility.Current),
                Prev = Round1(siblingData.Visibility.Previous),
                Insight = BreakdownInsight(siblingData.Visibility.Current, siblingData.Visibility.Previous)
            },
            new
            {
                Name = "Citation Quality",
                Cur = Round1(siblingData.CitationQuality.Current),
                Prev = Round1(siblingData.CitationQuality.Previous),
                Insight = BreakdownInsight(siblingData.CitationQuality.Current, siblingData.CitationQuality.Previous)
            },
            new
            {
                Name = "Brand Health",
                Cur = Round1(siblingData.BrandHealth.Current),
                Prev = Round1(siblingData.BrandHealth.Previous),
                Insight = BreakdownInsight(siblingData.BrandHealth.Current, siblingData.BrandHealth.Previous)
            },
            new
            {
                Name = "Share of Voice",
                Cur = Round1(siblingData.ShareOfVoice.Current),
                Prev = Round1(siblingData.ShareOfVoice.Previous),
                Insight = BreakdownInsight(siblingData.ShareOfVoice.Current, siblingData.ShareOfVoice.Previous)
            }
        };

        var actionItems = new List<object>();
        var alerts = new List<object>();

        void CheckMetric(string metricLabel, CommandCenterMetric metric, string link)
        {
            if (!metric.HasData) return;

            var drop = Math.Round(metric.Previous - metric.Current, 1);

            if (drop > 5)
            {
                actionItems.Add(new
                {
                    Source = metricLabel,
                    Title = $"{metricLabel} dropped {Math.Abs(drop)} pts",
                    Detail = $"{metricLabel} fell from {Round1(metric.Previous)} to {Round1(metric.Current)} since the last scan. Review the {metricLabel} dashboard for root causes.",
                    Severity = drop > 10 ? "high" : "medium",
                    Link = link
                });
            }

            if (drop > 10)
            {
                alerts.Add(new
                {
                    Title = $"{metricLabel} declined sharply",
                    Message = $"{metricLabel} dropped {Math.Abs(drop)} points this scan (from {Round1(metric.Previous)} to {Round1(metric.Current)}).",
                    Severity = "high"
                });
            }
            else if (drop > 5)
            {
                alerts.Add(new
                {
                    Title = $"{metricLabel} trending down",
                    Message = $"{metricLabel} dropped {Math.Abs(drop)} points this scan (from {Round1(metric.Previous)} to {Round1(metric.Current)}).",
                    Severity = "medium"
                });
            }
        }

        CheckMetric("AI Visibility", siblingData.Visibility, "/dashboard/visibility-radar");
        CheckMetric("Citation Quality", siblingData.CitationQuality, "/dashboard/citations");
        CheckMetric("Brand Health", siblingData.BrandHealth, "/dashboard/brand-pulse");
        CheckMetric("Share of Voice", siblingData.ShareOfVoice, "/dashboard/competitors");

        // Brand Pulse accuracy flags / risk alerts - deterministic, parsed defensively from raw JSON columns.
        if (!string.IsNullOrWhiteSpace(siblingData.BrandPulseAccuracyFlagsJson))
        {
            try
            {
                using var flagsDoc = JsonDocument.Parse(siblingData.BrandPulseAccuracyFlagsJson);
                if (flagsDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var flag in flagsDoc.RootElement.EnumerateArray())
                    {
                        string? severity = flag.TryGetProperty("severity", out var sevEl) ? sevEl.GetString() : null;
                        string? message = flag.TryGetProperty("message", out var msgEl) ? msgEl.GetString()
                            : (flag.TryGetProperty("description", out var descEl) ? descEl.GetString() : null);

                        if (!string.IsNullOrEmpty(severity) && severity.Equals("high", StringComparison.OrdinalIgnoreCase))
                        {
                            actionItems.Add(new
                            {
                                Source = "Brand Health",
                                Title = "Brand accuracy flag detected",
                                Detail = message ?? "A high-severity accuracy flag was raised on your Brand Pulse scan.",
                                Severity = "high",
                                Link = "/dashboard/brand-pulse"
                            });
                        }
                    }
                }
            }
            catch { /* malformed/legacy JSON shape - ignore defensively */ }
        }

        if (!string.IsNullOrWhiteSpace(siblingData.BrandPulseAlertsJson))
        {
            try
            {
                using var alertsDoc = JsonDocument.Parse(siblingData.BrandPulseAlertsJson);
                if (alertsDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var riskAlert in alertsDoc.RootElement.EnumerateArray())
                    {
                        string? severity = riskAlert.TryGetProperty("severity", out var sevEl) ? sevEl.GetString() : null;
                        string? title = riskAlert.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
                        string? message = riskAlert.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;

                        if (!string.IsNullOrEmpty(severity) &&
                            (severity.Equals("high", StringComparison.OrdinalIgnoreCase) || severity.Equals("risk", StringComparison.OrdinalIgnoreCase)))
                        {
                            alerts.Add(new
                            {
                                Title = title ?? "Brand risk alert",
                                Message = message ?? "A risk-level alert was raised on your Brand Pulse scan.",
                                Severity = "high"
                            });
                        }
                    }
                }
            }
            catch { /* malformed/legacy JSON shape - ignore defensively */ }
        }

        var latestDataScanDate = new[]
            {
                siblingData.Visibility.LatestScanDate,
                siblingData.CitationQuality.LatestScanDate,
                siblingData.BrandHealth.LatestScanDate,
                siblingData.ShareOfVoice.LatestScanDate
            }
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .DefaultIfEmpty(today)
            .Max();

        var response = new
        {
            HasData = hasData,
            LastScanDate = latestDataScanDate.ToString("yyyy-MM-dd"),
            Kpis = kpis,
            Breakdown = breakdown,
            ActionItems = actionItems,
            Alerts = alerts,
            Insights = insights
        };

        return Ok(response);
    }

    // ===================== Opportunity Finder =====================

    /// <summary>
    /// Returns the Opportunity Finder dashboard payload. If the organization has never had an
    /// opportunity scan at all (zero rows), this runs a ONE-TIME bootstrap scan so first-time
    /// visitors see data immediately. It never re-scans just because existing data is stale —
    /// staleness only gates whether POST /opportunity-finder/deep-scan is allowed.
    /// </summary>
    [HttpGet("opportunity-finder")]
    public async Task<IActionResult> GetOpportunityFinder([FromQuery] Guid organizationId, [FromQuery] string range = "30d")
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");

        var existingCount = await _opportunitySnapshotRepository.GetOpportunityCountAsync(organizationId);

        if (existingCount == 0)
        {
            var bootstrapResult = await _mediator.Send(new RunOpportunityScanCommand { OrganizationId = organizationId });
            if (!bootstrapResult.Success)
            {
                return BadRequest(new { error = bootstrapResult.Error ?? "Failed to run the initial opportunity scan." });
            }
        }

        var response = await BuildOpportunityFinderResponseAsync(organizationId, range);
        return Ok(response);
    }

    /// <summary>
    /// User-triggered refresh of the Opportunity Finder data. Enforces a 7-day cooldown
    /// server-side (authoritative — never trust a client-side disabled button alone).
    /// </summary>
    [HttpPost("opportunity-finder/deep-scan")]
    public async Task<IActionResult> RunOpportunityDeepScan([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");

        var lastScanDate = await _opportunitySnapshotRepository.GetLatestScanDateAsync(organizationId);

        if (lastScanDate.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var daysSinceLastScan = today.DayNumber - lastScanDate.Value.DayNumber;

            if (daysSinceLastScan < 7)
            {
                var nextEligible = lastScanDate.Value.AddDays(7);
                return BadRequest(new
                {
                    error = "A deep scan was already run within the last 7 days. Please wait until the cooldown expires.",
                    nextEligibleDate = nextEligible.ToString("yyyy-MM-dd")
                });
            }
        }

        var scanResult = await _mediator.Send(new RunOpportunityScanCommand { OrganizationId = organizationId });
        if (!scanResult.Success)
        {
            return BadRequest(new { error = scanResult.Error ?? "Failed to run the opportunity deep scan." });
        }

        var response = await BuildOpportunityFinderResponseAsync(organizationId, "30d");
        return Ok(response);
    }

    /// <summary>
    /// Builds the full OpportunityFinderResponse shape shared by both the GET bootstrap path and
    /// the POST deep-scan path, reading whatever is currently the latest persisted scan.
    /// </summary>
    private async Task<object> BuildOpportunityFinderResponseAsync(Guid organizationId, string range)
    {
        var days = ParseOpportunityRangeDays(range);

        var lastScanDate = await _opportunitySnapshotRepository.GetLatestScanDateAsync(organizationId);
        var latestOpportunities = (await _opportunitySnapshotRepository.GetLatestOpportunitiesAsync(organizationId)).ToList();
        var trendAggregates = (await _opportunitySnapshotRepository.GetHistoricalAggregatesAsync(organizationId, days)).ToList();

        var opportunityDtos = latestOpportunities.Select(o =>
        {
            var difficulty = ComputeOpportunityDifficulty(o.Effort);
            var quadrant = ComputeOpportunityQuadrant(o.Score, o.Effort);
            var priority = ComputeOpportunityPriority(o.Score, o.Effort);
            var badge = ComputeOpportunityBadge(quadrant);

            List<string> checklist;
            try
            {
                checklist = System.Text.Json.JsonSerializer.Deserialize<List<string>>(o.ChecklistJson ?? "[]")
                    ?? new List<string>();
            }
            catch
            {
                checklist = new List<string>();
            }

            return new
            {
                id = o.OpportunityKey,
                category = o.Category,
                title = o.Title,
                summary = o.Summary ?? string.Empty,
                whyItMatters = o.WhyItMatters ?? string.Empty,
                score = o.Score,
                effort = o.Effort,
                difficulty,
                estimatedGainPct = o.EstimatedGainPct,
                eta = o.Eta ?? string.Empty,
                competitorContext = o.CompetitorContext ?? string.Empty,
                checklist,
                quadrant,
                priority,
                badge
            };
        }).ToList();

        var totalOpportunities = opportunityDtos.Count;
        var estimatedImpactScore = totalOpportunities > 0 ? latestOpportunities.Average(o => o.Score) : 0;
        var criticalCount = opportunityDtos.Count(o => o.priority == "Critical");
        var quickWinsCount = opportunityDtos.Count(o => o.badge == "Quick Win");

        // Forecast's potentialGainPct is the average EstimatedGainPct across the latest scan's
        // opportunities — a representative "typical uplift" figure rather than a sum, since summing
        // percentage-gain estimates across many opportunities would overstate compounding impact.
        var potentialGainPct = totalOpportunities > 0 ? latestOpportunities.Average(o => o.EstimatedGainPct) : 0;

        var trend = trendAggregates.Select(t => new
        {
            date = t.ScanDate.ToString("yyyy-MM-dd"),
            avgScore = t.AvgScore,
            count = t.Count
        }).ToList();

        bool canRunDeepScan;
        string nextEligibleDate;
        int daysUntilEligible;

        if (!lastScanDate.HasValue)
        {
            canRunDeepScan = true;
            nextEligibleDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
            daysUntilEligible = 0;
        }
        else
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var nextEligible = lastScanDate.Value.AddDays(7);
            var remainingDays = nextEligible.DayNumber - today.DayNumber;
            canRunDeepScan = remainingDays <= 0;
            nextEligibleDate = nextEligible.ToString("yyyy-MM-dd");
            daysUntilEligible = Math.Max(0, remainingDays);
        }

        return new
        {
            hasData = lastScanDate.HasValue,
            lastScanDate = lastScanDate.HasValue ? lastScanDate.Value.ToString("yyyy-MM-dd") : string.Empty,
            canRunDeepScan,
            nextEligibleDate,
            daysUntilEligible,
            opportunities = opportunityDtos,
            kpis = new
            {
                totalOpportunities,
                estimatedImpactScore,
                criticalCount,
                quickWinsCount
            },
            forecast = new
            {
                potentialGainPct,
                trend
            }
        };
    }

    private static int ParseOpportunityRangeDays(string? range) => range switch
    {
        "7d" => 7,
        "30d" => 30,
        "90d" => 90,
        _ => 30
    };

    private static string ComputeOpportunityDifficulty(int effort) =>
        effort < 34 ? "Low" : effort < 67 ? "Medium" : "High";

    private static string ComputeOpportunityQuadrant(int score, int effort) =>
        score >= 50 && effort < 50 ? "high-low" :
        score >= 50 && effort >= 50 ? "high-high" :
        score < 50 && effort < 50 ? "low-low" : "low-high";

    private static string ComputeOpportunityPriority(int score, int effort) =>
        score >= 80 && effort < 50 ? "Critical" :
        score >= 60 ? "High" :
        score >= 40 ? "Medium" : "Low";

    private static string ComputeOpportunityBadge(string quadrant) => quadrant switch
    {
        "high-low" => "Quick Win",
        "high-high" => "Big Bet",
        "low-low" => "Fill-in",
        _ => "Reconsider"
    };

    // ===================== Competitor Watch =====================
    // Dependencies required by these actions (must be added to the shared constructor):
    //   - MediatR.IMediator                                    -> field: _mediator
    //   - Citationly.Application.Interfaces.ICompetitorSnapshotRepository -> field: _competitorSnapshotRepository

    [HttpGet("competitor-watch")]
    public async Task<IActionResult> GetCompetitorWatch([FromQuery] Guid organizationId, [FromQuery] string range = "30d")
    {
        if (organizationId == Guid.Empty)
        {
            return Ok(new CompetitorWatchResponse { You = null, Comps = new List<CompetitorWatchItem>() });
        }

        try
        {
            var days = ParseCompetitorWatchRangeDays(range);

            var latestScanDate = await _competitorSnapshotRepository.GetLatestScanDateAsync(organizationId);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var needsBootstrap = latestScanDate == null;
            var isStale = latestScanDate != null && (today.DayNumber - latestScanDate.Value.DayNumber) >= 7;

            if (needsBootstrap || isStale)
            {
                // Auto-refresh weekly (or bootstrap on first-ever call). Failure here should not break the read —
                // fall through and build the response from whatever data exists (possibly none).
                await _mediator.Send(new RunCompetitorSnapshotCommand { OrganizationId = organizationId });
            }

            var response = await BuildCompetitorWatchResponseAsync(organizationId, days);
            return Ok(response);
        }
        catch (Exception)
        {
            // Never throw for a dashboard read — the frontend's empty state already handles `you: null`.
            return Ok(new CompetitorWatchResponse { You = null, Comps = new List<CompetitorWatchItem>() });
        }
    }

    [HttpPost("competitor-watch/rescan")]
    public async Task<IActionResult> RescanCompetitorWatch([FromQuery] Guid organizationId, [FromQuery] string range = "30d")
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");

        // Manual override: always re-runs, regardless of staleness — no cooldown.
        var result = await _mediator.Send(new RunCompetitorSnapshotCommand { OrganizationId = organizationId });
        if (!result.Success) return BadRequest(result.Error);

        var days = ParseCompetitorWatchRangeDays(range);
        var response = await BuildCompetitorWatchResponseAsync(organizationId, days);
        return Ok(response);
    }

    private static int ParseCompetitorWatchRangeDays(string? range) => range switch
    {
        "7d" => 7,
        "30d" => 30,
        "90d" => 90,
        _ => 30
    };

    // Fixed 6-color palette (indigo/violet/blue/emerald/amber/rose), indexed deterministically by Rank-1 mod 6.
    private static readonly string[] CompetitorWatchColorPalette = new[]
    {
        "#6366F1", // indigo
        "#8B5CF6", // violet
        "#3B82F6", // blue
        "#10B981", // emerald
        "#F59E0B", // amber
        "#F43F5E"  // rose
    };

    private async Task<CompetitorWatchResponse> BuildCompetitorWatchResponseAsync(Guid organizationId, int days)
    {
        var snapshots = (await _competitorSnapshotRepository.GetLatestSnapshotsAsync(organizationId))
            .OrderBy(s => s.Rank)
            .ToList();

        if (!snapshots.Any())
        {
            return new CompetitorWatchResponse { You = null, Comps = new List<CompetitorWatchItem>() };
        }

        var items = new List<CompetitorWatchItem>();

        foreach (var snapshot in snapshots)
        {
            var trend = await _competitorSnapshotRepository.GetTrendAsync(organizationId, snapshot.CompetitorId, snapshot.IsYou, days);

            Dictionary<string, int> models;
            try
            {
                models = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(
                    string.IsNullOrWhiteSpace(snapshot.ModelsJson) ? "{}" : snapshot.ModelsJson)
                    ?? new Dictionary<string, int>();
            }
            catch
            {
                models = new Dictionary<string, int>();
            }

            var paletteIndex = ((snapshot.Rank - 1) % CompetitorWatchColorPalette.Length + CompetitorWatchColorPalette.Length)
                                % CompetitorWatchColorPalette.Length;

            items.Add(new CompetitorWatchItem
            {
                Id = snapshot.Id.ToString(),
                Rank = snapshot.Rank,
                Name = snapshot.Name,
                Tagline = snapshot.Tagline ?? string.Empty,
                WebsiteUrl = snapshot.WebsiteUrl ?? string.Empty,
                Logo = string.IsNullOrEmpty(snapshot.Name) ? "?" : snapshot.Name.Substring(0, 1).ToUpperInvariant(),
                Color = CompetitorWatchColorPalette[paletteIndex],
                You = snapshot.IsYou,
                Sov = snapshot.ShareOfVoice,
                SovChg = snapshot.ShareOfVoiceChange,
                Vis = snapshot.Visibility,
                VisChg = snapshot.VisibilityChange,
                Threat = snapshot.Threat,
                Trend = trend,
                Citations = new CompetitorWatchCitations { Share = snapshot.CitationsShare, Total = snapshot.CitationsTotal },
                Content = new CompetitorWatchContent { Velocity = snapshot.ContentVelocity },
                Models = models
            });
        }

        var youItem = items.FirstOrDefault(i => i.You);
        var comps = items.Where(i => !i.You).ToList();

        return new CompetitorWatchResponse
        {
            You = youItem,
            Comps = comps
        };
    }

    // ===================== GEO Dashboard =====================

    /// <summary>
    /// GEO Dashboard aggregate view: scorecards, trend, share of voice, composite header,
    /// pillars and insight callouts. Pure read — never triggers a new scan; ExecutiveSummaryData
    /// and HistoricalScan rows are populated elsewhere by the Onboarding analysis pipeline.
    /// Route is lowercase "geo-dashboard" to match the frontend's /dashboard/geo-dashboard call
    /// in dashboardApi.ts; ASP.NET Core routing is case-insensitive by default so this also
    /// answers to /Dashboard/geo-dashboard, but the literal casing is confirmed to line up.
    /// </summary>
    [HttpGet("geo-dashboard")]
    public async Task<IActionResult> GetGeoDashboard([FromQuery] Guid organizationId, [FromQuery] string range = "30d")
    {
        var result = await _geoDashboardAggregator.BuildAsync(organizationId, range);
        return Ok(result);
    }
}
