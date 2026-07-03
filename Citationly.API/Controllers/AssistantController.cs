using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using System.Text;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AssistantController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserRepository _userRepository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IMetricsRepository _metricsRepository;
    private readonly ISearchService _searchService;
    private readonly Citationly.Application.Interfaces.IDbConnectionFactory _dbConnectionFactory;

    public AssistantController(
        IHttpClientFactory httpClientFactory, 
        IConfiguration configuration,
        IUserRepository userRepository,
        IWebsiteRepository websiteRepository,
        IMetricsRepository metricsRepository,
        ISearchService searchService,
        Citationly.Application.Interfaces.IDbConnectionFactory dbConnectionFactory)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _userRepository = userRepository;
        _websiteRepository = websiteRepository;
        _metricsRepository = metricsRepository;
        _searchService = searchService;
        _dbConnectionFactory = dbConnectionFactory;
    }

    private async Task<Guid?> GetOrganizationIdAsync()
    {
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(firebaseUid)) return null;

        var user = await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
        return user?.OrganizationId;
    }

    [HttpGet("recent")]
    public IActionResult GetRecentItems()
    {
        // Mocking the recent items to match the screenshot for the UI demo
        var recentItems = new List<object>
        {
            new { id = 1, name = "Ioweb3 AEO Content Producer", owner = "Sudarshan Patil", type = "Agent", updatedAt = "5h ago" },
            new { id = 2, name = "AEO-Optimized FAQ Generator", owner = "Sudarshan Patil", type = "Agent", updatedAt = "22h ago" },
            new { id = 3, name = "Untitled Agent", owner = "Sudarshan Patil", type = "Agent", updatedAt = "22h ago" }
        };

        return Ok(recentItems);
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, [FromServices] Citationly.Application.Features.Assistant.Pipeline.AgentOrchestrator orchestrator)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Message cannot be empty." });

        Response.Headers.Add("Content-Type", "text/event-stream");
        Response.Headers.Add("Cache-Control", "no-cache");
        Response.Headers.Add("Connection", "keep-alive");

        var orgId = await GetOrganizationIdAsync();
        
        await foreach (var status in orchestrator.ExecutePipelineAsync(orgId, request.Message, request.History, HttpContext.RequestAborted))
        {
            var data = JsonSerializer.Serialize(new { status = status });
            await Response.WriteAsync($"data: {data}\n\n");
            await Response.Body.FlushAsync();
        }

        return new EmptyResult();
    }
}

public class ChatMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public List<ChatMessageDto>? History { get; set; }
}

public class ChatResponse
{
    public string Reply { get; set; } = string.Empty;
}
