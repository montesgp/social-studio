# Delta: Background Sourcing Policy — Structured Style Capture (Slide-Layout + Color Pairings)

<!-- Change: client-aligned-carousel-generation -->
<!-- Applies to: openspec/specs/background-sourcing-policy/spec.md (additive delta) -->

## Purpose

Add slide-layout capture (photo-vs-flat interleave + typical slide count) and
`{BackgroundHex, TextHex}` color pairings to `ClientAestheticProfile`, populated
by the multimodal extractor. All new fields are additive and nullable; absent
fields (cold-start) preserve current generation defaults without errors.

---

## PRD Traceability

| Requirement ID | Gap |
|---|---|
| CAC-BSP-01 | GAP 1 — extractor DTO has no field for the slide-mix pattern it detects |
| CAC-BSP-02 | GAP 1 — `ClientAestheticProfile` lacks `SlideLayoutPattern` and `TypicalSlideCount` |
| CAC-BSP-03 | GAP 2 — no `{BackgroundHex, TextHex}` pairing concept exists on the profile |
| CAC-BSP-04 | GAP 1 + GAP 2 cold-start — absent captured fields must not change existing behavior |

---

## ADDED Requirements

### Requirement: CAC-BSP-01 — Multimodal Extractor DTO Gains Nullable `SlideMixPattern` Field

The multimodal aesthetic extractor response DTO MUST include a nullable `SlideMixPattern`
field capturing the client's per-slide photo-vs-flat classification and interleave order
as observed in the feed. When no clear pattern is detectable the field MUST be null.
The addition MUST be backward-compatible: extractor pipeline stages that do not read
this field continue unchanged.

#### Scenario: Clear interleave detected — `SlideMixPattern` populated (CAC-BSP-01-a)

- GIVEN a multimodal extraction run against a client with consistent photographic/flat alternation
- WHEN the extractor produces its response DTO
- THEN `SlideMixPattern` is populated with the per-slide classification
- AND all other DTO fields are unaffected

#### Scenario: Ambiguous pattern — `SlideMixPattern` null; extraction succeeds (CAC-BSP-01-b)

- GIVEN a feed with no discernible slide structure
- WHEN the extractor produces its DTO
- THEN `SlideMixPattern` is null
- AND the extraction completes successfully with no error raised

---

### Requirement: CAC-BSP-02 — `ClientAestheticProfile` Gains Nullable `SlideLayoutPattern` and `TypicalSlideCount`

`ClientAestheticProfile` MUST carry two new nullable fields populated from the extractor output:

- `SlideLayoutPattern` — per-slide photo-vs-flat classification and interleave derived from `SlideMixPattern`.
- `TypicalSlideCount` — integer slide count typical for the client, derived from extractor output or
  analysis statistics (`AverageSlidesPerCarousel`).

Both fields MUST be persisted additively (nullable columns). A null value for either field MUST NOT
alter any existing generation behavior.

#### Scenario: Extractor output present — profile carries layout and count (CAC-BSP-02-a)

- GIVEN an analysis whose extractor output contains a 5-slide alternating photographic/flat layout
- WHEN `ClientAestheticProfile` is built from that analysis
- THEN `SlideLayoutPattern` reflects the per-slide classification
- AND `TypicalSlideCount = 5`

#### Scenario: Extractor output absent — profile fields null; consumers unaffected (CAC-BSP-02-b)

- GIVEN an analysis with no extractor slide-mix output
- WHEN `ClientAestheticProfile` is built
- THEN `SlideLayoutPattern = null` and `TypicalSlideCount = null`
- AND existing profile consumers compile and behave identically to their pre-change behavior

#### Scenario: Persistence round-trip preserves new fields (CAC-BSP-02-c)

- GIVEN an analysis persisted with a populated `SlideLayoutPattern`
- WHEN the analysis is reloaded and `ClientAestheticProfile` is reconstructed
- THEN `SlideLayoutPattern` matches the persisted value with no data loss

---

### Requirement: CAC-BSP-03 — `ClientAestheticProfile` Carries `{BackgroundHex, TextHex}` Color Pairings

`ClientAestheticProfile` MUST carry a nullable `ColorPairings` field — a collection of
`{BackgroundHex, TextHex}` value pairs — derived by joining the client's brand-manual
`ColorRole` entries with the multimodal extractor's extracted `palette.Role` output
according to the client's observed pairing criterion. When the join yields no pairings
(absent palette or absent brand-manual colors), the field MUST be null or empty and
the existing positional palette assignment remains as the active fallback.

#### Scenario: Joinable roles produce color pairings (CAC-BSP-03-a)

- GIVEN a client with brand-manual `ColorRole` entries and a matching extracted palette
- WHEN `ClientAestheticProfile` is built
- THEN `ColorPairings` contains at least one `{BackgroundHex, TextHex}` pair derived
  from the join of brand-manual and extracted roles

#### Scenario: No joinable roles — `ColorPairings` null; positional fallback active (CAC-BSP-03-b)

- GIVEN a client with no brand manual or an extracted palette with no joinable roles
- WHEN `ClientAestheticProfile` is built
- THEN `ColorPairings` is null or empty
- AND the existing positional array-position palette assignment remains the active method

---

### Requirement: CAC-BSP-04 — Cold-Start: Null Captured Fields Preserve Existing Generation Defaults

When `SlideLayoutPattern`, `TypicalSlideCount`, or `ColorPairings` are null, every downstream
consumer MUST apply its current default behavior:

- `CreativeSlideIndexResolver` MUST apply the `{first, last}` v1 default (per CIG-BSP-02).
- `CarouselGenerator` MUST use the existing `~3-5` slide range.
- `CompositionDefaults.ResolvePalette()` MUST apply positional palette assignment.

No error, warning, or degrade is issued for absent captured fields; this is a valid
progressive-capture state.

#### Scenario: Cold-start client — all null; current defaults active; job completes (CAC-BSP-04-a)

- GIVEN a `ClientAesthetic` client with `SlideLayoutPattern = null`, `TypicalSlideCount = null`,
  and `ColorPairings = null`
- WHEN a carousel generation job runs
- THEN `CreativeSlideIndexResolver` returns the `{first, last}` v1 default
- AND the carousel is generated within the existing `~3-5` slide range
- AND palette is assigned by the existing positional method
- AND the job completes normally with no errors or warnings

---

## MODIFIED Requirements

None. `CIG-BSP-01`, `CIG-BSP-02`, and `CIG-BSP-03` remain valid and unchanged.
`CIG-BSP-03` anticipated extractor population without schema change; this change
realizes that intent by supplying the extractor's `SlideMixPattern` output.

## REMOVED Requirements

None.
