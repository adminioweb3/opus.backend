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
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS WhoDoYouSellTo TEXT;
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS KnownCompetitors TEXT;
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS MainOffering TEXT;
        ALTER TABLE Recommendations ADD COLUMN IF NOT EXISTS DeployedUrl VARCHAR(2048);
        UPDATE Organizations SET TrialEndsAt = CreatedAt + INTERVAL '7 days' WHERE TrialEndsAt IS NULL;

        -- init.sql's CREATE TABLE HistoricalScans already includes these columns for a fresh DB,
        -- but this ALTER was missing here for existing databases, so DashboardController used to
        -- patch the live schema itself on request with a try/catch ALTER TABLE - that ad-hoc
        -- request-time DDL has been removed now that it belongs in this file instead.
        ALTER TABLE HistoricalScans ADD COLUMN IF NOT EXISTS HallucinationRisk INT DEFAULT 0;
        ALTER TABLE HistoricalScans ADD COLUMN IF NOT EXISTS SeoHealth INT DEFAULT 0;
        ALTER TABLE HistoricalScans ADD COLUMN IF NOT EXISTS AeoReadiness INT DEFAULT 0;
        ALTER TABLE HistoricalScans ADD COLUMN IF NOT EXISTS GeoReadiness INT DEFAULT 0;

        -- Phase 3 A1: distinguishes rows produced by the old single-LLM-invents-everything scoring
        -- pass (""v1-ai-generated"") from rows where Visibility/Citation/Sentiment/Competitor are
        -- now real deterministic computations (""v2-partial-real""). Existing rows default to v1
        -- since that's genuinely how they were produced - not a relabeling of old data as new.
        ALTER TABLE HistoricalScans ADD COLUMN IF NOT EXISTS ScoringMethodVersion VARCHAR(20) NOT NULL DEFAULT 'v1-ai-generated';
        CREATE INDEX IF NOT EXISTS idx_historicalscans_org_scandate_desc ON HistoricalScans (OrganizationId, ScanDate DESC);
        CREATE INDEX IF NOT EXISTS idx_shareofvoice_org_scandate_desc ON ShareOfVoice (OrganizationId, ScanDate DESC);
        CREATE INDEX IF NOT EXISTS idx_scrapingjobs_org_created ON ScrapingJobs (OrganizationId, CreatedAt DESC);
        CREATE INDEX IF NOT EXISTS idx_scrapingjobs_org_kb_created ON ScrapingJobs (OrganizationId, KnowledgeBaseId, CreatedAt DESC);
        CREATE INDEX IF NOT EXISTS idx_users_org_created ON Users (OrganizationId, CreatedAt DESC);
        CREATE INDEX IF NOT EXISTS idx_users_lower_email ON Users (LOWER(Email));

        -- Evidence provenance (Phase 2 B1) - which real provider/model produced a stored
        -- response, its cost, and whether it was grounded in a live web search. Backfills as
        -- NULL for pre-existing rows (their provider was never recorded), so a NULL here is
        -- itself meaningful: ""we don't know"", not ""OpenAI"" by default.
        ALTER TABLE PromptResponses ADD COLUMN IF NOT EXISTS ProviderKey VARCHAR(50);
        ALTER TABLE PromptResponses ADD COLUMN IF NOT EXISTS ModelUsed VARCHAR(100);
        ALTER TABLE PromptResponses ADD COLUMN IF NOT EXISTS PromptTokens INT;
        ALTER TABLE PromptResponses ADD COLUMN IF NOT EXISTS CompletionTokens INT;
        ALTER TABLE PromptResponses ADD COLUMN IF NOT EXISTS CostUsd NUMERIC(10,6);
        ALTER TABLE PromptResponses ADD COLUMN IF NOT EXISTS WasSearchGrounded BOOLEAN NOT NULL DEFAULT FALSE;
        ALTER TABLE PromptResponses ADD COLUMN IF NOT EXISTS PromptVersion VARCHAR(100) NOT NULL DEFAULT 'prompt-intelligence:v1';
        ALTER TABLE PromptResponses ADD COLUMN IF NOT EXISTS IsError BOOLEAN NOT NULL DEFAULT FALSE;
        ALTER TABLE PromptResponses ADD COLUMN IF NOT EXISTS ErrorMessage TEXT;

        -- AiSearchPrompts enrichment columns - previously added via a per-call ALTER TABLE inside
        -- WebsiteRepository.InsertAiSearchPromptsAsync/UpdateAiSearchPromptsAsync (the same
        -- request-time-DDL anti-pattern Phase 1 A2 removed from DashboardController). Consolidated
        -- here so it runs once at boot instead of on every write.
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

        -- Phase 3 B3: ""MonthlySearchEstimate"" implied a real search-volume metric, but this
        -- value has only ever been an LLM's qualitative guess - no search-volume API (SEMrush/
        -- Ahrefs/DataForSEO/etc.) is integrated anywhere in this codebase. Renamed to
        -- EstimatedInterestLevel so the column name itself doesn't misrepresent the data.
        -- Idempotent rename: only fires once, when the old column exists and the new one doesn't.
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

        ALTER TABLE Competitors ADD COLUMN IF NOT EXISTS Rank INTEGER DEFAULT 0;
        ALTER TABLE Competitors ADD COLUMN IF NOT EXISTS SimilarityScore INTEGER DEFAULT 0;
        ALTER TABLE Competitors ADD COLUMN IF NOT EXISTS RawJson JSONB DEFAULT '{}'::jsonb;
        ALTER TABLE Competitors ADD COLUMN IF NOT EXISTS EnrichmentStatus VARCHAR(20) DEFAULT 'Pending';
        ALTER TABLE Competitors ADD COLUMN IF NOT EXISTS EnrichedJson JSONB;
        ALTER TABLE Competitors ADD COLUMN IF NOT EXISTS EnrichedAt TIMESTAMPTZ;
        ALTER TABLE Competitors ADD COLUMN IF NOT EXISTS CompetitorType VARCHAR(50) DEFAULT 'Direct';
        ALTER TABLE Competitors ADD COLUMN IF NOT EXISTS Confidence INTEGER DEFAULT 0;

        -- Immutable evidence enforcement (Phase 2 B2): once a raw AI response is stored, nothing
        -- may rewrite what was actually observed. The one legitimate post-hoc write is sentiment
        -- classification (SentimentClassifierService, run after the fact against the immutable
        -- text) - everything else describing the original API call is frozen at insert time.
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
                OR NEW.PromptVersion IS DISTINCT FROM OLD.PromptVersion
                OR NEW.IsError IS DISTINCT FROM OLD.IsError
                OR NEW.ErrorMessage IS DISTINCT FROM OLD.ErrorMessage
            THEN
                RAISE EXCEPTION 'PromptResponses evidence fields are immutable once inserted - only Sentiment/SentimentQuote may be updated (see roadmap Phase 2 B2).';
            END IF;
            RETURN NEW;
        END;
        $body$ LANGUAGE plpgsql;

        DROP TRIGGER IF EXISTS trg_protect_promptresponses_evidence ON PromptResponses;
        CREATE TRIGGER trg_protect_promptresponses_evidence
            BEFORE UPDATE ON PromptResponses
            FOR EACH ROW EXECUTE FUNCTION trg_promptresponses_protect_evidence();

        -- Company Knowledge Graph: shared, deduplicated company directory for competitor discovery
        CREATE TABLE IF NOT EXISTS Company (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            NormalizedDomain VARCHAR(255) NOT NULL UNIQUE,
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

        -- Company competitor relationships: edges in the knowledge graph
        CREATE TABLE IF NOT EXISTS CompanyCompetitor (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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

        CREATE TABLE IF NOT EXISTS VisibilityScanSummaries (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID NOT NULL,
            ScanDate DATE NOT NULL,
            InsightsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
            CreatedAt TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS idx_commandcenterinsightsnapshots_org_scandate ON CommandCenterInsightSnapshots (OrganizationId, ScanDate);

        CREATE TABLE IF NOT EXISTS OpportunitySnapshots (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        ALTER TABLE PlatformVisibilities ADD COLUMN IF NOT EXISTS Explanation TEXT;
        ALTER TABLE PlatformVisibilities ADD COLUMN IF NOT EXISTS IsEnriched BOOLEAN DEFAULT FALSE;
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
            CreatedAt TIMESTAMP NOT NULL
        );
        ALTER TABLE CitationSources ADD COLUMN IF NOT EXISTS IsEnriched BOOLEAN DEFAULT FALSE;
        ALTER TABLE CitationSources ADD COLUMN IF NOT EXISTS EnrichedAt TIMESTAMP WITH TIME ZONE;
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
        ALTER TABLE GeoRecommendations ADD COLUMN IF NOT EXISTS IsEnriched BOOLEAN NOT NULL DEFAULT FALSE;
        ALTER TABLE GeoRecommendations ADD COLUMN IF NOT EXISTS EnrichedAt TIMESTAMP NULL;
        ALTER TABLE GeoRecommendations ADD COLUMN IF NOT EXISTS ExpandedGuidance TEXT NULL;
        ALTER TABLE GeoRecommendations ADD COLUMN IF NOT EXISTS BusinessImpact TEXT NULL;
        ALTER TABLE GeoRecommendations ADD COLUMN IF NOT EXISTS ExampleResourcesJson TEXT NULL;
        ALTER TABLE GeoRecommendations ADD COLUMN IF NOT EXISTS ReferenceLinksJson TEXT NULL;
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

        -- Phase 3 C4: distinguishes how each competitor edge was found - ""observed"" (the
        -- competitor actually co-occurs with the brand in real AI responses, per PromptMentions),
        -- ""graph"" (real cosine-similarity match in the Company Knowledge Graph), or ""generated""
        -- (LLM-suggested, unverified against any external source). Observed evidence is the
        -- strongest signal available and should rank above the other two.
        ALTER TABLE CompanyCompetitor ADD COLUMN IF NOT EXISTS DiscoverySource VARCHAR(20) NOT NULL DEFAULT 'graph';

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

        CREATE TABLE IF NOT EXISTS ApiKeys (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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

        CREATE TABLE IF NOT EXISTS AuditLogs (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
        ALTER TABLE SsoConnections ADD COLUMN IF NOT EXISTS ScimEnabled BOOLEAN NOT NULL DEFAULT FALSE;
        ALTER TABLE SsoConnections ADD COLUMN IF NOT EXISTS ScimTokenHash VARCHAR(255) NOT NULL DEFAULT '';
        CREATE INDEX IF NOT EXISTS idx_ssoconnections_scimtoken ON SsoConnections (ScimTokenHash) WHERE ScimTokenHash <> '';

        CREATE TABLE IF NOT EXISTS RetentionPolicies (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
            RawPromptEvidenceDays INT NULL,
            AuditLogDays INT NOT NULL DEFAULT 365,
            SnapshotDays INT NOT NULL DEFAULT 1095,
            UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE (OrganizationId)
        );

        CREATE TABLE IF NOT EXISTS DataDeletionRequests (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OwnerOrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
            Name VARCHAR(255) NOT NULL DEFAULT '',
            CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE (OwnerOrganizationId)
        );

        CREATE TABLE IF NOT EXISTS AgencyClients (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            AgencyId UUID NOT NULL REFERENCES Agencies(Id) ON DELETE CASCADE,
            ClientOrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
            ClientName VARCHAR(255) NOT NULL DEFAULT '',
            Role VARCHAR(50) NOT NULL DEFAULT 'Manager',
            CreatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE (AgencyId, ClientOrganizationId)
        );
        CREATE INDEX IF NOT EXISTS idx_agencyclients_client ON AgencyClients (ClientOrganizationId);

        CREATE TABLE IF NOT EXISTS WhiteLabelSettings (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            AgencyId UUID NOT NULL REFERENCES Agencies(Id) ON DELETE CASCADE,
            BrandName VARCHAR(255) NOT NULL DEFAULT '',
            LogoUrl TEXT NOT NULL DEFAULT '',
            PrimaryColor VARCHAR(32) NOT NULL DEFAULT '#4F46E5',
            UpdatedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE (AgencyId)
        );

        CREATE TABLE IF NOT EXISTS ReportShareLinks (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
        ALTER TABLE ReportShareLinks ADD COLUMN IF NOT EXISTS RevokedAt TIMESTAMP WITH TIME ZONE NULL;

        -- Billing (Phase 1) - Stripe-backed subscription/invoice/payment records.
        -- Organizations.PlanType remains a cached/derived mirror of Subscriptions.Status,
        -- kept in sync by the billing webhook handler once a real Stripe account is wired in.
        ALTER TABLE Organizations ADD COLUMN IF NOT EXISTS StripeCustomerId VARCHAR(255);
        CREATE INDEX IF NOT EXISTS idx_organizations_stripecustomerid ON Organizations (StripeCustomerId);

        CREATE TABLE IF NOT EXISTS Subscriptions (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
            StripeSubscriptionId VARCHAR(255) UNIQUE,
            CashfreeSubscriptionId VARCHAR(255) UNIQUE,
            PlanKey VARCHAR(100) NOT NULL,
            Status VARCHAR(50) NOT NULL DEFAULT 'trialing',
            CurrentPeriodStart TIMESTAMP WITH TIME ZONE,
            CurrentPeriodEnd TIMESTAMP WITH TIME ZONE,
            CancelAtPeriodEnd BOOLEAN NOT NULL DEFAULT FALSE,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UpdatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS idx_subscriptions_org ON Subscriptions (OrganizationId);

        CREATE TABLE IF NOT EXISTS Invoices (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
            StripeInvoiceId VARCHAR(255) UNIQUE,
            AmountDueCents BIGINT NOT NULL DEFAULT 0,
            AmountPaidCents BIGINT NOT NULL DEFAULT 0,
            Currency VARCHAR(10) NOT NULL DEFAULT 'usd',
            Status VARCHAR(50) NOT NULL DEFAULT 'draft',
            HostedInvoiceUrl VARCHAR(2048),
            IssuedAt TIMESTAMP WITH TIME ZONE,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS idx_invoices_org_issued ON Invoices (OrganizationId, IssuedAt DESC);

        CREATE TABLE IF NOT EXISTS PaymentMethods (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
            StripePaymentMethodId VARCHAR(255) UNIQUE,
            Brand VARCHAR(50),
            Last4 VARCHAR(4),
            ExpMonth INT,
            ExpYear INT,
            IsDefault BOOLEAN NOT NULL DEFAULT FALSE,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS idx_paymentmethods_org ON PaymentMethods (OrganizationId);

        -- Stripe delivery ledger: event IDs are globally unique, so a duplicate webhook cannot
        -- repeat subscription state transitions. Failed events retain their error and are claimable
        -- for a later Stripe retry.
        CREATE TABLE IF NOT EXISTS StripeWebhookEvents (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            StripeEventId VARCHAR(255) NOT NULL UNIQUE,
            PayloadHash VARCHAR(64) NOT NULL,
            EventType VARCHAR(255) NOT NULL,
            Status VARCHAR(32) NOT NULL,
            AttemptCount INT NOT NULL DEFAULT 1,
            ReceivedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
            LastAttemptAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CompletedAt TIMESTAMP WITH TIME ZONE,
            FailureReason VARCHAR(2000)
        );
        ALTER TABLE Subscriptions ADD COLUMN IF NOT EXISTS CashfreeSubscriptionId VARCHAR(255);
        CREATE UNIQUE INDEX IF NOT EXISTS idx_subscriptions_cashfreesubscriptionid
            ON Subscriptions (CashfreeSubscriptionId) WHERE CashfreeSubscriptionId IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_stripewebhookevents_status ON StripeWebhookEvents (Status, LastAttemptAt);

        CREATE TABLE IF NOT EXISTS CashfreeWebhookEvents (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            CashfreeEventId VARCHAR(255) NOT NULL UNIQUE,
            PayloadHash VARCHAR(64) NOT NULL,
            EventType VARCHAR(255) NOT NULL,
            Status VARCHAR(50) NOT NULL,
            AttemptCount INT NOT NULL DEFAULT 1,
            ReceivedAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
            LastAttemptAt TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CompletedAt TIMESTAMP WITH TIME ZONE,
            FailureReason VARCHAR(2000)
        );
        CREATE INDEX IF NOT EXISTS idx_cashfreewebhookevents_status ON CashfreeWebhookEvents (Status, LastAttemptAt);

        CREATE TABLE IF NOT EXISTS WebsiteProfiles (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID NOT NULL REFERENCES Organizations(Id) ON DELETE CASCADE,
            WebsiteUrl VARCHAR(2048) NOT NULL,
            BusinessName VARCHAR(255) NOT NULL,
            RawProfileJson JSONB NOT NULL DEFAULT '{}'::jsonb,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS idx_websiteprofiles_org_created ON WebsiteProfiles (OrganizationId, CreatedAt DESC);

        ALTER TABLE CompetitorSnapshots ADD COLUMN IF NOT EXISTS WebsiteUrl VARCHAR(2048);

        -- Usage metering (Phase 1) - per-org, per-metric, per-period counters so AI-cost
        -- endpoints and recurring jobs can be capped by plan instead of running unbounded.
        -- Persisted (unlike the in-process burst limiter in AiUsageLimiter) so a quota
        -- survives an app restart and is shared across all instances of the API.
        CREATE TABLE IF NOT EXISTS UsageCounters (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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

        -- Plan limits (Phase 1) - one row per plan/feature, read by IEntitlementService.
        -- A NULL LimitValue means unlimited. Seeded with today's actual plan set
        -- (Trial/Pro/Enterprise); adjust via a real admin UI once billing is live,
        -- not by hand-editing rows in production.
        CREATE TABLE IF NOT EXISTS PlanLimits (
            PlanKey VARCHAR(100) NOT NULL,
            FeatureKey VARCHAR(100) NOT NULL,
            LimitValue BIGINT,
            PRIMARY KEY (PlanKey, FeatureKey)
        );
        INSERT INTO PlanLimits (PlanKey, FeatureKey, LimitValue) VALUES
            ('Trial', 'ai_calls_per_day', NULL),
            ('Trial', 'ai_spend_micro_usd_per_day', NULL),
            ('Trial', 'recurring_scan_interval_days', 7),
            ('Trial', 'public_api_calls_per_day', 100),
            ('Trial', 'regions_summary', 0),
            ('Trial', 'personas_summary', 0),
            ('Pro', 'ai_calls_per_day', NULL),
            ('Pro', 'ai_spend_micro_usd_per_day', NULL),
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

        UPDATE PlanLimits
        SET LimitValue = NULL
        WHERE FeatureKey IN ('ai_calls_per_day', 'ai_spend_micro_usd_per_day');

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

        -- Phase 3 B1: real Prompt Intelligence taxonomy graph. Legacy PromptTopic -> PromptQuestion
        -- remains for compatibility, but questions now attach to graph nodes where available:
        -- Topic -> Subtopic -> Intent/Persona/FunnelStage -> Cluster -> Prompt.
        CREATE TABLE IF NOT EXISTS PromptSubtopics (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            PromptTopicId UUID REFERENCES PromptTopics(Id) ON DELETE CASCADE,
            Name VARCHAR(255) NOT NULL,
            Description TEXT NOT NULL DEFAULT '',
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_promptsubtopics_topic_lower_name ON PromptSubtopics (PromptTopicId, LOWER(Name));

        CREATE TABLE IF NOT EXISTS PromptIntents (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            Name VARCHAR(100) NOT NULL,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_promptintents_org_lower_name ON PromptIntents (OrganizationId, LOWER(Name));

        CREATE TABLE IF NOT EXISTS PromptPersonas (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            Name VARCHAR(255) NOT NULL,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_promptpersonas_org_lower_name ON PromptPersonas (OrganizationId, LOWER(Name));

        CREATE TABLE IF NOT EXISTS PromptFunnelStages (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            OrganizationId UUID REFERENCES Organizations(Id) ON DELETE CASCADE,
            Name VARCHAR(100) NOT NULL,
            SortOrder INT NOT NULL DEFAULT 0,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_promptfunnelstages_org_lower_name ON PromptFunnelStages (OrganizationId, LOWER(Name));

        CREATE TABLE IF NOT EXISTS PromptClusters (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            PromptSubtopicId UUID REFERENCES PromptSubtopics(Id) ON DELETE CASCADE,
            IntentId UUID REFERENCES PromptIntents(Id) ON DELETE SET NULL,
            PersonaId UUID REFERENCES PromptPersonas(Id) ON DELETE SET NULL,
            FunnelStageId UUID REFERENCES PromptFunnelStages(Id) ON DELETE SET NULL,
            Name VARCHAR(255) NOT NULL,
            CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS idx_promptclusters_subtopic ON PromptClusters (PromptSubtopicId);

        ALTER TABLE PromptQuestions ADD COLUMN IF NOT EXISTS PromptClusterId UUID REFERENCES PromptClusters(Id) ON DELETE SET NULL;
        ALTER TABLE PromptQuestions ADD COLUMN IF NOT EXISTS IntentId UUID REFERENCES PromptIntents(Id) ON DELETE SET NULL;
        ALTER TABLE PromptQuestions ADD COLUMN IF NOT EXISTS PersonaId UUID REFERENCES PromptPersonas(Id) ON DELETE SET NULL;
        ALTER TABLE PromptQuestions ADD COLUMN IF NOT EXISTS FunnelStageId UUID REFERENCES PromptFunnelStages(Id) ON DELETE SET NULL;
        CREATE INDEX IF NOT EXISTS idx_promptquestions_cluster ON PromptQuestions (PromptClusterId);

        INSERT INTO PromptSubtopics (PromptTopicId, Name, Description)
        SELECT t.Id, 'General', 'Default subtopic for prompts migrated from the legacy flat topic model.'
        FROM PromptTopics t
        WHERE NOT EXISTS (
            SELECT 1 FROM PromptSubtopics st
            WHERE st.PromptTopicId = t.Id AND LOWER(st.Name) = LOWER('General')
        );

        INSERT INTO PromptPersonas (OrganizationId, Name)
        SELECT DISTINCT t.OrganizationId, TRIM(q.Persona)
        FROM PromptQuestions q
        JOIN PromptTopics t ON t.Id = q.PromptTopicId
        WHERE q.Persona IS NOT NULL AND TRIM(q.Persona) <> ''
            AND NOT EXISTS (
                SELECT 1 FROM PromptPersonas p
                WHERE p.OrganizationId = t.OrganizationId AND LOWER(p.Name) = LOWER(TRIM(q.Persona))
            );

        INSERT INTO PromptPersonas (OrganizationId, Name)
        SELECT DISTINCT OrganizationId, TRIM(Persona)
        FROM AiSearchPrompts a
        WHERE a.Persona IS NOT NULL AND TRIM(a.Persona) <> ''
            AND NOT EXISTS (
                SELECT 1 FROM PromptPersonas p
                WHERE p.OrganizationId = a.OrganizationId AND LOWER(p.Name) = LOWER(TRIM(a.Persona))
            );

        INSERT INTO PromptIntents (OrganizationId, Name)
        SELECT DISTINCT OrganizationId, TRIM(Intent)
        FROM AiSearchPrompts a
        WHERE a.Intent IS NOT NULL AND TRIM(a.Intent) <> ''
            AND NOT EXISTS (
                SELECT 1 FROM PromptIntents i
                WHERE i.OrganizationId = a.OrganizationId AND LOWER(i.Name) = LOWER(TRIM(a.Intent))
            );

        INSERT INTO PromptFunnelStages (OrganizationId, Name, SortOrder)
        SELECT DISTINCT OrganizationId, TRIM(BuyerJourneyStage), 0
        FROM AiSearchPrompts a
        WHERE a.BuyerJourneyStage IS NOT NULL AND TRIM(a.BuyerJourneyStage) <> ''
            AND NOT EXISTS (
                SELECT 1 FROM PromptFunnelStages fs
                WHERE fs.OrganizationId = a.OrganizationId AND LOWER(fs.Name) = LOWER(TRIM(a.BuyerJourneyStage))
            );

        INSERT INTO PromptClusters (PromptSubtopicId, PersonaId, Name)
        SELECT DISTINCT st.Id, p.Id, COALESCE(p.Name, 'General Cluster')
        FROM PromptQuestions q
        JOIN PromptTopics t ON t.Id = q.PromptTopicId
        JOIN PromptSubtopics st ON st.PromptTopicId = t.Id AND LOWER(st.Name) = LOWER('General')
        LEFT JOIN PromptPersonas p ON p.OrganizationId = t.OrganizationId AND LOWER(p.Name) = LOWER(TRIM(q.Persona))
        WHERE NOT EXISTS (
            SELECT 1 FROM PromptClusters pc
            WHERE pc.PromptSubtopicId = st.Id
                AND pc.IntentId IS NULL
                AND pc.PersonaId IS NOT DISTINCT FROM p.Id
                AND pc.FunnelStageId IS NULL
                AND pc.Name = COALESCE(p.Name, 'General Cluster')
        );

        UPDATE PromptQuestions q
        SET PersonaId = p.Id,
            PromptClusterId = pc.Id
        FROM PromptTopics t
        JOIN PromptSubtopics st ON st.PromptTopicId = t.Id AND LOWER(st.Name) = LOWER('General')
        JOIN PromptPersonas p ON p.OrganizationId = t.OrganizationId
        JOIN PromptClusters pc ON pc.PromptSubtopicId = st.Id
            AND pc.IntentId IS NULL
            AND pc.PersonaId = p.Id
            AND pc.FunnelStageId IS NULL
            AND pc.Name = p.Name
        WHERE q.PromptTopicId = t.Id
            AND q.PromptClusterId IS NULL
            AND q.Persona IS NOT NULL
            AND TRIM(q.Persona) <> ''
            AND LOWER(p.Name) = LOWER(TRIM(q.Persona));

        UPDATE PromptQuestions q
        SET PromptClusterId = pc.Id
        FROM PromptTopics t
        JOIN PromptSubtopics st ON st.PromptTopicId = t.Id AND LOWER(st.Name) = LOWER('General')
        JOIN PromptClusters pc ON pc.PromptSubtopicId = st.Id
            AND pc.IntentId IS NULL
            AND pc.PersonaId IS NULL
            AND pc.FunnelStageId IS NULL
            AND pc.Name = 'General Cluster'
        WHERE q.PromptTopicId = t.Id
            AND q.PromptClusterId IS NULL
            AND (q.Persona IS NULL OR TRIM(q.Persona) = '');

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
        CREATE INDEX IF NOT EXISTS idx_promptresponses_analysis_platform ON PromptResponses (PromptAnalysisId, Platform, CreatedAt DESC);

        CREATE TABLE IF NOT EXISTS PromptMentions (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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

        CREATE TABLE IF NOT EXISTS RecommendationImplementations (
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
        CREATE INDEX IF NOT EXISTS idx_recommendationimplementations_due ON RecommendationImplementations (MeasurementDueAt, ImpactStatus);
        CREATE INDEX IF NOT EXISTS idx_recommendationimplementations_org ON RecommendationImplementations (OrganizationId, MarkedImplementedAt DESC);

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
        CREATE INDEX IF NOT EXISTS idx_promptcitations_analysis_platform_domain ON PromptCitations (PromptAnalysisId, Platform, Domain);

        CREATE TABLE IF NOT EXISTS BrandClaims (
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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
            Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
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

        -- Phase 3 C4: distinguishes how each competitor edge was found - ""observed"" (the
        -- competitor actually co-occurs with the brand in real AI responses, per PromptMentions),
        -- ""graph"" (real cosine-similarity match in the Company Knowledge Graph), or ""generated""
        -- (LLM-suggested, unverified against any external source). Observed evidence is the
        -- strongest signal available and should rank above the other two.
        ALTER TABLE CompanyCompetitor ADD COLUMN IF NOT EXISTS DiscoverySource VARCHAR(20) NOT NULL DEFAULT 'graph';

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
                UPDATE Users
                SET FirebaseUid = p_FirebaseUid
                WHERE Id = v_UserId
                  AND FirebaseUid IS DISTINCT FROM p_FirebaseUid
                  AND NOT EXISTS (
                      SELECT 1 FROM Users other
                      WHERE other.FirebaseUid = p_FirebaseUid AND other.Id <> v_UserId
                  );
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
                UPDATE Users
                SET FirebaseUid = p_FirebaseUid
                WHERE Id = v_UserId
                  AND FirebaseUid IS DISTINCT FROM p_FirebaseUid
                  AND NOT EXISTS (
                      SELECT 1 FROM Users other
                      WHERE other.FirebaseUid = p_FirebaseUid AND other.Id <> v_UserId
                  );
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
