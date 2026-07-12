using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.KnowledgeBases;

public class AskKnowledgeBaseQuery : IRequest<KnowledgeBaseAnswer>
{
    public Guid OrganizationId { get; set; }
    public Guid KnowledgeBaseId { get; set; }
    public string Question { get; set; } = string.Empty;
}

public class KnowledgeBaseAnswer
{
    public string Answer { get; set; } = string.Empty;
    public List<KnowledgeBaseAnswerSource> Sources { get; set; } = new();
}

public class KnowledgeBaseAnswerSource
{
    public Guid PageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class AskKnowledgeBaseQueryHandler : IRequestHandler<AskKnowledgeBaseQuery, KnowledgeBaseAnswer>
{
    private const int MaxSourcePages = 5;
    private const int MaxContentCharsPerPage = 3000;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "are", "what", "which", "who", "how", "why", "do", "does", "did",
        "of", "in", "on", "for", "to", "and", "or", "my", "your", "this", "that", "it", "will",
        "can", "could", "should", "would", "i", "me", "we", "us", "about", "with"
    };

    private readonly IScrapingJobRepository _scrapingJobRepository;
    private readonly IOpenAiService _openAiService;

    public AskKnowledgeBaseQueryHandler(IScrapingJobRepository scrapingJobRepository, IOpenAiService openAiService)
    {
        _scrapingJobRepository = scrapingJobRepository;
        _openAiService = openAiService;
    }

    public async Task<KnowledgeBaseAnswer> Handle(AskKnowledgeBaseQuery request, CancellationToken cancellationToken)
    {
        var pages = await _scrapingJobRepository.GetPagesByKnowledgeBaseAsync(request.OrganizationId, request.KnowledgeBaseId);
        var withContent = pages.Where(p => !string.IsNullOrWhiteSpace(p.MarkdownContent) || !string.IsNullOrWhiteSpace(p.Content)).ToList();

        if (withContent.Count == 0)
        {
            return new KnowledgeBaseAnswer
            {
                Answer = "This knowledge base doesn't have any indexed sources yet. Scrape or crawl a page first, then ask again.",
            };
        }

        var keywords = ExtractKeywords(request.Question);
        var ranked = withContent
            .Select(p => new { Page = p, Score = ScoreRelevance(p, keywords) })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Page.ScrapedAt)
            .ToList();

        var topPages = (ranked.Any(x => x.Score > 0) ? ranked.Where(x => x.Score > 0) : ranked)
            .Take(MaxSourcePages)
            .Select(x => x.Page)
            .ToList();

        var context = string.Join("\n\n", topPages.Select((p, i) =>
        {
            var body = p.MarkdownContent ?? p.Content ?? string.Empty;
            if (body.Length > MaxContentCharsPerPage) body = body[..MaxContentCharsPerPage];
            return $"Source {i + 1}: {p.Title ?? p.Url}\nURL: {p.Url}\n---\n{body}";
        }));

        const string systemPrompt =
            "You are a helpful assistant answering questions using ONLY the knowledge base content provided below. " +
            "Give a thorough, well-structured, long-form answer in Markdown (use headings, bullet points, and bold where helpful). " +
            "Ground every claim in the provided sources, and reference the source number (e.g. \"(Source 2)\") when citing specific facts. " +
            "If the provided sources don't fully answer the question, say so honestly and answer as best you can from what's available, clearly noting the gap.";

        var prompt = $"KNOWLEDGE BASE CONTENT:\n\n{context}\n\nQUESTION: {request.Question}";

        var answer = await _openAiService.GenerateContentAsync(prompt, systemPrompt);

        return new KnowledgeBaseAnswer
        {
            Answer = answer,
            Sources = topPages.Select(p => new KnowledgeBaseAnswerSource
            {
                PageId = p.Id,
                Title = string.IsNullOrWhiteSpace(p.Title) ? p.Url : p.Title!,
                Url = p.Url
            }).ToList()
        };
    }

    private static List<string> ExtractKeywords(string question)
    {
        return question
            .Split(new[] { ' ', '\t', '\n', '?', '.', ',', '!', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim().ToLowerInvariant())
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .Distinct()
            .ToList();
    }

    private static int ScoreRelevance(ScrapedPage page, List<string> keywords)
    {
        if (keywords.Count == 0) return 0;
        var haystack = $"{page.Title} {page.Content} {page.MarkdownContent}".ToLowerInvariant();
        return keywords.Sum(k => CountOccurrences(haystack, k));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
