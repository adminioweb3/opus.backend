using Citationly.API.Services;
using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AnalysisController : ControllerBase
{
    private readonly IAnalysisRepository _repository;
    private readonly ICurrentOrganizationAccessor _currentOrg;
    private readonly IAnalysisOrchestrator _orchestrator;

    public AnalysisController(
        IAnalysisRepository repository,
        ICurrentOrganizationAccessor currentOrg,
        IAnalysisOrchestrator orchestrator)
    {
        _repository = repository;
        _currentOrg = currentOrg;
        _orchestrator = orchestrator;
    }

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatestDashboardSnapshot()
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null) return Unauthorized();

        var snapshot = await _repository.GetLatestDashboardSnapshotAsync(orgId.Value);
        
        if (snapshot == null) 
        {
            return Ok(null); // Return empty so the frontend knows no analysis has been run
        }
        return Ok(snapshot);
    }

    [HttpGet("stream")]
    public async Task ExecuteAnalysisStream([FromQuery] Guid? websiteId)
    {
        var orgId = await _currentOrg.GetOrganizationIdAsync(User);
        if (orgId == null)
        {
            Response.StatusCode = 401;
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            var stream = _orchestrator.ExecuteAnalysisStreamAsync(orgId.Value, websiteId, HttpContext.RequestAborted);

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
        catch (Exception ex)
        {
            var errorData = $"data: Error: {ex.Message}\n\n";
            var errorBytes = System.Text.Encoding.UTF8.GetBytes(errorData);
            await Response.Body.WriteAsync(errorBytes, 0, errorBytes.Length);
            await Response.Body.FlushAsync();
            
            var endData = $"data: [DONE]\n\n";
            var endBytes = System.Text.Encoding.UTF8.GetBytes(endData);
            await Response.Body.WriteAsync(endBytes, 0, endBytes.Length);
            await Response.Body.FlushAsync();
        }
    }
}
