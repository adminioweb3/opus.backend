# Citationly Customer-Ready Development Plan

Date: 2026-08-26  
Source: `CITATIONLY_ENTERPRISE_SAAS_AUDIT.md`  
Target outcome: a secure, trustworthy, supportable paid SaaS launch

## Starting Position

Citationly has a credible modular-monolith foundation, real organization-aware APIs, billing scaffolding, background jobs, provider adapters, and a substantial product surface.

It is not ready for paid self-serve customers yet. The first release should be a controlled beta with 3-5 design partners. The plan below prioritizes customer safety and product truth before breadth.

## Delivery Principles

- Keep the modular monolith. Do not introduce microservices, Kafka, Kubernetes, or multi-region infrastructure before the product has validated demand and workload shape.
- Server-side controls are authoritative for tenancy, roles, entitlements, usage, and billing.
- Every customer-visible metric must disclose whether it is observed, derived, inferred, estimated, or unavailable.
- Expensive work is asynchronous, bounded, idempotent, cancellable, and visible to the customer.
- A phase is complete only when its code, tests, operational documentation, and customer-facing behavior are complete.

## Phase Overview

| Phase | Name | Primary result | Depends on | Exit status |
|---:|---|---|---|---|
| 0 | Launch Blocker Triage | Critical security and trust defects closed | None | Paid launch blocked until complete |
| 1 | Tenant, Usage, and Data Safety | Strong tenant boundary and enforceable limits | 0 | Required for any external user |
| 2 | Trustworthy AI Measurement | One provider pipeline with evidence and cost provenance | 1 | Required before publishing scores |
| 3 | Production Billing and Entitlements | Safe Stripe lifecycle and plan enforcement | 1, 2 | Required before charging |
| 4 | Customer Workflow Completion | Real onboarding, dashboard, reports, alerts, and settings | 2, 3 | Required for design-partner beta |
| 5 | Reliability and Beta Operations | Observable, recoverable, supportable production service | 1-4 | Required for paid beta |
| 6 | Enterprise Readiness and Scale | SSO/SCIM, governance, workload scaling, and launch controls | 5 | Required for enterprise expansion |

---

## Phase 0: Launch Blocker Triage

**Objective:** Remove vulnerabilities and misleading product behavior that make customer use unsafe or untrustworthy.

**Suggested sequence:** security fixes first, then customer-visible trust fixes, then regression tests.

### Workstreams

1. **Fix SCIM cross-tenant mutation.** Replace the email `ON CONFLICT` update path with an explicit organization-scoped lookup. Reject a matching email belonging to another organization. Add same-tenant and cross-tenant regression tests.

   Affected area: `backend/Citationly.API/Controllers/ScimController.cs`

2. **Close SSRF paths.** Create one URL validation policy used by competitor analysis, scraping, and GEO audits. Allow only HTTP/HTTPS, block localhost/private/link-local/multicast/metadata ranges, validate DNS resolution, cap redirects, response size, content types, pages, and timeouts.

   Affected areas: `ScraperController`, `AnalyzeCompetitorCommand`, `PlaywrightScraperEngine`, `GeoTechnicalAuditService`

3. **Remove simulated scoring from live customer paths.** Any unavailable provider result must produce an incomplete/unavailable state, never a random or silently estimated score. Mark legacy simulation paths for removal or quarantine.

4. **Remove or gate mock/preview screens.** Team, integrations, monitoring, and any mock-backed dashboard surfaces must be hidden from paid navigation or show an unmistakable preview state. A preview integration must never submit fake-looking configuration to a real write endpoint.

5. **Add a shared provenance presentation.** Every score and chart must show its evidence type and data freshness. Replace synthetic trend lines with real history or an honest insufficient-history state.

6. **Harden production defaults.** Disable public Swagger/Hangfire dashboards in production, remove exception detail from API responses, verify production secret configuration at startup, and remove development token bypasses outside development.

### Definition of Done

- Cross-tenant SCIM mutation test fails before the fix and passes after it.
- SSRF tests cover loopback, private IP, metadata IP, redirects, invalid schemes, and DNS rebinding defenses.
- No customer-facing score is generated from random or hidden fallback data.
- Mock/preview pages cannot be mistaken for live customer data.
- Production configuration has no demo authentication bypass or public operational dashboard.

---

## Phase 1: Tenant, Usage, and Data Safety

**Objective:** Make organization isolation, authorization, quota enforcement, and deletion behavior consistent infrastructure concerns.

### Workstreams

1. **Centralize tenant context.** Resolve the organization once from authenticated identity and inject `ICurrentOrganizationAccessor`/tenant context into controllers, handlers, repositories, and jobs. Reject client-supplied organization IDs on authenticated data APIs.

2. **Make authorization server-owned.** Consolidate role definitions and permissions. Treat frontend permission checks as display helpers only. Add tests for every high-value tenant-scoped endpoint and role boundary.

3. **Make usage reservation atomic.** Replace separate check/consume calls with a database transaction or conditional upsert that reserves usage only when the resulting count remains within the plan limit. Reconcile reservations after provider failure.

4. **Move short-window AI limits to shared storage.** Use Redis or locked Postgres counters so limits remain effective across multiple API instances and workers.

5. **Make destructive admin and deletion workflows transactional.** Add explicit confirmation, audit records, MFA or step-up authentication, soft-delete where appropriate, and resumable background deletion for large datasets.

6. **Add pagination and indexes.** Bound invoices, scans, scraping jobs/pages, prompt responses, mentions, citations, alerts, and dashboard trend reads. Add targeted organization/time indexes for high-volume tables.

7. **Replace startup raw migrations.** Move to versioned, reviewed migrations with history, deployment ordering, and a rollback/restore procedure.

### Definition of Done

- Tenant-isolation and role-matrix integration tests cover all customer-facing controllers.
- Concurrent quota tests prove that parallel requests cannot exceed limits.
- Multi-instance rate-limit behavior is verified against shared storage.
- Deletion and admin actions are auditable and recover safely from partial failure.
- Large list endpoints return bounded pages and use verified indexes.
- Production migrations can be applied independently of application startup.

---

## Phase 2: Trustworthy AI Measurement

**Objective:** Make every AI-backed result explainable, reproducible, cost-accounted, and honest about what was actually observed.

### Workstreams

1. **Create one AI execution boundary.** Route `IOpenAiService` callers through the provider registry/orchestrator. Remove direct provider-specific calls from business workflows.

2. **Standardize evidence records.** Persist organization, question, provider, model, prompt version, response, tokens, estimated/actual cost, grounded/search state, timestamps, error state, and scoring-method version.

3. **Separate measurement from inference.** Label values as `observed`, `derived`, `ai_inferred`, `estimated`, or `unavailable`. Do not use an LLM to simulate an answer-engine observation and present it as a measured result.

4. **Version scoring methodology.** Store the scoring version with every scan. Keep deterministic calculations separate from LLM classification and expose confidence only when it has a defined calculation.

5. **Add cost governance.** Enforce per-organization and global call, token, and spend budgets before execution. Record provider failures and release or reconcile reservations.

6. **Improve provider health behavior.** Add bounded retries, circuit breakers, timeouts, provider fallback rules, and customer-visible partial-result states.

### Definition of Done

- No production workflow bypasses the common AI execution boundary.
- Every score can be traced to provider evidence or is explicitly marked inferred/estimated/unavailable.
- A customer can inspect provider, model, timestamp, evidence count, and scoring version for a result.
- Provider failure never creates a fabricated score.
- Usage and cost dashboards reconcile with provider response metadata.

---

## Phase 3: Production Billing and Entitlements

**Objective:** Safely charge customers and keep plan state, usage, invoices, and feature access synchronized.

### Workstreams

1. **Define the plan catalog.** Make plans, limits, included usage, overage policy, trial rules, tax behavior, and cancellation behavior explicit and server-owned.

2. **Harden Stripe checkout and portal flows.** Allow only configured application return domains. Ignore arbitrary client redirect destinations. Validate price IDs and environment configuration at startup.

3. **Add webhook idempotency.** Create a durable event ledger keyed by Stripe event ID with payload hash, status, retry count, processed timestamp, and failure reason. Make event handlers replay-safe.

4. **Implement lifecycle reconciliation.** Handle trialing, active, past-due, unpaid, canceled, incomplete, upgrade, downgrade, and renewal states. Reconcile subscription state from Stripe on a scheduled job.

5. **Connect entitlements to billing state.** Feature access and quotas must be derived from the authoritative subscription state, with a deliberate grace policy for payment failure.

6. **Finish the billing UI.** Show current plan, limits, usage, payment status, invoices, and next action. Remove mock billing data and dead buttons.

### Definition of Done

- Stripe test-mode checkout, renewal, failure, upgrade, downgrade, cancellation, and replayed webhook scenarios pass.
- Redirect URLs are allowlisted and environment-specific.
- Duplicate webhook delivery produces one state transition.
- Usage limits and feature access change correctly with subscription status.
- Billing UI contains only real data and gives customers actionable failure states.

---

## Phase 4: Customer Workflow Completion

**Objective:** Deliver a coherent first-run experience in which the main customer journeys work end to end.

### Workstreams

1. **Onboarding pipeline.** Validate website ownership/domain input, run bounded crawl and analysis jobs, show progress, support retry/cancel, and land the user on a real first report.

2. **Dashboard truthfulness.** Consolidate the two organization stores, remove dummy defaults, show freshness and provenance on scorecards, and handle no-data/partial-data states.

3. **Prompt intelligence.** Provide observed prompt responses, citations, competitors, provider coverage, and failed-provider states. Do not imply that unconfigured engines were checked.

4. **Reports and exports.** Add evidence-linked report views, bounded export jobs, download status, and share-link access controls.

5. **Alerts.** Implement or remove webhook configuration. Add delivery status, retries, failure visibility, and email verification/opt-out behavior.

6. **Team and integrations.** Replace preview UI with real server-backed flows only. Add invite lifecycle, role changes, revoke behavior, integration validation, secret masking, and connection health.

7. **Authentication recovery.** Add auth bootstrap, token refresh, 401 recovery, logout-on-invalid-session, and clear expired-session behavior in the API client.

### Definition of Done

- A new user can sign up, onboard a verified site, wait for a bounded scan, inspect evidence-backed results, invite a teammate, configure an alert, and export a report.
- No primary workflow depends on frontend mock data.
- Empty, loading, partial, failed, and stale states are designed and tested.
- Customer-visible claims match the providers and data actually available to that organization.

---

## Phase 5: Reliability and Beta Operations

**Objective:** Operate the product safely for real design partners and respond to failures without manual database surgery.

### Workstreams

1. **Observability.** Add structured logs, request/job correlation IDs, OpenTelemetry traces, error tracking, database timing, provider latency, and cost metrics.

2. **Job reliability.** Add paging, leases, idempotency keys, bounded concurrency, retry policies, dead-letter/manual replay behavior, and per-organization job status.

3. **Operational controls.** Add health/readiness checks, backup monitoring, restore testing, migration runbooks, incident playbooks, and an emergency provider-spend kill switch.

4. **Support tooling.** Build an auditable support view for organization status, subscription state, usage, recent jobs, provider failures, and customer-visible incidents. Avoid unrestricted destructive tools.

5. **Security operations.** Add dependency scanning, secret scanning, vulnerability response ownership, access review, log redaction, and production environment separation.

6. **Performance validation.** Load test onboarding, dashboard reads, prompt execution, recurring scans, billing webhooks, and concurrent quota enforcement using representative data volumes.

### Definition of Done

- A failed scan or provider call can be diagnosed from correlated logs and traces.
- Jobs can be retried without duplicate customer data or duplicate charges.
- Backups have a tested restore result and documented recovery objectives.
- Load tests identify safe operating limits for the beta cohort.
- A support engineer can resolve common billing, auth, scan, and usage issues without direct production SQL edits.

---

## Phase 6: Enterprise Readiness and Scale

**Objective:** Add enterprise controls only after the core paid workflow is reliable and validated.

### Workstreams

1. **SSO.** Complete SAML/OIDC login, domain verification, session policy, JIT provisioning, and role mapping with real identity-provider tests.

2. **SCIM.** Complete create/update/deactivate/group behavior, token rotation, provisioning audit, retry handling, and cross-tenant regression coverage.

3. **Governance.** Finalize audit-log retention, data export/deletion requests, legal holds, privacy workflows, access reviews, and customer-facing security documentation.

4. **Agency and white-label validation.** Test agency/client isolation, delegated access, report sharing, custom branding, billing ownership, and client offboarding with realistic accounts.

5. **Scale execution infrastructure.** Separate API and worker capacity, partition heavy workloads, introduce queue infrastructure where justified, and add read scaling only from measured bottlenecks.

6. **Commercial launch controls.** Complete terms/privacy/security pages, DPA and subprocessors list, status page, support SLAs, pricing enforcement, and launch rollback plan.

### Definition of Done

- At least one real SSO and SCIM provider has passed an end-to-end test.
- Enterprise data lifecycle requests are auditable, resumable, and policy-compliant.
- Agency and shared-report scenarios have tenant-isolation tests.
- Capacity planning is based on load-test data, not projected user counts alone.
- Security and support commitments match the controls actually deployed.

---

## Customer-Readiness Gates

### Internal Alpha Gate

Required: Phase 0 complete, Phase 1 security and quota work substantially complete, real provider keys in a non-production environment, and no mock data in the tested workflow.

### Design-Partner Beta Gate

Required: Phases 0-4 complete, Stripe test mode validated, onboarding and reporting work end to end, observability and support runbooks exist, and 3-5 invited organizations pass tenant-isolation and failure-recovery tests.

### Paid Self-Serve Gate

Required: Phases 0-5 complete, production billing reconciliation verified, backup restore tested, security regression suite green, cost limits enforced across instances, and an incident/support owner assigned.

### Enterprise Expansion Gate

Required: Phase 6 complete for the specific enterprise commitments being sold. Do not sell SSO, SCIM, white-labeling, custom retention, or response-time SLAs before each capability has passed its own acceptance tests.

## Recommended First Sprint

Start with the smallest set of changes that materially reduces launch risk:

1. Fix the SCIM cross-tenant email conflict.
2. Build and apply the shared SSRF URL policy.
3. Disable the development token bypass and production operational dashboards.
4. Hide or relabel mock/preview pages and remove the fake integration write path.
5. Add regression tests for tenant isolation, SSRF, and fabricated-score prevention.

After those changes, proceed to atomic usage reservation and billing webhook idempotency. Those two controls determine whether Citationly can safely absorb real customer traffic and real payment events.

## Final Product Recommendation

Use the existing modular monolith and ship in controlled stages. The shortest credible route to customers is not more features; it is trustworthy measurement, secure tenant boundaries, enforceable AI cost controls, production-grade billing, and a first-run workflow that works without mock data.

Current recommendation: **internal alpha now, design-partner beta after Phases 0-5, paid self-serve after the beta gates pass, enterprise expansion after Phase 6.**
