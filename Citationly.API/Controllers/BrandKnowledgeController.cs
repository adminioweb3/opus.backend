using System.Security.Claims;
using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BrandKnowledgeController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly IBrandKnowledgeService _brandKnowledgeService;
    private readonly ICrossEngineConsensusService _consensusService;

    public BrandKnowledgeController(
        IUserRepository userRepository,
        IBrandKnowledgeService brandKnowledgeService,
        ICrossEngineConsensusService consensusService)
    {
        _userRepository = userRepository;
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
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(firebaseUid)) return null;

        var user = await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
        return user?.OrganizationId;
    }
}
