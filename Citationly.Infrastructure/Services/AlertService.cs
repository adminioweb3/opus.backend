using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Citationly.Infrastructure.Services;

public class AlertService : IAlertService
{
    private readonly IAiVisibilityRepository _visibilityRepository;
    private readonly ICompetitorSnapshotRepository _competitorSnapshotRepository;
    private readonly IVisibilitySnapshotRepository _visibilitySnapshotRepository;
    private readonly IAlertRepository _alertRepository;
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        IAiVisibilityRepository visibilityRepository,
        ICompetitorSnapshotRepository competitorSnapshotRepository,
        IVisibilitySnapshotRepository visibilitySnapshotRepository,
        IAlertRepository alertRepository,
        IUserRepository userRepository,
        IConfiguration configuration,
        ILogger<AlertService> logger)
    {
        _visibilityRepository = visibilityRepository;
        _competitorSnapshotRepository = competitorSnapshotRepository;
        _visibilitySnapshotRepository = visibilitySnapshotRepository;
        _alertRepository = alertRepository;
        _userRepository = userRepository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<int> GenerateCommandCenterAlertsAsync(Guid organizationId, CancellationToken ct = default)
    {
        var created = 0;
        var thresholds = (await _alertRepository.GetThresholdsAsync(organizationId))
            .ToDictionary(t => t.AlertType, StringComparer.OrdinalIgnoreCase);

        var scans = (await _visibilityRepository.GetHistoricalScansByOrgAsync(organizationId))
            .OrderBy(s => s.ScanDate)
            .ToList();
        if (scans.Count >= 2)
        {
            created += await AddRegressionAlertAsync(organizationId, thresholds, scans, "visibility_drop", "Visibility score", s => s.VisibilityScore, "/dashboard/command-center");
            created += await AddRegressionAlertAsync(organizationId, thresholds, scans, "citation_drop", "Citation score", s => s.CitationScore, "/dashboard/citation-intelligence");
            created += await AddRegressionAlertAsync(organizationId, thresholds, scans, "geo_drop", "GEO readiness", s => s.GeoReadiness, "/dashboard/geo-dashboard");
        }

        var competitorScanDate = await _competitorSnapshotRepository.GetLatestScanDateAsync(organizationId);
        if (competitorScanDate.HasValue)
        {
            var competitors = await _competitorSnapshotRepository.GetSnapshotsByScanDateAsync(organizationId, competitorScanDate.Value);
            foreach (var competitor in competitors.Where(c => !c.IsYou && c.Threat == "high").Take(3))
            {
                var alert = new Alert
                {
                    OrganizationId = organizationId,
                    DedupKey = $"competitor-high-threat:{competitorScanDate:yyyy-MM-dd}:{competitor.Name.ToLowerInvariant()}",
                    Type = "competitor_overtake",
                    Title = $"{competitor.Name} is a high threat",
                    Message = $"Rank #{competitor.Rank}, share of voice {competitor.ShareOfVoice}%.",
                    Severity = "High",
                    Source = "Competitor Watch",
                    ActionUrl = "/dashboard/competitor-watch",
                    EvidenceJson = JsonSerializer.Serialize(new { competitor.Name, competitor.Rank, competitor.ShareOfVoice, competitor.ScanDate })
                };
                if (await _alertRepository.UpsertAlertAsync(alert) != null) created++;
            }
        }

        var visibilityScanDate = await _visibilitySnapshotRepository.GetLatestScanDateAsync(organizationId);
        if (visibilityScanDate.HasValue)
        {
            var platforms = await _visibilitySnapshotRepository.GetPlatformSnapshotsByScanDateAsync(organizationId, visibilityScanDate.Value);
            foreach (var platform in platforms.Where(p => p.Status == "Weak").Take(3))
            {
                var alert = new Alert
                {
                    OrganizationId = organizationId,
                    DedupKey = $"weak-platform:{visibilityScanDate:yyyy-MM-dd}:{platform.Platform.ToLowerInvariant()}",
                    Type = "weak_platform",
                    Title = $"{platform.Platform} visibility is weak",
                    Message = $"Score {platform.Score}/100 with {platform.Citations} citation signal(s).",
                    Severity = "Medium",
                    Source = "Visibility Radar",
                    ActionUrl = "/dashboard/visibility-radar",
                    EvidenceJson = JsonSerializer.Serialize(new { platform.Platform, platform.Score, platform.Citations, platform.ScanDate })
                };
                if (await _alertRepository.UpsertAlertAsync(alert) != null) created++;
            }
        }

        return created;
    }

    public async Task<int> DeliverPendingAlertsAsync(CancellationToken ct = default)
    {
        var pending = (await _alertRepository.GetPendingDeliveriesAsync()).ToList();
        var delivered = 0;
        foreach (var alert in pending)
        {
            ct.ThrowIfCancellationRequested();
            var status = await TryDeliverEmailAsync(alert, ct);
            await _alertRepository.MarkDeliveryAsync(alert.Id, status, status == "Delivered" ? DateTime.UtcNow : null);
            if (status == "Delivered") delivered++;
        }

        return delivered;
    }

    private async Task<int> AddRegressionAlertAsync(
        Guid organizationId,
        Dictionary<string, AlertThreshold> thresholds,
        List<HistoricalScan> scans,
        string type,
        string label,
        Func<HistoricalScan, int> selector,
        string actionUrl)
    {
        var latest = scans[^1];
        var previous = scans[^2];
        var current = selector(latest);
        var prior = selector(previous);
        var drop = prior - current;
        var threshold = thresholds.TryGetValue(type, out var configured) ? configured.ThresholdValue : 5;
        var isRollingAnomaly = IsRollingAnomaly(scans.Take(scans.Count - 1).Select(selector).ToList(), current);

        if (drop < threshold && !isRollingAnomaly)
        {
            return 0;
        }

        var alert = new Alert
        {
            OrganizationId = organizationId,
            DedupKey = $"{type}:{latest.ScanDate:yyyy-MM-dd}:{prior}->{current}",
            Type = type,
            Title = $"{label} dropped",
            Message = isRollingAnomaly
                ? $"{label} fell to {current}, which is statistically unusual against the recent baseline."
                : $"{label} fell from {prior} to {current}.",
            Severity = drop >= threshold * 2 || isRollingAnomaly ? "High" : "Medium",
            Source = "Command Center",
            ActionUrl = actionUrl,
            EvidenceJson = JsonSerializer.Serialize(new { latest.ScanDate, previous = prior, current, drop, threshold, isRollingAnomaly })
        };

        return await _alertRepository.UpsertAlertAsync(alert) == null ? 0 : 1;
    }

    private static bool IsRollingAnomaly(IReadOnlyList<int> previousValues, int current)
    {
        if (previousValues.Count < 5) return false;
        var mean = previousValues.Average();
        var variance = previousValues.Sum(v => Math.Pow(v - mean, 2)) / previousValues.Count;
        var stdDev = Math.Sqrt(variance);
        if (stdDev < 1) return false;
        var z = (current - mean) / stdDev;
        return z <= -2.0;
    }

    private async Task<string> TryDeliverEmailAsync(Alert alert, CancellationToken ct)
    {
        var host = _configuration["Smtp:Host"];
        var from = _configuration["Smtp:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
        {
            return "NotConfigured";
        }

        try
        {
            var recipients = (await _userRepository.GetOrganizationUserEmailsAsync(alert.OrganizationId))
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct()
                .ToList();
            if (recipients.Count == 0) return "NoRecipients";

            using var client = new SmtpClient(host)
            {
                Port = int.TryParse(_configuration["Smtp:Port"], out var port) ? port : 587,
                EnableSsl = bool.TryParse(_configuration["Smtp:EnableSsl"], out var ssl) ? ssl : true
            };

            var username = _configuration["Smtp:Username"];
            var password = _configuration["Smtp:Password"];
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            using var message = new MailMessage { From = new MailAddress(from), Subject = $"Citationly alert: {alert.Title}", Body = $"{alert.Message}\n\nOpen: {alert.ActionUrl}" };
            foreach (var recipient in recipients) message.To.Add(recipient);
            await client.SendMailAsync(message, ct);
            return "Delivered";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver alert {AlertId}", alert.Id);
            return "Failed";
        }
    }
}
