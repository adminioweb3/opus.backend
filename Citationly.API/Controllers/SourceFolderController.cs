using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.API.Services;
using Citationly.Application.Features.SourceFolders;
using Citationly.Application.Interfaces;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SourceFolderController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentOrganizationAccessor _currentOrg;
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository;

    public SourceFolderController(IMediator mediator, ICurrentOrganizationAccessor currentOrg, IKnowledgeBaseRepository knowledgeBaseRepository)
    {
        _mediator = mediator;
        _currentOrg = currentOrg;
        _knowledgeBaseRepository = knowledgeBaseRepository;
    }

    // GET /api/SourceFolder?knowledgeBaseId=xxx
    [HttpGet]
    public async Task<IActionResult> GetFolders([FromQuery] Guid knowledgeBaseId)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var kb = await _knowledgeBaseRepository.GetByIdAsync(knowledgeBaseId);
        if (kb == null || kb.OrganizationId != orgId.Value) return NotFound();

        var result = await _mediator.Send(new GetSourceFoldersQuery { KnowledgeBaseId = knowledgeBaseId });
        return Ok(result);
    }

    // POST /api/SourceFolder
    [HttpPost]
    public async Task<IActionResult> CreateFolder([FromBody] CreateSourceFolderRequest request)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var kb = await _knowledgeBaseRepository.GetByIdAsync(request.KnowledgeBaseId);
        if (kb == null || kb.OrganizationId != orgId.Value) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Name is required." });

        var result = await _mediator.Send(new CreateSourceFolderCommand
        {
            KnowledgeBaseId = request.KnowledgeBaseId,
            Name = request.Name.Trim()
        });
        return Ok(result);
    }
}

public class CreateSourceFolderRequest
{
    public Guid KnowledgeBaseId { get; set; }
    public string Name { get; set; } = string.Empty;
}
