using MediatR;
using Citationly.Application.Interfaces;
using System.Text.Json;

namespace Citationly.Application.Features.Onboarding;

public class DetectOfferingCommand : IRequest<DetectOfferingResult>
{
    public string WebsiteUrl { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
}

public class DetectOfferingResult
{
    public string Offering { get; set; } = string.Empty;
    public int Confidence { get; set; }
}

public class DetectOfferingCommandHandler : IRequestHandler<DetectOfferingCommand, DetectOfferingResult>
{
    private readonly IOpenAiService _openAiService;

    public DetectOfferingCommandHandler(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public async Task<DetectOfferingResult> Handle(DetectOfferingCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var userPrompt = $@"Identify the SINGLE main thing this business sells — one line, the primary offering
a buyer would name if asked ""what do they sell?"". For a multi-product business, pick the one
offering the business is most known for or leads with, not a list of everything they do.

Business: {request.BusinessName}
Website: {request.WebsiteUrl}

Return this exact JSON format, no markdown:
{{
  ""offering"": ""One-line primary offering"",
  ""confidence"": 85
}}

Confidence is 0-100. Be precise based on the business name and domain.";

            var response = await _openAiService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: "You identify the single primary offering of a business in one line.",
                requireJson: true,
                model: "gpt-4o-mini");

            response = response.Trim();
            if (response.StartsWith("```json")) response = response.Substring(7);
            if (response.StartsWith("```")) response = response.Substring(3);
            if (response.EndsWith("```")) response = response.Substring(0, response.Length - 3);
            response = response.Trim();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var result = JsonSerializer.Deserialize<DetectOfferingResult>(response, options)
                ?? new DetectOfferingResult { Offering = "", Confidence = 0 };

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Offering detection failed: {ex.Message}");
            return new DetectOfferingResult { Offering = "", Confidence = 0 };
        }
    }
}
