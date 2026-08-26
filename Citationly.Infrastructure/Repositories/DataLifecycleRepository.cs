using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class DataLifecycleRepository : IDataLifecycleRepository
{
    private static readonly string[] OrganizationScopedTables =
    {
        "Alerts",
        "AlertThresholds",
        "AnalysisRuns",
        "ApiKeys",
        "AuditLogs",
        "BrandClaims",
        "BrandFactChecks",
        "BrandPulseScanSummaries",
        "CitationScanSummaries",
        "CitationSourceSnapshots",
        "Competitors",
        "CompetitorSnapshots",
        "ContentDrafts",
        "ContentOptimizations",
        "CrossEngineConsensusInsights",
        "DashboardSnapshots",
        "Embeddings",
        "GeoPillars",
        "HistoricalScans",
        "Invites",
        "KnowledgeBases",
        "OpportunitySnapshots",
        "PromptAnalyses",
        "PromptCitations",
        "PromptMentionEvidence",
        "PromptMentions",
        "PromptQuestions",
        "PromptRecommendations",
        "PromptResponses",
        "PromptTopics",
        "RecommendationImplementations",
        "RegionScores",
        "Reports",
        "RetentionPolicies",
        "SsoConnections",
        "UsageCounters",
        "Users",
        "VisibilityScanSummaries",
        "VisibilityPlatformSnapshots",
        "Websites",
        "WebsiteProfiles"
    };

    private readonly IDbConnectionFactory _dbConnectionFactory;

    public DataLifecycleRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<RetentionPolicy?> GetRetentionPolicyAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<RetentionPolicy>(
            "SELECT * FROM RetentionPolicies WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });
    }

    public async Task<RetentionPolicy> UpsertRetentionPolicyAsync(RetentionPolicy policy, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<RetentionPolicy>(
            """
            INSERT INTO RetentionPolicies (OrganizationId, RawPromptEvidenceDays, AuditLogDays, SnapshotDays, UpdatedAt)
            VALUES (@OrganizationId, @RawPromptEvidenceDays, @AuditLogDays, @SnapshotDays, CURRENT_TIMESTAMP)
            ON CONFLICT (OrganizationId) DO UPDATE SET
                RawPromptEvidenceDays = EXCLUDED.RawPromptEvidenceDays,
                AuditLogDays = EXCLUDED.AuditLogDays,
                SnapshotDays = EXCLUDED.SnapshotDays,
                UpdatedAt = CURRENT_TIMESTAMP
            RETURNING *
            """,
            policy);
    }

    public async Task<IEnumerable<DataDeletionRequest>> GetDeletionRequestsAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<DataDeletionRequest>(
            "SELECT * FROM DataDeletionRequests WHERE OrganizationId = @OrganizationId ORDER BY RequestedAt DESC",
            new { OrganizationId = organizationId });
    }

    public async Task<DataDeletionRequest> CreateDeletionRequestAsync(DataDeletionRequest request, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<DataDeletionRequest>(
            """
            INSERT INTO DataDeletionRequests (OrganizationId, RequestedByUserId, Status, Scope, Reason, ScheduledFor)
            VALUES (@OrganizationId, @RequestedByUserId, 'Pending', @Scope, @Reason, @ScheduledFor)
            RETURNING *
            """,
            request);
    }

    public async Task<bool> CancelDeletionRequestAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var rows = await connection.ExecuteAsync(
            """
            UPDATE DataDeletionRequests
            SET Status = 'Cancelled', CancelledAt = CURRENT_TIMESTAMP
            WHERE Id = @RequestId AND OrganizationId = @OrganizationId AND Status = 'Pending'
            """,
            new { OrganizationId = organizationId, RequestId = requestId });
        return rows > 0;
    }

    public async Task<IReadOnlyDictionary<string, long>> GetOrganizationDeletionPreviewAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var existing = (await connection.QueryAsync<string>(
            """
            SELECT table_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND LOWER(column_name) = 'organizationid'
            """))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(table => table, table => table, StringComparer.OrdinalIgnoreCase);

        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in OrganizationScopedTables.Where(existing.ContainsKey))
        {
            var quotedTable = "\"" + existing[table].Replace("\"", "\"\"") + "\"";
            var count = await connection.ExecuteScalarAsync<long>(
                $"SELECT COUNT(*) FROM {quotedTable} WHERE OrganizationId = @OrganizationId",
                new { OrganizationId = organizationId });
            counts[table] = count;
        }

        return counts;
    }
}
