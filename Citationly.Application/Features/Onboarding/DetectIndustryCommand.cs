using MediatR;
using Citationly.Application.Interfaces;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Citationly.Application.Features.Onboarding;

public class DetectIndustryCommand : IRequest<DetectIndustryResult>
{
    public string WebsiteUrl { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
}

public class DetectIndustryResult
{
    [JsonPropertyName("industry")]
    public string Industry { get; set; } = string.Empty;

    [JsonPropertyName("alternatives")]
    public List<string> Alternatives { get; set; } = new();

    [JsonPropertyName("confidence")]
    public int Confidence { get; set; }
}

public class DetectIndustryCommandHandler : IRequestHandler<DetectIndustryCommand, DetectIndustryResult>
{
    private readonly IOpenAiService _openAiService;

    public DetectIndustryCommandHandler(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public async Task<DetectIndustryResult> Handle(DetectIndustryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var userPrompt = $@"Detect the industry of this business and suggest alternatives. Return ONLY valid JSON.

Business: {request.BusinessName}
Website: {request.WebsiteUrl}

Return this exact JSON format, no markdown:
{{
  ""industry"": ""Primary Industry Name"",
  ""alternatives"": [""Alternative 1"", ""Alternative 2"", ""Alternative 3""],
  ""confidence"": 85
}}

Industry options: SaaS, E-commerce, Healthcare, Finance, Manufacturing, Retail, Real Estate, Education, Hospitality, Technology, Marketing, Consulting, Other

Confidence is 0-100. Be precise based on the business name and domain.";

            var response = await _openAiService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: "You are an industry classification expert. Analyze businesses and suggest their industry category.",
                requireJson: true,
                model: "gpt-4o-mini");

            response = response.Trim();
            if (response.StartsWith("```json")) response = response.Substring(7);
            if (response.StartsWith("```")) response = response.Substring(3);
            if (response.EndsWith("```")) response = response.Substring(0, response.Length - 3);
            response = response.Trim();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var result = JsonSerializer.Deserialize<DetectIndustryResult>(response, options)
                ?? new DetectIndustryResult { Industry = "Other", Confidence = 0 };

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Industry detection failed: {ex.Message}");
            return new DetectIndustryResult { Industry = "", Confidence = 0 };
        }
    }
}
