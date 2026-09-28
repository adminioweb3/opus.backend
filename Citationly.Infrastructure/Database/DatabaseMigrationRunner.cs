using Citationly.Application.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Data;

namespace Citationly.Infrastructure.Database;

public sealed class DatabaseMigrationRunner
{
    private const long MigrationLockKey = 78219360420501;
    private const int MaxConnectionAttempts = 12;
    private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromSeconds(5);

    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly ILogger<DatabaseMigrationRunner> _logger;

    public DatabaseMigrationRunner(
        IDbConnectionFactory dbConnectionFactory,
        ILogger<DatabaseMigrationRunner> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AppliedDatabaseMigration>> RunPendingAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await OpenConnectionWithRetryAsync(cancellationToken);

        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Id VARCHAR(150) PRIMARY KEY,
                Description TEXT NOT NULL,
                AppliedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_schemamigrations_applied ON SchemaMigrations (AppliedAt DESC);
            """);

        await connection.ExecuteAsync("SELECT pg_advisory_lock(@LockKey);", new { LockKey = MigrationLockKey });
        try
        {
            var applied = new List<AppliedDatabaseMigration>();
            foreach (var migration in DatabaseMigrations.All)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var alreadyApplied = await connection.ExecuteScalarAsync<bool>(
                    "SELECT EXISTS (SELECT 1 FROM SchemaMigrations WHERE Id = @Id)",
                    new { migration.Id });
                if (alreadyApplied)
                {
                    continue;
                }

                using var transaction = connection.BeginTransaction();
                try
                {
                    await connection.ExecuteAsync(migration.Sql, transaction: transaction);
                    await connection.ExecuteAsync(
                        """
                        INSERT INTO SchemaMigrations (Id, Description)
                        VALUES (@Id, @Description)
                        """,
                        new { migration.Id, migration.Description },
                        transaction);

                    transaction.Commit();
                    applied.Add(new AppliedDatabaseMigration(migration.Id, migration.Description));
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }

            return applied;
        }
        finally
        {
            await connection.ExecuteAsync("SELECT pg_advisory_unlock(@LockKey);", new { LockKey = MigrationLockKey });
        }
    }

    private async Task<IDbConnection> OpenConnectionWithRetryAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxConnectionAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var connection = _dbConnectionFactory.CreateConnection();
            try
            {
                connection.Open();
                return connection;
            }
            catch (Exception ex) when (IsStartupConnectionFailure(ex))
            {
                connection.Dispose();

                if (attempt >= MaxConnectionAttempts)
                {
                    throw;
                }

                _logger.LogWarning(
                    ex,
                    "Database was not ready for migrations on attempt {Attempt}/{MaxAttempts}. Retrying in {RetryDelaySeconds} seconds.",
                    attempt,
                    MaxConnectionAttempts,
                    ConnectionRetryDelay.TotalSeconds);

                await Task.Delay(ConnectionRetryDelay, cancellationToken);
            }
        }

        throw new InvalidOperationException("Database connection retry loop exited unexpectedly.");
    }

    private static bool IsStartupConnectionFailure(Exception exception)
    {
        return exception is NpgsqlException or TimeoutException or InvalidOperationException;
    }
}

public sealed record AppliedDatabaseMigration(string Id, string Description);

internal sealed record DatabaseMigration(string Id, string Description, string Sql);

internal static class DatabaseMigrations
{
    public static readonly IReadOnlyList<DatabaseMigration> All =
    [
        new("202608260001_self_healing_baseline", "Apply current idempotent production schema baseline", SelfHealingMigrations.Sql),
        new(
            "202609030002_onboarding_profile_schema",
            "Ensure onboarding analysis profile persistence schema exists",
            """
            ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS Industry VARCHAR(255);
            ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS WhoDoYouSellTo TEXT;
            ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS KnownCompetitors TEXT;
            ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS MainOffering TEXT;

            CREATE TABLE IF NOT EXISTS WebsiteProfiles (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                WebsiteUrl VARCHAR(2048) NOT NULL,
                BusinessName VARCHAR(255) NOT NULL,
                RawProfileJson JSONB NOT NULL,
                CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_websiteprofiles_org_created ON WebsiteProfiles (OrganizationId, CreatedAt DESC);
            """),
        new(
            "202609030003_alerts_and_competitor_discovery_schema",
            "Ensure alerts and competitor discovery schema added after the baseline exists",
            """
            CREATE TABLE IF NOT EXISTS Alerts (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
                DedupKey VARCHAR(255) NOT NULL,
                Type VARCHAR(100) NOT NULL DEFAULT '',
                Title VARCHAR(255) NOT NULL DEFAULT '',
                Message TEXT NOT NULL DEFAULT '',
                Severity VARCHAR(50) NOT NULL DEFAULT 'Info',
                Source VARCHAR(100) NOT NULL DEFAULT '',
                ActionUrl TEXT NOT NULL DEFAULT '',
                EvidenceJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                IsRead BOOLEAN NOT NULL DEFAULT FALSE,
                CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                DeliveredAt TIMESTAMP WITH TIME ZONE NULL,
                DeliveryStatus VARCHAR(50) NOT NULL DEFAULT 'Pending',
                UNIQUE (OrganizationId, DedupKey)
            );
            CREATE INDEX IF NOT EXISTS idx_alerts_org_created ON Alerts (OrganizationId, CreatedAt DESC);
            CREATE INDEX IF NOT EXISTS idx_alerts_delivery ON Alerts (DeliveryStatus, CreatedAt);

            CREATE TABLE IF NOT EXISTS AlertThresholds (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
                AlertType VARCHAR(100) NOT NULL,
                ThresholdValue INT NOT NULL DEFAULT 5,
                EmailEnabled BOOLEAN NOT NULL DEFAULT TRUE,
                WebhookEnabled BOOLEAN NOT NULL DEFAULT FALSE,
                WebhookUrl TEXT NOT NULL DEFAULT '',
                UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE (OrganizationId, AlertType)
            );

            ALTER TABLE CompanyCompetitor ADD COLUMN IF NOT EXISTS DiscoverySource VARCHAR(20) NOT NULL DEFAULT 'graph';
            """),
        new(
            "202609030001_aisearchprompts_prompt_class_backfill",
            "Backfill AiSearchPrompts prompt classification columns added after the baseline migration",
            """
            DO $rename$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'aisearchprompts' AND column_name = 'monthlysearchestimate'
                ) AND NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'aisearchprompts' AND column_name = 'estimatedinterestlevel'
                ) THEN
                    ALTER TABLE AiSearchPrompts RENAME COLUMN MonthlySearchEstimate TO EstimatedInterestLevel;
                END IF;
            END;
            $rename$;

            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Topic VARCHAR(255);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Intent VARCHAR(100);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Difficulty VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Persona VARCHAR(255);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS CommercialValue INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS RawJson JSONB DEFAULT '{}'::jsonb;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Region VARCHAR(100);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Language VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS TopicValidation VARCHAR(255);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS BuyerJourneyStage VARCHAR(100);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS IsEnriched BOOLEAN DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS EnrichedAt TIMESTAMP WITH TIME ZONE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS EstimatedInterestLevel VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS VisibilityScore INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS EstimatedRank VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Confidence INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS AppearsInAnswer BOOLEAN DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ShareOfVoiceContribution INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS MentionProbability INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS BrandStrength INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ContentStrength INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS CitationStrength INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS VisibilityReason TEXT;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS PromptClass VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS IsBranded BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS IsOrganicVisibilityEligible BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ExpectsProviderRecommendations BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ExpectsBrandMention BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS MetricBucket VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS VisibilityWeight NUMERIC(5,2) NOT NULL DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ScoringReason TEXT;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ClassificationConfidence NUMERIC(5,2) NOT NULL DEFAULT 0;
            """),
        new(
            "202609040001_companycompetitor_discoverysource_repair",
            "Ensure CompanyCompetitor discovery source exists on databases that already applied older migrations",
            """
            ALTER TABLE CompanyCompetitor ADD COLUMN IF NOT EXISTS DiscoverySource VARCHAR(20) NOT NULL DEFAULT 'graph';
            """),
        new(
            "202609040002_aisearchprompts_classification_repair",
            "Ensure AiSearchPrompts classification columns exist on databases that already applied older migrations",
            """
            DO $rename$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'aisearchprompts' AND column_name = 'monthlysearchestimate'
                ) AND NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'aisearchprompts' AND column_name = 'estimatedinterestlevel'
                ) THEN
                    ALTER TABLE AiSearchPrompts RENAME COLUMN MonthlySearchEstimate TO EstimatedInterestLevel;
                END IF;
            END;
            $rename$;

            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Topic VARCHAR(255);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Intent VARCHAR(100);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Difficulty VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Persona VARCHAR(255);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS CommercialValue INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS RawJson JSONB DEFAULT '{}'::jsonb;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Region VARCHAR(100);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Language VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS TopicValidation VARCHAR(255);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS BuyerJourneyStage VARCHAR(100);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS IsEnriched BOOLEAN DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS EnrichedAt TIMESTAMP WITH TIME ZONE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS EstimatedInterestLevel VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS VisibilityScore INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS EstimatedRank VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Confidence INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS AppearsInAnswer BOOLEAN DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ShareOfVoiceContribution INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS MentionProbability INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS BrandStrength INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ContentStrength INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS CitationStrength INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS VisibilityReason TEXT;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS PromptClass VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS IsBranded BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS IsOrganicVisibilityEligible BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ExpectsProviderRecommendations BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ExpectsBrandMention BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS MetricBucket VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS VisibilityWeight NUMERIC(5,2) NOT NULL DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ScoringReason TEXT;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ClassificationConfidence NUMERIC(5,2) NOT NULL DEFAULT 0;
            """),
        new(
            "202609100001_auth_sync_userid_ambiguity_repair",
            "Resolve UserId output-column ambiguity in the multi-provider auth sync function",
            """
            ALTER FUNCTION sp_CreateOrGetUserV2(VARCHAR, VARCHAR, VARCHAR, VARCHAR, VARCHAR)
                SET plpgsql.variable_conflict TO 'use_column';
            """),
        new(
            "202609100002_competitor_schema_drift_repair",
            "Add competitor evidence columns introduced after the production baseline was applied",
            """
            ALTER TABLE Competitors
                ADD COLUMN IF NOT EXISTS DiscoverySource VARCHAR(20) NOT NULL DEFAULT 'unknown';

            ALTER TABLE CompetitorSnapshots
                ADD COLUMN IF NOT EXISTS WebsiteUrl VARCHAR(2048),
                ADD COLUMN IF NOT EXISTS MentionCount INT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS RecommendationCount INT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS ResponseCount INT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS CitationCount INT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS AveragePosition INT NOT NULL DEFAULT 100,
                ADD COLUMN IF NOT EXISTS MeasurementSource VARCHAR(50) NOT NULL DEFAULT 'legacy-estimated',
                ADD COLUMN IF NOT EXISTS MethodologyVersion VARCHAR(50) NOT NULL DEFAULT 'legacy-v1',
                ADD COLUMN IF NOT EXISTS ModelUsed VARCHAR(100),
                ADD COLUMN IF NOT EXISTS DiscoverySource VARCHAR(20) NOT NULL DEFAULT 'unknown';

            ALTER TABLE PromptResponses
                ADD COLUMN IF NOT EXISTS Sentiment VARCHAR(10),
                ADD COLUMN IF NOT EXISTS SentimentQuote TEXT,
                ADD COLUMN IF NOT EXISTS ProviderKey VARCHAR(50),
                ADD COLUMN IF NOT EXISTS ModelUsed VARCHAR(100),
                ADD COLUMN IF NOT EXISTS PromptTokens INT,
                ADD COLUMN IF NOT EXISTS CompletionTokens INT,
                ADD COLUMN IF NOT EXISTS CostUsd NUMERIC(10,6),
                ADD COLUMN IF NOT EXISTS WasSearchGrounded BOOLEAN NOT NULL DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS SourceUrlsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                ADD COLUMN IF NOT EXISTS PromptVersion VARCHAR(100) NOT NULL DEFAULT 'prompt-intelligence:v1',
                ADD COLUMN IF NOT EXISTS IsError BOOLEAN NOT NULL DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS ErrorMessage TEXT;

            ALTER TABLE PromptMentions
                ADD COLUMN IF NOT EXISTS PromptResponseId UUID REFERENCES PromptResponses(Id) ON DELETE CASCADE,
                ADD COLUMN IF NOT EXISTS IsRecommended BOOLEAN NOT NULL DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS RecommendationPosition INT;

            ALTER TABLE PromptCitations
                ADD COLUMN IF NOT EXISTS PromptResponseId UUID REFERENCES PromptResponses(Id) ON DELETE CASCADE;

            CREATE INDEX IF NOT EXISTS idx_promptmentions_response ON PromptMentions (PromptResponseId);
            CREATE INDEX IF NOT EXISTS idx_promptcitations_response ON PromptCitations (PromptResponseId);

            ALTER TABLE PromptVisibility
                ADD COLUMN IF NOT EXISTS VisibilityRank INT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS CitationShare INT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS SampleCount INT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS MethodologyVersion VARCHAR(100);

            UPDATE PromptVisibility
            SET MethodologyVersion = 'legacy-v2'
            WHERE MethodologyVersion IS NULL;

            ALTER TABLE PromptVisibility
                ALTER COLUMN MethodologyVersion SET DEFAULT 'prompt-visibility:v5-search-grounded-sampled',
                ALTER COLUMN MethodologyVersion SET NOT NULL;
            """),
        new(
            "202609100003_full_schema_reconciliation",
            "Reapply the complete non-destructive schema baseline after production drift",
            SelfHealingMigrations.Sql),
        new(
            "202609150001_assistant_conversations",
            "Add persistent assistant threads and messages",
            """
            CREATE TABLE IF NOT EXISTS AssistantThreads (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                UserId UUID NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
                Title VARCHAR(80) NOT NULL DEFAULT 'New conversation',
                CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_assistantthreads_user_updated
                ON AssistantThreads (OrganizationId, UserId, UpdatedAt DESC);

            CREATE TABLE IF NOT EXISTS AssistantMessages (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                ThreadId UUID NOT NULL REFERENCES AssistantThreads(Id) ON DELETE CASCADE,
                Role VARCHAR(20) NOT NULL CHECK (Role IN ('user', 'assistant')),
                Content TEXT NOT NULL,
                CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_assistantmessages_thread_created
                ON AssistantMessages (ThreadId, CreatedAt ASC);
            """),
        new(
            "202609150002_integration_lifecycle_security",
            "Add encrypted integration credential metadata and connection health",
            """
            ALTER TABLE Integrations ALTER COLUMN ApiKey TYPE TEXT;
            ALTER TABLE Integrations ADD COLUMN IF NOT EXISTS AuthType VARCHAR(50) NOT NULL DEFAULT 'api_key';
            ALTER TABLE Integrations ADD COLUMN IF NOT EXISTS Status VARCHAR(50) NOT NULL DEFAULT 'Connected';
            ALTER TABLE Integrations ADD COLUMN IF NOT EXISTS CredentialHint VARCHAR(255) NOT NULL DEFAULT '';
            ALTER TABLE Integrations ADD COLUMN IF NOT EXISTS LastVerifiedAt TIMESTAMP WITH TIME ZONE;
            ALTER TABLE Integrations ADD COLUMN IF NOT EXISTS LastError TEXT;
            """),
        new(
            "202609160001_openrouter_promptresponse_provenance_repair",
            "Add OpenRouter provenance fields to PromptResponses on existing databases",
            """
            ALTER TABLE PromptResponses
                ADD COLUMN IF NOT EXISTS Gateway VARCHAR(50),
                ADD COLUMN IF NOT EXISTS UpstreamProvider VARCHAR(100),
                ADD COLUMN IF NOT EXISTS GenerationId VARCHAR(255),
                ADD COLUMN IF NOT EXISTS LatencyMs BIGINT;

            CREATE OR REPLACE FUNCTION trg_promptresponses_protect_evidence() RETURNS TRIGGER AS $body$
            BEGIN
                IF NEW.ResponseText IS DISTINCT FROM OLD.ResponseText
                    OR NEW.ResponseLength IS DISTINCT FROM OLD.ResponseLength
                    OR NEW.Platform IS DISTINCT FROM OLD.Platform
                    OR NEW.PromptAnalysisId IS DISTINCT FROM OLD.PromptAnalysisId
                    OR NEW.CreatedAt IS DISTINCT FROM OLD.CreatedAt
                    OR NEW.ProviderKey IS DISTINCT FROM OLD.ProviderKey
                    OR NEW.ModelUsed IS DISTINCT FROM OLD.ModelUsed
                    OR NEW.PromptTokens IS DISTINCT FROM OLD.PromptTokens
                    OR NEW.CompletionTokens IS DISTINCT FROM OLD.CompletionTokens
                    OR NEW.CostUsd IS DISTINCT FROM OLD.CostUsd
                    OR NEW.WasSearchGrounded IS DISTINCT FROM OLD.WasSearchGrounded
                    OR NEW.SourceUrlsJson IS DISTINCT FROM OLD.SourceUrlsJson
                    OR NEW.Gateway IS DISTINCT FROM OLD.Gateway
                    OR NEW.UpstreamProvider IS DISTINCT FROM OLD.UpstreamProvider
                    OR NEW.GenerationId IS DISTINCT FROM OLD.GenerationId
                    OR NEW.LatencyMs IS DISTINCT FROM OLD.LatencyMs
                    OR NEW.PromptVersion IS DISTINCT FROM OLD.PromptVersion
                    OR NEW.IsError IS DISTINCT FROM OLD.IsError
                    OR NEW.ErrorMessage IS DISTINCT FROM OLD.ErrorMessage
                THEN
                    RAISE EXCEPTION 'PromptResponses evidence fields are immutable once inserted - only Sentiment/SentimentQuote may be updated.';
                END IF;
                RETURN NEW;
            END;
            $body$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS trg_protect_promptresponses_evidence ON PromptResponses;
            CREATE TRIGGER trg_protect_promptresponses_evidence
                BEFORE UPDATE ON PromptResponses
                FOR EACH ROW EXECUTE FUNCTION trg_promptresponses_protect_evidence();
            """),
        new(
            "202609170001_scraping_job_diagnostics",
            "Persist website crawl failure details for onboarding diagnostics",
            """
            ALTER TABLE ScrapingJobs ADD COLUMN IF NOT EXISTS ErrorMessage TEXT;
            """),
        new(
            "202609250001_agent_control_plane",
            "Add organization-scoped agent definitions, settings, schedules, runs, findings, and approvals",
            """
            CREATE TABLE IF NOT EXISTS AgentDefinitions (
                AgentKey VARCHAR(100) PRIMARY KEY,
                Name VARCHAR(150) NOT NULL,
                Stage VARCHAR(50) NOT NULL,
                Description TEXT NOT NULL,
                Version VARCHAR(50) NOT NULL DEFAULT '1.0',
                DefaultAutonomyLevel VARCHAR(20) NOT NULL DEFAULT 'Assist'
                    CHECK (DefaultAutonomyLevel IN ('Observe', 'Assist', 'Autopilot')),
                DefaultTriggerType VARCHAR(50) NOT NULL DEFAULT 'Event',
                DefaultSchedule VARCHAR(255) NOT NULL DEFAULT '',
                CapabilitiesJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                IsAvailable BOOLEAN NOT NULL DEFAULT TRUE,
                SortOrder INT NOT NULL DEFAULT 0,
                CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            INSERT INTO AgentDefinitions
                (AgentKey, Name, Stage, Description, Version, DefaultAutonomyLevel,
                 DefaultTriggerType, DefaultSchedule, CapabilitiesJson, IsAvailable, SortOrder)
            VALUES
                ('visibility-monitor', 'Visibility Monitor', 'Monitor',
                 'Detects meaningful visibility, citation, competitor, sentiment, and data-quality changes.',
                 '1.0', 'Assist', 'Event', 'scan.completed',
                 '["visibility.read","citations.read","competitors.read","alerts.propose"]'::jsonb, TRUE, 10),
                ('intelligence-analyst', 'Intelligence Analyst', 'Explain',
                 'Explains workspace changes using dated, organization-scoped evidence.',
                 '1.0', 'Assist', 'Event', 'finding.important',
                 '["workspace.read","findings.explain","assistant.respond"]'::jsonb, TRUE, 20),
                ('geo-strategy', 'GEO Strategy Agent', 'Recommend',
                 'Creates deduplicated and prioritized GEO recommendations from explained findings.',
                 '1.0', 'Assist', 'Cron', '0 9 * * 1',
                 '["findings.read","recommendations.propose","roadmap.propose"]'::jsonb, TRUE, 30),
                ('content-execution', 'Content Execution Agent', 'Execute',
                 'Prepares grounded briefs and drafts while keeping live publishing approval-gated.',
                 '1.0', 'Assist', 'Event', 'recommendation.approved',
                 '["knowledge.read","content.draft","content.optimize","publishing.propose"]'::jsonb, TRUE, 40),
                ('impact-reporting', 'Impact & Reporting Agent', 'Measure',
                 'Measures implemented recommendations and produces evidence-linked impact summaries.',
                 '1.0', 'Assist', 'Event', 'recommendation.implemented',
                 '["baselines.read","impact.measure","reports.prepare"]'::jsonb, TRUE, 50)
            ON CONFLICT (AgentKey) DO UPDATE SET
                Name = EXCLUDED.Name,
                Stage = EXCLUDED.Stage,
                Description = EXCLUDED.Description,
                Version = EXCLUDED.Version,
                DefaultAutonomyLevel = EXCLUDED.DefaultAutonomyLevel,
                DefaultTriggerType = EXCLUDED.DefaultTriggerType,
                DefaultSchedule = EXCLUDED.DefaultSchedule,
                CapabilitiesJson = EXCLUDED.CapabilitiesJson,
                IsAvailable = EXCLUDED.IsAvailable,
                SortOrder = EXCLUDED.SortOrder,
                UpdatedAt = CURRENT_TIMESTAMP;

            CREATE TABLE IF NOT EXISTS AgentSettings (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                AgentKey VARCHAR(100) NOT NULL REFERENCES AgentDefinitions(AgentKey),
                IsEnabled BOOLEAN NOT NULL DEFAULT TRUE,
                AutonomyLevel VARCHAR(20) NOT NULL DEFAULT 'Assist'
                    CHECK (AutonomyLevel IN ('Observe', 'Assist', 'Autopilot')),
                MaxRunsPerDay INT NOT NULL DEFAULT 5 CHECK (MaxRunsPerDay BETWEEN 0 AND 10000),
                MaxCostMicroUsdPerRun BIGINT NOT NULL DEFAULT 100000 CHECK (MaxCostMicroUsdPerRun >= 0),
                AllowedActionsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE (OrganizationId, AgentKey)
            );
            CREATE INDEX IF NOT EXISTS idx_agentsettings_org ON AgentSettings (OrganizationId, AgentKey);

            CREATE TABLE IF NOT EXISTS AgentSchedules (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                AgentKey VARCHAR(100) NOT NULL REFERENCES AgentDefinitions(AgentKey),
                TriggerType VARCHAR(50) NOT NULL CHECK (TriggerType IN ('Event', 'Cron', 'Manual')),
                TriggerExpression VARCHAR(255) NOT NULL DEFAULT '',
                TimeZone VARCHAR(100) NOT NULL DEFAULT 'UTC',
                IsEnabled BOOLEAN NOT NULL DEFAULT TRUE,
                LastRunAt TIMESTAMP WITH TIME ZONE,
                NextRunAt TIMESTAMP WITH TIME ZONE,
                CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE (OrganizationId, AgentKey, TriggerType, TriggerExpression)
            );
            CREATE INDEX IF NOT EXISTS idx_agentschedules_due
                ON AgentSchedules (IsEnabled, NextRunAt) WHERE NextRunAt IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_agentschedules_org ON AgentSchedules (OrganizationId, AgentKey);

            INSERT INTO AgentSettings
                (OrganizationId, AgentKey, IsEnabled, AutonomyLevel, MaxRunsPerDay, MaxCostMicroUsdPerRun, AllowedActionsJson)
            SELECT o.Id, d.AgentKey, TRUE, d.DefaultAutonomyLevel, 5, 100000, d.CapabilitiesJson
            FROM Organizations o
            CROSS JOIN AgentDefinitions d
            WHERE d.IsAvailable = TRUE
            ON CONFLICT (OrganizationId, AgentKey) DO NOTHING;

            INSERT INTO AgentSchedules
                (OrganizationId, AgentKey, TriggerType, TriggerExpression, TimeZone, IsEnabled)
            SELECT o.Id, d.AgentKey, d.DefaultTriggerType, d.DefaultSchedule, 'UTC', TRUE
            FROM Organizations o
            CROSS JOIN AgentDefinitions d
            WHERE d.IsAvailable = TRUE AND d.DefaultSchedule <> ''
            ON CONFLICT (OrganizationId, AgentKey, TriggerType, TriggerExpression) DO NOTHING;

            CREATE TABLE IF NOT EXISTS AgentRuns (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                AgentKey VARCHAR(100) NOT NULL REFERENCES AgentDefinitions(AgentKey),
                InitiatedByUserId UUID REFERENCES Users(Id) ON DELETE SET NULL,
                ParentRunId UUID REFERENCES AgentRuns(Id) ON DELETE SET NULL,
                TriggerType VARCHAR(50) NOT NULL DEFAULT 'Event',
                TriggerReference VARCHAR(255) NOT NULL DEFAULT '',
                Status VARCHAR(50) NOT NULL DEFAULT 'Queued'
                    CHECK (Status IN ('Queued', 'Running', 'WaitingForApproval', 'CancellationRequested', 'Completed', 'Failed', 'Cancelled')),
                IdempotencyKey VARCHAR(255) NOT NULL,
                InputJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                OutputJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                Provider VARCHAR(100) NOT NULL DEFAULT '',
                Model VARCHAR(150) NOT NULL DEFAULT '',
                PromptTokens INT NOT NULL DEFAULT 0 CHECK (PromptTokens >= 0),
                CompletionTokens INT NOT NULL DEFAULT 0 CHECK (CompletionTokens >= 0),
                CostMicroUsd BIGINT NOT NULL DEFAULT 0 CHECK (CostMicroUsd >= 0),
                Attempt INT NOT NULL DEFAULT 1 CHECK (Attempt >= 1),
                MaxAttempts INT NOT NULL DEFAULT 3 CHECK (MaxAttempts BETWEEN 1 AND 10),
                ErrorCode VARCHAR(100) NOT NULL DEFAULT '',
                ErrorMessage TEXT NOT NULL DEFAULT '',
                QueuedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                StartedAt TIMESTAMP WITH TIME ZONE,
                CompletedAt TIMESTAMP WITH TIME ZONE,
                CancelRequestedAt TIMESTAMP WITH TIME ZONE,
                UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE (OrganizationId, IdempotencyKey)
            );
            CREATE INDEX IF NOT EXISTS idx_agentruns_org_queued ON AgentRuns (OrganizationId, QueuedAt DESC);
            CREATE INDEX IF NOT EXISTS idx_agentruns_agent_status ON AgentRuns (OrganizationId, AgentKey, Status, QueuedAt DESC);
            CREATE INDEX IF NOT EXISTS idx_agentruns_queue ON AgentRuns (Status, QueuedAt) WHERE Status = 'Queued';

            CREATE TABLE IF NOT EXISTS AgentRunEvents (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                RunId UUID NOT NULL REFERENCES AgentRuns(Id) ON DELETE CASCADE,
                EventType VARCHAR(100) NOT NULL,
                Status VARCHAR(50) NOT NULL DEFAULT '',
                Message TEXT NOT NULL DEFAULT '',
                DataJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_agentrunevents_org_created ON AgentRunEvents (OrganizationId, CreatedAt DESC);
            CREATE INDEX IF NOT EXISTS idx_agentrunevents_run_created ON AgentRunEvents (RunId, CreatedAt);

            CREATE TABLE IF NOT EXISTS AgentFindings (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                RunId UUID REFERENCES AgentRuns(Id) ON DELETE SET NULL,
                AgentKey VARCHAR(100) NOT NULL REFERENCES AgentDefinitions(AgentKey),
                FindingType VARCHAR(100) NOT NULL,
                Severity VARCHAR(30) NOT NULL DEFAULT 'Info'
                    CHECK (Severity IN ('Info', 'Low', 'Medium', 'High', 'Critical', 'Good')),
                Title VARCHAR(255) NOT NULL,
                Summary TEXT NOT NULL DEFAULT '',
                EntityType VARCHAR(100) NOT NULL DEFAULT '',
                EntityIdsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                EvidenceJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                ObservationStartedAt TIMESTAMP WITH TIME ZONE,
                ObservationEndedAt TIMESTAMP WITH TIME ZONE,
                Confidence NUMERIC(5,4) NOT NULL DEFAULT 0 CHECK (Confidence BETWEEN 0 AND 1),
                DeduplicationKey VARCHAR(255) NOT NULL,
                Status VARCHAR(30) NOT NULL DEFAULT 'Open'
                    CHECK (Status IN ('Open', 'Investigating', 'Explained', 'Dismissed', 'Resolved')),
                CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ResolvedAt TIMESTAMP WITH TIME ZONE,
                UNIQUE (OrganizationId, DeduplicationKey)
            );
            CREATE INDEX IF NOT EXISTS idx_agentfindings_org_status ON AgentFindings (OrganizationId, Status, UpdatedAt DESC);
            CREATE INDEX IF NOT EXISTS idx_agentfindings_agent_updated ON AgentFindings (OrganizationId, AgentKey, UpdatedAt DESC);

            CREATE TABLE IF NOT EXISTS AgentApprovals (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                RunId UUID NOT NULL REFERENCES AgentRuns(Id) ON DELETE CASCADE,
                FindingId UUID REFERENCES AgentFindings(Id) ON DELETE SET NULL,
                AgentKey VARCHAR(100) NOT NULL REFERENCES AgentDefinitions(AgentKey),
                ActionType VARCHAR(100) NOT NULL,
                Title VARCHAR(255) NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                PayloadJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                RiskLevel VARCHAR(30) NOT NULL DEFAULT 'Medium'
                    CHECK (RiskLevel IN ('Low', 'Medium', 'High', 'Critical')),
                Status VARCHAR(30) NOT NULL DEFAULT 'Pending'
                    CHECK (Status IN ('Pending', 'Approved', 'Rejected', 'Expired', 'Executed', 'Failed')),
                IdempotencyKey VARCHAR(255) NOT NULL,
                RequestedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ExpiresAt TIMESTAMP WITH TIME ZONE,
                DecidedByUserId UUID REFERENCES Users(Id) ON DELETE SET NULL,
                DecidedAt TIMESTAMP WITH TIME ZONE,
                DecisionNote TEXT NOT NULL DEFAULT '',
                ExecutedAt TIMESTAMP WITH TIME ZONE,
                UNIQUE (OrganizationId, IdempotencyKey)
            );
            CREATE INDEX IF NOT EXISTS idx_agentapprovals_pending ON AgentApprovals (OrganizationId, RequestedAt DESC)
                WHERE Status = 'Pending';
            CREATE INDEX IF NOT EXISTS idx_agentapprovals_run ON AgentApprovals (RunId, RequestedAt DESC);

            INSERT INTO PlanLimits (PlanKey, FeatureKey, LimitValue) VALUES
                ('Trial', 'agent_autopilot', 0),
                ('Trial', 'agent_runs_per_day', 5),
                ('Trial', 'agent_cost_micro_usd_per_run', 100000),
                ('Starter', 'agent_autopilot', 0),
                ('Starter', 'agent_runs_per_day', 20),
                ('Starter', 'agent_cost_micro_usd_per_run', 500000),
                ('Pro', 'agent_autopilot', 1),
                ('Pro', 'agent_runs_per_day', 100),
                ('Pro', 'agent_cost_micro_usd_per_run', 2000000),
                ('Enterprise', 'agent_autopilot', 1),
                ('Enterprise', 'agent_runs_per_day', 500),
                ('Enterprise', 'agent_cost_micro_usd_per_run', 10000000)
            ON CONFLICT (PlanKey, FeatureKey) DO NOTHING;
            """),
        new(
            "202609250002_agent_recommendations",
            "Add evidence-linked GEO strategy recommendations with approval, ownership, and lifecycle state",
            """
            CREATE TABLE IF NOT EXISTS AgentRecommendations (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                RunId UUID NOT NULL REFERENCES AgentRuns(Id) ON DELETE CASCADE,
                FindingId UUID NOT NULL REFERENCES AgentFindings(Id) ON DELETE CASCADE,
                ApprovalId UUID REFERENCES AgentApprovals(Id) ON DELETE SET NULL,
                AgentKey VARCHAR(100) NOT NULL REFERENCES AgentDefinitions(AgentKey),
                RecommendationType VARCHAR(100) NOT NULL,
                Category VARCHAR(100) NOT NULL,
                Title VARCHAR(255) NOT NULL,
                Summary TEXT NOT NULL DEFAULT '',
                Rationale TEXT NOT NULL DEFAULT '',
                TargetType VARCHAR(100) NOT NULL DEFAULT '',
                TargetKey VARCHAR(255) NOT NULL DEFAULT '',
                EvidenceJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                ActionPlanJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                ValidationPlanJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                ExpectedImpact TEXT NOT NULL DEFAULT '',
                ImpactScore INT NOT NULL CHECK (ImpactScore BETWEEN 0 AND 100),
                EffortScore INT NOT NULL CHECK (EffortScore BETWEEN 0 AND 100),
                UrgencyScore INT NOT NULL CHECK (UrgencyScore BETWEEN 0 AND 100),
                GoalAlignmentScore INT NOT NULL CHECK (GoalAlignmentScore BETWEEN 0 AND 100),
                Confidence NUMERIC(5,4) NOT NULL CHECK (Confidence BETWEEN 0 AND 1),
                PriorityScore NUMERIC(6,2) NOT NULL CHECK (PriorityScore BETWEEN 0 AND 100),
                Status VARCHAR(30) NOT NULL DEFAULT 'AwaitingApproval'
                    CHECK (Status IN ('AwaitingApproval', 'Approved', 'Rejected', 'Assigned', 'InProgress', 'Implemented', 'Dismissed')),
                AssignedToUserId UUID REFERENCES Users(Id) ON DELETE SET NULL,
                RejectionReason TEXT NOT NULL DEFAULT '',
                DeduplicationKey VARCHAR(255) NOT NULL,
                CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ApprovedAt TIMESTAMP WITH TIME ZONE,
                AssignedAt TIMESTAMP WITH TIME ZONE,
                ImplementedAt TIMESTAMP WITH TIME ZONE,
                UNIQUE (OrganizationId, DeduplicationKey)
            );
            CREATE INDEX IF NOT EXISTS idx_agentrecommendations_org_status
                ON AgentRecommendations (OrganizationId, Status, PriorityScore DESC, UpdatedAt DESC);
            CREATE INDEX IF NOT EXISTS idx_agentrecommendations_assignee
                ON AgentRecommendations (OrganizationId, AssignedToUserId, UpdatedAt DESC)
                WHERE AssignedToUserId IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS idx_agentrecommendations_approval
                ON AgentRecommendations (ApprovalId) WHERE ApprovalId IS NOT NULL;

            CREATE TABLE IF NOT EXISTS AgentStrategyPreferences (
                OrganizationId UUID PRIMARY KEY REFERENCES Organizations(Id) ON DELETE CASCADE,
                PrimaryGoal VARCHAR(50) NOT NULL DEFAULT 'Balanced'
                    CHECK (PrimaryGoal IN ('Balanced', 'GrowVisibility', 'ImproveCitations', 'DefendCompetitors', 'ImproveBrandAccuracy')),
                CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            INSERT INTO AgentStrategyPreferences (OrganizationId, PrimaryGoal)
            SELECT Id, 'Balanced' FROM Organizations
            ON CONFLICT (OrganizationId) DO NOTHING;

            INSERT INTO AgentSchedules
                (OrganizationId, AgentKey, TriggerType, TriggerExpression, TimeZone, IsEnabled)
            SELECT Id, 'geo-strategy', 'Event', 'finding.explained', 'UTC', TRUE
            FROM Organizations
            ON CONFLICT (OrganizationId, AgentKey, TriggerType, TriggerExpression) DO NOTHING;
            """),
        new(
            "202609250003_agent_content_execution",
            "Add approval-gated, Knowledge Vault-grounded content execution records",
            """
            CREATE TABLE IF NOT EXISTS AgentContentExecutions (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                RunId UUID NOT NULL REFERENCES AgentRuns(Id) ON DELETE CASCADE,
                RecommendationId UUID NOT NULL REFERENCES AgentRecommendations(Id) ON DELETE CASCADE,
                ContentDraftId UUID REFERENCES ContentDrafts(Id) ON DELETE SET NULL,
                KnowledgeBaseId UUID REFERENCES KnowledgeBases(Id) ON DELETE SET NULL,
                PublishApprovalId UUID REFERENCES AgentApprovals(Id) ON DELETE SET NULL,
                Status VARCHAR(40) NOT NULL DEFAULT 'Preparing'
                    CHECK (Status IN ('Preparing', 'NeedsEvidence', 'ReadyForReview', 'AwaitingPublishApproval',
                                      'ApprovedForPublishing', 'Publishing', 'Published', 'Rejected',
                                      'PublishFailed', 'Failed')),
                BriefJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                EvidenceJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                ReviewDiffJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                PolicyChecksJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                ReviewNote TEXT NOT NULL DEFAULT '',
                ReviewedByUserId UUID REFERENCES Users(Id) ON DELETE SET NULL,
                ReviewedAt TIMESTAMP WITH TIME ZONE,
                PublishedAt TIMESTAMP WITH TIME ZONE,
                CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE (OrganizationId, RecommendationId)
            );
            CREATE INDEX IF NOT EXISTS idx_agentcontentexecutions_org_status
                ON AgentContentExecutions (OrganizationId, Status, UpdatedAt DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_agentcontentexecutions_draft
                ON AgentContentExecutions (ContentDraftId) WHERE ContentDraftId IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS idx_agentcontentexecutions_publish_approval
                ON AgentContentExecutions (PublishApprovalId) WHERE PublishApprovalId IS NOT NULL;

            INSERT INTO AgentSchedules
                (OrganizationId, AgentKey, TriggerType, TriggerExpression, TimeZone, IsEnabled)
            SELECT Id, 'content-execution', 'Event', 'recommendation.approved', 'UTC', TRUE
            FROM Organizations
            ON CONFLICT (OrganizationId, AgentKey, TriggerType, TriggerExpression) DO NOTHING;
            """),
        new(
            "202609250004_agent_impact_measurements",
            "Add evidence-linked baselines, follow-up measurements, and client-ready impact reports",
            """
            CREATE TABLE IF NOT EXISTS AgentImpactMeasurements (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
                RecommendationId UUID NOT NULL REFERENCES AgentRecommendations(Id) ON DELETE CASCADE,
                BaselineRunId UUID NOT NULL REFERENCES AgentRuns(Id) ON DELETE CASCADE,
                MeasurementRunId UUID REFERENCES AgentRuns(Id) ON DELETE SET NULL,
                Status VARCHAR(30) NOT NULL DEFAULT 'Pending'
                    CHECK (Status IN ('Pending', 'WaitingForData', 'NeedsBaseline', 'Measured', 'Failed', 'Cancelled')),
                Outcome VARCHAR(30) NOT NULL DEFAULT 'Pending'
                    CHECK (Outcome IN ('Pending', 'Improved', 'Neutral', 'Regressed', 'Inconclusive')),
                MonitoringWindowDays INT NOT NULL DEFAULT 14 CHECK (MonitoringWindowDays BETWEEN 1 AND 90),
                BaselineCapturedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                MeasurementDueAt TIMESTAMP WITH TIME ZONE NOT NULL,
                MeasuredAt TIMESTAMP WITH TIME ZONE,
                BaselineJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                FollowupJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                DeltaJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                EvidenceJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                ReportJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                Confidence NUMERIC(5,4) NOT NULL DEFAULT 0 CHECK (Confidence BETWEEN 0 AND 1),
                ErrorMessage TEXT NOT NULL DEFAULT '',
                CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE (OrganizationId, RecommendationId)
            );
            CREATE INDEX IF NOT EXISTS idx_agentimpactmeasurements_due
                ON AgentImpactMeasurements (MeasurementDueAt, Status)
                WHERE Status IN ('Pending', 'WaitingForData');
            CREATE INDEX IF NOT EXISTS idx_agentimpactmeasurements_org_outcome
                ON AgentImpactMeasurements (OrganizationId, Outcome, UpdatedAt DESC);

            INSERT INTO AgentSchedules
                (OrganizationId, AgentKey, TriggerType, TriggerExpression, TimeZone, IsEnabled)
            SELECT Id, 'impact-reporting', 'Event', 'recommendation.implemented', 'UTC', TRUE
            FROM Organizations
            ON CONFLICT (OrganizationId, AgentKey, TriggerType, TriggerExpression) DO NOTHING;
            """)
    ];
}
