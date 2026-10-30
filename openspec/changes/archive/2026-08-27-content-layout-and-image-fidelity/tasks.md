# Tasks: Content Layout and Image Fidelity

## Status: COMPLETE

All ten work units (WU-1 through WU-9, WU-8 split into WU-8a/WU-8b) landed on `dev`. No work unit
was cut. Item 8b.6 (owner manual drag/Alt-bypass verification of the alignment guides on a real
multi-slot canvas) is an explicit, pending **human action**, not an implementation gap — no
automated component-render harness exists in this repo for `apps/web`, matching the honesty
pattern in `design.md`'s Testing Strategy. Final gate results (2026-08-27):
`dotnet test SocialStudio.Tests -c Release` — 3947 passed, 2 skipped (pre-existing), 0 failed;
`cd apps/web && npm test` — 406 passed, 0 failed; `npm run build` — succeeded (pre-existing
warnings only); `npm run lint` — warnings only, no errors.

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~4,100 total across 10 work units (WU-1..WU-9, with WU-8 split a/b) |
| 400-line budget risk | n/a per-unit — budget here is 800 lines/unit per Session Preflight |
| Chained PRs recommended | No |
| Suggested split | Single PR strategy: 10 sequential direct-to-`dev` commits, one GitHub issue |
| Delivery strategy | single-pr |
| Chain strategy | pending (not applicable — sequential commits, not stacked/chained PRs) |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Medium — WU-4 (~700, now confirmed 3 new types) sits within 100 lines of the
800 budget; WU-8's split (WU-8a ~440 / WU-8b ~400) is already resolved. No unit's task-level
breakdown grew beyond the proposal's re-forecast — WU-4's three new `Visual/Layout/` types
(`SlotOverlapDetector` = WU-2, `MeasuredLayoutReflow` + `SlotTopRewriter` + `RenderWithReflowAsync`
= WU-4) match design.md's file map exactly; no hidden growth found. WU-4 remains a **named split
candidate** if implementation measures over budget: seam = Infrastructure loop (`MeasuredLayoutReflow`,
`SlotTopRewriter`) vs. Application integration (`RenderWithReflowAsync`, `ContentGenerationJobProcessor`
wiring).

### Suggested Work Units

| Unit | Goal | Likely commit | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| WU-1 | Cold-start topical-fidelity instruction | Commit 1 | `dotnet test SocialStudio.Tests --filter FullyQualifiedName~ImagePromptWriter` | N/A — prompt text asserted offline; owner-verified by real generation | Revert restores prior instruction string + prompt-version key |
| WU-2 | `SlotOverlapDetector` (pure, new) | Commit 2 | `dotnet test SocialStudio.Tests --filter FullyQualifiedName~SlotOverlapDetector` | N/A — pure function, no render | Inert until WU-3 calls it; reverts freely |
| WU-3 | Guardrail wiring on template-backed path | Commit 3 | `dotnet test SocialStudio.Tests --filter FullyQualifiedName~VisualGuardrailValidatorCrossSlot` | N/A — unit-level, non-blocking call | Restores pre-change unvalidated template path |
| WU-4 | Two-pass reflow loop | Commit 4 | `dotnet test SocialStudio.Tests --filter FullyQualifiedName~MeasuredLayoutReflow\|RenderWithReflow` | N/A — Chromium render covered by existing `HtmlChromiumContentRenderer` test harness (no new process boundary) | Removes loop, restores fixed `top` literals; markup shape unchanged so no downstream break |
| WU-5 | Auto-fit typography | Commit 5 | `dotnet test SocialStudio.Tests --filter FullyQualifiedName~CompositionDefaults` | N/A | Restores rigid size application |
| WU-6 (conditional) | Slot geometry tightening | Commit 6 (may be empty) | `dotnet test SocialStudio.Tests --filter FullyQualifiedName~SlotGeometry` | N/A | Restores padded CSS box capture |
| WU-7 | OCR feasibility report | Commit 7 | N/A — doc only | N/A | Delete the doc; no production dependency |
| WU-8a | Snap-candidate engine (pure TS) | Commit 8 | `tsx --test "apps/web/lib/studio/alignment-guides.test.ts"` | N/A — pure module | Inert until WU-8b wires it in |
| WU-8b | Guide rendering + pointer integration | Commit 9 | `tsx --test "apps/web/lib/studio/**/*.test.ts"` + manual drag verification | Manual: Content Studio canvas drag/resize, owner-verified (no component-render harness) | Restores free unsnapped dragging |
| WU-9 | Spec/doc sync + ADR | Commit 10 | `pwsh -File scripts/gates/Run-Gates.ps1` | N/A — doc/spec only | Documentation and spec deltas only |

---

## WU-1: Cold-Start Topical-Fidelity Instruction (Track A, independent)
Spec: CACD-IMG-02 (modified), CLIF-IMG-01 (added, regression). Design: WU-1 instruction text (design.md `## Data Flow` prelude / "WU-1 cold-start instruction").

- [x] 1.1 RED: extend `SocialStudio.Tests/Application/Social/ContentGeneration/Background/ImagePromptWriterColdStartParityTests.cs` — **update the byte-identity baseline** to the new instructed cold-start string (design.md's exact wording), replacing the old assertion (not adding a bypass). Satisfies CACD-IMG-02-a.
- [x] 1.2 RED: add `ImagePromptWriterTests.cs` cases asserting aesthetic guidance (style descriptors, motifs, background descriptors, voice tone, palette) is still present and not displaced. Satisfies CACD-IMG-02-b.
- [x] 1.3 RED: add a regression case asserting the method-gated branch (`CACD-IMG-01`) wording/structure is byte-identical to before. Satisfies CLIF-IMG-01-a.
- [x] 1.4 RED: add a regression case for the remaining pipeline (guardrail retry, CSS fallback, storage upload, reference-image conditioning) behaving identically for a cold-start client. Satisfies CACD-IMG-02-c.
- [x] 1.5 GREEN: edit `SocialStudio.Application/Features/Social/ContentGeneration/Background/ImagePromptWriter.cs` (~L172-181) — insert the topical-fidelity sentence into the cold-start `instruction` string, mirroring design.md's exact wording; bump the image-prompt version key.
- [x] 1.6 Run `dotnet test SocialStudio.Tests --filter FullyQualifiedName~ImagePromptWriter` — confirm all four scenarios green.

## WU-2: `SlotOverlapDetector` (Track B, independent of WU-3, pure new type)
Spec: CLIF-CG-01 (added). Design: D-A, interfaces block (`SlotOverlapDetector.cs`).

- [x] 2.1 RED: create `SocialStudio.Tests/Application/Social/ContentGeneration/Visual/Layout/SlotOverlapDetectorTests.cs` — case reproducing the real defect: headline `top:255px` measured `h:447px` (149px font, 2-3 line wrap) vs subtitle `top:575px` → violation reported. Satisfies CLIF-CG-01-a. (Path corrected from tasks.md's stated `Features/Social/...` to match this repo's actual test-tree convention, mirroring production `Visual/Layout/` — see Deviations.)
- [x] 2.2 RED: same file — side-by-side (horizontally offset) slots with overlapping vertical bands but non-overlapping horizontal bands must NOT flag (both-axis intersection rule). Satisfies CLIF-CG-01-b.
- [x] 2.3 RED: same file — determinism case: pairs ordered by `(Top, Slot)`, emitted once per pair; `TotalIntersectionArea` sums correctly. Also added a tolerance-boundary case (touching by exactly `TolerancePx` must not flag).
- [x] 2.4 GREEN: create `SocialStudio.Application/Features/Social/ContentGeneration/Visual/Layout/SlotOverlapDetector.cs` — `SlotBox` record struct, `SlotOverlap` record, `Detect`/`TotalIntersectionArea` per design.md's exact contract (`TolerancePx = 1.0`).
- [x] 2.5 Run `dotnet test SocialStudio.Tests --filter FullyQualifiedName~SlotOverlapDetector` — confirm green. (4/4 passed.)

## WU-3: Guardrail Wiring on the Template-Backed Path (Track B, depends on WU-2)
Spec: CLIF-CG-02 (added). Design: D-B (non-blocking, post-priority-resolution call site).

- [x] 3.1-3.2 DEVIATION (see below): no `VisualGuardrailValidator` overload / `VisualGuardrailErrors.CrossSlotOverlap` was added — the actual call site (pre-render, before any Chromium measurement exists) can only ever see DECLARED geometry, so the check calls `SlotOverlapDetector.Detect` directly against declared boxes parsed from the final markup, per the orchestrator's WU-3 scope note and design.md D-A's stated declared-box fallback ("can only catch a builder regression"). RED coverage: `SocialStudio.Tests/Application/Social/ContentGeneration/Pipeline/VisualGuardrailCrossSlotWiringTests.cs` — `ExtractDeclaredSlotBoxes_*` unit cases (parses left/top/width/min-height per data-slot; skips bottom-anchored slots; real story-template literals do not overlap; a hypothetical overlapping declaration IS detected).
- [x] 3.3 RED/GREEN: same file — `Pipeline_EnforcedBackgroundStory_NoOverlapWarning_RendererStillCalledWithTemplateMarkup` asserts the check runs on the enforced-background (template-backed) path's actual final markup, not inferred, and does not alter which markup renders. Satisfies CLIF-CG-02-a.
- [x] 3.4 RED/GREEN: same file — `Pipeline_AiMarkupPath_NoEnforcedBackground_NoOverlapWarning` asserts the check is gated off (and therefore silent) on the pure AI-markup path; both integration tests assert `Record.ExceptionAsync`/pipeline completion — non-blocking, no job failure. Satisfies CLIF-CG-02-b.
- [x] 3.5-3.6 N/A per deviation above (no `VisualGuardrailValidator`/`VisualGuardrailErrors` changes).
- [x] 3.7 GREEN: edited `SocialStudio.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` — added `internal static ExtractDeclaredSlotBoxes(string markup)` (declared-box parser, `InternalsVisibleTo`-testable) and the second, separate, non-blocking `SlotOverlapDetector.Detect` call immediately after the `validatedMarkup` if/else closes (~L1035) and before `spec = spec with { … }` (~L1038), gated on `enforcedBackground is not null`; logs a structured `LogWarning` on a hit, no markup-selection change, no retry/fallback routing.
- [x] 3.8 Ran `dotnet test SocialStudio.Tests --filter FullyQualifiedName~VisualGuardrailCrossSlotWiringTests` (6/6 green) and the full suite (`dotnet test SocialStudio.Tests -c Release`: 3923 passed, 2 skipped pre-existing, 0 failed) — confirm green.

## WU-4: Two-Pass Reflow Loop (Track B, depends on WU-2/WU-3, largest unit — split-candidate if over budget)
Spec: CLIF-CG-03, CLIF-CG-04, CLIF-CG-05, CLIF-CG-06, CLIF-CG-08. Design: D-C, D-D, `MeasuredLayoutReflow` formula, `MaxRenderPasses = 3`, `EpsilonPx = 1.0`, ADR 0016.

### WU-4a: `MeasuredLayoutReflow` + `SlotTopRewriter` (pure Application types) — natural split seam if needed
- [x] 4.1 RED/GREEN: `SocialStudio.Tests/Application/Features/Social/ContentGeneration/Pipeline/MeasuredLayoutReflowTests.cs` — `RecomputeTops_FormulaExactness_StoryHeadlineGrows_SubtitlePushedByMeasuredExtent`: `255 + max(260,447) + gap 60 = 762` (the story headline/subtitle pair — design.md's own worked example; the carousel pair uses different literals but the identical formula). Satisfies CLIF-CG-04-a's numeric core.
- [x] 4.2 RED/GREEN: same file — `RecomputeTops_EverythingAlreadyFits_ReturnsDeclaredTopsExactly_ByteIdentity`. Satisfies CLIF-CG-08-a.
- [x] 4.3 RED/GREEN: same file — `RecomputeTops_ClampsAtCanvasBottomMinus140_ResidualOverlapNotHiddenByPushingOffCanvas`.
- [x] 4.4 RED/GREEN: same file — `RenderWithReflowAsync_NoOverlapNoMaterialDelta_StopsAfterExactlyOneRender` (render-count assertion via a stub `IContentRendererWithGeometry`, `CallCount == 1`). Satisfies CLIF-CG-03-a.
- [x] 4.5 RED/GREEN: same file — `RenderWithReflowAsync_NeverConverges_HardCapsAtThreeRenders_NeverAFourthRender` (pathological stub with a 4th, never-consumed geometry entry proving the structural cap; `CallCount == MeasuredLayoutReflow.MaxRenderPasses`). Satisfies CLIF-CG-05-a.
- [x] 4.6 RED/GREEN: same file — non-convergence case folded into the test above: best-effort = pass with minimum `TotalIntersectionArea` (asset content asserts pass 3 was selected: areas 66000/82500/24750 for passes 1/2/3), diagnostic log asserted for `JobId`, format key, `3 render pass(es)`, and explicitly `DoesNotContain("CAC-IMG-03")`. Satisfies CLIF-CG-05-b.
- [x] 4.7 RED/GREEN: `SocialStudio.Tests/Application/Social/ContentGeneration/Visual/Layout/SlotTopRewriterTests.cs` — scoped rewrite, empty-map no-op, bottom-anchored no-op, idempotence.
- [x] 4.8 RED/GREEN: `SocialStudio.Tests/Application/Social/ContentGeneration/Visual/Layout/MarkupSlotBoxParserTests.cs` — declared box parsing, bottom-anchored skip, missing-dimension skip.
- [x] 4.9 GREEN: `SocialStudio.Application/Features/Social/ContentGeneration/Visual/Layout/MeasuredLayoutReflow.cs` — matches design.md's formula/constants exactly (verified against the tests above).
- [x] 4.10 GREEN: `SocialStudio.Application/Features/Social/ContentGeneration/Visual/Layout/SlotTopRewriter.cs`.
- [x] 4.11 GREEN: `SocialStudio.Application/Features/Social/ContentGeneration/Visual/Layout/MarkupSlotBoxParser.cs`.
- [x] 4.12 Ran `dotnet test SocialStudio.Tests -c Release --filter "FullyQualifiedName~MeasuredLayoutReflow|FullyQualifiedName~SlotTopRewriter|FullyQualifiedName~MarkupSlotBoxParser"` — 13/13 passed.

### WU-4b: `RenderWithReflowAsync` integration (Application, wraps existing render call site)
DEVIATION: tasks 4.13-4.16 originally called for extending a single "pipeline test file" with full-`ProcessAsync` integration fixtures. Implemented instead as direct tests of the `internal` `RenderWithReflowAsync` method (in `MeasuredLayoutReflowTests.cs`, `InternalsVisibleTo`-testable, same pattern WU-3's `VisualGuardrailCrossSlotWiringTests` established for `ExtractDeclaredSlotBoxes`) against a stub `IContentRendererWithGeometry` — narrower than a full pipeline run, exercises the identical bounded-loop contract without needing to fabricate a full `ClientAesthetic`/`SizeRatio` fixture through the entire `ProcessAsync` call graph.
- [x] 4.13 RED/GREEN: `RenderWithReflowAsync_OverlapOnPass1_RecomputesAndReRenders_FinalPassHasNoIntersection` — pass 1 overlap (headline measured 447px intruding into subtitle), recompute + re-render, asserts `stub.CallCount` in `[2, MaxRenderPasses]` and the shipped result's markup carries the resolved layout. Satisfies CLIF-CG-04-a (integration level).
- [x] 4.14 RED/GREEN: same test — asserts `position:absolute` and both `data-slot` attributes survive in `PersistedMarkup`; no document-flow/flex conversion. Satisfies CLIF-CG-04-b.
- [x] 4.15 Verified by construction + full-suite regression (not a new duplicate test): `templateBackedForReflow` is set `true` only in the two `BuildTemplateBackedMarkup` call sites; `RenderFaithfulFlat()`/`BuildFlatColorMarkup` never touch that flag, so the reflow branch at `ContentGenerationJobProcessor.cs` ~L1204 is structurally unreachable from the flat path. Confirmed via the full `dotnet test` run (task 4.19) — every pre-existing flat-path byte-identity golden and AI-markup test still passes unchanged. Satisfies CLIF-CG-06-a.
- [x] 4.16 RED/GREEN: `RenderWithReflowAsync_NoOverlapNoMaterialDelta_StopsAfterExactlyOneRender` (task 4.4's test) doubles as the latency-posture assertion — the byte-identity/already-fitting fixture leaves render count at exactly 1. Ties to CLIF-CG-08-a end-to-end.
- [x] 4.17 GREEN: `internal async Task<Result<ReflowRenderResult>> RenderWithReflowAsync(...)` in `ContentGenerationJobProcessor.cs` (~L2093), wrapping the `RenderWithGeometryAsync` call site (~L1195-1249): parses declared/measured boxes, `SlotOverlapDetector.Detect` → fast path or `MeasuredLayoutReflow.RecomputeTops` → `SlotTopRewriter.Apply`, capped at `MeasuredLayoutReflow.MaxRenderPasses`, non-convergence best-effort selection + structured `ContentGeneration.LayoutReflow` diagnostic. `internal` (not `private`) specifically so the tests above can drive it directly via `InternalsVisibleTo`.
- [x] 4.18 GREEN: wired reflowable slot sets — `ReflowSlotOrderCarousel = ["title", "subtitle"]` and `ReflowSlotOrderStory = ["headline", "subtitle", "body"]` (~L2052-2054), selected per format key at the call site (~L1206-1210) and passed into `RenderWithReflowAsync`.
- [x] 4.19 Ran full suite: `dotnet test SocialStudio.Tests -c Release` — 3936 passed, 2 skipped (pre-existing), 0 failed. Every pre-existing template-path and flat-path test passes unchanged.
- [x] 4.20 Wrote `docs/architecture/adr/0016-two-pass-measure-then-render.md` — latency posture, 3-render hard cap, non-convergence disposition, disjoint-from-`CAC-IMG-03` correction (D-D).

## WU-5: Copy-Length-Aware Typography Auto-Fit (Track B, sequenced after WU-4)
Spec: CLIF-CG-07. Design: file map (`CompositionDefaults.cs`), Risks table (auto-fit legibility floor).

- [x] 5.1 RED: created `SocialStudio.Tests/Application/Social/ContentGeneration/Visual/CompositionDefaultsAutoFitTests.cs` (path corrected from tasks.md's stated `Features/Social/...` to match this repo's actual test-tree convention, mirroring `CompositionDefaultsTypographyTests.cs`/`ClientTypographyResolutionTests.cs` in the same directory — see Deviations). `UnusuallyLongCopy_ReducesResolvedHeadingSize_WithinStatedFloor` — a legitimate `SizeRatio` (0.10 → 135px unfit) + a 200-char headline reduces the resolved size, never below the stated floor. Satisfies CLIF-CG-07-a. NOT redundant with WU-4: `MeasuredLayoutReflow` only repositions SUBSEQUENT slots from a preceding slot's measured extent — it cannot shrink a headline slot's OWN size when nothing sits below it, nor when its own clamp-at-canvas-bottom is reached (`MeasuredLayoutReflow`'s own clamp comment explicitly defers that case to "WU-5's auto-fit").
- [x] 5.2 RED: same file — `AutoFit_NeverIncreasesTheResolvedSize_ForAnyCopyLength` theory across copy lengths 0/10/39/40/80/200/1000 chars (including the exact threshold boundary). Satisfies CLIF-CG-07-b.
- [x] 5.3 RED: same file — `CopyThatAlreadyFitsAtItsMeasuredRatio_RendersByteIdentically_NoOpRegression` and `ExistingCallersWithoutHeadlineCopy_AreUnaffected_ByteIdenticalToPreWu5` (no-op regression, ties to CLIF-CG-08).
- [x] 5.4 GREEN: edited `SocialStudio.Application/Features/Social/ContentGeneration/Visual/CompositionDefaults.cs` — added `ApplyCopyLengthAutoFit` (floor-bounded, reduction-only: no-op ≤40 chars, linear interpolation to a 0.70 floor ratio by 120 chars) and wired it into `ResolveTypography` via a new optional `string? headlineCopy = null` parameter (default keeps every existing call site — none of which pass copy today — byte-identical, satisfying CLIF-CG-08 without any composer wiring; matches design.md's file map, which lists only `CompositionDefaults.cs` for WU-5).
- [x] 5.5 Ran `dotnet test SocialStudio.Tests -c Release --filter FullyQualifiedName~CompositionDefaults` — 18/18 passed.

## WU-6 (conditional): Slot Geometry Tightening for Content Studio
Spec: CLIF-CS-01. Design: Open Questions — "Empty WU-6 is a valid outcome." Sequenced strictly after WU-4.

- [x] 6.1 Measured residual slack empirically (real Chromium, not speculative): inspected `SlotTopRewriter.Apply` — it only ever rewrites a slot's `top:`, never `min-height:`. `SlotGeometryScript` (pre-fix) captured `getBoundingClientRect()` directly on the `[data-slot]` element itself, which IS the styled `h1`/`p` carrying the `min-height` literal (confirmed against the real production headline slot, `ContentGenerationJobProcessor.cs` ~L2538: `min-height:260px`). WU-4's reflow only repositions SUBSEQUENT slots from a PRECEDING slot's measured extent — it never touches an individual slot's own declared box size. **Finding: slack remains** for any slot whose actual text content is shorter than its declared `min-height` (the common case) — WU-4 does nothing for this, since a slot that already fits needs no repositioning. A RED integration test against real headless Chromium (`RenderWithGeometryAsync_ShortTextInMinHeightSlot_CapturedBoxIsTighterThanTheDeclaredMinHeight`, short "Hi" text in a 260px-min-height/88px-font headline slot) confirmed the captured height was exactly 260px pre-fix — proving the slack empirically rather than by inference alone.
- [x] 6.2 RED: extended `SocialStudio.Tests/Infrastructure/Rendering/GeometryCaptureTests.cs` (existing Infrastructure rendering test suite, browser-gated, real headless Chromium — not skipped in this environment) with the test from 6.1: captured box height must be `< 260` (measurably smaller than the pre-change min-height-driven box) and `>= 60` (still fully contains the rendered glyph ink for one 88px line). Ran RED first against the unmodified script — failed with `got 260px`, confirming the defect. Satisfies CLIF-CS-01-a.
- [x] 6.3 CLIF-CS-01-b (Content Studio editing box tracks the tightened geometry) requires no separate frontend test or change: Content Studio's editing canvas already renders from the backend-persisted `SlotGeometryEntry` values (pre-existing behavior, unchanged by this fix) — tightening the SAME capture at its single source in `HtmlChromiumContentRenderer.cs` is sufficient; there is no second, independent geometry path in Content Studio to diverge from.
- [x] 6.4 GREEN: edited `SocialStudio.Infrastructure/Rendering/HtmlChromiumContentRenderer.cs` (`SlotGeometryScript.ExtractExpression`) — added a `document.createRange().selectNodeContents(el)` measurement of the element's rendered content, using `range.getBoundingClientRect()` (the actual glyph-ink line box) in place of `el.getBoundingClientRect()` whenever the range yields a positive width/height, falling back to the CSS box otherwise (empty/replaced content). Safe by construction for WU-4: when content already exceeds `min-height` (the wrapping-headline case WU-4's formula depends on), the element's own box already equals the content height, so the Range measurement and the CSS box coincide — only the short-text/padded case actually tightens. Re-ran the RED test from 6.2 — now passes (height `< 260`, `>= 60`); re-ran all 7 `GeometryCaptureTests` (0 failed) and all WU-4 tests (`MeasuredLayoutReflow`/`SlotTopRewriter`/`MarkupSlotBoxParser`/`RenderWithReflow`/`PptxExportAdapter`, 36/36 passed) — confirmed no regression to the reflow formula or export path.
- [x] 6.5 N/A — slack DID remain (see 6.1); this is the "slack remains" branch, not the empty-WU-6 branch. WU-6 shipped real production code (a scoped, minimally-invasive one-line-of-reasoning content-box tightening within the existing capture script, no CSS-flow rewrite — consistent with D1's rejection of the more invasive flex/flow approach).

## WU-7: OCR / Baked-In-Text Feasibility Report (Track C, independent, documentation-only)
Spec: none (no runtime behavior; D2 explicitly out of scope for production code).

- [x] 7.1 Create `openspec/changes/content-layout-and-image-fidelity/ocr-text-detection-feasibility-report.md` covering, in order: candidate runtimes (local Tesseract/`tesseract.js` vs. cloud OCR API vs. vision-model call), measured accuracy on ≥10 real generated slides (true/false positive/negative counts), per-image latency added to the ComfyUI stage, behaviour when the dependency is absent, cost per 1k images, one-line go/no-go recommendation with threshold.
- [x] 7.2 Confirm no production code, port, or adapter is introduced anywhere in this task (explicit non-goal check).

## WU-8a: Snap-Candidate Engine (Track D, pure TS, before WU-8b)
Spec: CLIF-CS-02, CLIF-CS-03, CLIF-CS-04, CLIF-CS-05. Design: D-E, `alignment-guides.ts` contract.

- [x] 8a.1 RED: create `apps/web/lib/studio/alignment-guides.test.ts` — canvas-centre-x/y snap within tolerance renders the corresponding guide axis. Satisfies CLIF-CS-02-a/b.
- [x] 8a.2 RED: same file — per-sibling six-axis snap (left/centre-x/right, top/centre-y/bottom) cases, including left-edge and centre-y snap. Satisfies CLIF-CS-03-a/b.
- [x] 8a.3 RED: same file — deterministic tie-break: canvas before sibling, then smaller candidate position, then edge order left<centre<right; two siblings offering candidates on different axes resolve to exactly one winner per axis. Satisfies CLIF-CS-03-c.
- [x] 8a.4 RED: same file — tolerance boundary at exactly `SNAP_TOLERANCE_PX = 6`; no candidate within tolerance returns unchanged position with `guides: []`. Satisfies CLIF-CS-04-a.
- [x] 8a.5 RED: same file — performance assertion: `computeSnap` completes in `< 1 ms` at `SNAP_MAX_SIBLINGS = 24`. Satisfies CLIF-CS-05-a.
- [x] 8a.6 RED: same file — `guideToOverlayStyle` mapping unit cases (position/scale → overlay div coordinates).
- [x] 8a.7 GREEN: create `apps/web/lib/studio/alignment-guides.ts` — `Rect`, `GuideLine`, `SnapResult`, `computeSnap`, `guideToOverlayStyle`, `SNAP_TOLERANCE_PX = 6`, `SNAP_MAX_SIBLINGS = 24`, per design.md's exact contract.
- [x] 8a.8 Run `tsx --test "apps/web/lib/studio/alignment-guides.test.ts"` — confirm green.

## WU-8b: Guide Rendering + Pointer Integration (Track D, depends on WU-8a)
Spec: CLIF-CS-02 through CLIF-CS-05 (integration-level), D-E (Alt bypass).

- [x] 8b.1 RED: extend `apps/web/lib/studio/alignment-guides.test.ts` (or a sibling test file) with the sibling-rect-snapshot mapping logic extracted into `lib/studio/` (pure), covering the `offsetLeft/offsetTop/offsetWidth/offsetHeight` canvas-coordinate convention — per design.md's stated testing strategy (no component-render harness in `apps/web`). (`offsetBoxesToRects` added to `alignment-guides.ts`.)
- [x] 8b.2 GREEN: edit `apps/web/components/social/content-studio-panel.tsx` `onPointerDown` (~L2843) — after `ensurePositioned(target)`, snapshot sibling rects once into `dragRef.current.siblings`.
- [x] 8b.3 GREEN: edit `onPointerMove` (~L2873, `move` mode only) — call `computeSnap` after the existing centre-clamp and before writing `style.left/top`; `setGuides(result.guides)`.
- [x] 8b.4 GREEN: add Alt-key bypass — holding Alt suppresses `computeSnap` application for the duration of the drag (D-E).
- [x] 8b.5 GREEN: render guide lines as absolutely-positioned 1px divs in the existing overlay container, reusing the `data-cs-handle` pattern (~L3238-3260, `className="pointer-events-none absolute z-20 bg-indigo-500/80"`); clear guides on `onPointerUp`.
- [ ] 8b.6 Manual verification: owner drags/resizes elements on a real multi-slot canvas; confirm snap feel and Alt bypass (no automated component-render coverage exists in this repo). **Owner action pending — not executable by the agent.**
- [x] 8b.7 Run `tsx --test "apps/web/lib/studio/**/*.test.ts"` — confirm all `lib/studio/` unit coverage green. (103/103 passed.)

## WU-9: Spec/Doc Sync + PRD Amendment (last — depends on all prior units)
Spec: none new; this is the sync unit itself.

- [x] 9.1 Merged `openspec/changes/content-layout-and-image-fidelity/specs/{image-generation,content-generation,content-studio}/spec-delta.md` into the corresponding `openspec/specs/*/spec.md` files. Full, unconditional merge (every requirement in this change shipped as real code — unlike the prior change's WU-7/WU-8 cut). Verified each requirement against shipped code before merging: `ImagePromptWriter.cs` (WU-1), `SlotOverlapDetector.cs`/`MeasuredLayoutReflow.cs` (WU-2/4), `ContentGenerationJobProcessor.cs` (WU-3/4), `CompositionDefaults.cs` (WU-5), `HtmlChromiumContentRenderer.cs` (WU-6), `alignment-guides.ts`/`content-studio-panel.tsx` (WU-8a/8b). Corrected CLIF-CG-01/CLIF-CG-02 wording during merge: the delta described the cross-slot check as living inside `VisualGuardrailValidator`; the shipped implementation (WU-3's documented deviation) instead calls `SlotOverlapDetector.Detect` directly from `ContentGenerationJobProcessor` against declared boxes, since the call site runs before any Chromium measurement exists — `VisualGuardrailValidator` itself was not modified. Requirement/scenario IDs and observable behavior are unchanged; only the mechanism description was corrected.
- [x] 9.2 Amended PRD §12 (Content Bank Planner, which builds on Content Studio/ADR 0004 — the section referencing the editorial review loop this change's Content Studio capabilities extend) with a blockquote for the layout-reflow/typography-auto-fit fix and the alignment-guide capability, referencing ADR 0016. Confirmed §4 non-goal item 2 ("Native editing suite replacing Canva") needs no amendment — alignment guides are an incremental capability on the existing editing canvas, not a full native editing suite.
- [x] 9.3 Searched `.github/project-ai/instructions/*.md` and `docs/` for references to `BuildTemplateBackedMarkup`, `SlotGeometryEntry`, `VisualGuardrailValidator`, `getBoundingClientRect`, `RenderWithGeometryAsync`. The one hit in `project.domain.md` (`VisualGuardrailValidator.ValidateTypography`, font-catalog allow-list) is unrelated to this change's cross-slot/geometry work — no update needed. ADR 0004 has no geometry-capture references. No other doc drift found.
- [x] 9.4 Ran the final gate directly (superseding `Run-Gates.ps1`'s environment-only `tests.backend` false failure from a Visual-Studio-held Debug-DLL file lock, confirmed unrelated to code at every prior batch): `dotnet test SocialStudio.Tests -c Release` — 3947 passed, 2 skipped (pre-existing), 0 failed. `cd apps/web && npm test` — 406 passed, 0 failed. `npm run build` — succeeded (pre-existing warnings only, none introduced by this change). `npm run lint` — warnings only (pre-existing), no errors.
- [ ] 9.5 Owner action pending — not executable by the agent: record accept/reject review for two real generations (materially different `SizeRatio`, materially different headline length) per proposal Success Criteria.
