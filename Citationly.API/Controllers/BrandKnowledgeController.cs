using Citationly.API.Services;
using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BrandKnowledgeController : ControllerBase
{
    private readonly ICurrentOrganizationAccessor _currentOrganization;
    private readonly IBrandKnowledgeService _brandKnowledgeService;
    private readonly ICrossEngineConsensusService _consensusService;

    public BrandKnowledgeController(
        ICurrentOrganizationAccessor currentOrganization,
        IBrandKnowledgeService brandKnowledgeService,
        ICrossEngineConsensusService consensusService)
    {
        _currentOrganization = currentOrganization;
        _brandKnowledgeService = brandKnowledgeService;
        _consensusService = consensusService;
    }

    [HttpGet("fact-accuracy")]
    public async Task<IActionResult> GetFactAccuracy([FromQuery] int lookbackDays = 30)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var result = await _brandKnowledgeService.GetAsync(orgId.Value, lookbackDays, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpPost("fact-accuracy/refresh")]
    public async Task<IActionResult> RefreshFactAccuracy([FromQuery] int lookbackDays = 30)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var result = await _brandKnowledgeService.RefreshAsync(orgId.Value, lookbackDays, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpGet("consensus")]
    public async Task<IActionResult> GetConsensus([FromQuery] int lookbackDays = 30)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var result = await _consensusService.GetAsync(orgId.Value, lookbackDays, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpPost("consensus/refresh")]
    public async Task<IActionResult> RefreshConsensus([FromQuery] int lookbackDays = 30)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var result = await _consensusService.RefreshAsync(orgId.Value, lookbackDays, HttpContext.RequestAborted);
        return Ok(result);
    }

    private async Task<Guid?> GetOrganizationIdAsync()
    {
        return await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
    }
}
