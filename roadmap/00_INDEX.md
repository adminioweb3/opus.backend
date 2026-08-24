# Citationly Commercialization Roadmap — Index

Source: `CITATIONLY_PRODUCT_AUDIT.md` (repo root). This roadmap turns that audit's findings into an execution plan, split into 8 phases, one file per phase. Each phase file is self-contained: objective, task list (problem → evidence → solution → affected files → complexity → priority), dependencies, and a Definition of Done.

**Do not skip ahead.** Phase order matters — Phase 1 (billing/tenancy) is close to pointless if Phase 0's IDOR hole is still open; Phase 3's scoring rework depends on Phase 2's provider/evidence layer existing first.

## Phase list

| Phase | File | Focus | Depends on | Status |
|---|---|---|---|---|
| 0 | [PHASE_0_FIX_FOUNDATIONS.md](PHASE_0_FIX_FOUNDATIONS.md) | Stop the bleeding: security holes, fabricated data presented as real, dead/duplicated code | — | **Done — see `.claude/ROADMAP_HANDOFF.md`. Uncommitted in `backend`/`frontend` submodules; verify + commit before continuing.** |
| 1 | [PHASE_1_COMMERCIAL_SAAS_FOUNDATION.md](PHASE_1_COMMERCIAL_SAAS_FOUNDATION.md) | Tenancy middleware, billing, usage metering, entitlements | Phase 0 | In progress — A1/C1/C2/C3/B1 done, B2 blocked on real Stripe keys. See `.claude/ROADMAP_HANDOFF.md`. |
| 2 | [PHASE_2_TRUSTWORTHY_AI_OBSERVATION.md](PHASE_2_TRUSTWORTHY_AI_OBSERVATION.md) | Real provider abstraction, evidence store, cost governance | Phase 0, 1 |
| 3 | [PHASE_3_AI_SEARCH_INTELLIGENCE.md](PHASE_3_AI_SEARCH_INTELLIGENCE.md) | Real scoring methodology, prompt graph, citation/competitor rigor | Phase 2 |
| 4 | [PHASE_4_GEO_INTELLIGENCE.md](PHASE_4_GEO_INTELLIGENCE.md) | Structural site audits, evidence-linked recommendations | Phase 3 |
| 5 | [PHASE_5_DIFFERENTIATION.md](PHASE_5_DIFFERENTIATION.md) | Recommendation impact tracking, fact-accuracy monitor, consensus analysis | Phase 3, 4 |
| 6 | [PHASE_6_PLATFORM.md](PHASE_6_PLATFORM.md) | Public API, MCP, agency/white-label, real alerting | Phase 3 |
| 7 | [PHASE_7_ENTERPRISE_SCALE.md](PHASE_7_ENTERPRISE_SCALE.md) | SSO/SCIM, audit logs, scaled execution infra | Phase 1, 2 |

## How to use these files

Each task in a phase file carries:
- **Problem** — one line, plain language.
- **Evidence** — exact file:line from the audit.
- **Solution** — the concrete change.
- **Affected** — modules/files touched.
- **Complexity** — S / M / L / XL (rough sizing, not a time estimate).
- **Priority** — P0–P4, matching the audit's priority tiers.

Nothing in these files has been implemented yet. Treat each phase as its own planning-then-build cycle — get sign-off on a phase's task list before starting its build, the same way this roadmap itself required sign-off on the audit first.

## Progress log

- **2026-08-24** — A separate agent session (codex) executed Phase 0 directly against the `backend`/`frontend` submodules. Handoff notes live at `.claude/ROADMAP_HANDOFF.md` (also mirrored at `graphify-out/ROADMAP_HANDOFF.md`) and claim all of A1–A3, B1–B9, C1–C2 complete. Spot-checked and confirmed: `MockSearchService` fully removed from the codebase (zero references), `ICurrentOrganizationAccessor`/`CurrentOrganizationAccessor` wired into `CompetitorController`, `DashboardController`, `MetricsController`, `OnboardingController`, `ReportController`, `ScraperController`, `SimulatorController`, and `AdminJwt` bearer scheme present in `AdminController.cs`/`Program.cs`. **This work is uncommitted** — `git status` on both submodules shows it sitting in the working tree, matching the files the handoff describes. Full Phase 0 checklist re-verification is still owed before treating it as closed (see `PHASE_0_FIX_FOUNDATIONS.md`'s Definition of Done).
- Phase 1 kickoff blocked on two decisions the user needs to make — see the open questions raised in-session before Phase 1 work began.
