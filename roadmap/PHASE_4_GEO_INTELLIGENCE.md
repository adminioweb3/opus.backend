# Phase 4 — GEO Intelligence

**Objective:** Build the structural, deterministic technical audit of a customer's (and competitors') actual website that the product currently lacks entirely, and connect it to evidence-linked recommendations instead of category-level templates.

**Depends on:** Phase 3 (needs the real citation/competitor data this phase will cross-reference; the GEO Readiness Score rework here follows the same disclosed-methodology pattern established in Phase 3).

---

## Workstream A — Structural content/technical audit

### A1. Real robots.txt / sitemap / schema.org / crawlability checks
- **Problem:** No backend code fetches or parses a customer's robots.txt, sitemap.xml, canonical tags, or structured data — the only such files in the repo are Citationly's own marketing pages, unrelated to customer analysis. "GEO Readiness Score" is currently 100% LLM-hallucinated from a text summary, never derived from any real technical check.
- **Evidence:** Audit §11 (Content Intelligence); `GeoPillar.cs:3-12`, `RunScanCommand.cs:204-212` (LLM invents `geoReadiness` and 6 pillar scores from text summary alone).
- **Solution:** Build a deterministic checker (can reuse `PlaywrightScraperEngine` infrastructure from the crawler) that fetches and parses: `robots.txt` (AI-bot allow/deny — GPTBot, ClaudeBot, Google-Extended, PerplexityBot), `sitemap.xml` presence/validity, canonical tag presence, `<script type="application/ld+json">` schema.org presence and type, heading structure (H1/H2 hierarchy), FAQ schema presence, meta description/title presence, and SSR vs. client-only rendering (does the initial HTML response contain real content or an empty shell). Score each check independently and deterministically — no LLM judgment call for anything checkable by parsing.
- **Affected:** New `Citationly.Infrastructure/Services/GeoAudit/` module, reuses `PlaywrightScraperEngine.cs`.
- **Complexity:** L
- **Priority:** P1

### A2. Replace the LLM-invented GEO Readiness Score with the deterministic one
- **Problem:** `geoReadiness` and its 6 pillars are invented wholesale by a single LLM call with no real technical signal behind them.
- **Evidence:** `RunScanCommand.cs:204-212`.
- **Solution:** Once A1 exists, `GeoReadiness` becomes a real weighted composite of A1's deterministic checks, following the same versioned-spec pattern from Phase 3 A1. Reserve LLM judgment only for genuinely qualitative aspects (e.g., "answerability" of prose) — and flag those sub-scores as AI-INFERRED per Phase 0's disclosure convention, distinct from the deterministic sub-scores.
- **Affected:** `RunScanCommand.cs`, `GeoPillar.cs`, `GeoDashboardAggregator.cs`.
- **Complexity:** M
- **Priority:** P1

### A3. Upgrade `GeoOptimizerService` from LLM-judgment-only to hybrid
- **Problem:** `GeoOptimizerService.cs` already does real page-fetch + LLM analysis, but its "score" is pure subjective LLM opinion with no deterministic technical checks backing it.
- **Evidence:** Audit §12; `GeoOptimizerService.cs:21-96`.
- **Solution:** Layer A1's deterministic checks into this service's output alongside the existing LLM qualitative analysis (content depth, answerability) — same hybrid pattern as A2.
- **Affected:** `GeoOptimizerService.cs`.
- **Complexity:** M
- **Priority:** P2

---

## Workstream B — Evidence-linked recommendations

### B1. Connect prompt-gap → competitor page → content characteristics → recommendation
- **Problem:** This chain does not exist end-to-end. `AnalyzeCompetitorCommand` (ad hoc, one-URL-at-a-time) is disconnected from `GapDetectionService`/`RoadmapService`, which only reach "prompt gap → generic category recommendation."
- **Evidence:** Audit §12, §13; `GapDetectionService.cs:8-54`, `RecommendationDiscoveryService.cs:27-59`, `AnalyzeCompetitorCommand.cs:34-55`.
- **Solution:** When Phase 3's citation-gap detection (C2) identifies a specific prompt where a specific competitor is cited and the customer isn't, automatically trigger `AnalyzeCompetitorCommand`'s logic against that competitor's actual cited page (using A1's structural checks + existing LLM judgment), and feed the result — specific page, specific missing characteristics — into the recommendation, replacing the current generic category text.
- **Affected:** `GapDetectionService.cs`, `RecommendationDiscoveryService.cs`, `AnalyzeCompetitorCommand.cs`, new orchestration linking them.
- **Complexity:** L
- **Priority:** P1

### B2. Replace hardcoded per-category roadmap fields with evidence-derived ones
- **Problem:** `RoadmapService.cs:75-124` assigns `EstimatedImpact`/`ImplementationTime`/`SuccessMetric` identically for every recommendation in a category, regardless of actual content.
- **Evidence:** `RoadmapService.cs:75-124`.
- **Solution:** Once B1 provides specific evidence (which prompts are affected, which competitor page is winning, what the content gap actually is), derive impact/effort estimates from that evidence — e.g., impact scaled by how many real prompt executions the gap affects, per Phase 3's real prompt-execution volume data — rather than a category-keyed lookup table.
- **Affected:** `RoadmapService.cs`.
- **Complexity:** M
- **Priority:** P2

### B3. Retire the fully-hardcoded `RecommendationEngineService`
- **Problem:** A second, older recommendation generator is 100% templated — four fixed titles/descriptions/gain-values selected by simple thresholds, no LLM, no evidence.
- **Evidence:** `RecommendationEngineService.cs:14-83`.
- **Solution:** Once B1/B2 deliver a real evidence-based recommendation path, retire this service and its four hardcoded templates rather than maintaining two parallel recommendation systems of different quality that a user can't tell apart.
- **Affected:** `RecommendationEngineService.cs` (delete after migration), whatever endpoint currently calls it.
- **Complexity:** S
- **Priority:** P2

---

## Workstream C — AI crawler analytics (real implementation)

### C1. Real AI-bot traffic detection
- **Problem:** The `AI_CRAWLERS` toggle in Settings is a static list with local-only React state and zero backend — flagged in Phase 0 for honest labeling; this phase is where the real implementation belongs.
- **Evidence:** `WebsitesSection.tsx:15,23-25`.
- **Solution:** Design and implement real ingestion of server/CDN logs (or a lightweight reverse-proxy/middleware tap if the customer's site is self-hosted, or a Cloudflare integration if they're on Cloudflare) to detect actual GPTBot/ClaudeBot/PerplexityBot/Google-Extended requests: pages accessed, frequency, status codes, blocked requests. This is a genuinely new subsystem, not a fix to existing code — architect it before committing to an implementation approach, since it depends heavily on what access the customer's infrastructure can grant (log export API, webhook, or a bundled JS/edge snippet).
- **Affected:** New module; frontend `WebsitesSection.tsx` becomes a real settings surface once wired.
- **Complexity:** XL
- **Priority:** P3 (deferred — this is substantial new infrastructure, not a fix; sequence it after the higher-leverage GEO audit work above unless a customer specifically demands it sooner)

---

## Definition of Done for Phase 4

- [ ] robots.txt / sitemap / schema.org / heading / FAQ / SSR checks run deterministically against real customer (and competitor) pages.
- [ ] GEO Readiness Score is a disclosed composite of real checks, with LLM-judged sub-scores clearly flagged as such.
- [ ] At least one class of recommendation cites a specific competitor page and specific missing content characteristics, not just a category template.
- [ ] The fully-hardcoded `RecommendationEngineService` is retired.
- [ ] AI crawler analytics has a scoped architecture decision recorded, even if full implementation is deferred.
