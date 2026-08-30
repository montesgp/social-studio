# Spec Delta: Content Generation — Cross-Slot Overlap Detection, Guardrail Coverage on the Template-Backed Path, Two-Pass Reflow, and Auto-Fit Typography

<!-- Change: content-layout-and-image-fidelity -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->
<!-- Track B (WU-2, WU-3, WU-4, WU-5). Reflow is scoped to the template-backed builders only; -->
<!-- the deterministic flat path (BuildFlatColorMarkup / BuildFlatCarouselSlots) and its -->
<!-- byte-identity golden are an explicit non-goal (D1). WU-6 (slot geometry tightening) and -->
<!-- Track D (alignment guides) are specified under content-studio, per the proposal's Modified -->
<!-- Capabilities mapping. WU-7 (OCR feasibility report, D2) is documentation-only and has no -->
<!-- runtime behavior; it introduces no spec delta. -->

## Purpose

Every stacked text slot's `top` offset in `BuildTemplateBackedMarkup` (and its
`BuildCarouselTemplateSlots`/`BuildStoryTemplateSlots` dispatch targets) is a fixed pixel
literal today; no slot's position is ever computed from another slot's measured extent, and
`VisualGuardrailValidator` is never invoked on this path's output at all. This delta adds
cross-slot overlap detection, wires guardrail validation onto the template-backed path, and
introduces a bounded two-pass measure-then-render reflow (D1) so a legitimately large headline
pushes the subtitle down instead of colliding with it, plus a floor-bounded typography auto-fit
for unusually long copy. Absolute positioning is preserved throughout; the markup contract does
not change.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CLIF-CG-01 | Scope item 2; Success Criteria — cross-slot check reproduces the real defect |
| CLIF-CG-02 | Scope item 3; Success Criteria — guardrail validation runs on the template-backed path |
| CLIF-CG-03 | Scope item 4; D1 fast path; Success Criteria — no-overlap slide renders exactly once |
| CLIF-CG-04 | Scope item 4; D1 Pass 2/3; Success Criteria — long headline renders without intersection |
| CLIF-CG-05 | D1 hard cap and non-convergence disposition; Success Criteria — render loop never exceeds 3, best-effort + diagnostic, no `CAC-IMG-03` routing (disjoint budgets, per design.md's correction) |
| CLIF-CG-06 | Out of Scope — deterministic flat path explicit non-goal; Success Criteria — pre-existing tests pass unchanged |
| CLIF-CG-07 | Scope item 5; Success Criteria — auto-fit floor-bounded, reduction-only |
| CLIF-CG-08 | Success Criteria — byte-identical output for a profile/copy that already fits |

---

## ADDED Requirements

### Requirement: CLIF-CG-01 — Cross-Slot Vertical-Overlap Detection

`VisualGuardrailValidator` MUST gain a cross-slot geometry check able to detect when two text
slots' rendered vertical extents intersect. This check MUST be independent of, and additional
to, the existing single-slot `data-max-lines` char-count heuristic (`ValidateOverflow`), which
this change does not modify.

#### Scenario: Real defect fixture is flagged as a violation (CLIF-CG-01-a)

- GIVEN a headline slot rendered at `font-size:149px`, `top:255px`, wrapping to 2–3 lines
- AND a subtitle slot at `top:575px`
- WHEN the cross-slot overlap check runs
- THEN the headline's rendered bottom edge is found to exceed the subtitle's top edge
- AND a cross-slot vertical-overlap violation is reported

#### Scenario: Non-overlapping slots pass (CLIF-CG-01-b)

- GIVEN a headline and a subtitle slot whose rendered vertical extents do not intersect
- WHEN the cross-slot overlap check runs
- THEN no violation is reported for that slide

---

### Requirement: CLIF-CG-02 — Guardrail Validation Runs on the Template-Backed Path's Output

`BuildTemplateBackedMarkup`'s output MUST be submitted to guardrail validation
(`VisualGuardrailValidator`, including CLIF-CG-01's cross-slot check). Guardrail validation MUST
NOT be limited to AI-designer markup or to the pre-enforced-background-resolution step.

#### Scenario: Template-backed markup is actually validated (CLIF-CG-02-a, regression)

- GIVEN a `ClientAesthetic` job whose enforced-background priority resolution selects
  `BuildTemplateBackedMarkup`'s output over the AI designer's markup
- WHEN the slide's final markup is produced
- THEN `VisualGuardrailValidator` is invoked on that final template-backed markup
- AND this is asserted directly (not inferred), guarding against the previously-existing
  call-ordering bypass — where `_guardrails.Validate` ran once against AI-designer markup
  before the enforced-background priority resolution picked template markup instead — being
  silently reintroduced by a future refactor

#### Scenario: A violation detected on the template-backed path is recorded, not retried (CLIF-CG-02-b)

- GIVEN guardrail validation reports a cross-slot violation on `BuildTemplateBackedMarkup`'s
  output
- WHEN the pipeline handles the violation
- THEN the violation is logged as a non-blocking diagnostic (this call is step 5, non-blocking
  per design.md — it does not gate markup selection and is a separate budget from
  `CAC-IMG-03`, which governs image-generation retries and has already completed by this point)
- AND the job is not marked failed

---

### Requirement: CLIF-CG-03 — Two-Pass Reflow: Fast Path Renders Once When No Overlap Is Measured

If the first render's measurement shows no two slots intersect and no recomputed `top` differs
from the rendered `top` by more than a stated epsilon, the pipeline MUST stop at one render. The
fast path MUST apply to the template-backed carousel and story builders only.

#### Scenario: Short-copy slide renders exactly once (CLIF-CG-03-a)

- GIVEN a template-backed slide whose first (Pass 1) render shows no cross-slot intersection and
  no material `top` delta
- WHEN the reflow mechanism runs
- THEN rendering stops after exactly one render (no Pass 2, no re-measurement)
- AND this is asserted directly against a render-count assertion

---

### Requirement: CLIF-CG-04 — Two-Pass Reflow: Overlap Triggers Recompute and Re-Render, Verified by Re-Measurement

If Pass 1's measurement detects an intersection, the pipeline MUST recompute subsequent slots'
`top` offsets from the preceding slot's measured extent (read via the existing
`SlotGeometryScript` mechanism), re-render, and re-measure to verify the overlap is resolved. If
the recompute changes a slot's own wrapping and overlap survives, the pipeline MUST recompute
and re-render once more (Pass 3), subject to the hard cap in `CLIF-CG-05`.

#### Scenario: Long headline at the real measured SizeRatio converges without intersection (CLIF-CG-04-a)

- GIVEN a headline whose `top` is derived from the client's real measured `SizeRatio` and wraps
  to 2–3 lines, initially overlapping the subtitle
- WHEN the reflow mechanism runs on the carousel or the story template builder
- THEN the subtitle's `top` is recomputed from the headline's measured extent
- AND the re-rendered, re-measured layout shows no intersection between the two slots

#### Scenario: Markup remains absolutely positioned after reflow (CLIF-CG-04-b, regression)

- GIVEN a slide that converges via CLIF-CG-04-a
- WHEN the final markup is inspected
- THEN every text slot remains absolutely positioned (no document-flow/flex conversion)
- AND `PptxExportAdapter` output and `StudioVisualMarkupResolver` behavior are unaffected

---

### Requirement: CLIF-CG-05 — Hard Cap of 3 Renders; Non-Convergence Produces a Best-Effort Layout and a Structured Diagnostic

The reflow loop MUST NEVER exceed 3 renders per slide (1 initial + at most 2 re-renders), and
MUST be structurally incapable of iterating further. If overlap still survives at the cap, the
pipeline MUST accept the best-effort pass (the measured layout with the smallest total
intersection area), emit a structured non-convergence diagnostic (job id, slide index,
intersection measurements), and ship that best-effort layout rather than failing the slide.
This budget is deliberately disjoint from `CAC-IMG-03` (`ClientAestheticImageBackgroundService`'s
image-generation retry budget, exhausted earlier in the pipeline before markup exists) — the two
never compose, and non-convergence at the render stage MUST NOT re-trigger image generation or
any other retry.

#### Scenario: Render loop never exceeds 3 renders (CLIF-CG-05-a)

- GIVEN a deliberately non-converging fixture (recompute changes wrapping in a way that
  re-introduces overlap each pass)
- WHEN the reflow mechanism runs
- THEN no more than 3 renders occur for that slide
- AND this is asserted directly against a render-count assertion, not inferred

#### Scenario: Non-convergence yields a best-effort layout and a diagnostic, never a retry (CLIF-CG-05-b)

- GIVEN the same non-converging fixture reaches the 3-render cap without resolving overlap
- WHEN the cap is reached
- THEN the pipeline selects the pass with the smallest total intersection area as the
  best-effort layout
- AND a structured non-convergence diagnostic is emitted (job id, slide index, intersection
  measurements)
- AND that best-effort layout ships as the slide's final markup — no `CAC-IMG-03` route, no
  image-regeneration retry, no additional render pass
- AND the job is not marked failed and does not hang

---

### Requirement: CLIF-CG-06 — Scope Boundary Regression: The Deterministic Flat Path Is Untouched

The reflow mechanism (CLIF-CG-03 through CLIF-CG-05) MUST apply only to the template-backed
path. `BuildFlatColorMarkup` / `BuildFlatCarouselSlots` MUST NOT be reflowed, measured, or
re-rendered by this change.

#### Scenario: Flat-path byte-identity golden renders unchanged (CLIF-CG-06-a, regression)

- GIVEN a flat-color carousel/story job that previously produced a byte-identical golden output
- WHEN the same job runs after this change
- THEN `BuildFlatColorMarkup` / `BuildFlatCarouselSlots` output remains byte-identical to the
  pre-change golden
- AND no measurement pass, re-render, or overlap check runs against this path

---

### Requirement: CLIF-CG-07 — Copy-Length-Aware Typography Auto-Fit Is Floor-Bounded and Reduction-Only

`CompositionDefaults` MUST gain a bounded auto-fit: when a legitimate `SizeRatio` applied to an
unusually long copy string would produce a heading size beyond the auto-fit's stated limits, the
resolved size MUST be reduced within those stated limits. Auto-fit MUST NEVER increase a
resolved size, and MUST NEVER reduce a size below the auto-fit's stated floor. `MaxSizeRatio`
continues to guard against a bad measurement; auto-fit separately guards against a long copy
string at an otherwise legitimate ratio.

#### Scenario: Legitimately large SizeRatio with unusually long copy is reduced within stated limits (CLIF-CG-07-a)

- GIVEN a client's real measured `SizeRatio` and a copy string materially longer than typical
- WHEN typography is resolved for that headline
- THEN the resolved size is reduced from the rigid `SizeRatio`-derived value
- AND the reduced size does not fall below the auto-fit's stated floor

#### Scenario: Auto-fit never increases a resolved size (CLIF-CG-07-b)

- GIVEN any copy string and `SizeRatio` combination
- WHEN typography auto-fit runs
- THEN the resolved size after auto-fit is never greater than the size before auto-fit

---

### Requirement: CLIF-CG-08 — Cold-Start / No-Op Regression: A Profile and Copy That Already Fit Render Byte-Identically

A profile and copy string that already fit today (no long headline, no cross-slot overlap) MUST
render byte-identically to the pre-change output. Neither the reflow mechanism nor the
typography auto-fit MUST alter output for a slide unaffected by either fix.

#### Scenario: Unaffected client/job renders identically to before (CLIF-CG-08-a, regression)

- GIVEN a client and copy string that produce no long headline and no cross-slot overlap under
  today's fixed-literal layout
- WHEN the same job runs after this change
- THEN the rendered markup, typography sizes, and slot positions are byte-identical to the
  pre-change output
- AND the reflow mechanism takes the fast path (CLIF-CG-03) with exactly one render
