using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Competitors;

namespace Citationly.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class CompetitorController : ControllerBase
{
    private readonly ICompetitorRankingService _rankingService;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly ICompetitorCacheService _cacheService;

    public CompetitorController(
        ICompetitorRankingService rankingService,
        IWebsiteRepository websiteRepository,
        ICompetitorCacheService cacheService)
    {
        _rankingService = rankingService;
        _websiteRepository = websiteRepository;
        _cacheService = cacheService;
    }

    /// <summary>
    /// Returns the full competitive ranking dashboard with chart data.
    /// </summary>
    [HttpGet("{organizationId}/rankings")]
    public async Task<IActionResult> GetRankings(Guid organizationId)
    {
        try
        {
            var result = await _rankingService.ComputeRankingsAsync(organizationId, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Returns the enrichment status for all competitors of an organization.
    /// </summary>
    [HttpGet("{organizationId}/enrichment-status")]
    public async Task<IActionResult> GetEnrichmentStatus(Guid organizationId)
    {
        var competitors = await _websiteRepository.GetCompetitorsAsync(organizationId);
        var list = competitors.ToList();

        var result = new
        {
            Total = list.Count,
            Completed = list.Count(c => c.EnrichmentStatus == "Completed"),
            InProgress = list.Count(c => c.EnrichmentStatus == "InProgress"),
            Pending = list.Count(c => c.EnrichmentStatus == "Pending"),
            Failed = list.Count(c => c.EnrichmentStatus == "Failed"),
            IsComplete = list.All(c => c.EnrichmentStatus == "Completed" || c.EnrichmentStatus == "Pending")
                         && list.Any(c => c.EnrichmentStatus == "Completed"),
            Competitors = list.Select(c => new
            {
                c.Id,
                c.Name,
                c.WebsiteUrl,
                c.EnrichmentStatus,
                c.EnrichedAt,
                c.SimilarityScore,
                c.CompetitorType
            })
        };

        return Ok(result);
    }

    /// <summary>
    /// Force re-discovery and re-enrichment for an organization.
    /// </summary>
    [HttpPost("{organizationId}/refresh")]
    public async Task<IActionResult> Refresh(Guid organizationId)
    {
        await _cacheService.InvalidateCacheAsync(organizationId, HttpContext.RequestAborted);
        return Ok(new { message = "Cache invalidated. Run analyze-competitors again to re-discover." });
    }
}
