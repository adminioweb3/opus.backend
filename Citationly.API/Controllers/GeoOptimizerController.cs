using System.Threading.Tasks;
using Citationly.API.Services;
using Citationly.Application.Features.GeoOptimizer;
using Citationly.Application.Interfaces.GeoOptimizer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GeoOptimizerController : ControllerBase
{
    private readonly IGeoOptimizerService _geoOptimizerService;
    private readonly ICurrentOrganizationAccessor _currentOrganization;

    public GeoOptimizerController(IGeoOptimizerService geoOptimizerService, ICurrentOrganizationAccessor currentOrganization)
    {
        _geoOptimizerService = geoOptimizerService;
        _currentOrganization = currentOrganization;
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] GeoOptimizationRequest request)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized("User not found or unlinked.");

        if (string.IsNullOrWhiteSpace(request.TargetKeyword))
        {
            return BadRequest("Target keyword is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Engine))
        {
            return BadRequest("Target engine is required.");
        }

        try
        {
            var result = await _geoOptimizerService.AnalyzeAsync(organizationId.Value, request);
            return Ok(result);
        }
        catch (System.Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("generate-schema")]
    public async Task<IActionResult> GenerateSchema([FromBody] SchemaGenerationRequest request)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized("User not found or unlinked.");

        if (string.IsNullOrWhiteSpace(request.SchemaType))
        {
            return BadRequest("Schema type is required.");
        }

        try
        {
            var result = await _geoOptimizerService.GenerateSchemaAsync(organizationId.Value, request);
            return Ok(result);
        }
        catch (System.Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }
}
