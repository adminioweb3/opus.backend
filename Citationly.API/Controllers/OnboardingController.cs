using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Citationly.Application.Features.Onboarding;
using Citationly.Application.Interfaces;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
// [Authorize]
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
            Keywords = request.Keywords ?? string.Empty,
            Competitors = request.Competitors ?? string.Empty,
            RankingGoal = request.RankingGoal ?? string.Empty
        };

        var result = await _mediator.Send(command);

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
            Competitors = request.Competitors ?? string.Empty,
            VisibilityScore = request.VisibilityScore,
            BrandAuthority = request.BrandAuthority,
            ContentStrength = request.ContentStrength,
            CitationScore = request.CitationScore
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
    public string? Competitors { get; set; }
    public string? RankingGoal { get; set; }
}

public class CompleteOnboardingRequest
{
    public string? WebsiteUrl { get; set; }
    public string? BusinessName { get; set; }
    public string? Competitors { get; set; }
    public int VisibilityScore { get; set; }
    public int BrandAuthority { get; set; }
    public int ContentStrength { get; set; }
    public int CitationScore { get; set; }
}
