# Spec Delta: Content Generation — Carousel Coherence, Structured Color-Role Scale, and Advisory Concision

<!-- Change: content-background-sourcing-policy -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->

## Purpose

Specify the **additive behavioral changes** this change introduces to the
`content-generation` capability:

1. **Carousel background coherence** — a single shared `CarouselBackgroundPlan` is
   computed ONCE before the slide loop and consumed by every slide in both
   `ContentGenerationJobProcessor` and `CarouselVisualComposer`. Per-slide independent
   background selection is eliminated.
2. **Structured `ColorEntry.Role` scale** — the color palette carried in
   `BrandManualSnapshot` is upgraded from a flat hex list to `(Hex, Role, rank)` triples.
   The `ColorEntry.Role` enum is `Primary / Secondary / Accent / Neutral`; rank is an
   integer where 1 = highest prevalence. This data is threaded from the snapshot through
   to the AI visual designer as a prevalence/weighting signal.
3. **Advisory concision directives on copy generators** — copy-generator prompts receive
   firm concision rules (singles = super-summary; carousels = one concept across all
   slides, per-slot length guidance). Violations are LOGGED and never hard-rejected.
   `ContentGenerationPromptVersions` are bumped for every touched generator.

No rendering, guardrail, or publishing behavior is modified.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CG-COH-01 | Proposal §Scope — carousel-coherence fix (shared `CarouselBackgroundPlan`) |
| CG-COH-02 | Proposal §Scope — reconcile `CarouselVisualComposer` second call site |
| CG-COL-01 | Proposal §Scope — structured `ColorEntry.Role` scale in `BrandManualSnapshot` |
| CG-COL-02 | Proposal §Scope — propagation through snapshot loader (`EfClientBrandSnapshotLoader`) |
| CG-COL-03 | Proposal §Scope — AI designer receives palette as prevalence/weighting signal |
| CG-CON-01 | Proposal §Scope — advisory concision on singles (super-summary) |
| CG-CON-02 | Proposal §Scope — advisory concision on carousels (one concept across slides) |
| CG-CON-03 | Proposal §Scope — LOG violations, no hard reject |
| CG-CON-04 | Proposal §Scope — prompt-version bumps |

---

## ADDED Requirements

---

### Requirement: CG-COH-01 — Carousel Background Plan Is Computed Once Before the Slide Loop

For every carousel generation job, a `CarouselBackgroundPlan` MUST be computed exactly
ONCE before the per-slide iteration begins. The plan encapsulates all slide background
assignments for the carousel. Every slide in the loop MUST read its background from this
pre-computed plan — no slide MAY trigger an independent `BackgroundSelector.Select()`
call after the plan has been computed.

This requirement eliminates the incoherence caused by the current per-slide call in
`ContentGenerationJobProcessor` (line 315) and the uncoordinated second call site in
`CarouselVisualComposer` (line 61).

#### Scenario: Pre-loop plan covers all carousel slides (CG-COH-01-a)

- GIVEN a carousel generation job that will produce 5 slides
- WHEN the job processor prepares the carousel
- THEN a single `CarouselBackgroundPlan` is computed before the first slide iteration begins
- AND the plan contains an assignment for each of the 5 slides
- AND the background selection function is called exactly ONCE to produce the plan
  (not once per slide)

#### Scenario: All carousel slides use backgrounds from the shared plan (CG-COH-01-b)

- GIVEN a `CarouselBackgroundPlan` has been computed for a 3-slide carousel
- WHEN each slide's visual composition is executed in sequence
- THEN slide 1 uses the background assigned to position 1 in the plan
- AND slide 2 uses the background assigned to position 2 in the plan
- AND slide 3 uses the background assigned to position 3 in the plan
- AND no slide initiates a new background resolution call

---

### Requirement: CG-COH-02 — CarouselVisualComposer Reads from the Shared Plan; Does Not Independently Select

`CarouselVisualComposer` MUST receive the `CarouselBackgroundPlan` as an input and
consume per-slide assignments from it. It MUST NOT call `BackgroundSelector.Select()` or
any strategy method to re-derive a background for a slide it is composing. The plan is
the authoritative source; the composer is a consumer.

#### Scenario: CarouselVisualComposer uses injected plan, not independent selection (CG-COH-02-a)

- GIVEN a `CarouselBackgroundPlan` is passed to `CarouselVisualComposer`
- WHEN `CarouselVisualComposer` composes a specific slide
- THEN it reads the background for that slide from the injected plan
- AND it does NOT call any background selection or strategy method independently
- AND the composed slide's background matches the plan assignment for that slide's position

#### Scenario: Carousel with heterogeneous backgrounds — all slides remain plan-consistent (CG-COH-02-b)

- GIVEN a client with policy `CaseA` and backgrounds `[bg1, bg2]`
- AND a 4-slide carousel plan where slides are assigned `[bg1, bg2, bg1, bg2]`
- WHEN `CarouselVisualComposer` composes all 4 slides
- THEN the rendered compositions for slides 1 and 3 use `bg1`
- AND the rendered compositions for slides 2 and 4 use `bg2`
- AND all assignments match the pre-computed plan

---

### Requirement: CG-COL-01 — BrandManualSnapshot.ColorPalette Carries Structured (Hex, Role, Rank) Entries

`BrandManualSnapshot.ColorPalette` MUST be upgraded from `IReadOnlyList<string>` (hex
values only) to `IReadOnlyList<ColorPaletteEntry>` where each entry carries:
- `Hex`: the color hex string
- `Role`: one of `Primary`, `Secondary`, `Accent`, `Neutral` (strongly typed enum)
- `Rank`: a positive integer, where 1 indicates the highest palette prevalence (most dominant role)

The previous flat-hex shape MUST NOT be used anywhere that a `BrandManualSnapshot` is
consumed after this change is applied. All consumers (AI designer, guardrail, template
markup builder) MUST be updated to accept the new shape in the same change.

#### Scenario: Snapshot carries full (Hex, Role, rank) for each palette entry (CG-COL-01-a)

- GIVEN a client brand manual with a color palette of 3 entries:
  `(#FF0000, Primary, 1)`, `(#0000FF, Secondary, 2)`, `(#AAAAAA, Neutral, 3)`
- WHEN a `BrandManualSnapshot` is built for that brand manual
- THEN `ColorPalette` contains exactly 3 `ColorPaletteEntry` objects with the corresponding
  Hex, Role, and Rank values
- AND no entry has a null Hex, null Role, or zero/negative Rank

#### Scenario: Consumer receiving flat-hex shape is absent after change (CG-COL-01-b)

- GIVEN the completed implementation
- WHEN all callers of `BrandManualSnapshot.ColorPalette` are inspected
- THEN no caller treats `ColorPalette` as a plain `IEnumerable<string>` or `string[]`
- AND all callers access `.Hex`, `.Role`, and `.Rank` properties

---

### Requirement: CG-COL-02 — EfClientBrandSnapshotLoader Propagates Role and Rank; Does Not Discard Them

`EfClientBrandSnapshotLoader` (line 102 area) MUST load `ColorEntry.Role` and the rank
ordering from the persisted brand-manual data and include them in the `BrandManualSnapshot`
it constructs. Discarding or ignoring `Role` / rank during snapshot construction is a
conformance failure.

#### Scenario: Snapshot loader includes Role and rank for every palette entry (CG-COL-02-a)

- GIVEN a brand manual persisted with color entries including Role = "Primary" and rank = 1
- WHEN `EfClientBrandSnapshotLoader` builds the snapshot for that brand manual
- THEN the resulting `BrandManualSnapshot.ColorPalette` entry has `Role = Primary` and `Rank = 1`
- AND the Role and rank values are NOT null or default-initialized incorrectly

---

### Requirement: CG-COL-03 — AI Visual Designer Receives Palette as Prevalence/Weighting Signal

The `AiVisualDesigner` prompt MUST include the structured palette entries in a form that
conveys role prevalence: the Primary color (rank 1) MUST be presented as the most dominant,
Secondary (rank 2) as the second, Accent as accent-level, and Neutral as background/neutral.
The weighting language MUST be explicit in the prompt (not just a hex list).

#### Scenario: AI visual designer prompt includes role-annotated palette (CG-COL-03-a)

- GIVEN a `BrandManualSnapshot` with palette entries of mixed roles and ranks
- WHEN `AiVisualDesigner` builds the generation prompt
- THEN the prompt section for color guidance includes each entry's Hex AND its Role label
  (e.g., "Primary #FF0000", "Secondary #0000FF") in rank order (rank 1 first)
- AND the prompt communicates that higher-rank roles have greater visual weight/prevalence

#### Scenario: CASE B — palette weighting is the only background creative direction (CG-COL-03-b)

- GIVEN a client with policy `CaseB`
- WHEN the AI visual designer prompt is built
- THEN the palette weighting section is present and references Role/rank ordering
- AND no client background set or profile aesthetic descriptors are included in the prompt

---

### Requirement: CG-CON-01 — Advisory Concision Directive on Single-Format Copy Generators

Copy-generator prompts for single-slide formats (story, feed post) MUST include an
advisory concision directive specifying that copy should be a "super-summary" — maximum
impact in minimum words. The directive MUST be included in the prompt as guidance, not
enforced as a schema-level length constraint. No AI output is rejected due to concision
alone.

#### Scenario: Single-format prompt includes super-summary directive (CG-CON-01-a)

- GIVEN a story or feed-post generation job
- WHEN the copy-generator prompt is assembled
- THEN the prompt contains a concision directive describing the target as a super-summary
  (e.g., "maximum impact in minimum words", short-form tone guidance, or per-slot character
  recommendations)

---

### Requirement: CG-CON-02 — Advisory Concision Directive on Carousel Copy Generators

Copy-generator prompts for carousel formats MUST include an advisory concision directive
specifying that all slides serve ONE core concept, copy per slide is tightly bounded
(one idea per slide, minimal headline + one short body line), and the full carousel reads
as a cohesive unit, not a list of independent slides.

#### Scenario: Carousel prompt includes one-concept-across-slides directive (CG-CON-02-a)

- GIVEN a carousel generation job
- WHEN the copy-generator prompt is assembled
- THEN the prompt contains a concision directive describing the carousel as carrying a
  single concept across slides (e.g., per-slot line-count or character recommendations,
  cohesion language, no-repetition guidance across slides)

---

### Requirement: CG-CON-03 — Concision Violations Are Logged, Never Hard-Rejected

If the AI-generated copy for any format or slide exceeds the advisory concision guidance
(i.e., a violation is detected), the system MUST log a structured warning identifying the
job id, format key, slot name, expected bound, and actual length. The generation pipeline
MUST continue and the generated copy MUST NOT be discarded or cause a retry solely on
concision grounds.

#### Scenario: Concision violation logged but generation proceeds (CG-CON-03-a)

- GIVEN a copy-generator receives AI output where the headline exceeds the advisory length bound
- WHEN the concision check runs
- THEN a structured warning is logged containing: job id, format, slot name, advisory bound, actual length
- AND the generation pipeline continues with the returned copy unchanged
- AND no exception is thrown and no retry is triggered due to concision alone

#### Scenario: Non-concision failures still fail normally (CG-CON-03-b)

- GIVEN a generation job where a guardrail validation failure occurs (unrelated to concision)
- WHEN the guardrail runs
- THEN the existing failure path executes normally
- AND the concision advisory behavior does not interfere with or suppress the guardrail failure

---

### Requirement: CG-CON-04 — ContentGenerationPromptVersions Bumped for All Touched Generators

Every copy-generator whose prompt is modified by this change (single-format and carousel
generators that receive new concision directives, and any generator whose prompt is
adjusted for color-role guidance) MUST have its entry in `ContentGenerationPromptVersions`
incremented. No previously-versioned prompt that receives changes in this change may retain
its old version constant.

#### Scenario: Prompt versions incremented for all modified generators (CG-CON-04-a)

- GIVEN the completed implementation
- WHEN `ContentGenerationPromptVersions` is inspected
- THEN every generator whose prompt was modified by this change has a version value
  higher than its pre-change value
- AND no modified generator retains its pre-change version constant

---

## MODIFIED Requirements

None. All requirements in this delta are additive. No existing `openspec/specs/content-generation/spec.md`
requirement is changed in meaning or removed.

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative, for test authoring)

- CG-COH-01 MUST be verified by a unit test on `ContentGenerationJobProcessor` that uses a mock
  background strategy and asserts the strategy's plan method is called exactly ONCE for a multi-slide
  carousel, not N times.
- CG-COH-02 MUST be verified by a unit test on `CarouselVisualComposer` that injects a pre-built
  plan and asserts no external strategy/selector call is made.
- CG-COL-01 MUST be covered by a unit test on snapshot construction: given a `ColorEntry` with
  Role and rank, assert the resulting `ColorPaletteEntry` matches.
- CG-COL-02 MUST be covered by an integration test against `EfClientBrandSnapshotLoader` using
  a seeded database row with non-trivial role values.
- CG-COL-03 MUST be verified by a unit test on `AiVisualDesigner.BuildPromptAsync` (or equivalent)
  that captures the prompt string and asserts the role labels appear in rank order.
- CG-CON-01 / CG-CON-02: unit tests on prompt builders — assert concision directive text is present
  in the assembled prompt string.
- CG-CON-03: unit test with a mock that returns oversized copy — assert the warning is logged and
  the copy is not discarded.
- CG-CON-04: compile-time / snapshot test on `ContentGenerationPromptVersions` constants. CI fails
  if a modified generator's constant matches the pre-change value.
