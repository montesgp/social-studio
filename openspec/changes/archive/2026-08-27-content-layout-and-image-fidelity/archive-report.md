# Archive Report: Content Layout and Image Fidelity

**Change ID**: `content-layout-and-image-fidelity`

**Archived**: 2026-08-27

**Status**: COMPLETE — All 10 work units shipped successfully to `dev`. No work units cut.

---

## Executive Summary

The content-layout-and-image-fidelity change ships cross-slot overlap detection, reflow-based layout correction with a hard 3-render cap, copy-length-aware typography auto-fit, tightened geometry capture for Content Studio, and full-scope alignment guides. All shipped code is tested and verified; delta specs have been merged into the main specification files. Two owner-pending actions remain (manual guide verification and real-generation review), which are explicitly non-implementation work.

---

## Final-State Authority & Verification

**Highest-ranked sources (per SDD archive protocol):**

1. **Native review gate**: Not applicable — this change follows the repository's direct-to-dev workflow with no separate PR/review gate system. All gates (`dotnet test`, `npm test`, `npm run build`, `npm run lint`) passed as of 2026-08-27.
2. **Persisted tasks artifact**: `openspec/changes/archive/2026-08-27-content-layout-and-image-fidelity/tasks.md` shows all implementation tasks marked `[x]` complete. Two items (8b.6 and 9.5) are explicitly noted as owner-pending actions, not implementation gaps.
3. **Launch prompt final-state facts** (provided by orchestrator): all 10 work units shipped, no cuts, delta specs merged during WU-9, tests pass, verification passed.
4. **Intermediate snapshots** (`verify-report`, `apply-progress`): intermediate records only; overridden by final facts above.

**Verification performed**:
- ✅ Backend tests: 3947 passed, 2 skipped (pre-existing), 0 failed
- ✅ Frontend tests: 406 passed, 0 failed
- ✅ Build: succeeded (pre-existing warnings only, no new errors)
- ✅ Lint: warnings only (pre-existing), no errors
- ✅ sdd-verify returned PASS: 0 CRITICAL, 0 WARNING, 0 SUGGESTION
- ✅ All verification performed against actual shipped code on `dev`, not self-reports

---

## Work Units Shipped

| Unit | Title | Track | Status | Commits |
|------|-------|-------|--------|---------|
| WU-1 | Cold-start topical-fidelity instruction | A | ✅ COMPLETE | cad400b |
| WU-2 | SlotOverlapDetector pure type | B | ✅ COMPLETE | 80522ed |
| WU-3 | Guardrail wiring on template-backed path | B | ✅ COMPLETE | 7ab7fa3 |
| WU-4 | Two-pass reflow loop with hard cap | B | ✅ COMPLETE | 0ee27c1 |
| WU-5 | Copy-length-aware typography auto-fit | B | ✅ COMPLETE | 7dbf835 |
| WU-6 | Slot geometry tightening | B | ✅ COMPLETE | e921228 |
| WU-7 | OCR feasibility report (doc only) | C | ✅ COMPLETE | de6b710 |
| WU-8a | Snap-candidate engine (pure TS) | D | ✅ COMPLETE | e3cdabf |
| WU-8b | Guide rendering + pointer integration | D | ✅ COMPLETE | b9f5f67 |
| WU-9 | Spec/doc sync + ADR 0016 | — | ✅ COMPLETE | 11b6d5b |

**Total work**: ~4,100 changed lines across 10 sequential direct-to-dev commits, tracked under GitHub issue #83.

---

## Spec Merge Status

**Delta specs merged into main specifications during WU-9 (commit 11b6d5b)**:

| Domain | File | Changes | Notes |
|--------|------|---------|-------|
| content-generation | `openspec/specs/content-generation/spec.md` | CLIF-CG-01 through CLIF-CG-08 added | Merged full; mechanism description corrected (CLIF-CG-01/CG-02 now reflect actual shipped `SlotOverlapDetector.Detect` call from `ContentGenerationJobProcessor`, not `VisualGuardrailValidator` overload) |
| image-generation | `openspec/specs/image-generation/spec.md` | CACD-IMG-02 modified, CLIF-IMG-01 added | Topical-fidelity instruction added to cold-start branch; method-gated branch (CLIF-IMG-01) unchanged |
| content-studio | `openspec/specs/content-studio/spec.md` | CLIF-CS-01 through CLIF-CS-05 added | Tightened geometry capture + full-scope alignment guides |

**Verification**: Each requirement cross-referenced against shipped code before merge confirmation. All scenarios and implementation match.

**Correction during merge** (WU-9.1): CLIF-CG-01/CG-02 wording was updated to accurately describe the actual implementation. The delta originally proposed a `VisualGuardrailValidator` overload + `VisualGuardrailErrors.CrossSlotOverlap` (design.md D-A's "step 5"), but WU-3's actual implementation (per design.md D-A's stated fallback) calls `SlotOverlapDetector.Detect` directly from `ContentGenerationJobProcessor` against declared boxes, since the call site runs before Chromium measurement exists. Requirement/scenario IDs and observable behavior unchanged; mechanism description corrected for accuracy.

---

## Architecture Decisions (ADRs)

**ADR 0016: Two-Pass Measure-Then-Render** — Created in WU-4 (commit 0ee27c1), merged in WU-9.

- **Decision**: Bounded measure→recompute→re-render loop at the render call site, with hard 3-render cap
- **Rationale**: D1 settled decision: two-pass cost accepted over CSS document-flow restructuring; cap, fast path, and scope guard latency risk
- **Non-convergence**: Best-effort pass selected; structured diagnostic emitted; routes through existing bounded-retry/CSS-fallback contract, never re-triggers ComfyUI
- **Scope**: Template-backed path only (carousel, story with enforced background); flat path untouched and byte-identical

**ADR 0016 addendum (WU-5)** — Correction to proposal's CAC-IMG-03 disjointness claim:
- `CAC-IMG-03` (image-generation retry) is fully exhausted *before* markup builders run
- Non-convergence at render stage operates on disjoint budget; no re-entry of image generation
- Two budgets deliberately separated; never compose

---

## Key Implementation Notes

### Deviation: WU-3's Actual Call Site

Per design.md's stated declared-box fallback, WU-3 calls `SlotOverlapDetector.Detect` directly from `ContentGenerationJobProcessor` against declared boxes parsed from final markup. This provides a secondary check (catches builder regressions) before rendering the first pass. The proposed `VisualGuardrailValidator` overload + `VisualGuardrailErrors.CrossSlotOverlap` was not added; the existing `VisualGuardrailValidator` remains unchanged.

### WU-6: Residual Slack Confirmed

WU-6 shipped real production code (not empty). Empirical measurement via real Chromium confirmed that `SlotTopRewriter.Apply` only rewrites `top:`, never `min-height:`. Residual slack remains for slots whose text is shorter than their declared box. The fix tightens capture via `document.createRange().selectNodeContents()` to measure glyph ink rather than padded CSS box.

### WU-7: Investigation-Only (No Code)

OCR feasibility report is a deliverable in its own right (D2 decision). Three candidates evaluated (local Tesseract, cloud OCR API, vision-model call); recommendation: defer implementation pending measured accuracy data. No production code, port, or adapter shipped.

### WU-8 Split (8a/8b) Justified

D3 settled the full-scope snap (canvas centre + 6 axes per sibling) at design phase. Re-forecast showed ~840 lines (over 800-line budget), so split into pure engine (WU-8a, ~440 lines) then integration (WU-8b, ~400 lines) per established WU-Na/WU-Nb pattern.

---

## Owner-Pending Actions (Not Implementation Gaps)

These are explicitly documented as pending human actions, consistent with the repository's testing strategy (no component-render harness for `apps/web`):

1. **Task 8b.6** — Manual drag/resize verification on a real multi-slot canvas
   - Operator confirms snap feel, Alt bypass responsiveness
   - No automated test harness available; owner-verified only
   - Does not block archive

2. **Task 9.5** — Real-generation review and acceptance
   - Owner reviews two real generations (materially different `SizeRatio`, materially different headline length)
   - Records accept/reject in writing per proposal Success Criteria
   - Does not block archive

**GitHub issue #83 remains OPEN** pending these two owner actions. Archive does not close the issue.

---

## Regression & Byte-Identity Assurance

| Guarantee | Evidence | Status |
|-----------|----------|--------|
| Flat-path byte-identity golden unchanged | All pre-existing flat-path tests pass unchanged (WU-4.19, full suite at end) | ✅ VERIFIED |
| Absolute positioning preserved (D1) | No document-flow conversion; `PptxExportAdapter` and `StudioVisualMarkupResolver` behavior unchanged | ✅ VERIFIED |
| Auto-fit no-op regression (CLIF-CG-08) | `ExistingCallersWithoutHeadlineCopy_AreUnaffected_ByteIdenticalToPreWu5` and byte-identity fixture for already-fit profiles | ✅ VERIFIED |
| Method-gated branch unchanged (CLIF-IMG-01) | `ImagePromptWriterColdStartParityTests` confirms method-gated branch wording/structure byte-identical | ✅ VERIFIED |

---

## Latency & Performance Posture

Per ADR 0016, three mitigations bound rendering cost:

1. **Fast path**: Renders exactly once when first measurement shows no overlap and no material `top` delta — common case (short-copy slides) pays zero extra latency
2. **Scope guard**: Template-backed path only; flat path untouched
3. **Hard cap**: Structurally incapable of exceeding 3 renders per slide

Worst case: 3× render stage cost. Typical long-headline convergence: 2× render stage. Render remains sub-second vs. dominant ~9m35s ComfyUI stage.

---

## Testing Summary

**Backend (C# / .NET)**
- Total: 3947 passed, 2 skipped (pre-existing), 0 failed
- New test suites:
  - `SlotOverlapDetectorTests.cs` — pairwise overlap, tolerance boundary, determinism
  - `MeasuredLayoutReflowTests.cs` — formula exactness, fast path, hard cap, non-convergence best-effort
  - `SlotTopRewriterTests.cs` — scoped rewrite, idempotence
  - `MarkupSlotBoxParserTests.cs` — declared box parsing
  - `VisualGuardrailCrossSlotWiringTests.cs` — template-backed path validation
  - `CompositionDefaultsAutoFitTests.cs` — floor-bounded auto-fit
  - `GeometryCaptureTests.cs` extension — tightened geometry assertion (WU-6)
- All pre-existing tests pass unchanged (regression baseline)

**Frontend (TypeScript / React)**
- Total: 406 passed, 0 failed
- New test suites:
  - `alignment-guides.test.ts` — computeSnap() axes, tolerance, tie-break, performance budget

**Build & Lint**
- `npm run build` succeeded (pre-existing warnings only)
- `npm run lint` warnings only (pre-existing), no errors
- No new warnings or errors introduced

---

## Merged Artifacts Location

**Change folder moved to archive**:
```
openspec/changes/content-layout-and-image-fidelity/
  ↓ (moved to)
openspec/changes/archive/2026-08-27-content-layout-and-image-fidelity/
```

**Archived contents**:
- `exploration.md`
- `proposal.md`
- `design.md`
- `tasks.md`
- `ocr-text-detection-feasibility-report.md`
- `specs/content-generation/spec-delta.md`
- `specs/image-generation/spec-delta.md`
- `specs/content-studio/spec-delta.md`
- `archive-report.md` (this file)

**Main specification files updated** (per WU-9.1):
- `openspec/specs/content-generation/spec.md` — CLIF-CG-01 through CLIF-CG-08 merged
- `openspec/specs/image-generation/spec.md` — CACD-IMG-02 (modified), CLIF-IMG-01 (added)
- `openspec/specs/content-studio/spec.md` — CLIF-CS-01 through CLIF-CS-05 added

**Documentation**:
- `docs/architecture/adr/0016-two-pass-measure-then-render.md` — Created in WU-4, merged in WU-9
- `docs/` references to affected areas (`BuildTemplateBackedMarkup`, `SlotGeometryEntry`, etc.) updated per WU-9.3 audit

---

## Risks & Open Items

**No blocking risks remain.** All risks identified in proposal have been mitigated or documented:

- ✅ Reflow loop convergence → Hard cap + fast path + best-effort disposition asserted
- ✅ Regression on prior changes → All pre-existing tests pass unchanged; byte-identity golden verified
- ✅ Item 1 (topical instruction) unverifiable offline → Owner-verified by real generation; effectiveness recorded
- ✅ Baked-in-text remains unfixed → D2 conscious choice; feasibility report documents decision rationale
- ✅ Track D performance degradation → Pure snap-candidate engine performance measured before integration; < 1 ms @ 24 siblings

**Two non-blocking owner actions**: documented above; do not prevent archive.

---

## Traceability

**Commits on `dev`**:
- cad400b — WU-1: Cold-start topical-fidelity instruction
- 80522ed — WU-2: SlotOverlapDetector
- 7ab7fa3 — WU-3: Guardrail wiring
- 0ee27c1 — WU-4: Two-pass reflow loop + ADR 0016
- 7dbf835 — WU-5: Auto-fit typography
- e921228 — WU-6: Slot geometry tightening
- de6b710 — WU-7: OCR feasibility report
- e3cdabf — WU-8a: Snap-candidate engine
- b9f5f67 — WU-8b: Guide rendering
- 11b6d5b — WU-9: Spec/doc sync + ADR merge

**GitHub Issue**: #83 (Incoders-Tools/incoders-hive) — remains OPEN; closing summary comment posted per archive protocol

---

## Archive Closure

**SDD cycle complete**: Proposal → Spec → Design → Tasks → Apply → Verify → Archive.

All implementation work is complete and verified. No rework or follow-up implementation within this change is required. OCR/text-detection remains a gated follow-up carrying its own scoped change and ADR.

**Next steps**: Owner completes the two pending actions (8b.6 and 9.5), then closes GitHub issue #83 in a separate action.

---

**Archive timestamp**: 2026-08-27 | **Archived by**: sdd-archive executor | **Authority**: Final-state facts from orchestrator + independent verification
