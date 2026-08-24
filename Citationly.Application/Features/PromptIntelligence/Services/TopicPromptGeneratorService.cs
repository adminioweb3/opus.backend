using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface ITopicPromptGeneratorService
{
    Task<List<string>> GeneratePromptsAsync(Guid topicId, string topicName, int count, CancellationToken ct, string? brandName = null, string? brandWebsite = null);
}

/// <summary>
/// Generates brand-aware prompts that real prospects would ask AI search engines — scoped to one
/// topic and the organization's own brand context, so analysis runs return meaningful visibility
/// scores instead of 0 for every generic SaaS/tech question.
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

    private readonly IOpenAiService _openAiService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IPromptIntelligenceRepository _repo;

    public TopicPromptGeneratorService(
        IOpenAiService openAiService,
        IEmbeddingService embeddingService,
        IPromptIntelligenceRepository repo)
    {
        _openAiService = openAiService;
        _embeddingService = embeddingService;
        _repo = repo;
    }

    public async Task<List<string>> GeneratePromptsAsync(Guid topicId, string topicName, int count, CancellationToken ct, string? brandName = null, string? brandWebsite = null)
    {
        var raw = await GenerateRawAsync(topicName, count + GenerationHeadroom, ct, brandName, brandWebsite);
        if (raw.Count == 0) return raw;

        var existingQuestions = await _repo.GetQuestionsByTopicAsync(topicId);
        var existingTexts = existingQuestions.Select(q => q.PromptText).ToList();

        return await DeduplicateAsync(raw, existingTexts, count, ct);
    }

    private async Task<List<string>> GenerateRawAsync(string topicName, int requestCount, CancellationToken ct, string? brandName, string? brandWebsite)
    {
        const string systemPrompt = "You are an expert AI Search Prompt Generator for a brand visibility and AEO (Answer Engine Optimization) tool. Your goal is to generate prompts that real prospects would search for, and that could plausibly surface the evaluated brand in an AI answer.";

        var brandContext = !string.IsNullOrWhiteSpace(brandName)
            ? $"\nBrand being tracked: '{brandName}' (website: {brandWebsite ?? "unknown"}).\nGenerate prompts that someone in this brand's target market would realistically ask — where the brand might organically appear in an AI answer if they are well-positioned in this niche."
            : "";

        var userPrompt = $@"Generate {requestCount} realistic, distinct prompts that potential customers would ask AI search engines about the topic '{topicName}'.{brandContext}
Each prompt should:
- Sound like a genuine conversational question under 25 words
- Be specific enough that a real niche player (not just mega-brands) could be recommended
- NOT be generic 'What is X?' questions — instead ask things like 'best X for Y', 'who offers X in Y space', 'how to do X for Y use case'
- Be meaningfully DIFFERENT from each other — vary the angle, persona, or use case, not just the wording of the same question
Respond with ONLY JSON: {{""prompts"": [string, ...]}}. Do not wrap in markdown.";

        var result = new List<string>();
        try
        {
            var content = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);
            content = StripFences(content);
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("prompts", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) result.Add(text.Trim());
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
