# Spec Delta: Content Studio Background Rendering — Brand-Mark Ref Resolution

<!-- Change: client-aesthetic-generation-hardening -->
<!-- Applies to: openspec/changes/archive/2026-08-25-studio-background-render-and-conditional-signature/specs/content-studio-background-rendering/spec.md (additive delta; MODIFIED SBR-01) -->

## Purpose

`StudioVisualMarkupResolver` today resolves only the envelope's `BackgroundImageRef` literal to a
fetchable reference. Once icon-mark rendering (`content-generation` `CAGH-CG-05`) emits an `<img>`
pointing at a stored brand-mark crop, that ref reaches the browser unresolved unless the resolver —
and the `PptxExportAdapter` export path, which reads the same refs — cover it too. Without this the
icon renders as a broken image in Studio and in export.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAGH-SBR-01 | Scope item 10 — knock-on requirement: resolver must cover the brand-mark ref |
| CAGH-SBR-02 | Success Criteria — brand-mark ref resolves in Studio preview AND PPTX export |

---

## ADDED Requirements

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

---

## MODIFIED Requirements

### Requirement: SBR-01 — Slide Background Is Addressable and Rendered in the Studio

For every slide whose `CompositionSpecEnvelope.BackgroundImageRef` is set, the content
studio/package preview MUST resolve and render that background image by URL/reference,
layered behind the slide's composed layout/text. The background MUST be resolved
through the existing asset-serving port (`IGeneratedAssetStorage` or equivalent) —
no storage-provider type MAY leak into Domain/Application. The same resolver MUST additionally
resolve a slide's brand-mark asset ref (`CAGH-SBR-01`) using the identical mechanism; a slide may
carry a background ref, a brand-mark ref, both, or neither, and each resolves independently.
(Previously: only `BackgroundImageRef` was resolved; a brand-mark storage path reached the browser
unresolved.)

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

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- `StudioVisualMarkupResolver.cs` (Application) and `PptxExportAdapter.cs` (Infrastructure) both
  read the same composition-envelope refs; both MUST be extended together so Studio preview and
  export stay consistent (this was the "knock-on requirement found during this revision" in the
  proposal).
- No new `content_generation_format_assets` row is implied by brand-mark ref resolution, mirroring
  `SBR-02`'s existing constraint for background refs.
