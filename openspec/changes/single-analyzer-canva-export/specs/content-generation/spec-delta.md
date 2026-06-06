# Spec Delta: Content Generation — Editable PPTX Export + Designer Markup Persistence

<!-- Change: single-analyzer-canva-export (Fase B / B3 — #4 Editable Download) -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->

## Purpose

Specify the observable requirements introduced by B3 in the content-generation capability:

1. **Designer-markup persistence** — the validated markup produced during render MUST be persisted with
   the format result so on-demand export is deterministic and does not require a second composition call.
2. **Editable PPTX download** — a user MUST be able to download any generated format as an editable
   `.pptx` file in which text slots become real, editable text boxes and the decorative background
   becomes a flattened image layer.

The Canva export flow (Design Import, per-user OAuth) is covered in the integrations delta.
B1b designer/renderer contract changes are explicitly OUT of scope for this delta.

---

## PRD Traceability

| Requirement ID | PRD Anchor |
|---|---|
| PPTX-01 | PRD `docs/PRD.md` — review item #4 "editable download" |
| PPTX-02 | PRD `docs/PRD.md` — review item #4 "editable download" |
| PPTX-03 | PRD `docs/PRD.md` — review item #4 "editable download" |
| PPTX-04 | PRD `docs/PRD.md` — review item #4 "editable download" + review item #5 "Export to Canva" (shared persistence dependency) |

---

## ADDED Requirements

---

### Requirement: PPTX-01 — Validated Designer Markup Is Persisted with the Format Result

After a successful render, the guardrail-validated designer markup MUST be persisted as part of the
format-result record. A format result whose render succeeded MUST NOT store a null markup value.

This requirement exists because the on-demand PPTX export and Canva export paths both retrieve markup
after generation completes — retrieving it later without re-composing is possible only if the markup was
saved at render time.

#### Scenario: Markup is stored after a successful render (PPTX-01-a)

- GIVEN a content-generation job that produces a successful render for a given format
- WHEN the format result is persisted
- THEN the stored format-result record contains the guardrail-validated designer markup as a non-null value
- AND the stored markup is identical to the markup that passed guardrail validation

#### Scenario: Markup stored for every format in a multi-format job (PPTX-01-b)

- GIVEN a content-generation job that renders two or more formats (e.g. flyer and carousel)
- WHEN all format results are persisted
- THEN each successfully-rendered format result contains its own non-null markup

#### Scenario: Failed render does not store markup (PPTX-01-c)

- GIVEN a content-generation job where one format fails guardrail validation
- WHEN the failure is persisted
- THEN the failed format result does NOT store a markup value
- AND no exception is thrown; the failure is a structured, handled result

---

### Requirement: PPTX-02 — A User Can Download a Generated Format as an Editable PPTX

A user MUST be able to trigger a download of any generated format as a `.pptx` file. The system MUST
derive the editable presentation from the persisted designer markup for that format without calling the
composition model again.

The downloaded file MUST use the MIME type
`application/vnd.openxmlformats-officedocument.presentationml.presentation`.

#### Scenario: Text slots become editable text boxes (PPTX-02-a)

- GIVEN a format result with persisted markup containing `data-slot` elements (e.g. `headline`, `subtitle`, `cta`)
- WHEN the user requests the PPTX download
- THEN the downloaded `.pptx` contains editable text boxes corresponding to the `data-slot` elements
- AND each text box contains the text content of its corresponding slot
- AND the text boxes are independently editable in PowerPoint or Keynote (not images, not locked)

#### Scenario: Decorative background becomes a flattened image layer (PPTX-02-b)

- GIVEN a format result with persisted markup that contains a `data-layer="background"` element
- WHEN the user requests the PPTX download
- THEN the downloaded `.pptx` contains the background as a raster image layer behind the text boxes
- AND the background image layer is NOT an editable text box

#### Scenario: Slide dimensions match the source format (PPTX-02-c)

- GIVEN a flyer format (portrait, e.g. 1080×1350 px) and a carousel format (square, e.g. 1080×1080 px)
- WHEN each is exported to PPTX
- THEN the flyer `.pptx` slide has portrait dimensions matching the flyer canvas
- AND the carousel `.pptx` slide has square dimensions matching the carousel canvas
- AND neither presentation uses a default widescreen (16:9) slide size

#### Scenario: Format with no persisted markup yields a handled error (PPTX-02-d)

- GIVEN a format result whose markup was not persisted (e.g. it predates this change)
- WHEN the user requests the PPTX download for that format
- THEN the system returns a clear, user-facing error indicating the asset is not available for editable export
- AND the system does NOT crash or return a server error without explanation

---

### Requirement: PPTX-03 — PPTX Generation Is Deterministic and Offline

Generating a PPTX from a stored markup must not depend on browser rendering, live network calls, or
AI composition. Given the same markup and the same format dimensions, repeated PPTX generation MUST
produce structurally identical presentations (same slide count, same text-box content, same background
presence).

This requirement enables offline testing and ensures that an export request never triggers a
non-deterministic Opus composition.

#### Scenario: Repeated export of the same format produces structurally identical output (PPTX-03-a)

- GIVEN a format result with persisted markup and known dimensions
- WHEN PPTX export is requested twice in succession (without any intervening composition call)
- THEN both responses have the same slide count, text-box count, background-layer presence,
  and text content
- AND no AI composition call is made during either export

#### Scenario: PPTX generation succeeds without a live Chromium session or network connection (PPTX-03-b)

- GIVEN a format result with persisted markup and a PPTX export request
- WHEN the request is processed in an environment where all outbound network calls are blocked
  and no browser is launched
- THEN the PPTX is generated successfully (text boxes + background layer present)
- AND no timeout or network error occurs

---

### Requirement: PPTX-04 — Persisted Markup Is the Source of Truth for Both Export Paths

The same persisted markup that drives PPTX export (PPTX-02) MUST also be the input that the
Export-to-Canva path uses to build the PPTX before uploading it to Canva Design Import
(see integrations delta, CANVA-EXPORT-02). There MUST be a single retrieval path; the two export
paths MUST NOT re-compose or re-render independently.

#### Scenario: Export-to-PPTX and export-to-Canva use the same stored markup (PPTX-04-a)

- GIVEN a format result with persisted markup
- WHEN the user requests a direct PPTX download AND subsequently exports to Canva
- THEN both paths use the identical stored markup (same content, same dimensions)
- AND neither path triggers a new AI composition or a new Chromium render

---

## MODIFIED Requirements

None. All requirements in this delta are additive; no existing `openspec/specs/content-generation/spec.md`
requirements are modified.

---

## REMOVED Requirements

None.

---

## Implementation Reconciliation (WU-8)

Completed in change `single-analyzer-canva-export` (Fase B / B3). All requirements in this delta are
implemented and verified. Work units responsible:

| Requirement | Work Unit | Status |
|---|---|---|
| PPTX-01 (markup persistence) | WU-1 | Implemented. `ContentGenerationJobProcessor.PersistRenderSuccessAsync` writes a versioned `CompositionSpecEnvelope` (schemaVersion 1) to `composition_spec_json`. Null is never written on the success path. |
| PPTX-02 (editable PPTX download) | WU-2 + WU-3 | Implemented. `PptxExportAdapter` (OpenXML) builds the PPTX offline. Endpoint: `GET /api/social/single-analyzer/{analysisId}/formats/{formatKey}/download-pptx?jobId={jobId}`. |
| PPTX-03 (deterministic, offline) | WU-2 | Implemented. PPTX generation never calls Chromium or the AI composition model. Same input → same structural output (verified by `BuildAsync_Deterministic_SameInputSameStructure`). |
| PPTX-04 (single markup read path) | WU-3 + WU-7 | Implemented. Both `ExportFormatToPptxQueryHandler` and `ExportFormatToCanvaCommandHandler` read from `composition_spec_json`; neither triggers re-render or re-composition. |

### Status code note (PPTX-02-d / "not exportable")

The spec scenario PPTX-02-d says the system returns a "clear, user-facing error" when markup is not
persisted. The implementation returns **HTTP 409 Conflict** with error code `content-gen.not_exportable`.
HTTP 422 is not in the project's error-type infrastructure — `ErrorType.Conflict` maps to 409 via
`ResultExtensions.StatusCodeFor`. The observable behavior matches the intent of the scenario.
