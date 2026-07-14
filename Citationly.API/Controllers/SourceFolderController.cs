using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.SourceFolders;
using Citationly.Application.Interfaces;
using System.Security.Claims;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SourceFolderController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IUserRepository _userRepository;
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository;

    public SourceFolderController(IMediator mediator, IUserRepository userRepository, IKnowledgeBaseRepository knowledgeBaseRepository)
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

    // GET /api/SourceFolder?knowledgeBaseId=xxx
    [HttpGet]
    public async Task<IActionResult> GetFolders([FromQuery] Guid knowledgeBaseId)
    {
        var orgId = await GetOrganizationIdAsync();
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
        var orgId = await GetOrganizationIdAsync();
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
