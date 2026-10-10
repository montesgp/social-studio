# Content Studio Spec

## Purpose

Define the observable requirements for the Content Studio capability, including the operator review loop, slide viewer, and per-slide metadata surface.

## Capabilities

- Review and edit generated content (carousels, stories, reels) in a visual slide viewer.
- Display per-slide metadata, including degrade indicators when deterministic fallbacks were applied.
- Render slide-strip thumbnail navigation with visual indicators.
- Support edit-and-autosave workflow with metadata preservation.

## Constraints

- Unknown keys in composition envelopes MUST be preserved through edit/autosave round-trips.
- Operator-visible badges MUST accurately reflect the slide composition state.

## Acceptance Criteria

- Operator can identify slides that used deterministic degrade fallbacks via per-slide badge.
- Badge styling is informational (not error-state).
- Envelope metadata survives JSON serialization/deserialization.
- Autosave round-trip preserves degrade markers.

---

## Per-Slide Degrade Indicator — Change: client-aesthetic-fidelity-fixes

> Added by change `client-aesthetic-fidelity-fixes`. This section is additive; no existing Content Studio requirement is modified.

### Requirement: CAFF-CS-01 — Studio Slide Viewer Surfaces a Per-Slide Degrade Indicator

The Content Studio carousel/story viewer and slide strip (`apps/web`) MUST render a
per-slide badge/indicator for any slide whose `CompositionSpecEnvelope` carries the degrade
marker written by `content-generation` CAFF-CG-04. A slide whose envelope carries no marker
MUST render with no badge, identical to pre-change behavior.

#### Scenario: Slide envelope carries the degrade marker — badge renders

- GIVEN a carousel slide whose `visual_spec_markup` envelope contains the degrade marker
- WHEN the operator opens the Content Studio carousel/story viewer
- THEN a degrade badge/indicator is shown on that slide only
- AND no other slide in the carousel shows the badge unless its own envelope also carries the marker

#### Scenario: Slide envelope has no degrade marker — no badge (regression)

- GIVEN a carousel slide whose envelope carries no degrade marker
- WHEN the operator opens the Content Studio viewer
- THEN no degrade badge is shown for that slide
- AND the viewer renders identically to its pre-change appearance

---

### Requirement: CAFF-CS-02 — Degrade Marker Survives Studio Edit/Autosave Round-Trip

The degrade marker MUST survive a Studio edit + autosave round-trip. Because the `Envelope`
TypeScript interface declares `[key: string]: unknown` and `serializeEnvelopes` re-stringifies
the whole object, the marker MUST remain present (and the badge MUST still render) after any
caption/visual edit and subsequent autosave.

#### Scenario: Marker persists after an edit and autosave cycle

- GIVEN a slide showing the degrade badge (its envelope carries the marker)
- WHEN the operator edits that slide's caption or visual field and autosave completes
- THEN the reloaded envelope still carries the degrade marker
- AND the degrade badge still renders on that slide after reload

---

## MODIFIED Requirements

None. Both requirements above are additive to the Content Studio slide viewer; no existing
Content Studio requirement (materialization, review-state transitions, autosave mechanics
themselves) is changed.

## REMOVED Requirements

None.

*Added by change `client-aesthetic-fidelity-fixes` — applied 2026-08-24.*

---

## Not Implemented: Ornament Ref Resolution and Token-Aware Degrade Badge — client-aesthetic-visual-sophistication

`client-aesthetic-visual-sophistication` proposed two `content-studio` changes: `CAVS-CS-01`
(`StudioVisualMarkupResolver`/`PptxExportAdapter` resolve an ornament asset ref) and `CAVS-CS-02`
(a token-aware `getDegradeLabel` map giving `"ornament-unavailable"` its own badge label, modifying
`CAFF-CS-01` above). Both were gated on `profile-analysis` `CAVS-PA-04-c`'s ornament bounding-box
reliability measurement, which did **not pass** during `sdd-apply` (see
`openspec/changes/client-aesthetic-visual-sophistication/wu6-reliability-gate.md`). Because no
ornament element or `"ornament-unavailable"` degrade token is ever produced by `content-generation`
in this delivery, neither `CAVS-CS-01` nor `CAVS-CS-02` was implemented, and `CAFF-CS-01` above is
**unmodified** — the badge still hardcodes the "Fallback design" label for every token, exactly as
before this change. No requirement text is merged into this spec for `CAVS-CS-01`/`CAVS-CS-02`; the
full text remains in
`openspec/changes/client-aesthetic-visual-sophistication/specs/content-studio/spec-delta.md` for
reference if/when the reliability gate is re-attempted and passes.

---

## Tightened Slot Geometry Capture and Full-Scope Alignment Guides — Change: content-layout-and-image-fidelity

`SlotGeometryEntry` was captured via `getBoundingClientRect()` on the styled `[data-slot]`
element, returning the full padded CSS box (including fixed `min-height` literals) rather than
the glyph ink — making Content Studio's editing boxes look far bigger than the actual text. This
section tightens residual slack in the captured geometry once the content-generation reflow
(`CLIF-CG-04`/`CLIF-CG-05`) removes the stacked-slot fixed-`top` root cause, and adds a
full-scope alignment/snap mechanism — canvas centre and every sibling slot's six edge/centre
axes — to the drag-resize canvas, with visible guide rendering while a snap is active.

### Requirement: CLIF-CS-01 — Captured Slot Geometry Tracks the Rendered Text Box

Once the content-generation reflow (`CLIF-CG-04`/`CLIF-CG-05`) removes stacked slots' fixed-`top`
root cause, `SlotGeometryEntry` capture MUST track the real rendered text box. If residual slack
remains after reflow (padding, line-height), the capture script MUST tighten to the content box
rather than the padded CSS box. Content Studio's editing boxes MUST render tracking the captured,
tightened geometry.

#### Scenario: Captured geometry is measurably tighter for a short text string (CLIF-CS-01-a)

- GIVEN a short headline text string rendered inside a slot with a fixed `min-height` literal
  (e.g. `260px`)
- WHEN `SlotGeometryEntry` is captured after this change
- THEN the captured box height is measurably smaller than the pre-change `min-height`-driven box
- AND the captured box still fully contains the rendered glyph ink

#### Scenario: Content Studio editing box tracks the tightened geometry (CLIF-CS-01-b)

- GIVEN a slide whose slot geometry has been tightened per CLIF-CS-01-a
- WHEN the operator opens the Content Studio editing canvas
- THEN the rendered editing box for that slot matches the tightened captured geometry, not the
  padded CSS box

### Requirement: CLIF-CS-02 — Canvas-Centre Snap (X and Y)

During drag/resize, the canvas MUST compute a snap candidate against the canvas's own centre-x
and centre-y. When the dragged/resized element's corresponding axis is within tolerance of the
canvas centre, that axis MUST snap and a guide line MUST render for the duration of the snap.

#### Scenario: Dragging near canvas centre-x snaps and renders a guide (CLIF-CS-02-a)

- GIVEN an operator drags an element so its centre-x approaches the canvas's centre-x within
  tolerance
- WHEN the pointer-move handler evaluates snap candidates
- THEN the element's x position snaps to the canvas centre-x
- AND a vertical guide line renders for the duration of the snap

#### Scenario: Dragging near canvas centre-y snaps and renders a guide (CLIF-CS-02-b)

- GIVEN an operator drags an element so its centre-y approaches the canvas's centre-y within
  tolerance
- WHEN the pointer-move handler evaluates snap candidates
- THEN the element's y position snaps to the canvas centre-y
- AND a horizontal guide line renders for the duration of the snap

### Requirement: CLIF-CS-03 — Per-Sibling-Slot Snap on All Six Edge/Centre Axes

For every sibling slot on the canvas, the snap-candidate engine MUST evaluate the dragged
element's position against that sibling's left, centre-x, right, top, centre-y, and bottom
edges. A candidate within tolerance MUST win; the winning axis MUST render a guide line for the
duration of the snap.

#### Scenario: Dragged element snaps to a sibling's left edge (CLIF-CS-03-a)

- GIVEN a sibling slot with a known left edge and a dragged element approaching that x-position
  within tolerance
- WHEN snap candidates are evaluated
- THEN the dragged element's left edge snaps to the sibling's left edge
- AND a guide line renders along that edge

#### Scenario: Dragged element snaps to a sibling's centre-y (CLIF-CS-03-b)

- GIVEN a sibling slot with a known vertical centre and a dragged element approaching that
  y-position within tolerance
- WHEN snap candidates are evaluated
- THEN the dragged element's centre-y snaps to the sibling's centre-y
- AND a guide line renders at that position

#### Scenario: Multiple sibling candidates resolve to one deterministic winner (CLIF-CS-03-c)

- GIVEN two sibling slots each offering a candidate within tolerance on different axes
- WHEN snap candidates are evaluated for a single pointer-move event
- THEN exactly one winning candidate per independent axis (x, y) is applied
- AND the resolution is deterministic for the same input geometry

### Requirement: CLIF-CS-04 — Snapping Is Distinguishable From Free Movement

When no snap candidate (canvas centre or any sibling axis) is within tolerance, the dragged
element MUST move freely with no guide rendered. Holding Alt during a drag MUST bypass snapping
entirely for the duration of that drag.

#### Scenario: No candidate within tolerance — free movement, no guide (CLIF-CS-04-a)

- GIVEN a dragged element whose position is not within tolerance of any canvas-centre or
  sibling-slot candidate
- WHEN the pointer-move handler evaluates snap candidates
- THEN the element follows the pointer with no snapping applied
- AND no guide line renders

### Requirement: CLIF-CS-05 — Snap-Candidate Engine Meets a Stated Performance Budget

The snap-candidate engine (a pure module computing canvas-centre and per-sibling six-axis
candidates) MUST meet a stated per-move computation time budget (`< 1 ms`) at a stated
sibling-slot count (`SNAP_MAX_SIBLINGS = 24`). This MUST be measured on the pure module before it
is wired into the pointer-move handler.

#### Scenario: Candidate computation meets the stated budget at the stated slot count (CLIF-CS-05-a)

- GIVEN `SNAP_MAX_SIBLINGS = 24` sibling slots
- WHEN the snap-candidate engine computes candidates for one simulated pointer-move event
- THEN the computation completes within the stated time budget
- AND this is measured directly on the pure module, independent of pointer-handler integration

## Modified Requirements (content-layout-and-image-fidelity)

None. `CLIF-CS-01`..`05` are additive to the Content Studio slot-geometry capture and drag-resize
canvas; no existing Content Studio requirement is changed.

## Removed Requirements (content-layout-and-image-fidelity)

None.

*Merged from `openspec/changes/content-layout-and-image-fidelity/specs/content-studio/spec-delta.md`
— change applied 2026-08-27. Verified against shipped code: `SlotGeometryScript.ExtractExpression`
in `HtmlChromiumContentRenderer.cs` now measures `document.createRange().selectNodeContents(el)`
content-box extent, falling back to the CSS box when the range yields zero width/height (CLIF-CS-01);
`apps/web/lib/studio/alignment-guides.ts` exports `SNAP_TOLERANCE_PX = 6`, `SNAP_MAX_SIBLINGS = 24`,
`computeSnap`, and `guideToOverlayStyle` exactly per the design contract (CLIF-CS-02..05);
`content-studio-panel.tsx`'s `onPointerMove` wires `computeSnap` with an Alt-key bypass and renders
guide divs from `setGuides` (CLIF-CS-04). Item 8b.6 (owner manual drag/Alt-bypass verification) is a
pending human action, not an implementation gap — no automated component-render harness exists in
this repo for `apps/web`.*

---

## Client Style Fidelity Requirements

### Requirement: CSF-CS-01 — Studio Loads the Bundled Font Catalog

The API MUST serve the bundled catalog as `GET /api/content-studio/fonts.css` (`@font-face` rules)
and `GET /api/content-studio/fonts/{dir}/{file}` (font files), without authentication, since the
fonts are OFL-licensed. The Studio canvas MUST load the stylesheet once into `document.head`, so
edited text uses the same faces as the rendered slide.

*Merged from `openspec/changes/client-style-fidelity/specs/content-studio/spec-delta.md` — change applied 2026-10-10.*
