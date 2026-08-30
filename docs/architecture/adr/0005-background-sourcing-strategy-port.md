# ADR 0005 - Background Sourcing Strategy Port

## Status
Accepted

## Context

Carousel generation previously called `BackgroundSelector.Select` on every slide inside the generation loop, with no coordination across slides. This produced two problems:

1. **Carousel incoherence.** Each slide independently drew a background, so adjacent slides could present visually unrelated assets even within a single run.
2. **No client-level control.** Background sourcing was a hard-coded 3-tier look-up; clients could not express a preference (own template set, AI-derived palette, aesthetic descriptors) without touching generation code.

Additionally, `BrandManualSnapshot.ColorPalette` was a flat `IReadOnlyList<string>` (hex values only). The AI visual designer had no way to express color significance or ordering, limiting prompt quality for palette-sensitive formats.

Concision was also uncontrolled: the AI generator received no advisory length guidance, and prompt version constants were stale.

## Decision

### 1. Per-client Background Sourcing Policy (strategy pattern + open-closed port)

A `BackgroundSourcingPolicy` enum is stored on `Client` (`Unset=0`, `ClientDefinedSet=1`, `AiPalette=2`, `ClientAesthetic=3`). At generation time, an `IBackgroundStrategyResolver` picks the appropriate `IBackgroundSourcingStrategy` implementation:

- **`LegacyTierStrategy`** (`Unset`) — preserves existing 3-tier behavior via `BackgroundSelector`.
- **`ClientDefinedSetStrategy`** (`ClientDefinedSet`) — rotates deterministically across the client's background template set using a stable hash of `(jobId, slideIndex)` so reruns produce the same assignment.
- **`AiPaletteStrategy`** (`AiPalette`) — returns `BackgroundSelection.Creative` for every slide; the AI designer drives selection from the brand palette.
- **`ClientAestheticStrategy`** (`ClientAesthetic`) — uses text descriptors from `ProfileAiAnalysis.DetectedBackgroundImages` to guide selection; degrades to `Creative` when descriptors are absent.

`IBackgroundSourcingStrategy` and `IBackgroundStrategyResolver` live in the Application layer (no Infrastructure imports). New cases plug in by implementing the port and registering a DI key — no changes to A/B/C strategies (OCP).

### 2. Pre-loop CarouselBackgroundPlan (carousel coherence)

The processor resolves the strategy and calls `strategy.Plan(...)` exactly once per format before the slide loop. The result (`CarouselBackgroundPlan`) is passed to `CarouselVisualComposer`, which reads `plan.ForSlide(i)` per slide. `BackgroundSelector.Select` is no longer called inside the composer. This guarantees coherence: all slides in a run share a single, deterministic plan.

### 3. Structured ColorRole + Rank on ColorEntry

`ColorEntry.Role` changes from a free-text `string?` to a `ColorRole?` enum (`Primary=0, Secondary=1, Accent=2, Neutral=3`) plus an `int Rank` ordering field. `BrandManualSnapshot.ColorPalette` becomes `IReadOnlyList<WeightedColor>`, pre-sorted by `(Role ordinal, Rank)`. The AI visual designer receives explicit `(hex, role, rank)` tuples and expresses dominant/accent prevalence in its system prompt. A migration backfills existing jsonb color palette documents and adds the `background_sourcing_policy smallint NULL` column to `social.clients`.

### 4. CASE C seam (text-descriptor channel)

`IClientAestheticDescriptorReader` is a new Application port returning `IReadOnlyList<string>` from the latest analyzed `ProfileAiAnalysis` record. Infrastructure provides `EfClientAestheticDescriptorReader`. The processor reads descriptors lazily — only when policy is `ClientAesthetic`. Full image-URL sourcing (Apify post-image pipeline) is deferred to Phase 2.

### 5. Advisory concision directives

A `ConcisionConstraint` constant is appended to carousel, story, and feed-post prompts. Post-generation, the processor's `CheckConcisionAdvisory` method inspects slot lengths and emits `LogWarning` (never throws). Prompt version constants are bumped to invalidate stale cached outputs (`Carousel=carousel.v3`, `Story=story-single.v2`, `FeedPost=feed-single.v2`).

## Consequences

- Carousel slide backgrounds are now coherent within a generation run (same `CarouselBackgroundPlan` consumed by all slides).
- Clients can opt into three sourcing modes without any generation pipeline changes; future modes extend via the port.
- Color roles and ranks propagate from the brand manual editor through `BrandManualSnapshot` to the AI designer, improving visual consistency of AI-generated assets.
- Advisory concision reduces token waste from verbose AI outputs; the pipeline never hard-rejects content, preserving backward compatibility.
- `BackgroundSelector.Select` is no longer called inside `CarouselVisualComposer`. The `LegacyTierStrategy` adapter preserves parity for clients with `Unset` policy.
- CASE C ships as a seam only (text descriptors); the full Apify image-URL pipeline is an open dependency gated on Phase 2.
- The `background_sourcing_policy` column is `smallint NULL`; existing rows remain `NULL` (legacy behavior) until explicitly set.

## Related

- Proposal: `openspec/changes/content-background-sourcing-policy/proposal.md`
- Tasks: `openspec/changes/content-background-sourcing-policy/tasks.md`
- Specs: `openspec/changes/content-background-sourcing-policy/specs/`
- Builds on: ADR 0003 (Profile Analysis bounded-context isolation — seam for CASE C descriptors)
- Extends: ADR 0004 (Content Studio — `BackgroundSelection` reuse in editorial edits)
