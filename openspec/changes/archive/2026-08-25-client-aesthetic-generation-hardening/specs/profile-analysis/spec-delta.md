# Spec Delta: Profile Analysis — Brand-Mark Type, Text, Source Image, and Bounding Box

<!-- Change: client-aesthetic-generation-hardening -->
<!-- Applies to: openspec/specs/profile-analysis/spec.md (additive delta, defensive-parse per FFS-PA-02) -->

## Purpose

`BrandMarkDetection` (`FFS-PA-01`/`FFS-PA-02`) captures only `Present`/`PositionHint`/`ColorHex` —
never the mark's actual content or kind. Per settled decision 3, the extractor MUST now capture
whether the mark is a text signature or an icon/symbol, the literal text when it is a signature,
and — for an icon — where its clearest occurrence sits in an already-archived reference image, so a
downstream crop can be derived. All additions are additive-nullable and defensively parsed
(missing/unknown/malformed → null, never throws), per the `FFS-PA-02` convention.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAGH-PA-01 | Scope item 7 — `MarkType` discriminator, defensive parsing |
| CAGH-PA-02 | Scope item 7 — `Text` field, meaningful only for `MarkType: "text"` |
| CAGH-PA-03 | Scope item 7 — `SourceImagePath` resolved from extractor source index to `StoragePath` |
| CAGH-PA-04 | Scope item 7 — `BoundingBox`, normalised, validated, minimum-area guard |
| CAGH-PA-05 | Risks — bounding-box reliability measured independently of rendering (WU-6b) |

---

## ADDED Requirements

### Requirement: CAGH-PA-01 — BrandMarkDetection Gains an Additive Nullable MarkType Discriminator

`BrandMarkDetection`/`BrandMarkDto` MUST gain an additive nullable `MarkType` discriminator with
values `"text"`, `"icon"`, or `"unknown"`. Any value returned by the extractor that does not map to
`"text"` or `"icon"` MUST parse to `"unknown"`. A profile persisted before this change MUST
deserialize with `MarkType = null` and throw nothing.

#### Scenario: Extractor returns a recognised MarkType (CAGH-PA-01-a)

- GIVEN the extractor returns `markType: "text"` for a client's recurring mark
- WHEN the response is parsed into `BrandMarkDetection`
- THEN `MarkType = "text"`

#### Scenario: Extractor returns an unrecognised value — maps to unknown (CAGH-PA-01-b)

- GIVEN the extractor returns an unrecognised or malformed `markType` token
- WHEN the response is parsed
- THEN `MarkType = "unknown"`
- AND no exception is thrown

#### Scenario: Pre-change profile deserializes with MarkType null (CAGH-PA-01-c, regression/cold-start)

- GIVEN a profile blob persisted before this change (no `markType` key)
- WHEN deserialized into `BrandMarkDetection`
- THEN `MarkType = null`
- AND no exception is thrown; all pre-existing fields (`Present`, `PositionHint`, `ColorHex`) intact

---

### Requirement: CAGH-PA-02 — Text Field Carries the Literal Signature String for Text Marks

`BrandMarkDetection` MUST gain an additive nullable `Text` field carrying the literal signature
string, meaningful only when `MarkType = "text"`. When the extractor does not return a text value,
or `MarkType` is not `"text"`, `Text` MUST be null.

#### Scenario: Text mark carries the extracted literal (CAGH-PA-02-a)

- GIVEN the extractor detects a recurring text signature `"Studio Marisol"`
- WHEN the response is parsed
- THEN `MarkType = "text"` and `Text = "Studio Marisol"`

#### Scenario: Icon mark — Text is null (CAGH-PA-02-b)

- GIVEN the extractor detects an icon/symbol mark, not a text signature
- WHEN the response is parsed
- THEN `MarkType = "icon"` and `Text = null`

---

### Requirement: CAGH-PA-03 — SourceImagePath Resolves an Extractor Source Index to the Archived StoragePath

For an icon mark, the extractor returns a source image **index** referencing its own indexed input
images (`"Image 0"`, `"Image 1"`, …). The extractor — the only component that knows that ordering —
MUST resolve that index to the corresponding `ProfilePostImageRef.StoragePath` before returning, so
`BrandMarkDetection.SourceImagePath` carries a storage path, not a prompt-ordering index. No
downstream consumer MAY depend on prompt ordering. When the index cannot be resolved (out of range,
missing, or the referenced image has no archived path), `SourceImagePath` MUST be null.

#### Scenario: Valid source index resolves to a storage path (CAGH-PA-03-a)

- GIVEN the extractor returns source index `2` for an icon mark, and index 2 corresponds to an
  archived `ProfilePostImageRef` with `StoragePath = "clients/123/refs/img2.jpg"`
- WHEN the extraction response is mapped
- THEN `BrandMarkDetection.SourceImagePath = "clients/123/refs/img2.jpg"`

#### Scenario: Out-of-range or unresolvable index — SourceImagePath is null (CAGH-PA-03-b)

- GIVEN the extractor returns a source index that does not correspond to any archived reference
  image
- WHEN the extraction response is mapped
- THEN `SourceImagePath = null`
- AND no exception is thrown and the rest of `BrandMarkDetection` is unaffected

---

### Requirement: CAGH-PA-04 — BoundingBox Is Normalised, Validated, and Guarded by a Minimum-Area Threshold

`BrandMarkDetection` MUST gain an additive nullable `BoundingBox` field, normalised to the 0..1
coordinate range relative to the source image. A bounding box that is malformed, out of the 0..1
range, or below a configured minimum-area guard MUST be rejected and MUST result in
`BoundingBox = null`, not an exception.

#### Scenario: Valid normalised bounding box is retained (CAGH-PA-04-a)

- GIVEN the extractor returns a bounding box within 0..1 bounds and above the minimum-area guard
- WHEN the extraction response is parsed
- THEN `BoundingBox` carries the normalised coordinates unchanged

#### Scenario: Bounding box below the minimum-area guard is rejected (CAGH-PA-04-b)

- GIVEN the extractor returns a bounding box whose area is below the configured minimum-area
  threshold
- WHEN the extraction response is parsed
- THEN `BoundingBox = null`
- AND no exception is thrown

#### Scenario: Malformed or out-of-range bounding box is rejected (CAGH-PA-04-c)

- GIVEN the extractor returns coordinates outside the 0..1 range or a malformed shape
- WHEN the extraction response is parsed
- THEN `BoundingBox = null`
- AND no exception is thrown

#### Scenario: Bounding-box reliability is measured independently of rendering (CAGH-PA-04-d)

- GIVEN a batch of extraction runs against real client feeds
- WHEN `BoundingBox` acceptance/rejection outcomes are recorded (WU-6b measurement)
- THEN the reliability measurement is recorded and available before any brand-mark render change
  (`content-generation` `CAGH-CG-05`/`CAGH-CG-06`) depends on it

---

## MODIFIED Requirements

### Requirement: FFS-PA-02 — BrandMark Persisted as Additive Nullable Field, No EF Migration

`ClientAestheticProfile` MUST carry a nullable `BrandMark` field populated from the extractor
output, persisted additively as a JSON key inside the existing `aesthetic_profile_json` blob column
via `AestheticProfilePersistence`. No EF Core migration is required or permitted. Pre-change blobs
MUST deserialize with `BrandMark = null` and no exception. `BrandMark` now additionally carries
`MarkType`, `Text`, `SourceImagePath`, and `BoundingBox` (`CAGH-PA-01`..`CAGH-PA-04`), each
independently additive-nullable and defensively parsed; a blob persisted before this change, or by
an extractor version that did not populate these four fields, MUST deserialize with all four as
null and MUST NOT throw.
(Previously: `BrandMark` carried only `Present`, `PositionHint`, and `ColorHex`.)

#### Scenario: Round-trip preserves BrandMark (FFS-PA-02-a)

- GIVEN a profile with a populated `BrandMark`
- WHEN persisted and read back
- THEN `BrandMark` matches with no data loss

#### Scenario: Pre-change blob reads as null (FFS-PA-02-b)

- GIVEN a stored blob with no `brandMark` key (persisted before this change)
- WHEN deserialized into `ClientAestheticProfile`
- THEN `BrandMark` is null; no exception is thrown; all pre-existing fields intact

#### Scenario: Blob with only the pre-hardening BrandMark shape deserializes the four new fields as null (FFS-PA-02-c, new)

- GIVEN a stored blob with a `brandMark` object carrying only `present`/`positionHint`/`colorHex`
  (persisted by the version before this change)
- WHEN deserialized into `ClientAestheticProfile`
- THEN `MarkType`, `Text`, `SourceImagePath`, and `BoundingBox` are all null
- AND `Present`, `PositionHint`, and `ColorHex` deserialize exactly as before
- AND no exception is thrown

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- Extractor prompt/schema changes live in `MultimodalAestheticStyleExtractor.cs`; index→`StoragePath`
  resolution happens inside the extractor, the only component aware of image-prompt ordering.
- `BrandMarkDetection.cs` gains the four additive nullable fields; `AestheticProfilePersistence.cs`
  gains the corresponding JSON keys.
- The icon-mark crop derivation (`IBrandMarkAssetExtractor`, SkiaSharp crop adapter) is a separate
  Application/Infrastructure concern that CONSUMES `SourceImagePath`/`BoundingBox`; it is covered by
  the `content-generation` delta's render-time requirements, not this spec.
