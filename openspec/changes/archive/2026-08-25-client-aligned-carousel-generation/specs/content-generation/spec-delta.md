# Delta: Content Generation — Slide Count Enforcement, Color-Pairing Palette, Handle Slot

<!-- Change: client-aligned-carousel-generation -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->

## Purpose

Apply the captured client aesthetic structure at generation time: enforce captured slide
count over the hardcoded `~3-5` range, apply `{BackgroundHex, TextHex}` color pairings
over positional palette, add an `@handle`/signature slot to `CarouselVisualComposer`,
and load `ClientOwnAccount.Handle` into `BrandSnapshot`.

---

## PRD Traceability

| Requirement ID | Gap |
|---|---|
| CAC-CG-01 | GAP 4 — `AverageSlidesPerCarousel` advisory only; conflicts with hardcoded `~3-5` in `CarouselGenerator` |
| CAC-CG-02 | GAP 2 — `CompositionDefaults.ResolvePalette()` uses positional palette; captured pairings never applied |
| CAC-CG-03 | GAP 5 — `CarouselVisualComposer` lacks `@handle`/signature slot (story and feed-post have it) |
| CAC-CG-04 | GAP 5 — `ClientOwnAccount.Handle` not loaded into `BrandSnapshot` |

---

## ADDED Requirements

### Requirement: CAC-CG-01 — `CarouselGenerator` Enforces `TypicalSlideCount` When Captured

When `ClientAestheticProfile.TypicalSlideCount` is non-null, `CarouselGenerator` MUST
use that value as the target slide count, overriding the hardcoded `~3-5` range. When
`TypicalSlideCount` is null, the existing `~3-5` range applies unchanged. This requirement
is independent of `CAG-CG-08` (the advisory avg-slides hint in copy prompts), which
remains valid and unchanged.

#### Scenario: Captured count enforced — carousel generated at client's typical length (CAC-CG-01-a)

- GIVEN a `ClientAesthetic` carousel job with `TypicalSlideCount = 7`
- WHEN `CarouselGenerator` runs
- THEN the carousel is generated with 7 slides
- AND the hardcoded `~3-5` range does not apply

#### Scenario: `TypicalSlideCount` null — existing range applies unchanged (CAC-CG-01-b)

- GIVEN a `ClientAesthetic` carousel job with `TypicalSlideCount = null`
- WHEN `CarouselGenerator` runs
- THEN the carousel is generated within the current `~3-5` slide range
- AND no error is raised for the absent count

---

### Requirement: CAC-CG-02 — `CompositionDefaults.ResolvePalette()` Applies Captured Color Pairings; Positional Fallback When Absent

When `ClientAestheticProfile.ColorPairings` is non-null and non-empty,
`CompositionDefaults.ResolvePalette()` MUST assign background and text colors from
the captured `{BackgroundHex, TextHex}` pairings according to the client's observed
criterion. When `ColorPairings` is null or empty, positional array-position palette
assignment MUST be used unchanged.

#### Scenario: Captured pairings applied — colors follow client criterion (CAC-CG-02-a)

- GIVEN a `ClientAesthetic` carousel job with `ColorPairings = [{BackgroundHex: "#1A1A2E", TextHex: "#E0E0E0"}]`
- WHEN `CompositionDefaults.ResolvePalette()` is called for a slide
- THEN the slide background color is `#1A1A2E` and text color is `#E0E0E0`
- AND array-position palette assignment is NOT used

#### Scenario: Absent pairings — positional palette assignment runs unchanged (CAC-CG-02-b)

- GIVEN a `ClientAesthetic` carousel job with `ColorPairings = null`
- WHEN `CompositionDefaults.ResolvePalette()` is called
- THEN colors are assigned by existing positional palette order
- AND the job completes normally with no error

---

### Requirement: CAC-CG-03 — `CarouselVisualComposer` Gains `@handle`/Signature Slot

`CarouselVisualComposer` MUST include a dedicated composition slot for the client's
`@handle` and signature at parity with `StoryVisualComposer` and `FeedPostVisualComposer`.
The slot MUST be rendered when `BrandSnapshot.Handle` is non-null. When `Handle` is null
or empty, the slot MUST be empty or omitted with no error.

#### Scenario: Handle present — slot rendered in carousel composition (CAC-CG-03-a)

- GIVEN a `ClientAesthetic` carousel job where `BrandSnapshot.Handle = "@clientbrand"`
- WHEN `CarouselVisualComposer` composes a slide
- THEN the `@handle` slot carries `"@clientbrand"` in the composition envelope
- AND the slot position is consistent with the story/feed-post format parity

#### Scenario: Handle absent — slot empty; composition succeeds (CAC-CG-03-b)

- GIVEN `BrandSnapshot.Handle = null`
- WHEN `CarouselVisualComposer` composes a slide
- THEN the `@handle` slot is empty or omitted
- AND no exception is raised and the slide renders normally

---

### Requirement: CAC-CG-04 — `BrandSnapshot` Carries `Handle` from `ClientOwnAccount` via Application Port

The `BrandSnapshot` loader MUST load `ClientOwnAccount.Handle` into the snapshot via
an Application-layer port (no Infrastructure type in the snapshot or loader port signature),
making it available as `BrandSnapshot.Handle` at composition time. When no own account
exists for the client, `Handle` MUST be null. This field is additive and MUST NOT break
any existing consumer of `BrandSnapshot` per BS-01.

#### Scenario: Own account found — `Handle` populated in snapshot (CAC-CG-04-a)

- GIVEN a client with a registered own account whose handle is `"@brand"`
- WHEN the `BrandSnapshot` is assembled
- THEN `BrandSnapshot.Handle = "@brand"`
- AND no Infrastructure or provider type appears in the snapshot or the loader port

#### Scenario: No own account — `Handle` null; snapshot assembles normally (CAC-CG-04-b)

- GIVEN a client with no registered own account
- WHEN the `BrandSnapshot` is assembled
- THEN `BrandSnapshot.Handle = null`
- AND snapshot assembly succeeds and all existing consumers are unaffected

---

## MODIFIED Requirements

None. `CAG-CG-08` (advisory `AverageSlidesPerCarousel` hint in copy prompts) remains
valid; `CAC-CG-01` adds enforcement at the generator level, independent of the advisory
copy-prompt behavior.

## REMOVED Requirements

None.
