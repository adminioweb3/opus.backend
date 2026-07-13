namespace Citationly.Domain.Entities;

public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class User
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string FirebaseUid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Role { get; set; } = "Viewer";
    public DateTime CreatedAt { get; set; }
}

public class Website
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string DomainUrl { get; set; } = string.Empty;
    public string PlatformName { get; set; } = "Custom";
    public int HealthScore { get; set; } = 0;
    public int VisibilityScore { get; set; } = 0;
    public string Status { get; set; } = "Connected";
    public DateTime? LastSyncAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CrawledPage
{
    public Guid Id { get; set; }
    public Guid WebsiteId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? Title { get; set; }
    public DateTime LastCrawledAt { get; set; }
}

public class Recommendation
{
    public Guid Id { get; set; }
    public Guid WebsiteId { get; set; }
    public Guid CrawledPageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ActionType { get; set; }
    public string? Priority { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
}

public class Integration
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public string? ApiUrl { get; set; }
    public string? ApiKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Embedding
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ReferenceId { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public string TextContent { get; set; } = string.Empty;
    public double[] Vector { get; set; } = Array.Empty<double>();
    public DateTime CreatedAt { get; set; }
}

public class HistoricalScan
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateOnly ScanDate { get; set; }
    public int VisibilityScore { get; set; }
    public int CitationScore { get; set; }
    public int SentimentScore { get; set; }
    public int CompetitorScore { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ShareOfVoice
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateOnly ScanDate { get; set; }
    public string CompetitorName { get; set; } = string.Empty;
    public int SharePercentage { get; set; }
    public string ColorCode { get; set; } = "#000000";
    public DateTime CreatedAt { get; set; }
}

public class Competitor
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Logo { get; set; }
    public string? Country { get; set; }
    public int Authority { get; set; }
    public int Popularity { get; set; }
    public int Rank { get; set; } = 0;
    public int SimilarityScore { get; set; } = 0;
    public string RawJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    // Enrichment tracking
    public string EnrichmentStatus { get; set; } = "Pending";
    public string? EnrichedJson { get; set; }
    public DateTime? EnrichedAt { get; set; }
    // Competitor classification
    public string CompetitorType { get; set; } = "Direct";
    public int Confidence { get; set; }
}

public class AiSearchPrompt
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string QueryString { get; set; } = string.Empty;
    public string SearchEngine { get; set; } = "Google"; // e.g. Perplexity, Google, Bing
    public string? Topic { get; set; }
    public string? Intent { get; set; }
    public string? Difficulty { get; set; }
    public string? Persona { get; set; }
    public int CommercialValue { get; set; }
    public string RawJson { get; set; } = string.Empty;
    public int VisibilityScore { get; set; }
    public string? EstimatedRank { get; set; }
    public int Confidence { get; set; }
    public bool AppearsInAnswer { get; set; }
    public int ShareOfVoiceContribution { get; set; }
    public int MentionProbability { get; set; }
    public int BrandStrength { get; set; }
    public int ContentStrength { get; set; }
    public int CitationStrength { get; set; }
    public string? VisibilityReason { get; set; }
    public string MonthlySearchEstimate { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string TopicValidation { get; set; } = string.Empty;
    public string BuyerJourneyStage { get; set; } = string.Empty;
    public bool IsEnriched { get; set; }
    public DateTime? EnrichedAt { get; set; }
    public DateTime GeneratedAt { get; set; }
}

public class VisibilitySummary
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public int OverallVisibilityScore { get; set; }
    public string BestPlatform { get; set; } = string.Empty;
    public string WeakestPlatform { get; set; } = string.Empty;
    public int AverageMentionRate { get; set; }
    public int AveragePromptCoverage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PlatformVisibility
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public int VisibilityScore { get; set; }
    public string AverageRank { get; set; } = string.Empty;
    public int MentionRate { get; set; }
    public int PromptCoverage { get; set; }
    public int Confidence { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public bool IsEnriched { get; set; }
    public string StrengthsJson { get; set; } = "[]";
    public string WeaknessesJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CitationSummary
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public int TotalSources { get; set; }
    public int TotalMentionsAnalyzed { get; set; }
    public int AverageAuthorityScore { get; set; }
    public int AverageInfluenceScore { get; set; }
    public string HighestOpportunitySource { get; set; } = string.Empty;
    public string MostInfluentialSource { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CitationSource
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public int Rank { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int AuthorityScore { get; set; }
    public int InfluenceScore { get; set; }
    public int CitationFrequency { get; set; }
    public int CompetitorCoverage { get; set; }
    public int OpportunityScore { get; set; }
    public int MentionProbability { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsEnriched { get; set; }
    public DateTime? EnrichedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class BrandMention
{
    public Guid Id { get; set; }
    public Guid AiSearchPromptId { get; set; }
    public Guid OrganizationId { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public int Position { get; set; } // Position in the AI response (1 = first)
    public int SentimentScore { get; set; } // e.g. -1 to 1, or 0 to 100
    public DateTime MentionedAt { get; set; }
}

public class ScrapingJob
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? WebsiteId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string ScrapeType { get; set; } = "Single";
    public int TotalPages { get; set; } = 0;
    public int ProcessedPages { get; set; } = 0;
    public int MaxPages { get; set; } = 100;
    public int SuccessfulPages { get; set; } = 0;
    public int FailedPages { get; set; } = 0;
    public int TotalWords { get; set; } = 0;
    public int TotalImages { get; set; } = 0;
    public int TotalLinks { get; set; } = 0;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ScrapedPage
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Content { get; set; }
    public string? HtmlContent { get; set; }
    public string? MarkdownContent { get; set; }
    public int WordCount { get; set; }
    public int ImageCount { get; set; }
    public int LinkCount { get; set; }
    public string Images { get; set; } = "[]";
    public string InternalLinks { get; set; } = "[]";
    public string ExternalLinks { get; set; } = "[]";
    public string Headings { get; set; } = "[]";
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
}

public class ExtractedImage
{
    public Guid Id { get; set; }
    public Guid PageId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
}

public class ExtractedLink
{
    public Guid Id { get; set; }
    public Guid PageId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? LinkType { get; set; } // Internal, External
}

public class WebsiteMetadata
{
    public Guid Id { get; set; }
    public Guid WebsiteId { get; set; }
    public Guid? JobId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string OpenGraph { get; set; } = "{}";
    public string TwitterCard { get; set; } = "{}";
    public string SchemaData { get; set; } = "{}";
    public string JsonLd { get; set; } = "[]";
    public string? CanonicalUrl { get; set; }
    public string? Robots { get; set; }
    public string? Language { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class WebsiteProfile
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string WebsiteUrl { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    // The entire raw JSON from the AI analysis
    public string RawProfileJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PersonaAnalysisSummary
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public int OverallVisibility { get; set; }
    public string StrongestPersona { get; set; } = string.Empty;
    public string WeakestPersona { get; set; } = string.Empty;
    public int AverageShareOfVoice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PersonaScore
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Persona { get; set; } = string.Empty;
    public int Visibility { get; set; }
    public string AverageRank { get; set; } = string.Empty;
    public int ShareOfVoice { get; set; }
    public string TopCompetitorsJson { get; set; } = "[]";
    public string RecommendedContentJson { get; set; } = "[]";
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RegionAnalysisSummary
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public int OverallGlobalVisibility { get; set; }
    public string StrongestRegion { get; set; } = string.Empty;
    public string WeakestRegion { get; set; } = string.Empty;
    public int AverageShareOfVoice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RegionScore
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Region { get; set; } = string.Empty;
    public int Visibility { get; set; }
    public string Ranking { get; set; } = string.Empty;
    public string CompetitorLeader { get; set; } = string.Empty;
    public int ShareOfVoice { get; set; }
    public string ContentOpportunityJson { get; set; } = "[]";
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GeoRecommendationSummary
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string OverallPriority { get; set; } = string.Empty;
    public string EstimatedOverallImpact { get; set; } = string.Empty;
    public string EstimatedImplementationTime { get; set; } = string.Empty;
    public int TotalRecommendations { get; set; }
    public int CriticalRecommendations { get; set; }
    public int HighPriorityRecommendations { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GeoRecommendation
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string RecommendationId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string EstimatedImpact { get; set; } = string.Empty;
    public string EstimatedDifficulty { get; set; } = string.Empty;
    public string ImplementationTime { get; set; } = string.Empty;
    public string ExpectedOutcome { get; set; } = string.Empty;
    public string SuccessMetric { get; set; } = string.Empty;
    public string ActionItemsJson { get; set; } = "[]";
    public bool IsEnriched { get; set; }
    public DateTime? EnrichedAt { get; set; }
    public string ExpandedGuidance { get; set; } = string.Empty;
    public string BusinessImpact { get; set; } = string.Empty;
    public string ExampleResourcesJson { get; set; } = "[]";
    public string ReferenceLinksJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ExecutiveSummaryData
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string BusinessOverview { get; set; } = string.Empty;
    public string CurrentAIVisibility { get; set; } = string.Empty;
    public string CompetitorPosition { get; set; } = string.Empty;
    public string PlatformPerformance { get; set; } = string.Empty;
    public string TopicPerformance { get; set; } = string.Empty;
    public string PromptPerformance { get; set; } = string.Empty;
    public string CitationSummary { get; set; } = string.Empty;
    public string StrengthsJson { get; set; } = "[]";
    public string WeaknessesJson { get; set; } = "[]";
    public string OpportunitiesJson { get; set; } = "[]";
    public string ThreatsJson { get; set; } = "[]";
    
    public int OverallGEOScore { get; set; }
    public int OverallAIVisibilityScore { get; set; }
    public int OverallSEOScore { get; set; }
    public int OverallBrandAuthority { get; set; }
    public int OverallContentScore { get; set; }

    public string OverallAssessment { get; set; } = string.Empty;
    public string TopPriorityRecommendation { get; set; } = string.Empty;
    public string ExpectedBusinessImpact { get; set; } = string.Empty;
    public string NextStepsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AnalysisRun
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? WebsiteId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int DurationSeconds { get; set; }
    public string ModelsUsed { get; set; } = string.Empty; 
    public int PromptsExecuted { get; set; }
    public int PagesAnalyzed { get; set; }
    public int CompetitorsCompared { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DashboardSnapshot
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid AnalysisRunId { get; set; }
    public int VisibilityScore { get; set; }
    public int CitationHealth { get; set; }
    public string RevenueImpact { get; set; } = "$0";
    public string CompetitorRisk { get; set; } = "Low";
    
    public string PlatformVisibilitiesJson { get; set; } = "[]"; 
    public string TopCompetitorsJson { get; set; } = "[]";
    public string OpportunityPipelineJson { get; set; } = "{}";
    public string ExecutiveAlertsJson { get; set; } = "[]";
    public string RecommendedActionsJson { get; set; } = "[]";
    public string KnowledgeVaultJson { get; set; } = "{}";
    public string CitationTimelineJson { get; set; } = "[]";
    public string AgentOperationsJson { get; set; } = "[]";
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class VisibilityHistory
{
    public Guid Id { get; set; }
    public Guid AnalysisRunId { get; set; }
    public Guid OrganizationId { get; set; }
    public int Score { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

public class CitationHistory
{
    public Guid Id { get; set; }
    public Guid AnalysisRunId { get; set; }
    public Guid OrganizationId { get; set; }
    public int Score { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

public class RecommendationHistory
{
    public Guid Id { get; set; }
    public Guid AnalysisRunId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

public class PromptHistory
{
    public Guid Id { get; set; }
    public Guid AnalysisRunId { get; set; }
    public Guid OrganizationId { get; set; }
    public string QueryString { get; set; } = string.Empty;
    public string SearchEngine { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

// ===================================================================================
// Dashboard feature entities (Visibility Radar, Citation Intelligence, Brand Pulse,
// Command Center, Opportunity Finder, Competitor Watch). Each maps 1:1 onto a table that
// already exists in the working dev database (created by earlier, since-lost repository
// code) — property names match the live Postgres columns exactly so no data migration is
// needed; repositories just need to be rebuilt against this already-correct schema.
// ===================================================================================

public class VisibilityScanSummary
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateOnly ScanDate { get; set; }
    public int CompositeScore { get; set; }
    public int DirectPct { get; set; }
    public int MentionsPct { get; set; }
    public int IndirectPct { get; set; }
    public int ComparativePct { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class VisibilityPlatformSnapshot
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateOnly ScanDate { get; set; }
    public string Platform { get; set; } = string.Empty;
    public int Score { get; set; }
    public int Citations { get; set; }
    public string Status { get; set; } = "Developing";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CitationScanSummary
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateOnly ScanDate { get; set; }
    public int CompositeQualityScore { get; set; }
    public int AverageAuthorityScore { get; set; }
    public int AverageInfluenceScore { get; set; }
    public int CitationSignal { get; set; }
    public int ModelsReferencingCount { get; set; }
    public int ModelsTrackedCount { get; set; }
    // Self-healed column (ALTER TABLE ... ADD COLUMN IF NOT EXISTS) — holds the AI-generated
    // per-platform citation breakdown the frontend's `platforms[]` field needs.
    public string PlatformsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CitationSourceSnapshot
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateOnly ScanDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int AuthorityScore { get; set; }
    public int InfluenceScore { get; set; }
    public int CitationFrequency { get; set; }
    public int CompetitorCoverage { get; set; }
    public int OpportunityScore { get; set; }
    public int MentionProbability { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class BrandPulseScanSummary
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateOnly ScanDate { get; set; }
    public int BrandHealth { get; set; }
    public int AiConfidence { get; set; }
    public int MessagingConsistency { get; set; }
    public int BrandTrust { get; set; }
    public int SentimentPositive { get; set; }
    public int SentimentNeutral { get; set; }
    public int SentimentNegative { get; set; }
    public string AlertsJson { get; set; } = "[]";
    public string ModelInsightsJson { get; set; } = "[]";
    public string AccuracyFlagsJson { get; set; } = "[]";
    public string PromptEvidenceJson { get; set; } = "[]";
    // Self-healed column — the frontend's `shareOfPerception[]` field.
    public string SharePerceptionJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CommandCenterInsightSnapshot
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateOnly ScanDate { get; set; }
    public string InsightsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class OpportunitySnapshot
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateOnly ScanDate { get; set; }
    public string OpportunityKey { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? WhyItMatters { get; set; }
    public int Score { get; set; }
    public int Effort { get; set; }
    public double EstimatedGainPct { get; set; }
    public string? Eta { get; set; }
    public string? CompetitorContext { get; set; }
    public string ChecklistJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CompetitorSnapshot
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? CompetitorId { get; set; }
    public bool IsYou { get; set; }
    public DateOnly ScanDate { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
    public int Rank { get; set; }
    public int ShareOfVoice { get; set; }
    public int ShareOfVoiceChange { get; set; }
    public int Visibility { get; set; }
    public int VisibilityChange { get; set; }
    public string Threat { get; set; } = "low";
    public string ModelsJson { get; set; } = "{}";
    public string? Tagline { get; set; }
    public string? WebsiteUrl { get; set; }
    // Self-healed columns — the frontend's `citations.share`/`citations.total`/`content.velocity`.
    public int CitationsShare { get; set; }
    public int CitationsTotal { get; set; }
    public string ContentVelocity { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
