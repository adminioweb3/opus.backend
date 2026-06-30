using System.Text.Json;
using System.Text.Json.Serialization;

namespace Citationly.Application.Features.Assistant.Services;

public class IntentDetectionResult
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "General Chat";
    
    [JsonPropertyName("requiredTools")]
    public string[] RequiredTools { get; set; } = Array.Empty<string>();
    
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 1.0;
    
    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "Normal";

    [JsonPropertyName("responseMode")]
    public string ResponseMode { get; set; } = "Consultant";
}

public class IntentDetectionService
{
    private readonly OpenAiClientService _openAi;

    public IntentDetectionService(OpenAiClientService openAi)
    {
        _openAi = openAi;
    }

    public async Task<IntentDetectionResult> DetectIntentAsync(string userMessage, CancellationToken ct)
    {
        var prompt = $@"Classify the intent of this user message: ""{userMessage}""
Respond ONLY with valid JSON.
Fields required: intent (string), requiredTools (array of strings from: Visibility Tool, Competitor Tool, SEO Tool, Website Tool), confidence (number 0-1), priority (Low/Normal/High), responseMode (Quick Answer/Consultant/Research/Coding/Report).";

        var json = await _openAi.GenerateResponseFastAsync(prompt, ct);
        try
        {
            var result = JsonSerializer.Deserialize<IntentDetectionResult>(json);
            return result ?? new IntentDetectionResult();
        }
        catch
        {
            return new IntentDetectionResult();
        }
    }
}
