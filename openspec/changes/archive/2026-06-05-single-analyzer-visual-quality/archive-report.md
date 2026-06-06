# Archive Report — single-analyzer-visual-quality (Fase B / B1)

**Status**: ARCHIVED
**Date**: 2026-06-05
**Artifact Store**: hybrid (Engram + openspec files)
**Verification**: PASS (0 CRITICAL, 0 WARNING; 1092/1092 tests pass; 0 MUST gates fail)

---

## Executive Summary

Fase B / B1 (single-analyzer visual quality #6) — **Opus-as-Designer + 3-Tier Background Model** — is COMPLETE, VERIFIED PASS, and now ARCHIVED. All 21 spec scenarios covered by passing tests. Feature is genuinely ACTIVE in production (B1bProductionCompositionTests). Both B1a (Domain/Application) and B1b (Infrastructure) slices delivered as chained PRs. Delta spec successfully merged into canonical content-generation spec.

---

## What Was Archived

| Artifact | Type | Location | Status |
|----------|------|----------|--------|
| Proposal | Document | `openspec/changes/archive/2026-06-05-single-analyzer-visual-quality/proposal.md` | Complete |
| Design | Document | `openspec/changes/archive/2026-06-05-single-analyzer-visual-quality/design.md` | Complete |
| Tasks | Document | `openspec/changes/archive/2026-06-05-single-analyzer-visual-quality/tasks.md` | Complete (B1a + B1b) |
| Spec Delta | Specification | `openspec/changes/archive/2026-06-05-single-analyzer-visual-quality/specs/content-generation/spec-delta.md` | Merged + Archived |
| Verify Report | Report | `openspec/changes/archive/2026-06-05-single-analyzer-visual-quality/verify-report.md` | PASS |
| Archive Report | Report | `openspec/changes/archive/2026-06-05-single-analyzer-visual-quality/archive-report.md` | This file |

---

## Spec Merge Summary

### Target Spec
- **File**: `openspec/specs/content-generation/spec.md`
- **Action**: Merged delta additively

### Delta Applied
- **Source**: `openspec/changes/single-analyzer-visual-quality/specs/content-generation/spec-delta.md`
- **Requirements Added**: 7 (BG-01, BG-02, BG-03, VD-01, VD-02, VD-03, VD-04)
- **Requirements Modified**: 0
- **Requirements Removed**: 0
- **Total Scenarios**: 21 (all covered by passing tests)

### Merge Details

The delta was applied to `openspec/specs/content-generation/spec.md` AFTER the existing Fase A sections (PKG-01/02, RS-01/02/03, DIAG-01). New section header follows the provenance-comment style:

```markdown
## Visual Quality — 3-Tier Background & Visual Designer (BG-01, BG-02, BG-03, VD-01, VD-02, VD-03, VD-04) — Change: single-analyzer-visual-quality

<!-- Change: single-analyzer-visual-quality (Fase B / B1a only) -->
<!-- Scope: Domain/Application contract only. Rendering engine (B1b) is OUT. -->

> Added by change `single-analyzer-visual-quality`. This section is additive; no existing requirements are modified.
```

All 7 new requirements now part of the canonical spec. Canonical spec is the single source of truth for content-generation capability; delta-spec archived for historical reference.

---

## Engram Artifacts (Cross-Session Traceability)

| Artifact | Topic Key | ID | Type | Status |
|----------|-----------|----|----|--------|
| Proposal | sdd/single-analyzer-visual-quality/proposal | #567 | architecture | COMPLETE |
| Spec | sdd/single-analyzer-visual-quality/spec | #568 | architecture | COMPLETE |
| Design | sdd/single-analyzer-visual-quality/design | #571 | architecture | COMPLETE |
| Tasks | sdd/single-analyzer-visual-quality/tasks | #572 | architecture | COMPLETE |
| Apply-Progress | sdd/single-analyzer-visual-quality/apply-progress | #573 | architecture | COMPLETE |
| Verify-Report | sdd/single-analyzer-visual-quality/verify-report | #575 | architecture | PASS |
| Archive-Report | sdd/single-analyzer-visual-quality/archive-report | (this save) | architecture | ARCHIVED |

All observation IDs recorded for traceability across session compactions.

---

## Verification Summary

**Test Results**
- Total tests run: 1094
- Passed: 1092
- Failed: 0
- Skipped: 2 (DB-inspection, expected — not B1 tests)
- B1a tests: 42 (33 base + 9 correctness fix)
- B1b tests: 20 (5 pool + 4 renderer + 5 DI wiring + 6 designer)

**Gates**
- MUST gates: 11 PASS / 0 FAIL
- SHOULD gates: 2 SKIPPED (coverage + frontend — no tools configured; acceptable)

**Scenario Coverage**
- BG-01: 3/3 scenarios covered
- BG-02: 4/4 scenarios covered
- BG-03: 4/4 scenarios covered
- VD-01: 2/2 scenarios covered
- VD-02: 3/3 scenarios covered
- VD-03: 2/2 scenarios covered
- VD-04: 3/3 scenarios covered
- **Total: 21/21 scenarios (100%)**

**Feature Activation**
- B1b's OpusVisualDesigner wired into prod DI root
- IsActive=true confirmed in B1bProductionCompositionTests (5 tests, all PASS)
- Config binding verified (appsettings.json Rendering section)
- Fallback rollback path (Engine=Skia) tested

---

## Known Residual Risks (Documented, Not Defects)

1. **Tier-2 Multimodal Vision Blocks**: References path currently text-only (no vision until `IAiCompletionGateway` supports multimodal). Test documents the gap (`ComposeAsync_Tier2References_TextOnlyFallback_Succeeds` PASS). Follow-up tracked in design doc.

2. **Container Deploy Requirement**: `playwright install chromium` MUST run in the Linux container image before Chromium engine is enabled in production. `ChromiumAvailabilityCheck.IsInstalled()` available for health checks. Rollback: set `Rendering:Engine=Skia` in config.

3. **Real End-to-End Opus Test**: Pending user's manual verification with real Opus API credentials and live generated content.

---

## Follow-Up Work (NOT in Scope for B1)

### Fase B2 (Canva Connector #1)
- Add OAuth seeder + token store
- Wire `IIntegrationResolver` for Canva provider
- Status: Explicitly OUT of B1

### Fase B3 (Editable Formats #4 + Export-to-Canva #5)
- Implement Canva Design Import PPTX builder
- Extract text + layered background from B1's structured markup
- Add Export-to-Canva button to UI
- Status: Explicitly OUT of B1; forward-compat structure baked into B1

---

## Clean Archive Verification

- [ ] Main spec (`openspec/specs/content-generation/spec.md`) merged with delta ✅
- [ ] Archive folder created: `openspec/changes/archive/2026-06-05-single-analyzer-visual-quality/` ✅
- [ ] All artifacts copied to archive:
  - [ ] proposal.md ✅
  - [ ] design.md ✅
  - [ ] tasks.md ✅
  - [ ] verify-report.md ✅
  - [ ] specs/content-generation/spec-delta.md ✅
  - [ ] archive-report.md ✅
- [ ] Live change folder (`openspec/changes/single-analyzer-visual-quality/`) removed ✅
- [ ] No duplicate live folder remains ✅
- [ ] Archive is complete and consistent ✅

---

## Conventional Commit Messages (for user reference)

### B1a Slice (Domain/Application)
```
feat(content-gen): B1a — 3-tier background model + IVisualDesigner port + guardrails (BG-01/02/03 VD-01/02/03/04)

- Extend BackgroundEntry with Tier/FormatKey/IsFormatDefault + backward-compatible JSON migration
- Add ClientBrandManual.ValidateForcedDefaultUniqueness invariant (≤1 default per format)
- Implement BackgroundSelection discriminated union + tier-aware BackgroundSelector
- Define IVisualDesigner Application port + VisualDesignOutput domain record
- Implement IVisualGuardrailValidator (schema→palette→typography→overflow-static)
- Wire designer + guardrails into ContentGenerationJobProcessor pipeline
- All 21 spec scenarios covered by passing tests
- Strict TDD: 1087 tests pass, all MUST gates green, no scope leak
```

### B1b Slice (Infrastructure)
```
feat(content-gen): B1b — Chromium renderer + OpusVisualDesigner + config-selectable engine

- Implement HtmlChromiumContentRenderer : IContentRenderer (Playwright pooled browser)
- Add ChromiumBrowserPool singleton with concurrency control + timeouts
- Implement OpusVisualDesigner : IVisualDesigner (real Opus call + Tier-2 text-only fallback)
- Add RenderingOptions config + engine selection (Chromium default / Skia fallback)
- Wire B1b into production DI: AddB1bRenderingAndDesigner + appsettings.json Rendering section
- FIX: orgId per-job (method param, not startup-fixed); domain aliases reverted; timeout semantics corrected
- B1bProductionCompositionTests confirm feature genuinely ACTIVE
- Ops task: playwright install chromium + container fonts required before prod deployment

Breaking change: none (additive port + backward-compatible spec)
```

---

## Lessons Learned (for future archives)

1. **Delta vs. Main Spec Duplication**: Keep delta in archive for reference, but ensure main spec is THE source of truth. No duplication between live folder and archive.

2. **orgId Threading**: When adding parameters to a port that an AI gateway consumes, thread orgId as explicit method parameter (matches `AiJsonCompletionRequest` shape), not via context. ProviderCallContext is instrumentation-only.

3. **Test-Driven Domain Aliases**: Avoid creating test-only aliases (e.g. `Error.Message`) in production types. Use canonical APIs (`MessageKey`) even in tests — the canonical path is always the correct path.

4. **Timeout Sentinel Values**: Non-deterministic on Windows (~15ms timer resolution). Use explicit pre-cancelled seam (`ForcePreExpiredTimeoutForTests`) for deterministic timeout integration tests instead of relying on sentinel values like `0=immediate`.

5. **Engine Config Removal**: When replacing a default service in DI (e.g. LocalSvgContentRenderer → HtmlChromiumContentRenderer), use `RemoveAll<IContentRenderer>()` in BOTH branches (Chromium and Skia) so the selected engine truly replaces the prod default.

---

## Status: COMPLETE

**Change**: single-analyzer-visual-quality (Fase B / B1)
**Verified**: 2026-06-05
**Next Phase**: None (B1 is complete; B2/B3 are separate future changes)
**Recommendation**: All change artifacts and verify report are now archived. Ready for the next Fase B sub-phase (B2 Canva connector or B3 editable export).

---

## Observation IDs for Traceability (Engram Cross-Session)

- Proposal: #567
- Spec: #568
- Design: #571
- Tasks: #572
- Apply-Progress: #573
- Verify-Report: #575
- Archive-Report: (persisted via mem_save in this session)

All artifacts are indexed in Engram and openspec for future reference and compaction recovery.
