# Spec Delta: Profile Analysis — DecorationMotifs (Divider Rule, Composition Archetype, Ornament Detection)

<!-- Change: client-aesthetic-visual-sophistication -->
<!-- Applies to: openspec/specs/profile-analysis/spec.md (additive delta, defensive-parse per FFS-PA-02) -->

## Purpose

`AestheticExtraction`/`ClientAestheticProfile` gain one additive-nullable field, `DecorationMotifs`,
capturing the client's recurring divider-rule style, an open composition-archetype classification
token, and (nested) a recurring decorative ornament's source path and bounding box — following the
`BrandMarkDetection` defensive-parse precedent (FFS-PA-02) with one deliberate divergence for the
archetype token (verbatim preservation instead of collapse-to-unknown).

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAVS-PA-01 | Settled decisions 3, 5, 7; Approach 1 — `DecorationMotifs` schema shape |
| CAVS-PA-02 | Settled decision 5; "Why a string, precisely" — `CompositionArchetype` verbatim preservation |
| CAVS-PA-03 | Approach 1 — `DividerRuleStyle` closed-enum defensive parse |
| CAVS-PA-04 | Scope item 6 — `DecorativeOrnamentDetection` nested schema, bounding-box validation |
| CAVS-PA-05 | Settled decision 7 — binding schema-fatigue norm |

---

## ADDED Requirements

### Requirement: CAVS-PA-01 — DecorationMotifs Is One Additive-Nullable Record With Two Members

`AestheticExtraction` and `ClientAestheticProfile` MUST each gain exactly one additive-nullable
field, `DecorationMotifs? DecorationMotifs`, carrying exactly two members: `DividerRuleStyle?
DividerRule` and `string? CompositionArchetype`. No `AccentShape` field or type MUST exist anywhere
in the codebase. A profile persisted before this change MUST deserialize with `DecorationMotifs =
null` and throw nothing.

#### Scenario: Cold-start client has no decoration signal (CAVS-PA-01-a, regression/cold-start)

- GIVEN a client whose extraction produced no divider or archetype signal
- WHEN the extraction response is mapped
- THEN `DecorationMotifs = null`
- AND no exception is thrown

#### Scenario: Pre-change profile deserializes with DecorationMotifs null (CAVS-PA-01-b, regression)

- GIVEN a profile blob persisted before this change (no `decorationMotifs` key)
- WHEN deserialized into `ClientAestheticProfile`
- THEN `DecorationMotifs = null`
- AND no exception is thrown; all pre-existing fields deserialize intact

#### Scenario: Both members populate independently (CAVS-PA-01-c)

- GIVEN the extractor reports a divider style and a composition archetype for the same client
- WHEN the extraction response is mapped
- THEN `DecorationMotifs.DividerRule` and `DecorationMotifs.CompositionArchetype` both carry their
  respective values
- AND no `AccentShape` member or value is present anywhere in the mapped record

---

### Requirement: CAVS-PA-02 — CompositionArchetype Is an Open Token, Preserved Verbatim When Unrecognised

`CompositionArchetype` MUST be a defensively-normalised `string?` (trim + lowercase; null/blank ⇒
`null`), never a closed enum. Unlike `BrandMarkDetection.NormalizeMarkType`, which collapses an
unrecognised token to `"unknown"`, `CompositionArchetype` normalisation MUST preserve an
unrecognised token verbatim (post trim/lowercase) rather than discarding or collapsing it, so a
future recognised archetype requires no schema or data migration.

#### Scenario: Recognised archetype token is captured (CAVS-PA-02-a)

- GIVEN the extractor reports a composition archetype token that matches a recognised value in
  `CompositionArchetypes`
- WHEN the extraction response is mapped
- THEN `CompositionArchetype` equals that recognised token

#### Scenario: Unrecognised archetype token round-trips verbatim (CAVS-PA-02-b)

- GIVEN the extractor reports a novel token not present in `CompositionArchetypes` (e.g.
  `"diagonal-split"`)
- WHEN the extraction response is mapped, persisted, and read back through
  `ClientAestheticProfile`
- THEN `CompositionArchetype` equals the normalised novel token verbatim (trimmed, lowercased) at
  every stage
- AND the token is NOT collapsed to `"unknown"` or any other placeholder
- AND no exception is thrown

#### Scenario: Null or blank archetype normalises to null (CAVS-PA-02-c)

- GIVEN the extractor returns a null or blank/whitespace-only archetype value
- WHEN the extraction response is mapped
- THEN `CompositionArchetype = null`

---

### Requirement: CAVS-PA-03 — DividerRuleStyle Is a Closed Enum, Unrecognised Maps to Null

`DividerRuleStyle` MUST remain a closed enum with exactly three values (`Line`,
`LineWithDiamond`, `LineWithDot`), because its values are bounded by the CSS render primitive, not
by clients. An unrecognised, missing, or malformed divider-style token MUST map to `DividerRule =
null`, never throw.

#### Scenario: Recognised divider style is captured (CAVS-PA-03-a)

- GIVEN the extractor reports a divider style matching one of the three enum values
- WHEN the extraction response is mapped
- THEN `DecorationMotifs.DividerRule` equals that enum value

#### Scenario: Unrecognised divider token maps to null (CAVS-PA-03-b)

- GIVEN the extractor returns a divider-style token that does not match any of the three values
- WHEN the extraction response is mapped
- THEN `DecorationMotifs.DividerRule = null`
- AND no exception is thrown

---

### Requirement: CAVS-PA-04 — DecorativeOrnamentDetection Nests Source Path and Validated Bounding Box

`DecorationMotifs` MUST gain a nested `DecorativeOrnamentDetection?` member carrying a source image
path (resolved from the extractor's own index→`StoragePath` resolution, mirroring `CAGH-PA-03`) and
a `BrandMarkBoundingBox`, validated by the existing `TryValidateBoundingBox` / `MinimumNormalizedArea`
guard. A malformed, out-of-range, or below-minimum-area box MUST result in a null bounding box, not
an exception. The bounding-box reliability outcome across a batch of real extraction runs MUST be
recorded before any ornament render work depends on it.

#### Scenario: Valid ornament bounding box is retained (CAVS-PA-04-a)

- GIVEN the extractor returns an ornament bounding box within 0..1 bounds and above the
  minimum-area guard, with a resolvable source index
- WHEN the extraction response is parsed
- THEN `DecorativeOrnamentDetection.BoundingBox` carries the normalised coordinates
- AND `SourceImagePath` carries the resolved storage path

#### Scenario: Below-minimum-area or malformed ornament box is rejected (CAVS-PA-04-b)

- GIVEN the extractor returns an ornament bounding box below the minimum-area guard, out of 0..1
  range, or malformed
- WHEN the extraction response is parsed
- THEN `DecorativeOrnamentDetection.BoundingBox = null`
- AND no exception is thrown

#### Scenario: Ornament bounding-box reliability is measured before render work depends on it (CAVS-PA-04-c)

- GIVEN a batch of extraction runs against real client feeds
- WHEN ornament `BoundingBox` acceptance/rejection outcomes are recorded
- THEN the reliability measurement is recorded and available before any ornament render
  requirement (`content-generation` ornament-render scenarios) depends on it
- AND if the measurement is unusable, the decision to cut ornament rendering is recorded rather
  than silently absorbed

---

### Requirement: CAVS-PA-05 — Schema-Fatigue Norm Is Binding for Further Additive Fields

No further additive field MAY be added to `AestheticExtraction` or `ClientAestheticProfile` without
either (a) a consolidation pass grouping accumulated signals into a container, or (b) an ADR
recorded before the field is written. `DecorationMotifs` itself complies by nesting
`DecorativeOrnamentDetection` inside it rather than adding it as a separate top-level field, and by
cutting `AccentShape` rather than absorbing it as a third member.

#### Scenario: A field added without consolidation or an ADR is non-compliant (CAVS-PA-05-a)

- GIVEN a future change proposes a new additive field directly on `AestheticExtraction` or
  `ClientAestheticProfile`
- WHEN that change is reviewed against this norm
- THEN it MUST cite either a consolidation pass or a recorded ADR
- AND absent either, the change is non-compliant

---

## MODIFIED Requirements

None. `DecorationMotifs` is fully additive; no existing `profile-analysis` requirement changes
behavior.

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- `DecorationMotifs`, `DividerRuleStyle`, `CompositionArchetypes`, and
  `DecorativeOrnamentDetection` live in `Features/Social/ContentGeneration/Shared/` as Application
  value objects.
- Extractor prompt/schema/`Map()` changes and index→`StoragePath` resolution live in
  `MultimodalAestheticStyleExtractor.cs`, reusing the same resolution mechanism `CAGH-PA-03`
  established for brand marks.
- Ornament crop derivation (the Application port + SkiaSharp adapter that CONSUMES
  `SourceImagePath`/`BoundingBox`) is covered by the `content-generation` delta's render-time
  requirements, not this spec.
