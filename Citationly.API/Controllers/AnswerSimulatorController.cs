using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.AnswerSimulator;
using Citationly.Application.Interfaces.AnswerSimulator;
using Citationly.API.Services;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AnswerSimulatorController : ControllerBase
{
    private readonly IAnswerSimulatorService _service;
    private readonly ICurrentOrganizationAccessor _organizationAccessor;

    public AnswerSimulatorController(IAnswerSimulatorService service, ICurrentOrganizationAccessor organizationAccessor)
    {
        _service = service;
        _organizationAccessor = organizationAccessor;
    }

    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate([FromBody] SimulateAnswerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { message = "A question/prompt is required." });

        try
        {
            var organizationId = await _organizationAccessor.GetOrganizationIdAsync(User);
            if (!organizationId.HasValue) return Unauthorized();
            return Ok(await _service.SimulateAsync(organizationId.Value, request));
        }
        catch (InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "AI simulation is temporarily unavailable. Please try again." });
        }
    }

    [HttpPost("compare")]
    public async Task<IActionResult> Compare([FromBody] CompareContentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt) || string.IsNullOrWhiteSpace(request.PageContent))
            return BadRequest(new { message = "Prompt and page content are required." });

        try
        {
            var organizationId = await _organizationAccessor.GetOrganizationIdAsync(User);
            if (!organizationId.HasValue) return Unauthorized();
            return Ok(await _service.CompareAsync(organizationId.Value, request));
        }
        catch (InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "AI comparison is temporarily unavailable. Please try again." });
        }
    }

    [HttpPost("battle")]
    public async Task<IActionResult> Battle([FromBody] BattleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt) || string.IsNullOrWhiteSpace(request.Competitor))
            return BadRequest(new { message = "Prompt and competitor are required." });

        try
        {
            var organizationId = await _organizationAccessor.GetOrganizationIdAsync(User);
            if (!organizationId.HasValue) return Unauthorized();
            return Ok(await _service.BattleAsync(organizationId.Value, request));
        }
        catch (InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "AI comparison is temporarily unavailable. Please try again." });
        }
    }
}
