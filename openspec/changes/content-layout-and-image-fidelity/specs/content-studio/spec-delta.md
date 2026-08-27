# Spec Delta: Content Studio — Tightened Slot Geometry Capture and Full-Scope Alignment Guides

<!-- Change: content-layout-and-image-fidelity -->
<!-- Applies to: openspec/specs/content-studio/spec.md (additive delta) -->
<!-- WU-6 (slot geometry tightening) and WU-8a/WU-8b (alignment guides, D3 full scope). -->
<!-- WU-6 is sequenced after content-generation's reflow (CLIF-CG-04/05) ships, so residual -->
<!-- slack is measured against real reflowed geometry, not the broken baseline. Track D has no -->
<!-- ordering dependency on Track B (D1 preserved absolute positioning). -->

## Purpose

`SlotGeometryEntry` is captured via `getBoundingClientRect()` on the styled `[data-slot]`
element, returning the full padded CSS box (including fixed `min-height` literals) rather than
the glyph ink — making Content Studio's editing boxes look far bigger than the actual text. This
delta asserts the captured geometry tracks the rendered text box once the content-generation
reflow removes the root cause, and tightens residual slack where it remains. It also adds a
full-scope (D3) alignment/snap mechanism — canvas centre and every sibling slot's six edge/centre
axes — to the drag-resize canvas, with visible guide rendering while a snap is active.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CLIF-CS-01 | Scope item 6; Success Criteria — captured geometry measurably tighter |
| CLIF-CS-02 | Scope item 8, D3; Success Criteria — canvas-centre snap |
| CLIF-CS-03 | Scope item 8, D3; Success Criteria — sibling edge/centre snap on all 6 axes |
| CLIF-CS-04 | Success Criteria — snap distinguishable from free movement by test |
| CLIF-CS-05 | Risks (guide performance); Success Criteria — snap-candidate engine performance budget |

---

## ADDED Requirements

### Requirement: CLIF-CS-01 — Captured Slot Geometry Tracks the Rendered Text Box

Once the content-generation reflow (`CLIF-CG-04`/`CLIF-CG-05`) removes stacked slots' fixed-`top`
root cause, `SlotGeometryEntry` capture MUST be asserted to track the real rendered text box. If
residual slack remains after reflow (padding, line-height), the capture script MUST tighten to
the content box rather than the padded CSS box. Content Studio's editing boxes MUST render
tracking the captured, tightened geometry.

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

---

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

---

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

---

### Requirement: CLIF-CS-04 — Snapping Is Distinguishable From Free Movement

When no snap candidate (canvas centre or any sibling axis) is within tolerance, the dragged
element MUST move freely with no guide rendered.

#### Scenario: No candidate within tolerance — free movement, no guide (CLIF-CS-04-a)

- GIVEN a dragged element whose position is not within tolerance of any canvas-centre or
  sibling-slot candidate
- WHEN the pointer-move handler evaluates snap candidates
- THEN the element follows the pointer with no snapping applied
- AND no guide line renders

---

### Requirement: CLIF-CS-05 — Snap-Candidate Engine Meets a Stated Performance Budget

The snap-candidate engine (a pure module computing canvas-centre and per-sibling six-axis
candidates) MUST meet a stated per-move computation time budget at a stated sibling-slot count.
This MUST be measured on the pure module before it is wired into the pointer-move handler.

#### Scenario: Candidate computation meets the stated budget at the stated slot count (CLIF-CS-05-a)

- GIVEN a stated number of sibling slots (per the design/tasks-phase performance target)
- WHEN the snap-candidate engine computes candidates for one simulated pointer-move event
- THEN the computation completes within the stated time budget
- AND this is measured directly on the pure module, independent of pointer-handler integration
