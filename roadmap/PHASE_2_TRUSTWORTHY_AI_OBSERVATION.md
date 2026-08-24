# Phase 2 — Trustworthy AI Observation

**Objective:** Replace the single-provider persona-simulation with either real independent multi-vendor querying, or — if budget/timeline forces a staged approach — an honestly-labeled interim state. Build the evidence store and cost-governance infrastructure that every later phase depends on. This is the phase that fixes the audit's single most important finding.

**Depends on:** Phase 0 (fabrication removed), Phase 1 (usage metering/entitlements exist, since this phase will materially increase AI spend).

---

## Workstream A — Decide and implement the provider strategy

### A0. Make the call: real multi-vendor now, or honest single-provider now with a funded path to multi-vendor
- **Problem:** Today "ChatGPT," "Claude," and "Gemini" are all `gpt-4o-mini` with a different persona instruction — `LLMRunnerService.cs:13-18` says this outright in its own comment.
- **Evidence:** Audit §9 in full.
- **Decision needed before A1 starts:** Either (a) budget for and integrate real Anthropic + Google (+ Perplexity for search-grounded results) APIs, or (b) ship an interim state where the product is honestly relabeled — e.g., "Simulated AI Search Response (via GPT-4o-mini)" instead of a bare "Claude" label — while multi-vendor integration is scheduled. **Do not ship Phase 2 without picking one of these; silently continuing the current mislabeling is not an option that survives this roadmap.**
- **Complexity:** — (decision, not code)
- **Priority:** P0

### A1. Build a real provider abstraction layer
- **Problem:** `OpenAiService.cs`/`LLMRunnerService.cs` hardcode one vendor's endpoint and model directly into business logic — there's no seam to add a second provider even once budget is approved.
- **Evidence:** `LLMRunnerService.cs:77-89`, `OpenAiService.cs:102`.
- **Solution:** Define `IAiProvider` (methods: `ExecuteChatAsync`, capability flags for search/browsing support, model identifier) with one implementation per real vendor selected in A0 (`OpenAiProvider`, `AnthropicProvider`, `GoogleProvider`, `PerplexityProvider` as applicable). `LLMRunnerService` becomes a thin dispatcher over a configured provider list — no more "acting as X" persona prompts for vendors that are actually being called for real.
- **Affected:** New `Citationly.Infrastructure/AiProviders/` module, `LLMRunnerService.cs` rewritten, `DependencyInjection.cs`.
- **Complexity:** L (per additional real vendor) / M (for the interim honest-relabeling path alone)
- **Priority:** P0

### A2. Enable web-search/browsing grounding
- **Problem:** No call anywhere sets a `web_search`/`tools`/`browsing` parameter — every response is a parametric-knowledge guess, not an observation of what a live AI search surface actually shows.
- **Evidence:** Audit §9 (confirmed via exhaustive grep across the AI call surface).
- **Solution:** For providers that support it (OpenAI's browsing tool, Perplexity's native search, Google's search grounding), enable it explicitly and capture whether a given observation was search-grounded or not — this becomes a stored field (see B1), not an assumption.
- **Affected:** `IAiProvider` implementations from A1.
- **Complexity:** M
- **Priority:** P0

### A3. Remove the "stay close to the previous score" prompt instruction
- **Problem:** `RunScanCommand.cs:204-212` explicitly instructs the LLM to keep new scores "realistically close to" the previous (also invented) scan — manufacturing the appearance of a smooth trend with no real re-measurement behind it.
- **Evidence:** `RunScanCommand.cs:212`.
- **Solution:** Remove this instruction entirely as part of the Phase 3 scoring rework (tracked there), but flag it now since it's directly caused by the single-call-invents-everything pattern this phase is dismantling.
- **Affected:** Tracked for removal in Phase 3, cross-referenced here.
- **Complexity:** S
- **Priority:** P1

---

## Workstream B — Evidence store

### B1. Add provider/model/cost/grounding attribution to every stored AI output
- **Problem:** `PromptResponse` has no `ModelUsed`/`ProviderId` field — even the best-provenance-chain metric in the product (citations) can't be audited after the fact to confirm which model actually produced a "Claude" response. No token/cost tracking exists anywhere; `OpenAiService.GenerateContentAsync` discards the `usage` object entirely.
- **Evidence:** `PromptModels.cs:32-42` (missing fields), `OpenAiService.cs:88-95` (usage discarded).
- **Solution:** Add `ProviderId`, `ModelId`, `PromptTokens`, `CompletionTokens`, `CostUsd`, `WasSearchGrounded` columns to `PromptResponse` and every scan/analysis table that stores an AI output. Capture the `usage` object on every call instead of discarding it.
- **Affected:** `PromptModels.cs`, all AI-calling services, DB migration.
- **Complexity:** M
- **Priority:** P0

### B2. Enforce append-only, immutable observation storage
- **Problem:** Storage is already mostly insert-only for raw response text (a genuine strength) — but no schema-level constraint enforces this, and enrichment paths (e.g., sentiment column updates) should be reviewed to ensure they never touch the raw evidence fields.
- **Evidence:** Audit §6, §14; `PromptIntelligenceRepository.cs:123-130` (insert-only, verified) vs. `:320-324` (sentiment-only update, acceptable).
- **Solution:** Add a DB-level trigger or application-level guard preventing `UPDATE`/`DELETE` on the raw `ResponseText`/evidence columns of observation tables. Document which columns are mutable (classification/enrichment) vs. immutable (raw evidence) per table.
- **Affected:** `PromptResponses`, `HistoricalScans`, `PromptCitations` table definitions.
- **Complexity:** M
- **Priority:** P1

---

## Workstream C — Cost governance

### C1. Real resilience layer
- **Problem:** No Polly/circuit-breaker framework exists; the only retry logic is a manual one-shot retry applied uniformly with no per-tenant awareness.
- **Evidence:** Audit §9, §17.
- **Solution:** Build on Phase 0's stopgap (A3 there) into a real Polly policy set: exponential backoff respecting `Retry-After` headers, circuit breaker per provider, and provider fallback ordering (e.g., if Anthropic is down, does the system degrade gracefully or fail the observation outright and say so — never silently substitute a different provider's answer under the original vendor's label).
- **Affected:** `Citationly.Infrastructure/AiProviders/`, `Citationly.Infrastructure/Resilience/`.
- **Complexity:** M
- **Priority:** P1

### C2. Per-tenant AI budget enforcement tied to Phase 1's entitlement service
- **Problem:** Phase 1 added the metering scaffolding; this phase is what actually has meaningful cost to meter, since multi-vendor calls (if A0 chooses that path) multiply spend by the number of vendors.
- **Evidence:** Audit §17 (cost architecture scenario: 1,000 customers × 100 prompts × 5 "engines" × daily = 500k observations/day).
- **Solution:** Wire every new `IAiProvider` call through Phase 1's `CheckQuota()`/`ConsumeUsage()`. Add a hard per-tenant daily/monthly spend ceiling, not just a call-count ceiling, since different providers/models have different costs.
- **Affected:** `IAiProvider` implementations, Phase 1's `IEntitlementService`.
- **Complexity:** M
- **Priority:** P0

---

## Database changes required (Phase 2)

- `PromptResponses`: add `ProviderId`, `ModelId`, `PromptTokens`, `CompletionTokens`, `CostUsd`, `WasSearchGrounded`
- Equivalent columns on `HistoricalScans` and any other table storing a raw or derived AI output
- New `AiProviderConfig` table or config-driven equivalent (provider name, enabled flag, capability flags)

---

## Definition of Done for Phase 2

- [ ] A0's decision is made and implemented — either real multi-vendor calls exist, or every AI-generated label in the UI honestly discloses "simulated via GPT-4o-mini" until multi-vendor ships.
- [ ] No prompt instructs a model to "respond in the style of" a vendor it isn't actually calling.
- [ ] Every stored AI output records which provider/model produced it and what it cost.
- [ ] No AI call is made without going through the resilience layer and the Phase 1 entitlement/quota check.
- [ ] Raw evidence fields are immutable at the schema/application level.
