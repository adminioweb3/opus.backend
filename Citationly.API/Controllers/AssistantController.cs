using System.Text.Json;
using System.Text.RegularExpressions;
using Citationly.API.Services;
using Citationly.Application.Features.Assistant.Pipeline;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public partial class AssistantController : ControllerBase
{
    private static readonly JsonSerializerOptions StreamJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICurrentOrganizationAccessor _currentOrg;
    private readonly IAssistantConversationRepository _conversations;

    public AssistantController(ICurrentOrganizationAccessor currentOrg, IAssistantConversationRepository conversations)
    {
        _currentOrg = currentOrg;
        _conversations = conversations;
    }

    [HttpGet("threads")]
    public async Task<IActionResult> GetThreads(CancellationToken cancellationToken)
    {
        var caller = await _currentOrg.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();
        var threads = await _conversations.GetThreadsAsync(caller.Value.OrganizationId, caller.Value.UserId, cancellationToken: cancellationToken);
        return Ok(threads.Select(ToThreadResponse));
    }

    [HttpPost("threads")]
    public async Task<IActionResult> CreateThread([FromBody] CreateThreadRequest? request, CancellationToken cancellationToken)
    {
        var caller = await _currentOrg.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();
        var thread = await _conversations.CreateThreadAsync(caller.Value.OrganizationId, caller.Value.UserId, request?.Title ?? "New conversation", cancellationToken);
        return CreatedAtAction(nameof(GetThread), new { threadId = thread.Id }, ToThreadResponse(thread));
    }

    [HttpGet("threads/{threadId:guid}")]
    public async Task<IActionResult> GetThread(Guid threadId, CancellationToken cancellationToken)
    {
        var caller = await _currentOrg.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();
        var thread = await _conversations.GetThreadAsync(threadId, caller.Value.OrganizationId, caller.Value.UserId, cancellationToken);
        if (thread == null) return NotFound();
        var messages = await _conversations.GetMessagesAsync(threadId, caller.Value.OrganizationId, caller.Value.UserId, cancellationToken: cancellationToken);
        return Ok(new { thread = ToThreadResponse(thread), messages = messages.Select(ToMessageResponse) });
    }

    [HttpPatch("threads/{threadId:guid}")]
    public async Task<IActionResult> RenameThread(Guid threadId, [FromBody] RenameThreadRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest(new { error = "Title cannot be empty." });
        var caller = await _currentOrg.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();
        var changed = await _conversations.RenameThreadAsync(threadId, caller.Value.OrganizationId, caller.Value.UserId, request.Title, cancellationToken);
        return changed ? NoContent() : NotFound();
    }

    [HttpDelete("threads/{threadId:guid}")]
    public async Task<IActionResult> DeleteThread(Guid threadId, CancellationToken cancellationToken)
    {
        var caller = await _currentOrg.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();
        var deleted = await _conversations.DeleteThreadAsync(threadId, caller.Value.OrganizationId, caller.Value.UserId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("threads/{threadId:guid}/messages")]
    public async Task<IActionResult> SendMessage(Guid threadId, [FromBody] SendAssistantMessageRequest request, [FromServices] AgentOrchestrator orchestrator, CancellationToken cancellationToken)
    {
        var content = request.Message?.Trim();
        if (string.IsNullOrWhiteSpace(content)) return BadRequest(new { error = "Message cannot be empty." });
        if (content.Length > 8_000) return BadRequest(new { error = "Message is too long." });

        var caller = await _currentOrg.GetCurrentUserAsync(User, cancellationToken);
        if (caller == null) return Unauthorized();
        var thread = await _conversations.GetThreadAsync(threadId, caller.Value.OrganizationId, caller.Value.UserId, cancellationToken);
        if (thread == null) return NotFound();

        var history = await _conversations.GetMessagesAsync(threadId, caller.Value.OrganizationId, caller.Value.UserId, cancellationToken: cancellationToken);
        var userMessage = await _conversations.AddMessageAsync(threadId, caller.Value.OrganizationId, caller.Value.UserId, "user", content, cancellationToken);
        if (userMessage == null) return NotFound();
        if (history.Count == 0 && thread.Title == "New conversation")
            await _conversations.RenameThreadAsync(threadId, caller.Value.OrganizationId, caller.Value.UserId, content, cancellationToken);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";
        await WriteEventAsync("message.started", new { message = ToMessageResponse(userMessage) }, cancellationToken);

        try
        {
            string? finalResponse = null;
            var modelMessage = request.Mode == "inspect" ? $"Inspect the available Citationly workspace evidence for this request: {content}" : content;
            await foreach (var item in orchestrator.ExecutePipelineAsync(caller.Value.OrganizationId, modelMessage, history, cancellationToken))
            {
                if (item == "STATUS_DONE") continue;
                if (item.StartsWith("RESPONSE:", StringComparison.Ordinal))
                {
                    finalResponse = item[9..];
                    foreach (Match match in ResponseChunkRegex().Matches(finalResponse))
                        await WriteEventAsync("message.delta", new { delta = match.Value }, cancellationToken);
                }
                else
                {
                    await WriteEventAsync("run.status", new { status = item }, cancellationToken);
                }
            }

            finalResponse ??= "I could not generate a response. Please try again.";
            var assistantMessage = await _conversations.AddMessageAsync(threadId, caller.Value.OrganizationId, caller.Value.UserId, "assistant", finalResponse, cancellationToken);
            await WriteEventAsync("message.completed", new { message = assistantMessage == null ? null : ToMessageResponse(assistantMessage) }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user message remains available for retry when generation is stopped.
        }
        catch (Exception)
        {
            if (!HttpContext.RequestAborted.IsCancellationRequested)
                await WriteEventAsync("error", new { message = "Citationly could not complete that response. Please try again." }, cancellationToken);
        }

        return new EmptyResult();
    }

    private async Task WriteEventAsync(string eventName, object payload, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"event: {eventName}\ndata: {JsonSerializer.Serialize(payload, StreamJsonOptions)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static object ToThreadResponse(AssistantThread thread) => new { thread.Id, thread.Title, thread.CreatedAt, thread.UpdatedAt };
    private static object ToMessageResponse(AssistantMessage message) => new { message.Id, message.Role, message.Content, message.CreatedAt };

    [GeneratedRegex(@"\S+\s*|\s+")]
    private static partial Regex ResponseChunkRegex();
}

public sealed class CreateThreadRequest
{
    public string? Title { get; set; }
}

public sealed class RenameThreadRequest
{
    public string Title { get; set; } = string.Empty;
}

public sealed class SendAssistantMessageRequest
{
    public string Message { get; set; } = string.Empty;
    public string Mode { get; set; } = "ask";
}
