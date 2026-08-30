# Delta: Profile Analysis — Conceptual Method Detection, Image-Type Ratio, Paired Input

<!-- Change: client-aesthetic-conceptual-detection -->
<!-- Applies to: openspec/specs/profile-analysis/spec.md (additive delta) -->

## Purpose

Enrich the multimodal aesthetic extractor and its downstream model chain with three
additive capabilities:

1. **Conceptual/art-direction method signal** — captures HOW a client turns a message
   into an image (metaphorical-editorial vs. literal-illustration) rather than only
   the visual surface.
2. **Robust image-type composition ratio** — a statistically grounded photo-vs-flat
   frequency across the client's feed, robustifying the existing `SlideLayoutPattern`
   foundation.
3. **Ordered image↔caption pair input** — feeds the analyzer paired (image, caption)
   inputs so the model can learn the message→image metaphor mapping.

All new fields are additive nullable. Cold-start clients (no conceptual data) MUST
produce output identical to pre-change behavior.

---

## PRD Traceability

| Requirement ID | Proposal Anchor |
|---|---|
| CACD-PA-01 | §Scope — Enrich detection (conceptual method) |
| CACD-PA-02 | §Scope — Enrich detection (robust ratio) |
| CACD-PA-03 | §Scope — Image↔caption pairing input |
| CACD-PA-04 | §Scope — Carry fields through AestheticExtraction (additive) |
| CACD-PA-05 | §Scope — Carry fields through ClientAestheticProfile + persistence |
| CACD-PA-06 | §Scope — Real vision model on ai-aesthetic step |
| CACD-PA-07 | §Success Criteria — Cold-start parity |

---

## Client Exemplars (Anchors for Acceptance Tests)

| Client | Method | Ratio | Visual Identity |
|---|---|---|---|
| Atlas (`@atlas.editorial`) | Metaphorical-editorial: idea-driven visual metaphors via mono-color still-life and staged photography | ≈50% photo / 50% flat-color | Saturated mono-color backgrounds; conceptual still-life; staged photography |
| Comet Comedy | Literal-illustration: tender watercolor illustrations of children with warm cream palette | Predominantly illustration (low photo frequency) | Warm cream; hand-drawn children; pastel decorative icons |

These two exemplars MUST be distinguishable by CACD-PA-01 and CACD-PA-02 outputs and
MUST be used as the canonical validation anchors in spec/integration tests.

---

## ADDED Requirements

### Requirement: CACD-PA-01 — Extractor DTO Gains Nullable `ConceptualMethod` Field

The multimodal aesthetic extractor response DTO MUST include a nullable `ConceptualMethod`
field. The field captures the client's art-direction method — the image↔message creative
relationship — as a free-text string or a structured value that carries at minimum a
human-readable description. When the method is not determinable from the feed, the field
MUST be null. The addition MUST be backward-compatible; extractor consumers that do not
read this field are unchanged.

The field MUST distinguish a metaphorical-editorial method (Atlas exemplar) from a
literal-illustration method (Comet exemplar) when both are detectable.

#### Scenario: Metaphorical-editorial method detected — Atlas exemplar (CACD-PA-01-a)

- GIVEN a multimodal extraction run against Atlas's feed, where each image is a
  visual metaphor of a post message (e.g., staged still-life objects representing
  abstract psychology concepts, saturated mono-color backgrounds)
- WHEN the extractor produces its response DTO
- THEN `ConceptualMethod` contains a description identifying a metaphorical or
  editorial art-direction method (e.g., references conceptual still-life, staged
  photography, or metaphor-driven image construction)
- AND the description is NOT compatible with a literal-illustration characterization

#### Scenario: Literal-illustration method detected — Comet exemplar (CACD-PA-01-b)

- GIVEN a multimodal extraction run against Comet Comedy' feed, where images
  are tender hand-drawn watercolor illustrations of children with warm cream backgrounds
- WHEN the extractor produces its response DTO
- THEN `ConceptualMethod` contains a description identifying a literal or
  illustration-based art-direction method (e.g., references watercolor illustration,
  literal depiction, or tender character illustration)
- AND the description is NOT compatible with a metaphorical-editorial characterization

#### Scenario: No determinable method — `ConceptualMethod` null; extraction succeeds (CACD-PA-01-c)

- GIVEN a feed with ambiguous or inconsistent images that do not reveal a clear
  art-direction method
- WHEN the extractor produces its DTO
- THEN `ConceptualMethod` is null
- AND the extraction completes successfully with no error raised
- AND all other DTO fields are unaffected

---

### Requirement: CACD-PA-02 — Extractor DTO Gains Nullable `ImageTypeRatio` Field

The multimodal aesthetic extractor response DTO MUST include a nullable `ImageTypeRatio`
field representing the relative frequency of photographic/staged-photography slides
versus flat-color slides across the client's feed. The ratio MUST be statistically
grounded across multiple posts in the feed sample (not inferred from a single carousel).
When no reliable ratio can be determined, the field MUST be null.

The field MUST distinguish Atlas's approximately equal photo/flat mix from Comet's
predominantly illustration (low photo frequency) composition.

#### Scenario: Photo/flat ratio detected — Atlas exemplar ≈50/50 (CACD-PA-02-a)

- GIVEN a feed sample from Atlas's profile with approximately equal counts of
  photographic/staged slides and flat-color/mono slides across multiple carousels
- WHEN the extractor produces its DTO
- THEN `ImageTypeRatio` reflects approximately 50% photographic and 50% flat-color
  (within a reasonable margin, e.g., ±20%)
- AND the ratio is grounded across multiple posts, not a single carousel

#### Scenario: Low-photo ratio detected — Comet exemplar (CACD-PA-02-b)

- GIVEN a feed sample from Comet Comedy with predominantly watercolor
  illustration slides and few photographic slides
- WHEN the extractor produces its DTO
- THEN `ImageTypeRatio` reflects a significantly lower photographic frequency than
  Atlas's ratio (the two values MUST be distinguishable)

#### Scenario: Insufficient sample — `ImageTypeRatio` null; extraction succeeds (CACD-PA-02-c)

- GIVEN a feed with too few posts to ground a statistically meaningful ratio
- WHEN the extractor produces its DTO
- THEN `ImageTypeRatio` is null
- AND the extraction completes successfully with no error raised

---

### Requirement: CACD-PA-03 — Extractor Receives Ordered Image↔Caption Pairs; Best-Effort Fallback When Captions Absent

The `IClientAestheticStyleExtractor` (and its implementation `MultimodalAestheticStyleExtractor`)
MUST accept the client's content as an ordered sequence of `{Image, Caption?}` pairs rather
than a loose caption block. When a caption is present for an image, the extractor MUST
receive them together so it can infer the message→image metaphor mapping. When a caption
is absent or the caption list is shorter than the image list, the affected images MUST be
passed image-only. The extraction MUST NOT fail when captions are missing, fewer than
images, or misaligned.

#### Scenario: All image↔caption pairs present — paired input passed (CACD-PA-03-a)

- GIVEN a list of N images each paired with a matching caption
- WHEN the extractor is called with the ordered pairs
- THEN each image and its caption are passed together as a pair
- AND the extractor can reason about the message→image method across all pairs

#### Scenario: Some captions absent — image-only fallback for those positions (CACD-PA-03-b)

- GIVEN a list of 5 images where positions 1 and 3 have no matching caption
- WHEN the extractor is called
- THEN images at positions 0, 2, 4 are passed with their captions
- AND images at positions 1, 3 are passed image-only (no caption argument)
- AND the extraction completes successfully with no error raised

#### Scenario: Caption list shorter than image list — trailing images passed image-only (CACD-PA-03-c)

- GIVEN 5 images and 3 captions
- WHEN the extractor is called
- THEN the first 3 images are paired with captions
- AND the last 2 images are passed image-only
- AND no index-out-of-range or alignment exception is thrown

#### Scenario: No captions available — all images passed image-only (CACD-PA-03-d)

- GIVEN N images and 0 captions
- WHEN the extractor is called
- THEN all N images are passed image-only
- AND the extraction proceeds and produces a result
- AND no error is raised

---

### Requirement: CACD-PA-04 — `AestheticExtraction` Carries `ConceptualMethod` and `ImageTypeRatio` as Additive Nullable Fields

The Application-layer `AestheticExtraction` value object MUST include nullable
`ConceptualMethod` and `ImageTypeRatio` fields, populated from the extractor DTO.
Both fields MUST be additive; no existing field is modified or removed. When the
corresponding extractor DTO fields are null, the `AestheticExtraction` fields MUST be null.
No provider, EF Core, or Infrastructure type MAY appear in `AestheticExtraction`.

#### Scenario: Both fields populated from DTO (CACD-PA-04-a)

- GIVEN an extractor DTO with non-null `ConceptualMethod` and non-null `ImageTypeRatio`
- WHEN mapped to `AestheticExtraction`
- THEN `AestheticExtraction.ConceptualMethod` carries the DTO value
- AND `AestheticExtraction.ImageTypeRatio` carries the DTO value

#### Scenario: Both fields null in DTO — AestheticExtraction fields null (CACD-PA-04-b)

- GIVEN an extractor DTO with null `ConceptualMethod` and null `ImageTypeRatio`
- WHEN mapped to `AestheticExtraction`
- THEN `AestheticExtraction.ConceptualMethod` is null
- AND `AestheticExtraction.ImageTypeRatio` is null

#### Scenario: Existing fields are unaffected by the addition (CACD-PA-04-c)

- GIVEN the completed implementation
- WHEN `AestheticExtraction` is compiled and its fields enumerated
- THEN all pre-change fields (palette, styleDescriptors, motifs, captionVoice,
  slideMixPattern, etc.) retain their types and semantics
- AND no field is removed or renamed

---

### Requirement: CACD-PA-05 — `ClientAestheticProfile` Carries New Fields; Persistence Round-Trip Preserves Them; Old Blobs Read as Null

`ClientAestheticProfile` MUST carry nullable `ConceptualMethod` and `ImageTypeRatio`
fields populated from `AestheticExtraction`. Both fields MUST be persisted additively
by `AestheticProfilePersistence`. A stored profile blob persisted before this change
(containing no `ConceptualMethod` or `ImageTypeRatio` entries) MUST deserialize with
those fields as null and MUST NOT raise an exception.

#### Scenario: New fields round-trip through persistence (CACD-PA-05-a)

- GIVEN a `ClientAestheticProfile` with non-null `ConceptualMethod` and `ImageTypeRatio`
- WHEN persisted and read back via `AestheticProfilePersistence`
- THEN `ConceptualMethod` and `ImageTypeRatio` match the persisted values with no data loss

#### Scenario: Pre-change blob reads as null without error (CACD-PA-05-b)

- GIVEN a stored profile blob that has no `ConceptualMethod` or `ImageTypeRatio` entries
  (persisted by a version of the system before this change)
- WHEN deserialized into `ClientAestheticProfile`
- THEN `ConceptualMethod` is null and `ImageTypeRatio` is null
- AND no exception is thrown and all pre-existing fields are intact

#### Scenario: Null fields persist and read back as null (CACD-PA-05-c)

- GIVEN a `ClientAestheticProfile` with `ConceptualMethod = null` and `ImageTypeRatio = null`
- WHEN persisted and read back
- THEN both fields remain null with no error

---

### Requirement: CACD-PA-06 — `ai-aesthetic` Step Routes to a Vision-Capable Model; `claude -p` Is Prohibited

The `ai-aesthetic` / `extract-aesthetic` workflow step MUST route to a model that
accepts image input (a genuine vision-capable model). The step MUST NOT route to
`claude -p` (the Claude Code CLI agent), which converses and does not honor JSON
contracts. This is the ONLY routing change in this change; all other workflow step
routing is unaffected.

#### Scenario: Vision model receives images and returns JSON contract (CACD-PA-06-a)

- GIVEN a configured `ai-aesthetic` step targeting a vision-capable model
- WHEN a profile analysis job runs the aesthetic extraction step
- THEN the model receives image inputs alongside the extraction instruction
- AND the model returns a response conforming to `AestheticExtractionDto`
- AND the `claude -p` binary is NOT invoked

#### Scenario: Other workflow steps are unaffected (CACD-PA-06-b)

- GIVEN the completed routing configuration
- WHEN workflow steps other than `ai-aesthetic` run
- THEN their model/provider routing is unchanged from the pre-change configuration

---

### Requirement: CACD-PA-07 — Cold-Start Parity: Null Conceptual Fields Produce Pre-Change Output

When `ConceptualMethod` and `ImageTypeRatio` are both null on `ClientAestheticProfile`,
every downstream consumer MUST produce output identical to the output produced before
this change. No default value, placeholder text, or behavior change MAY be applied
when these fields are absent.

#### Scenario: Cold-start client produces identical output (CACD-PA-07-a)

- GIVEN a `ClientAesthetic` client whose `ClientAestheticProfile` has
  `ConceptualMethod = null` and `ImageTypeRatio = null`
- WHEN content generation runs (image prompt writing and any downstream steps)
- THEN the image prompts and generated outputs are identical to the outputs produced
  by the pre-change system for the same inputs
- AND no ideation section, ratio section, or new instruction block appears in any output

---

## MODIFIED Requirements

None. All requirements above are additive. Pre-existing requirements
`PA-SEAM-01`–`PA-SEAM-03`, `CAG-PA-01`–`CAG-PA-06` remain valid and unchanged.
The `IClientAestheticStyleExtractor` input change (CACD-PA-03) replaces the loose
caption block with ordered pairs; the signature shape is a design-level decision
(OQ-3) and does not alter any previously specified behavioral guarantee for the
output contract.

## REMOVED Requirements

None.
