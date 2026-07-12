using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.Content;
using Citationly.Application.Interfaces;
using System.Security.Claims;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ContentController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IUserRepository _userRepository;

    public ContentController(IMediator mediator, IUserRepository userRepository)
    {
        _mediator = mediator;
        _userRepository = userRepository;
    }

    private async Task<Guid?> GetOrganizationIdAsync()
    {
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(firebaseUid)) return null;

        var user = await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
        return user?.OrganizationId;
    }

    // GET /api/Content
    [HttpGet]
    public async Task<IActionResult> GetDrafts()
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new GetContentDraftsQuery { OrganizationId = orgId.Value });
        return Ok(result);
    }

    // GET /api/Content/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetDraft(Guid id)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new GetContentDraftQuery { OrganizationId = orgId.Value, DraftId = id });
        if (result == null) return NotFound();
        return Ok(result);
    }

    // POST /api/Content/generate
    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateContentRequest request)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { message = "Prompt is required." });

        var command = new GenerateContentCommand
        {
            OrganizationId = orgId.Value,
            Prompt = request.Prompt,
            Keywords = request.Keywords,
            ContentType = request.ContentType,
            Tone = request.Tone,
            Language = request.Language,
            Audience = request.Audience,
            OutputLength = request.OutputLength,
            BrandVoice = request.BrandVoice,
            OutputFormat = request.OutputFormat,
            Creativity = request.Creativity,
            TargetKeyword = request.TargetKeyword,
            BusinessName = request.BusinessName,
            TargetAudience = request.TargetAudience,
            SearchIntent = request.SearchIntent,
            Goal = request.Goal,
            Cta = request.Cta,
            ReferenceUrls = request.ReferenceUrls,
            SecondaryAngle = request.SecondaryAngle,
            CompetitorUrl = request.CompetitorUrl,
            CompetitorInsights = request.CompetitorInsights,
            Model = request.Model
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    // POST /api/Content/analyze-competitor
    [HttpPost("analyze-competitor")]
    public async Task<IActionResult> AnalyzeCompetitor([FromBody] AnalyzeCompetitorRequest request)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(new { message = "Url is required." });

        try
        {
            var result = await _mediator.Send(new AnalyzeCompetitorCommand { Url = request.Url });
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Failed to analyze competitor page: {ex.Message}" });
        }
    }

    // POST /api/Content/{id}/rewrite
    [HttpPost("{id}/rewrite")]
    public async Task<IActionResult> Rewrite(Guid id, [FromBody] RewriteContentRequest request)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new RewriteContentCommand
        {
            OrganizationId = orgId.Value,
            DraftId = id,
            Instruction = request.Instruction
        });
        if (result == null) return NotFound();
        return Ok(result);
    }

    // PUT /api/Content/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDraft(Guid id, [FromBody] UpdateContentDraftRequest request)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new UpdateContentDraftCommand
        {
            OrganizationId = orgId.Value,
            DraftId = id,
            Content = request.Content,
            Status = request.Status
        });
        if (result == null) return NotFound();
        return Ok(result);
    }

    // POST /api/Content/{id}/optimize
    [HttpPost("{id}/optimize")]
    public async Task<IActionResult> Optimize(Guid id, [FromBody] OptimizeContentRequest request)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new OptimizeContentCommand
        {
            OrganizationId = orgId.Value,
            ContentDraftId = id,
            PrimaryKeyword = request.PrimaryKeyword,
            Goal = request.Goal,
            Audience = request.Audience,
            Notes = request.Notes,
            Depth = request.Depth
        });
        if (result == null) return NotFound();
        return Ok(result);
    }

    // POST /api/Content/{id}/publish
    [HttpPost("{id}/publish")]
    public async Task<IActionResult> Publish(Guid id)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new PublishContentDraftCommand
        {
            OrganizationId = orgId.Value,
            DraftId = id
        });
        return Ok(result);
    }

    // GET /api/Content/publishing-summary
    [HttpGet("publishing-summary")]
    public async Task<IActionResult> GetPublishingSummary()
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized("User not found or unlinked.");

        var result = await _mediator.Send(new GetPublishingSummaryQuery { OrganizationId = orgId.Value });
        return Ok(result);
    }
}

public class GenerateContentRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string? Keywords { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string OutputLength { get; set; } = string.Empty;
    public string BrandVoice { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = string.Empty;
    public int Creativity { get; set; }
    public string? TargetKeyword { get; set; }
    public string? BusinessName { get; set; }
    public string? TargetAudience { get; set; }
    public string? SearchIntent { get; set; }
    public string? Goal { get; set; }
    public string? Cta { get; set; }
    public string? ReferenceUrls { get; set; }
    public string? SecondaryAngle { get; set; }
    public string? CompetitorUrl { get; set; }
    public string? CompetitorInsights { get; set; }
    public string? Model { get; set; }
}

public class AnalyzeCompetitorRequest
{
    public string Url { get; set; } = string.Empty;
}

public class RewriteContentRequest
{
    public string Instruction { get; set; } = string.Empty;
}

public class UpdateContentDraftRequest
{
    public string Content { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public class OptimizeContentRequest
{
    public string PrimaryKeyword { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int Depth { get; set; }
}
