# ADR 0004 - Content Studio Review Model

## Status
Accepted

## Context

The social content lifecycle ends at generation. `Single Analyzer` runs `Analyze → Generate → Deliver` and persists an **immutable** `GeneratedPackage` (per analysis × client, ADR 0002 / CP-05) whose per-format results are durably stored (`social.content_generation_format_assets`, designer markup persisted per format result). There is no surface to review, edit, validate, approve, or stage those deliverables before publishing, and the `publishing` / `publication-pipeline` capabilities are still stubs. The PRD records the gap as two open questions: *"Whether creative preview is built internally or delegated to Canva first"* and *"Exact approval workflow for clients"* (PRD §7).

Two forces shape the design:

1. **Immutability must be preserved.** `GeneratedPackage` and its format results are the traceable, cost-accounted, generate-once history. An editorial surface must not mutate them — exactly the constraint that led `ContentPlan` to *reference* packages by soft Guid rather than own them (ADR 0002, CP-03/CP-05).
2. **Single-item today, bulk tomorrow.** `Single Analyzer` produces a few deliverables per run. `Magic Generator` (and other future generators) will produce many pieces per run (multiple carousels/stories/reels/scripts for a campaign or period). A review surface modeled around "one analysis → one package" cannot absorb bulk without rework.

The repository already provides the precedent for adding such a surface: `ProfileAnalysis` (Mundo 2) is a new first-class aggregate in its own namespace and tables, isolated from `SingleAnalyzer` (PA-REG-01), with its own status enum + explicit transitions, reusing existing write commands idempotently and a lazy-loaded frontend module. This is an architecture change (`require_adr_for_architecture_changes: true`).

## Decision

Introduce **Content Studio** as a new bounded context that owns an **editable, reviewable working surface derived from immutable generation output**, following the established hexagonal sibling-aggregate + soft-reference patterns.

1. **New bounded context, isolated.** `Domain/Social/ContentStudio/` is a first-class context with its own aggregates, tables, and migrations. It MUST NOT share domain types with `Domain/Social/SingleAnalyzer/` and MUST NOT reference `ContentGeneration` internals beyond soft Guids — mirroring `ProfileAnalysis` isolation (PA-REG-01). (Rejected: extend `content-generation` to make packages editable — destroys the immutability/traceability guarantee and couples editorial concerns to the generation pipeline.)

2. **`ContentItem` is the atomic reviewable aggregate.** It carries an editable `ContentDraft` initialized from — but decoupled from — the source per-format result. The source format result / `GeneratedPackage` is **never** mutated by Content Studio. (Rejected: edit the format result in place — violates CP-05 immutability and erases generation history.)

3. **`ContentOrigin` makes the surface source-agnostic.** Every item references its generator output via a soft `ContentOrigin { GeneratorKind, SourceRunId, SourcePackageId?, FormatResultId, FormatKey }` — plain Guids, no EF FK navigation. `SingleAnalyzer` is simply the first `GeneratorKind`; `MagicGenerator` and others slot in with no model change. (Rejected: a direct FK/navigation to Single Analyzer or to `GeneratedPackage` — couples the context to one generator and to package lifetime.)

4. **`ReviewState` is a guarded domain status machine.** `Draft → InReview → Edited → Approved → ReadyForPublication → HandedOff`, plus `Rejected` / `Discarded`. Transitions are enforced by an explicit `ReviewStateTransitions` table, reusing the `ProfileAnalysisStatusTransitions` pattern. Illegal transitions return structured domain errors, never silent no-ops. This is an **editorial** lifecycle, distinct from `Provenance.Status` (audit-only) and from operational job/analysis statuses.

5. **`ContentSet` unifies individual and bulk as cardinality.** A `ContentSet` is a named, soft-referencing collection of items with a `SetKind` (`GenerationRun` now; `Campaign` / `PlanPeriod` / `ClientPresentation` / `AdHocSelection` later) and a roll-up `ReviewSummary`. A Single Analyzer run is a small set; Magic Generator is a large set (or a `Campaign` of sets). Bulk operations are **application-layer fan-outs** over a resolved selection with partial-success semantics — there is no separate "bulk" domain model. (Rejected: distinct aggregates for single vs. bulk — guarantees a future redesign.)

6. **Edits reuse the existing rendering/guardrail stack; no new editor.** Text edits mutate the draft text. Visual edits (Phase 2+) mutate the draft's `VisualCompositionSpec` markup and re-render deterministically through the existing `IVisualGuardrailValidator` (palette/typography/overflow) + `IContentRenderer`; background swaps reuse `BackgroundSelection`/`BackgroundTier`; image swaps go through `IClientAssetStorage`. Content Studio is explicitly **not** a freeform/Canva-style editor. (Rejected: a bespoke visual editor — out of scope and redundant with the generation stack.)

7. **Preview is a presentation-only projection.** The backend emits a normalized `ContentPreviewProjection` (medium + target channel + renderable payload + channel constraints); the destination emulator (IG Feed/Carousel/Story/Reel, TikTok, YT Shorts) is a frontend component. The emulator currently sketched inside `generate-social-content-workflow` is consolidated to consume this single projection. (Rejected: per-surface bespoke preview — duplicates logic.)

8. **Handoff to publishing is an outbound, versioned contract.** Reaching `ReadyForPublication` emits `ContentApprovedForPublication` carrying a versioned `PublicationHandoffPayload` (final asset URIs, copy, target network/placement, client/org, provenance) through an `IPublicationHandoff` port. Content Studio marks the item `HandedOff` and stops — it never schedules or publishes. The future publication pipeline implements the consumer. (Rejected: Content Studio calling a publishing provider directly — violates the responsibility boundary and couples to an unbuilt pipeline.)

9. **Soft references everywhere, additive persistence.** New `social.content_studio_*` tables are org-scoped (`organization_id`), reference generation/package/plan by plain Guid (no EF navigations), and are purely additive — no existing table changes. Orphan references are tolerated and resolved at read time, preserving aggregate independence (same discipline as `ContentPlan.CalendarSlot.ProducedPackageId`).

10. **`EditTrace` over shared `Provenance`.** Content Studio uses a local `EditTrace { LastEditedSource: Manual|AI, LastEditedAtUtc, PromptVersion? }` rather than importing `ContentPlans.Provenance`, to preserve context isolation. Promoting `Provenance` to a shared kernel is a noted future option, not a dependency.

## Consequences

- The content lifecycle gains a first-class, persisted, inspectable **editorial surface** between generation and publication, with a guarded approval lifecycle that answers PRD §7's "approval workflow" open question.
- `GeneratedPackage` immutability, traceability, and cost accounting are fully preserved; Content Studio is a derived working surface, never a mutation path.
- The surface is **source-agnostic from day one**: Magic Generator and future generators integrate by emitting a new `GeneratorKind` and feeding the same materializer — no redesign.
- Individual and bulk operations share one model; bulk is a cardinality concern handled in the application layer, deferred to a later phase without architectural debt.
- Preview is consolidated to one projection contract, removing the duplication risk with `generate-social-content-workflow`.
- The publishing boundary is explicit and decoupled: a versioned outbound contract the future pipeline builds against, answering PRD §7's "preview built internally" question (internal preview + internal review, external publication).
- Hexagonal boundaries hold: `ContentItem`, `ContentSet`, `ReviewState`, `ContentOrigin`, `ContentKind`, `ContentDraft`, `EditTrace` are pure domain; EF mapping, enum conversions, and provider concerns live only in Infrastructure.
- Cost: a new context, tables, ports, and a frontend module — justified by the size of the gap and amortized across all future generators.

## Related
- Proposal: `openspec/changes/content-studio/proposal.md`
- Design: `openspec/changes/content-studio/design.md`
- Spec: `openspec/changes/content-studio/specs/content-studio/spec.md`
- Deltas: `specs/content-generation/spec-delta.md`, `specs/content-plan/spec-delta.md`, `specs/publishing/spec-delta.md`
- Builds on: ADR 0002 (Content Plan working-surface + soft package references), ADR 0003 (Profile Analysis bounded-context isolation)
- Upstream producers: `single-analyzer-*` (current), `generate-social-content-workflow` / Magic Generator (future)
- Downstream consumer: `publishing` / `publication-pipeline` (future)
