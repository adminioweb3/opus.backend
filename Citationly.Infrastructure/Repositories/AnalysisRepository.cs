using System;
using System.Threading.Tasks;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories
{
    public class AnalysisRepository : IAnalysisRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public AnalysisRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Guid> CreateAnalysisRunAsync(AnalysisRun run)
        {
            var sql = @"
                INSERT INTO AnalysisRuns (OrganizationId, WebsiteId, Status, StartedAt, ModelsUsed, PromptsExecuted, PagesAnalyzed, CompetitorsCompared)
                VALUES (@OrganizationId, @WebsiteId, @Status, @StartedAt, @ModelsUsed, @PromptsExecuted, @PagesAnalyzed, @CompetitorsCompared)
                RETURNING Id;
            ";

            using var connection = _connectionFactory.CreateConnection();
            run.Id = await connection.ExecuteScalarAsync<Guid>(sql, run);
            return run.Id;
        }

        public async Task UpdateAnalysisRunAsync(AnalysisRun run)
        {
            var sql = @"
                UPDATE AnalysisRuns 
                SET Status = @Status,
                    CompletedAt = @CompletedAt,
                    DurationSeconds = @DurationSeconds,
                    ModelsUsed = @ModelsUsed,
                    PromptsExecuted = @PromptsExecuted,
                    PagesAnalyzed = @PagesAnalyzed,
                    CompetitorsCompared = @CompetitorsCompared
                WHERE Id = @Id;
            ";

            using var connection = _connectionFactory.CreateConnection();
            await connection.ExecuteAsync(sql, run);
        }

        public async Task<Guid> CreateDashboardSnapshotAsync(DashboardSnapshot snapshot)
        {
            var sql = @"
                INSERT INTO DashboardSnapshots (
                    OrganizationId, AnalysisRunId, VisibilityScore, CitationHealth, 
                    RevenueImpact, CompetitorRisk, PlatformVisibilitiesJson, TopCompetitorsJson, 
                    OpportunityPipelineJson, ExecutiveAlertsJson, RecommendedActionsJson, 
                    KnowledgeVaultJson, CitationTimelineJson, AgentOperationsJson
                )
                VALUES (
                    @OrganizationId, @AnalysisRunId, @VisibilityScore, @CitationHealth, 
                    @RevenueImpact, @CompetitorRisk, @PlatformVisibilitiesJson::jsonb, @TopCompetitorsJson::jsonb, 
                    @OpportunityPipelineJson::jsonb, @ExecutiveAlertsJson::jsonb, @RecommendedActionsJson::jsonb, 
                    @KnowledgeVaultJson::jsonb, @CitationTimelineJson::jsonb, @AgentOperationsJson::jsonb
                )
                RETURNING Id;
            ";

            using var connection = _connectionFactory.CreateConnection();
            snapshot.Id = await connection.ExecuteScalarAsync<Guid>(sql, snapshot);
            return snapshot.Id;
        }

        public async Task<DashboardSnapshot?> GetLatestDashboardSnapshotAsync(Guid organizationId)
        {
            var sql = @"
                SELECT * FROM DashboardSnapshots 
                WHERE OrganizationId = @OrganizationId 
                ORDER BY CreatedAt DESC 
                LIMIT 1;
            ";

            using var connection = _connectionFactory.CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<DashboardSnapshot>(sql, new { OrganizationId = organizationId });
        }

        public async Task AddVisibilityHistoryAsync(VisibilityHistory history)
        {
            var sql = "INSERT INTO VisibilityHistory (AnalysisRunId, OrganizationId, Score) VALUES (@AnalysisRunId, @OrganizationId, @Score)";
            using var connection = _connectionFactory.CreateConnection();
            await connection.ExecuteAsync(sql, history);
        }

        public async Task AddCitationHistoryAsync(CitationHistory history)
        {
            var sql = "INSERT INTO CitationHistory (AnalysisRunId, OrganizationId, Score) VALUES (@AnalysisRunId, @OrganizationId, @Score)";
            using var connection = _connectionFactory.CreateConnection();
            await connection.ExecuteAsync(sql, history);
        }

        public async Task AddRecommendationHistoryAsync(RecommendationHistory history)
        {
            var sql = "INSERT INTO RecommendationHistory (AnalysisRunId, OrganizationId, Title, Description, Priority) VALUES (@AnalysisRunId, @OrganizationId, @Title, @Description, @Priority)";
            using var connection = _connectionFactory.CreateConnection();
            await connection.ExecuteAsync(sql, history);
        }

        public async Task AddPromptHistoryAsync(PromptHistory history)
        {
            var sql = "INSERT INTO PromptHistory (AnalysisRunId, OrganizationId, QueryString, SearchEngine) VALUES (@AnalysisRunId, @OrganizationId, @QueryString, @SearchEngine)";
            using var connection = _connectionFactory.CreateConnection();
            await connection.ExecuteAsync(sql, history);
        }
    }
}
