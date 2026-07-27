using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface IPromptTopicSeedingService
{
    /// <summary>
    /// Imports the org's already-generated AiSearchPrompt rows into PromptTopic/PromptQuestion,
    /// grouped by AiSearchPrompt.Topic, the first time the org has zero PromptTopic rows. A no-op
    /// on every call after the first. Returns true if seeding actually happened this call.
    /// </summary>
    Task<bool> EnsureSeededAsync(Guid organizationId);
}

public class PromptTopicSeedingService : IPromptTopicSeedingService
{
    private readonly IPromptIntelligenceRepository _repo;
    private readonly IWebsiteRepository _websiteRepository;

    public PromptTopicSeedingService(IPromptIntelligenceRepository repo, IWebsiteRepository websiteRepository)
    {
        _repo = repo;
        _websiteRepository = websiteRepository;
    }

    public async Task<bool> EnsureSeededAsync(Guid organizationId)
    {
        var existingTopics = await _repo.GetTopicsAsync(organizationId);
        if (existingTopics.Any()) return false;

        var existingPrompts = await _websiteRepository.GetAiSearchPromptsAsync(organizationId);
        var groups = existingPrompts
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Topic) ? "General" : p.Topic!.Trim())
            .ToList();

        if (groups.Count == 0) return false;

        foreach (var group in groups)
        {
            var topicId = await _repo.CreateTopicAsync(new PromptTopic
            {
                OrganizationId = organizationId,
                Name = group.Key,
                Description = $"Imported from your generated prompt set ({group.Count()} prompts).",
            });

            var seenText = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prompt in group)
            {
                if (string.IsNullOrWhiteSpace(prompt.QueryString)) continue;
                if (!seenText.Add(prompt.QueryString.Trim())) continue; // dedupe identical prompt text within the topic

                await _repo.CreateQuestionAsync(new PromptQuestion
                {
                    PromptTopicId = topicId,
                    PromptText = prompt.QueryString.Trim(),
                    Region = string.IsNullOrWhiteSpace(prompt.Region) ? "Global" : prompt.Region.Trim(),
                    Persona = string.IsNullOrWhiteSpace(prompt.Persona) ? null : prompt.Persona.Trim(),
                });
            }
        }

        return true;
    }
}
