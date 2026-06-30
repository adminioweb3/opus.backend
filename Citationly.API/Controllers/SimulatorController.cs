using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Citationly.Application.Features.Simulators;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SimulatorController : ControllerBase
{
    private readonly IMediator _mediator;

    public SimulatorController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("search")]
    public async Task<IActionResult> SearchSimilar([FromBody] SimulatorSearchRequest request)
    {
        var query = new SearchSimilarQuery
        {
            OrganizationId = request.OrganizationId == Guid.Empty ? Guid.NewGuid() : request.OrganizationId,
            QueryText = request.QueryText,
            TopK = request.TopK
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }
}

public class SimulatorSearchRequest
{
    public Guid OrganizationId { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
}
