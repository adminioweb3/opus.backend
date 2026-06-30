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
            INSERT INTO PromptQuestions (PromptTopicId, PromptText, CreatedAt)
            VALUES (@PromptTopicId, @PromptText, @CreatedAt)
            RETURNING Id;";
        return await connection.ExecuteScalarAsync<Guid>(sql, question);
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
            INSERT INTO PromptResponses (PromptAnalysisId, Platform, ResponseText, ResponseLength, CreatedAt)
            VALUES (@PromptAnalysisId, @Platform, @ResponseText, @ResponseLength, @CreatedAt);";
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
            VALUES (@PromptAnalysisId, @CompetitorName, @VisibilityScore, @ShareOfVoice, @MissingTopicsJson);";
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
}
