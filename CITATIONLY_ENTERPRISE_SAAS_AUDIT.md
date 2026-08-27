# Citationly Enterprise SaaS Audit

Date: 2026-08-26  
Mode: Read-only architecture, product, security, billing, AI, and readiness audit

## 1. Executive Verdict

Citationly is **not ready for paid customers today**.

It is no longer just a prototype. The codebase now has real SaaS structure, Firebase auth, organization scoping, admin JWTs, Hangfire jobs, Stripe scaffolding, usage counters, provider adapters, prompt evidence tables, and a much better backend foundation than older audit notes suggest.

However, it still has enough security, billing, AI-trust, mock-data, and operational gaps that charging customers would be risky.

Recommended launch stage: **internal alpha / controlled design-partner beta only**.

## 2. Overall Scores

| Area | Score |
|---|---:|
| Product maturity | 5/10 |
| UI/UX | 6/10 |
| Frontend architecture | 4/10 |
| Backend architecture | 6/10 |
| Database design | 5/10 |
| Scalability | 4/10 |
| Security | 3/10 |
| Tenant isolation | 4/10 |
| Subscription readiness | 4/10 |
| AI architecture | 5/10 |
| AI cost control | 4/10 |
| Observability | 4/10 |
| Reliability | 4/10 |
| Testing | 2/10 |
| Enterprise readiness | 3/10 |
| Customer retention potential | 6/10 |

## 3. Current Architecture

```text
Next.js customer app + Vite admin app
        |
        v
ASP.NET API
  |-- Firebase JWT auth
  |-- Admin JWT auth
  |-- API key auth
  |-- Organization and RBAC accessors
  |-- Billing, entitlements, and usage counters
  |-- Prompt intelligence
  |-- Content, reports, alerts, crawler, admin
        |
        v
Application layer
  |-- MediatR commands
  |-- Background job services
  |-- Domain services
        |
        v
Infrastructure layer
  |-- Dapper/Postgres repositories
  |-- Hangfire/Postgres jobs
  |-- Stripe billing
  |-- AI provider adapters
  |-- Playwright crawler
  |-- SMTP alert delivery
        |
        v
Postgres + external AI APIs + Stripe + SMTP
```

## 4. What Is Working

- The backend has a respectable modular-monolith shape across API, Application, Infrastructure, and Domain projects.
- Most customer controllers now derive organization context server-side through `ICurrentOrganizationAccessor`.
- Admin auth is server-side JWT based, replacing the older frontend-secret pattern.
- Billing and entitlement scaffolding exists.
- Prompt execution has a provider registry for OpenAI, Anthropic, Gemini, and Perplexity.
- Hangfire jobs are staggered instead of all scheduled at exactly the same interval.
- Prompt analysis, prompt responses, mentions, citations, usage counters, subscriptions, invoices, audit, retention, deletion, SCIM, and SSO-related schema pieces exist.

Relevant evidence:

- `backend/Citationly.API/Program.cs`
- `backend/Citationly.Infrastructure/Services/CurrentOrganizationAccessor.cs`
- `backend/Citationly.Infrastructure/Services/EntitlementService.cs`
- `backend/Citationly.Infrastructure/Services/StripeBillingService.cs`
- `backend/Citationly.Infrastructure/Database/SelfHealingMigrations.cs`

## 5. Critical Risks

### 5.1 SCIM Cross-Tenant User Mutation

`ScimController.CreateUser` uses an `ON CONFLICT (Email) DO UPDATE` path without scoping the conflict to the authenticated SCIM tenant. Because email is globally unique, one tenant provisioning an existing email can update another tenant user's display name or role.

Evidence:

- `backend/Citationly.API/Controllers/ScimController.cs`

Impact:

- Cross-tenant integrity violation.
- Potential role mutation on a user outside the caller's organization.
- Enterprise launch blocker.

Recommended fix:

- Reject email conflicts where the existing user's `OrganizationId` differs from the SCIM token's organization.
- Prefer a transaction with explicit lookup before insert.
- Add tests for same-org conflict, cross-org conflict, and role escalation.

### 5.2 SSRF Via Crawler And Analyzer URLs

The crawler, competitor analyzer, and GEO audit accept arbitrary URLs and send Playwright or HttpClient to them without private-network, DNS, IP, or scheme validation.

Evidence:

- `backend/Citationly.API/Controllers/ScraperController.cs`
- `backend/Citationly.Infrastructure/Services/PlaywrightScraperEngine.cs`
- `backend/Citationly.Application/Features/Content/Commands/AnalyzeCompetitorCommand.cs`
- `backend/Citationly.Infrastructure/Services/GeoTechnicalAuditService.cs`

Impact:

- Internal network probing.
- Cloud metadata endpoint exposure.
- Expensive crawler abuse.
- Browser exploitation surface through Playwright.

Recommended fix:

- Allow only `http` and `https`.
- Block localhost, loopback, link-local, private, multicast, and cloud metadata IP ranges.
- Resolve and re-check DNS before connection.
- Enforce max pages, max response size, content type limits, timeouts, redirects policy, and per-org crawl quotas.

### 5.3 Billing Is Scaffolded But Not Production-Safe

Stripe checkout and portal session creation exist, but success/cancel/return URLs are accepted from the client and passed through. There is no durable Stripe event ledger/idempotency table.

Evidence:

- `backend/Citationly.API/Controllers/BillingController.cs`
- `backend/Citationly.Infrastructure/Services/StripeBillingService.cs`
- `backend/Citationly.Infrastructure/Repositories/BillingRepository.cs`

Impact:

- Open redirect or incorrect billing flow risks.
- Webhook replay/idempotency issues.
- Subscription status drift.
- Hard-to-debug revenue support issues.

Recommended fix:

- Allowlist redirect domains.
- Add `StripeWebhookEvents` or `BillingEvents` with unique `StripeEventId`.
- Store event payload hash, processing status, processed timestamp, and failure reason.
- Make subscription state transitions explicit.
- Add billing integration tests with replayed webhooks.

### 5.4 Usage Limits Are Race-Prone

Quota checking and usage consumption are separate calls. In-memory hourly AI caps do not coordinate across multiple app instances.

Evidence:

- `backend/Citationly.Application/Interfaces/IEntitlementService.cs`
- `backend/Citationly.Infrastructure/Services/EntitlementService.cs`
- `backend/Citationly.Infrastructure/Services/AiUsageLimiter.cs`

Impact:

- Customers can exceed plan limits during concurrent requests.
- Multiple backend instances can bypass global and tenant hourly AI caps.
- Spend controls are advisory rather than authoritative.

Recommended fix:

- Implement atomic usage reservation in Postgres.
- Use a single database statement or stored function that increments only if the resulting count is within limit.
- Move short-window counters to Redis or Postgres advisory/row-lock based counters.
- Track usage after provider result reconciliation.

### 5.5 AI Trust Is Inconsistent

The product now has provider adapters, but many workflows still call `IOpenAiService` directly. Some onboarding/scoring paths still ask an LLM to simulate visibility instead of relying only on observed provider responses.

Evidence:

- `backend/Citationly.Infrastructure/Services/LLMRunnerService.cs`
- `backend/Citationly.Infrastructure/Services/OpenAiService.cs`
- `backend/Citationly.Infrastructure/Services/AiVisibilityEngineService.cs`
- `backend/Citationly.Application/Features/Onboarding/Commands/CompleteOnboardingCommand.cs`

Impact:

- Customers may treat estimated scores as observed market evidence.
- Cost/provenance is inconsistent.
- Multi-engine marketing can overstate what actually ran.

Recommended fix:

- Route all AI calls through one provider execution layer.
- Store provider, model, prompt, response, token counts, cost, grounded/search flag, error state, and scoring method.
- Label every score as measured, inferred, estimated, or unavailable.

## 6. Top 20 Problems

| # | Severity | Problem | Impact | Recommended Fix | Complexity |
|---:|---|---|---|---|---|
| 1 | Critical | SCIM cross-tenant email conflict | Tenant integrity breach | Scope/reject cross-org email conflicts | Medium |
| 2 | Critical | SSRF via crawler/analyzers | Internal network exposure | URL validator, IP blocklist, crawl limits | Medium |
| 3 | High | Billing redirect URLs client-controlled | Open redirect / broken payments | Domain allowlist | Low |
| 4 | High | Stripe webhooks lack event ledger | Replay/drift risk | Unique Stripe event processing table | Medium |
| 5 | High | Quotas are not atomic | Plan limit bypass | DB-side usage reservation | Medium |
| 6 | High | AI hourly caps are in-memory | Multi-instance bypass | Shared counter store | Medium |
| 7 | High | Old simulated AI scoring remains live | Trust erosion | Remove or quarantine old scoring path | Medium |
| 8 | High | Mixed AI provider architecture | Inconsistent cost/provenance | Central provider execution service | High |
| 9 | High | Preview/mock pages are in main nav | Customer confusion | Hide behind beta flags | Low |
| 10 | High | Dummy integration flow can write fake config | False integration state | Disable until validated | Low |
| 11 | Medium | Dual frontend organization stores | State drift | Consolidate server-backed org store | Medium |
| 12 | Medium | Frontend permission matrix drifts from backend RBAC | UX/security confusion | Server-authoritative permissions | Medium |
| 13 | Medium | Startup raw migrations | Operational risk | Versioned migrations | Medium |
| 14 | Medium | Admin destructive actions lack transactions | Partial deletion/data loss | Transactions, soft deletes, MFA | Medium |
| 15 | Medium | Unbounded reads | Slow dashboards/support | Pagination/cursors | Medium |
| 16 | Medium | Prompt child tables need high-volume indexes | Query slowdown | Add targeted indexes | Low |
| 17 | Medium | Alert webhook fields are not delivered | Broken customer expectation | Implement or remove webhook UI | Medium |
| 18 | Medium | GET onboarding endpoints can mutate/trigger expensive work | Abuse/caching bugs | Use POST and rate limits | Low |
| 19 | Medium | Anonymous AI helper endpoints lack strong abuse controls | AI spend abuse | Auth/rate limits | Medium |
| 20 | Medium | Thin tests | Regression risk | Security/billing/AI/job/e2e tests | High |

## 7. Product Workflow Assessment

| Workflow | Current State | Problem | Recommendation |
|---|---|---|---|
| Signup/login | Firebase auth with backend sync | Persisted client state can drift | Add auth bootstrap, token refresh, 401 recovery |
| Onboarding | Crawling, AI analysis, first-run jobs | Mixes real data with estimates | Use explicit job pipeline and provenance labels |
| Dashboard | Real APIs plus preview surfaces | Customer trust risk | Hide preview pages for paid tenants |
| AI prompt analysis | Provider registry exists | Some flows are still OpenAI-only | Centralize all AI execution |
| Content analysis | Useful commands exist | Arbitrary URLs and cost risk | Validate URLs and gate by entitlement |
| Billing | Stripe scaffold exists | Not safe to charge yet | Harden webhooks, URLs, plans, reconciliation |
| Team/RBAC | Roles exist | UI matrix and backend role ranks drift | Server-owned permissions |
| Alerts | Email delivery exists | Webhook settings are not implemented | Implement channel delivery status |
| Reports/exports | Scoped server APIs exist | Needs stronger provenance and pagination | Add export jobs and evidence labels |

## 8. Frontend Assessment

The frontend is visually more mature than the backend readiness would imply. It has meaningful dashboard surfaces, billing UI, settings, sidebar navigation, and a reasonably complete product shell.

Main frontend risks:

- Two organization stores exist with different mock/default organizations.
- Several dashboard pages are preview/mock-backed but still present in main navigation.
- The integrations preview can submit dummy-looking config to a real API path.
- Client-side permission checks are not authoritative and can drift from backend role enforcement.
- API client behavior lacks a robust token refresh/revalidation story.

Relevant files:

- `frontend/src/lib/stores/organizationStore.ts`
- `frontend/src/lib/stores/organization-store.ts`
- `frontend/src/lib/stores/auth-store.ts`
- `frontend/src/lib/utils.ts`
- `frontend/src/components/settings/BillingSection.tsx`
- `frontend/src/app/(dashboard)/dashboard/team/page.tsx`
- `frontend/src/app/(dashboard)/dashboard/integrations/page.tsx`
- `frontend/src/app/(dashboard)/dashboard/monitoring/page.tsx`

## 9. Backend Assessment

The backend has a solid direction: ASP.NET API, MediatR, Dapper, clear infrastructure services, Hangfire, and domain entities. It is appropriate to keep this as a modular monolith for now.

Main backend risks:

- Some services trust caller ownership checks done by controllers instead of verifying internally.
- Admin destructive paths need transactions, MFA, stronger audit, and safer data lifecycle handling.
- Several endpoints lack strong validation, pagination, and consistent error response shape.
- Expensive operations are too easy to trigger.
- Some exception responses leak implementation details.

## 10. Database Assessment

The schema is significantly beyond prototype level. It contains organizations, users, subscriptions, invoices, payment methods, usage counters, plan limits, prompt analysis, responses, mentions, visibility, alerts, audit logs, SSO, SCIM, retention, deletion, and recommendation/impact tables.

Main database risks:

- Raw startup migration style is risky for production.
- Migration history and rollback are missing.
- Billing events need a durable idempotency table.
- High-volume prompt response/mention/citation tables need careful indexing.
- Data deletion needs transactionally safe workflows.
- Some repository reads are unbounded.

Recommended database improvements:

- Add versioned migrations using DbUp, Flyway, EF migrations, or a similar tool.
- Add `BillingEvents(StripeEventId unique, EventType, PayloadHash, Status, ProcessedAt, Error)`.
- Add atomic usage reservation SQL.
- Add indexes for prompt analysis, prompt responses, mentions, citations, scraping jobs, alerts, and users by organization.
- Add soft-delete and deletion-audit conventions.

## 11. Security Assessment

Highest priority security fixes:

1. Fix SCIM cross-tenant email upsert.
2. Add SSRF protections to all URL-fetching paths.
3. Validate Stripe redirect URLs.
4. Make admin destructive actions transactional and MFA-gated.
5. Make quota checks atomic.
6. Move rate limits out of memory.
7. Replace frontend permission assumptions with server-owned permissions.
8. Lock down production API docs or require authorization.

The current server-side organization access pattern is good, but it is not enough to call the system secure while SCIM and URL-fetching remain exposed.

## 12. Billing And Subscription Readiness

Can Citationly safely start charging customers today?

**No.**

What exists:

- Subscription, invoice, payment method, and usage tables.
- Stripe checkout session creation.
- Stripe billing portal creation.
- Stripe webhook handler.
- Plan limits and entitlement checks.
- Billing settings UI.

What is missing before paid launch:

- Stripe event idempotency.
- Redirect URL allowlist.
- Production Stripe configuration validation.
- Correct plan mapping and plan catalog ownership.
- Atomic usage enforcement.
- Tax/address/customer metadata decisions.
- Billing failure states and support workflows.
- Integration tests with real Stripe test-mode events.

## 13. AI Architecture And Trust

The AI direction is promising but uneven.

Good:

- Provider adapters exist.
- Prompt responses can store provider, model, tokens, cost, and grounded/search state.
- Prompt execution can run configured providers concurrently.
- Some scoring now derives from prompt history and technical audit data.

Risk:

- Many workflows still call `IOpenAiService` directly.
- Some scores are still LLM-estimated.
- Some onboarding paths still simulate answer-engine visibility.
- Missing provider keys can make multi-engine claims inaccurate.
- Cost and token accounting are incomplete outside the provider-runner path.

Recommendation:

- All AI work should go through one orchestrator.
- Every AI result should have provenance.
- Every score should be labeled measured, inferred, estimated, or unavailable.
- Marketing and UI claims should reflect configured providers and actual executed checks.

## 14. Observability, Reliability, And Jobs

What exists:

- Console/debug logging.
- Hangfire with Postgres storage.
- Staggered recurring jobs.
- Background jobs for visibility, citation, competitor, alerts, opportunities, recommendations, and onboarding.
- Some audit log schema.

Risks:

- No clear distributed tracing.
- No error tracking service integration was observed.
- No durable AI cost dashboard.
- Recurring jobs pull broad organization sets and need paging/leases as usage grows.
- First-run prompt jobs are intentionally not idempotent.
- Alert webhook configuration exists without webhook delivery.

Recommended improvements:

- Add OpenTelemetry traces and metrics.
- Add structured request IDs across frontend/backend/jobs.
- Add job idempotency keys.
- Add per-org job leases.
- Add AI cost/usage dashboards.
- Add alert delivery logs by channel.

## 15. Testing Assessment

Observed backend test coverage appears thin, focused around grade calculation and GEO dashboard aggregation. I did not find evidence of broad coverage for:

- Tenant isolation.
- SCIM.
- Billing webhooks.
- Entitlement races.
- SSRF protections.
- Admin destructive flows.
- AI provider execution.
- Prompt provenance.
- Frontend auth state.
- End-to-end onboarding and billing flows.

Recommended pre-launch test plan:

- Add security regression tests for every tenant-scoped endpoint.
- Add SCIM cross-tenant conflict tests.
- Add Stripe webhook replay/idempotency tests.
- Add crawler URL validation tests.
- Add AI budget/quota concurrency tests.
- Add Playwright e2e tests for signup, onboarding, dashboard, billing, and settings.

## 16. Scalability Forecast

### 100 Users

The product can likely function technically, but support confusion from preview/mock pages, AI cost surprises, and billing readiness will be painful.

### 1,000 Users

Prompt data volume, dashboard reads, scraping jobs, and recurring scans will need pagination, indexes, and better queue controls.

### 10,000 Users

Postgres plus Hangfire can still work, but only with paging, job partitioning, distributed usage controls, provider quota management, and stronger observability.

### 100,000 Users

The current architecture would need significant evolution: read replicas, partitioning, workload isolation, distributed rate limiting, stronger data pipelines, and possibly separate worker pools by workload type.

## 17. Founder/Product Owner Verdict

I would not launch paid self-serve today.

I would launch a tightly controlled beta with 3-5 design partners after fixing:

- SCIM tenant isolation.
- SSRF protections.
- Billing URL validation and webhook idempotency.
- Atomic quota enforcement.
- Mock/preview UX leakage.
- Central AI provenance and cost accounting.

Do not build microservices, Kubernetes, Kafka, service mesh, event sourcing, custom billing ledgers, active-active multi-region, or a proprietary large-scale crawler yet.

The highest-leverage path is to keep the modular monolith, make the current workflows trustworthy, and make every customer-facing AI insight auditable.

## Final Recommendation

Citationly has a real product center of gravity. The core concept is commercially credible, and the codebase has moved meaningfully toward SaaS maturity.

But paid launch requires trust: tenant isolation, secure crawling, reliable billing, measured AI evidence, accurate usage enforcement, and removal of mock-product surfaces from the paid experience.

Current status: **not paid-customer ready, but viable for a focused beta after high-priority hardening**.
