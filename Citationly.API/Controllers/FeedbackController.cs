using Citationly.API.Services;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class FeedbackController : ControllerBase
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "General",
        "Bug",
        "Confusing",
        "MissingFeature",
        "Billing",
        "Integration"
    };

    private readonly ICurrentOrganizationAccessor _currentOrganization;
    private readonly IBetaFeedbackRepository _feedbackRepository;

    public FeedbackController(ICurrentOrganizationAccessor currentOrganization, IBetaFeedbackRepository feedbackRepository)
    {
        _currentOrganization = currentOrganization;
        _feedbackRepository = feedbackRepository;
    }

    [HttpPost]
    [AuditAction("feedback.create", "Support", "BetaFeedback")]
    public async Task<IActionResult> Create([FromBody] CreateFeedbackRequest request)
    {
        var caller = await _currentOrganization.GetCurrentUserAsync(User, HttpContext.RequestAborted);
        if (caller == null) return Unauthorized("User not found or unlinked.");

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { message = "Feedback message is required." });
        }

        if (request.Rating is < 1 or > 5)
        {
            return BadRequest(new { message = "Rating must be between 1 and 5." });
        }

        var type = string.IsNullOrWhiteSpace(request.FeedbackType) ? "General" : request.FeedbackType.Trim();
        if (!AllowedTypes.Contains(type))
        {
            return BadRequest(new { message = "Unsupported feedback type." });
        }

        var feedback = new BetaFeedback
        {
            OrganizationId = caller.Value.OrganizationId,
            UserId = caller.Value.UserId,
            PagePath = Truncate(request.PagePath, 2048),
            FeedbackType = type,
            Rating = request.Rating,
            Message = Truncate(request.Message, 4000),
            ContextId = Truncate(request.ContextId, 100),
            Status = "Open"
        };

        var id = await _feedbackRepository.CreateAsync(feedback, HttpContext.RequestAborted);
        return CreatedAtAction(nameof(GetRecent), new { limit = 25 }, new { id, message = "Feedback received." });
    }

    [HttpGet]
    [RequireOrgRole("Admin")]
    [AuditAction("feedback.read", "Support", "BetaFeedback")]
    public async Task<IActionResult> GetRecent([FromQuery] int limit = 100)
    {
        var organizationId = await _currentOrganization.GetOrganizationIdAsync(User, HttpContext.RequestAborted);
        if (organizationId == null) return Unauthorized();

        var rows = await _feedbackRepository.GetByOrganizationAsync(organizationId.Value, Math.Clamp(limit, 1, 500), HttpContext.RequestAborted);
        return Ok(rows);
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

public class CreateFeedbackRequest
{
    public string PagePath { get; set; } = string.Empty;
    public string FeedbackType { get; set; } = "General";
    public int? Rating { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ContextId { get; set; } = string.Empty;
}
