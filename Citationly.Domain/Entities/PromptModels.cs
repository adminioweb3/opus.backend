namespace Citationly.Domain.Entities;

public class PromptTopic
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PromptQuestion
{
    public Guid Id { get; set; }
    public Guid PromptTopicId { get; set; }
    public string PromptText { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PromptAnalysis
{
    public Guid Id { get; set; }
    public Guid PromptQuestionId { get; set; }
    public DateTime RunAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Running"; // Running, Completed, Failed
    public string? ErrorMessage { get; set; }
}

public class PromptResponse
{
    public Guid Id { get; set; }
    public Guid PromptAnalysisId { get; set; }
    public string Platform { get; set; } = string.Empty; // ChatGPT, Claude, Gemini, etc.
    public string ResponseText { get; set; } = string.Empty;
    public int ResponseLength { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PromptMention
{
    public Guid Id { get; set; }
    public Guid PromptAnalysisId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty; // Brand or Competitor Name
    public bool IsBrand { get; set; }
    public string ContextSnippet { get; set; } = string.Empty;
    public int Position { get; set; }
}

public class PromptVisibility
{
    public Guid Id { get; set; }
    public Guid PromptAnalysisId { get; set; }
    public int OverallVisibilityScore { get; set; }
    public int MentionFrequency { get; set; }
    public int AveragePosition { get; set; }
    public int ShareOfVoice { get; set; }
    public int CitationCount { get; set; }
    public int CompetitorCount { get; set; }
}

public class PromptRecommendation
{
    public Guid Id { get; set; }
    public Guid PromptAnalysisId { get; set; }
    public string Category { get; set; } = string.Empty; // Content, GEO, Technical, Revenue
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = "Medium";
    public string Difficulty { get; set; } = "Medium";
    public int EstimatedVisibilityGain { get; set; }
}

public class CompetitorComparison
{
    public Guid Id { get; set; }
    public Guid PromptAnalysisId { get; set; }
    public string CompetitorName { get; set; } = string.Empty;
    public int VisibilityScore { get; set; }
    public int ShareOfVoice { get; set; }
    public string MissingTopicsJson { get; set; } = "[]";
}
