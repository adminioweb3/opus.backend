using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Competitors;
using Citationly.API.Services;

namespace Citationly.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[Authorize]
public class CompetitorController : ControllerBase
{
    private readonly ICompetitorRankingService _rankingService;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly ICompetitorCacheService _cacheService;
    private readonly ICurrentOrganizationAccessor _currentOrganization;

    public CompetitorController(
        ICompetitorRankingService rankingService,
        IWebsiteRepository websiteRepository,
        ICompetitorCacheService cacheService,
        ICurrentOrganizationAccessor currentOrganization)
    {
        _rankingService = rankingService;
        _websiteRepository = websiteRepository;
        _cacheService = cacheService;
        _currentOrganization = currentOrganization;
    }

    /// <summary>
    /// Returns the full competitive ranking dashboard with chart data.
    /// </summary>
    [HttpGet("rankings")]
    public async Task<IActionResult> GetRankings()
    {
        try
        {
            var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
            if (organizationId is null) return Unauthorized();

            var result = await _rankingService.ComputeRankingsAsync(organizationId.Value, HttpContext.RequestAborted);
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
    [HttpGet("enrichment-status")]
    public async Task<IActionResult> GetEnrichmentStatus()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized();

        var competitors = await _websiteRepository.GetCompetitorsAsync(organizationId.Value);
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
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized();

        await _cacheService.InvalidateCacheAsync(organizationId.Value, HttpContext.RequestAborted);
        return Ok(new { message = "Cache invalidated. Run analyze-competitors again to re-discover." });
    }
}
