using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface ITopicPromptGeneratorService
{
    Task<List<string>> GeneratePromptsAsync(Guid organizationId, Guid topicId, string topicName, int count, CancellationToken ct, string? brandName = null, string? brandWebsite = null);
}

/// <summary>
/// Generates a stable brand-neutral prompt panel for organic visibility measurement. Branded
/// comparison questions are a different metric and must not be inserted into this panel because
/// naming the tracked brand structurally increases its mention rate.
///
/// Phase 3 B2: exact-string dedup only ever ran once, during initial topic seeding
/// (PromptTopicSeedingService) - a repeat call to this generator had no protection at all, so
/// generating "more prompts" on an established topic could and did create near-duplicates. This
/// now checks every new candidate (exact match, then embedding cosine similarity) against both
/// the topic's existing questions and the rest of the freshly-generated batch before returning it.
/// </summary>
public class TopicPromptGeneratorService : ITopicPromptGeneratorService
{
    /// <summary>Cosine similarity at or above this is treated as a near-duplicate, not a
    /// distinct prompt. Conservative - two prompts can share a lot of vocabulary about the same
    /// topic without being the same question.</summary>
    private const double SimilarityThreshold = 0.92;

    /// <summary>Ask for a few more than requested since dedup will reject some.</summary>
    private const int GenerationHeadroom = 6;

    private readonly IAiCompletionService _aiCompletionService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IPromptIntelligenceRepository _repo;

    public TopicPromptGeneratorService(
        IAiCompletionService aiCompletionService,
        IEmbeddingService embeddingService,
        IPromptIntelligenceRepository repo)
    {
        _aiCompletionService = aiCompletionService;
        _embeddingService = embeddingService;
        _repo = repo;
    }

    public async Task<List<string>> GeneratePromptsAsync(Guid organizationId, Guid topicId, string topicName, int count, CancellationToken ct, string? brandName = null, string? brandWebsite = null)
    {
        var neutralTarget = count;
        var neutralFoundation = BuildNeutralFoundationPrompts(topicName, neutralTarget);
        var remainingNeutral = Math.Max(0, neutralTarget - neutralFoundation.Count);
        var raw = remainingNeutral == 0
            ? new List<string>()
            : await GenerateRawAsync(organizationId, topicName, remainingNeutral + GenerationHeadroom, ct, brandName, brandWebsite);
        var candidates = neutralFoundation.Concat(raw).ToList();
        if (candidates.Count == 0) return candidates;

        var existingQuestions = await _repo.GetQuestionsByTopicAsync(topicId);
        var existingTexts = existingQuestions.Select(q => q.PromptText).ToList();

        return await DeduplicateAsync(candidates, existingTexts, count, ct);
    }

    private static List<string> BuildNeutralFoundationPrompts(string topicName, int targetCount)
    {
        if (targetCount <= 0) return new List<string>();

        var conciseTopic = string.Join(' ', topicName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(14));
        var templates = new[]
        {
            $"Which companies specialize in {conciseTopic}?",
            $"What are the best providers for {conciseTopic}?",
            $"Which {conciseTopic} companies should buyers shortlist?",
            $"Which providers offer end-to-end {conciseTopic} services?",
            $"How do leading {conciseTopic} companies compare?",
            $"Who are reputable specialists in {conciseTopic}?",
            $"Which companies have demonstrated experience in {conciseTopic}?",
            $"What {conciseTopic} providers are worth considering?",
        };

        return templates.Take(Math.Min(targetCount, templates.Length)).ToList();
    }

    private async Task<List<string>> GenerateRawAsync(Guid organizationId, string topicName, int requestCount, CancellationToken ct, string? brandName, string? brandWebsite)
    {
        const string systemPrompt = "You design statistically useful prompt panels for AI-search visibility measurement. Generate natural buyer questions that are brand-neutral, category-specific, repeatable over time, and capable of returning several real providers. Never insert or favor the tracked brand.";

        var brandContext = !string.IsNullOrWhiteSpace(brandName)
            ? $"\nPrivate category context (do not repeat in any question): the measured company is '{brandName}' ({brandWebsite ?? "website unknown"}). Use this only to understand the market. Every output question must omit this company and all supplied company names."
            : "";

        var userPrompt = $@"Generate {requestCount} realistic, distinct prompts that potential customers would ask AI search engines about the topic '{topicName}'.{brandContext}
Each prompt should:
- Sound like a genuine conversational question under 25 words
- Ask the assistant to discover, shortlist, compare, or recommend providers/products
- Be specific enough that multiple real niche providers, not only mega-brands, are eligible
- Prefer broad category discovery. Add at most one audience, constraint, use case, or geography only when it is explicitly present in the topic text
- Never invent or stack company stage, industry, geography, budget, technology, or compliance requirements
- Exclude educational questions answerable without naming a provider
- Exclude the tracked brand, known vendor names, invented companies, and leading language tailored to one company
- Cover a balanced mix of discovery, best-provider, requirements, alternatives, comparisons, and commercial intent
- Be meaningfully different from the others in buyer intent or use case, not merely wording
Respond with ONLY JSON: {{""prompts"": [string, ...]}}. Do not wrap in markdown.";

        var result = new List<string>();
        try
        {
            var completion = await _aiCompletionService.CompleteAsync(
                organizationId,
                "prompt_intelligence.topic_prompt_generation",
                userPrompt,
                systemPrompt,
                requireJson: true,
                preferredProviderKey: "openai",
                ct);
            if (!completion.Success) return result;

            var content = StripFences(completion.Content);
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("prompts", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var text = item.GetString();
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var candidate = text.Trim();
                    if (!string.IsNullOrWhiteSpace(brandName) && ContainsEntity(candidate, brandName)) continue;
                    if (candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 25) continue;
                    result.Add(candidate);
                }
            }
        }
        catch
        {
            // Leave the result empty on failure — no fabricated prompts.
        }

        return result;
    }

    private async Task<List<string>> DeduplicateAsync(List<string> candidates, List<string> existingTexts, int targetCount, CancellationToken ct)
    {
        var accepted = new List<string>();
        var acceptedEmbeddings = new List<double[]>();

        // Exact-match dedup first (cheap, catches the common case with no API calls at all).
        var seenExact = new HashSet<string>(existingTexts.Select(Normalize), StringComparer.Ordinal);

        // Existing questions' embeddings are computed on demand rather than stored, since
        // PromptQuestion has no embedding column yet — acceptable cost for a dedup check that
        // only runs when a human explicitly asks to generate more prompts for a topic.
        var existingEmbeddings = new List<double[]>();
        foreach (var text in existingTexts)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(text, ct);
            if (embedding != null) existingEmbeddings.Add(embedding);
        }

        foreach (var candidate in candidates)
        {
            if (accepted.Count >= targetCount) break;

            var normalized = Normalize(candidate);
            if (!seenExact.Add(normalized)) continue;

            var candidateEmbedding = await _embeddingService.GenerateEmbeddingAsync(candidate, ct);
            if (candidateEmbedding == null)
            {
                // Couldn't verify via embedding (service unavailable) — exact-match dedup above
                // already ran, so fail open rather than blocking generation entirely.
                accepted.Add(candidate);
                continue;
            }

            var isDuplicate = existingEmbeddings.Any(e => CosineSimilarity(candidateEmbedding, e) >= SimilarityThreshold)
                || acceptedEmbeddings.Any(e => CosineSimilarity(candidateEmbedding, e) >= SimilarityThreshold);

            if (isDuplicate) continue;

            accepted.Add(candidate);
            acceptedEmbeddings.Add(candidateEmbedding);
        }

        return accepted;
    }

    private static string Normalize(string text) => text.Trim().ToLowerInvariant();

    private static bool ContainsEntity(string text, string entity) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            text,
            $@"(?<![\p{{L}}\p{{N}}]){System.Text.RegularExpressions.Regex.Escape(entity.Trim())}(?![\p{{L}}\p{{N}}])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static double CosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        if (magA == 0 || magB == 0) return 0;
        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }

    private static string StripFences(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```json")) s = s[7..];
        else if (s.StartsWith("```")) s = s[3..];
        if (s.EndsWith("```")) s = s[..^3];
        return s.Trim();
    }
}
