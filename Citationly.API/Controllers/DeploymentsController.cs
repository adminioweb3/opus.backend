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
        try
        {
            var resultUrl = await _mediator.Send(command);
            return Ok(new { DeployedUrl = resultUrl, Status = "Success" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}
