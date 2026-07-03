using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IAiVisibilityRepository _visibilityRepository;

    public DashboardController(IAiVisibilityRepository visibilityRepository)
    {
        _visibilityRepository = visibilityRepository;
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
}
