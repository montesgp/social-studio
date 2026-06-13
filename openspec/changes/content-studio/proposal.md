# Proposal: Content Studio

## Change ID
`content-studio`

## Status
Proposal — defines the full Content Studio capability and scopes the first incremental slice (Single Analyzer integration). Design: `design.md`. ADR: `docs/architecture/adr/0004-content-studio-review-model.md`.

## Intent

Add the missing **post-generation stage** to the social content lifecycle: a surface where AI-generated deliverables are **reviewed, edited, validated, approved, and prepared for publication** before they are scheduled or published.

Today the pipeline ends when content is generated. `Single Analyzer` runs `Analyze → Generate → Deliver` and produces an **immutable** `GeneratedPackage` (per analysis × client) with durably-stored per-format results (flyer/carousel PPTX, reel-script Markdown, captions, hooks, ideas). There is no place to inspect a piece, fix a caption, swap a background, approve it, reject it, or stage it for publishing. The PRD lists this gap explicitly as two open questions: *"Whether creative preview is built internally or delegated to Canva first"* and *"Exact approval workflow for clients"* (PRD §7).

Content Studio closes that gap as a **source-agnostic** capability: it consumes the output of *any* generator, not just Single Analyzer. It is designed from day one to scale from today's single-item generation to tomorrow's bulk generation (**Magic Generator** and others) **without a redesign**, by modeling the atomic reviewable unit correctly and treating bulk as set operations over that unit.

## Problem

1. **Generated content is a dead end.** `GeneratedPackage` is generate-once and immutable (correct, for traceability/cost/history — CP-05, ADR 0002). Nothing derives an editable, approvable working surface from it.
2. **No review/approval lifecycle exists.** There is no `Draft → InReview → Approved/Rejected` concept for a content piece; the only status precedent is `Provenance.Status` (audit-only) and the generation job/analysis statuses (operational, not editorial).
3. **No client-facing preview or approval workflow.** PRD §7 leaves both open. The active `generate-social-content-workflow` change *sketches* an "Adaptive Emulator Preview" (reel/story/carousel/feed) and per-content actions inside a generation wizard — but as wizard steps, not a reusable post-generation review surface. Left unconsolidated, preview logic will be duplicated.
4. **No handoff contract to publishing.** `publishing` and `publication-pipeline` specs are stubs (TBD). There is no defined boundary describing *what an approved piece carries* when it crosses into scheduling/publication.
5. **Future bulk generation will overwhelm a single-item mental model.** Magic Generator will emit many pieces per run (multiple carousels/stories/reels/scripts for a campaign or period). A review surface coupled to "one analysis → one package" cannot absorb that without rework.

## Goals

- Introduce a **`ContentStudio` bounded context** (new first-class aggregate, own namespace, own tables, own migrations) that is **isolated** from `SingleAnalyzer` and never mutates `ContentGeneration` internals — mirroring the `ProfileAnalysis` (Mundo 2) isolation discipline (PA-REG-01).
- Model the atomic reviewable unit (`ContentItem`) and its **editorial lifecycle** (`ReviewState`) as pure domain, reusing the existing status-transition pattern (`ProfileAnalysisStatusTransitions`).
- **Derive** editable items from immutable generation output (per-format results) by **soft reference only** — preserving the immutability/traceability of `GeneratedPackage` exactly as `ContentPlan` references packages without owning them.
- Make individual and bulk operations **the same operation at different cardinality** via a `ContentSet` grouping abstraction, so Single Analyzer (small set) and Magic Generator (large set/campaign) share one model.
- Reuse the existing rendering and brand-guardrail stack (`IVisualDesigner`, `IVisualGuardrailValidator`, `IContentRenderer`, `BackgroundSelection`/`BackgroundTier`) for edits — practical adjustment, **not a Canva clone**.
- Define the **outbound handoff contract** to publication (event + port + payload + terminal state) without building or coupling to the publication pipeline.
- Integrate the first slice into the existing **Single Analyzer** flow incrementally and compatibly (additive surface, like the Package tab — the existing flow stays untouched).

## Scope

### In Scope (Phase 1 — Foundation on Single Analyzer)

- **Domain context `Domain/Social/ContentStudio/`**: `ContentItem` aggregate, `ContentItemId`, `ContentOrigin` VO (source-agnostic generator reference), `ContentKind` VO (format key + medium classification), `ContentDraft` (editable working payload derived from the source format result), `ReviewState` enum + `ReviewStateTransitions`, `EditTrace` VO, `VariantGroupId`; `ContentSet` aggregate (`SetKind.GenerationRun`) with roll-up `ReviewSummary`.
- **Materialization bridge**: build `ContentItem`s for a completed Single Analyzer generation run from the existing durable per-format results (`IContentGenerationFormatResultRepository` / format assets), grouped into one `ContentSet`. Read-only consumption of generation output; no generation change.
- **Review + preview**: list and view items and sets; a normalized **preview projection** contract (medium + target channel + renderable payload + channel constraints) that drives a presentation-only frontend emulator (consolidating the emulator sketched in `generate-social-content-workflow`).
- **Text editing**: edit caption / script / markdown-script text on the draft, with brand-safe length/format validation. Source format result is never mutated.
- **Item operations**: approve, reject (with reason), discard, duplicate, delete.
- **Terminal + handoff contract**: `Approved → ReadyForPublication`, plus the outbound `ContentApprovedForPublication` event, `IPublicationHandoff` port, and `PublicationHandoffPayload` (final asset URIs, copy, target network/placement, client/org, provenance). The actual publication-pipeline consumption is **not** built here.
- **Persistence**: additive `social.content_studio_items`, `social.content_studio_sets`, `social.content_studio_set_items` (org-scoped, soft refs, no EF navigations to generation/package).
- **API + Frontend**: Content Studio endpoints + a lazy-loaded **Studio surface in Single Analyzer** (additive tab; existing flow unchanged).

### Out of Scope (later phases — roadmapped, not this change)

- **Visual/layout editing** (image replacement, background-tier swap, layout/branding tweaks, re-render through guardrails) — Phase 2.
- **Variants and single-item regenerate** (re-invoke the generator at item granularity) — Phase 2.
- **Bulk operations** (mass approve / reject / regenerate / validate over a set or filtered selection) — Phase 3.
- **ContentPlan linkage** (attach approved items to calendar slots / period) and **client-facing presentations** (slides / navigable / shareable views) with **PPTX/PDF export** (`IPresentationExportAdapter`) — Phase 4.
- **Magic Generator source** and the **`Campaign`** grouping (`SetKind.Campaign`, large-N projections, async bulk) — Phase 5.
- **Publication pipeline implementation** (consuming the handoff) — separate change in the `publishing` capability.
- Building a general-purpose visual editor / Canva replacement (explicit non-goal).

### Scope Boundaries

- Content Studio **owns** the editorial working surface and lifecycle; it **references** generation output and content plans softly (plain Guids, no EF FK navigations), and **never** edits `GeneratedPackage` / format-result internals (immutability boundary).
- Its responsibility **ends** at `ReadyForPublication` + handoff. It does not schedule or publish.
- Provider/model choices for any edit-time AI (variants/regenerate, future) stay behind existing ports; no model is hardcoded.

## Capabilities

### New Capabilities

- `content-studio`: review, edit, validate, approve, and stage AI-generated content for publication, over individual items and grouped sets, source-agnostic across generators.

### Modified Capabilities (additive deltas)

- `content-generation`: expose a **read contract** so a completed run's per-format results can be materialized by Content Studio (additive; no generation behavior change). Delta: `specs/content-generation/spec-delta.md`.
- `content-plan`: define the **soft item→plan association** seam (approved item may reference a `(contentPlanId, period, calendarSlotId)`), enabling the future plan view. Delta: `specs/content-plan/spec-delta.md`. (Plan→item linkage and presentations are Phase 4.)
- `publishing`: define the **inbound handoff contract** the future publication pipeline consumes from Content Studio. Delta: `specs/publishing/spec-delta.md`.

## Approach

A new application feature area `Features/Social/ContentStudio/**` plus the `Domain/Social/ContentStudio/` context. When a Single Analyzer generation run completes, a **materializer** reads the durable per-format results and creates a `ContentSet` (kind `GenerationRun`) of `ContentItem`s — one per format — each carrying a `ContentOrigin` soft reference back to its source run/package/format-result. The `ContentItem` holds an editable `ContentDraft` initialized from (but decoupled from) the immutable source.

The editorial lifecycle is a pure-domain `ReviewState` machine with explicit, table-driven transitions (`ReviewStateTransitions`), exactly like `ProfileAnalysisStatusTransitions`. Operations (approve/reject/discard/duplicate/delete, edit-text) are MediatR command slices that load the aggregate, apply a guarded transition or a draft mutation, and persist via `IContentItemRepository`. A `ContentSet` exposes a roll-up `ReviewSummary` (counts by state) so the UI and future bulk operations read one projection.

**Individual vs. bulk is cardinality, not a different model.** A Single Analyzer run is a small `ContentSet`. Magic Generator (Phase 5) is a large set (or a `Campaign` of sets). Bulk operations (Phase 3) are application-layer fan-outs that resolve a selection (set id or filter) and apply the same per-item command with partial-success semantics — the same way per-format generation already tolerates partial completion.

**Preview** is presentation-only: the backend emits a normalized projection (renderable payload + channel constraints); the frontend renders the destination emulator. This consolidates the emulator currently sketched inside `generate-social-content-workflow` so preview lives in exactly one place.

**Edits reuse the generation stack** (Phase 2+): visual edits mutate the draft's `VisualCompositionSpec` markup and re-render through the existing `IVisualGuardrailValidator` (palette/typography/overflow) + `IContentRenderer`; background swaps reuse `BackgroundSelection`/`BackgroundTier`; image swaps go through `IClientAssetStorage`. No new renderer or designer is introduced.

**Handoff** is an outbound contract: reaching `ReadyForPublication` emits `ContentApprovedForPublication` with a `PublicationHandoffPayload` through `IPublicationHandoff`. The future publication pipeline implements the consumer; Content Studio marks `HandedOff` and stops.

## Domain Model (summary — full model in `design.md`)

- **`ContentItem`** (aggregate root) — atomic reviewable/editable piece. Holds `ContentOrigin` (soft ref: generatorKind, sourceRunId, sourcePackageId?, formatResultId, formatKey), `ContentKind`, `ContentDraft` (text + visualSpec + assetRefs + targetChannel), `ReviewState`, `EditTrace`, `VariantGroupId`/`IsPrimaryVariant`, optional `PlanContext` (soft refs), tenancy (`OrganizationId`, `ClientId`).
- **`ContentSet`** (aggregate root) — named collection of items with `SetKind` (`GenerationRun` now; `Campaign`/`PlanPeriod`/`ClientPresentation`/`AdHocSelection` later), soft origin refs, ordered item refs, roll-up `ReviewSummary`.
- **`ReviewState`** — `Draft → InReview → Edited → Approved → ReadyForPublication → HandedOff`, plus `Rejected` / `Discarded`; transitions guarded by `ReviewStateTransitions`.
- **Reused as-is**: `VisualCompositionSpec`, `BackgroundSelection`/`BackgroundTier`, brand snapshots, the generation rendering/guardrail ports. **Not** reused by inheritance/sharing of `SingleAnalyzer` types (context isolation).

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Coupling Content Studio to Single Analyzer | High | `ContentOrigin` is a source-agnostic soft reference (`GeneratorKind` discriminator + run/result ids). The materializer takes a generic run descriptor; Single Analyzer is just the first `GeneratorKind`. No `SingleAnalyzer` domain type is referenced from `ContentStudio`. |
| Immutability tension (editable items vs. immutable packages) | High | `ContentItem` is a **derived working surface**; the source format result/package is never mutated (CS-10). Mirrors the proven `ContentPlan` ⟶ `GeneratedPackage` soft-reference split (ADR 0002). |
| Scope creep toward a visual editor / Canva | Medium | Edit scope is explicitly "practical adjustment": Phase 1 text-only; Phase 2 image/background/layout strictly through existing brand guardrails + renderer. No freeform canvas. |
| Bulk performance at Magic Generator scale | Medium | Bulk is an application fan-out, not a new model. Paged repositories + read projections + `ReviewSummary` roll-up; async bulk for large N; partial-success semantics reuse the existing per-format pattern. Designed in, built in Phase 3/5. |
| Handoff contract instability (pipeline is a stub) | Medium | Define a **versioned** `PublicationHandoffPayload` + `IPublicationHandoff` port now; the pipeline builds against the contract later. Content Studio owns the outbound shape; the consumer is decoupled. |
| Preview duplication with `generate-social-content-workflow` | Medium | Consolidate: Content Studio owns the preview projection contract; the wizard's emulator becomes a consumer of the same projection. Called out for sequencing. |
| Edit re-render cost / AI-token spend (Phase 2+) | Medium | Visual edits re-render deterministically through the existing renderer (no AI call); variants/regenerate reuse the existing 2-failure lock + usage-event ledger for cost visibility. |
| Provenance VO reuse vs. context isolation | Low | Do not share `ContentPlans.Provenance` across contexts; Content Studio uses a local `EditTrace`. Consolidation to a shared kernel is a noted future option, not a Phase 1 dependency. |

## Dependencies

- **Existing generation output**: durable per-format results + assets (`IContentGenerationFormatResultRepository`, `IContentGenerationFormatAssetRepository`, `IGeneratedAssetStorage`, `IGeneratedPackageRepository`) — consumed read-only.
- **Existing rendering/guardrail stack** (Phase 2+): `IVisualDesigner`, `IVisualGuardrailValidator`, `IContentRenderer`/`IContentRendererWithGeometry`, `BackgroundSelection`/`BackgroundSelector`, `IClientAssetStorage`.
- **Existing context loaders**: `IClientBrandSnapshotLoader` (brand context for edits).
- **Multi-tenancy + integrations**: org-scoped tables (`organization_id`), `IIntegrationResolver` for any edit-time AI routing (future).
- **Usage ledger** (`platform.usage_events`) for variant/regenerate cost capture (Phase 2+).
- **Patterns to mirror**: `ProfileAnalysis` (Mundo 2) context isolation; `ContentPlan` soft-reference + sibling-aggregate; `ProfileAnalysisStatusTransitions` status machine.
- **Coordination**: `generate-social-content-workflow` (preview emulator consolidation) — not a hard build dependency for Phase 1.

## Rollback Plan

Additive at every layer:
- New tables (`social.content_studio_items`, `social.content_studio_sets`, `social.content_studio_set_items`) drop without touching any existing table.
- The materializer reads existing generation output; disabling it leaves generation untouched.
- No `GeneratedPackage`, format-result, or `ContentPlan` schema/behavior changes in Phase 1 (the content-plan delta is an additive seam, not a write-path change).
- The Single Analyzer Studio tab is an additive frontend surface; reverting it restores the prior UI with a single-module rollback.
- The handoff event/port are outbound contracts with no consumer wired in Phase 1; removing them affects nothing downstream.

## Success Criteria

- [ ] Given a completed Single Analyzer generation run for a client, materialization produces a `ContentSet` (kind `GenerationRun`) with one `ContentItem` per generated format, each carrying a valid `ContentOrigin` soft reference; **no** `GeneratedPackage` or format-result row is modified.
- [ ] A user can view an item, see a preview projection for its target channel, edit its caption/script text, and the source format result remains byte-identical.
- [ ] A user can approve / reject (with reason) / discard / duplicate / delete an item; every state change is a guarded `ReviewStateTransitions` move (illegal transitions are rejected as structured domain errors, never silent).
- [ ] Approving an item to `ReadyForPublication` emits `ContentApprovedForPublication` with a complete, versioned `PublicationHandoffPayload`; the item moves to `HandedOff` only via the handoff port (no publishing performed).
- [ ] `ContentStudio` references **no** `SingleAnalyzer` domain type and edits **no** `ContentGeneration` internals (architecture gate green; CS-10, context isolation).
- [ ] All org-scoped Content Studio tables carry `organization_id`; queries are tenant-isolated.
- [ ] All MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`).
- [ ] Strict TDD discipline — every new domain rule, application handler, and transition has tests written before implementation.

## Open Questions (for spec/design)

- **OQ-1** — Materialization trigger: auto-materialize on generation-completion (event-driven) vs. lazy on first Studio open (pull)? (Design leans **lazy pull** for Phase 1: zero coupling to the generation worker; recommendation resolved in design §2.)
- **OQ-2** — `EditTrace` vs. promoting `ContentPlans.Provenance` to a shared kernel. (Design leans **local `EditTrace`** now to preserve context isolation; shared-kernel promotion deferred.)
- **OQ-3** — `ContentSet` membership storage: join table (`content_studio_set_items`) vs. `item_ids` jsonb. (Design leans **join table** for queryability and ordering, mirroring `content_plan_slots`.)
- **OQ-4** — Does an item belong to at most one set, or many? (Design: many soft memberships allowed; a `GenerationRun` set is the canonical "home," but `AdHocSelection`/`ClientPresentation` sets may reference the same item.)
- **OQ-5** — `PublicationHandoffPayload` versioning mechanism: schema-version field on the event vs. a versioned contract type. (Design leans **schema-version field**, mirroring the `CompositionSpecEnvelope` schemaVersion precedent.)
