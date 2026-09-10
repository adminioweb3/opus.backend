using System.Text.Json;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface IEntityRecommendationClassifierService
{
    Task<IReadOnlyDictionary<string, int>> ClassifyAsync(
        Guid organizationId,
        string responseText,
        IReadOnlyCollection<string> mentionedEntities,
        CancellationToken cancellationToken);
}

public sealed class EntityRecommendationClassifierService : IEntityRecommendationClassifierService
{
    private readonly IAiCompletionService _aiCompletionService;

    public EntityRecommendationClassifierService(IAiCompletionService aiCompletionService)
    {
        _aiCompletionService = aiCompletionService;
    }

    public async Task<IReadOnlyDictionary<string, int>> ClassifyAsync(
        Guid organizationId,
        string responseText,
        IReadOnlyCollection<string> mentionedEntities,
        CancellationToken cancellationToken)
    {
        if (mentionedEntities.Count == 0 || string.IsNullOrWhiteSpace(responseText))
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        const string systemPrompt =
            "Classify recommendation evidence using only the supplied response. An entity is recommended only when " +
            "the response presents it as a suitable choice, shortlist option, or preferred solution. A neutral mention " +
            "or negative comparison is not a recommendation. Return only JSON with a recommendations array. " +
            "Each item must contain an exact entity name from the supplied list and its 1-based order among recommended entities.";

        var entities = string.Join("\n", mentionedEntities.Select(name => $"- {name}"));
        var boundedResponse = responseText.Length > 12000 ? responseText[..12000] : responseText;
        var userPrompt = $"ENTITIES:\n{entities}\n\nRESPONSE:\n{boundedResponse}\n\n" +
                         "Return: {\"recommendations\":[{\"entity\":\"exact name\",\"position\":1}]}";

        try
        {
            var completion = await _aiCompletionService.CompleteAsync(
                organizationId,
                "prompt-intelligence.entity-recommendations",
                userPrompt,
                systemPrompt,
                requireJson: true,
                preferredProviderKey: "openai",
                cancellationToken);
            if (!completion.Success) return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(StripFences(completion.Content));
            if (!document.RootElement.TryGetProperty("recommendations", out var recommendations) ||
                recommendations.ValueKind != JsonValueKind.Array)
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var allowed = mentionedEntities.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in recommendations.EnumerateArray())
            {
                var entity = item.TryGetProperty("entity", out var entityElement) ? entityElement.GetString()?.Trim() : null;
                if (entity == null || !allowed.Contains(entity) || result.ContainsKey(entity)) continue;

                var position = item.TryGetProperty("position", out var positionElement) && positionElement.TryGetInt32(out var parsed)
                    ? Math.Clamp(parsed, 1, 100)
                    : result.Count + 1;
                result[entity] = position;
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string StripFences(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

        var firstLineEnd = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd >= 0 && lastFence > firstLineEnd
            ? trimmed[(firstLineEnd + 1)..lastFence].Trim()
            : trimmed;
    }
}
