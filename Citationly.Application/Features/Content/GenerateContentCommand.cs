using System.Text;
using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Content;

public class GenerateContentCommand : IRequest<ContentDraft>
{
    public Guid OrganizationId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string? Keywords { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string OutputLength { get; set; } = string.Empty;
    public string BrandVoice { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = string.Empty;
    public int Creativity { get; set; }
    public string? TargetKeyword { get; set; }
    public string? BusinessName { get; set; }
    public string? TargetAudience { get; set; }
    public string? SearchIntent { get; set; }
    public string? Goal { get; set; }
    public string? Cta { get; set; }
    public string? ReferenceUrls { get; set; }
    public string? SecondaryAngle { get; set; }
    public string? CompetitorUrl { get; set; }
    public string? CompetitorInsights { get; set; }
    public string? Model { get; set; }
}

public class GenerateContentCommandHandler : IRequestHandler<GenerateContentCommand, ContentDraft>
{
    private static readonly Dictionary<string, string> ModelMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GPT-4.1"] = "gpt-4.1",
        ["GPT-4o"] = "gpt-4o",
        ["GPT-4.1 mini"] = "gpt-4.1-mini",
    };

    private readonly IContentDraftRepository _repository;
    private readonly IOpenAiService _openAiService;

    public GenerateContentCommandHandler(IContentDraftRepository repository, IOpenAiService openAiService)
    {
        _repository = repository;
        _openAiService = openAiService;
    }

    public async Task<ContentDraft> Handle(GenerateContentCommand request, CancellationToken cancellationToken)
    {
        var systemPrompt = BuildSystemPrompt(request);
        var userPrompt = BuildUserPrompt(request);
        var model = request.Model != null && ModelMap.TryGetValue(request.Model, out var mapped) ? mapped : "gpt-4o-mini";

        var content = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, false, model);

        var draft = new ContentDraft
        {
            OrganizationId = request.OrganizationId,
            Title = ExtractTitle(content, request),
            ContentType = request.ContentType,
            Content = content,
            WordCount = CountWords(content),
            Status = "Draft",
            CompetitorUrl = request.CompetitorUrl,
            RequestJson = JsonSerializer.Serialize(request)
        };

        return await _repository.CreateAsync(draft);
    }

    private static string BuildSystemPrompt(GenerateContentCommand r)
    {
        return $"You are an expert content writer producing {r.ContentType} content. " +
               $"Write in a {r.Tone} tone, in {r.Language}, for an audience of {r.Audience}. " +
               $"Target output length: {r.OutputLength}. Brand voice: {r.BrandVoice}. Output format: {r.OutputFormat}. " +
               "Write the full, publish-ready content in well-structured Markdown, starting with a single '#' H1 title line. " +
               "Do not include any meta-commentary, explanations, or notes about the content — output only the content itself.";
    }

    private static string BuildUserPrompt(GenerateContentCommand r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Prompt);
        if (!string.IsNullOrWhiteSpace(r.Keywords)) sb.AppendLine($"\nTarget keywords: {r.Keywords}");
        if (!string.IsNullOrWhiteSpace(r.TargetKeyword)) sb.AppendLine($"Primary target keyword: {r.TargetKeyword}");
        if (!string.IsNullOrWhiteSpace(r.BusinessName)) sb.AppendLine($"Business name: {r.BusinessName}");
        if (!string.IsNullOrWhiteSpace(r.TargetAudience)) sb.AppendLine($"Target audience: {r.TargetAudience}");
        if (!string.IsNullOrWhiteSpace(r.SearchIntent)) sb.AppendLine($"Search intent: {r.SearchIntent}");
        if (!string.IsNullOrWhiteSpace(r.Goal)) sb.AppendLine($"Content goal: {r.Goal}");
        if (!string.IsNullOrWhiteSpace(r.Cta)) sb.AppendLine($"Call to action to include: {r.Cta}");
        if (!string.IsNullOrWhiteSpace(r.ReferenceUrls)) sb.AppendLine($"Reference URLs for context: {r.ReferenceUrls}");
        if (!string.IsNullOrWhiteSpace(r.SecondaryAngle)) sb.AppendLine($"Secondary angle to weave in: {r.SecondaryAngle}");
        if (!string.IsNullOrWhiteSpace(r.CompetitorInsights)) sb.AppendLine($"\nOutperform this competitor analysis by directly addressing its gaps:\n{r.CompetitorInsights}");
        return sb.ToString();
    }

    private static string ExtractTitle(string content, GenerateContentCommand r)
    {
        var firstLine = content
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;
        firstLine = firstLine.TrimStart('#').Trim();
        return string.IsNullOrWhiteSpace(firstLine) ? (r.TargetKeyword ?? r.ContentType) : firstLine;
    }

    private static int CountWords(string content)
    {
        return content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
