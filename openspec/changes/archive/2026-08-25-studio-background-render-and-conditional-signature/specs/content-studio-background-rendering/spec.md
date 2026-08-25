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
no storage-provider type MAY leak into Domain/Application.

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
