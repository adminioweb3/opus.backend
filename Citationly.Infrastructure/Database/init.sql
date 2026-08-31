-- Enable UUID extension
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Drop tables to reset DB
DROP TABLE IF EXISTS ExtractedLinks CASCADE;
DROP TABLE IF EXISTS ExtractedImages CASCADE;
DROP TABLE IF EXISTS WebsiteMetadata CASCADE;
DROP TABLE IF EXISTS ScrapedPages CASCADE;
DROP TABLE IF EXISTS ScrapingJobs CASCADE;
DROP TABLE IF EXISTS ShareOfVoice CASCADE;
DROP TABLE IF EXISTS HistoricalScans CASCADE;
DROP TABLE IF EXISTS BrandMentions CASCADE;
DROP TABLE IF EXISTS AiSearchPrompts CASCADE;
DROP TABLE IF EXISTS Competitors CASCADE;
DROP TABLE IF EXISTS CompanyCompetitor CASCADE;
DROP TABLE IF EXISTS Company CASCADE;
DROP TABLE IF EXISTS Embeddings CASCADE;
DROP TABLE IF EXISTS Integrations CASCADE;
DROP TABLE IF EXISTS SourceFolders CASCADE;
DROP TABLE IF EXISTS KnowledgeBases CASCADE;
DROP TABLE IF EXISTS Recommendations CASCADE;
DROP TABLE IF EXISTS CrawledPages CASCADE;
DROP TABLE IF EXISTS Websites CASCADE;
DROP TABLE IF EXISTS Users CASCADE;
DROP TABLE IF EXISTS Organizations CASCADE;

-- Organizations (Tenants)
CREATE TABLE IF NOT EXISTS Organizations (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    Name VARCHAR(255) NOT NULL,
    PlanType VARCHAR(50) NOT NULL DEFAULT 'Trial',
    TrialEndsAt TIMESTAMP WITH TIME ZONE DEFAULT (CURRENT_TIMESTAMP + INTERVAL '7 days'),
    Industry VARCHAR(255),
    WhoDoYouSellTo TEXT,
    KnownCompetitors TEXT,
    MainOffering TEXT,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Users
CREATE TABLE IF NOT EXISTS Users (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id),
    FirebaseUid VARCHAR(128) UNIQUE NOT NULL,
    Email VARCHAR(255) UNIQUE NOT NULL,
    DisplayName VARCHAR(255),
    Role VARCHAR(50) DEFAULT 'Viewer',
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Team invites: matched purely by email (no invite-token URL flow yet) — sp_CreateOrGetUser
-- joins a genuinely new signup to Invites.OrganizationId/Role instead of creating a new org
-- whenever their email matches a pending, unexpired invite.
CREATE TABLE IF NOT EXISTS Invites (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    Email VARCHAR(255) NOT NULL,
    Role VARCHAR(50) NOT NULL DEFAULT 'Viewer',
    Token VARCHAR(64) NOT NULL UNIQUE,
    InvitedByUserId UUID REFERENCES Users(Id) ON DELETE SET NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ExpiresAt TIMESTAMP WITH TIME ZONE NOT NULL,
    AcceptedAt TIMESTAMP WITH TIME ZONE
);

-- Websites
CREATE TABLE IF NOT EXISTS Websites (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) NOT NULL,
    DomainUrl VARCHAR(255) NOT NULL,
    PlatformName VARCHAR(100) NOT NULL DEFAULT 'Custom',
    HealthScore INT NOT NULL DEFAULT 0,
    VisibilityScore INT NOT NULL DEFAULT 0,
    Status VARCHAR(50) NOT NULL DEFAULT 'Connected',
    LastSyncAt TIMESTAMP WITH TIME ZONE,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Knowledge Bases
CREATE TABLE IF NOT EXISTS KnowledgeBases (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    Name VARCHAR(255) NOT NULL,
    Icon VARCHAR(100) DEFAULT 'Building2',
    Tint VARCHAR(50) DEFAULT '#6366F1',
    Bg VARCHAR(50) DEFAULT '#EEEEFE',
    Description TEXT,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Source Folders (user-named groupings of scraped/crawled sources within a knowledge base)
CREATE TABLE IF NOT EXISTS SourceFolders (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    KnowledgeBaseId UUID REFERENCES KnowledgeBases(Id) ON DELETE CASCADE,
    Name VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Content Drafts (Content Studio: AI-generated blog posts, social posts, landing copy, etc.)
CREATE TABLE IF NOT EXISTS ContentDrafts (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    Title VARCHAR(512) NOT NULL DEFAULT '',
    ContentType VARCHAR(100) NOT NULL DEFAULT '',
    Content TEXT NOT NULL DEFAULT '',
    WordCount INT NOT NULL DEFAULT 0,
    Status VARCHAR(50) NOT NULL DEFAULT 'Draft', -- Draft, Optimized, Published
    RequestJson JSONB NOT NULL DEFAULT '{}'::jsonb,
    CompetitorUrl VARCHAR(2048),
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Content Optimizations (one row per optimization run against a ContentDraft)
CREATE TABLE IF NOT EXISTS ContentOptimizations (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
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

-- Crawled Pages
CREATE TABLE IF NOT EXISTS CrawledPages (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    WebsiteId UUID REFERENCES Websites(Id) ON DELETE CASCADE,
    Url VARCHAR(2048) NOT NULL,
    Content TEXT,
    Title VARCHAR(512),
    LastCrawledAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Recommendations (AI Generated)
CREATE TABLE IF NOT EXISTS Recommendations (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    WebsiteId UUID REFERENCES Websites(Id) ON DELETE CASCADE,
    CrawledPageId UUID REFERENCES CrawledPages(Id) ON DELETE CASCADE,
    Title VARCHAR(255) NOT NULL,
    Description TEXT,
    ActionType VARCHAR(100), -- e.g., 'Content Update', 'Meta Tag'
    Priority VARCHAR(50), -- 'High', 'Medium', 'Low'
    Status VARCHAR(50) DEFAULT 'Pending',
    DeployedUrl VARCHAR(2048),
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Stored Procedures (Functions in Postgres)

-- 1. Create or Get User based on Firebase Login
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
    -- Email is case-insensitive from here on (lock key, lookups, storage). A Google/GitHub-linked
    -- identity can report a differently-cased email than a password account registered for the
    -- exact same address, and Users.Email's UNIQUE constraint is case-sensitive — without this,
    -- signing in via a second provider for the same real address silently created a second
    -- Organization instead of matching the existing one.
    p_Email := LOWER(TRIM(p_Email));

    -- Serializes concurrent first-sync calls for the same account (e.g. React StrictMode
    -- double-invoke, double-click, multi-tab) so only one can create the Organization/User rows;
    -- the rest block here, then see the row already exists once they proceed. Locks on Email,
    -- not FirebaseUid, since Email carries the actual unique constraint this protects, including
    -- the email-fallback lookup below.
    PERFORM pg_advisory_xact_lock(hashtext(p_Email));

    -- Check if user exists
    SELECT u.Id, u.OrganizationId, u.Role INTO v_UserId, v_OrganizationId, v_Role
    FROM Users u WHERE u.FirebaseUid = p_FirebaseUid;

    -- Firebase can issue a different UID for an email that already has a Users row (account
    -- recreated, a different sign-in provider linked, manual Firebase console edits). Email is
    -- the durable identity — re-link FirebaseUid to the existing row instead of INSERTing a
    -- second one and hitting users_email_key. LOWER() on the stored side too, so this still
    -- matches legacy rows saved with mixed-case email before this normalization existed.
    IF v_UserId IS NULL THEN
        SELECT u.Id, u.OrganizationId, u.Role INTO v_UserId, v_OrganizationId, v_Role
        FROM Users u WHERE LOWER(u.Email) = p_Email;

        IF v_UserId IS NOT NULL THEN
            UPDATE Users SET FirebaseUid = p_FirebaseUid WHERE Id = v_UserId;
        END IF;
    END IF;

    -- Genuinely new user: check for a pending team invite before creating a brand new
    -- organization. Invites are matched purely by email — there's no invite-token URL flow yet,
    -- so anyone who registers/logs in with the invited address is automatically joined to that
    -- org at the invited role instead of getting their own.
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

    -- Still not found and no invite matched: create a new Organization and User
    IF v_UserId IS NULL THEN
        -- Create default organization for new user, starting on a 7-day trial
        INSERT INTO Organizations (Name, PlanType, TrialEndsAt)
        VALUES (p_DisplayName || '''s Org', 'Trial', CURRENT_TIMESTAMP + INTERVAL '7 days')
        RETURNING Id INTO v_OrganizationId;

        -- Create user as Admin
        INSERT INTO Users (OrganizationId, FirebaseUid, Email, DisplayName, Role)
        VALUES (v_OrganizationId, p_FirebaseUid, p_Email, p_DisplayName, 'Admin')
        RETURNING Id INTO v_UserId;

        v_Role := 'Admin';
    END IF;

    RETURN QUERY SELECT v_UserId, v_OrganizationId, v_Role;
END;
$$ LANGUAGE plpgsql;

-- 2. Bulk Insert Crawled Pages
CREATE OR REPLACE FUNCTION sp_InsertCrawledPage(
    p_WebsiteId UUID,
    p_Url VARCHAR,
    p_Title VARCHAR,
    p_Content TEXT
) RETURNS UUID AS $$
DECLARE
    v_PageId UUID;
BEGIN
    INSERT INTO CrawledPages (WebsiteId, Url, Title, Content)
    VALUES (p_WebsiteId, p_Url, p_Title, p_Content)
    RETURNING Id INTO v_PageId;
    
    RETURN v_PageId;
END;
$$ LANGUAGE plpgsql;

-- 3. Insert Recommendation
CREATE OR REPLACE FUNCTION sp_InsertRecommendation(
    p_WebsiteId UUID,
    p_CrawledPageId UUID,
    p_Title VARCHAR,
    p_Description TEXT,
    p_ActionType VARCHAR,
    p_Priority VARCHAR
) RETURNS UUID AS $$
DECLARE
    v_RecommendationId UUID;
BEGIN
    INSERT INTO Recommendations (WebsiteId, CrawledPageId, Title, Description, ActionType, Priority)
    VALUES (p_WebsiteId, p_CrawledPageId, p_Title, p_Description, p_ActionType, p_Priority)
    RETURNING Id INTO v_RecommendationId;
    
    RETURN v_RecommendationId;
END;
$$ LANGUAGE plpgsql;

-- Integrations (CMS)
CREATE TABLE IF NOT EXISTS Integrations (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    PlatformName VARCHAR(100) NOT NULL, -- e.g., 'WordPress', 'Shopify'
    ApiUrl VARCHAR(2048),
    ApiKey VARCHAR(1024), -- Plain text for MVP as agreed
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(OrganizationId, PlatformName)
);

-- API keys (server-generated workspace keys)
CREATE TABLE IF NOT EXISTS ApiKeys (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    Name VARCHAR(255) NOT NULL,
    KeyPrefix VARCHAR(32) NOT NULL,
    KeyHash VARCHAR(128) NOT NULL UNIQUE,
    Last4 VARCHAR(4) NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    RevokedAt TIMESTAMP WITH TIME ZONE
);
CREATE INDEX IF NOT EXISTS idx_apikeys_org_created ON ApiKeys (OrganizationId, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS Alerts (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
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
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    AlertType VARCHAR(100) NOT NULL,
    ThresholdValue INT NOT NULL DEFAULT 5,
    EmailEnabled BOOLEAN NOT NULL DEFAULT TRUE,
    WebhookEnabled BOOLEAN NOT NULL DEFAULT FALSE,
    WebhookUrl TEXT NOT NULL DEFAULT '',
    UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (OrganizationId, AlertType)
);

CREATE TABLE IF NOT EXISTS AuditLogs (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NULL REFERENCES Organizations(Id) ON DELETE SET NULL,
    ActorUserId UUID NULL REFERENCES Users(Id) ON DELETE SET NULL,
    ActorEmail VARCHAR(255) NOT NULL DEFAULT '',
    ActorType VARCHAR(50) NOT NULL DEFAULT 'User',
    Action VARCHAR(150) NOT NULL,
    Category VARCHAR(100) NOT NULL DEFAULT '',
    Outcome VARCHAR(50) NOT NULL DEFAULT 'Success',
    TargetType VARCHAR(100) NOT NULL DEFAULT '',
    TargetId VARCHAR(255) NOT NULL DEFAULT '',
    IpAddress VARCHAR(128) NOT NULL DEFAULT '',
    UserAgent TEXT NOT NULL DEFAULT '',
    MetadataJson JSONB NOT NULL DEFAULT '{}'::jsonb,
    CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_auditlogs_org_created ON AuditLogs (OrganizationId, CreatedAt DESC);
CREATE INDEX IF NOT EXISTS idx_auditlogs_action_created ON AuditLogs (Action, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS SsoConnections (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
    Provider VARCHAR(50) NOT NULL DEFAULT 'OIDC',
    Domain VARCHAR(255) NOT NULL DEFAULT '',
    MetadataUrl TEXT NOT NULL DEFAULT '',
    EntityId TEXT NOT NULL DEFAULT '',
    ScimEnabled BOOLEAN NOT NULL DEFAULT FALSE,
    ScimTokenHash VARCHAR(255) NOT NULL DEFAULT '',
    IsEnabled BOOLEAN NOT NULL DEFAULT FALSE,
    CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (OrganizationId, Provider, Domain)
);
CREATE INDEX IF NOT EXISTS idx_ssoconnections_scimtoken ON SsoConnections (ScimTokenHash) WHERE ScimTokenHash <> '';

CREATE TABLE IF NOT EXISTS RetentionPolicies (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
    RawPromptEvidenceDays INT NULL,
    AuditLogDays INT NOT NULL DEFAULT 365,
    SnapshotDays INT NOT NULL DEFAULT 1095,
    UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (OrganizationId)
);

CREATE TABLE IF NOT EXISTS DataDeletionRequests (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
    RequestedByUserId UUID NOT NULL REFERENCES Users(Id) ON DELETE RESTRICT,
    Status VARCHAR(50) NOT NULL DEFAULT 'Pending',
    Scope VARCHAR(50) NOT NULL DEFAULT 'Organization',
    Reason TEXT NOT NULL DEFAULT '',
    RequestedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ScheduledFor TIMESTAMP WITH TIME ZONE NOT NULL,
    CancelledAt TIMESTAMP WITH TIME ZONE NULL,
    CompletedAt TIMESTAMP WITH TIME ZONE NULL
);
CREATE INDEX IF NOT EXISTS idx_datadeletionrequests_org ON DataDeletionRequests (OrganizationId, RequestedAt DESC);
CREATE INDEX IF NOT EXISTS idx_datadeletionrequests_due ON DataDeletionRequests (Status, ScheduledFor);

CREATE TABLE IF NOT EXISTS Agencies (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OwnerOrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
    Name VARCHAR(255) NOT NULL DEFAULT '',
    CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (OwnerOrganizationId)
);

CREATE TABLE IF NOT EXISTS AgencyClients (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    AgencyId UUID NOT NULL REFERENCES Agencies(Id) ON DELETE CASCADE,
    ClientOrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
    ClientName VARCHAR(255) NOT NULL DEFAULT '',
    Role VARCHAR(50) NOT NULL DEFAULT 'Manager',
    CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (AgencyId, ClientOrganizationId)
);
CREATE INDEX IF NOT EXISTS idx_agencyclients_client ON AgencyClients (ClientOrganizationId);

CREATE TABLE IF NOT EXISTS WhiteLabelSettings (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    AgencyId UUID NOT NULL REFERENCES Agencies(Id) ON DELETE CASCADE,
    BrandName VARCHAR(255) NOT NULL DEFAULT '',
    LogoUrl TEXT NOT NULL DEFAULT '',
    PrimaryColor VARCHAR(32) NOT NULL DEFAULT '#4F46E5',
    UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (AgencyId)
);

CREATE TABLE IF NOT EXISTS ReportShareLinks (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
    AgencyId UUID NULL REFERENCES Agencies(Id) ON DELETE SET NULL,
    TokenHash VARCHAR(255) NOT NULL,
    ReportType VARCHAR(100) NOT NULL DEFAULT 'Executive',
    ExpiresAt TIMESTAMP WITH TIME ZONE NOT NULL,
    RevokedAt TIMESTAMP WITH TIME ZONE NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (TokenHash)
);
CREATE INDEX IF NOT EXISTS idx_reportsharelinks_org ON ReportShareLinks (OrganizationId, CreatedAt DESC);

-- Publishing: tracks the real deploy result of a ContentDraft against a connected CMS integration
ALTER TABLE ContentDrafts ADD COLUMN IF NOT EXISTS PublishedUrl VARCHAR(2048);
ALTER TABLE ContentDrafts ADD COLUMN IF NOT EXISTS PublishedAt TIMESTAMP WITH TIME ZONE;
ALTER TABLE ContentDrafts ADD COLUMN IF NOT EXISTS IntegrationId UUID REFERENCES Integrations(Id) ON DELETE SET NULL;
ALTER TABLE ContentDrafts ADD COLUMN IF NOT EXISTS PublishError TEXT;

-- 4. Insert or Update Integration
CREATE OR REPLACE FUNCTION sp_UpsertIntegration(
    p_OrganizationId UUID,
    p_PlatformName VARCHAR,
    p_ApiUrl VARCHAR,
    p_ApiKey VARCHAR
) RETURNS UUID AS $$
DECLARE
    v_IntegrationId UUID;
BEGIN
    INSERT INTO Integrations (OrganizationId, PlatformName, ApiUrl, ApiKey)
    VALUES (p_OrganizationId, p_PlatformName, p_ApiUrl, p_ApiKey)
    ON CONFLICT (OrganizationId, PlatformName) DO UPDATE
    SET ApiUrl = EXCLUDED.ApiUrl,
        ApiKey = EXCLUDED.ApiKey,
        UpdatedAt = CURRENT_TIMESTAMP
    RETURNING Id INTO v_IntegrationId;
    
    RETURN v_IntegrationId;
END;
$$ LANGUAGE plpgsql;

-- 5. Get Integrations by Organization
CREATE OR REPLACE FUNCTION sp_GetIntegrationsByOrg(
    p_OrganizationId UUID
) RETURNS TABLE (
    Id UUID,
    PlatformName VARCHAR,
    ApiUrl VARCHAR,
    CreatedAt TIMESTAMP WITH TIME ZONE,
    UpdatedAt TIMESTAMP WITH TIME ZONE
) AS $$
BEGIN
    RETURN QUERY 
    SELECT i.Id, i.PlatformName, i.ApiUrl, i.CreatedAt, i.UpdatedAt
    FROM Integrations i
    WHERE i.OrganizationId = p_OrganizationId;
END;
$$ LANGUAGE plpgsql;

-- Embeddings (Fallback approach without pgvector)
CREATE TABLE IF NOT EXISTS Embeddings (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    ReferenceId UUID NOT NULL, -- Could be a CrawledPageId or RecommendationId
    ReferenceType VARCHAR(50) NOT NULL, -- 'Page', 'Recommendation'
    TextContent TEXT NOT NULL,
    Vector FLOAT8[] NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 6. Insert Embedding
CREATE OR REPLACE FUNCTION sp_InsertEmbedding(
    p_OrganizationId UUID,
    p_ReferenceId UUID,
    p_ReferenceType VARCHAR,
    p_TextContent TEXT,
    p_Vector FLOAT8[]
) RETURNS UUID AS $$
DECLARE
    v_EmbeddingId UUID;
BEGIN
    INSERT INTO Embeddings (OrganizationId, ReferenceId, ReferenceType, TextContent, Vector)
    VALUES (p_OrganizationId, p_ReferenceId, p_ReferenceType, p_TextContent, p_Vector)
    RETURNING Id INTO v_EmbeddingId;
    
    RETURN v_EmbeddingId;
END;
$$ LANGUAGE plpgsql;

-- 7. Get All Embeddings by Organization
CREATE OR REPLACE FUNCTION sp_GetEmbeddingsByOrg(
    p_OrganizationId UUID
) RETURNS TABLE (
    Id UUID,
    ReferenceId UUID,
    ReferenceType VARCHAR,
    TextContent TEXT,
    Vector FLOAT8[]
) AS $$
BEGIN
    RETURN QUERY 
    SELECT e.Id, e.ReferenceId, e.ReferenceType, e.TextContent, e.Vector
    FROM Embeddings e
    WHERE e.OrganizationId = p_OrganizationId;
END;
$$ LANGUAGE plpgsql;

-- 8. Competitor Engine Tables

CREATE TABLE IF NOT EXISTS Competitors (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    Name VARCHAR(255) NOT NULL,
    WebsiteUrl VARCHAR(2048),
    Industry VARCHAR(255),
    Description TEXT,
    Category VARCHAR(255),
    Logo VARCHAR(2048),
    Country VARCHAR(100),
    Authority INT DEFAULT 0,
    Popularity INT DEFAULT 0,
    Rank INTEGER DEFAULT 0,
    SimilarityScore INTEGER DEFAULT 0,
    RawJson JSONB DEFAULT '{}'::jsonb,
    EnrichmentStatus VARCHAR(20) DEFAULT 'Pending',
    EnrichedJson JSONB,
    EnrichedAt TIMESTAMPTZ,
    CompetitorType VARCHAR(50) DEFAULT 'Direct',
    Confidence INTEGER DEFAULT 0,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Competitor Watch: real, AI-judged snapshots taken every scan (7-day recurring),
-- so rank/trend/share-of-voice-change are comparisons over time, not per-request randomness.
CREATE TABLE IF NOT EXISTS CompetitorSnapshots (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    CompetitorId UUID REFERENCES Competitors(Id) ON DELETE CASCADE,
    IsYou BOOLEAN NOT NULL DEFAULT false,
    ScanDate DATE NOT NULL,
    Name VARCHAR(255) NOT NULL,
    Score INT NOT NULL DEFAULT 0,
    Rank INT NOT NULL DEFAULT 0,
    ShareOfVoice INT NOT NULL DEFAULT 0,
    ShareOfVoiceChange INT NOT NULL DEFAULT 0,
    Visibility INT NOT NULL DEFAULT 0,
    VisibilityChange INT NOT NULL DEFAULT 0,
    Threat VARCHAR(10) NOT NULL DEFAULT 'low',
    ModelsJson JSONB NOT NULL DEFAULT '{}'::jsonb,
    Tagline VARCHAR(512),
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_competitorsnapshots_org_scandate ON CompetitorSnapshots (OrganizationId, ScanDate);

CREATE TABLE IF NOT EXISTS VisibilityScanSummaries (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NOT NULL,
    ScanDate DATE NOT NULL,
    CompositeScore INT NOT NULL DEFAULT 0,
    DirectPct INT NOT NULL DEFAULT 0,
    MentionsPct INT NOT NULL DEFAULT 0,
    IndirectPct INT NOT NULL DEFAULT 0,
    ComparativePct INT NOT NULL DEFAULT 0,
    CreatedAt TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_visibilityscansummaries_org_scandate ON VisibilityScanSummaries (OrganizationId, ScanDate);

CREATE TABLE IF NOT EXISTS VisibilityPlatformSnapshots (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NOT NULL,
    ScanDate DATE NOT NULL,
    Platform VARCHAR(255) NOT NULL,
    Score INT NOT NULL DEFAULT 0,
    Citations INT NOT NULL DEFAULT 0,
    Status VARCHAR(20) NOT NULL DEFAULT 'Developing',
    CreatedAt TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_visibilityplatformsnapshots_org_scandate ON VisibilityPlatformSnapshots (OrganizationId, ScanDate);

CREATE TABLE IF NOT EXISTS CitationScanSummaries (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NOT NULL,
    ScanDate DATE NOT NULL,
    CompositeQualityScore INT NOT NULL DEFAULT 0,
    AverageAuthorityScore INT NOT NULL DEFAULT 0,
    AverageInfluenceScore INT NOT NULL DEFAULT 0,
    CitationSignal INT NOT NULL DEFAULT 0,
    ModelsReferencingCount INT NOT NULL DEFAULT 0,
    ModelsTrackedCount INT NOT NULL DEFAULT 0,
    CreatedAt TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_citationscansummaries_org_scandate ON CitationScanSummaries (OrganizationId, ScanDate);

CREATE TABLE IF NOT EXISTS CitationSourceSnapshots (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NOT NULL,
    ScanDate DATE NOT NULL,
    Source VARCHAR(255) NOT NULL,
    Category VARCHAR(255),
    AuthorityScore INT NOT NULL DEFAULT 0,
    InfluenceScore INT NOT NULL DEFAULT 0,
    CitationFrequency INT NOT NULL DEFAULT 0,
    CompetitorCoverage INT NOT NULL DEFAULT 0,
    OpportunityScore INT NOT NULL DEFAULT 0,
    MentionProbability INT NOT NULL DEFAULT 0,
    Reason TEXT,
    CreatedAt TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_citationsourcesnapshots_org_scandate ON CitationSourceSnapshots (OrganizationId, ScanDate);

CREATE TABLE IF NOT EXISTS BrandPulseScanSummaries (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NOT NULL,
    ScanDate DATE NOT NULL,
    BrandHealth INT NOT NULL DEFAULT 0,
    AiConfidence INT NOT NULL DEFAULT 0,
    MessagingConsistency INT NOT NULL DEFAULT 0,
    BrandTrust INT NOT NULL DEFAULT 0,
    SentimentPositive INT NOT NULL DEFAULT 0,
    SentimentNeutral INT NOT NULL DEFAULT 0,
    SentimentNegative INT NOT NULL DEFAULT 0,
    AlertsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
    ModelInsightsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
    AccuracyFlagsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
    PromptEvidenceJson JSONB NOT NULL DEFAULT '[]'::jsonb,
    CreatedAt TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_brandpulsescansummaries_org_scandate ON BrandPulseScanSummaries (OrganizationId, ScanDate);

CREATE TABLE IF NOT EXISTS CommandCenterInsightSnapshots (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NOT NULL,
    ScanDate DATE NOT NULL,
    InsightsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
    CreatedAt TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_commandcenterinsightsnapshots_org_scandate ON CommandCenterInsightSnapshots (OrganizationId, ScanDate);

CREATE TABLE IF NOT EXISTS OpportunitySnapshots (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NOT NULL,
    ScanDate DATE NOT NULL,
    OpportunityKey VARCHAR(20) NOT NULL,
    Category VARCHAR(100) NOT NULL,
    Title VARCHAR(255) NOT NULL,
    Summary TEXT,
    WhyItMatters TEXT,
    Score INT NOT NULL DEFAULT 0,
    Effort INT NOT NULL DEFAULT 0,
    EstimatedGainPct DOUBLE PRECISION NOT NULL DEFAULT 0,
    Eta VARCHAR(100),
    CompetitorContext TEXT,
    ChecklistJson JSONB NOT NULL DEFAULT '[]'::jsonb,
    CreatedAt TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_opportunitysnapshots_org_scandate ON OpportunitySnapshots (OrganizationId, ScanDate);

CREATE TABLE IF NOT EXISTS VisibilitySummaries (
    Id UUID PRIMARY KEY,
    OrganizationId UUID NOT NULL,
    OverallVisibilityScore INTEGER NOT NULL,
    BestPlatform VARCHAR(255),
    WeakestPlatform VARCHAR(255),
    AverageMentionRate INTEGER NOT NULL,
    AveragePromptCoverage INTEGER NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_visibilitysummaries_org_created ON VisibilitySummaries (OrganizationId, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS PlatformVisibilities (
    Id UUID PRIMARY KEY,
    OrganizationId UUID NOT NULL,
    Platform VARCHAR(255) NOT NULL,
    VisibilityScore INTEGER NOT NULL,
    AverageRank VARCHAR(50),
    MentionRate INTEGER NOT NULL,
    PromptCoverage INTEGER NOT NULL,
    Confidence INTEGER NOT NULL,
    StrengthsJson TEXT,
    WeaknessesJson TEXT,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    Explanation TEXT,
    IsEnriched BOOLEAN DEFAULT FALSE
);
CREATE INDEX IF NOT EXISTS idx_platformvisibilities_org_created ON PlatformVisibilities (OrganizationId, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS CitationSummaries (
    Id UUID PRIMARY KEY,
    OrganizationId UUID NOT NULL,
    TotalSources INTEGER NOT NULL,
    AverageAuthorityScore INTEGER NOT NULL,
    AverageInfluenceScore INTEGER NOT NULL,
    HighestOpportunitySource VARCHAR(255),
    MostInfluentialSource VARCHAR(255),
    CreatedAt TIMESTAMP NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_citationsummaries_org_created ON CitationSummaries (OrganizationId, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS CitationSources (
    Id UUID PRIMARY KEY,
    OrganizationId UUID NOT NULL,
    Rank INTEGER NOT NULL,
    Source VARCHAR(255) NOT NULL,
    Category VARCHAR(255),
    AuthorityScore INTEGER NOT NULL,
    InfluenceScore INTEGER NOT NULL,
    CitationFrequency INTEGER NOT NULL,
    CompetitorCoverage INTEGER NOT NULL,
    OpportunityScore INTEGER NOT NULL,
    MentionProbability INTEGER NOT NULL,
    Reason TEXT,
    CreatedAt TIMESTAMP NOT NULL,
    IsEnriched BOOLEAN DEFAULT FALSE,
    EnrichedAt TIMESTAMP WITH TIME ZONE
);
CREATE INDEX IF NOT EXISTS idx_citationsources_org_rank ON CitationSources (OrganizationId, Rank);

CREATE TABLE IF NOT EXISTS PersonaAnalysisSummaries (
    Id UUID PRIMARY KEY,
    OrganizationId UUID NOT NULL,
    OverallVisibility INTEGER NOT NULL,
    StrongestPersona VARCHAR(255),
    WeakestPersona VARCHAR(255),
    AverageShareOfVoice INTEGER NOT NULL,
    CreatedAt TIMESTAMP NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_personaanalysissummaries_org_created ON PersonaAnalysisSummaries (OrganizationId, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS PersonaScores (
    Id UUID PRIMARY KEY,
    OrganizationId UUID NOT NULL,
    Persona VARCHAR(255) NOT NULL,
    Visibility INTEGER NOT NULL,
    AverageRank VARCHAR(50),
    ShareOfVoice INTEGER NOT NULL,
    TopCompetitorsJson TEXT,
    RecommendedContentJson TEXT,
    Reason TEXT,
    CreatedAt TIMESTAMP NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_personascores_org_created ON PersonaScores (OrganizationId, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS RegionAnalysisSummaries (
    Id UUID PRIMARY KEY,
    OrganizationId UUID NOT NULL,
    OverallGlobalVisibility INT NOT NULL,
    StrongestRegion TEXT,
    WeakestRegion TEXT,
    AverageShareOfVoice INT NOT NULL,
    CreatedAt TIMESTAMP NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_regionanalysissummaries_org_created ON RegionAnalysisSummaries (OrganizationId, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS RegionScores (
    Id UUID PRIMARY KEY,
    OrganizationId UUID NOT NULL,
    Region TEXT NOT NULL,
    Visibility INT NOT NULL,
    Ranking TEXT,
    CompetitorLeader TEXT,
    ShareOfVoice INT NOT NULL,
    ContentOpportunityJson TEXT,
    Reason TEXT,
    CreatedAt TIMESTAMP NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_regionscores_org_created ON RegionScores (OrganizationId, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS GeoRecommendationSummaries (
    Id UUID PRIMARY KEY,
    OrganizationId UUID NOT NULL,
    OverallPriority TEXT,
    EstimatedOverallImpact TEXT,
    EstimatedImplementationTime TEXT,
    TotalRecommendations INT NOT NULL,
    CriticalRecommendations INT NOT NULL,
    HighPriorityRecommendations INT NOT NULL,
    CreatedAt TIMESTAMP NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_georecommendationsummaries_org_created ON GeoRecommendationSummaries (OrganizationId, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS GeoRecommendations (
    Id UUID PRIMARY KEY,
    OrganizationId UUID NOT NULL,
    RecommendationId TEXT NOT NULL,
    Category TEXT NOT NULL,
    Title TEXT NOT NULL,
    Description TEXT NOT NULL,
    Priority TEXT NOT NULL,
    EstimatedImpact TEXT NOT NULL,
    EstimatedDifficulty TEXT NOT NULL,
    ImplementationTime TEXT NOT NULL,
    ExpectedOutcome TEXT NOT NULL,
    SuccessMetric TEXT NOT NULL,
    ActionItemsJson TEXT NOT NULL,
    IsEnriched BOOLEAN NOT NULL DEFAULT FALSE,
    EnrichedAt TIMESTAMP NULL,
    ExpandedGuidance TEXT NULL,
    BusinessImpact TEXT NULL,
    ExampleResourcesJson TEXT NULL,
    ReferenceLinksJson TEXT NULL,
    CreatedAt TIMESTAMP NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_georecommendations_org_created ON GeoRecommendations (OrganizationId, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS ExecutiveSummaryData (
    Id UUID PRIMARY KEY,
    OrganizationId UUID NOT NULL,
    BusinessOverview TEXT NOT NULL,
    CurrentAIVisibility TEXT NOT NULL,
    CompetitorPosition TEXT NOT NULL,
    PlatformPerformance TEXT NOT NULL,
    TopicPerformance TEXT NOT NULL,
    PromptPerformance TEXT NOT NULL,
    CitationSummary TEXT NOT NULL,
    StrengthsJson TEXT NOT NULL,
    WeaknessesJson TEXT NOT NULL,
    OpportunitiesJson TEXT NOT NULL,
    ThreatsJson TEXT NOT NULL,
    OverallGEOScore INT NOT NULL,
    OverallAIVisibilityScore INT NOT NULL,
    OverallSEOScore INT NOT NULL,
    OverallBrandAuthority INT NOT NULL,
    OverallContentScore INT NOT NULL,
    OverallAssessment TEXT NOT NULL,
    TopPriorityRecommendation TEXT NOT NULL,
    ExpectedBusinessImpact TEXT NOT NULL,
    NextStepsJson TEXT NOT NULL,
    CreatedAt TIMESTAMP NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_executivesummarydata_org_created ON ExecutiveSummaryData (OrganizationId, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS AiSearchPrompts (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    QueryString TEXT NOT NULL,
    SearchEngine VARCHAR(100) DEFAULT 'Google',
    Topic VARCHAR(255),
    Intent VARCHAR(100),
    Difficulty VARCHAR(50),
    Persona VARCHAR(255),
    CommercialValue INTEGER DEFAULT 0,
    RawJson JSONB DEFAULT '{}'::jsonb,
    Region VARCHAR(100),
    Language VARCHAR(50),
    TopicValidation VARCHAR(255),
    BuyerJourneyStage VARCHAR(100),
    IsEnriched BOOLEAN DEFAULT FALSE,
    EnrichedAt TIMESTAMP WITH TIME ZONE,
    EstimatedInterestLevel VARCHAR(50),
    VisibilityScore INTEGER DEFAULT 0,
    EstimatedRank VARCHAR(50),
    Confidence INTEGER DEFAULT 0,
    AppearsInAnswer BOOLEAN DEFAULT FALSE,
    ShareOfVoiceContribution INTEGER DEFAULT 0,
    MentionProbability INTEGER DEFAULT 0,
    BrandStrength INTEGER DEFAULT 0,
    ContentStrength INTEGER DEFAULT 0,
    CitationStrength INTEGER DEFAULT 0,
    VisibilityReason TEXT,
    GeneratedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS BrandMentions (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    AiSearchPromptId UUID REFERENCES AiSearchPrompts(Id) ON DELETE CASCADE,
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    BrandName VARCHAR(255) NOT NULL,
    Position INT NOT NULL,
    SentimentScore INT DEFAULT 0,
    MentionedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Answer Atlas / Prompt Intelligence — topic -> question -> analysis -> per-engine results.
CREATE TABLE IF NOT EXISTS PromptTopics (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    Name VARCHAR(255) NOT NULL,
    Description TEXT NOT NULL DEFAULT '',
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_users_org_created ON Users (OrganizationId, CreatedAt DESC);
CREATE INDEX IF NOT EXISTS idx_users_lower_email ON Users (LOWER(Email));

CREATE TABLE IF NOT EXISTS PromptSubtopics (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PromptTopicId UUID REFERENCES PromptTopics(Id) ON DELETE CASCADE,
    Name VARCHAR(255) NOT NULL,
    Description TEXT NOT NULL DEFAULT '',
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_promptsubtopics_topic_lower_name ON PromptSubtopics (PromptTopicId, LOWER(Name));

CREATE TABLE IF NOT EXISTS PromptIntents (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    Name VARCHAR(100) NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_promptintents_org_lower_name ON PromptIntents (OrganizationId, LOWER(Name));

CREATE TABLE IF NOT EXISTS PromptPersonas (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    Name VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_promptpersonas_org_lower_name ON PromptPersonas (OrganizationId, LOWER(Name));

CREATE TABLE IF NOT EXISTS PromptFunnelStages (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    Name VARCHAR(100) NOT NULL,
    SortOrder INT NOT NULL DEFAULT 0,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_promptfunnelstages_org_lower_name ON PromptFunnelStages (OrganizationId, LOWER(Name));

CREATE TABLE IF NOT EXISTS PromptClusters (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PromptSubtopicId UUID REFERENCES PromptSubtopics(Id) ON DELETE CASCADE,
    IntentId UUID REFERENCES PromptIntents(Id) ON DELETE SET NULL,
    PersonaId UUID REFERENCES PromptPersonas(Id) ON DELETE SET NULL,
    FunnelStageId UUID REFERENCES PromptFunnelStages(Id) ON DELETE SET NULL,
    Name VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_promptclusters_subtopic ON PromptClusters (PromptSubtopicId);

CREATE TABLE IF NOT EXISTS PromptQuestions (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PromptTopicId UUID REFERENCES PromptTopics(Id) ON DELETE CASCADE,
    PromptClusterId UUID REFERENCES PromptClusters(Id) ON DELETE SET NULL,
    IntentId UUID REFERENCES PromptIntents(Id) ON DELETE SET NULL,
    PersonaId UUID REFERENCES PromptPersonas(Id) ON DELETE SET NULL,
    FunnelStageId UUID REFERENCES PromptFunnelStages(Id) ON DELETE SET NULL,
    PromptText TEXT NOT NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    Region VARCHAR(100) NOT NULL DEFAULT 'Global',
    Persona VARCHAR(255),
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_promptquestions_cluster ON PromptQuestions (PromptClusterId);

CREATE TABLE IF NOT EXISTS PromptAnalysis (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PromptQuestionId UUID REFERENCES PromptQuestions(Id) ON DELETE CASCADE,
    RunAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    Status VARCHAR(50) NOT NULL DEFAULT 'Running',
    ErrorMessage TEXT
);

CREATE TABLE IF NOT EXISTS PromptResponses (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
    Platform VARCHAR(100) NOT NULL DEFAULT '',
    ResponseText TEXT NOT NULL DEFAULT '',
    ResponseLength INT NOT NULL DEFAULT 0,
    Sentiment VARCHAR(10),
    SentimentQuote TEXT,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ProviderKey VARCHAR(50),
    ModelUsed VARCHAR(100),
    PromptTokens INT,
    CompletionTokens INT,
    CostUsd NUMERIC(10,6),
    WasSearchGrounded BOOLEAN NOT NULL DEFAULT FALSE,
    PromptVersion VARCHAR(100) NOT NULL DEFAULT 'prompt-intelligence:v1',
    IsError BOOLEAN NOT NULL DEFAULT FALSE,
    ErrorMessage TEXT
);
CREATE INDEX IF NOT EXISTS idx_promptresponses_analysis_platform ON PromptResponses (PromptAnalysisId, Platform, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS PromptMentions (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
    Platform VARCHAR(100) NOT NULL DEFAULT '',
    EntityName VARCHAR(255) NOT NULL DEFAULT '',
    IsBrand BOOLEAN NOT NULL DEFAULT FALSE,
    ContextSnippet TEXT NOT NULL DEFAULT '',
    Position INT NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_promptmentions_analysis_platform_brand ON PromptMentions (PromptAnalysisId, Platform, IsBrand);
CREATE INDEX IF NOT EXISTS idx_promptmentions_entity_lower ON PromptMentions (LOWER(EntityName));

CREATE TABLE IF NOT EXISTS PromptVisibility (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
    OverallVisibilityScore INT NOT NULL DEFAULT 0,
    MentionFrequency INT NOT NULL DEFAULT 0,
    AveragePosition INT NOT NULL DEFAULT 0,
    ShareOfVoice INT NOT NULL DEFAULT 0,
    CitationCount INT NOT NULL DEFAULT 0,
    CompetitorCount INT NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS PromptRecommendations (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
    Category VARCHAR(100) NOT NULL DEFAULT '',
    Title VARCHAR(255) NOT NULL DEFAULT '',
    Description TEXT NOT NULL DEFAULT '',
    Priority VARCHAR(50) NOT NULL DEFAULT 'Medium',
    Difficulty VARCHAR(50) NOT NULL DEFAULT 'Medium',
    EstimatedVisibilityGain INT NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS RecommendationImplementations (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
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
CREATE INDEX IF NOT EXISTS idx_recommendationimplementations_due ON RecommendationImplementations (MeasurementDueAt, ImpactStatus);
CREATE INDEX IF NOT EXISTS idx_recommendationimplementations_org ON RecommendationImplementations (OrganizationId, MarkedImplementedAt DESC);

CREATE TABLE IF NOT EXISTS CompetitorComparisons (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
    CompetitorName VARCHAR(255) NOT NULL DEFAULT '',
    VisibilityScore INT NOT NULL DEFAULT 0,
    ShareOfVoice INT NOT NULL DEFAULT 0,
    MissingTopicsJson JSONB NOT NULL DEFAULT '[]'::jsonb
);

CREATE TABLE IF NOT EXISTS PromptCitations (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
    Platform VARCHAR(100) NOT NULL DEFAULT '',
    Domain VARCHAR(255) NOT NULL DEFAULT '',
    Url TEXT NOT NULL DEFAULT '',
    Category VARCHAR(50) NOT NULL DEFAULT 'Other',
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_promptcitations_analysis_platform_domain ON PromptCitations (PromptAnalysisId, Platform, Domain);

CREATE TABLE IF NOT EXISTS BrandClaims (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
    PromptResponseId UUID REFERENCES PromptResponses(Id) ON DELETE CASCADE,
    PromptQuestionId UUID REFERENCES PromptQuestions(Id) ON DELETE CASCADE,
    Platform VARCHAR(100) NOT NULL DEFAULT '',
    ClaimType VARCHAR(100) NOT NULL DEFAULT '',
    ClaimText TEXT NOT NULL DEFAULT '',
    EvidenceQuote TEXT NOT NULL DEFAULT '',
    ObservedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (PromptResponseId, ClaimType, ClaimText)
);
CREATE INDEX IF NOT EXISTS idx_brandclaims_org_observed ON BrandClaims (OrganizationId, ObservedAt DESC);

CREATE TABLE IF NOT EXISTS BrandFactChecks (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    BrandClaimId UUID REFERENCES BrandClaims(Id) ON DELETE CASCADE,
    VerificationStatus VARCHAR(50) NOT NULL DEFAULT 'Unverified',
    VerifiedFact TEXT NOT NULL DEFAULT '',
    Explanation TEXT NOT NULL DEFAULT '',
    CheckedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (BrandClaimId)
);
CREATE INDEX IF NOT EXISTS idx_brandfactchecks_org_checked ON BrandFactChecks (OrganizationId, CheckedAt DESC);

CREATE TABLE IF NOT EXISTS CrossEngineConsensusInsights (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    PromptAnalysisId UUID REFERENCES PromptAnalysis(Id) ON DELETE CASCADE,
    InsightType VARCHAR(100) NOT NULL DEFAULT '',
    Summary TEXT NOT NULL DEFAULT '',
    PlatformsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
    EvidenceJson JSONB NOT NULL DEFAULT '[]'::jsonb,
    CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (PromptAnalysisId, InsightType, Summary)
);
CREATE INDEX IF NOT EXISTS idx_consensusinsights_org_created ON CrossEngineConsensusInsights (OrganizationId, CreatedAt DESC);

CREATE TABLE IF NOT EXISTS PromptFanouts (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PromptQuestionId UUID REFERENCES PromptQuestions(Id) ON DELETE CASCADE,
    FanoutText TEXT NOT NULL DEFAULT '',
    Engine VARCHAR(100) NOT NULL DEFAULT '',
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS HistoricalScans (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    ScanDate DATE NOT NULL,
    VisibilityScore INT DEFAULT 0,
    CitationScore INT DEFAULT 0,
    SentimentScore INT DEFAULT 0,
    CompetitorScore INT DEFAULT 0,
    HallucinationRisk INT DEFAULT 0,
    SeoHealth INT DEFAULT 0,
    AeoReadiness INT DEFAULT 0,
    GeoReadiness INT DEFAULT 0,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    ScoringMethodVersion VARCHAR(20) NOT NULL DEFAULT 'v1-ai-generated',
    UNIQUE(OrganizationId, ScanDate)
);
CREATE INDEX IF NOT EXISTS idx_historicalscans_org_scandate_desc ON HistoricalScans (OrganizationId, ScanDate DESC);

CREATE TABLE IF NOT EXISTS ShareOfVoice (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    ScanDate DATE NOT NULL,
    CompetitorName VARCHAR(255) NOT NULL,
    SharePercentage INT DEFAULT 0,
    ColorCode VARCHAR(50) DEFAULT '#000000',
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(OrganizationId, ScanDate, CompetitorName)
);
CREATE INDEX IF NOT EXISTS idx_shareofvoice_org_scandate_desc ON ShareOfVoice (OrganizationId, ScanDate DESC);

-- Scraping Jobs
CREATE TABLE IF NOT EXISTS ScrapingJobs (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    WebsiteId UUID REFERENCES Websites(Id) ON DELETE SET NULL,
    KnowledgeBaseId UUID REFERENCES KnowledgeBases(Id) ON DELETE SET NULL,
    FolderId UUID REFERENCES SourceFolders(Id) ON DELETE SET NULL,
    Url VARCHAR(2048) NOT NULL,
    Status VARCHAR(50) DEFAULT 'Pending', -- Pending, Processing, Completed, Failed
    ScrapeType VARCHAR(50) DEFAULT 'Single', -- Single, Website
    TotalPages INT DEFAULT 0,
    ProcessedPages INT DEFAULT 0,
    MaxPages INT DEFAULT 100,
    SuccessfulPages INT DEFAULT 0,
    FailedPages INT DEFAULT 0,
    TotalWords INT DEFAULT 0,
    TotalImages INT DEFAULT 0,
    TotalLinks INT DEFAULT 0,
    StartedAt TIMESTAMP WITH TIME ZONE,
    CompletedAt TIMESTAMP WITH TIME ZONE,
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_scrapingjobs_org_created ON ScrapingJobs (OrganizationId, CreatedAt DESC);
CREATE INDEX IF NOT EXISTS idx_scrapingjobs_org_kb_created ON ScrapingJobs (OrganizationId, KnowledgeBaseId, CreatedAt DESC);

-- Scraped Pages (Results of ScrapingJobs)
CREATE TABLE IF NOT EXISTS ScrapedPages (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    JobId UUID REFERENCES ScrapingJobs(Id) ON DELETE CASCADE,
    Url VARCHAR(2048) NOT NULL,
    Title VARCHAR(512),
    Description TEXT,
    Content TEXT,
    HtmlContent TEXT,
    MarkdownContent TEXT,
    WordCount INT DEFAULT 0,
    ImageCount INT DEFAULT 0,
    LinkCount INT DEFAULT 0,
    Images JSONB DEFAULT '[]'::jsonb, -- Kept for UI backwards compatibility & speed
    InternalLinks JSONB DEFAULT '[]'::jsonb, -- Kept for UI backwards compatibility & speed
    ExternalLinks JSONB DEFAULT '[]'::jsonb, -- Kept for UI backwards compatibility & speed
    Headings JSONB DEFAULT '[]'::jsonb,
    ScrapedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Prevents the same URL from being stored twice under the same crawl/scrape job
CREATE UNIQUE INDEX IF NOT EXISTS idx_scrapedpages_job_url ON ScrapedPages (JobId, Url);

-- Normalized tables requested by the user
CREATE TABLE IF NOT EXISTS ExtractedImages (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PageId UUID REFERENCES ScrapedPages(Id) ON DELETE CASCADE,
    Url VARCHAR(2048) NOT NULL,
    AltText TEXT
);

CREATE TABLE IF NOT EXISTS ExtractedLinks (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    PageId UUID REFERENCES ScrapedPages(Id) ON DELETE CASCADE,
    Url VARCHAR(2048) NOT NULL,
    LinkType VARCHAR(50) -- Internal, External
);

CREATE TABLE IF NOT EXISTS WebsiteMetadata (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    WebsiteId UUID REFERENCES Websites(Id) ON DELETE CASCADE,
    JobId UUID REFERENCES ScrapingJobs(Id) ON DELETE SET NULL,
    Title VARCHAR(512),
    Description TEXT,
    OpenGraph JSONB DEFAULT '{}'::jsonb,
    TwitterCard JSONB DEFAULT '{}'::jsonb,
    SchemaData JSONB DEFAULT '{}'::jsonb,
    JsonLd JSONB DEFAULT '[]'::jsonb,
    CanonicalUrl VARCHAR(2048),
    Robots VARCHAR(255),
    Language VARCHAR(50),
    CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 9. Company Knowledge Graph — shared, deduplicated, cross-org company registry.
-- Distinct from WebsiteProfiles (org-scoped raw extraction, left untouched) — Company is the
-- canonical, deduplicated-by-domain record that any org's competitor discovery reads/writes.
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
CREATE TABLE IF NOT EXISTS UsageCounters (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
    MetricKey VARCHAR(100) NOT NULL,
    PeriodStart TIMESTAMP WITH TIME ZONE NOT NULL,
    PeriodEnd TIMESTAMP WITH TIME ZONE NOT NULL,
    Count BIGINT NOT NULL DEFAULT 0,
    UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (OrganizationId, MetricKey, PeriodStart)
);
CREATE INDEX IF NOT EXISTS idx_usagecounters_org_metric ON UsageCounters (OrganizationId, MetricKey, PeriodStart DESC);

CREATE TABLE IF NOT EXISTS AiRateLimitCounters (
    ScopeKey VARCHAR(255) NOT NULL,
    PeriodStart TIMESTAMP WITH TIME ZONE NOT NULL,
    PeriodEnd TIMESTAMP WITH TIME ZONE NOT NULL,
    Count BIGINT NOT NULL DEFAULT 0,
    UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (ScopeKey, PeriodStart)
);
CREATE INDEX IF NOT EXISTS idx_airatelimitcounters_periodend ON AiRateLimitCounters (PeriodEnd);

CREATE TABLE IF NOT EXISTS PlanLimits (
    PlanKey VARCHAR(100) NOT NULL,
    FeatureKey VARCHAR(100) NOT NULL,
    LimitValue BIGINT,
    PRIMARY KEY (PlanKey, FeatureKey)
);
INSERT INTO PlanLimits (PlanKey, FeatureKey, LimitValue) VALUES
    ('Trial', 'ai_calls_per_day', 50),
    ('Trial', 'ai_spend_micro_usd_per_day', 100000),
    ('Trial', 'recurring_scan_interval_days', 7),
    ('Trial', 'public_api_calls_per_day', 100),
    ('Trial', 'regions_summary', 0),
    ('Trial', 'personas_summary', 0),
    ('Pro', 'ai_calls_per_day', 1000),
    ('Pro', 'ai_spend_micro_usd_per_day', 5000000),
    ('Pro', 'recurring_scan_interval_days', 1),
    ('Pro', 'public_api_calls_per_day', 5000),
    ('Pro', 'regions_summary', 0),
    ('Pro', 'personas_summary', 0),
    ('Enterprise', 'ai_calls_per_day', NULL),
    ('Enterprise', 'ai_spend_micro_usd_per_day', NULL),
    ('Enterprise', 'recurring_scan_interval_days', 1),
    ('Enterprise', 'public_api_calls_per_day', NULL),
    ('Enterprise', 'regions_summary', 1),
    ('Enterprise', 'personas_summary', 1)
ON CONFLICT (PlanKey, FeatureKey) DO NOTHING;

CREATE UNIQUE INDEX IF NOT EXISTS idx_company_normalizeddomain ON Company (NormalizedDomain);
CREATE INDEX IF NOT EXISTS idx_company_industry ON Company (Industry);

-- Directed edge: "for CompanyId, CompetitorCompanyId is a ranked competitor". Rows are per
-- home-company, not per-org — two orgs analyzing the same domain reuse the same edges.
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
    DiscoverySource VARCHAR(20) NOT NULL DEFAULT 'graph',
    CONSTRAINT chk_companycompetitor_not_self CHECK (CompanyId <> CompetitorCompanyId)
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_companycompetitor_pair ON CompanyCompetitor (CompanyId, CompetitorCompanyId);
CREATE INDEX IF NOT EXISTS idx_companycompetitor_company_rank ON CompanyCompetitor (CompanyId, Rank);

-- Links each org's own website to its Company graph node.
ALTER TABLE Websites ADD COLUMN IF NOT EXISTS CompanyId UUID REFERENCES Company(Id) ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS idx_websites_companyid ON Websites (CompanyId);

