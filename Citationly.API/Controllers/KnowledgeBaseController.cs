using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.API.Services;
using Citationly.Application.Features.KnowledgeBases;
using Citationly.Application.Interfaces;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class KnowledgeBaseController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentOrganizationAccessor _currentOrg;
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository;

    public KnowledgeBaseController(IMediator mediator, ICurrentOrganizationAccessor currentOrg, IKnowledgeBaseRepository knowledgeBaseRepository)
    {
        _mediator = mediator;
        _currentOrg = currentOrg;
        _knowledgeBaseRepository = knowledgeBaseRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetKnowledgeBases()
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new GetKnowledgeBasesQuery { OrganizationId = orgId.Value });
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateKnowledgeBase([FromBody] CreateKnowledgeBaseRequest request)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var command = new CreateKnowledgeBaseCommand
        {
            OrganizationId = orgId.Value,
            Name = request.Name,
            Icon = string.IsNullOrWhiteSpace(request.Icon) ? "Building2" : request.Icon,
            Tint = string.IsNullOrWhiteSpace(request.Tint) ? "#6366F1" : request.Tint,
            Bg = string.IsNullOrWhiteSpace(request.Bg) ? "#EEEEFE" : request.Bg,
            Description = request.Description
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    // POST /api/KnowledgeBase/{id}/ask
    [HttpPost("{id}/ask")]
    public async Task<IActionResult> AskKnowledgeBase(Guid id, [FromBody] AskKnowledgeBaseRequest request)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var kb = await _knowledgeBaseRepository.GetByIdAsync(id);
        if (kb == null || kb.OrganizationId != orgId.Value) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "Question is required." });

        var result = await _mediator.Send(new AskKnowledgeBaseQuery
        {
            OrganizationId = orgId.Value,
            KnowledgeBaseId = id,
            Question = request.Question.Trim()
        });

        return Ok(result);
    }
}

public class CreateKnowledgeBaseRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Tint { get; set; }
    public string? Bg { get; set; }
    public string? Description { get; set; }
}

public class AskKnowledgeBaseRequest
{
    public string Question { get; set; } = string.Empty;
}
