using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.API.Services;
using Citationly.Application.Features.Onboarding;
using Microsoft.AspNetCore.RateLimiting;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OnboardingController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentOrganizationAccessor _currentOrganizationAccessor;

    public OnboardingController(IMediator mediator, ICurrentOrganizationAccessor currentOrganizationAccessor)
    {
        _mediator = mediator;
        _currentOrganizationAccessor = currentOrganizationAccessor;
    }

    private Task<Guid?> GetCurrentOrganizationIdAsync()
        => _currentOrganizationAccessor.GetOrganizationIdAsync(User, HttpContext.RequestAborted);

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeOnboardingRequest request)
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new AnalyzeOnboardingCommand
        {
            OrganizationId = organizationId.Value,
            WebsiteUrl = request.WebsiteUrl ?? string.Empty,
            BusinessName = request.BusinessName ?? string.Empty,
            Industry = request.Industry ?? string.Empty,
            TargetAudience = request.TargetAudience ?? string.Empty,
            Keywords = request.Keywords ?? string.Empty,
            WhoDoYouSellTo = request.WhoDoYouSellTo ?? string.Empty,
            KnownCompetitors = request.KnownCompetitors ?? string.Empty,
            MainOffering = request.MainOffering ?? string.Empty
        };

        var result = await _mediator.Send(command);

        return Ok(result);
    }

    [HttpGet("analyze")]
    public async Task<IActionResult> GetAnalyze([FromQuery] string? websiteUrl, [FromQuery] string? businessName, [FromQuery] string? industry, [FromQuery] string? targetAudience, [FromQuery] string? keywords)
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new AnalyzeOnboardingCommand
        {
            OrganizationId = organizationId.Value,
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
    public async Task<IActionResult> AnalyzeCompetitors()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new AnalyzeCompetitorsCommand
        {
            OrganizationId = organizationId.Value
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-competitors")]
    public async Task<IActionResult> GetAnalyzeCompetitors()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var result = await _mediator.Send(new AnalyzeCompetitorsCommand { OrganizationId = organizationId.Value });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("analyze-prompts")]
    public async Task<IActionResult> AnalyzePrompts()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new AnalyzeAiSearchPromptsCommand
        {
            OrganizationId = organizationId.Value
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-prompts")]
    public async Task<IActionResult> GetAnalyzePrompts()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var result = await _mediator.Send(new AnalyzeAiSearchPromptsCommand { OrganizationId = organizationId.Value });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("analyze-visibility")]
    public async Task<IActionResult> AnalyzeVisibility()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new AnalyzeVisibilityCommand
        {
            OrganizationId = organizationId.Value
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-visibility")]
    public async Task<IActionResult> GetAnalyzeVisibility()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var result = await _mediator.Send(new AnalyzeVisibilityCommand { OrganizationId = organizationId.Value });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("analyze-platform-visibility")]
    public async Task<IActionResult> AnalyzePlatformVisibility()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new AnalyzePlatformVisibilityCommand
        {
            OrganizationId = organizationId.Value
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-platform-visibility")]
    public async Task<IActionResult> GetAnalyzePlatformVisibility()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var result = await _mediator.Send(new AnalyzePlatformVisibilityCommand { OrganizationId = organizationId.Value });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("analyze-citations")]
    public async Task<IActionResult> AnalyzeCitations()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new AnalyzeCitationsCommand
        {
            OrganizationId = organizationId.Value
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-citations")]
    public async Task<IActionResult> GetAnalyzeCitations()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var result = await _mediator.Send(new AnalyzeCitationsCommand { OrganizationId = organizationId.Value });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("analyze-personas")]
    public async Task<IActionResult> AnalyzePersonas()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new AnalyzePersonasCommand
        {
            OrganizationId = organizationId.Value
        };

        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-personas")]
    public async Task<IActionResult> GetAnalyzePersonas()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var result = await _mediator.Send(new AnalyzePersonasCommand { OrganizationId = organizationId.Value });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("analyze-regions")]
    public async Task<IActionResult> AnalyzeRegions()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new AnalyzeRegionsCommand { OrganizationId = organizationId.Value };
        var result = await _mediator.Send(command);

        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("analyze-regions")]
    public async Task<IActionResult> GetAnalyzeRegions()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var result = await _mediator.Send(new AnalyzeRegionsCommand { OrganizationId = organizationId.Value });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("generate-recommendations")]
    public async Task<IActionResult> GenerateRecommendations()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new GenerateRecommendationsCommand { OrganizationId = organizationId.Value };
        var result = await _mediator.Send(command);

        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("generate-recommendations")]
    public async Task<IActionResult> GetGenerateRecommendations()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var result = await _mediator.Send(new GenerateRecommendationsCommand { OrganizationId = organizationId.Value });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [HttpPost("generate-executive-summary")]
    public async Task<IActionResult> GenerateExecutiveSummary()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new GenerateExecutiveSummaryCommand { OrganizationId = organizationId.Value };
        var result = await _mediator.Send(command);

        if (!result.Success) return BadRequest(result.Error);

        return Ok(result);
    }

    [HttpGet("generate-executive-summary")]
    public async Task<IActionResult> GetGenerateExecutiveSummary()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var result = await _mediator.Send(new GenerateExecutiveSummaryCommand { OrganizationId = organizationId.Value });
        if (!result.Success) return BadRequest(result.Error);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteOnboardingRequest request)
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new CompleteOnboardingCommand
        {
            OrganizationId = organizationId.Value,
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
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var command = new CompleteOnboardingCommand
        {
            OrganizationId = organizationId.Value,
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

    [AllowAnonymous]
    [EnableRateLimiting("AnonymousAI")]
    [HttpPost("suggest-keywords")]
    public async Task<IActionResult> SuggestKeywords([FromBody] SuggestKeywordsRequest request)
    {
        if (string.IsNullOrEmpty(request.WebsiteUrl) || string.IsNullOrEmpty(request.BusinessName))
            return BadRequest("WebsiteUrl and BusinessName are required.");

        var command = new SuggestKeywordsCommand
        {
            WebsiteUrl = request.WebsiteUrl,
            BusinessName = request.BusinessName,
            Industry = request.Industry
        };

        var result = await _mediator.Send(command);
        return Ok(new { keywords = result });
    }

    [AllowAnonymous]
    [EnableRateLimiting("AnonymousAI")]
    [HttpPost("detect-industry")]
    public async Task<IActionResult> DetectIndustry([FromBody] DetectIndustryRequest request)
    {
        if (string.IsNullOrEmpty(request.WebsiteUrl) || string.IsNullOrEmpty(request.BusinessName))
            return BadRequest("WebsiteUrl and BusinessName are required.");

        var command = new DetectIndustryCommand
        {
            WebsiteUrl = request.WebsiteUrl,
            BusinessName = request.BusinessName
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [AllowAnonymous]
    [EnableRateLimiting("AnonymousAI")]
    [HttpPost("detect-offering")]
    public async Task<IActionResult> DetectOffering([FromBody] DetectIndustryRequest request)
    {
        if (string.IsNullOrEmpty(request.WebsiteUrl) || string.IsNullOrEmpty(request.BusinessName))
            return BadRequest("WebsiteUrl and BusinessName are required.");

        var command = new DetectOfferingCommand
        {
            WebsiteUrl = request.WebsiteUrl,
            BusinessName = request.BusinessName
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [HttpGet("competitors/unified")]
    public async Task<IActionResult> GetUnifiedCompetitors()
    {
        var organizationId = await GetCurrentOrganizationIdAsync();
        if (organizationId is null) return Unauthorized();

        var query = new GetUnifiedCompetitorsQuery { OrganizationId = organizationId.Value };
        var result = await _mediator.Send(query);
        return Ok(result);
    }
}

public class SuggestKeywordsRequest
{
    public string? WebsiteUrl { get; set; }
    public string? BusinessName { get; set; }
    public string? Industry { get; set; }
}

public class DetectIndustryRequest
{
    public string? WebsiteUrl { get; set; }
    public string? BusinessName { get; set; }
}

public class AnalyzeOnboardingRequest
{
    public string? WebsiteUrl { get; set; }
    public string? BusinessName { get; set; }
    public string? Industry { get; set; }
    public string? TargetAudience { get; set; }
    public string? Keywords { get; set; }
    public string? WhoDoYouSellTo { get; set; }
    public string? KnownCompetitors { get; set; }
    public string? MainOffering { get; set; }
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
