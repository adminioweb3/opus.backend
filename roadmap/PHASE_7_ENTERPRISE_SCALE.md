# Phase 7 — Enterprise & Scale

**Objective:** Add the security, compliance, and infrastructure maturity that enterprise buyers require, and rework execution infrastructure so cost and reliability hold at 1,000–10,000+ tenants instead of the current sequential-loop-over-every-org pattern.

**Depends on:** Phase 1 (tenancy/billing foundation) and Phase 2 (cost governance) — enterprise features and scale work are both meaningless without those already in place.

---

## Workstream A — Enterprise security & compliance

### A1. SSO / SAML / SCIM
- **Problem:** Current auth is Firebase JWT only, sufficient for self-serve signup but not for enterprise IT requirements.
- **Evidence:** Audit §26 (aspirational); `Program.cs:63-116` (current Firebase-only JWT validation, otherwise sound).
- **Solution:** Add SAML/OIDC SSO support and SCIM provisioning for enterprise customers, layered on top of (not replacing) the existing Firebase-backed auth for self-serve tenants.
- **Affected:** `Program.cs` auth configuration, new SSO/SCIM module.
- **Complexity:** L
- **Priority:** P3

### A2. Audit logs
- **Problem:** No audit log of who did what exists anywhere in the product — relevant both for enterprise compliance and for investigating the kind of cross-tenant access Phase 0 fixed (so a recurrence would actually be detectable next time).
- **Evidence:** N/A — confirmed absent.
- **Solution:** Add an `AuditLog` table capturing authentication events, admin actions, data exports, and destructive operations (especially anything touching `AdminController`'s endpoints from Phase 0's A2 fix).
- **Affected:** New table, middleware/interceptor to log key actions.
- **Complexity:** M
- **Priority:** P3

### A3. Granular RBAC
- **Problem:** Current authorization is effectively binary (authenticated or not) plus a `PlanType` check — no role-based permission model within an organization (e.g., Admin vs. Editor vs. Viewer).
- **Evidence:** Audit §16 (tenancy model only distinguishes org membership, not role).
- **Solution:** Add role assignment per `User`/`Organization` pair and enforce it at the same centralized layer as Phase 1's tenant middleware.
- **Affected:** `Users`/`Organizations` relationship, tenant middleware from Phase 1.
- **Complexity:** M
- **Priority:** P3

### A4. Data retention, deletion, and regional storage
- **Problem:** No data retention policy or region-pinning exists — relevant for GDPR-class enterprise requirements once serving EU customers at scale.
- **Evidence:** N/A — confirmed absent.
- **Solution:** Add configurable retention windows for raw AI observations (balancing Phase 2's "immutable evidence store" goal against storage cost and compliance deletion requirements), a real data-deletion flow (distinct from `AdminController`'s current blunt cascading delete), and evaluate regional DB deployment if EU/enterprise demand requires it.
- **Affected:** Data lifecycle policies across `PromptResponses`, `HistoricalScans`, etc.; `AdminController.cs` user/org deletion flow (needs to become a proper, audited, non-admin-secret-gated customer-initiated flow too).
- **Complexity:** L
- **Priority:** P4

---

## Workstream B — Scaled execution infrastructure

### B1. Replace the sequential-loop-over-every-org job pattern
- **Problem:** All 7 recurring Hangfire jobs loop every organization sequentially with no batching — Phase 1 added plan-based gating and staggering as a stopgap, but at 10,000+ tenants this pattern itself needs to change.
- **Evidence:** Audit §17; `BrandPulseScanRecurringJob.cs:33-65` and siblings.
- **Solution:** Move to a queue-based execution model (RabbitMQ or Kafka) where each organization's scheduled scan is enqueued as an independent message, consumed by a pool of workers with real backpressure and per-tenant/global concurrency limits — replacing "one job process iterates everyone" with "many workers drain a queue."
- **Affected:** Replaces the `*RecurringJob.cs` execution pattern; new message queue infrastructure.
- **Complexity:** XL
- **Priority:** P4 (only becomes necessary at real scale — don't over-build this before Phase 1's simpler batching/staggering stopgap is actually insufficient)

### B2. Caching and deduplication layer
- **Problem:** No caching layer or deduplication of identical/near-identical prompt executions across close time windows exists — every scan re-executes everything from scratch.
- **Evidence:** Audit §17 (cost architecture recommendations, not yet implemented).
- **Solution:** Add a cache (Redis) keyed on prompt+provider+time-bucket to avoid re-querying an AI provider for functionally identical observations within a short window, where the product's freshness requirements allow it.
- **Affected:** New caching layer in front of `IAiProvider` calls (Phase 2).
- **Complexity:** M
- **Priority:** P4

### B3. Cheap-model routing for classification tasks
- **Problem:** Every task — including simple classification (sentiment, category tagging) — goes through the same chat-completion call as complex generation tasks.
- **Evidence:** Audit §17.
- **Solution:** Route simple classification/extraction tasks to a cheaper model tier (or a non-LLM deterministic classifier where possible, per Phase 4's structural-check philosophy) and reserve the more expensive model calls for genuinely generative tasks.
- **Affected:** `SentimentClassifierService.cs`, `IAiProvider` routing logic.
- **Complexity:** M
- **Priority:** P4

### B4. Observability
- **Problem:** No mention of structured logging/metrics/tracing infrastructure was found during the audit beyond default framework logging.
- **Evidence:** N/A — not directly audited, flagged as a gap to verify.
- **Solution:** Add structured logging, request tracing, and cost/latency dashboards per provider (building on Phase 2's per-call cost capture) so operational issues and cost anomalies are visible before they become customer-facing incidents.
- **Affected:** Cross-cutting — logging/metrics middleware.
- **Complexity:** M
- **Priority:** P3

### B5. Backup & disaster recovery
- **Problem:** No backup/DR strategy was surfaced during the audit.
- **Evidence:** N/A — not directly audited, flagged as a gap to verify before enterprise sales conversations.
- **Solution:** Confirm/establish a real Postgres backup and point-in-time-recovery policy, and a documented DR runbook, before any enterprise contract requiring an SLA is signed.
- **Affected:** Infrastructure/ops, not application code.
- **Complexity:** M
- **Priority:** P3

---

## Definition of Done for Phase 7

- [ ] SSO/SAML/SCIM available for enterprise tenants without breaking self-serve Firebase auth.
- [ ] Audit log captures admin and destructive actions.
- [ ] Role-based permissions exist within an organization.
- [ ] Recurring job execution has moved off (or has a validated plan to move off) the sequential-loop-over-every-org pattern before tenant count makes it a real bottleneck.
- [ ] Backup/DR policy is documented and tested.
