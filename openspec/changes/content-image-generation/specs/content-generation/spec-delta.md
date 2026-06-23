# Spec Delta: Content Generation — Generated Raster Backgrounds for ClientAesthetic Creative Slides

<!-- Change: content-image-generation -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->

## Purpose

Introduce per-slide creative-vs-flat routing for `ClientAesthetic` carousel generation:
designated creative slides receive a GENERATED raster background composited as an
editable-text slide; flat slides keep the existing CSS designer path.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CIG-CG-01 | Proposal §Scope — per-slide creative-vs-flat routing; CSS for flat slides |
| CIG-CG-02 | Proposal §Scope — editable text over generated raster (BackgroundSelection.ForcedDefault) |
| CIG-CG-03 | Proposal §Scope — CSS fallback on image-gen failure; job always completes |
| CIG-CG-04 | Proposal §Scope — two-step: text route writes image prompt; image route renders pixels |

---

## ADDED Requirements

### Requirement: CIG-CG-01 — ClientAesthetic Carousels Route Slides as Creative or Flat

For `ClientAesthetic` carousel jobs, each slide MUST be classified as either **creative**
(from the client's configured creative-slide set) or **flat** before compositing begins.
Flat slides MUST continue to use the existing CSS designer path unchanged.

#### Scenario: Creative slides identified by position; flat slides use CSS (CIG-CG-01-a)

- GIVEN a `ClientAesthetic` carousel with 5 slides and a creative-slide set = `{0, 4}` (first + last)
- WHEN the carousel generator prepares slide assignments
- THEN slides at positions 0 and 4 are marked as creative (image-gen path)
- AND slides at positions 1, 2, 3 are marked as flat (CSS designer path)
- AND the CSS designer is invoked for flat slides exactly as before this change

#### Scenario: No creative-slide set configured — all slides use CSS designer (CIG-CG-01-b)

- GIVEN a `ClientAesthetic` client with no creative-slide set on `ClientAestheticProfile`
- WHEN the carousel generator runs
- THEN all slides take the CSS designer path (no image-gen attempted)
- AND the job completes normally

#### Scenario: Non-ClientAesthetic clients are unaffected (CIG-CG-01-c)

- GIVEN a client with `BackgroundSourcingPolicy` other than `ClientAesthetic`
- WHEN a carousel job runs
- THEN no creative-vs-flat routing is applied
- AND the existing carousel path executes unchanged

---

### Requirement: CIG-CG-02 — Generated Background Produces an Editable-Text Slide

For creative slides, the generated raster image URL MUST be injected as
`BackgroundSelection.ForcedDefault(url, "ai-generated-background")` so the existing
per-slide compositing path places the image as `data-layer=background` with
`data-layer=content` text remaining fully editable in the Content Studio.

#### Scenario: Text on a generated-background slide is editable in the studio (CIG-CG-02-a)

- GIVEN a creative slide whose background was generated and stored in Supabase
- WHEN the composition envelope is built using `BackgroundSelection.ForcedDefault(url, ...)`
- THEN the slide's background layer carries the raster image URL
- AND the slide's content layer carries text fields rendered as editable in the Content Studio
- AND the studio can modify text without touching the background layer

#### Scenario: Composition envelope carries a URL, not inline bytes (CIG-CG-02-b)

- GIVEN a successful image render and storage upload
- WHEN the `BackgroundSelection.ForcedDefault` value is built
- THEN the background reference is a durable URL or storage path
- AND `composition_spec_json` contains no base-64 or data-URI representation of the image

---

### Requirement: CIG-CG-03 — CSS Designer Fallback on Any Image-Gen Failure; Job Always Completes

If any step of the image-gen pipeline fails for a creative slide (prompt write, render,
or storage upload), the CSS designer background MUST be used for that slide. The failure
MUST be logged. The job MUST NOT be marked failed on image-gen error.

#### Scenario: Image-gen failure for one slide — other slides and job unaffected (CIG-CG-03-a)

- GIVEN a `ClientAesthetic` carousel with creative slides at positions 0 and 4
- AND image-gen fails (any reason) for position 0
- WHEN the carousel completes
- THEN slide 0 uses the CSS designer background
- AND slide 4 proceeds through image-gen normally (independent of slide 0's failure)
- AND the job completes with a success status
- AND the failure is logged with job id, slide index, and reason

#### Scenario: All image-gen steps fail — job completes with all-CSS output (CIG-CG-03-b)

- GIVEN image-gen fails for every creative slide in a job
- WHEN the carousel completes
- THEN all slides use the CSS designer background
- AND the job completes normally (no job-level failure raised)

---

### Requirement: CIG-CG-04 — Two-Step Flow Drives Each Creative Slide Independently

Each creative slide MUST execute the two-step flow (prompt write → pixel render)
independently. A failure in one slide's two-step sequence MUST NOT abort the sequence
for another slide.

#### Scenario: Per-slide two-step flow; each slide's failure is isolated (CIG-CG-04-a)

- GIVEN a carousel with two creative slides
- AND the render step fails for slide 0 (provider error)
- WHEN the carousel generator processes slide 4 next
- THEN slide 4's two-step flow executes independently
- AND slide 4 can succeed even though slide 0 fell back to CSS
- AND both outcomes are logged

---

## MODIFIED Requirements

None. All requirements in this delta are additive. No existing
`openspec/specs/content-generation/spec.md` requirement is changed in meaning or removed.

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- CIG-CG-01: creative-vs-flat classification lives in the carousel generator (Application).
  The `ClientAestheticProfile.CreativeSlideSet` (nullable) is the source; absence = all CSS.
- CIG-CG-02: `BuildTemplateBackedMarkup` receives the `ForcedDefault` background exactly as it
  does for client-defined backgrounds — near-zero compositing change.
- CIG-CG-03/CIG-CG-04: resilience boundary is a try/catch per creative slide before compositing;
  each slide's failure is caught independently.
- `ContentGenerationPromptVersions` MUST be bumped for the carousel image-prompt sub-step.
- Strict TDD: image-gen pipeline paths MUST have tests before implementation per `sdd-init` cache.
