using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Citationly.API.Services;
using Citationly.Application.Features.Deployments;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DeploymentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentOrganizationAccessor _currentOrganization;

    public DeploymentsController(IMediator mediator, ICurrentOrganizationAccessor currentOrganization)
    {
        _mediator = mediator;
        _currentOrganization = currentOrganization;
    }

    [HttpPost("execute")]
    public async Task<IActionResult> ExecuteDeployment([FromBody] ExecuteDeploymentRequest request)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized("User not found or unlinked.");

        var command = new DeployRecommendationCommand
        {
            OrganizationId = organizationId.Value,
            RecommendationId = request.RecommendationId,
            IntegrationId = request.IntegrationId,
            Status = request.Status
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(new { Error = result.Message });
        return Ok(new { DeployedUrl = result.DeployedUrl, Status = "Success" });
    }

    [HttpPost("developer-handoff")]
    public async Task<IActionResult> ExportDeveloperHandoff([FromBody] ExportDeveloperHandoffRequest request)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId is null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new ExportDeveloperHandoffCommand
        {
            OrganizationId = organizationId.Value,
            RecommendationId = request.RecommendationId,
            SiteType = request.SiteType,
            TargetPath = request.TargetPath,
            DeliveryOption = request.DeliveryOption
        });

        if (!result.Success) return BadRequest(new { Error = result.Message });

        return Ok(new
        {
            result.FileName,
            ContentType = "text/markdown",
            result.Markdown
        });
    }
}

public class ExecuteDeploymentRequest
{
    public Guid RecommendationId { get; set; }
    public Guid IntegrationId { get; set; }
    public string Status { get; set; } = "draft";
}

public class ExportDeveloperHandoffRequest
{
    public Guid RecommendationId { get; set; }
    public string? SiteType { get; set; }
    public string? TargetPath { get; set; }
    public string? DeliveryOption { get; set; }
}
