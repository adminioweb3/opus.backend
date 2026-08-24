using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class PromptIntelligenceRepository : IPromptIntelligenceRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public PromptIntelligenceRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<IEnumerable<PromptTopic>> GetTopicsAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<PromptTopic>(
            "SELECT * FROM PromptTopics WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC",
            new { OrganizationId = organizationId });
    }

    public async Task<PromptTopic?> GetTopicAsync(Guid topicId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PromptTopic>(
            "SELECT * FROM PromptTopics WHERE Id = @Id",
            new { Id = topicId });
    }

    public async Task<Guid> CreateTopicAsync(PromptTopic topic)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO PromptTopics (OrganizationId, Name, Description, CreatedAt)
            VALUES (@OrganizationId, @Name, @Description, @CreatedAt)
            RETURNING Id;";
        return await connection.ExecuteScalarAsync<Guid>(sql, topic);
    }

    public async Task<IEnumerable<PromptQuestion>> GetQuestionsByTopicAsync(Guid topicId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<PromptQuestion>(
            "SELECT * FROM PromptQuestions WHERE PromptTopicId = @PromptTopicId ORDER BY CreatedAt ASC",
            new { PromptTopicId = topicId });
    }

    public async Task<PromptQuestion?> GetQuestionAsync(Guid questionId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PromptQuestion>(
            "SELECT * FROM PromptQuestions WHERE Id = @Id",
            new { Id = questionId });
    }

    public async Task<Guid> CreateQuestionAsync(PromptQuestion question)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO PromptQuestions (PromptTopicId, PromptText, Region, Persona, CreatedAt)
            VALUES (@PromptTopicId, @PromptText, @Region, @Persona, @CreatedAt)
            RETURNING Id;";
        return await connection.ExecuteScalarAsync<Guid>(sql, question);
    }

    public async Task UpdateQuestionAsync(Guid questionId, string? promptText, bool? isActive)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            UPDATE PromptQuestions
            SET PromptText = COALESCE(@PromptText, PromptText),
                IsActive = COALESCE(@IsActive, IsActive)
            WHERE Id = @Id";
        await connection.ExecuteAsync(sql, new { Id = questionId, PromptText = promptText, IsActive = isActive });
    }

    public async Task<PromptAnalysis?> GetAnalysisAsync(Guid analysisId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PromptAnalysis>(
            "SELECT * FROM PromptAnalysis WHERE Id = @Id",
            new { Id = analysisId });
    }

    public async Task<IEnumerable<PromptAnalysis>> GetAnalysesByQuestionAsync(Guid questionId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<PromptAnalysis>(
            "SELECT * FROM PromptAnalysis WHERE PromptQuestionId = @PromptQuestionId ORDER BY RunAt DESC",
            new { PromptQuestionId = questionId });
    }

    public async Task<PromptAnalysis?> GetLatestAnalysisAsync(Guid questionId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<PromptAnalysis>(
            "SELECT * FROM PromptAnalysis WHERE PromptQuestionId = @PromptQuestionId ORDER BY RunAt DESC LIMIT 1",
            new { PromptQuestionId = questionId });
    }

    public async Task<Guid> CreateAnalysisAsync(PromptAnalysis analysis)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO PromptAnalysis (PromptQuestionId, RunAt, Status, ErrorMessage)
            VALUES (@PromptQuestionId, @RunAt, @Status, @ErrorMessage)
            RETURNING Id;";
        return await connection.ExecuteScalarAsync<Guid>(sql, analysis);
    }

    public async Task UpdateAnalysisStatusAsync(Guid analysisId, string status, string? errorMessage = null)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            UPDATE PromptAnalysis 
            SET Status = @Status, ErrorMessage = @ErrorMessage
            WHERE Id = @Id";
        await connection.ExecuteAsync(sql, new { Id = analysisId, Status = status, ErrorMessage = errorMessage });
    }

    public async Task InsertResponsesAsync(IEnumerable<PromptResponse> responses)
    {
        if (!responses.Any()) return;
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO PromptResponses (PromptAnalysisId, Platform, ResponseText, ResponseLength, CreatedAt,
                                          ProviderKey, ModelUsed, PromptTokens, CompletionTokens, CostUsd, WasSearchGrounded)
            VALUES (@PromptAnalysisId, @Platform, @ResponseText, @ResponseLength, @CreatedAt,
                    @ProviderKey, @ModelUsed, @PromptTokens, @CompletionTokens, @CostUsd, @WasSearchGrounded);";
        await connection.ExecuteAsync(sql, responses);
    }

    public async Task InsertMentionsAsync(IEnumerable<PromptMention> mentions)
    {
        if (!mentions.Any()) return;
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO PromptMentions (PromptAnalysisId, Platform, EntityName, IsBrand, ContextSnippet, Position)
            VALUES (@PromptAnalysisId, @Platform, @EntityName, @IsBrand, @ContextSnippet, @Position);";
        await connection.ExecuteAsync(sql, mentions);
    }

    public async Task InsertVisibilityAsync(PromptVisibility visibility)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO PromptVisibility (PromptAnalysisId, OverallVisibilityScore, MentionFrequency, AveragePosition, ShareOfVoice, CitationCount, CompetitorCount)
            VALUES (@PromptAnalysisId, @OverallVisibilityScore, @MentionFrequency, @AveragePosition, @ShareOfVoice, @CitationCount, @CompetitorCount);";
        await connection.ExecuteAsync(sql, visibility);
    }

    public async Task InsertRecommendationsAsync(IEnumerable<PromptRecommendation> recommendations)
    {
        if (!recommendations.Any()) return;
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO PromptRecommendations (PromptAnalysisId, Category, Title, Description, Priority, Difficulty, EstimatedVisibilityGain)
            VALUES (@PromptAnalysisId, @Category, @Title, @Description, @Priority, @Difficulty, @EstimatedVisibilityGain);";
        await connection.ExecuteAsync(sql, recommendations);
    }

    public async Task InsertCompetitorComparisonsAsync(IEnumerable<CompetitorComparison> comparisons)
    {
        if (!comparisons.Any()) return;
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO CompetitorComparisons (PromptAnalysisId, CompetitorName, VisibilityScore, ShareOfVoice, MissingTopicsJson)
            VALUES (@PromptAnalysisId, @CompetitorName, @VisibilityScore, @ShareOfVoice, @MissingTopicsJson::jsonb);";
        await connection.ExecuteAsync(sql, comparisons);
    }

    public async Task<IEnumerable<PromptResponse>> GetResponsesAsync(Guid analysisId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<PromptResponse>(
            "SELECT * FROM PromptResponses WHERE PromptAnalysisId = @PromptAnalysisId",
            new { PromptAnalysisId = analysisId });
    }

    public async Task<IEnumerable<PromptMention>> GetMentionsAsync(Guid analysisId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<PromptMention>(
            "SELECT * FROM PromptMentions WHERE PromptAnalysisId = @PromptAnalysisId",
            new { PromptAnalysisId = analysisId });
    }

    public async Task<PromptVisibility?> GetVisibilityAsync(Guid analysisId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PromptVisibility>(
            "SELECT * FROM PromptVisibility WHERE PromptAnalysisId = @PromptAnalysisId",
            new { PromptAnalysisId = analysisId });
    }

    public async Task<IEnumerable<PromptRecommendation>> GetRecommendationsAsync(Guid analysisId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<PromptRecommendation>(
            "SELECT * FROM PromptRecommendations WHERE PromptAnalysisId = @PromptAnalysisId",
            new { PromptAnalysisId = analysisId });
    }

    public async Task<IEnumerable<CompetitorComparison>> GetCompetitorComparisonsAsync(Guid analysisId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<CompetitorComparison>(
            "SELECT * FROM CompetitorComparisons WHERE PromptAnalysisId = @PromptAnalysisId",
            new { PromptAnalysisId = analysisId });
    }

    public async Task<IEnumerable<PromptVisibilitySummaryRow>> GetVisibilitySummaryDataAsync(Guid organizationId, DateTime since)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            SELECT t.Id AS TopicId, t.Name AS TopicName, q.Id AS QuestionId, q.Region AS Region, q.Persona AS Persona, a.RunAt AS RunAt,
                   v.OverallVisibilityScore, v.ShareOfVoice, v.AveragePosition, v.CitationCount
            FROM PromptVisibility v
            JOIN PromptAnalysis a ON v.PromptAnalysisId = a.Id
            JOIN PromptQuestions q ON a.PromptQuestionId = q.Id
            JOIN PromptTopics t ON q.PromptTopicId = t.Id
            WHERE t.OrganizationId = @OrganizationId AND a.Status = 'Completed' AND a.RunAt >= @Since
            ORDER BY a.RunAt ASC";
        return await connection.QueryAsync<PromptVisibilitySummaryRow>(sql, new { OrganizationId = organizationId, Since = since });
    }

    public async Task<IEnumerable<CompetitorComparisonSummaryRow>> GetCompetitorComparisonSummaryDataAsync(Guid organizationId, DateTime since)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            SELECT cc.CompetitorName, cc.VisibilityScore, cc.ShareOfVoice, a.RunAt AS RunAt
            FROM CompetitorComparisons cc
            JOIN PromptAnalysis a ON cc.PromptAnalysisId = a.Id
            JOIN PromptQuestions q ON a.PromptQuestionId = q.Id
            JOIN PromptTopics t ON q.PromptTopicId = t.Id
            WHERE t.OrganizationId = @OrganizationId AND a.Status = 'Completed' AND a.RunAt >= @Since
            ORDER BY a.RunAt ASC";
        return await connection.QueryAsync<CompetitorComparisonSummaryRow>(sql, new { OrganizationId = organizationId, Since = since });
    }

    public async Task<IEnumerable<PromptPlatformSummaryRow>> GetPlatformSummaryDataAsync(Guid organizationId, DateTime since)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        // Based on PromptResponses (one row per platform per analysis, regardless of whether the
        // brand was mentioned there) rather than PromptMentions alone, so platforms with zero
        // mentions still count toward the denominator instead of silently vanishing.
        var sql = @"
            SELECT r.PromptAnalysisId AS AnalysisId, r.Platform, a.RunAt AS RunAt,
                   EXISTS(SELECT 1 FROM PromptMentions m WHERE m.PromptAnalysisId = r.PromptAnalysisId AND m.Platform = r.Platform AND m.IsBrand = TRUE) AS IsBrandMentioned,
                   (SELECT MIN(m2.Position) FROM PromptMentions m2 WHERE m2.PromptAnalysisId = r.PromptAnalysisId AND m2.Platform = r.Platform AND m2.IsBrand = TRUE) AS BrandPosition,
                   (SELECT COUNT(*) FROM PromptMentions m3 WHERE m3.PromptAnalysisId = r.PromptAnalysisId AND m3.Platform = r.Platform) AS TotalMentionsOnPlatform,
                   (SELECT COUNT(*) FROM PromptMentions m4 WHERE m4.PromptAnalysisId = r.PromptAnalysisId AND m4.Platform = r.Platform AND m4.IsBrand = TRUE) AS BrandMentionsOnPlatform
            FROM PromptResponses r
            JOIN PromptAnalysis a ON r.PromptAnalysisId = a.Id
            JOIN PromptQuestions q ON a.PromptQuestionId = q.Id
            JOIN PromptTopics t ON q.PromptTopicId = t.Id
            WHERE t.OrganizationId = @OrganizationId AND a.Status = 'Completed' AND a.RunAt >= @Since";
        return await connection.QueryAsync<PromptPlatformSummaryRow>(sql, new { OrganizationId = organizationId, Since = since });
    }

    public async Task<IEnumerable<PromptCitationSummaryRow>> GetCitationSummaryDataAsync(Guid organizationId, DateTime since)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            SELECT c.PromptAnalysisId AS AnalysisId, c.Platform, c.Domain, c.Url, c.Category, a.RunAt AS RunAt
            FROM PromptCitations c
            JOIN PromptAnalysis a ON c.PromptAnalysisId = a.Id
            JOIN PromptQuestions q ON a.PromptQuestionId = q.Id
            JOIN PromptTopics t ON q.PromptTopicId = t.Id
            WHERE t.OrganizationId = @OrganizationId AND a.Status = 'Completed' AND a.RunAt >= @Since";
        return await connection.QueryAsync<PromptCitationSummaryRow>(sql, new { OrganizationId = organizationId, Since = since });
    }

    public async Task<IEnumerable<PromptSentimentSummaryRow>> GetSentimentSummaryDataAsync(Guid organizationId, DateTime since)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            SELECT r.Sentiment, r.SentimentQuote, r.Platform, a.RunAt AS RunAt
            FROM PromptResponses r
            JOIN PromptAnalysis a ON r.PromptAnalysisId = a.Id
            JOIN PromptQuestions q ON a.PromptQuestionId = q.Id
            JOIN PromptTopics t ON q.PromptTopicId = t.Id
            WHERE t.OrganizationId = @OrganizationId AND a.Status = 'Completed' AND a.RunAt >= @Since AND r.Sentiment IS NOT NULL";
        return await connection.QueryAsync<PromptSentimentSummaryRow>(sql, new { OrganizationId = organizationId, Since = since });
    }

    public async Task<IEnumerable<PromptExecutionHistoryRow>> GetExecutionHistoryAsync(Guid questionId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            SELECT a.Id AS AnalysisId, a.RunAt, a.Status,
                   v.OverallVisibilityScore, v.ShareOfVoice, v.AveragePosition
            FROM PromptAnalysis a
            LEFT JOIN PromptVisibility v ON v.PromptAnalysisId = a.Id
            WHERE a.PromptQuestionId = @QuestionId
            ORDER BY a.RunAt DESC";
        return await connection.QueryAsync<PromptExecutionHistoryRow>(sql, new { QuestionId = questionId });
    }

    public async Task<string?> GetOrganizationPlanTypeAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT PlanType FROM Organizations WHERE Id = @Id",
            new { Id = organizationId });
    }

    public async Task InsertCitationsAsync(IEnumerable<PromptCitation> citations)
    {
        if (!citations.Any()) return;
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO PromptCitations (PromptAnalysisId, Platform, Domain, Url, Category, CreatedAt)
            VALUES (@PromptAnalysisId, @Platform, @Domain, @Url, @Category, @CreatedAt);";
        await connection.ExecuteAsync(sql, citations);
    }

    public async Task UpdateResponseSentimentAsync(Guid analysisId, string platform, string? sentiment, string? quote)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            UPDATE PromptResponses
            SET Sentiment = @Sentiment, SentimentQuote = @Quote
            WHERE PromptAnalysisId = @AnalysisId AND Platform = @Platform";
        await connection.ExecuteAsync(sql, new { AnalysisId = analysisId, Platform = platform, Sentiment = sentiment, Quote = quote });
    }

    public async Task<IEnumerable<PromptFanout>> GetFanoutsByQuestionAsync(Guid questionId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<PromptFanout>(
            "SELECT * FROM PromptFanouts WHERE PromptQuestionId = @PromptQuestionId ORDER BY CreatedAt ASC",
            new { PromptQuestionId = questionId });
    }

    public async Task InsertFanoutsAsync(IEnumerable<PromptFanout> fanouts)
    {
        if (!fanouts.Any()) return;
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO PromptFanouts (PromptQuestionId, FanoutText, Engine, CreatedAt)
            VALUES (@PromptQuestionId, @FanoutText, @Engine, @CreatedAt);";
        await connection.ExecuteAsync(sql, fanouts);
    }

    public async Task DeleteFanoutsByQuestionAsync(Guid questionId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync("DELETE FROM PromptFanouts WHERE PromptQuestionId = @PromptQuestionId", new { PromptQuestionId = questionId });
    }

    public async Task<IEnumerable<CompetitorMentionSummaryRow>> GetCompetitorMentionSummaryDataAsync(Guid organizationId, DateTime since)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            SELECT m.EntityName AS CompetitorName, m.Platform, m.Position, a.RunAt AS RunAt
            FROM PromptMentions m
            JOIN PromptAnalysis a ON m.PromptAnalysisId = a.Id
            JOIN PromptQuestions q ON a.PromptQuestionId = q.Id
            JOIN PromptTopics t ON q.PromptTopicId = t.Id
            WHERE t.OrganizationId = @OrganizationId AND a.Status = 'Completed' AND a.RunAt >= @Since AND m.IsBrand = FALSE";
        return await connection.QueryAsync<CompetitorMentionSummaryRow>(sql, new { OrganizationId = organizationId, Since = since });
    }

    public async Task<IEnumerable<FanoutOverviewRow>> GetFanoutOverviewDataAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            SELECT q.Id AS QuestionId, q.PromptText,
                   (SELECT COUNT(*) FROM PromptFanouts f WHERE f.PromptQuestionId = q.Id) AS FanoutCount,
                   (SELECT COUNT(*) FROM PromptAnalysis a WHERE a.PromptQuestionId = q.Id AND a.Status = 'Completed') AS AnalysisCount
            FROM PromptQuestions q
            JOIN PromptTopics t ON q.PromptTopicId = t.Id
            WHERE t.OrganizationId = @OrganizationId
              AND EXISTS (SELECT 1 FROM PromptFanouts f WHERE f.PromptQuestionId = q.Id)";
        return await connection.QueryAsync<FanoutOverviewRow>(sql, new { OrganizationId = organizationId });
    }
}
