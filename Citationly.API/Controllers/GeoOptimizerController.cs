using System.Threading.Tasks;
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

    public GeoOptimizerController(IGeoOptimizerService geoOptimizerService)
    {
        _geoOptimizerService = geoOptimizerService;
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] GeoOptimizationRequest request)
    {
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
            var result = await _geoOptimizerService.AnalyzeAsync(request);
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
        if (string.IsNullOrWhiteSpace(request.SchemaType))
        {
            return BadRequest("Schema type is required.");
        }

        try
        {
            var result = await _geoOptimizerService.GenerateSchemaAsync(request);
            return Ok(result);
        }
        catch (System.Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }
}
