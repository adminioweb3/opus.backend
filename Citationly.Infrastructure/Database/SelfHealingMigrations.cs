namespace Citationly.Infrastructure.Database;

/// <summary>
/// The idempotent CREATE/ALTER statements that heal schema drift on a live database — tables and
/// columns that init.sql defines but that were added after this database was first created, so
/// init.sql (which only ever runs against a brand-new database) never actually ran them.
///
/// Deliberately excludes anything destructive: init.sql itself opens with `DROP TABLE ... CASCADE`
/// for a from-scratch dev reset, which must never run automatically against a live database. Used
/// both by Program.cs on every startup and by AdminController's /database/reset endpoint (which
/// runs init.sql first, then this, to fully recreate a fresh database including tables init.sql
/// doesn't know about yet).
/// </summary>
public static class SelfHealingMigrations
{
    public const string Sql = @"
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS PlanType VARCHAR(50) NOT NULL DEFAULT 'Trial';
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS TrialEndsAt TIMESTAMP WITH TIME ZONE;
        UPDATE Organizations SET TrialEndsAt = CreatedAt + INTERVAL '7 days' WHERE TrialEndsAt IS NULL;

        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS DomainUrl VARCHAR(255) NOT NULL DEFAULT '';
        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS PlatformName VARCHAR(100) NOT NULL DEFAULT 'Custom';
        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS HealthScore INT NOT NULL DEFAULT 0;
        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS VisibilityScore INT NOT NULL DEFAULT 0;
        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS Status VARCHAR(50) NOT NULL DEFAULT 'Connected';
        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS LastSyncAt TIMESTAMP WITH TIME ZONE;

        CREATE TABLE IF NOT EXISTS KnowledgeBases (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            Name VARCHAR(255) NOT NULL,
            Icon VARCHAR(100) DEFAULT 'Building2',
            Tint VARCHAR(50) DEFAULT '#6366F1',
            Bg VARCHAR(50) DEFAULT '#EEEEFE',
            Description TEXT,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS SourceFolders (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            KnowledgeBaseId UUID REFERENCES KnowledgeBases(Id) ON DELETE CASCADE,
            Name VARCHAR(255) NOT NULL,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS Integrations (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            PlatformName VARCHAR(100) NOT NULL,
            ApiUrl VARCHAR(2048),
            ApiKey VARCHAR(1024),
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(OrganizationId, PlatformName)
        );

        CREATE TABLE IF NOT EXISTS ContentDrafts (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            Title VARCHAR(512) NOT NULL DEFAULT '',
            ContentType VARCHAR(100) NOT NULL DEFAULT '',
            Content TEXT NOT NULL DEFAULT '',
            WordCount INT NOT NULL DEFAULT 0,
            Status VARCHAR(50) NOT NULL DEFAULT 'Draft',
            RequestJson JSONB NOT NULL DEFAULT '{}'::jsonb,
            CompetitorUrl VARCHAR(2048),
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        ALTER TABLE ContentDrafts ADD COLUMN IF NOT EXISTS PublishedUrl VARCHAR(2048);
        ALTER TABLE ContentDrafts ADD COLUMN IF NOT EXISTS PublishedAt TIMESTAMP WITH TIME ZONE;
        ALTER TABLE ContentDrafts ADD COLUMN IF NOT EXISTS IntegrationId UUID REFERENCES Integrations(Id) ON DELETE SET NULL;
        ALTER TABLE ContentDrafts ADD COLUMN IF NOT EXISTS PublishError TEXT;

        CREATE TABLE IF NOT EXISTS ContentOptimizations (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            ContentDraftId UUID REFERENCES ContentDrafts(Id) ON DELETE CASCADE,
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            SeoScore INT NOT NULL DEFAULT 0,
            ReadabilityScore INT NOT NULL DEFAULT 0,
            HumanizedScore INT NOT NULL DEFAULT 0,
            AiScore INT NOT NULL DEFAULT 0,
            KeywordDensity NUMERIC(5,2) NOT NULL DEFAULT 0,
            PrimaryKeyword VARCHAR(255) NOT NULL DEFAULT '',
            RecommendationsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
            InternalLinksJson JSONB NOT NULL DEFAULT '[]'::jsonb,
            CitationRecsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
            OptimizedContent TEXT NOT NULL DEFAULT '',
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS GeoPillars (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID NOT NULL,
            ScanDate DATE NOT NULL,
            PillarKey TEXT NOT NULL,
            Label TEXT NOT NULL,
            Description TEXT NOT NULL,
            Score INT NOT NULL,
            UNIQUE (OrganizationId, ScanDate, PillarKey)
        );

        CREATE TABLE IF NOT EXISTS PromptCoverages (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID NOT NULL,
            ScanDate DATE NOT NULL,
            PromptType TEXT NOT NULL,
            Example TEXT NOT NULL,
            Note TEXT NOT NULL,
            Percentage INT NOT NULL,
            Direction TEXT NOT NULL,
            UNIQUE (OrganizationId, ScanDate, PromptType)
        );

        CREATE TABLE IF NOT EXISTS WinLossEvents (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID NOT NULL,
            Timestamp TIMESTAMPTZ NOT NULL,
            Type TEXT NOT NULL,
            Title TEXT NOT NULL,
            Engine TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Invites (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            Email VARCHAR(255) NOT NULL,
            Role VARCHAR(50) NOT NULL DEFAULT 'Viewer',
            Token VARCHAR(64) NOT NULL UNIQUE,
            InvitedByUserId UUID REFERENCES Users(Id) ON DELETE SET NULL,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            ExpiresAt TIMESTAMP WITH TIME ZONE NOT NULL,
            AcceptedAt TIMESTAMP WITH TIME ZONE
        );

        -- Postgres can't CREATE OR REPLACE a function whose RETURNS TABLE shape changed (only body
        -- changes are allowed in place) — drop first so this heals databases left over from an
        -- earlier version of this function with a different return signature.
        DROP FUNCTION IF EXISTS sp_CreateOrGetUser(VARCHAR, VARCHAR, VARCHAR);

        CREATE OR REPLACE FUNCTION sp_CreateOrGetUser(
            p_FirebaseUid VARCHAR,
            p_Email VARCHAR,
            p_DisplayName VARCHAR
        )
        RETURNS TABLE (
            UserId UUID,
            OrganizationId UUID,
            Role VARCHAR
        ) AS $$
        DECLARE
            v_UserId UUID;
            v_OrganizationId UUID;
            v_Role VARCHAR;
        BEGIN
            -- Lock on Email, not FirebaseUid: Email carries the actual unique constraint being
            -- protected here, including the email-fallback lookup below.
            PERFORM pg_advisory_xact_lock(hashtext(p_Email));

            SELECT u.Id, u.OrganizationId, u.Role INTO v_UserId, v_OrganizationId, v_Role
            FROM Users u WHERE u.FirebaseUid = p_FirebaseUid;

            IF v_UserId IS NULL THEN
                -- Firebase can issue a different UID for an email that already has a Users row
                -- (account recreated, a different sign-in provider linked, manual Firebase console
                -- edits). Email is the durable identity — re-link FirebaseUid to the existing row
                -- instead of INSERTing a second one and hitting users_email_key.
                SELECT u.Id, u.OrganizationId, u.Role INTO v_UserId, v_OrganizationId, v_Role
                FROM Users u WHERE u.Email = p_Email;

                IF v_UserId IS NOT NULL THEN
                    UPDATE Users SET FirebaseUid = p_FirebaseUid WHERE Id = v_UserId;
                END IF;
            END IF;

            -- Genuinely new user: check for a pending team invite before creating a brand new
            -- organization. Invites are matched purely by email — anyone who registers/logs in
            -- with the invited address is automatically joined to that org at the invited role.
            IF v_UserId IS NULL THEN
                DECLARE
                    v_InviteId UUID;
                    v_InviteOrgId UUID;
                    v_InviteRole VARCHAR;
                BEGIN
                    SELECT i.Id, i.OrganizationId, i.Role INTO v_InviteId, v_InviteOrgId, v_InviteRole
                    FROM Invites i
                    WHERE LOWER(i.Email) = LOWER(p_Email) AND i.AcceptedAt IS NULL AND i.ExpiresAt > CURRENT_TIMESTAMP
                    ORDER BY i.CreatedAt DESC
                    LIMIT 1;

                    IF v_InviteId IS NOT NULL THEN
                        INSERT INTO Users (OrganizationId, FirebaseUid, Email, DisplayName, Role)
                        VALUES (v_InviteOrgId, p_FirebaseUid, p_Email, p_DisplayName, v_InviteRole)
                        RETURNING Id INTO v_UserId;

                        UPDATE Invites SET AcceptedAt = CURRENT_TIMESTAMP WHERE Id = v_InviteId;

                        v_OrganizationId := v_InviteOrgId;
                        v_Role := v_InviteRole;
                    END IF;
                END;
            END IF;

            IF v_UserId IS NULL THEN
                INSERT INTO Organizations (Name, PlanType, TrialEndsAt)
                VALUES (p_DisplayName || '''s Org', 'Trial', CURRENT_TIMESTAMP + INTERVAL '7 days')
                RETURNING Id INTO v_OrganizationId;

                INSERT INTO Users (OrganizationId, FirebaseUid, Email, DisplayName, Role)
                VALUES (v_OrganizationId, p_FirebaseUid, p_Email, p_DisplayName, 'Admin')
                RETURNING Id INTO v_UserId;

                v_Role := 'Admin';
            END IF;

            RETURN QUERY SELECT v_UserId, v_OrganizationId, v_Role;
        END;
        $$ LANGUAGE plpgsql;
    ";
}
