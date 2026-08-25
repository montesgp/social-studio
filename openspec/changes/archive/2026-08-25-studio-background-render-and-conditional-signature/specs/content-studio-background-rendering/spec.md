# Delta: Content Studio Renders AI Backgrounds by URL/Reference (Slice 2)

<!-- Change: studio-background-render-and-conditional-signature (Issue #58) -->
<!-- Applies to: openspec/specs/content-generation/spec.md (BGREF-01, BGREF-03, BGREF-04) -->

## Purpose

Slice 1 (#56) persists `CompositionSpecEnvelope.BackgroundImageRef` as a storage path
(with legacy `data:` URI back-compat) but never exposes it to the frontend. Slice 2
makes each slide's AI background addressable and renders it in the studio and package
preview, without registering it as a `content_generation_format_assets` row.

## ADDED Requirements

### Requirement: SBR-01 — Slide Background Is Addressable and Rendered in the Studio

For every slide whose `CompositionSpecEnvelope.BackgroundImageRef` is set, the content
studio/package preview MUST resolve and render that background image by URL/reference,
layered behind the slide's composed layout/text. The background MUST be resolved
through the existing asset-serving port (`IGeneratedAssetStorage` or equivalent) —
no storage-provider type MAY leak into Domain/Application. The same resolver MUST additionally
resolve a slide's brand-mark asset ref (`CAGH-SBR-01`) using the identical mechanism; a slide may
carry a background ref, a brand-mark ref, both, or neither, and each resolves independently.
(Previously: only `BackgroundImageRef` was resolved; a brand-mark storage path reached the browser
unresolved. Amended by `client-aesthetic-generation-hardening`, 2026-08-25.)

#### Scenario: Slide with a storage-path background renders in the studio

- GIVEN a slide whose `BackgroundImageRef` is a storage path (new-form row)
- WHEN the studio/package preview loads that slide
- THEN the AI background image renders behind the slide's layout/text layer
- AND the image is fetched by URL/reference, not embedded inline

#### Scenario: Slide with a legacy data-URI background still renders (back-compat)

- GIVEN a slide whose `BackgroundImageRef` is a `data:image/png;base64,...` URI (pre-existing row)
- WHEN the studio/package preview loads that slide
- THEN the AI background image renders behind the slide's layout/text layer
- AND resolution mirrors the same legacy-form handling PPTX export already applies (BGREF-03)

#### Scenario: Slide with no background renders without error

- GIVEN a slide whose `BackgroundImageRef` is null or empty
- WHEN the studio/package preview loads that slide
- THEN the slide renders its layout/text layer with no background image
- AND no error or broken-image state is shown

#### Scenario: Slide with both a background ref and a brand-mark ref resolves both independently (new)

- GIVEN a slide whose `BackgroundImageRef` is a storage path AND whose brand-mark slot references a
  stored crop asset
- WHEN the studio/package preview loads that slide
- THEN both the background image and the brand-mark image resolve and render
- AND resolving one does not block or invalidate resolution of the other

### Requirement: SBR-02 — Background Exposure MUST NOT Add a FormatAsset Row

Serving the background to the frontend MUST NOT insert a row into
`content_generation_format_assets`. That table is enumerated without an asset-kind
filter by package assembly (ZIP), package listing, Canva export, and delete/regenerate
handlers; an extra row would double ZIP deliverables and corrupt asset counts.

#### Scenario: ZIP/package deliverable count is unchanged after background exposure

- GIVEN a generated package with N composed slide assets (`slide_1.png`..`slide_N.png`)
- WHEN the studio/package preview renders each slide's AI background by URL/reference
- THEN the ZIP/export deliverable count remains N (no additional `FormatAssetRow` for backgrounds)
- AND package listing and Canva export enumerate the same N assets as before

#### Scenario: Delete/regenerate flows are unaffected by background exposure

- GIVEN an item whose slides have addressable AI backgrounds
- WHEN a slide asset is deleted or regenerated
- THEN the delete/regenerate operation enumerates only `FormatAsset` rows (unchanged count)
- AND the background reference resolution is unaffected by the delete/regenerate operation

---

## Added Requirements: Brand-Mark Ref Resolution (client-aesthetic-generation-hardening)

### Requirement: CAGH-SBR-01 — StudioVisualMarkupResolver Resolves the Brand-Mark Asset Ref

`StudioVisualMarkupResolver` MUST resolve a slide's brand-mark asset ref (the storage path emitted
by icon-mark rendering) through the same asset-serving port (`IGeneratedAssetStorage` or equivalent)
already used for `BackgroundImageRef`, so the Studio preview can fetch it by URL/reference. A slide
with no brand-mark asset ref MUST resolve with no change to its existing background/markup
resolution.

#### Scenario: Slide with a brand-mark storage-path ref resolves in the Studio preview (CAGH-SBR-01-a)

- GIVEN a slide whose brand-mark slot references a stored crop asset by storage path
- WHEN the Studio preview loads that slide
- THEN the brand-mark `<img>` resolves to a fetchable reference and renders
- AND no broken-image state is shown

#### Scenario: Slide with no brand-mark ref is unaffected (CAGH-SBR-01-b, regression)

- GIVEN a slide with no brand-mark asset ref (text mark, chip fallback, or no mark)
- WHEN the Studio preview loads that slide
- THEN resolution behaves exactly as before this change; no brand-mark resolution is attempted

**Implementation note (non-normative)**: `StudioVisualMarkupResolver` already inlines every
`src="..."` attribute in the composed markup, not only `BackgroundImageRef` — this requirement was
already satisfied by the existing implementation with no code change. Verified by two dedicated
regression tests (`tasks.md`, WU-7c task 7c.4).

---

### Requirement: CAGH-SBR-02 — PPTX Export Also Resolves the Brand-Mark Asset Ref

`PptxExportAdapter`, which reads the same composition refs as the Studio resolver, MUST also resolve
the brand-mark asset ref so an exported PPTX renders the icon crop rather than a broken/missing
image reference.

#### Scenario: PPTX export resolves the brand-mark crop (CAGH-SBR-02-a)

- GIVEN a slide with a stored brand-mark crop asset ref
- WHEN the item is exported to PPTX
- THEN the exported slide includes the resolved brand-mark image
- AND no unresolved storage-path string leaks into the exported file

## Removed Requirements (client-aesthetic-generation-hardening)

None.

*Merged from `openspec/changes/client-aesthetic-generation-hardening/specs/content-studio-background-rendering/spec-delta.md`
— change applied 2026-08-25. This target file is itself an archived-change spec snapshot (no
canonical `openspec/specs/content-studio-background-rendering/` directory exists yet); merged here
per this change's own spec-delta header, which named this exact path as its target.*
