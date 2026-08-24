# Phase 1 — Commercial SaaS Foundation

**Objective:** Give the product the ability to actually be sold: enforced tenant isolation as infrastructure (not per-controller discipline), real billing, and usage metering that caps AI cost per tenant.

**Depends on:** Phase 0 (the IDOR fix and admin lockdown must already be done — this phase builds the permanent versions of those fixes).

---

## Workstream A — Tenant isolation as infrastructure

### A1. Centralized tenant-resolution middleware
- **Problem:** Tenant context is currently resolved per-controller via a copy-pasted `GetOrganizationIdAsync()` helper — the direct root cause of the Phase 0 IDOR findings. Even after Phase 0's point fixes, this pattern will regress the next time a controller is added.
- **Evidence:** Audit §16; `PromptIntelligenceController.cs:44-51`, `ContentController.cs:24-31`, `AnalysisController.cs:27-33`, `TeamController.cs:24` (the correct-but-duplicated pattern).
- **Solution:** Build one `ITenantContext`/`ICurrentOrganizationAccessor` service populated once per request from the JWT, injected everywhere org-scoped data is read. Add an analyzer/lint rule or code-review checklist item: no controller may accept `organizationId` as a route/query/body parameter for authenticated endpoints.
- **Affected:** New `Citationly.Infrastructure/Tenancy/` module; every controller currently doing this manually.
- **Complexity:** M
- **Priority:** P0

### A2. Controllers that bypass MediatR/raw-SQL controllers
- **Problem:** 7 of 21 controllers bypass MediatR entirely; 4 controllers (`AdminController`, `AssistantController`, `AuthController`, `DashboardController`) hit `IDbConnection` directly, bypassing the repository layer — making it harder to guarantee every query goes through the same tenant-filtering discipline.
- **Evidence:** Audit §2 (Backend Architecture).
- **Solution:** Not a full rewrite in this phase — but every raw-SQL query in these 4 controllers must be audited to confirm it filters by the now-centralized tenant context. Track this as a checklist, not a refactor mandate.
- **Affected:** `AdminController.cs`, `AssistantController.cs`, `AuthController.cs`, `DashboardController.cs`.
- **Complexity:** M
- **Priority:** P1

---

## Workstream B — Billing

### B1. Integrate a real payment processor (Stripe recommended)
- **Problem:** No billing integration exists anywhere — `PlanType` is a free-form string with no payment processor, invoice, or subscription lifecycle.
- **Evidence:** Audit §15; `Organization.PlanType`/`TrialEndsAt` only, no `Subscriptions`/`Invoices`/`Billing` tables (confirmed absent from `init.sql`).
- **Solution:** Add `Subscriptions`, `Invoices`, `PaymentMethods` tables (see Phase 1 DB changes below). Integrate Stripe Checkout/Billing Portal for subscription creation, upgrade/downgrade, cancellation, and payment-failure handling via webhooks. `Organization.PlanType` becomes a derived/cached field synced from the Stripe subscription status, not a manually-set string.
- **Affected:** New `Citationly.Infrastructure/Billing/` module, `Organizations` table (add `StripeCustomerId`), new tables, new `BillingController`/webhook endpoint.
- **Complexity:** L
- **Priority:** P0

### B2. Wire the real billing UI, remove the orphaned mock page
- **Problem:** `BillingSection.tsx` has every button calling `notImplemented()`; a second, unlinked mock billing page exists at `dashboard/billing/page.tsx`.
- **Evidence:** Audit §18; `frontend/src/components/settings/BillingSection.tsx`, `frontend/src/lib/mock-data/billing.ts`.
- **Solution:** Replace both with one real billing UI backed by B1's API — plan display, upgrade/downgrade, payment method management, invoice history/download via Stripe's hosted portal or a thin custom UI over the Stripe API. Delete the orphaned page and its mock-data file.
- **Affected:** `BillingSection.tsx`, `dashboard/billing/page.tsx` (replace or delete), `mock-data/billing.ts` (delete).
- **Complexity:** M
- **Priority:** P1

---

## Workstream C — Usage metering & entitlements

### C1. Usage metering tables and capture
- **Problem:** No `Usage`/`Quota` table exists anywhere; AI-cost-heavy endpoints have no rate limit or per-tenant cap beyond one hardcoded weekly cooldown.
- **Evidence:** Audit §15, §17; grep for `Usage`/`Quota` across `backend/` returned nothing.
- **Solution:** Add a `UsageCounters` table (org, metric type — AI calls, tokens, crawl pages, exports — period, count) and a middleware/decorator that increments it on every AI call, crawl job, and export. This depends on Phase 2's per-call token/cost capture existing to be meaningful — coordinate timing with Phase 2, but the table and counting scaffolding can be built now.
- **Affected:** New table, new `IUsageMeteringService`.
- **Complexity:** M
- **Priority:** P0

### C2. Centralized entitlement service
- **Problem:** Only two backend-enforced plan checks exist in the entire product (`regions-summary`, `personas-summary` gated to Enterprise); no general `CanUseFeature()`/`CheckQuota()` layer exists; the frontend has zero authority to gate anything reliably.
- **Evidence:** Audit §15; `PromptIntelligenceController.cs:527-546`.
- **Solution:** Build `IEntitlementService` with `CanUseFeature(orgId, feature)` and `CheckQuota(orgId, metric)` methods, backed by a `PlanLimits` config (per-plan feature flags and numeric quotas). Every AI-cost endpoint and every plan-gated feature must call this service server-side — never trust a frontend `if (plan === "pro")` check as the sole gate.
- **Affected:** New `Citationly.Application/Entitlements/` module; every controller currently missing a plan check.
- **Complexity:** L
- **Priority:** P0

### C3. Gate the recurring background jobs by plan and add batching
- **Problem:** 7 daily Hangfire jobs loop every organization with no plan-based skip, no batching, no concurrency cap — direct unbounded cost multiplier as tenant count grows.
- **Evidence:** Audit §16, §17; `BrandPulseScanRecurringJob.cs:33-65` and siblings, all `Cron.Daily` with no offset (`Program.cs:204-237`).
- **Solution:** Add plan-aware scan cadence (e.g., Trial = weekly, Pro = daily, Enterprise = configurable), batch organizations instead of unconditional sequential loop, stagger the 7 jobs' cron offsets so they don't all fire at once, and add a global concurrency cap enforced through C2's entitlement service.
- **Affected:** All 7 `*RecurringJob.cs` files, `Program.cs` cron registration.
- **Complexity:** L
- **Priority:** P0

---

## Database changes required (Phase 1)

- `Subscriptions` (org_id, stripe_subscription_id, plan, status, current_period_end, ...)
- `Invoices` (org_id, stripe_invoice_id, amount, status, issued_at, ...)
- `PaymentMethods` (org_id, stripe_payment_method_id, brand, last4, ...)
- `UsageCounters` (org_id, metric_type, period_start, period_end, count)
- `PlanLimits` (plan_name, feature_key, limit_value) — or a config-driven equivalent if a table feels like overkill initially
- `Organizations`: add `StripeCustomerId` column

---

## Definition of Done for Phase 1

- [ ] No controller resolves tenant context from a client-supplied value — all resolution goes through the shared middleware.
- [ ] A customer can subscribe, upgrade, downgrade, and cancel via a real Stripe-backed flow.
- [ ] Every AI-cost endpoint calls `CheckQuota()` before executing and `ConsumeUsage()` after.
- [ ] Recurring jobs respect plan-based cadence and are staggered/batched, not all firing at once against every org.
- [ ] Billing UI shows real invoices/payment methods, no `notImplemented()` buttons remain.
