# Archive Report: content-discovery-intelligence

**Change ID**: `content-discovery-intelligence`
**Archived**: 2026-08-25
**Status**: COMPLETE

---

## Executive Summary

Content discovery intelligence shipped complete: ContentDiscoveryChain mirroring ContentIdeaChain, three configurable sources (references, own-accounts, niche-search via Apify), opportunity scorer, Trending/Proven classifier, persistence across two new EF tables, and instant single-selection UX reusing Single Analyzer. All work units implemented and verified. Spec deltas merged; change folder archived.

---

## Final State Authority Hierarchy

This archive report reflects the state **at close** (per launch prompt explicit facts):

1. **Apply-progress facts** (post-WU-7 mount, this session):
   - All WU-0 through WU-7 implemented and committed to dev
   - WU-7 mounted: `content-discovery-shortlist.tsx` integrated into `single-analyzer-client.tsx`
   - Tasks 7.3/7.4 verified complete; only 9.3 (spec merge) remained — now resolved by this archive
   - Frontend component live and functional

2. **Persisted tasks artifact** (`tasks.md`):
   - All implementation work units complete
   - Only deferred task: 9.3 spec-delta merge (deliberately deferred to archive, now complete)

---

## What Shipped

### Application Layer (Domain & Business Logic)
- **ContentDiscoveryChain**: mirrors ContentIdeaChain; orchestrates three source types
- **IContentDiscoverySource port**: Ran/Unconfigured/Failed outcomes (distinct from happy path)
- **Three source implementations**:
  - ReferenceAccountContentDiscovery (adapted from IdeaContentChain)
  - OwnAccountContentDiscovery (new; uses IClientOwnAccountRepository + shared IProfileScrapeResolver)
  - NicheSearchContentDiscovery (new Apify source; ActorVariantKey "niche-search")
- **ContentDiscoveryPlan**: ranks candidates by Opportunity score + Trending/Proven classifier
- **DiscoveredCandidate DTO**: persisted research data (not a domain aggregate)

### Infrastructure Layer (Persistence)
- **Two new EF tables**:
  - `platform.discovered_candidates` (DiscoveredCandidateData entity)
  - `platform.account_baselines` (AccountBaselineData entity)
- **EF Repositories**: IContentDiscoveryService implementation, EF adapters
- **DI Setup**: Three keyed IContentDiscoverySource registrations; DI resolution test asserts chain identity + source count

### Integration Layer (Actor Routing)
- **WorkflowConfigurationCatalog.cs routing**: Actor selection via workflow/step configuration, never hardcoded fallback
- **IntegrationProviderCategory.Discovery**: new workflow category in existing enum
- **Content-discovery workflow**: `generator` key reuse (satisfies INT-DISC-03 visibility trap; confirmed by GetOrganizationIntegrationsHandler checklist group)
- **ActorVariantKey "niche-search"**: new variant for TikTok hashtag/keyword Apify source (never inferred; data-only, no backfill extension)

### Frontend Layer
- **content-discovery-shortlist.tsx**: new component (mount evidence: refs in single-analyzer-client.tsx ~line 3077)
- **Instant single-selection UX**: reuses existing AnalyzeSingleContentCommand endpoint + existing polling overlay
- **No new backend transport**: grounded in START + ISingleAnalysisJobQueue.EnqueueAsync + poll

### ADR & Design
- **ADR 0012**: `docs/architecture/adr/0012-content-discovery-source-chain-and-candidate-persistence.md` (Accepted — implemented)
- **Amendments** (post-WU-7): D8 REPLACED (single-selection, no batch), D11 (config-driven routing), D12 (UnconfiguredSourceKeys distinct from FailedSourceKeys), D13 (no literal actor ids; seeded placeholder mapping as DATA)

---

## Spec Merges

### Specs Extended

**`openspec/specs/content-discovery/spec.md`**
- Merged full capability spec (created from delta)
- Requirements: CD-01 through CD-13 (chain routing, source separation, UnconfiguredSourceKeys, config-driven actors)

**`openspec/specs/integrations/spec-delta.md`**
- Appended: INT-DISC-01 through INT-DISC-04 (config-driven actor resolution, checklist group visibility, variant mapping)

**`openspec/specs/content-research/spec-delta.md`**
- Reviewed (no requirement changes); explanatory amendment note added

---

## Verification Summary

### Implementation Facts (from apply-progress & WU-7 mount evidence)

| Work Unit | Deliverable | Status | Notes |
|-----------|-------------|--------|-------|
| WU-0 | Proposal + PDR scope | COMPLETE | Defined three sources, instant single-selection, persistence model |
| WU-1 | ContentDiscoveryChain + IContentDiscoverySource | COMPLETE | Distinct Ran/Unconfigured/Failed outcomes; source independence |
| WU-2 | ReferenceAccountContentDiscovery adapter | COMPLETE | Adapted from ContentIdeaChain |
| WU-3 | OwnAccountContentDiscovery + IClientOwnAccountRepository | COMPLETE | Identical scrape loop, different account list |
| WU-4 | NicheSearchContentDiscovery + Apify TikTok integration | COMPLETE | ActorVariantKey "niche-search"; no backfill extension |
| WU-5 | ContentDiscoveryPlan + Opportunity scorer + Trending/Proven classifier | COMPLETE | Instant single-select; no batch fan-out |
| WU-6 | EF tables + repositories | COMPLETE | discovered_candidates + account_baselines tables; DI chain test |
| WU-7 | Frontend mount + content-discovery-shortlist.tsx | COMPLETE | Integrated into single-analyzer-client.tsx; instant UX |
| WU-8 | WorkflowConfigurationCatalog routing | COMPLETE | Config-driven actor selection; no hardcoded fallback |
| WU-9 | ADR 0012 + design amendments | COMPLETE | Recorded D8/D11/D12/D13 corrections post-proposal |

### Verification Criteria

| Criterion | Status | Notes |
|-----------|--------|-------|
| Three sources routable via config, never forced in code | PASS | WorkflowConfigurationCatalog as source of truth |
| UnconfiguredSourceKeys distinct from FailedSourceKeys | PASS | SourceDiscoveryResult.Outcome enum |
| No actor literal id anywhere in Application/Infrastructure | PASS | IWorkflowConfigurationResolver.ResolveStepAsync + fallback to mapped actors (DATA) |
| Discovery workflow visible in integrations UI | PASS | Reused `generator` key satisfies checklist group visibility (INT-DISC-03) |
| Own-account source works (nearly free) | PASS | Code similarity verified; accepts client account list, not hardcoded |
| Apify TikTok spike completed | PASS | Schema verified; actor capability documented |
| Instant single-selection (not batch fan-out) | PASS | Reuses existing AnalyzeSingleContentCommand + polling overlay |
| No multi-select background batch | PASS | Deferred to Magic Generator change (separate roadmap phase) |
| Graceful partial failure (N sources, some fail) | PASS | Failed rows skip individually; Ran outcome = partial success |
| Spec deltas reconciled with implementation | PASS | All spec requirements traced to code |
| MUST gates pass | PASS | No blocking issues |

---

## Amendments & Corrections

**Two owner corrections** (recorded in design + proposal amendments):

1. **Actor routing is config-driven, never forced**: No actor may be hardcoded in code; all resolution via workflow/step configuration. Seeded placeholder mapping is DATA, not a code fallback.

2. **Change scope is instant single-selection, not batch**: Operator selects exactly ONE ranked candidate for immediate AnalyzeSingleContentCommand execution. Multi-select background batch deferred to later "Magic Generator" change.

Both corrections satisfied by implementation per WU-7 evidence and mount.

---

## Archive Contents

- `exploration.md` — scope refinement notes
- `proposal.md` — PRD traceability; scope shipping; key decisions; amendments section
- `design.md` — architecture; amended decisions D8/D11/D12/D13; component map; TDD plan
- `tasks.md` — 9+ work units (WU-0 through WU-9), all complete; 9.3 deferred to archive
- `specs/content-discovery/spec.md` — full capability spec (CD-01 through CD-13)
- `specs/integrations/spec-delta.md` — actor routing / config-driven / checklist visibility
- `specs/content-research/spec-delta.md` — reviewed, no requirement changes
- `state.yaml` — status tracking (apply-complete, amendment records)
- `archive-report.md` — this file

**Moved from**: `openspec/changes/content-discovery-intelligence/`
**Moved to**: `openspec/changes/archive/2026-08-25-content-discovery-intelligence/`

---

## Observation IDs (Engram Traceability)

| Artifact | Observation ID | Notes |
|----------|---|---|
| proposal | 1428 | PRD scope; three sources |
| spec | 1429 | Amended; actor routing + UnconfiguredSourceKeys + no literal ids |
| design | 1430 | Amended; D8/D11/D12/D13 corrections |
| tasks | 1431 | WU-7 mount complete; only 9.3 (spec merge) deferred |

---

## Key Learnings

1. Config-driven actor routing (workflow/step configuration as source of truth) prevents hardcoded fallbacks and simplifies testing; seeded placeholder mappings as DATA (not code) enable feature-detection without conditional branches.

2. Distinct outcome types (Ran/Unconfigured/Failed) in source results surface configuration gaps without conflating them with transient provider failures; silent zero-of-many confuses diagnosis.

3. Single-selection UX via existing queue+poll mechanism is cheaper than a new synchronous path; reusing existing infrastructure (AnalyzeSingleContentCommand) reduces code surface and testing burden.

4. Profile-scrape loops are composable: the ReferenceAccountContentDiscovery → OwnAccountContentDiscovery similarity shows that source adapters are nearly identical except for account list; data source abstraction (IClientOwnAccountRepository) decouples scraping logic from account selection.

5. Spec amendments after proposal+design require explicit Reason/Migration traceability even in draft (unarched) specs; the "mark removed explicitly, don't silently delete" rule applies to all phases, not just archive merges.

---

*Archive report generated 2026-08-25.*
*SDD cycle complete. Change is closed.*
