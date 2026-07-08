# Delta for Content Generation — Background Asset Reference

> Change: `studio-background-asset-ref` (Slice 1). Additive — no existing
> requirement text is modified. New behavior for how the background-only PNG
> is referenced inside `CompositionSpecEnvelope.BackgroundImageRef` and how
> consumers resolve it.

## ADDED Requirements

### Requirement: BGREF-01 — New Generations Store a Storage Path in BackgroundImageRef

For every NEW content generation job that captures a background-only PNG, the
`ContentGenerationJobProcessor` MUST upload that PNG via `IGeneratedAssetStorage`
and set `CompositionSpecEnvelope.BackgroundImageRef` to the returned storage
path. The processor MUST NOT embed a base64 `data:` URI into the persisted
envelope for new generations.

#### Scenario: New generation persists a storage path, not base64

- GIVEN a content generation job captures a background-only PNG for a slide
- WHEN the job persists `CompositionSpecEnvelope` to `composition_spec_json`
- THEN `BackgroundImageRef` is a storage path (not a `data:` URI)
- AND the persisted `composition_spec_json` size does not grow with the PNG's base64 payload

#### Scenario: MaterializeRunCommand copies the storage path, not base64, into draft markup

- GIVEN a `ContentGenerationFormatResult` whose `CompositionSpecJson` has a storage-path `BackgroundImageRef`
- WHEN `MaterializeRunCommand` copies it into `content_studio_items.draft_visual_spec_markup`
- THEN the materialized markup contains the same storage path
- AND no base64 payload is introduced during materialization

---

> **Deferred to Slice 2 (not in this change):** making the background-only PNG
> addressable/servable to the frontend (e.g. via `/items/{itemId}/assets/{assetIndex}`).
> Slice 2 MUST NOT register the background as a `FormatAssetRow` in
> `content_generation_format_assets` — that table is enumerated by package
> assembly (ZIP), package listing, Canva export, and delete/regenerate handlers
> with no asset-kind filter, so an extra row would double ZIP deliverables and
> corrupt asset counts. Serving mechanism is decided in Slice 2's own design.

---

### Requirement: BGREF-03 — PPTX Export Resolves Both Legacy and New BackgroundImageRef Forms

`PptxExportAdapter` MUST resolve `BackgroundImageRef` by detecting its form at
render time: a `data:` URI prefix MUST be decoded inline (legacy behavior); a
storage path MUST be opened via `IGeneratedAssetStorage`. Both forms MUST
produce a correctly rendered background Picture layer in the exported PPTX.
This requirement is back-compat only — no backfill of existing rows occurs.

#### Scenario: Legacy envelope with a base64 data-URI still exports correctly

- GIVEN a persisted `CompositionSpecEnvelope` whose `BackgroundImageRef` is a `data:image/png;base64,...` URI (pre-existing row)
- WHEN the PPTX export renders that slide
- THEN the background Picture layer is decoded inline from the data-URI
- AND the exported PPTX contains the correct background image

#### Scenario: New envelope with a storage path exports correctly

- GIVEN a persisted `CompositionSpecEnvelope` whose `BackgroundImageRef` is a storage path (new-form row)
- WHEN the PPTX export renders that slide
- THEN the background Picture layer is resolved by opening the asset via `IGeneratedAssetStorage`
- AND the exported PPTX contains the correct background image

---

### Requirement: BGREF-04 — Missing Background Reference Degrades Gracefully

If `CompositionSpecEnvelope.BackgroundImageRef` is null or empty (background
capture failed upstream), PPTX export MUST NOT throw and MUST NOT abort the
whole export. The affected slide MUST export without a background Picture
layer while all other layers (text/content components) remain present.

#### Scenario: Null BackgroundImageRef exports without a background layer

- GIVEN a persisted `CompositionSpecEnvelope` whose `BackgroundImageRef` is null
- WHEN the PPTX export renders that slide
- THEN the export completes successfully
- AND the slide has no background Picture layer
- AND text/content components for that slide are still present in the export

---

### Requirement: BGREF-05 — Markdown/Text Formats Are Unaffected

Formats that produce `CompositionOutputKind.Markdown` output (e.g. reel-script)
have no `CompositionSpecEnvelope` and MUST NOT be affected by this change.

#### Scenario: Reel-script generation and export behavior is unchanged

- GIVEN a reel-script generation job (Markdown output, no composition envelope)
- WHEN the job runs end to end and is packaged
- THEN the output is `reel-script.md` as before
- AND no `BackgroundImageRef` resolution logic is invoked
