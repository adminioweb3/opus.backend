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
    -- second one and hitting users_email_key.
    IF v_UserId IS NULL THEN
        SELECT u.Id, u.OrganizationId, u.Role INTO v_UserId, v_OrganizationId, v_Role
        FROM Users u WHERE u.Email = p_Email;

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

CREATE TABLE IF NOT EXISTS AiSearchPrompts (
    Id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
    QueryString TEXT NOT NULL,
    SearchEngine VARCHAR(100) DEFAULT 'Google',
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
    UNIQUE(OrganizationId, ScanDate)
);

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

