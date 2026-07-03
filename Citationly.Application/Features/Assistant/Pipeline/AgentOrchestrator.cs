using System.Runtime.CompilerServices;
using System.Text.Json;
using Citationly.Application.Features.Assistant.Services;
using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Citationly.Application.Features.Assistant.Pipeline;

public class AgentOrchestrator
{
    private readonly IntentDetectionService _intentService;
    private readonly ToolExecutionService _toolService;
    private readonly ContextBuilderService _contextService;
    private readonly AnalyticsEngineService _analyticsService;
    private readonly PromptBuilderService _promptService;
    private readonly OpenAiClientService _openAiClient;

    public AgentOrchestrator(
        IntentDetectionService intentService,
        ToolExecutionService toolService,
        ContextBuilderService contextService,
        AnalyticsEngineService analyticsService,
        PromptBuilderService promptService,
        OpenAiClientService openAiClient)
    {
        _intentService = intentService;
        _toolService = toolService;
        _contextService = contextService;
        _analyticsService = analyticsService;
        _promptService = promptService;
        _openAiClient = openAiClient;
    }

    public async IAsyncEnumerable<string> ExecutePipelineAsync(
        Guid? organizationId, 
        string userMessage, 
        object history,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Step 1: Intent Detection
        yield return "Understanding your request...";
        var intentResult = await _intentService.DetectIntentAsync(userMessage, cancellationToken);

        // Step 2: Tool Detection & Execution
        yield return "Determining required datasets...";
        var rawToolData = await _toolService.ExecuteToolsAsync(organizationId, intentResult.RequiredTools, cancellationToken);

        // Step 3 & 4: Merge Results & Analytics Layer
        yield return "Loading visibility data and analyzing rankings...";
        var analyticsResult = _analyticsService.RunCalculations(rawToolData);
        var mergedContext = _contextService.BuildMergedContext(analyticsResult, rawToolData);

        // Step 5: Prompt Builder
        yield return "Preparing recommendations...";
        var finalPrompt = _promptService.BuildDynamicPrompt(userMessage, history, mergedContext, intentResult.ResponseMode);

        // Step 6: OpenAI Execution
        yield return "Generating final response...";
        
        // Return a special marker so the frontend knows the "thinking" is done
        yield return "STATUS_DONE";

        string finalResponse;
        try
        {
            finalResponse = await _openAiClient.GenerateResponseAsync(finalPrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            finalResponse = $"OpenAI Error: {ex.Message}";
        }
        
        // Finally yield the actual response content
        yield return $"RESPONSE:{finalResponse}";
    }
}
