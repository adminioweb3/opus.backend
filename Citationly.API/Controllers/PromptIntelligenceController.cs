using System.Security.Claims;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Citationly.Application.Features.PromptIntelligence.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PromptIntelligenceController : ControllerBase
{
    private readonly IPromptIntelligenceRepository _repo;
    private readonly IUserRepository _userRepository;
    private readonly IPromptExecutionService _executionService;

    public PromptIntelligenceController(
        IPromptIntelligenceRepository repo, 
        IUserRepository userRepository,
        IPromptExecutionService executionService)
    {
        _repo = repo;
        _userRepository = userRepository;
        _executionService = executionService;
    }

    private async Task<Guid?> GetOrganizationIdAsync()
    {
        var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(firebaseUid)) return null;

        var user = await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
        return user?.OrganizationId;
    }

    [HttpGet("topics")]
    public async Task<IActionResult> GetTopics()
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        var topics = await _repo.GetTopicsAsync(orgId.Value);
        return Ok(topics);
    }

    [HttpPost("topics")]
    public async Task<IActionResult> CreateTopic([FromBody] PromptTopic topic)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null) return Unauthorized();

        topic.OrganizationId = orgId.Value;
        topic.Id = await _repo.CreateTopicAsync(topic);
        return Ok(topic);
    }

    [HttpGet("topics/{topicId}/questions")]
    public async Task<IActionResult> GetQuestions(Guid topicId)
    {
        var questions = await _repo.GetQuestionsByTopicAsync(topicId);
        
        // Add latest visibility data to each question
        var results = new List<object>();
        foreach (var q in questions)
        {
            var analysis = await _repo.GetLatestAnalysisAsync(q.Id);
            PromptVisibility? vis = null;
            if (analysis != null && analysis.Status == "Completed")
            {
                vis = await _repo.GetVisibilityAsync(analysis.Id);
            }
            results.Add(new { Question = q, LatestAnalysis = analysis, Visibility = vis });
        }

        return Ok(results);
    }

    [HttpPost("questions")]
    public async Task<IActionResult> CreateQuestion([FromBody] PromptQuestion question)
    {
        question.Id = await _repo.CreateQuestionAsync(question);
        return Ok(question);
    }

    [HttpGet("analyses/{analysisId}")]
    public async Task<IActionResult> GetAnalysisResults(Guid analysisId)
    {
        var visibility = await _repo.GetVisibilityAsync(analysisId);
        var mentions = await _repo.GetMentionsAsync(analysisId);
        var responses = await _repo.GetResponsesAsync(analysisId);
        var recommendations = await _repo.GetRecommendationsAsync(analysisId);
        var competitors = await _repo.GetCompetitorComparisonsAsync(analysisId);

        return Ok(new
        {
            Visibility = visibility,
            Mentions = mentions,
            Responses = responses,
            Recommendations = recommendations,
            CompetitorComparisons = competitors
        });
    }

    [HttpGet("analyze/stream/{questionId}")]
    public async Task ExecuteAnalysisStream(Guid questionId)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId == null)
        {
            Response.StatusCode = 401;
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var stream = _executionService.ExecutePromptAnalysisAsync(orgId.Value, questionId, HttpContext.RequestAborted);

        await foreach (var message in stream)
        {
            var data = $"data: {message}\n\n";
            var bytes = System.Text.Encoding.UTF8.GetBytes(data);
            await Response.Body.WriteAsync(bytes, 0, bytes.Length);
            await Response.Body.FlushAsync();
        }

        var endData = $"data: [DONE]\n\n";
        var endBytes = System.Text.Encoding.UTF8.GetBytes(endData);
        await Response.Body.WriteAsync(endBytes, 0, endBytes.Length);
        await Response.Body.FlushAsync();
    }
}
