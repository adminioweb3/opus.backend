using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.AnswerSimulator;
using Citationly.Application.Interfaces.AnswerSimulator;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AnswerSimulatorController : ControllerBase
{
    private readonly IAnswerSimulatorService _service;

    public AnswerSimulatorController(IAnswerSimulatorService service)
    {
        _service = service;
    }

    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate([FromBody] SimulateAnswerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { message = "A question/prompt is required." });

        try
        {
            return Ok(await _service.SimulateAsync(request));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("compare")]
    public async Task<IActionResult> Compare([FromBody] CompareContentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt) || string.IsNullOrWhiteSpace(request.PageContent))
            return BadRequest(new { message = "Prompt and page content are required." });

        try
        {
            return Ok(await _service.CompareAsync(request));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("battle")]
    public async Task<IActionResult> Battle([FromBody] BattleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt) || string.IsNullOrWhiteSpace(request.Competitor))
            return BadRequest(new { message = "Prompt and competitor are required." });

        try
        {
            return Ok(await _service.BattleAsync(request));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }
}
