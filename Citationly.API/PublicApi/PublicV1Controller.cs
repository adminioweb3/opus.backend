using Citationly.API.Services;
using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.PublicApi;

[ApiController]
[Route("api/public/v1")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
public class PublicV1Controller : ControllerBase
{
    private const string PublicApiMetric = "public_api_calls_per_day";

    private readonly IPromptIntelligenceRepository _promptRepository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IAlertRepository _alertRepository;
    private readonly IBrandKnowledgeService _brandKnowledgeService;
    private readonly IEntitlementService _entitlements;

    public PublicV1Controller(
        IPromptIntelligenceRepository promptRepository,
        IWebsiteRepository websiteRepository,
        IAlertRepository alertRepository,
        IBrandKnowledgeService brandKnowledgeService,
        IEntitlementService entitlements)
    {
        _promptRepository = promptRepository;
        _websiteRepository = websiteRepository;
        _alertRepository = alertRepository;
        _brandKnowledgeService = brandKnowledgeService;
        _entitlements = entitlements;
    }

    [HttpGet("visibility")]
    public async Task<IActionResult> GetVisibility([FromQuery] int days = 30)
    {
        var orgId = await AuthorizeAndMeterAsync();
        if (orgId.Result != null) return orgId.Result;

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
        var rows = (await _promptRepository.GetVisibilitySummaryDataAsync(orgId.OrganizationId, since)).ToList();
        return Ok(new
        {
            provenance = "derived_from_real_prompt_visibility_rows",
            hasData = rows.Count > 0,
            averageVisibility = rows.Count == 0 ? 0 : (int)Math.Round(rows.Average(r => r.OverallVisibilityScore)),
            averageShareOfVoice = rows.Count == 0 ? 0 : (int)Math.Round(rows.Average(r => r.ShareOfVoice)),
            promptCount = rows.Select(r => r.QuestionId).Distinct().Count(),
            rows
        });
    }

    [HttpGet("competitors")]
    public async Task<IActionResult> GetCompetitors()
    {
        var orgId = await AuthorizeAndMeterAsync();
        if (orgId.Result != null) return orgId.Result;

        var competitors = await _websiteRepository.GetCompetitorsAsync(orgId.OrganizationId);
        return Ok(new { provenance = "derived_from_company_competitor_graph_with_legacy_fallback", competitors });
    }

    [HttpGet("citations")]
    public async Task<IActionResult> GetCitations([FromQuery] int days = 30)
    {
        var orgId = await AuthorizeAndMeterAsync();
        if (orgId.Result != null) return orgId.Result;

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
        var rows = await _promptRepository.GetCitationSummaryDataAsync(orgId.OrganizationId, since);
        return Ok(new { provenance = "extracted_from_stored_prompt_responses", citations = rows });
    }

    [HttpGet("recommendations")]
    public async Task<IActionResult> GetRecommendations()
    {
        var orgId = await AuthorizeAndMeterAsync();
        if (orgId.Result != null) return orgId.Result;

        var recommendations = await _websiteRepository.GetGeoRecommendationsAsync(orgId.OrganizationId);
        return Ok(new { provenance = "evidence_linked_geo_recommendations", recommendations });
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts([FromQuery] int limit = 50)
    {
        var orgId = await AuthorizeAndMeterAsync();
        if (orgId.Result != null) return orgId.Result;

        var alerts = await _alertRepository.GetAlertsAsync(orgId.OrganizationId, limit);
        return Ok(new { provenance = "persisted_deduplicated_alerts", alerts });
    }

    [HttpGet("brand-facts")]
    public async Task<IActionResult> GetBrandFacts([FromQuery] int days = 30)
    {
        var orgId = await AuthorizeAndMeterAsync();
        if (orgId.Result != null) return orgId.Result;

        var facts = await _brandKnowledgeService.GetAsync(orgId.OrganizationId, days, HttpContext.RequestAborted);
        return Ok(new { provenance = "claims_extracted_from_real_prompt_responses_compared_to_verified_profile", facts });
    }

    private async Task<(Guid OrganizationId, IActionResult? Result)> AuthorizeAndMeterAsync()
    {
        var orgClaim = User.FindFirst("organization_id")?.Value;
        if (!Guid.TryParse(orgClaim, out var organizationId))
        {
            return (Guid.Empty, Unauthorized(new { error = "Invalid API key organization scope." }));
        }

        if (!await _entitlements.CanUseFeatureAsync(organizationId, PublicApiMetric, HttpContext.RequestAborted))
        {
            return (organizationId, StatusCode(403, new { error = "Public API access is not enabled for this plan." }));
        }

        var quota = await _entitlements.TryConsumeUsageAsync(organizationId, PublicApiMetric, 1, HttpContext.RequestAborted);
        if (!quota.IsWithinLimit)
        {
            return (organizationId, StatusCode(429, new { error = "Public API daily quota exceeded.", quota.CurrentUsage, quota.Limit }));
        }

        return (organizationId, null);
    }
}
