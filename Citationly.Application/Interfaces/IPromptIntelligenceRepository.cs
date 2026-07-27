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
    Task UpdateQuestionAsync(Guid questionId, string? promptText, bool? isActive);

    // Analysis Runs
    Task<IEnumerable<PromptAnalysis>> GetAnalysesByQuestionAsync(Guid questionId);
    Task<PromptAnalysis?> GetAnalysisAsync(Guid analysisId);
    Task<PromptAnalysis?> GetLatestAnalysisAsync(Guid questionId);
    Task<Guid> CreateAnalysisAsync(PromptAnalysis analysis);
    Task UpdateAnalysisStatusAsync(Guid analysisId, string status, string? errorMessage = null);

    // Answer Atlas aggregation
    Task<IEnumerable<PromptVisibilitySummaryRow>> GetVisibilitySummaryDataAsync(Guid organizationId, DateTime since);
    Task<IEnumerable<CompetitorComparisonSummaryRow>> GetCompetitorComparisonSummaryDataAsync(Guid organizationId, DateTime since);
    Task<IEnumerable<PromptPlatformSummaryRow>> GetPlatformSummaryDataAsync(Guid organizationId, DateTime since);
    Task<IEnumerable<PromptCitationSummaryRow>> GetCitationSummaryDataAsync(Guid organizationId, DateTime since);
    Task<IEnumerable<PromptSentimentSummaryRow>> GetSentimentSummaryDataAsync(Guid organizationId, DateTime since);
    Task<IEnumerable<PromptExecutionHistoryRow>> GetExecutionHistoryAsync(Guid questionId);
    Task<string?> GetOrganizationPlanTypeAsync(Guid organizationId);
    Task<IEnumerable<CompetitorMentionSummaryRow>> GetCompetitorMentionSummaryDataAsync(Guid organizationId, DateTime since);
    Task<IEnumerable<FanoutOverviewRow>> GetFanoutOverviewDataAsync(Guid organizationId);

    // Results (Insert)
    Task InsertResponsesAsync(IEnumerable<PromptResponse> responses);
    Task InsertMentionsAsync(IEnumerable<PromptMention> mentions);
    Task InsertVisibilityAsync(PromptVisibility visibility);
    Task InsertRecommendationsAsync(IEnumerable<PromptRecommendation> recommendations);
    Task InsertCompetitorComparisonsAsync(IEnumerable<CompetitorComparison> comparisons);
    Task InsertCitationsAsync(IEnumerable<PromptCitation> citations);
    Task UpdateResponseSentimentAsync(Guid analysisId, string platform, string? sentiment, string? quote);

    // Fanouts
    Task<IEnumerable<PromptFanout>> GetFanoutsByQuestionAsync(Guid questionId);
    Task InsertFanoutsAsync(IEnumerable<PromptFanout> fanouts);
    Task DeleteFanoutsByQuestionAsync(Guid questionId);

    // Results (Fetch)
    Task<IEnumerable<PromptResponse>> GetResponsesAsync(Guid analysisId);
    Task<IEnumerable<PromptMention>> GetMentionsAsync(Guid analysisId);
    Task<PromptVisibility?> GetVisibilityAsync(Guid analysisId);
    Task<IEnumerable<PromptRecommendation>> GetRecommendationsAsync(Guid analysisId);
    Task<IEnumerable<CompetitorComparison>> GetCompetitorComparisonsAsync(Guid analysisId);
}

public class PromptVisibilitySummaryRow
{
    public Guid TopicId { get; set; }
    public string TopicName { get; set; } = string.Empty;
    public Guid QuestionId { get; set; }
    public string Region { get; set; } = "Global";
    public string? Persona { get; set; }
    public DateTime RunAt { get; set; }
    public int OverallVisibilityScore { get; set; }
    public int ShareOfVoice { get; set; }
    public int AveragePosition { get; set; }
    public int CitationCount { get; set; }
}

public class CompetitorComparisonSummaryRow
{
    public string CompetitorName { get; set; } = string.Empty;
    public int VisibilityScore { get; set; }
    public int ShareOfVoice { get; set; }
    public DateTime RunAt { get; set; }
}

public class PromptPlatformSummaryRow
{
    public Guid AnalysisId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public bool IsBrandMentioned { get; set; }
    public int? BrandPosition { get; set; }
    public int TotalMentionsOnPlatform { get; set; }
    public int BrandMentionsOnPlatform { get; set; }
    public DateTime RunAt { get; set; }
}

public class PromptCitationSummaryRow
{
    public Guid AnalysisId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime RunAt { get; set; }
}

public class PromptSentimentSummaryRow
{
    public string? Sentiment { get; set; }
    public string? SentimentQuote { get; set; }
    public string Platform { get; set; } = string.Empty;
    public DateTime RunAt { get; set; }
}

public class PromptExecutionHistoryRow
{
    public Guid AnalysisId { get; set; }
    public DateTime RunAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? OverallVisibilityScore { get; set; }
    public int? ShareOfVoice { get; set; }
    public int? AveragePosition { get; set; }
}

public class CompetitorMentionSummaryRow
{
    public string CompetitorName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public int Position { get; set; }
    public DateTime RunAt { get; set; }
}

public class FanoutOverviewRow
{
    public Guid QuestionId { get; set; }
    public string PromptText { get; set; } = string.Empty;
    public int FanoutCount { get; set; }
    public int AnalysisCount { get; set; }
}
