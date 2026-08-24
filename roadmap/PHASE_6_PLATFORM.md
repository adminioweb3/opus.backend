# Phase 6 — Platform

**Objective:** Turn Citationly from a single-account dashboard into a platform other systems and other kinds of customers (agencies, developers) can build on — public API, MCP server, agency/white-label mode, and real alert delivery.

**Depends on:** Phase 3 (real, disclosed scoring — you should not expose an external API or an AI-agent-facing MCP tool over numbers that are still fabricated).

---

## Workstream A — Real alerting

### A1. Persist and deliver Command Center's regression alerts
- **Problem:** `CommandCenterAggregator.cs:131-171` computes genuinely real, on-the-fly regression deltas (visibility drop, competitor overtake, weak platform) — but they're request-time-only, never persisted, never deduplicated, and have no delivery mechanism (no email/webhook/push).
- **Evidence:** Audit §12; `CommandCenterAggregator.cs:131-171`.
- **Solution:** Persist computed alerts to a new `Alerts` table with deduplication (don't re-alert on the same regression every time the dashboard loads), add threshold configuration per organization, and add real delivery channels (email at minimum, Slack/webhook as a fast follow). Replace the frontend's fully-mocked `mock-data/alerts.ts` with this real backend.
- **Affected:** New `Alerts` table, `CommandCenterAggregator.cs`, new notification-delivery service, `frontend/src/lib/stores/notification-store.ts`.
- **Complexity:** L
- **Priority:** P1

### A2. Anomaly detection beyond simple current-vs-previous comparison
- **Problem:** Today's only alert logic is a simple `current < previous` threshold check — no real anomaly detection, confidence weighting, or noise reduction.
- **Evidence:** `CommandCenterAggregator.cs:133-143`.
- **Solution:** Layer basic statistical anomaly detection (e.g., z-score against a rolling window once enough Phase 3 real historical data exists) on top of the simple threshold, and require alerts to clear a confidence bar before firing — avoid the "noisy alerts" failure mode the product vision explicitly warns against.
- **Affected:** `CommandCenterAggregator.cs` or successor alerting engine.
- **Complexity:** M
- **Priority:** P2

---

## Workstream B — Public API & MCP

### B1. Public REST API
- **Problem:** No external-facing API exists today beyond the internal frontend-consumed endpoints.
- **Evidence:** Audit §27 (aspirational, not implemented).
- **Solution:** Expose a versioned, API-key-authenticated (server-generated keys per Phase 0's B9 fix) subset of read endpoints: visibility summary, competitors, citations, recommendations, alerts. Rate-limit and meter this the same way Phase 1's entitlement system meters internal AI usage — API access should itself be a plan-gated, metered feature.
- **Affected:** New `Citationly.API/PublicApi/` versioned controller set, API key management UI (already partially scaffolded in `ApiKeysSection.tsx`).
- **Complexity:** L
- **Priority:** P2

### B2. MCP server
- **Problem:** No MCP exposure exists; this is a real differentiator opportunity (letting a customer ask their own Claude/ChatGPT "why did our visibility drop this week?" grounded in real Citationly data).
- **Evidence:** Audit §27.
- **Solution:** Build an MCP server wrapping B1's API: `get_visibility`, `get_competitors`, `get_prompt_performance`, `get_citations`, `get_visibility_changes`, `get_recommendations`, `get_content_gaps`, `get_brand_facts` (from Phase 5's B1/B2), `get_alerts`. Every tool's output must carry the same provenance disclosure (Phase 0's B6 convention) so an external LLM consuming it doesn't present a fabricated-or-uncertain number as ground truth to the end user.
- **Affected:** New MCP server module, depends on B1's API existing first.
- **Complexity:** M
- **Priority:** P3

---

## Workstream C — Agency / white-label mode

### C1. Multi-brand-per-account data model
- **Problem:** `useOrganizationStore` holds a single `organizationId` with no organizations list or switcher — the product is single-brand-per-account only. "Agency" exists solely as marketing copy (`(public)/solutions/agencies`), not a real capability.
- **Evidence:** Audit §18; `organizationStore.ts`.
- **Solution:** Add an `Agency`/`Client` relationship layer above `Organization` (an agency account manages N client organizations), an org switcher in the frontend, and scoped permissions so an agency user's access to a client org doesn't bypass that client's own tenant isolation (careful cross-reference with Phase 1's tenant middleware — this needs to be additive, not a backdoor).
- **Affected:** New `Agencies`/`AgencyClients` tables, `organizationStore.ts`, tenant-resolution middleware updated to understand agency-scoped access.
- **Complexity:** L
- **Priority:** P3

### C2. White-label reporting
- **Problem:** No scheduled/shareable/branded report capability exists.
- **Evidence:** Audit §25 (aspirational).
- **Solution:** PDF/CSV export with custom branding (logo, color) per agency, scheduled email delivery, and a shareable read-only report link (with its own access-control scope, not the full dashboard).
- **Affected:** New reporting/export module, `ReportController.cs` (already has a base to build on, once its IDOR issue from Phase 0 is fixed).
- **Complexity:** L
- **Priority:** P3

---

## Definition of Done for Phase 6

- [ ] Alerts are persisted, deduplicated, and delivered via at least one real channel (email).
- [ ] A versioned, metered, API-key-gated public API exists for at least the core read endpoints.
- [ ] An MCP server exposes the same data with provenance disclosure intact.
- [ ] An agency account can manage multiple client organizations without breaking any client's tenant isolation.
- [ ] White-label branded report export/sharing exists.
