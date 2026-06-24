using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Opus.Application.Features.Integrations;

namespace Opus.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class IntegrationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public IntegrationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid organizationId)
    {
        var query = new GetIntegrationsQuery
        {
            OrganizationId = organizationId == Guid.Empty ? Guid.NewGuid() : organizationId
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertIntegrationRequest request)
    {
        var command = new UpsertIntegrationCommand
        {
            OrganizationId = request.OrganizationId == Guid.Empty ? Guid.NewGuid() : request.OrganizationId,
            PlatformName = request.PlatformName,
            ApiUrl = request.ApiUrl,
            ApiKey = request.ApiKey
        };

        try
        {
            var result = await _mediator.Send(command);
            return Ok(new { Message = "Integration connected successfully", IntegrationId = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
}

public class UpsertIntegrationRequest
{
    public Guid OrganizationId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
