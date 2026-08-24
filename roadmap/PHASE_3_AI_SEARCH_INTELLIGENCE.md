# Phase 3 — AI Search Intelligence

**Objective:** Replace "one LLM call invents 8 scores" with a real, disclosed, versioned scoring methodology; turn the flat prompt list into a real intelligence graph; make competitor discovery evidence-driven using data the product already collects but doesn't use.

**Depends on:** Phase 2 (real/honest provider layer and evidence store must exist — you cannot build a real scoring formula on top of a single fabricated LLM call).

---

## Workstream A — Real scoring methodology

### A1. Replace the single-call 8-score invention with disclosed, versioned formulas
- **Problem:** Visibility/Citation/Sentiment/Competitor/HallucinationRisk/SeoHealth/AeoReadiness/GeoReadiness are all invented in one LLM JSON call with no formula, explicitly told to resemble the previous invention.
- **Evidence:** `RunScanCommand.cs:196-212,257-320`, `AiVisibilityEngineService.cs:123-173`.
- **Solution:** For each score, define a real spec (see the audit's §7 template): disclosed inputs, disclosed formula/weights, a version number, and a confidence value derived from actual sample size/variance across Phase 2's real observations — not LLM self-report. Example starting point already partially real: `VisibilityCalculatorService.cs:95`'s `mentionFrequency*2 - averagePosition/2` — replace the arbitrary constants with weights derived from real backtesting or, at minimum, document why they were chosen. The per-platform weighting in `VisibilityScoringService.cs:33-44` (0.6/0.4, 0.7/0.3, 0.5/0.3/0.2) needs the same treatment — and only makes sense once Phase 2 confirms these are actually different providers, not the same model in disguise.
- **Affected:** `VisibilityCalculatorService.cs`, `VisibilityScoringService.cs`, `RunScanCommand.cs`, `AiVisibilityEngineService.cs`, `GeoDashboardAggregator.cs` (fix the unweighted-mean-with-inverted-polarity-metric issue at L112-122).
- **Complexity:** XL (this is the single largest engineering effort in the whole roadmap)
- **Priority:** P0

### A2. Real confidence methodology
- **Problem:** "Confidence" is either hardcoded (`90`) or defaulted to `0` — never computed from evidence.
- **Evidence:** `VisibilityScoringService.cs:69`, `CompetitorDiscoveryService.cs:172`.
- **Solution:** Confidence should be a function of (a) number of independent prompt executions backing the score, (b) agreement/variance across providers once multi-vendor exists, and (c) recency of the underlying observations. Document the formula in the same spec format as A1.
- **Affected:** Same files as A1.
- **Complexity:** M
- **Priority:** P1

---

## Workstream B — Prompt Intelligence Graph

### B1. Build the real taxonomy: Topic → Subtopic → Intent → Persona → Funnel Stage → Prompt Cluster → Prompt
- **Problem:** The current model is a flat two-level `PromptTopic → PromptQuestion` with loose string tags for Region/Persona — not a graph.
- **Evidence:** `PromptModels.cs` (PromptTopic, PromptQuestion definitions), audit §8.
- **Solution:** Add real entities/tables for Subtopic, Intent, FunnelStage, PromptCluster, with foreign keys instead of denormalized strings. Migrate existing `Region`/`Persona` string columns into proper lookup tables. Reconcile with the separate, richer `AiSearchPrompt` flat schema (Intent, Persona, Difficulty, BuyerJourneyStage) so there is one taxonomy, not two overlapping ones.
- **Affected:** New DB tables, `IPromptIntelligenceRepository.cs`, `PromptModels.cs`, migration from `AiSearchPrompt`.
- **Complexity:** L
- **Priority:** P1

### B2. Semantic deduplication for prompt generation
- **Problem:** No dedup exists after initial seeding; repeat "generate more prompts" calls can and will create near-duplicate prompts.
- **Evidence:** `TopicPromptGeneratorService.cs`, `PromptTopicSeedingService.cs:48-52` (exact-match dedup, seeding-only).
- **Solution:** Add an embedding-similarity check (reuse `OpenAiEmbeddingService.cs`, already in the codebase) before inserting a newly-generated prompt — reject or merge near-duplicates above a similarity threshold.
- **Affected:** `TopicPromptGeneratorService.cs`, `OpenAiEmbeddingService.cs`.
- **Complexity:** M
- **Priority:** P1

### B3. Replace fabricated "demand"/"volume" or relabel it honestly
- **Problem:** `MonthlySearchEstimate`/`CommercialValue` are LLM guesses with no real search-volume API behind them, but the field name implies real market data.
- **Evidence:** `PromptEnrichmentService.cs:38-46,72-76`.
- **Solution:** Either integrate a real keyword/search-volume API (DataForSEO, SEMrush, Ahrefs) if budget allows, or rename the field and its UI label to something honest ("AI-Estimated Interest") with a disclosure badge (Phase 0's B6 convention). Do not ship both the current name and the current methodology together.
- **Affected:** `PromptEnrichmentService.cs`, `PromptModels.cs`, frontend fields displaying this value.
- **Complexity:** M (relabel) / L (real API integration)
- **Priority:** P1

---

## Workstream C — Citation & competitor rigor

### C1. Expand citation source classification beyond 4 buckets
- **Problem:** Only Owned/Social/Institution/Other exist — competitor sites, editorial/media, review platforms, directories, marketplaces, docs, and industry publications all collapse into "Other."
- **Evidence:** `CitationExtractorService.cs:49-65`, `PromptModels.cs:96`.
- **Solution:** Expand `CategoryFor()` with a richer domain-classification ruleset (known review-platform/directory domain lists, competitor-domain cross-reference against the `Company`/`CompanyCompetitor` table from Phase 3's C3 below, editorial/media heuristics). This can stay rule-based — it doesn't need an LLM call.
- **Affected:** `CitationExtractorService.cs`.
- **Complexity:** M
- **Priority:** P2

### C2. Build real citation winners/losers/gaps reporting on real data
- **Problem:** The only real-data citation endpoint computes static share-of-total aggregates with no time-comparison; the "opportunity"/"gap" framing only exists on the separate fabricated LLM-invented pipeline.
- **Evidence:** `PromptIntelligenceController.cs:576-620` (`GetCitationsSummary`, no delta logic), `Delta()` helper at L396 (exists but unused for citations).
- **Solution:** Add a real time-comparison query (current period vs. prior period) over the real `PromptCitations` table, and a real "competitor cited here, customer isn't" query joining citations against the (by-then-reconciled) competitor model. Wire the existing `Delta()` helper into this.
- **Affected:** `PromptIntelligenceController.cs`, new repository queries.
- **Complexity:** M
- **Priority:** P2

### C3. Reconcile the two competitor data models
- **Problem:** `Competitors`/`CompetitorSnapshots` and `Company`/`CompanyCompetitor` coexist as unfinished-migration duplicates (flagged for decision in Phase 0's C2).
- **Evidence:** `init.sql`, `SelfHealingMigrations.cs:27-61`.
- **Solution:** Migrate fully onto the `Company`/`CompanyCompetitor` graph model (it's what real similarity-based discovery already uses), backfill/retire the older `Competitors` table, update every service still reading the old table.
- **Affected:** `CompetitorDiscoveryService.cs`, `CompetitorRankingService.cs`, `CompetitorGraphSyncService.cs`, DB migration.
- **Complexity:** L
- **Priority:** P1

### C4. Wire `PromptMentions` into competitor discovery (observed competitor emergence)
- **Problem:** Real observed brand co-occurrence data exists (`PromptMentions`, `GetCompetitorMentionSummaryDataAsync`) but nothing in competitor discovery/ranking references it — a brand that repeatedly co-occurs with the customer in real AI responses never surfaces as a discovered competitor unless it's already a high-similarity graph node or an LLM guess.
- **Evidence:** Audit §11 (Competitor Intelligence); `CompetitorDiscoveryService.cs` (confirmed no reference to `PromptMentions`/`EntityName`).
- **Solution:** Add a new discovery signal: organizations/companies that exceed a co-occurrence frequency threshold in `PromptMentions` across a customer's executed prompts get proposed as "observed competitors," ranked alongside (and flagged as higher-confidence than) the LLM-generation fallback path. This is the product's most build-ready differentiator per the audit — the data already exists.
- **Affected:** `CompetitorDiscoveryService.cs`, `PromptIntelligenceRepository.cs`.
- **Complexity:** M
- **Priority:** P1 (also tracked as a differentiator in Phase 5)

### C5. Fix illusory-rigor competitor ranking inputs
- **Problem:** `CompetitorRankingService.cs`'s formula is real and deterministic, but 7 of 10 input categories are flat hardcoded constants (50, or 40/30) applied identically to the customer and every competitor.
- **Evidence:** `CompetitorRankingService.cs:182-210,222`.
- **Solution:** Replace flat defaults with real measurements where Phase 3/4 now makes them available (e.g., Content score from Phase 4's GEO audit, Citation score from C2 above). Where a real signal genuinely doesn't exist yet, keep the honest-neutral-default pattern the code comment already describes — but disclose in the UI which ranking categories are currently measured vs. defaulted (Phase 0's B6 convention).
- **Affected:** `CompetitorRankingService.cs`.
- **Complexity:** M (grows as Phase 4 delivers more real signals to plug in)
- **Priority:** P2

---

## Definition of Done for Phase 3

- [ ] Every core dashboard score has a documented, versioned formula — no score is a single opaque LLM completion inventing multiple numbers at once.
- [ ] Confidence is computed from real sample size/variance, not hardcoded or defaulted.
- [ ] Prompt data model is a real graph (Topic→Subtopic→Intent→Persona→Funnel→Cluster→Prompt), not a flat two-level list.
- [ ] Prompt generation has semantic dedup.
- [ ] "Demand"/"volume" fields are either backed by a real API or honestly relabeled with a disclosure badge.
- [ ] Citation classification covers the full requested taxonomy, not just 4 buckets.
- [ ] Citation winners/losers/gaps are computed from real time-series data.
- [ ] The two competitor data models are reconciled into one.
- [ ] `PromptMentions` observed co-occurrence feeds competitor discovery.
