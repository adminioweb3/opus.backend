using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Citationly.Application.Features.Deployments;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DeploymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DeploymentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("execute")]
    public async Task<IActionResult> ExecuteDeployment([FromBody] DeployRecommendationCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(new { Error = result.Message });
        return Ok(new { DeployedUrl = result.DeployedUrl, Status = "Success" });
    }
}
