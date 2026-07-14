using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.KnowledgeBases;
using Citationly.Application.Interfaces;
using System.Security.Claims;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class KnowledgeBaseController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IUserRepository _userRepository;
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository;

    public KnowledgeBaseController(IMediator mediator, IUserRepository userRepository, IKnowledgeBaseRepository knowledgeBaseRepository)
    {
        _mediator = mediator;
        _userRepository = userRepository;
        _knowledgeBaseRepository = knowledgeBaseRepository;
    }

    private async Task<Guid?> GetOrganizationIdAsync()
    {
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(firebaseUid)) return null;

        var user = await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
        return user?.OrganizationId;
    }

    [HttpGet]
    public async Task<IActionResult> GetKnowledgeBases()
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new GetKnowledgeBasesQuery { OrganizationId = orgId.Value });
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateKnowledgeBase([FromBody] CreateKnowledgeBaseRequest request)
    {
        var orgId = await GetOrganizationIdAsync();
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
        var orgId = await GetOrganizationIdAsync();
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
