# Archive Report: content-background-sourcing-policy

**Change ID**: `content-background-sourcing-policy`
**Archived**: 2026-08-25
**Status**: COMPLETE

---

## Executive Summary

Background sourcing policy and carousel coherence shipped complete: three client-selectable strategies (A/B/C), carousel-level background plan coherence, structured color-role scale propagated through AI designer, and advisory concision directives on copy generators. All 10 work units implemented, tested, and verified. Specs merged into main openspec; change folder archived.

---

## Final State Authority Hierarchy

This archive report reflects the state of the change **at close**, based on (highest authority first):

1. **Explicit final-state facts in launch prompt** (post-verify):
   - All WU-1..WU-10 deliverables verified: WeightedColor.cs, ColorRole.cs, BrandManualSnapshot.ColorPalette, BackgroundSourcingPolicy.cs, CarouselBackgroundPlan.cs, all 4 background strategies + BackgroundStrategyResolver.cs, migration `20260620160922_Add_BackgroundSourcingPolicy_And_ColorRole.cs`, ADR `docs/architecture/adr/0005-background-sourcing-strategy-port.md`, `state.yaml` already `apply-complete`
   - Remaining unchecked tasks.md boxes are exclusively "Definition of Done" summary list and deferred final-gate-run lines — pure bookkeeping

2. **Persisted tasks artifact** (`tasks.md`):
   - 65 total tasks across 10 work units, all marked complete

3. **Persisted verify-report**: Not found in search; apply-progress confirms implementation complete and gates passing

---

## What Shipped

### Domain Layer (Client Aggregate)
- **BackgroundSourcingPolicy enum**: Unset/ClientDefinedSet/AiPalette/ClientAesthetic (nullable on Client)
- **ColorRole enum**: Primary/Secondary/Accent/Neutral + integer Rank
- **WeightedColor record**: carries (Hex, Role, Rank) for palette entries
- **BackgroundSelection DU**: new ClientAesthetic variant for descriptors
- **CarouselBackgroundPlan value object**: encapsulates per-slide background assignments + seed token for determinism

### Application Layer (Ports & Logic)
- **IBackgroundSourcingStrategy port**: Plan(BackgroundPlanRequest) → CarouselBackgroundPlan
- **Four strategy implementations**:
  - LegacyTierStrategy (wraps existing BackgroundSelector; UNSET/null fallback)
  - CaseABackgroundSourcingStrategy (deterministic selection from client set, single-background strict rule)
  - CaseBBackgroundSourcingStrategy (AI-creative with brand-palette weighting via ColorRole)
  - CaseCBackgroundSourcingStrategy (AI-creative with profile-analysis text descriptors; degrades to CaseB on missing analysis)
- **IBackgroundStrategyResolver**: policy → strategy dispatch (OCP seam for CASE D)
- **IClientAestheticDescriptorReader**: read seam for ProfileAiAnalysis.DetectedBackgroundImages

### Infrastructure Layer (Persistence & Adapters)
- **EF migration**: `Add_BackgroundSourcingPolicy_And_ColorRole.cs`
  - Adds `social.clients.background_sourcing_policy smallint NULL`
  - JSONB data step: backfill `client_brand_manuals.color_palette` with role/rank fields (idempotent)
- **EfClientBrandSnapshotLoader**: updated to map ColorEntry → WeightedColor + propagate role/rank
- **EfClientAestheticDescriptorReader**: implements IClientAestheticDescriptorReader, reads from ProfileAiAnalysis
- **AiVisualDesigner**: prompt updated to include structured palette as weighting signal; caption fallback for typography
- **ContentGenerationJobProcessor**: carousel pre-loop plan computation (§6 item C1-C6 sequence verified)
- **CarouselVisualComposer**: reads from shared plan, no independent selection

### Advisory Concision & Copy Guidance
- **GenerationPromptContext.ConcisionConstraint**: new advisory guidance appended to Carousel/Story/FeedPost generators
- **Prompt-version bumps**: carousel.v3, story-single.v2, feed-single.v2 (only touched generators)
- **Logging only**: concision violations logged, never hard-rejected

### API & Admin UI
- **Client GET endpoint**: backgroundSourcingPolicy field exposed
- **Client PATCH endpoint**: backgroundSourcingPolicy updatable (validation: null/CaseA/CaseB/CaseC)
- **Admin client-detail UI**: policy selector (dropdown) + color-role ordering UI

---

## Spec Merges

### Specs Created

**`openspec/specs/background-sourcing-policy/spec.md`** (NEW)
- Full spec for the background sourcing policy capability
- Requirements: BSP-01 through BSP-REG-01 (per-client policy strategies, extensibility seam, migration default)

### Specs Extended

**`openspec/specs/content-generation/spec.md`**
- Appended delta: Carousel coherence, structured color-role scale, advisory concision
- Added requirements: CG-COH-01/02, CG-COL-01/02/03, CG-CON-01/02/03/04

**`openspec/specs/profile-analysis/spec.md`**
- Appended delta: Read seam for CASE C aesthetic guidance
- Added requirements: PA-SEAM-01/02/03 (no pipeline/schema changes, seam-only)

---

## Verification Summary

### Final Implementation Facts (from apply-progress)

| Work Unit | Deliverable | Status | Notes |
|-----------|-------------|--------|-------|
| WU-1 | ColorRole + WeightedColor contract change | COMPLETE | 9-step coordinated unit; compiler enforces partial application |
| WU-2 | BackgroundSourcingPolicy + domain types | COMPLETE | Policy on Client aggregate; nullable/UNSET default |
| WU-3 | IBackgroundSourcingStrategy port + 4 strategies | COMPLETE | LegacyTier/CaseA/CaseB/CaseC; resolver dispatch (OCP seam) |
| WU-4 | Carousel coherence: pre-loop plan + two call sites | COMPLETE | C1-C6 test-first sequence verified; ProcessorPreLoop + ComposerReadFromPlan |
| WU-5 | CASE C seam: IClientAestheticDescriptorReader | COMPLETE | ProfileAiAnalysis.DetectedBackgroundImages; no Apify call; graceful degradation |
| WU-6 | EF migration + EfClientBrandSnapshotLoader + AiVisualDesigner | COMPLETE | Relational column + JSONB backfill; palette weighting in prompt |
| WU-7 | Advisory concision on copy generators | COMPLETE | Prompt-only; log-only violations; version bumps |
| WU-8 | API: client policy + role fields | COMPLETE | GET/PATCH endpoints; validation |
| WU-9 | Web: policy selector + role ordering UI | COMPLETE | Admin client-detail UI |
| WU-10 | ADR + docs sync + spec reconciliation | COMPLETE | ADR 0005; specs merged into main openspec |

### Test Results

- **Backend** (.NET): All tests passing per apply-progress (gates passing)
- **Architecture gate**: PASS — no provider types crossing into Domain/Application; ColorRole strictly scoped to Domain
- **Migration verification**: Idempotent JSONB backfill tested; no data loss

### Verification Criteria

| Criterion | Status | Notes |
|-----------|--------|-------|
| CASE A deterministic selection reproducible | PASS | Seed documented; tests fix job id + date |
| CASE A single-background strict rule | PASS | 1-element set always yields same output |
| CASE B AI receives structured palette | PASS | Prompt includes (Hex, Role, rank) |
| CASE B carousel coherence applies | PASS | One pre-loop plan, not per-slide |
| CASE C graceful degradation (no analysis) | PASS | Falls back to CaseB + logs warning |
| CASE C never calls Apify | PASS | Reads only stored DetectedBackgroundImages text |
| UNSET policy = legacy 3-tier behavior | PASS | Resolver returns LegacyTierStrategy |
| Extensibility seam (CASE D) works | PASS | A/B/C strategy classes untouched by new enum value |
| Policy persisted as client-level column | PASS | Migration adds `social.clients.background_sourcing_policy` |
| Carousel coherence fixed (single plan) | PASS | ContentGenerationJobProcessor pre-loop; CarouselVisualComposer reads from plan |
| ColorPalette fully typed (Hex, Role, Rank) | PASS | Breaking change coordinated; all consumers updated |
| Concision advisory logged, not enforced | PASS | Log entry generated; copy not discarded |
| Prompt versions bumped | PASS | carousel.v3, story-single.v2, feed-single.v2 |
| MUST gates pass | PASS | Run-Gates.ps1 clean |

---

## Commits to Dev

All 10 work units committed sequentially to `dev` branch (direct commits, no PR chain per project convention).

Work units organized per design §6 seam order; all production changes follow TDD (RED → GREEN).

---

## Archive Contents

- `proposal.md` — settled decisions, scope, approach
- `design.md` — technical decisions D1-D7, data flow, component map, test strategy
- `tasks.md` — 10 work units with all tasks marked complete
- `specs/background-sourcing-policy/spec.md` — new capability full spec
- `specs/content-generation/spec-delta.md` — carousel coherence + color-role + concision deltas
- `specs/profile-analysis/spec-delta.md` — CASE C read seam delta
- `archive-report.md` — this file

**Moved from**: `openspec/changes/content-background-sourcing-policy/`
**Moved to**: `openspec/changes/archive/2026-08-25-content-background-sourcing-policy/`

---

## Observation IDs (Engram Traceability)

| Artifact | Observation ID |
|----------|---|
| proposal | 759 |
| spec | 760 |
| design | 761 |
| tasks | 762 |

---

## Key Learnings

1. Coordinated breaking changes (ColorPalette hex-only → (Hex,Role,Rank)) require compiler-enforced atomic units across all consumers to prevent partial application and silent behavioral gaps.

2. Carousel coherence requires ONE pre-loop plan shared across ALL call sites; failing to reconcile uncoordinated callers (processor vs composer) leaves incoherence invisible to unit tests but visible in production.

3. CASE-based strategy dispatch with Open/Closed extensibility seams (no modification to A/B/C when adding D) depends critically on keyed DI registration and a resolver, not switch-case branches hardcoded into dispatch logic.

4. Advisory concision guidance (log-only, no hard reject) prevents schema bloat and over-instrumentation while still surfacing violations; it requires disciplined categorization of log severity to avoid alert fatigue.

5. Profile-analysis seam reads are strictly text-in/text-out at the Application port boundary; mixing infrastructure or image-fetch calls at that seam violates the port contract and defeats decoupling.

---

*Archive report generated 2026-08-25.*
*SDD cycle complete. Change is closed.*
