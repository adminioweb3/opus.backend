using MediatR;
using Citationly.Application.Interfaces;
using System.Text.Json;

namespace Citationly.Application.Features.Onboarding;

public class SuggestKeywordsCommand : IRequest<List<string>>
{
    public string WebsiteUrl { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? Industry { get; set; }
}

public class SuggestKeywordsCommandHandler : IRequestHandler<SuggestKeywordsCommand, List<string>>
{
    private readonly IOpenAiService _openAiService;

    public SuggestKeywordsCommandHandler(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public async Task<List<string>> Handle(SuggestKeywordsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var userPrompt = $@"Suggest 10 highly relevant keywords for this business. Return ONLY a JSON array of keywords.

Business: {request.BusinessName}
Website: {request.WebsiteUrl}
{(string.IsNullOrEmpty(request.Industry) ? "" : $"Industry: {request.Industry}")}

Return ONLY this JSON format, no markdown:
[""keyword1"", ""keyword2"", ""keyword3"", ""keyword4"", ""keyword5"", ""keyword6"", ""keyword7"", ""keyword8"", ""keyword9"", ""keyword10""]

Ensure keywords are:
- Specific to the business
- Search-friendly
- Realistic for the industry
- Varied in length (1-3 words each)";

            var response = await _openAiService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: "You are a keyword research expert. Generate relevant keywords for businesses.",
                requireJson: true,
                model: "gpt-4o-mini");

            response = response.Trim();
            if (response.StartsWith("```json")) response = response.Substring(7);
            if (response.StartsWith("```")) response = response.Substring(3);
            if (response.EndsWith("```")) response = response.Substring(0, response.Length - 3);
            response = response.Trim();

            var keywords = JsonSerializer.Deserialize<List<string>>(response) ?? new List<string>();
            return keywords.Take(10).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Keyword suggestion failed: {ex.Message}");
            return new List<string>();
        }
    }
}
