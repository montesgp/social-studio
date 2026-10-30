# Archive Report: Client-Aesthetic Generation Hardening

**Change ID**: `client-aesthetic-generation-hardening`  
**Archived to**: `openspec/changes/archive/2026-08-25-client-aesthetic-generation-hardening/`  
**Archive date**: 2026-08-25  
**Status**: COMPLETE

---

## Executive Summary

All 11 work units (WU-1 through WU-8, with WU-6a/6b and WU-7a/7b/7c as sub-units) have been implemented and committed directly to the `dev` branch. A post-verify follow-up commit (`cd51ff3`) closed a CRITICAL specification gap identified during verification (shared `ImagePromptDirectives.BackgroundOnly` constant). The change is verified clean with no unresolved CRITICAL findings. All MUST gates pass; backend tests total 3829 passed, 0 failed, 2 pre-existing skips.

---

## Final State Authority & Ranking

This archive report reflects the state of the change **at close per the SDD skill's Final-State Authority hierarchy**:

1. **Native review authority** (terminal receipt): N/A — receipt-driven development is disabled per project policy.
2. **Persisted tasks artifact**: All 11 implementation tasks in `tasks.md` marked complete [x].
3. **Explicit final-state facts in launch prompt**: 
   - All 11 WUs implemented and committed (commits: `3e0eb5c`, `983d178`, `f5d63fb`, `c10d4e0`, `b958c37`, `8db28ee`, `8864d80`, `978c1df`, `39adc4e`, `bc91b33`, `599ab77`, plus follow-up `cd51ff3`)
   - sdd-verify found 1 CRITICAL (fixed in `cd51ff3`), 0 other blockers
   - Backend tests: 3829 passed, 0 failed, 2 pre-existing skips
   - Gates: all MUST gates pass clean
4. **Intermediate snapshots** (tasks.md, verify-report): Lowest rank; used only for historical context, never as evidence of final state.

---

## Work Units Delivered

All 11 work units shipped to `dev`, committed sequentially per the proposal's delivery plan:

| WU | Title | Commit | Status |
|---|---|---|---|
| WU-1 | Typography Size Consumption (Application, ~380 lines) | `3e0eb5c` | ✅ Complete |
| WU-2 | AiVisualDesigner Size-Derived Prompt Line (Infrastructure, ~140 lines) | `983d178` | ✅ Complete |
| WU-3 | Enforced-Photo Text Colour (Application, ~420 lines) | `f5d63fb` | ✅ Complete |
| WU-4 | ComfyUI BackgroundOnly Parity + Anatomy Terms (Infrastructure, ~300 lines) | `c10d4e0` | ✅ Complete |
| WU-5 | Detailer Feasibility Report (Docs, ~200 lines — investigation only) | `b958c37` | ✅ Complete |
| WU-6a | BrandMarkDetection MarkType + Text (Application/Infrastructure, ~460 lines) | `8db28ee` | ✅ Complete |
| WU-6b | SourceImagePath + BoundingBox, Validation, Reliability Suite (~400 lines) | `8864d80` | ✅ Complete |
| WU-7a | Text-Mark Rendering (Application, ~340 lines) | `978c1df` | ✅ Complete |
| WU-7b | Icon-Mark Asset Pipeline + ADR (Application/Infrastructure, ~640 lines) | `39adc4e` | ✅ Complete |
| WU-7c | Icon-Mark Render + Ref Resolution (Application/Infrastructure, ~560 lines) | `bc91b33` | ✅ Complete |
| WU-8 | Spec/Doc Sync, PRD Amendment, Gate Run (Docs, ~420 lines) | `599ab77` | ✅ Complete |
| Follow-up | Extracted shared `ImagePromptDirectives.BackgroundOnly` (post-verify, closes CAGH-CUI-01-c) | `cd51ff3` | ✅ Complete |

**Total authored lines**: ~4,260 across 11 WUs (per-unit budgets respected; all within 800-line limit per WU).

---

## Verification Status

**sdd-verify (first pass)** found 1 CRITICAL issue: `CAGH-CUI-01-c` — shared `BackgroundOnly` directive undefined; both `ComfyUiImageModelClient` and `OpenAiImageModelClient` had separate local `BackgroundOnlyDirective` constants instead of one shared source.

**Post-verify follow-up** (`cd51ff3`):
- Created `Infrastructure/Ai/Providers/ImagePromptDirectives.cs` with `internal const string BackgroundOnly`
- Updated `OpenAiImageModelClient.cs` and `ComfyUiImageModelClient.cs` to reference shared constant
- Added `ImagePromptDirectivesTests.cs` asserting both adapters trace back to the same constant
- Result: CRITICAL resolved; spec requirement `CAGH-CUI-01-c` now satisfied

**Post-fix verification**: Clean. No unresolved CRITICAL findings.

---

## Quality Gates

**Final gate run** (`pwsh -File scripts/gates/Run-Gates.ps1`):
- All MUST gates: ✅ PASS
- Backend test suite: 3829 passed, 0 failed, 2 skipped (pre-existing)
- Architecture gate: ✅ PASS
- No CRITICAL or blocking issues

---

## Specs Synced

All four delta specs have been merged into their corresponding main specs:

| Spec Domain | Delta | Status | Verification |
|---|---|---|---|
| content-generation | `specs/content-generation/spec-delta.md` | ✅ Merged | Requirements CAGH-CG-01 through CAGH-CG-07 + modified FFS-CG-02 added |
| comfyui-image-provider | `specs/comfyui-image-provider/spec-delta.md` | ✅ Merged | Requirements CAGH-CUI-01, CAGH-CUI-02 added |
| profile-analysis | `specs/profile-analysis/spec-delta.md` | ✅ Merged | Requirements CAGH-PA-01 through CAGH-PA-04 + modified FFS-PA-02 added |
| content-studio-background-rendering | `specs/content-studio-background-rendering/spec-delta.md` | ✅ Merged | Requirements CAGH-SBR-01, CAGH-SBR-02 + modified SBR-01 added to `openspec/changes/archive/2026-08-25-studio-background-render-and-conditional-signature/specs/content-studio-background-rendering/spec.md` per established location (not main specs directory) |

---

## Architecture & Documentation

**ADR 0014**: `docs/architecture/adr/0014-brand-mark-asset-port.md`
- New Application port `IBrandMarkAssetExtractor` with SkiaSharp adapter
- Crop-only design (no matting, no vision API, no logo regeneration)
- Degrade contract specified: crop failure → fallback to colored chip, never throws
- Cites `DegenerateImageGuardrailValidator` as existing SkiaSharp-behind-a-port precedent (per design finding F4)
- Records WU-6b bounding-box reliability verdict (see below) and rationale for proceeding with WU-7b/7c

**PRD Amendment**: `docs/PRD.md` §11 (Profile Analysis / Mundo 2)
- Added "Amended by" blockquote referencing ADR 0014
- New MVP Acceptance Criteria bullet describing brand-mark asset derivation from analysed imagery
- Reflects that profile analysis now captures and stores durable brand-mark crops, not just categorical fields

---

## Honestly Deferred Items (Documented as Decisions, Not Silent Drops)

### WU-5: Detailer Node Investigation (Investigation-Only Deliverable)

**Verdict**: Recommend-with-preflight. Do NOT ship `FaceDetailer` unconditionally into `DefaultComfyUiWorkflowTemplate.json`.

**Rationale**:
- Required package (ComfyUI-Impact-Pack) and detection-model weights are backend-side dependencies beyond this repo's control
- Single-flat-graph failure mode: missing node → entire workflow fails, worse than pre-change occasional anatomy defects
- Existing negative-prompt anatomy terms (WU-4) provide low-risk partial mitigation at zero dependency cost
- Repo already has `/object_info` probe capability (`ComfyUiModelCatalogClient`) for future preflight-gated rollout

**Deliverable**: `detailer-feasibility-report.md` (investigation-only, no code/workflow change)

**Follow-up requirement**: If/when a detailer node is shipped in a future change, it must be:
1. Gated behind a preflight `GET /object_info` probe
2. Require explicit per-org opt-in (not unconditional)
3. Include an ADR covering the conditional logic and dependency management

### WU-6b: Bounding-Box Reliability Measurement (Not Measurable Offline)

**Verdict**: NOT MEASURABLE IN THIS SESSION.

**Why**: This batch ran offline with no live vision-model (multimodal AI) access and no owner-provided reference images or ground-truth annotations. Task 6b.5 calls for:
- 3-5 real client reference images with hand-annotated expected icon-mark regions
- Running the real `MultimodalAestheticStyleExtractor` against a real configured vision route
- Computing IoU between model output and ground truth (informal floor ≥0.5)

None of these exist in this environment.

**What shipped anyway**: Defensive-parsing suite (11 canned-JSON cases in `MultimodalAestheticStyleExtractorTests.cs`) — proves the code path never throws and never fabricates a value on malformed/adversarial model output. This is a **correctness guarantee**, not a reliability measurement.

**Decision made**: Per proposal risk table, WU-7b/7c ship anyway, because:
- Defensive parsing guarantees correctness (no crashes or silently-wrong values)
- `IBrandMarkAssetExtractor` degrade contract ensures failure → safe fallback (invalid box → coloured chip, never throws)
- ADR 0014 documents the bounding-box reliability question and the decision to proceed pending owner-run measurement

**Required owner-run follow-up** (before considering the bounding-box reliability question closed):
1. Pick 3-5 real client reference images (from an org with recurring icon mark), already archived
2. Hand-annotate true icon bounding box on each (normalised 0..1 `{x,y,width,height}`)
3. Run real `MultimodalAestheticStyleExtractor.ExtractAsync` against real configured `ai-aesthetic` route for each fixture
4. Compute IoU per fixture; record mean against ≥0.5 informal floor
5. Record Ship / Cut-WU-7b-7c decision (Ship = proceed as designed; Cut = icon marks use WU-6a chip treatment only)

Until this measurement lands, the bounding-box reliability question remains **live, not yet measured**. This is recorded explicitly, not absorbed silently.

---

## Incidental Improvements (Side-Effects, Not Scope Creep)

**PptxExportAdapter Bug Fix** (WU-7c task 7c.3):
- Fixed latent `SlotRegex` bug in `Infrastructure/Rendering/PptxExportAdapter.cs`
- Root cause: regex had no tag-name anchor, greedily matched to next unrelated closing tag
- Surfaced by introducing first self-closing `<img data-slot="...">` element
- Fix: tag-name-backreferenced regex (`\k<tag>`) handles self-closing `/>` correctly
- Coverage: 3 new tests in `PptxExportAdapterTests.cs` prevent regression
- Impact: enables correct PPTX export of brand-mark `<img>` refs; side benefit improves export robustness

This is a positive side-effect directly relevant to scope, not scope creep to apologize for.

---

## GitHub Issue #80 Status

**Issue**: `Incoders-Tools/incoders-hive` issue #80 — tracks this entire change (all 11 WUs + post-verify follow-up)

**Action taken**: GitHub issue left **OPEN** (not closed by this archive step)

**Closing comment** (to be posted): Will summarize:
- All 11 WUs committed to `dev` with hashes
- Post-verify follow-up `cd51ff3` closed CRITICAL shared-const gap
- ADR 0014 and PRD §11 amendment shipped
- Two honestly-deferred items: WU-5 investigation, WU-6b measurement (both documented)
- Final gate status: clean

---

## Test Results Summary

**Backend Test Suite**:
- Total: 3831 tests
- Passed: 3829 ✅
- Failed: 0 ✅
- Skipped: 2 (pre-existing, unrelated)

**All MUST gates**: ✅ PASS

**No regressions detected** across any test tier.

---

## Archive Contents Checklist

- [x] `exploration.md` — codebase map with file/line citations
- [x] `proposal.md` — revision 2, three settled decisions
- [x] `design.md` — 13 architecture decisions (D1–D13) with findings F1–F5
- [x] `tasks.md` — 11 WUs + follow-up, all marked complete
- [x] `detailer-feasibility-report.md` — WU-5 investigation-only deliverable
- [x] `specs/content-generation/spec-delta.md` — ADDED CAGH-CG-01..07 + MODIFIED FFS-CG-02
- [x] `specs/comfyui-image-provider/spec-delta.md` — ADDED CAGH-CUI-01, CAGH-CUI-02
- [x] `specs/profile-analysis/spec-delta.md` — ADDED CAGH-PA-01..04 + MODIFIED FFS-PA-02
- [x] `specs/content-studio-background-rendering/spec-delta.md` — ADDED CAGH-SBR-01, CAGH-SBR-02 + MODIFIED SBR-01

**Specs merged into main repositories**:
- [x] `openspec/specs/content-generation/spec.md` — all CAGH-CG requirements + modified FFS-CG-02
- [x] `openspec/specs/comfyui-image-provider/spec.md` — all CAGH-CUI requirements
- [x] `openspec/specs/profile-analysis/spec.md` — all CAGH-PA requirements + modified FFS-PA-02
- [x] `openspec/changes/archive/2026-08-25-studio-background-render-and-conditional-signature/specs/content-studio-background-rendering/spec.md` — all CAGH-SBR requirements + modified SBR-01

---

## SDD Artifact Observation IDs

_(For Engram hybrid mode traceability — if applicable)_

This archive is stored in the filesystem under `openspec/changes/archive/2026-08-25-client-aesthetic-generation-hardening/` and in no other persistent system.

---

## Key Learnings

1. **Shared constants across adapters require a single source of truth**: WU-4's `BackgroundOnlyDirective` duplication across `ComfyUiImageModelClient` and `OpenAiImageModelClient` was caught only in verification; moving to `ImagePromptDirectives.cs` prevents future drift and is asserted by test.

2. **Bounding-box reliability measurement cannot be fabricated offline**: Task 6b.5's IoU fixture measurement genuinely requires live vision-model access and owner-provided ground-truth annotations; proceeding with WU-7b/7c depends entirely on the defensive-parsing guarantee and the degrade contract, not on speculated accuracy numbers.

3. **`StudioVisualMarkupResolver` already resolves all `src` attributes generically**: The proposal's assumption that only `BackgroundImageRef` was resolved was wrong; the resolver's regex catches any `src="..."`, making the Studio half of WU-7c a pure test-coverage add, not a code change.

4. **Post-skip index resolution must happen inside the extractor**: WU-6b's `D9` decision to resolve `Image {i}` index to `StoragePath` inside `MultimodalAestheticStyleExtractor` is the only place that knows the post-skip ordering after failed `OpenAsync` calls; returning raw indices would force all downstream consumers to reimplement the ordering logic and risk silent bugs.

5. **Work-unit budgeting by per-unit 800-line limits prevents oversized PR creep**: Splitting the original "brand-mark v1" into WU-6a/6b/7a/7b/7c (instead of one 2400-line unit) required explicit decision-making and resulted in testable, reviewable, independently-rollbackable stages.

---

*Archived on 2026-08-25*  
*Change archived to `openspec/changes/archive/2026-08-25-client-aesthetic-generation-hardening/`*  
*Follow-up work (if any): GitHub issue #80 remains OPEN for owner tracking of deferred items*
