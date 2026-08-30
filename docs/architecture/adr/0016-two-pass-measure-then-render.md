# ADR 0016 - Two-Pass Measure-Then-Render Layout Reflow

## Status
Accepted

## Update (WU-5/WU-6, `content-layout-and-image-fidelity`)

Two refinements shipped after this ADR's original decision, both consistent with the framing
above and not requiring any change to the reflow loop itself:

- **WU-5 — copy-length-aware typography auto-fit** (`CompositionDefaults.ApplyCopyLengthAutoFit`,
  `CLIF-CG-07`) was the forward-looking Consequences bullet made concrete: it is a separate,
  opt-in, floor-bounded, reduction-only size adjustment on `CompositionDefaults.ResolveTypography`
  (new optional `headlineCopy` parameter, `null` by default — every pre-existing call site stays
  byte-identical). It does **not** live inside the reflow loop and does not consume a render pass;
  it addresses a case the loop structurally cannot: a headline slot's own size, when nothing sits
  below it to push, or when the loop's own clamp-at-canvas-bottom is reached. The two mechanisms
  are independently testable and independently revertible.
- **WU-6 — Range-based geometry measurement** tightened `SlotGeometryScript.ExtractExpression`
  (`HtmlChromiumContentRenderer.cs`) to measure `document.createRange().selectNodeContents(el)`
  content-box extent (the glyph-ink line box) in place of `el.getBoundingClientRect()`'s full
  padded CSS box, falling back to the CSS box when the range yields zero width/height. This is
  safe by construction for the reflow loop: when a slot's content already exceeds its declared
  `min-height` (the wrapping-headline case this ADR's formula depends on), the element's own box
  already equals the content height, so the Range measurement and the CSS box coincide — only the
  short-text/padded case (which the reflow loop never repositions, since a slot that already fits
  needs no push) is actually tightened. `SlotBox`/`SlotOverlapDetector`/`MeasuredLayoutReflow`
  consume whichever geometry `RenderWithGeometryAsync` returns without needing to know which
  measurement strategy produced it — the reflow loop's contract with `SlotGeometryEntry` was
  already measurement-strategy-agnostic, so this refinement required no change to any `Visual/Layout/`
  type or to `RenderWithReflowAsync`. Confirmed via the full WU-4 test suite (`MeasuredLayoutReflow`/
  `SlotTopRewriter`/`MarkupSlotBoxParser`/`RenderWithReflow`, 36/36) re-run unchanged after the WU-6
  script edit.

## Context

`content-layout-and-image-fidelity` WU-2/WU-3 made cross-slot vertical overlap **observable**
(`SlotOverlapDetector`, pure pairwise rectangle intersection) and **wired** it onto the
template-backed carousel/story path as a non-blocking, declared-box-only diagnostic (D-B). Neither
step *corrects* anything: the declared-box check can only ever catch a builder regression, because
the shipped literals were hand-tuned not to overlap — it structurally cannot see the real defect,
which only exists once a headline's actual measured height (after Chromium wraps long copy to 2-3
lines) exceeds its declared `min-height` and intrudes into the slot below it.

WU-4 closes that gap: it needs authoritative MEASURED geometry (already captured once per render
via `RenderWithGeometryAsync`'s `SlotGeometryEntry` list, D-A) and a bounded mechanism to correct
the layout when that measurement disagrees with the declared box — without a second render engine,
a new port, or an unbounded retry loop.

## Decision

### 1. Measure → recompute → re-render, capped at 3 total renders, at the existing render call site

A new `private async Task<Result<ReflowRenderResult>> RenderWithReflowAsync(...)` in
`ContentGenerationJobProcessor` wraps the existing `RenderWithGeometryAsync` call (previously
~L1157, now the `else` branch of the `templateBackedForReflow` conditional at ~L1195). All
arithmetic and selection logic live in pure, independently-testable `Visual/Layout/` types — the
processor method is a thin orchestrating loop:

1. Render pass 1 with `RenderWithGeometryAsync`.
2. Parse the declared box for every reflowable slot from the already-built markup
   (`MarkupSlotBoxParser`, pre-render, no Chromium) and pair it with the measured box from this
   pass's `SlotGeometry` (`SlotOverlapDetector.Detect`).
3. **Fast path (CLIF-CG-03):** if no two slots intersect AND no recomputed `top`
   (`MeasuredLayoutReflow.RecomputeTops`) differs from the rendered `top` by more than
   `EpsilonPx` (1.0px), stop — exactly one render for the common case.
4. **Otherwise:** rewrite the affected slots' `top:NNNpx` declarations in place
   (`SlotTopRewriter.Apply`, applied identically to both the render markup and the persisted
   markup — D-C below), re-render, re-measure. Repeat until convergence or `MaxRenderPasses = 3`
   (1 initial + at most 2 re-renders) is reached. The `for` loop's upper bound IS the constant —
   the method is structurally incapable of a 4th render, not merely policy-limited to one.
5. **Non-convergence (CLIF-CG-05):** at the cap, select the pass with the smallest total
   intersection area as the best-effort layout (ties broken by lowest pass index), emit one
   structured `ContentGeneration.LayoutReflow` `LogWarning` (job id, format key, spec index, total
   render passes, selected pass index, residual overlap count/area, whether the layout clamped at
   the canvas bottom, the selected tops, and the overlapping pairs), and ship that layout. The
   slide is never failed for residual overlap.

### 2. The reflow loop rewrites `top:` in already-built markup; it never re-invokes a builder

Re-invoking `BuildTemplateBackedMarkup` per pass with new tops was rejected: it would require
threading the recomputed tops through three builder call signatures and would lose the
already-appended brand-mark slot and inlined background/icon references those builders do not
know how to reconstruct mid-loop. `SlotTopRewriter.Apply` is a pure, idempotent, scoped
`top:NNNpx` string rewrite instead — inspectable, testable without Chromium, and it preserves
everything upstream already built. The identical top-map is applied to **both** the render markup
(with inlined data-URIs) and the persisted markup, so Content Studio, `PptxExportAdapter`, and
downstream geometry capture all see the exact layout that shipped. Markup stays absolutely
positioned throughout — no document-flow/flex conversion (CLIF-CG-04-b regression guard).

### 3. Non-convergence does NOT re-enter `CAC-IMG-03` or any other retry/fallback

**Correction to the proposal's original non-convergence wording.** `CAC-IMG-03`
(`ClientAestheticImageBackgroundService.RenderWithGuardrailRetryAsync`, `MaxAttempts`) is an
*image-generation* retry budget that is already fully exhausted **before** the processor builds
any markup for this slide — it is a different pipeline stage entirely. `RenderFaithfulFlat()` (the
deterministic flat-color fallback) is only reachable earlier in the same method, before
`templateBackedForReflow` is ever set. Routing a non-converged reflow slide back through either
would either re-trigger ComfyUI image generation (a double spend for a layout problem, not an
image problem) or require a 4th render (breaking the hard cap). **Decision: the two budgets are
disjoint and never interact.** A non-converging reflow always ships the best-effort layout plus
the structured diagnostic above — never a retry, never a flat-fallback substitution, never a
failed slide.

### 4. Scope: template-backed carousel/story builders only

`templateBackedForReflow` is set `true` only in the two branches that call
`BuildTemplateBackedMarkup` (the enforced-background path and the legacy resilience backstop). It
is never set by `RenderFaithfulFlat()` / `BuildFlatColorMarkup` (the deterministic FlatColor
short-circuit and both CAFF-CG degrade triggers) or by the pure AI-designer markup path
(CLIF-CG-06). The flat path's byte-identity golden and every AI-markup test are therefore
untouched by construction — no new measurement, re-render, or overlap check runs against them.

### 5. Reflowable slot sets, by format

Bottom-anchored and decorative slots are excluded **by construction** — a slot whose declared
style uses `bottom:` (never `top:`) is skipped by `MarkupSlotBoxParser` and is a silent no-op for
`SlotTopRewriter.Apply`:

- carousel: `title` → `subtitle` (`cta` is bottom-anchored)
- story: `headline` → `subtitle` → `body` (`accent`, `cta`, `signature`, `brand-mark` excluded)

## Latency Posture

The fast path (CLIF-CG-03/CLIF-CG-08) is a single render — identical cost to the pre-WU-4
pipeline for every slide whose copy already fits its declared box (the common case; measured
against real client jobs, most headlines do not wrap far enough to overlap the slot below).

Only a slide whose measured geometry actually overlaps pays for a second Chromium render pass, and
only a slide that still overlaps after that recompute pays for a third. The hard cap bounds the
worst case at exactly 3 renders per affected slide (2 renders more than today, never more) — a
carousel with N slides therefore adds at most `2 * N` extra render passes in the pathological case
where every slide overlaps and never converges, which the non-convergence disposition (§3) still
resolves without an unbounded loop or a failed job. This cost is accepted: it trades a bounded,
worst-case-known latency increase for the correctness gain of never shipping the actual real
overlapping-headline defect this change exists to fix.

## Consequences

- `MeasuredLayoutReflow`, `SlotTopRewriter`, and `MarkupSlotBoxParser` are pure, public Application
  types under `Features/Social/ContentGeneration/Visual/Layout/` — independently unit-testable
  without Chromium, mirroring the WU-2 `SlotOverlapDetector` precedent. `RenderWithReflowAsync`
  itself is `internal` (not `private`) specifically so `Incoders.Hive.Tests` can drive the bounded
  loop directly against a stub `IContentRendererWithGeometry` via `InternalsVisibleTo` — no live
  Chromium process boundary is involved in testing the loop's pass-count/convergence/diagnostic
  contract.
- The recompute formula is monotone non-decreasing (a slot's recomputed `top` is never less than
  its declared `top` — the loop never moves a slot up) and clamps at `canvasHeight - 140` rather
  than pushing text off-canvas; a clamp that still leaves residual overlap is reported by the
  non-convergence diagnostic, not hidden.
- `CAC-IMG-03` and this reflow loop remain two independently-owned, disjoint retry/correction
  budgets on two different pipeline stages (image generation vs. layout). Neither is aware of the
  other's outcome, and neither routes into the other.
- WU-5 (copy-length-aware typography auto-fit) is explicitly sequenced AFTER this ADR's mechanism
  ships, because it is tuned against real reflowed geometry this loop produces.

## Related

- Proposal / Spec / Design: `openspec/changes/content-layout-and-image-fidelity/`
  (`specs/content-generation/spec-delta.md` CLIF-CG-03 through CLIF-CG-06, CLIF-CG-08;
  `design.md` D-C, D-D).
- Precedent: WU-2's `SlotOverlapDetector` (pure pairwise rectangle intersection, no Chromium
  dependency) — this ADR's three new `Visual/Layout/` types follow the same shape.
- Depends on: WU-1/D3's `IContentRendererWithGeometry.RenderWithGeometryAsync` — the existing
  `SlotGeometryEntry` measurement capture this loop re-invokes per pass; no new renderer port.
