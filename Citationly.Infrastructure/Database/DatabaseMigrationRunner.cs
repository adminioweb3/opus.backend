using Citationly.Application.Interfaces;
using Dapper;

namespace Citationly.Infrastructure.Database;

public sealed class DatabaseMigrationRunner
{
    private const long MigrationLockKey = 78219360420501;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public DatabaseMigrationRunner(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<IReadOnlyList<AppliedDatabaseMigration>> RunPendingAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();

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
            """)
    ];
}
