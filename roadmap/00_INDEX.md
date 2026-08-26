# Citationly Commercialization Roadmap - Index

Source: `CITATIONLY_PRODUCT_AUDIT.md` (repo root). This roadmap turns that audit's findings into an execution plan, split into 8 phases, one file per phase. Each phase file is self-contained: objective, task list (problem -> evidence -> solution -> affected files -> complexity -> priority), dependencies, and a Definition of Done.

**Do not skip ahead.** Phase order matters - Phase 1 (billing/tenancy) is close to pointless if Phase 0's IDOR hole is still open; Phase 3's scoring rework depends on Phase 2's provider/evidence layer existing first.

## Phase list

| Phase | File | Focus | Depends on | Status |
|---|---|---|---|---|
| 0 | [PHASE_0_FIX_FOUNDATIONS.md](PHASE_0_FIX_FOUNDATIONS.md) | Stop the bleeding: security holes, fabricated data presented as real, dead/duplicated code | - | **Complete.** See `.claude/ROADMAP_HANDOFF.md`. |
| 1 | [PHASE_1_COMMERCIAL_SAAS_FOUNDATION.md](PHASE_1_COMMERCIAL_SAAS_FOUNDATION.md) | Tenancy middleware, billing, usage metering, entitlements | Phase 0 | **Complete.** All A1/A2/B1/B2/C1/C2/C3 shipped. Billing works against local DB and returns an honest "not configured" status until real Stripe keys are supplied. |
| 2 | [PHASE_2_TRUSTWORTHY_AI_OBSERVATION.md](PHASE_2_TRUSTWORTHY_AI_OBSERVATION.md) | Real provider abstraction, evidence store, cost governance | Phase 0, 1 | **Complete.** Real `IAiProvider` abstraction replaces fabricated vendor simulation; evidence provenance, immutability trigger, resilience, daily call quota, and estimated spend quota are done. |
| 3 | [PHASE_3_AI_SEARCH_INTELLIGENCE.md](PHASE_3_AI_SEARCH_INTELLIGENCE.md) | Real scoring methodology, prompt graph, citation/competitor rigor | Phase 2 | **Complete.** A1/A2/B1/B2/B3/C1/C2/C3/C4/C5 done. |
| 4 | [PHASE_4_GEO_INTELLIGENCE.md](PHASE_4_GEO_INTELLIGENCE.md) | Structural site audits, evidence-linked recommendations | Phase 3 | **Complete.** Deterministic technical GEO audit, hybrid optimizer scoring, evidence-linked recommendations, and AI-crawler analytics architecture decision done. |
| 5 | [PHASE_5_DIFFERENTIATION.md](PHASE_5_DIFFERENTIATION.md) | Recommendation impact tracking, fact-accuracy monitor, consensus analysis | Phase 3, 4 | **In progress.** Backend capability layer and visible UI implemented; real end-to-end impact cycle remains. |
| 6 | [PHASE_6_PLATFORM.md](PHASE_6_PLATFORM.md) | Public API, MCP, agency/white-label, real alerting | Phase 3 | **Code-complete; local smoke passed.** Alerts/API/MCP/agency/white-label code shipped; SMTP email delivery and production agency-client validation remain. |
| 7 | [PHASE_7_ENTERPRISE_SCALE.md](PHASE_7_ENTERPRISE_SCALE.md) | SSO/SCIM, audit logs, scaled execution infra | Phase 1, 2 | **Partially complete.** Audit logs, org RBAC, SCIM scaffold/endpoints, request telemetry, data-lifecycle request workflow, frontend security UI, and ops runbooks shipped; real IdP/queue/backup/destructive deletion automation remains. |

## How to use these files

Each task in a phase file carries:

- **Problem** - one line, plain language.
- **Evidence** - exact file:line from the audit.
- **Solution** - the concrete change.
- **Affected** - modules/files touched.
- **Complexity** - S / M / L / XL (rough sizing, not a time estimate).
- **Priority** - P0-P4, matching the audit's priority tiers.

Treat each phase as its own build-and-handoff cycle. Update `.claude/ROADMAP_HANDOFF.md` and `graphify-out/ROADMAP_HANDOFF.md` after each phase.

## Progress log

- **2026-08-24** - Phase 0 executed by a prior codex session and preserved in `.claude/ROADMAP_HANDOFF.md` / `graphify-out/ROADMAP_HANDOFF.md`.
- **2026-08-25** - Phase 1 completed: tenancy middleware cleanup, billing scaffolding/UI wiring, usage metering, entitlements, and recurring job governance.
- **2026-08-25** - Phase 2 completed: real provider abstraction, OpenAI web-search grounding, evidence provenance/immutability, and AI spend governance.
- **2026-08-25** - Phase 3 completed: real scoring methodology, confidence, prompt taxonomy graph, citation taxonomy, citation winners/losers/gaps, graph-first competitors, and observed competitor ranking.
- **2026-08-25** - Phase 4 completed: deterministic robots/sitemap/schema/heading/SSR/freshness/entity/authority checks, disclosed `v3-geo-audit` scan composite, hybrid GEO optimizer evidence output, evidence-derived recommendations, and AI crawler traffic analytics scoped as a future log-ingestion architecture decision.
- **2026-08-25** - Phase 5 started: recommendation implementation/impact tracking, learned-impact feedback, brand claim/fact-check services, gated consensus service, frontend API clients, and visible UI panels/actions implemented. Remaining: real measured impact cycle.
- **2026-08-25** - Phase 6 code-complete: persisted Command Center alerts with anomaly detection and delivery job, API-key-authenticated/metered public API v1, MCP stdio wrapper, agency/client + white-label backend, Settings -> Agency UI, and token-scoped shared reports. Local schema/public API/MCP smoke tests passed with a short-lived revoked API key. Remaining: configure SMTP and validate real agency-client data.
- **2026-08-25** - Phase 7 partially completed: audit log schema/API/action filter, centralized org RBAC attributes, SSO/SCIM config scaffolding, SCIM v2 user endpoints, request telemetry middleware, retention/deletion request workflow, cheap classification routing, Security settings UI/API clients, and scale/backup/retention runbooks. Backend/frontend checks plus local SCIM/DataLifecycle smoke passed. Remaining: real SAML/OIDC IdP login/testing, real SCIM IdP testing, queue infrastructure, backup/PITR restore test, destructive deletion/export/legal-hold automation.
