# Phase 0 — Fix Foundations

**Objective:** Stop shipping fabricated numbers as real data, and close the two critical security holes. Nothing else in this roadmap matters commercially until this phase is done — you cannot sell a fixed billing system on top of numbers that are still invented, and you cannot onboard paying customers onto a backend where any of them can read another tenant's data.

**Depends on:** nothing — this is the starting point.

---

## Workstream A — Security (must go first)

### A1. Fix cross-tenant IDOR
- **Problem:** Any authenticated user can view or trigger paid AI rescans against another organization's data by supplying a different `organizationId`.
- **Evidence:** `DashboardController.cs` (all endpoints, e.g. L66, L91, L100, L117, L143, L160, L272, L318, L401, L527, L649, L664, L679), `ReportController.cs:21-24`, `CompetitorController.cs:31-51`.
- **Solution:** Introduce a single server-side tenant-resolution mechanism (ASP.NET middleware or an `[Authorize]`-adjacent filter) that derives `OrganizationId` from the JWT claim and injects it into the request pipeline. Delete every controller parameter/query-string `organizationId` that a client can set — the org ID must never travel client → server for these endpoints. Use `PromptIntelligenceController.cs`, `ContentController.cs`, `AnalysisController.cs`, `TeamController.cs` as the reference pattern — they already do this correctly.
- **Affected:** `Citationly.API/Middleware/` (new), `DashboardController.cs`, `ReportController.cs`, `CompetitorController.cs`, and an audit pass over every other controller to confirm no other instance of client-supplied tenant ID exists.
- **Complexity:** M
- **Priority:** P0

### A2. Rotate and properly secure the admin secret
- **Problem:** `Admin:ResetSecret` is committed to `docker-compose.yml` in git and inlined into the public admin JS bundle via `VITE_ADMIN_SECRET`.
- **Evidence:** `AdminController.cs:42-48`, `docker-compose.yml:24`, `admin/src/pages/Dashboard.jsx:24,43,67`.
- **Solution:** Rotate the secret immediately (it must be treated as compromised — it's in git history and was public in a shipped bundle). Move admin authentication off a static shared-secret header entirely: require a real authenticated session (server-verified login, not the current client-side `admin`/`pass@123` cosmetic check in `admin/src/pages/LoginPage.jsx:17-18,32-39`) plus a role claim checked server-side. Remove the secret from `docker-compose.yml` (use a secrets manager or untracked `.env`) and scrub it from git history if the repo will ever go public or be shared beyond the current team.
- **Affected:** `AdminController.cs`, `admin/src/pages/LoginPage.jsx`, `admin/src/pages/Dashboard.jsx`, `docker-compose.yml`, `admin/SETUP.md`.
- **Complexity:** M
- **Priority:** P0

### A3. Add per-tenant/global rate limiting around every AI call
- **Problem:** No resilience framework exists; any tenant can drive unlimited OpenAI spend today, and this gets worse with every phase that adds more AI calls.
- **Evidence:** No `Polly`/`RetryPolicy`/`CircuitBreaker` reference anywhere in `backend/`; manual one-shot retry only in `OpenAiService.cs:64-83`.
- **Solution:** Introduce Polly for retry/circuit-breaking around the OpenAI client, plus a basic in-memory or Redis-backed per-tenant call counter with a hard ceiling (even a crude "N calls/hour" cap is better than none). This is a stopgap — real quota enforcement belongs to Phase 1's entitlement system — but it must exist before Phase 0 closes so no other fix in this phase inadvertently increases exposure.
- **Affected:** `OpenAiService.cs`, `LLMRunnerService.cs`, new `Citationly.Infrastructure/Resilience/` folder.
- **Complexity:** S
- **Priority:** P0

---

## Workstream B — Stop presenting fabricated data as real

### B1. Remove `MockSearchService` from the production dependency graph
- **Problem:** Returns the same 3 hardcoded competitors and `Random()`-based sentiment/mentions for every organization; registered twice in DI and actually executes on a live recurring job.
- **Evidence:** `MockSearchService.cs:8-60`, `DependencyInjection.cs:64,112`, `RecurringScrapeService.cs:108-121` (hardcoded `industry = "Web3 Development"`).
- **Solution:** Remove both DI registrations. Remove the `RecurringScrapeService` invocation entirely until a real `ISearchService` implementation exists (Phase 2). Do not leave a "disabled" stub silently registered — delete the wiring so a future developer can't accidentally re-enable it without noticing.
- **Affected:** `DependencyInjection.cs`, `RecurringScrapeService.cs`, `MockSearchService.cs` (delete or clearly quarantine under a `Testing/` namespace never referenced by `DependencyInjection.cs`).
- **Complexity:** S
- **Priority:** P0

### B2. Remove silent `Random()` score fallbacks
- **Problem:** When the LLM scoring call fails to parse, the system silently returns `Random.Next(30,80)` etc. with no flag distinguishing it from a real score.
- **Evidence:** `AiVisibilityEngineService.cs:165-172`, invoked live from `CompleteOnboardingCommand.cs`.
- **Solution:** Replace the random fallback with an explicit "scan incomplete / retry" state surfaced to the user — never fabricate a number silently. Same treatment for `VisibilityScoringService.cs:16,50`'s "realism" variance injection — remove it; deterministic math should not have random noise added on top.
- **Affected:** `AiVisibilityEngineService.cs`, `VisibilityScoringService.cs`, whatever DTO/UI currently assumes a score is always present.
- **Complexity:** M
- **Priority:** P0

### B3. Fix the hardcoded `Confidence = 90` and always-zero `CitationCount`
- **Problem:** "Confidence" is a hardcoded literal commented as "deterministic formula"; `CitationCount` is declared, never incremented, and always reads `0`.
- **Evidence:** `VisibilityScoringService.cs:69`, `VisibilityCalculatorService.cs:31,108`.
- **Solution:** Either compute a real confidence value (defer full methodology to Phase 3, but at minimum stop calling a hardcoded `90` a "deterministic formula") or remove the field/relabel it "not yet calculated." Fix `CitationCount` to actually increment from `CitationExtractorService` output, or remove the field until it's wired.
- **Affected:** `VisibilityScoringService.cs`, `VisibilityCalculatorService.cs`.
- **Complexity:** S
- **Priority:** P1

### B4. Remove client-side fabricated trend charts
- **Problem:** Two dashboard components regenerate synthetic history with `Math.random()` jitter around the current score, ignoring real historical DB rows entirely.
- **Evidence:** `frontend/src/components/report/AIVisibilityOverview.tsx:9-13` (comment admits it), `frontend/src/app/(dashboard)/dashboard/geo/page.tsx:106-109`.
- **Solution:** Replace with either (a) a real query against `HistoricalScans`/`ShareOfVoice` rows if enough history exists, or (b) an honest empty/low-data state ("not enough scan history yet — check back after N days") when it doesn't. Never draw a fake line.
- **Affected:** `AIVisibilityOverview.tsx`, `geo/page.tsx`, the backend endpoints these components call (may need a new "trend" endpoint that returns real rows or an explicit "insufficient data" response).
- **Complexity:** M
- **Priority:** P0

### B5. Fix the 4 hardcoded scorecard tiles on the GEO dashboard
- **Problem:** Sentiment/Hallucination/SEO/AEO tiles are literal hardcoded numbers, indistinguishable in the UI from the 4 real tiles beside them.
- **Evidence:** `frontend/src/app/(dashboard)/dashboard/geo/page.tsx:98,100-102`.
- **Solution:** Wire these tiles to `reportData` like the other 4, or remove them until they're real. Do not ship a dashboard where half the tiles are decoration.
- **Affected:** `geo/page.tsx`.
- **Complexity:** S
- **Priority:** P0

### B6. Add a provenance disclosure convention across the UI
- **Problem:** No metric in the product distinguishes OBSERVED / DERIVED / ESTIMATED / AI-INFERRED to the end user — everything looks equally "measured."
- **Evidence:** Audit §4–§7 (Feature Reality Matrix, Fake Analytics, Data Provenance, Scoring Audit).
- **Solution:** Define a small shared UI badge/tooltip component ("AI-estimated," "Based on N observed responses," etc.) and require every score component to carry one. This doesn't require the underlying metric to be fixed yet (that's Phase 2/3) — it requires that nothing ships without disclosing what kind of number it is. This is the single highest-leverage trust fix available before the real scoring engine exists.
- **Affected:** New shared frontend component; every dashboard card/chart that renders a score.
- **Complexity:** M
- **Priority:** P0

### B7. Hide or clearly label the fully-mocked dashboard pages
- **Problem:** Brand, Billing, Team, Monitoring, and Integrations pages are sidebar-linked and render entirely from static frontend mock-data files with no backend connection or disclosure.
- **Evidence:** `frontend/src/lib/mock-data/{brand,billing,prompts,enterprise,deployments}.ts`; pages at `dashboard/brand/page.tsx`, `dashboard/billing/page.tsx`, `dashboard/team/page.tsx`, `dashboard/monitoring/page.tsx`, `dashboard/integrations/page.tsx`.
- **Solution:** Remove these from primary navigation until Phase 1 (billing) / Phase 6 (team/integrations) ship real backends, or gate them behind an explicit "Preview — coming soon" state that cannot be mistaken for live data. Do not leave them reachable and indistinguishable from real pages.
- **Affected:** Sidebar/nav config, the five page components listed above.
- **Complexity:** S
- **Priority:** P0

### B8. Fix the AI-crawler toggle placeholder
- **Problem:** `AI_CRAWLERS` toggle switches in Settings do nothing — local React state only, no backend, no disclosure.
- **Evidence:** `frontend/src/components/settings/WebsitesSection.tsx:15,23-25` (developer comment admits it).
- **Solution:** Add a "Coming soon" label directly on the control, or remove it until Phase 6's real crawler-log analytics exists. A silently-broken toggle is worse than an honestly-labeled missing feature.
- **Affected:** `WebsitesSection.tsx`.
- **Complexity:** S
- **Priority:** P1

### B9. Fix the client-side fake API key generator
- **Problem:** `Math.random()` used client-side to generate what looks like a real API key string.
- **Evidence:** `frontend/src/components/settings/ApiKeysSection.tsx:23`.
- **Solution:** API key generation must happen server-side with a cryptographically secure generator, never in the browser. Treat as a security fix, not just a data-honesty fix.
- **Affected:** `ApiKeysSection.tsx`, corresponding backend endpoint (add one if it doesn't exist).
- **Complexity:** S
- **Priority:** P0

---

## Workstream C — Remove dead/duplicated code

### C1. Remove dead crawler and package reference
- **Problem:** `WebScraperService.cs` is unused legacy code (single-page HtmlAgilityPack scraper, comment says "just scrape the homepage for the demo"); `Citationly.API.csproj` references `Microsoft.EntityFrameworkCore` with zero `DbContext` classes anywhere.
- **Evidence:** `WebScraperService.cs:16`, `Citationly.API.csproj`.
- **Solution:** Delete `WebScraperService.cs` and its DI registration if confirmed unused (re-verify via grep before deleting — the audit agent found no live caller but this should be double-checked at implementation time). Remove the EF Core package reference.
- **Affected:** `WebScraperService.cs`, `DependencyInjection.cs`, `Citationly.API.csproj`.
- **Complexity:** S
- **Priority:** P2

### C2. Flag the two parallel competitor data models for reconciliation
- **Problem:** `Competitors`/`CompetitorSnapshots` and `Company`/`CompanyCompetitor` ("Knowledge Graph") coexist as two separate, unreconciled competitor systems.
- **Evidence:** `init.sql`, `SelfHealingMigrations.cs:27-61`.
- **Solution:** This phase only needs to document the duplication and decide which model is canonical going forward (the `Company`/`CompanyCompetitor` graph model, since it's what real similarity-based discovery uses) — full reconciliation is a Phase 3 task once competitor discovery logic is being reworked anyway. Do not attempt a data migration in Phase 0.
- **Affected:** Decision document only in this phase; implementation deferred to Phase 3.
- **Complexity:** S (decision only)
- **Priority:** P2

---

## Definition of Done for Phase 0

- [ ] No controller accepts a client-supplied `organizationId`/tenant identifier for data access.
- [ ] Admin secret rotated, admin auth requires a real server-verified session, secret removed from tracked files.
- [ ] `MockSearchService` no longer registered in DI or invoked by any job.
- [ ] No code path silently returns `Random()`-generated data as if it were a real score.
- [ ] No frontend component draws a synthetic trend line from `Math.random()`.
- [ ] Every score-rendering UI component carries a provenance disclosure badge.
- [ ] Brand/Billing/Team/Monitoring/Integrations pages are either removed from nav or clearly marked as previews.
- [ ] API keys are generated server-side only.
- [ ] Basic Polly-based retry/circuit-breaker and a crude per-tenant call cap exist around every OpenAI call.

Only once every box above is checked should Phase 1 begin.
