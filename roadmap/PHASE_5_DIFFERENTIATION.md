# Phase 5 — Differentiation

**Objective:** Build the capabilities that would make Citationly genuinely better than Profound/Peec/Otterly rather than a copy of them — specifically the two the audit identifies as unsolved industry-wide: recommendation impact tracking, and evidence-grounded fact accuracy monitoring.

**Depends on:** Phase 3 (real scoring, real prompt graph) and Phase 4 (evidence-linked recommendations) — you cannot measure the impact of a recommendation until recommendations carry real baselines and real specificity.

---

## Workstream A — Recommendation impact tracking

### A1. Build the baseline → monitoring window → impact pipeline
- **Problem:** `EstimatedVisibilityGain` is written once at recommendation-creation time and never read back or compared against actual subsequent scores. No baseline/monitoring-window/impact-score concept exists anywhere in the codebase. The product cannot currently demonstrate that any recommendation it has ever given a customer worked.
- **Evidence:** Audit §13 (confirmed via exhaustive grep — only INSERT/model references to `EstimatedVisibilityGain` exist, never a read-back comparison).
- **Solution:** Add a `RecommendationImplementations` table: recommendation_id, marked-implemented-at (customer or system flagged), baseline scores (snapshot of the real Phase 3 scores at implementation time), monitoring window length, and a scheduled job that re-measures the same real scores at window-end and computes an actual delta. Surface "Did this work?" as a real, evidence-backed answer per recommendation — this only has integrity once Phase 3's scores are real, which is why this phase is sequenced after it.
- **Affected:** New table, new background job, `RoadmapService.cs`/recommendation UI to add an "I implemented this" action.
- **Complexity:** L
- **Priority:** P1 (highest-value item in this phase)

### A2. Surface impact history back into future recommendation prioritization
- **Problem:** Even once A1 exists, nothing feeds learned impact data back into how future recommendations are prioritized or estimated.
- **Evidence:** N/A — net-new capability building on A1.
- **Solution:** Use accumulated real impact data (once enough exists) to replace the category-keyed `EstimatedImpact` guesses from Phase 4's B2 with actual historical-average impact for that recommendation type, per industry/vertical where sample size allows.
- **Affected:** `RoadmapService.cs`, recommendation-scoring logic.
- **Complexity:** M
- **Priority:** P3 (needs A1's data to accumulate first — early phase, not immediately actionable)

---

## Workstream B — AI Brand Knowledge / fact accuracy

### B1. Extract structured brand claims from real AI responses
- **Problem:** No system currently detects what AI engines actually believe/claim about the customer's brand (pricing, features, location, founders, etc.) versus what's true.
- **Evidence:** Audit product-vision §14 (aspirational, confirmed not implemented anywhere in the codebase).
- **Solution:** Add a claim-extraction pass over real stored `PromptResponse` text (once Phase 2's real multi-vendor evidence store exists) — structured extraction of factual claims (Product, Feature, Pricing, Location, Founder, Capability) with the same evidence-linkage discipline as citation extraction (link back to the exact response/prompt/timestamp that produced the claim).
- **Affected:** New `Citationly.Application/Features/BrandKnowledge/` module.
- **Complexity:** L
- **Priority:** P2

### B2. Compare extracted claims against verified company data
- **Problem:** No ground-truth comparison exists.
- **Evidence:** N/A — net-new capability.
- **Solution:** Let the customer (or the onboarding-derived `WebsiteProfile` from real crawled content, per Phase 0's provenance fixes) supply verified facts; diff against B1's extracted claims to surface incorrect/outdated/contradictory AI statements as a real, evidence-backed "AI Fact Accuracy Monitor" — a distinct feature from the current fabricated "Authority"/"Opportunity" scores this audit flagged.
- **Affected:** `BrandKnowledge` module, `WebsiteProfile` data from onboarding.
- **Complexity:** L
- **Priority:** P2

---

## Workstream C — Cross-engine consensus

### C1. Cross-engine agreement/disagreement analysis
- **Problem:** This capability is fundamentally impossible today because there is only one underlying model behind all "engines" (Phase 2's core finding). It only becomes meaningful once Phase 2's A0 decision results in real independent providers.
- **Evidence:** Audit §9.
- **Solution:** Once real multi-vendor data exists (Phase 2), build comparison logic: does ChatGPT vs. Claude vs. Gemini agree on category leaders, brand capabilities, or citations for the same prompt? Surface disagreement as a genuinely novel insight ("Claude and Perplexity both cite Competitor X for this prompt; ChatGPT doesn't mention them at all").
- **Affected:** New analysis service over Phase 2's evidence store.
- **Complexity:** M
- **Priority:** P2 (blocked entirely until Phase 2 A0 delivers real multi-vendor data — do not attempt this against simulated data)

---

## Definition of Done for Phase 5

- [ ] At least one full recommendation → implementation → measured-impact cycle has run end-to-end against real Phase 3 scores.
- [ ] Brand claims are extracted from real AI responses with full evidence linkage, and compared against verified company facts.
- [ ] Cross-engine consensus analysis exists and is gated on Phase 2 actually delivering independent providers (not run against single-provider simulated data).
