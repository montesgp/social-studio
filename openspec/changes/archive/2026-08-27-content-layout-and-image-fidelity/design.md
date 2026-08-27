# Design: Content Layout and Image Fidelity

## Technical Approach

Four independent tracks (proposal `## Approach`). The architectural core is Track B: overlap becomes
**observable** as a pure function over rectangles (WU-2), then **wired** onto the template-backed path
(WU-3), then **corrected** by a bounded measure→recompute→re-render loop at the existing render call
site (WU-4). All decision logic lands in new pure, public Application types under
`Features/Social/ContentGeneration/Visual/Layout/` — because `ContentGenerationJobProcessor`'s
builders are `private static` and unreachable from `Incoders.Hive.Tests`, so anything left inside that
file is untestable. Chromium stays the only measurement source; no new port, no new dependency.

## Architecture Decisions

### D-A: Overlap detection needs MEASURED geometry, not the declared CSS box

| Option | Tradeoff | Decision |
|---|---|---|
| Parse declared `top`/`min-height` from markup pre-render | Cheap, no render; but the literals were hand-tuned to *not* overlap, so it structurally cannot see the real defect | Secondary only |
| Use `SlotGeometryEntry` from `RenderWithGeometryAsync` | Authoritative Chromium boxes; requires a render first | **Chosen (primary)** |

`VisualGuardrailValidator.Validate(output, brand, spec)` keeps its exact signature (every existing
caller unchanged). A new overload `Validate(output, brand, spec, IReadOnlyList<SlotGeometryEntry>
measured)` runs steps 1–4 **plus** a new step 5 (cross-slot). Without measured geometry, step 5 falls
back to declared boxes and can only catch a builder regression — stated as a limit, not a gate.
`ValidateOverflow` is untouched (proposal non-goal).

### D-B: WU-3 wires validation AFTER priority resolution, and is non-blocking

The enforced-background branch must keep winning. Adding a call before L977 would either re-order
priority or validate markup that gets discarded. Chosen: leave `_guardrails.Validate` at ~L957
exactly where it is, and add a **second, separate** call immediately after the `validatedMarkup`
if/else chain closes (~L1035) and before `spec = spec with { … }` (~L1038), gated on
`enforcedBackground is not null`. On failure it **logs a structured warning and records the
violation; it does not change markup selection.** Rationale: rejecting slides on declared-box
evidence would ship a false-positive gate before WU-4 supplies real evidence — and the proposal
requires WU-2's false-positive rate be measurable before it can block.

### D-C: The reflow loop rewrites `top:` in the already-built markup; it does not re-invoke builders

| Option | Tradeoff | Decision |
|---|---|---|
| Re-invoke `BuildTemplateBackedMarkup` per pass with new tops | Requires threading tops through 3 builder signatures; loses the appended brand-mark slot and inlined refs | Rejected |
| Pure string rewrite of `top:NNNpx` inside each named slot's `style` | Idempotent, inspectable, preserves brand-mark append + inline-ref substitution, testable without Chromium | **Chosen** |

The identical top-map is applied to **both** the render markup (data-URI inlined) and the persisted
markup, so Content Studio, `PptxExportAdapter` and geometry capture all see the shipped layout.
Markup stays absolutely positioned — the D1 guarantee.

### D-D: The loop lives at the render call site; non-convergence does NOT re-enter CAC-IMG-03

Owner: a new thin `private async Task<…> RenderWithReflowAsync(…)` in
`ContentGenerationJobProcessor`, wrapping the existing `RenderWithGeometryAsync` block at ~L1157.
All arithmetic and selection live in pure `Visual/Layout/` types.

**Correction to the proposal's non-convergence wording.** `CAC-IMG-03`
(`ClientAestheticImageBackgroundService.RenderWithGuardrailRetryAsync`, `MaxAttempts`) is an
*image-generation* budget that is fully exhausted **before** the processor builds any markup, and
`RenderFaithfulFlat()` is only reachable earlier in the same method. Routing a non-converged slide
back through either would re-trigger ComfyUI (double spend) or require a 4th render (breaks the cap).
**Decision: the two budgets are disjoint and never interact.** Non-convergence ships the best-effort
layout plus a structured diagnostic. No retry, no flat fallback, no failed job.

### D-E: Snap tolerance 6px, Alt bypass, sibling rects captured once per drag

Proposal open item #1 answered: `SNAP_TOLERANCE_PX = 6` in **canvas** coordinates (the handler
already divides pointer delta by `scaleRef.current`, so tolerance is zoom-invariant). Bypass = hold
**Alt** (Shift is conventionally axis-lock; Ctrl/Cmd collide with browser gestures). Both are local UI
state, trivially retunable.

## Data Flow

```
BuildTemplateBackedMarkup ──> markup(literal tops) ──> +brand-mark ──> renderSpec(inlined URIs)
                                                                            │
                                    ┌───────────────────────────────────────┘
                                    v
              ┌── RenderWithGeometryAsync ──> asset + IReadOnlyList<SlotGeometryEntry>
              │                                        │
              │                          SlotOverlapDetector.Detect
              │                                        │
              │              none ──> FAST PATH: ship pass 1 (common case)
              │                                        │
              │              some ──> MeasuredLayoutReflow.RecomputeTops
              │                                        │
              └──< SlotTopRewriter.Apply (render markup AND persisted markup)
                                     (max 3 passes total)
                                                       │
                       cap reached ──> best-effort pass (min total intersection) + diagnostic
```

## Interfaces / Contracts

```csharp
// Application/Features/Social/ContentGeneration/Visual/Layout/SlotOverlapDetector.cs
public readonly record struct SlotBox(string Slot, double Left, double Top, double Width, double Height);
public sealed record SlotOverlap(string UpperSlot, string LowerSlot, double IntersectionAreaPx2);

public static class SlotOverlapDetector
{
    public const double TolerancePx = 1.0;
    /// Pairwise. A pair overlaps only when BOTH axes intersect by > TolerancePx —
    /// a vertical-band-only test would false-positive on side-by-side slots.
    /// Pairs are emitted once, ordered by (Top, Slot) for determinism.
    public static IReadOnlyList<SlotOverlap> Detect(IReadOnlyList<SlotBox> boxes);
    public static double TotalIntersectionArea(IReadOnlyList<SlotOverlap> overlaps);
}
```

```csharp
// Visual/Layout/MeasuredLayoutReflow.cs — the position-recompute formula (D1 requires it inspectable)
// Stacked, top-anchored slots only, in declared order. Anchor slot never moves.
//   top[0]      = declaredTop[0]
//   gap[i]      = declaredTop[i] - (declaredTop[i-1] + declaredMinHeight[i-1])   // designed spacing, preserved
//   prevBottom  = top[i-1] + max(declaredMinHeight[i-1], measuredHeight[i-1])
//   top[i]      = max(declaredTop[i], round(prevBottom + gap[i]))
// Monotone non-decreasing => when everything already fits (measured <= min-height),
// top[i] == declaredTop[i] EXACTLY, i.e. byte-identical output. Never moves a slot up.
// Clamp: if top[i] + height[i] > canvasHeight - BottomSafePx (140), stop at the clamp and
// report residual overlap rather than pushing text off-canvas (that case is WU-5's auto-fit).
public static class MeasuredLayoutReflow
{
    public const int MaxRenderPasses = 3;
    public const double EpsilonPx = 1.0;
    public static IReadOnlyDictionary<string, int> RecomputeTops(
        IReadOnlyList<SlotBox> declared, IReadOnlyList<SlotBox> measured, int canvasHeight);
}
```

**Reflowable slot sets** (bottom-anchored and decorative slots are excluded *by construction* — a slot
whose declared style uses `bottom:` never participates):
- carousel: `title` → `subtitle` (`cta` bottom-anchored)
- story: `headline` → `subtitle` → `body` (`accent`, `cta`, `signature`, `brand-mark` excluded)

**Non-convergence diagnostic** — one structured `LogWarning`, event `ContentGeneration.LayoutReflow`:
`JobId`, `FormatKey`, `SpecIndex`, `RenderPasses`, `SelectedPassIndex`, `ResidualOverlapCount`,
`ResidualIntersectionAreaPx2`, `OverlapPairs`, `SelectedTops`, `ClampedAtCanvasBottom`.
Best-effort selection: minimum `TotalIntersectionArea`, ties broken by lowest pass index.

```ts
// apps/web/lib/studio/alignment-guides.ts (WU-8a) — pure, framework-free
export const SNAP_TOLERANCE_PX = 6;
export const SNAP_MAX_SIBLINGS = 24;              // stated performance threshold
export interface Rect { left: number; top: number; width: number; height: number }
export interface GuideLine { axis: "x" | "y"; position: number; from: number; to: number; source: "canvas" | "sibling" }
export interface SnapResult { left: number; top: number; guides: readonly GuideLine[] }
export function computeSnap(input: {
  dragged: Rect; siblings: readonly Rect[];
  canvas: { width: number; height: number }; tolerance?: number;
}): SnapResult;
export function guideToOverlayStyle(g: GuideLine, scale: number): { top: number; left: number; width: number; height: number };
```

X candidates: canvas centre + per sibling `{left, centreX, right}`; dragged edges tested:
`{left, centreX, right}`. Y symmetric — **6 axes per sibling**. Winner = smallest `|delta| <=
tolerance`; deterministic tie-break: canvas before sibling, then smaller candidate position, then
edge order left<centre<right. X and Y resolve independently. Nothing in tolerance → position returned
unchanged with `guides: []` (free movement is test-distinguishable).

**WU-8b integration.** In `onPointerDown` (~L2843), after `ensurePositioned(target)`, snapshot sibling
rects **once** from `offsetLeft/offsetTop/offsetWidth/offsetHeight` (canvas coords, matching the
existing `d.left = target.offsetLeft` convention — *not* `getBoundingClientRect`, which is scaled) into
`dragRef.current.siblings`. In `onPointerMove` (~L2873, `move` mode only) call `computeSnap` after the
existing centre-clamp and before writing `style.left/top`; `setGuides(result.guides)`.
**Rendering: absolutely-positioned 1px divs in the existing React overlay container**, reusing the
`data-cs-handle` pattern at ~L3238-3260 (`className="pointer-events-none absolute z-20 bg-indigo-500/80"`).
No SVG — the canvas has no SVG overlay primitive. Guides clear on `onPointerUp`.
**Performance**: per-move cost is O(N) arithmetic only (no DOM reads), asserted `< 1 ms at
SNAP_MAX_SIBLINGS = 24`. Mitigation if exceeded: coalesce `setGuides` behind `requestAnimationFrame`.

**WU-1 cold-start instruction** — inserted after sentence 1, before the existing text-free rule,
mirroring the method-gated branch's voice (~L150-169):

> `"The image MUST depict the subject matter of THIS slide's message as given in slideCopySummary — "`
> `"a literal or metaphorical scene of the message subjects — rendered in the client's established visual style. "`

Aesthetic guidance sentences are retained verbatim. The image-prompt version key is bumped, and
`ImagePromptWriterColdStartParityTests` (which asserts byte-identical cold-start text) is **updated to
the new baseline, never bypassed**.

**WU-7 report** (`ocr-text-detection-feasibility-report.md`) must answer, in order: candidate runtimes
(local Tesseract/`tesseract.js` vs. cloud OCR API vs. a vision-model call), measured accuracy on ≥10
real generated slides (true/false positive and negative counts, not adjectives), per-image latency
added to the ComfyUI stage, behaviour when the dependency is absent, cost per 1k images, and a
one-line **go / no-go** recommendation with its threshold. No code, no port, no adapter.

## File Changes

| File | Action | Description |
|---|---|---|
| `Incoders.Hive.Application/Features/Social/ContentGeneration/Visual/Layout/SlotOverlapDetector.cs` | Create | Pure pairwise overlap detection + total intersection area (WU-2) |
| `…/Visual/Layout/MarkupSlotBoxParser.cs` | Create | Declared `left/top/width/min-height` → `SlotBox` from inline styles; skips `bottom:`-anchored slots (WU-2/WU-4) |
| `…/Visual/Layout/MeasuredLayoutReflow.cs` | Create | The recompute formula, cap, epsilon, clamp, best-effort selection (WU-4) |
| `…/Visual/Layout/SlotTopRewriter.cs` | Create | Idempotent `top:NNNpx` rewrite scoped per `data-slot` (WU-4) |
| `…/Visual/VisualGuardrailValidator.cs` | Modify | Add step 5 + measured-geometry overload; `ValidateOverflow` untouched (WU-2) |
| `…/Visual/VisualGuardrailErrors.cs` | Modify | Add `CrossSlotOverlap(upper, lower)` (WU-2) |
| `…/Pipeline/ContentGenerationJobProcessor.cs` | Modify | Template-path guardrail call after ~L1035 (WU-3); `RenderWithReflowAsync` wrapping ~L1157 (WU-4) |
| `…/Background/ImagePromptWriter.cs` | Modify | Cold-start topical-fidelity instruction + version bump (WU-1) |
| `…/Visual/CompositionDefaults.cs` | Modify | Floor-bounded, reduction-only copy-length auto-fit (WU-5) |
| `Incoders.Hive.Infrastructure/Rendering/HtmlChromiumContentRenderer.cs` | Modify | `SlotGeometryScript` content-box tightening, **only if WU-4 leaves residual slack** (WU-6) |
| `apps/web/lib/studio/alignment-guides.ts` | Create | Pure snap-candidate engine (WU-8a) |
| `apps/web/components/social/content-studio-panel.tsx` | Modify | Sibling snapshot on pointerdown, snap in pointermove, guide divs in the overlay (WU-8b) |
| `openspec/changes/…/ocr-text-detection-feasibility-report.md` | Create | Document only (WU-7) |
| `docs/architecture/adr/0016-two-pass-measure-then-render.md` | Create | D1 ADR: latency posture, 3-render cap, non-convergence disposition |

## Testing Strategy

| Layer | What to Test | Approach |
|---|---|---|
| Unit (new) | Overlap detection incl. the **real defect** (headline `top:255` measured `h:447` vs subtitle `top:575`); side-by-side slots must NOT flag | `Tests/Application/Social/ContentGeneration/Visual/SlotOverlapDetectorTests.cs` |
| Unit (new) | Guardrail step 5 with and without measured geometry; existing 4 steps unchanged | `…/Visual/VisualGuardrailValidatorCrossSlotTests.cs`, beside `VisualGuardrailValidatorTests.cs` |
| Unit (new) | **Formula exactness**: `255 + max(260,447) + gap 60 = 762` for the carousel subtitle; fits-today case returns declared tops unchanged; clamp at `canvasHeight - 140` | `…/Pipeline/MeasuredLayoutReflowTests.cs` (existing `Pipeline/` folder) |
| Unit (new) | **Fast path renders once**; **cap == 3** against a pathological stub whose measured height grows each pass; best-effort = min intersection area; diagnostic field set emitted | same file, stub geometry source — no Chromium |
| Unit (extend) | Cold-start instruction present, references `slideCopySummary`, aesthetic guidance survives | extend `ImagePromptWriterColdStartParityTests.cs` + `ImagePromptWriterTests.cs` |
| Regression | Flat-path byte-identity golden and every existing template-path test pass **unchanged** (blocking) | existing suites, unmodified |
| Unit (web) | `computeSnap` axes, tolerance boundary at exactly 6px, tie-break determinism, `guides: []` when free, `< 1 ms @ 24 siblings` | `apps/web/lib/studio/alignment-guides.test.ts` via `tsx --test "lib/**/*.test.ts"` — the `degrade-badge.ts` precedent |
| Manual | Owner review of one real generation for two clients with materially different `SizeRatio` | proposal success criteria |

`apps/web` has **no component-render test infrastructure**. WU-8b is therefore covered by extracting
`guideToOverlayStyle` and the sibling-snapshot mapping into the same pure module and testing those;
the handler wiring itself is owner-verified. Stated honestly rather than claimed.

## Threat Matrix

N/A — no routing, shell command, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary is added or changed. The Chromium render is a pre-existing boundary:
this change adds no new process invocation, no new launch argument, and no new externally-sourced
input to it. The reflow loop only re-invokes the **existing** `RenderWithGeometryAsync` with markup
this codebase authored.

## Migration / Rollout

No migration required. No schema change, no new runtime dependency, no credential, no feature flag.
Every work unit reverts independently (proposal `## Rollback Plan`).

## Open Questions

- [ ] WU-6 is conditional by construction: whether `SlotGeometryScript` needs content-box tightening
      can only be answered by measuring residual slack **after** WU-4 ships. Empty WU-6 is a valid
      outcome.
- [ ] Whether the template-path guardrail call (D-B) should become blocking once WU-4 supplies
      measured evidence — deferred to WU-4's implementation, decided against the observed
      false-positive rate.
