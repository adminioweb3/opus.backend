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
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS Industry VARCHAR(255);
        UPDATE Organizations SET TrialEndsAt = CreatedAt + INTERVAL '7 days' WHERE TrialEndsAt IS NULL;

        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS DomainUrl VARCHAR(255) NOT NULL DEFAULT '';
        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS PlatformName VARCHAR(100) NOT NULL DEFAULT 'Custom';
        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS HealthScore INT NOT NULL DEFAULT 0;
        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS VisibilityScore INT NOT NULL DEFAULT 0;
        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS Status VARCHAR(50) NOT NULL DEFAULT 'Connected';
        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS LastSyncAt TIMESTAMP WITH TIME ZONE;
        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS CompanyId UUID REFERENCES Company(Id) ON DELETE SET NULL;
        CREATE INDEX IF NOT EXISTS idx_websites_companyid ON Websites (CompanyId);

        -- Unified competitor view: link related organizations that operate in same market
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS RelatedOrganizationId UUID REFERENCES Organizations(Id) ON DELETE SET NULL;
        CREATE INDEX IF NOT EXISTS idx_organizations_related ON Organizations (RelatedOrganizationId);

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

        -- Answer Atlas / Prompt Intelligence — topic -> question -> analysis -> per-engine results.
        -- These tables back the already-written PromptIntelligenceRepository/Controller, which
        -- until now had no schema to run against.
        CREATE TABLE IF NOT EXISTS PromptTopics (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            Name VARCHAR(255) NOT NULL,
            Description TEXT NOT NULL DEFAULT '',
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS PromptQuestions (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            PromptTopicId UUID REFERENCES PromptTopics(Id) ON DELETE CASCADE,
            PromptText TEXT NOT NULL,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        ALTER TABLE PromptQuestions ADD COLUMN IF NOT EXISTS IsActive BOOLEAN NOT NULL DEFAULT TRUE;
        ALTER TABLE PromptQuestions ADD COLUMN IF NOT EXISTS Region VARCHAR(100) NOT NULL DEFAULT 'Global';
        ALTER TABLE PromptQuestions ADD COLUMN IF NOT EXISTS Persona VARCHAR(255);

        CREATE TABLE IF NOT EXISTS PromptAnalysis (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            PromptQuestionId UUID REFERENCES PromptQuestions(Id) ON DELETE CASCADE,
            RunAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            Status VARCHAR(50) NOT NULL DEFAULT 'Running',
            ErrorMessage TEXT
        );

        CREATE TABLE IF NOT EXISTS PromptResponses (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
            Platform VARCHAR(100) NOT NULL DEFAULT '',
            ResponseText TEXT NOT NULL DEFAULT '',
            ResponseLength INT NOT NULL DEFAULT 0,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        ALTER TABLE PromptResponses ADD COLUMN IF NOT EXISTS Sentiment VARCHAR(10);
        ALTER TABLE PromptResponses ADD COLUMN IF NOT EXISTS SentimentQuote TEXT;

        CREATE TABLE IF NOT EXISTS PromptMentions (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
            Platform VARCHAR(100) NOT NULL DEFAULT '',
            EntityName VARCHAR(255) NOT NULL DEFAULT '',
            IsBrand BOOLEAN NOT NULL DEFAULT FALSE,
            ContextSnippet TEXT NOT NULL DEFAULT '',
            Position INT NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS PromptVisibility (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
            OverallVisibilityScore INT NOT NULL DEFAULT 0,
            MentionFrequency INT NOT NULL DEFAULT 0,
            AveragePosition INT NOT NULL DEFAULT 0,
            ShareOfVoice INT NOT NULL DEFAULT 0,
            CitationCount INT NOT NULL DEFAULT 0,
            CompetitorCount INT NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS PromptRecommendations (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
            Category VARCHAR(100) NOT NULL DEFAULT '',
            Title VARCHAR(255) NOT NULL DEFAULT '',
            Description TEXT NOT NULL DEFAULT '',
            Priority VARCHAR(50) NOT NULL DEFAULT 'Medium',
            Difficulty VARCHAR(50) NOT NULL DEFAULT 'Medium',
            EstimatedVisibilityGain INT NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS CompetitorComparisons (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
            CompetitorName VARCHAR(255) NOT NULL DEFAULT '',
            VisibilityScore INT NOT NULL DEFAULT 0,
            ShareOfVoice INT NOT NULL DEFAULT 0,
            MissingTopicsJson JSONB NOT NULL DEFAULT '[]'::jsonb
        );

        CREATE TABLE IF NOT EXISTS PromptCitations (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
            Platform VARCHAR(100) NOT NULL DEFAULT '',
            Domain VARCHAR(255) NOT NULL DEFAULT '',
            Url TEXT NOT NULL DEFAULT '',
            Category VARCHAR(50) NOT NULL DEFAULT 'Other',
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS PromptFanouts (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            PromptQuestionId UUID REFERENCES PromptQuestions(Id) ON DELETE CASCADE,
            FanoutText TEXT NOT NULL DEFAULT '',
            Engine VARCHAR(100) NOT NULL DEFAULT '',
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
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
            -- Email is case-insensitive from here on (lock key, lookups, storage). A Google/GitHub-
            -- linked identity can report a differently-cased email than a password account
            -- registered for the exact same address, and Users.Email's UNIQUE constraint is
            -- case-sensitive — without this, signing in via a second provider for the same real
            -- address silently created a second Organization instead of matching the existing one.
            p_Email := LOWER(TRIM(p_Email));

            -- Lock on Email, not FirebaseUid: Email carries the actual unique constraint being
            -- protected here, including the email-fallback lookup below.
            PERFORM pg_advisory_xact_lock(hashtext(p_Email));

            SELECT u.Id, u.OrganizationId, u.Role INTO v_UserId, v_OrganizationId, v_Role
            FROM Users u WHERE u.FirebaseUid = p_FirebaseUid;

            IF v_UserId IS NULL THEN
                -- Firebase can issue a different UID for an email that already has a Users row
                -- (account recreated, a different sign-in provider linked, manual Firebase console
                -- edits). Email is the durable identity — re-link FirebaseUid to the existing row
                -- instead of INSERTing a second one and hitting users_email_key. LOWER() on the
                -- stored side too, so this still matches legacy mixed-case rows.
                SELECT u.Id, u.OrganizationId, u.Role INTO v_UserId, v_OrganizationId, v_Role
                FROM Users u WHERE LOWER(u.Email) = p_Email;

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
                    WHERE LOWER(i.Email) = p_Email AND i.AcceptedAt IS NULL AND i.ExpiresAt > CURRENT_TIMESTAMP
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

        -- Company Knowledge Graph — shared, deduplicated, cross-org company registry. See
        -- init.sql for the authoritative comments on this section.
        CREATE TABLE IF NOT EXISTS Company (
            Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            NormalizedDomain VARCHAR(255) NOT NULL,
            Website VARCHAR(2048) NOT NULL,
            CompanyName VARCHAR(255) NOT NULL,
            Industry VARCHAR(255),
            BusinessProfileJson JSONB NOT NULL DEFAULT '{}'::jsonb,
            Embedding FLOAT8[],
            EmbeddingModel VARCHAR(100),
            EmbeddingUpdatedAt TIMESTAMP WITH TIME ZONE,
            SourceOrganizationId UUID REFERENCES Organizations(Id) ON DELETE SET NULL,
            LastAnalyzedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_company_normalizeddomain ON Company (NormalizedDomain);
        CREATE INDEX IF NOT EXISTS idx_company_industry ON Company (Industry);

        CREATE TABLE IF NOT EXISTS CompanyCompetitor (
            Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
            CompanyId UUID NOT NULL REFERENCES Company(Id) ON DELETE CASCADE,
            CompetitorCompanyId UUID NOT NULL REFERENCES Company(Id) ON DELETE CASCADE,
            Similarity NUMERIC(5,2) NOT NULL DEFAULT 0,
            Confidence INT NOT NULL DEFAULT 0,
            Rank INT NOT NULL DEFAULT 0,
            Reason TEXT,
            Strength TEXT,
            Weakness TEXT,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT chk_companycompetitor_not_self CHECK (CompanyId <> CompetitorCompanyId)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_companycompetitor_pair ON CompanyCompetitor (CompanyId, CompetitorCompanyId);
        CREATE INDEX IF NOT EXISTS idx_companycompetitor_company_rank ON CompanyCompetitor (CompanyId, Rank);

        ALTER TABLE Websites ADD COLUMN IF NOT EXISTS CompanyId UUID REFERENCES Company(Id) ON DELETE SET NULL;
        CREATE INDEX IF NOT EXISTS idx_websites_companyid ON Websites (CompanyId);

        -- Multi-Auth Provider Support: track all auth methods linked to each user
        CREATE TABLE IF NOT EXISTS AuthProviders (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            UserId UUID NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
            Provider VARCHAR(50) NOT NULL,
            ProviderUid VARCHAR(255) NOT NULL,
            LinkedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(Provider, ProviderUid),
            UNIQUE(UserId, Provider)
        );
        CREATE INDEX IF NOT EXISTS idx_authproviders_provideruid ON AuthProviders (Provider, ProviderUid);
        CREATE INDEX IF NOT EXISTS idx_authproviders_userid ON AuthProviders (UserId);

        -- Link Account API: track pending account linking requests
        CREATE TABLE IF NOT EXISTS PendingAccountLinks (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            Email VARCHAR(255) NOT NULL,
            Provider VARCHAR(50) NOT NULL,
            ProviderUid VARCHAR(255) NOT NULL,
            ProviderEmail VARCHAR(255),
            ExpiresAt TIMESTAMP WITH TIME ZONE NOT NULL,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(Provider, ProviderUid)
        );
        CREATE INDEX IF NOT EXISTS idx_pendinglinks_email ON PendingAccountLinks (Email);
        CREATE INDEX IF NOT EXISTS idx_pendinglinks_expiresat ON PendingAccountLinks (ExpiresAt);

        -- Enhanced sp_CreateOrGetUser with AuthProviders support
        CREATE OR REPLACE FUNCTION sp_CreateOrGetUserV2(
            p_FirebaseUid VARCHAR,
            p_Provider VARCHAR,
            p_ProviderUid VARCHAR,
            p_Email VARCHAR,
            p_DisplayName VARCHAR
        )
        RETURNS TABLE (
            UserId UUID,
            OrganizationId UUID,
            Role VARCHAR,
            IsNewUser BOOLEAN
        ) AS $$
        DECLARE
            v_UserId UUID;
            v_OrganizationId UUID;
            v_Role VARCHAR;
            v_IsNewUser BOOLEAN := FALSE;
        BEGIN
            p_Email := LOWER(TRIM(p_Email));
            PERFORM pg_advisory_xact_lock(hashtext(p_Email));

            -- 1. Check if this provider+uid combo is already linked
            SELECT ap.UserId INTO v_UserId FROM AuthProviders ap WHERE ap.Provider = p_Provider AND ap.ProviderUid = p_ProviderUid;

            IF v_UserId IS NOT NULL THEN
                -- Auth already exists, get org and role
                SELECT u.OrganizationId, u.Role INTO v_OrganizationId, v_Role FROM Users u WHERE u.Id = v_UserId;
                RETURN QUERY SELECT v_UserId AS UserId, v_OrganizationId AS OrganizationId, v_Role AS Role, FALSE AS IsNewUser;
                RETURN;
            END IF;

            -- 2. Check if email already exists
            SELECT u.Id, u.OrganizationId, u.Role INTO v_UserId, v_OrganizationId, v_Role
            FROM Users u WHERE LOWER(u.Email) = p_Email;

            IF v_UserId IS NOT NULL THEN
                -- Email exists: link this new provider to existing user
                INSERT INTO AuthProviders (UserId, Provider, ProviderUid) VALUES (v_UserId, p_Provider, p_ProviderUid)
                ON CONFLICT (UserId, Provider) DO NOTHING;
                RETURN QUERY SELECT v_UserId AS UserId, v_OrganizationId AS OrganizationId, v_Role AS Role, FALSE AS IsNewUser;
                RETURN;
            END IF;

            -- 3. New user: check for team invite
            DECLARE
                v_InviteId UUID;
                v_InviteOrgId UUID;
                v_InviteRole VARCHAR;
            BEGIN
                SELECT i.Id, i.OrganizationId, i.Role INTO v_InviteId, v_InviteOrgId, v_InviteRole
                FROM Invites i
                WHERE LOWER(i.Email) = p_Email AND i.AcceptedAt IS NULL AND i.ExpiresAt > CURRENT_TIMESTAMP
                ORDER BY i.CreatedAt DESC LIMIT 1;

                IF v_InviteId IS NOT NULL THEN
                    INSERT INTO Users (OrganizationId, FirebaseUid, Email, DisplayName, Role)
                    VALUES (v_InviteOrgId, p_FirebaseUid, p_Email, p_DisplayName, v_InviteRole)
                    RETURNING Id INTO v_UserId;

                    INSERT INTO AuthProviders (UserId, Provider, ProviderUid) VALUES (v_UserId, p_Provider, p_ProviderUid);
                    UPDATE Invites SET AcceptedAt = CURRENT_TIMESTAMP WHERE Id = v_InviteId;

                    v_OrganizationId := v_InviteOrgId;
                    v_Role := v_InviteRole;
                    v_IsNewUser := TRUE;
                    RETURN QUERY SELECT v_UserId AS UserId, v_OrganizationId AS OrganizationId, v_Role AS Role, v_IsNewUser AS IsNewUser;
                    RETURN;
                END IF;
            END;

            -- 4. Brand new user: create organization and user
            INSERT INTO Organizations (Name, PlanType, TrialEndsAt)
            VALUES (p_DisplayName || '''s Org', 'Trial', CURRENT_TIMESTAMP + INTERVAL '7 days')
            RETURNING Id INTO v_OrganizationId;

            INSERT INTO Users (OrganizationId, FirebaseUid, Email, DisplayName, Role)
            VALUES (v_OrganizationId, p_FirebaseUid, p_Email, p_DisplayName, 'Admin')
            RETURNING Id INTO v_UserId;

            INSERT INTO AuthProviders (UserId, Provider, ProviderUid) VALUES (v_UserId, p_Provider, p_ProviderUid);

            v_Role := 'Admin';
            v_IsNewUser := TRUE;
            RETURN QUERY SELECT v_UserId AS UserId, v_OrganizationId AS OrganizationId, v_Role AS Role, v_IsNewUser AS IsNewUser;
        END;
        $$ LANGUAGE plpgsql;
    ";
}
