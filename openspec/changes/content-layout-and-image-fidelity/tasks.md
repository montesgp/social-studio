# Tasks: Content Layout and Image Fidelity

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
| WU-1 | Cold-start topical-fidelity instruction | Commit 1 | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~ImagePromptWriter` | N/A — prompt text asserted offline; owner-verified by real generation | Revert restores prior instruction string + prompt-version key |
| WU-2 | `SlotOverlapDetector` (pure, new) | Commit 2 | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~SlotOverlapDetector` | N/A — pure function, no render | Inert until WU-3 calls it; reverts freely |
| WU-3 | Guardrail wiring on template-backed path | Commit 3 | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~VisualGuardrailValidatorCrossSlot` | N/A — unit-level, non-blocking call | Restores pre-change unvalidated template path |
| WU-4 | Two-pass reflow loop | Commit 4 | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~MeasuredLayoutReflow\|RenderWithReflow` | N/A — Chromium render covered by existing `HtmlChromiumContentRenderer` test harness (no new process boundary) | Removes loop, restores fixed `top` literals; markup shape unchanged so no downstream break |
| WU-5 | Auto-fit typography | Commit 5 | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~CompositionDefaults` | N/A | Restores rigid size application |
| WU-6 (conditional) | Slot geometry tightening | Commit 6 (may be empty) | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~SlotGeometry` | N/A | Restores padded CSS box capture |
| WU-7 | OCR feasibility report | Commit 7 | N/A — doc only | N/A | Delete the doc; no production dependency |
| WU-8a | Snap-candidate engine (pure TS) | Commit 8 | `tsx --test "apps/web/lib/studio/alignment-guides.test.ts"` | N/A — pure module | Inert until WU-8b wires it in |
| WU-8b | Guide rendering + pointer integration | Commit 9 | `tsx --test "apps/web/lib/studio/**/*.test.ts"` + manual drag verification | Manual: Content Studio canvas drag/resize, owner-verified (no component-render harness) | Restores free unsnapped dragging |
| WU-9 | Spec/doc sync + ADR | Commit 10 | `pwsh -File scripts/gates/Run-Gates.ps1` | N/A — doc/spec only | Documentation and spec deltas only |

---

## WU-1: Cold-Start Topical-Fidelity Instruction (Track A, independent)
Spec: CACD-IMG-02 (modified), CLIF-IMG-01 (added, regression). Design: WU-1 instruction text (design.md `## Data Flow` prelude / "WU-1 cold-start instruction").

- [x] 1.1 RED: extend `Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/ImagePromptWriterColdStartParityTests.cs` — **update the byte-identity baseline** to the new instructed cold-start string (design.md's exact wording), replacing the old assertion (not adding a bypass). Satisfies CACD-IMG-02-a.
- [x] 1.2 RED: add `ImagePromptWriterTests.cs` cases asserting aesthetic guidance (style descriptors, motifs, background descriptors, voice tone, palette) is still present and not displaced. Satisfies CACD-IMG-02-b.
- [x] 1.3 RED: add a regression case asserting the method-gated branch (`CACD-IMG-01`) wording/structure is byte-identical to before. Satisfies CLIF-IMG-01-a.
- [x] 1.4 RED: add a regression case for the remaining pipeline (guardrail retry, CSS fallback, storage upload, reference-image conditioning) behaving identically for a cold-start client. Satisfies CACD-IMG-02-c.
- [x] 1.5 GREEN: edit `Incoders.Hive.Application/Features/Social/ContentGeneration/Background/ImagePromptWriter.cs` (~L172-181) — insert the topical-fidelity sentence into the cold-start `instruction` string, mirroring design.md's exact wording; bump the image-prompt version key.
- [x] 1.6 Run `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~ImagePromptWriter` — confirm all four scenarios green.

## WU-2: `SlotOverlapDetector` (Track B, independent of WU-3, pure new type)
Spec: CLIF-CG-01 (added). Design: D-A, interfaces block (`SlotOverlapDetector.cs`).

- [x] 2.1 RED: create `Incoders.Hive.Tests/Application/Social/ContentGeneration/Visual/Layout/SlotOverlapDetectorTests.cs` — case reproducing the real defect: headline `top:255px` measured `h:447px` (149px font, 2-3 line wrap) vs subtitle `top:575px` → violation reported. Satisfies CLIF-CG-01-a. (Path corrected from tasks.md's stated `Features/Social/...` to match this repo's actual test-tree convention, mirroring production `Visual/Layout/` — see Deviations.)
- [x] 2.2 RED: same file — side-by-side (horizontally offset) slots with overlapping vertical bands but non-overlapping horizontal bands must NOT flag (both-axis intersection rule). Satisfies CLIF-CG-01-b.
- [x] 2.3 RED: same file — determinism case: pairs ordered by `(Top, Slot)`, emitted once per pair; `TotalIntersectionArea` sums correctly. Also added a tolerance-boundary case (touching by exactly `TolerancePx` must not flag).
- [x] 2.4 GREEN: create `Incoders.Hive.Application/Features/Social/ContentGeneration/Visual/Layout/SlotOverlapDetector.cs` — `SlotBox` record struct, `SlotOverlap` record, `Detect`/`TotalIntersectionArea` per design.md's exact contract (`TolerancePx = 1.0`).
- [x] 2.5 Run `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~SlotOverlapDetector` — confirm green. (4/4 passed.)

## WU-3: Guardrail Wiring on the Template-Backed Path (Track B, depends on WU-2)
Spec: CLIF-CG-02 (added). Design: D-B (non-blocking, post-priority-resolution call site).

- [x] 3.1-3.2 DEVIATION (see below): no `VisualGuardrailValidator` overload / `VisualGuardrailErrors.CrossSlotOverlap` was added — the actual call site (pre-render, before any Chromium measurement exists) can only ever see DECLARED geometry, so the check calls `SlotOverlapDetector.Detect` directly against declared boxes parsed from the final markup, per the orchestrator's WU-3 scope note and design.md D-A's stated declared-box fallback ("can only catch a builder regression"). RED coverage: `Incoders.Hive.Tests/Application/Social/ContentGeneration/Pipeline/VisualGuardrailCrossSlotWiringTests.cs` — `ExtractDeclaredSlotBoxes_*` unit cases (parses left/top/width/min-height per data-slot; skips bottom-anchored slots; real story-template literals do not overlap; a hypothetical overlapping declaration IS detected).
- [x] 3.3 RED/GREEN: same file — `Pipeline_EnforcedBackgroundStory_NoOverlapWarning_RendererStillCalledWithTemplateMarkup` asserts the check runs on the enforced-background (template-backed) path's actual final markup, not inferred, and does not alter which markup renders. Satisfies CLIF-CG-02-a.
- [x] 3.4 RED/GREEN: same file — `Pipeline_AiMarkupPath_NoEnforcedBackground_NoOverlapWarning` asserts the check is gated off (and therefore silent) on the pure AI-markup path; both integration tests assert `Record.ExceptionAsync`/pipeline completion — non-blocking, no job failure. Satisfies CLIF-CG-02-b.
- [x] 3.5-3.6 N/A per deviation above (no `VisualGuardrailValidator`/`VisualGuardrailErrors` changes).
- [x] 3.7 GREEN: edited `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` — added `internal static ExtractDeclaredSlotBoxes(string markup)` (declared-box parser, `InternalsVisibleTo`-testable) and the second, separate, non-blocking `SlotOverlapDetector.Detect` call immediately after the `validatedMarkup` if/else closes (~L1035) and before `spec = spec with { … }` (~L1038), gated on `enforcedBackground is not null`; logs a structured `LogWarning` on a hit, no markup-selection change, no retry/fallback routing.
- [x] 3.8 Ran `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~VisualGuardrailCrossSlotWiringTests` (6/6 green) and the full suite (`dotnet test Incoders.Hive.Tests -c Release`: 3923 passed, 2 skipped pre-existing, 0 failed) — confirm green.

## WU-4: Two-Pass Reflow Loop (Track B, depends on WU-2/WU-3, largest unit — split-candidate if over budget)
Spec: CLIF-CG-03, CLIF-CG-04, CLIF-CG-05, CLIF-CG-06, CLIF-CG-08. Design: D-C, D-D, `MeasuredLayoutReflow` formula, `MaxRenderPasses = 3`, `EpsilonPx = 1.0`, ADR 0016.

### WU-4a: `MeasuredLayoutReflow` + `SlotTopRewriter` (pure Application types) — natural split seam if needed
- [ ] 4.1 RED: create `Incoders.Hive.Tests/Application/Features/Social/ContentGeneration/Pipeline/MeasuredLayoutReflowTests.cs` — formula-exactness case: `255 + max(260,447) + gap 60 = 762` for the carousel subtitle. Satisfies CLIF-CG-04-a's numeric core.
- [ ] 4.2 RED: same file — fits-today case returns declared tops unchanged (byte-identity path). Satisfies CLIF-CG-08-a.
- [ ] 4.3 RED: same file — clamp case at `canvasHeight - 140` (BottomSafePx), residual overlap reported rather than pushing text off-canvas.
- [ ] 4.4 RED: same file — fast-path case: no overlap + no material `top` delta stops at pass 1 (render-count assertion). Satisfies CLIF-CG-03-a.
- [ ] 4.5 RED: same file — hard-cap case: pathological stub whose measured height grows each pass never exceeds 3 renders (render-count assertion, not inferred). Satisfies CLIF-CG-05-a.
- [ ] 4.6 RED: same file — non-convergence case: best-effort = pass with minimum `TotalIntersectionArea`, ties broken by lowest pass index; full diagnostic field set (`JobId`, `FormatKey`, `SpecIndex`, `RenderPasses`, `SelectedPassIndex`, `ResidualOverlapCount`, `ResidualIntersectionAreaPx2`, `OverlapPairs`, `SelectedTops`, `ClampedAtCanvasBottom`) is emitted; asserts NO `CAC-IMG-03` routing. Satisfies CLIF-CG-05-b.
- [ ] 4.7 RED: create `Incoders.Hive.Tests/Application/Features/Social/ContentGeneration/Visual/Layout/SlotTopRewriterTests.cs` — idempotent `top:NNNpx` rewrite scoped per `data-slot`, applied to both render markup (data-URI inlined) and persisted markup.
- [ ] 4.8 RED: create `Incoders.Hive.Tests/Application/Features/Social/ContentGeneration/Visual/Layout/MarkupSlotBoxParserTests.cs` — declared `left/top/width/min-height` parsing from inline styles; `bottom:`-anchored slots (`cta`, `accent`, `signature`, `brand-mark`) are skipped.
- [ ] 4.9 GREEN: create `Incoders.Hive.Application/Features/Social/ContentGeneration/Visual/Layout/MeasuredLayoutReflow.cs` per design.md's exact formula/constants.
- [ ] 4.10 GREEN: create `Incoders.Hive.Application/Features/Social/ContentGeneration/Visual/Layout/SlotTopRewriter.cs`.
- [ ] 4.11 GREEN: create `Incoders.Hive.Application/Features/Social/ContentGeneration/Visual/Layout/MarkupSlotBoxParser.cs`.
- [ ] 4.12 Run `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~MeasuredLayoutReflow|SlotTopRewriter|MarkupSlotBoxParser` — confirm green.

### WU-4b: `RenderWithReflowAsync` integration (Application, wraps existing render call site)
- [ ] 4.13 RED: extend the pipeline test file — assert a long headline at the client's real measured `SizeRatio` renders without intersecting the subtitle on both carousel and story builders. Satisfies CLIF-CG-04-a (integration level).
- [ ] 4.14 RED: same file — assert final markup remains absolutely positioned after reflow; `PptxExportAdapter`/`StudioVisualMarkupResolver` behavior unaffected (regression). Satisfies CLIF-CG-04-b.
- [ ] 4.15 RED: same file — assert the deterministic flat path (`BuildFlatColorMarkup`/`BuildFlatCarouselSlots`) is never measured, re-rendered, or reflowed; existing byte-identity golden passes unchanged. Satisfies CLIF-CG-06-a.
- [ ] 4.16 RED: latency-posture assertion — fast path leaves render count unchanged for the byte-identity fixture (ties to CLIF-CG-08-a end-to-end).
- [ ] 4.17 GREEN: add `private async Task<…> RenderWithReflowAsync(…)` to `ContentGenerationJobProcessor.cs`, wrapping the existing `RenderWithGeometryAsync` block at ~L1157, calling `SlotOverlapDetector.Detect` → fast path or `MeasuredLayoutReflow.RecomputeTops` → `SlotTopRewriter.Apply`, capped at `MaxRenderPasses`.
- [ ] 4.18 GREEN: wire `BuildCarouselTemplateSlots` (~L2297-2304) and `BuildStoryTemplateSlots` (~L2241) reflowable slot sets per design.md (carousel: `title`→`subtitle`; story: `headline`→`subtitle`→`body`).
- [ ] 4.19 Run full template-path + flat-path regression suite — confirm every pre-existing test passes unchanged (blocking criterion).
- [ ] 4.20 Write ADR `docs/architecture/adr/0016-two-pass-measure-then-render.md` — latency posture (measured render-stage cost before/after), 3-render hard cap, non-convergence disposition, disjoint-from-`CAC-IMG-03` correction (D-D).

## WU-5: Copy-Length-Aware Typography Auto-Fit (Track B, sequenced after WU-4)
Spec: CLIF-CG-07. Design: file map (`CompositionDefaults.cs`), Risks table (auto-fit legibility floor).

- [ ] 5.1 RED: extend `Incoders.Hive.Tests/Application/Features/Social/ContentGeneration/Visual/CompositionDefaultsTests.cs` (or create if absent) — legitimate `SizeRatio` + unusually long copy reduces resolved size within stated limits, never below the stated floor. Satisfies CLIF-CG-07-a.
- [ ] 5.2 RED: same file — auto-fit never increases a resolved size for any copy/ratio combination (boundary assertion). Satisfies CLIF-CG-07-b.
- [ ] 5.3 RED: same file — a profile whose copy fits at its measured ratio today renders byte-identically after auto-fit (no-op regression, ties to CLIF-CG-08).
- [ ] 5.4 GREEN: edit `Incoders.Hive.Application/Features/Social/ContentGeneration/Visual/CompositionDefaults.cs` (`ResolveTypography`/`SizeFor`/`SizeForCaption`) — add floor-bounded, reduction-only auto-fit tuned against real reflowed geometry from WU-4.
- [ ] 5.5 Run `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~CompositionDefaults` — confirm green.

## WU-6 (conditional): Slot Geometry Tightening for Content Studio
Spec: CLIF-CS-01. Design: Open Questions — "Empty WU-6 is a valid outcome." Sequenced strictly after WU-4.

- [ ] 6.1 Measure residual slack: after WU-4 ships, capture `SlotGeometryEntry` for a short headline against its fixed `min-height` literal (e.g. 260px) and record whether padding/line-height slack remains.
- [ ] 6.2 IF slack remains — RED: extend `Incoders.Hive.Tests` geometry-capture tests (Infrastructure rendering test suite) — captured box height measurably smaller than pre-change `min-height`-driven box, still fully contains rendered glyph ink. Satisfies CLIF-CS-01-a.
- [ ] 6.3 IF slack remains — RED: assert Content Studio editing box tracks the tightened geometry, not the padded CSS box. Satisfies CLIF-CS-01-b.
- [ ] 6.4 IF slack remains — GREEN: edit `Incoders.Hive.Infrastructure/Rendering/HtmlChromiumContentRenderer.cs` (`SlotGeometryScript` ~L51-66) — tighten capture to the content box.
- [ ] 6.5 IF NO slack remains — record the finding (empty WU-6) in the change's verification notes; no production code changes; CLIF-CS-01 is satisfied by WU-4 alone.

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

- [ ] 9.1 Merge `openspec/changes/content-layout-and-image-fidelity/specs/{image-generation,content-generation,content-studio}/spec-delta.md` into the corresponding `openspec/specs/*/spec.md` files (already-corrected CAC-IMG-03 wording — confirm no drift from the corrected text during merge).
- [ ] 9.2 Confirm PRD §12 (Content Studio operator editing) amendment for alignment guides (new operator capability per proposal's honesty note); confirm §4 (Content Studio boxes/item 4) needs no amendment.
- [ ] 9.3 Update any affected `.github/project-ai/instructions/*.md` or `docs/` pages referencing the template-backed render pipeline, guardrail call sites, or Content Studio geometry capture, per this repo's Documentation Sync (MUST) rule.
- [ ] 9.4 Run `pwsh -File scripts/gates/Run-Gates.ps1` — confirm all MUST gates pass, including architecture gate (no provider/EF type crossing into Domain/Application) and Strict TDD compliance.
- [ ] 9.5 Record owner's accept/reject review for two real generations (materially different `SizeRatio`, materially different headline length) per proposal Success Criteria.
