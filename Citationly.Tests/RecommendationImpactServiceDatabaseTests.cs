using System.Data;
using Citationly.Application.Interfaces;
using Citationly.Infrastructure.Repositories;
using Citationly.Infrastructure.Services;
using Dapper;
using Npgsql;
using Xunit;

namespace Citationly.Tests;

public class RecommendationImpactServiceDatabaseTests
{
    [Fact]
    public async Task ProcessDueMeasurements_CompletesMeasuredImpactCycle_WithRealPromptVisibilityRows()
    {
        await using var database = await RecommendationImpactTestDatabase.TryCreateAsync();
        if (database == null) return;

        var orgId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        var baselineAnalysisId = Guid.NewGuid();
        var recommendationId = Guid.NewGuid();
        var followupAnalysisId = Guid.NewGuid();
        var dueAt = DateTime.UtcNow.AddMinutes(-5);

        await database.SeedImpactCycleAsync(
            orgId,
            topicId,
            questionId,
            baselineAnalysisId,
            recommendationId,
            followupAnalysisId,
            dueAt);

        var repository = new PromptIntelligenceRepository(new ConnectionFactory(database.SchemaConnectionString));
        var service = new RecommendationImpactService(repository);

        var implementation = await service.MarkImplementedAsync(orgId, recommendationId, monitoringWindowDays: 1);
        Assert.NotNull(implementation);

        await database.SetMeasurementDueAtAsync(implementation.Id, dueAt);

        var measured = await service.ProcessDueMeasurementsAsync(orgId);

        Assert.Equal(1, measured);

        await using var connection = database.CreateOpenConnection();
        var row = await connection.QuerySingleAsync<MeasuredImpactRow>(
            """
            SELECT
                ImpactStatus,
                FollowupAnalysisId,
                DeltaVisibilityScore,
                DeltaShareOfVoice,
                DeltaAveragePosition,
                DeltaCitationCount,
                EvidenceJson::text AS EvidenceJson
            FROM RecommendationImplementations
            WHERE Id = @Id
            """,
            new { implementation.Id });

        Assert.Equal("Improved", row.ImpactStatus);
        Assert.Equal(followupAnalysisId, row.FollowupAnalysisId);
        Assert.Equal(12, row.DeltaVisibilityScore);
        Assert.Equal(9, row.DeltaShareOfVoice);
        Assert.Equal(2, row.DeltaAveragePosition);
        Assert.Equal(2, row.DeltaCitationCount);
        Assert.Contains(followupAnalysisId.ToString(), row.EvidenceJson, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecommendationImpactTestDatabase : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _schema;

        private RecommendationImpactTestDatabase(string adminConnectionString, string schema, string schemaConnectionString)
        {
            _adminConnectionString = adminConnectionString;
            _schema = schema;
            SchemaConnectionString = schemaConnectionString;
        }

        public string SchemaConnectionString { get; }

        public static async Task<RecommendationImpactTestDatabase?> TryCreateAsync()
        {
            var rawConnectionString = Environment.GetEnvironmentVariable("CITATIONLY_TEST_DATABASE")
                ?? "Host=localhost;Database=opus_db;Username=postgres;Password=postgres";
            var schema = "phase5_impact_" + Guid.NewGuid().ToString("N");

            try
            {
                var adminBuilder = new NpgsqlConnectionStringBuilder(rawConnectionString)
                {
                    Timeout = 2,
                    CommandTimeout = 5
                };

                await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
                await admin.OpenAsync();
                await admin.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS pgcrypto");
                await admin.ExecuteAsync($"CREATE SCHEMA {schema}");

                var schemaBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString)
                {
                    SearchPath = schema
                };

                var database = new RecommendationImpactTestDatabase(
                    adminBuilder.ConnectionString,
                    schema,
                    schemaBuilder.ConnectionString);
                await database.CreateSchemaAsync();
                return database;
            }
            catch (NpgsqlException)
            {
                return null;
            }
            catch (TimeoutException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public NpgsqlConnection CreateOpenConnection()
        {
            var connection = new NpgsqlConnection(SchemaConnectionString);
            connection.Open();
            return connection;
        }

        public async Task SeedImpactCycleAsync(
            Guid organizationId,
            Guid topicId,
            Guid questionId,
            Guid baselineAnalysisId,
            Guid recommendationId,
            Guid followupAnalysisId,
            DateTime dueAt)
        {
            await using var connection = CreateOpenConnection();
            await connection.ExecuteAsync(
                """
                INSERT INTO Organizations (Id, Name) VALUES (@OrganizationId, 'Impact Test Org');
                INSERT INTO PromptTopics (Id, OrganizationId, Name) VALUES (@TopicId, @OrganizationId, 'AI Visibility');
                INSERT INTO PromptQuestions (Id, PromptTopicId, PromptText) VALUES (@QuestionId, @TopicId, 'Which AI visibility platform should I use?');

                INSERT INTO PromptAnalysis (Id, PromptQuestionId, RunAt, Status)
                VALUES (@BaselineAnalysisId, @QuestionId, @BaselineRunAt, 'Completed');
                INSERT INTO PromptVisibility (PromptAnalysisId, OverallVisibilityScore, MentionFrequency, AveragePosition, ShareOfVoice, CitationCount, CompetitorCount)
                VALUES (@BaselineAnalysisId, 40, 45, 5, 30, 1, 2);
                INSERT INTO PromptRecommendations (Id, PromptAnalysisId, Category, Title, Description, Priority, Difficulty, EstimatedVisibilityGain)
                VALUES (@RecommendationId, @BaselineAnalysisId, 'Content', 'Add authoritative comparison proof', 'Publish a stronger evidence-backed comparison page.', 'High', 'Medium', 10);

                INSERT INTO PromptAnalysis (Id, PromptQuestionId, RunAt, Status)
                VALUES (@FollowupAnalysisId, @QuestionId, @FollowupRunAt, 'Completed');
                INSERT INTO PromptVisibility (PromptAnalysisId, OverallVisibilityScore, MentionFrequency, AveragePosition, ShareOfVoice, CitationCount, CompetitorCount)
                VALUES (@FollowupAnalysisId, 52, 58, 3, 39, 3, 2);
                """,
                new
                {
                    OrganizationId = organizationId,
                    TopicId = topicId,
                    QuestionId = questionId,
                    BaselineAnalysisId = baselineAnalysisId,
                    RecommendationId = recommendationId,
                    FollowupAnalysisId = followupAnalysisId,
                    BaselineRunAt = dueAt.AddDays(-2),
                    FollowupRunAt = dueAt.AddMinutes(1)
                });
        }

        public async Task SetMeasurementDueAtAsync(Guid implementationId, DateTime dueAt)
        {
            await using var connection = CreateOpenConnection();
            await connection.ExecuteAsync(
                "UPDATE RecommendationImplementations SET MeasurementDueAt = @DueAt WHERE Id = @ImplementationId",
                new { ImplementationId = implementationId, DueAt = dueAt });
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync($"DROP SCHEMA IF EXISTS {_schema} CASCADE");
        }

        private async Task CreateSchemaAsync()
        {
            await using var connection = CreateOpenConnection();
            await connection.ExecuteAsync(
                """
                CREATE TABLE Organizations (
                    Id UUID PRIMARY KEY,
                    Name VARCHAR(255) NOT NULL
                );

                CREATE TABLE PromptTopics (
                    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
                    Name VARCHAR(255) NOT NULL,
                    Description TEXT NOT NULL DEFAULT '',
                    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE PromptQuestions (
                    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    PromptTopicId UUID REFERENCES PromptTopics(Id) ON DELETE CASCADE,
                    PromptText TEXT NOT NULL,
                    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
                    Region VARCHAR(100) NOT NULL DEFAULT 'Global',
                    Persona VARCHAR(255),
                    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE PromptAnalysis (
                    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    PromptQuestionId UUID REFERENCES PromptQuestions(Id) ON DELETE CASCADE,
                    RunAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    Status VARCHAR(50) NOT NULL DEFAULT 'Running',
                    ErrorMessage TEXT
                );

                CREATE TABLE PromptVisibility (
                    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
                    OverallVisibilityScore INT NOT NULL DEFAULT 0,
                    MentionFrequency INT NOT NULL DEFAULT 0,
                    AveragePosition INT NOT NULL DEFAULT 0,
                    ShareOfVoice INT NOT NULL DEFAULT 0,
                    CitationCount INT NOT NULL DEFAULT 0,
                    CompetitorCount INT NOT NULL DEFAULT 0
                );

                CREATE TABLE PromptRecommendations (
                    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
                    Category VARCHAR(100) NOT NULL DEFAULT '',
                    Title VARCHAR(255) NOT NULL DEFAULT '',
                    Description TEXT NOT NULL DEFAULT '',
                    Priority VARCHAR(50) NOT NULL DEFAULT 'Medium',
                    Difficulty VARCHAR(50) NOT NULL DEFAULT 'Medium',
                    EstimatedVisibilityGain INT NOT NULL DEFAULT 0
                );

                CREATE TABLE RecommendationImplementations (
                    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
                    PromptRecommendationId UUID REFERENCES PromptRecommendations(Id) ON DELETE CASCADE,
                    PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
                    PromptQuestionId UUID REFERENCES PromptQuestions(Id) ON DELETE CASCADE,
                    MarkedImplementedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    MonitoringWindowDays INT NOT NULL DEFAULT 14,
                    BaselineVisibilityScore INT NOT NULL DEFAULT 0,
                    BaselineShareOfVoice INT NOT NULL DEFAULT 0,
                    BaselineAveragePosition INT NOT NULL DEFAULT 0,
                    BaselineCitationCount INT NOT NULL DEFAULT 0,
                    MeasurementDueAt TIMESTAMP WITH TIME ZONE NOT NULL,
                    MeasuredAt TIMESTAMP WITH TIME ZONE NULL,
                    FollowupAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE SET NULL,
                    DeltaVisibilityScore INT NULL,
                    DeltaShareOfVoice INT NULL,
                    DeltaAveragePosition INT NULL,
                    DeltaCitationCount INT NULL,
                    ImpactStatus VARCHAR(50) NOT NULL DEFAULT 'Pending',
                    EvidenceJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                    UNIQUE (PromptRecommendationId)
                );
                """);
        }
    }

    private sealed class ConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public ConnectionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
    }

    private sealed class MeasuredImpactRow
    {
        public string ImpactStatus { get; set; } = string.Empty;
        public Guid FollowupAnalysisId { get; set; }
        public int DeltaVisibilityScore { get; set; }
        public int DeltaShareOfVoice { get; set; }
        public int DeltaAveragePosition { get; set; }
        public int DeltaCitationCount { get; set; }
        public string EvidenceJson { get; set; } = string.Empty;
    }
}
