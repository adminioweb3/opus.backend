using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IPromptIntelligenceRepository
{
    // Topics & Questions
    Task<IEnumerable<PromptTopic>> GetTopicsAsync(Guid organizationId);
    Task<PromptTopic?> GetTopicAsync(Guid topicId);
    Task<Guid> CreateTopicAsync(PromptTopic topic);
    
    Task<IEnumerable<PromptQuestion>> GetQuestionsByTopicAsync(Guid topicId);
    Task<PromptQuestion?> GetQuestionAsync(Guid questionId);
    Task<Guid> CreateQuestionAsync(PromptQuestion question);

    // Analysis Runs
    Task<IEnumerable<PromptAnalysis>> GetAnalysesByQuestionAsync(Guid questionId);
    Task<PromptAnalysis?> GetLatestAnalysisAsync(Guid questionId);
    Task<Guid> CreateAnalysisAsync(PromptAnalysis analysis);
    Task UpdateAnalysisStatusAsync(Guid analysisId, string status, string? errorMessage = null);

    // Results (Insert)
    Task InsertResponsesAsync(IEnumerable<PromptResponse> responses);
    Task InsertMentionsAsync(IEnumerable<PromptMention> mentions);
    Task InsertVisibilityAsync(PromptVisibility visibility);
    Task InsertRecommendationsAsync(IEnumerable<PromptRecommendation> recommendations);
    Task InsertCompetitorComparisonsAsync(IEnumerable<CompetitorComparison> comparisons);

    // Results (Fetch)
    Task<IEnumerable<PromptResponse>> GetResponsesAsync(Guid analysisId);
    Task<IEnumerable<PromptMention>> GetMentionsAsync(Guid analysisId);
    Task<PromptVisibility?> GetVisibilityAsync(Guid analysisId);
    Task<IEnumerable<PromptRecommendation>> GetRecommendationsAsync(Guid analysisId);
    Task<IEnumerable<CompetitorComparison>> GetCompetitorComparisonsAsync(Guid analysisId);
}
