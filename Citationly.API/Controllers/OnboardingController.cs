using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.Onboarding;
using Citationly.Application.Interfaces;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OnboardingController : ControllerBase
{
    private readonly IMediator _mediator;

    private readonly IUserRepository _userRepository;

    public OnboardingController(IMediator mediator, IUserRepository userRepository)
    {
        _mediator = mediator;
        _userRepository = userRepository;
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeOnboardingRequest request)
    {
        var orgId = request.OrganizationId == Guid.Empty ? Guid.NewGuid() : request.OrganizationId;

        var command = new AnalyzeOnboardingCommand
        {
            OrganizationId = orgId,
            WebsiteUrl = request.WebsiteUrl ?? string.Empty,
            BusinessName = request.BusinessName ?? string.Empty,
            Industry = request.Industry ?? string.Empty,
            TargetAudience = request.TargetAudience ?? string.Empty,
            Keywords = request.Keywords ?? string.Empty
        };

        var result = await _mediator.Send(command);

        return Ok(result);
    }

    [HttpGet("analyze")]
    public async Task<IActionResult> GetAnalyze([FromQuery] Guid organizationId, [FromQuery] string? websiteUrl, [FromQuery] string? businessName, [FromQuery] string? industry, [FromQuery] string? targetAudience, [FromQuery] string? keywords)
    {
        var orgId = organizationId == Guid.Empty ? Guid.NewGuid() : organizationId;
        var command = new AnalyzeOnboardingCommand
        {
            OrganizationId = orgId,
            WebsiteUrl = websiteUrl ?? string.Empty,
            BusinessName = businessName ?? string.Empty,
            Industry = industry ?? string.Empty,
            TargetAudience = targetAudience ?? string.Empty,
            Keywords = keywords ?? string.Empty
        };
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [HttpPost("analyze-competitors")]
    public async Task<IActionResult> AnalyzeCompetitors([FromBody] AnalyzeCompetitorsRequest request)
    {
        if (request.OrganizationId == Guid.Empty)
            return BadRequest("OrganizationId is required.");

        var command = new AnalyzeCompetitorsCommand
        {
            OrganizationId = request.OrganizationId
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-competitors")]
    public async Task<IActionResult> GetAnalyzeCompetitors([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");
        var result = await _mediator.Send(new AnalyzeCompetitorsCommand { OrganizationId = organizationId });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("analyze-prompts")]
    public async Task<IActionResult> AnalyzePrompts([FromBody] AnalyzeAiSearchPromptsRequest request)
    {
        if (request.OrganizationId == Guid.Empty)
            return BadRequest("OrganizationId is required.");

        var command = new AnalyzeAiSearchPromptsCommand
        {
            OrganizationId = request.OrganizationId
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-prompts")]
    public async Task<IActionResult> GetAnalyzePrompts([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");
        var result = await _mediator.Send(new AnalyzeAiSearchPromptsCommand { OrganizationId = organizationId });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("analyze-visibility")]
    public async Task<IActionResult> AnalyzeVisibility([FromBody] AnalyzeVisibilityRequest request)
    {
        if (request.OrganizationId == Guid.Empty)
            return BadRequest("OrganizationId is required.");

        var command = new AnalyzeVisibilityCommand
        {
            OrganizationId = request.OrganizationId
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-visibility")]
    public async Task<IActionResult> GetAnalyzeVisibility([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");
        var result = await _mediator.Send(new AnalyzeVisibilityCommand { OrganizationId = organizationId });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("analyze-platform-visibility")]
    public async Task<IActionResult> AnalyzePlatformVisibility([FromBody] AnalyzePlatformVisibilityRequest request)
    {
        if (request.OrganizationId == Guid.Empty)
            return BadRequest("OrganizationId is required.");

        var command = new AnalyzePlatformVisibilityCommand
        {
            OrganizationId = request.OrganizationId
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-platform-visibility")]
    public async Task<IActionResult> GetAnalyzePlatformVisibility([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");
        var result = await _mediator.Send(new AnalyzePlatformVisibilityCommand { OrganizationId = organizationId });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("analyze-citations")]
    public async Task<IActionResult> AnalyzeCitations([FromBody] AnalyzeCitationsRequest request)
    {
        if (request.OrganizationId == Guid.Empty)
            return BadRequest("OrganizationId is required.");

        var command = new AnalyzeCitationsCommand
        {
            OrganizationId = request.OrganizationId
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-citations")]
    public async Task<IActionResult> GetAnalyzeCitations([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");
        var result = await _mediator.Send(new AnalyzeCitationsCommand { OrganizationId = organizationId });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("analyze-personas")]
    public async Task<IActionResult> AnalyzePersonas([FromBody] AnalyzePersonasRequest request)
    {
        if (request.OrganizationId == Guid.Empty)
            return BadRequest("OrganizationId is required.");

        var command = new AnalyzePersonasCommand
        {
            OrganizationId = request.OrganizationId
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-personas")]
    public async Task<IActionResult> GetAnalyzePersonas([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");
        var result = await _mediator.Send(new AnalyzePersonasCommand { OrganizationId = organizationId });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("analyze-regions")]
    public async Task<IActionResult> AnalyzeRegions([FromBody] AnalyzeRegionsRequest request)
    {
        var command = new AnalyzeRegionsCommand { OrganizationId = request.OrganizationId };
        var result = await _mediator.Send(command);

        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-regions")]
    public async Task<IActionResult> GetAnalyzeRegions([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");
        var result = await _mediator.Send(new AnalyzeRegionsCommand { OrganizationId = organizationId });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("generate-recommendations")]
    public async Task<IActionResult> GenerateRecommendations([FromBody] GenerateRecommendationsRequest request)
    {
        var command = new GenerateRecommendationsCommand { OrganizationId = request.OrganizationId };
        var result = await _mediator.Send(command);

        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("generate-recommendations")]
    public async Task<IActionResult> GetGenerateRecommendations([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");
        var result = await _mediator.Send(new GenerateRecommendationsCommand { OrganizationId = organizationId });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("generate-executive-summary")]
    public async Task<IActionResult> GenerateExecutiveSummary([FromBody] GenerateExecutiveSummaryRequest request)
    {
        var command = new GenerateExecutiveSummaryCommand { OrganizationId = request.OrganizationId };
        var result = await _mediator.Send(command);

        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("generate-executive-summary")]
    public async Task<IActionResult> GetGenerateExecutiveSummary([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty) return BadRequest("OrganizationId is required.");
        var result = await _mediator.Send(new GenerateExecutiveSummaryCommand { OrganizationId = organizationId });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteOnboardingRequest request)
    {
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(firebaseUid)) return Unauthorized();

        var user = await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
        if (user == null) return Unauthorized();

        var command = new CompleteOnboardingCommand
        {
            OrganizationId = user.Value.OrganizationId,
            WebsiteUrl = request.WebsiteUrl ?? string.Empty,
            BusinessName = request.BusinessName ?? string.Empty,
            VisibilityScore = request.VisibilityScore,
            BrandAuthority = request.BrandAuthority,
            ContentStrength = request.ContentStrength,
            CitationScore = request.CitationScore
        };

        var result = await _mediator.Send(command);

        return Ok(result);
    }

    [Authorize]
    [HttpGet("complete")]
    public async Task<IActionResult> GetComplete([FromQuery] string? websiteUrl, [FromQuery] string? businessName, [FromQuery] int visibilityScore, [FromQuery] int brandAuthority, [FromQuery] int contentStrength, [FromQuery] int citationScore)
    {
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(firebaseUid)) return Unauthorized();

        var user = await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
        if (user == null) return Unauthorized();

        var command = new CompleteOnboardingCommand
        {
            OrganizationId = user.Value.OrganizationId,
            WebsiteUrl = websiteUrl ?? string.Empty,
            BusinessName = businessName ?? string.Empty,
            VisibilityScore = visibilityScore,
            BrandAuthority = brandAuthority,
            ContentStrength = contentStrength,
            CitationScore = citationScore
        };

        var result = await _mediator.Send(command);

        return Ok(result);
    }
}

public class AnalyzeOnboardingRequest
{
    public Guid OrganizationId { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? BusinessName { get; set; }
    public string? Industry { get; set; }
    public string? TargetAudience { get; set; }
    public string? Keywords { get; set; }
}

public class CompleteOnboardingRequest
{
    public string? WebsiteUrl { get; set; }
    public string? BusinessName { get; set; }
    public int VisibilityScore { get; set; }
    public int BrandAuthority { get; set; }
    public int ContentStrength { get; set; }
    public int CitationScore { get; set; }
}

public class AnalyzeCompetitorsRequest
{
    public Guid OrganizationId { get; set; }
}

public class AnalyzeAiSearchPromptsRequest
{
    public Guid OrganizationId { get; set; }
}

public class AnalyzeVisibilityRequest
{
    public Guid OrganizationId { get; set; }
}

public class AnalyzePlatformVisibilityRequest
{
    public Guid OrganizationId { get; set; }
}

public class AnalyzeCitationsRequest
{
    public Guid OrganizationId { get; set; }
}

public class AnalyzePersonasRequest
{
    public Guid OrganizationId { get; set; }
}

public class AnalyzeRegionsRequest
{
    public Guid OrganizationId { get; set; }
}

public class GenerateRecommendationsRequest
{
    public Guid OrganizationId { get; set; }
}

public class GenerateExecutiveSummaryRequest
{
    public Guid OrganizationId { get; set; }
}
