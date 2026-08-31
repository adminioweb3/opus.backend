using Citationly.API.Services;
using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    private readonly ICurrentOrganizationAccessor _currentOrg;

    public AssistantController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ICurrentOrganizationAccessor currentOrg)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _currentOrg = currentOrg;
    }

    [HttpGet("recent")]
    public IActionResult GetRecentItems()
    {
        return Ok(Array.Empty<object>());
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, [FromServices] Citationly.Application.Features.Assistant.Pipeline.AgentOrchestrator orchestrator)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Message cannot be empty." });

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        
        await foreach (var status in orchestrator.ExecutePipelineAsync(orgId, request.Message, request.History ?? new List<ChatMessageDto>(), HttpContext.RequestAborted))
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
