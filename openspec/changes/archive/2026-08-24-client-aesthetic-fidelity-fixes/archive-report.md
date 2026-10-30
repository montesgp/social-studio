# Archive Report: client-aesthetic-fidelity-fixes

**Change ID**: `client-aesthetic-fidelity-fixes`
**Archived**: 2026-08-24
**Status**: COMPLETE

---

## Executive Summary

ClientAesthetic carousel fidelity fixes shipped complete: faithful degradation for failed slides, 3-role typography extraction, per-slide transparency treatment, and operator-visible degrade badges. All 8 work units implemented and tested. Backend suite: 3721 passed; Frontend suite: 391 passed. Specs merged into main openspec; change folder archived.

---

## Final State Authority Hierarchy

This archive report reflects the state of the change **at close**, based on (highest authority first):

1. **Explicit final-state facts in launch prompt** (post-verify-report):
   - All 8 work units (WU-0 through WU-4) implemented and committed to `dev`
   - sdd-verify returned PASS WITH WARNINGS (1 WARNING: TypographyNormalizer not role-aware for caption, NOT a spec-delta blocker, filed as separate issue #79)
   - Test counts: backend 3721 passed / 0 failed; frontend 391 passed / 0 failed
   - WARNING was NOT fixed inline; determined to need separate design work (GitHub issue #79, explicitly out of scope)
   - Reprocess-reference-job criterion deferred (requires ~30+ min against local dev DB/ComfyUI)
   - comfyui-generation-time-report.md final: no code fix recommended

2. **Persisted tasks artifact** (`tasks.md`):
   - All 8 tasks marked complete (checkboxes all checked)
   - WU-0 through WU-4 landed in sequence per delivery plan

3. **Persisted verify-report** (referenced, not reproduced here):
   - Result: PASS WITH WARNINGS
   - 0 CRITICAL issues
   - 1 WARNING (TypographyNormalizer caption-role gap, not spec-delta blocker)
   - 1 SUGGESTION (log the gap, done)

---

## What Shipped

### Backend (.NET) Changes

**Faithful Degrade Path (WU-1a)**
- New type: `ClientAestheticDegradeDecision` with `IsEligible()`, `TriggerA()`, `TriggerB()` predicates
- Trigger A: Photo-slide image-generation failure → deterministic flat treatment
- Trigger B: AI-designer returns null → deterministic flat treatment (ClientAesthetic only)
- Non-ClientAesthetic jobs retain legacy `rgba(0,0,0,0.52)` scrim card

**Degrade Visibility (WU-1b)**
- New optional field: `CompositionSpecEnvelope.DegradeReason` (tokens: `photo-image-unavailable`, `designer-unusable`)
- Serialization: `[property: JsonIgnore(Condition = WhenWritingNull)]` ensures non-degraded envelopes are byte-identical to pre-change

**Typography Extraction and Render (WU-2a/2b)**
- Multimodal extractor extended: heading, body, AND caption/overline extraction
- Three roles persist through `AestheticExtraction` → `AestheticProfilePersistence` → `ClientAestheticProfile` → markup
- New init-defaulted members on `CompositionTypography`: `CaptionFamily`, `CaptionSize`, `CaptionWeight`, `CaptionItalic`, `RequestedCaptionFamily`
- `ResolveTypography` 4-arg overload resolves `Caption ?? Overline ?? Accent` fallback
- `AiVisualDesigner.AllowedFonts` now sources from client-derived classes (resolved through `FontFamilyResolution`), appended to brand kit (client-first ordering)

**Per-Slide Transparency (WU-3a/3b)**
- New type: `SlideBackgroundTransparency(bool Blended, string? Descriptor)`
- Extracted per-slide, parallel to `BackgroundPattern` (index-aligned)
- Persisted through `AestheticProfilePersistence` → `ClientAestheticProfile` → `ClientAestheticDesignContext`
- Reconciler: `BackgroundTransparencyReconciler.Align()` tolerantly pads/truncates arrays, never throws
- Render: `BuildTemplateBackedMarkup` applies opacity treatment per `specIndex` when signal is true (constant `BlendedPhotoOpacity = 0.72`)

**Documentation (WU-0)**
- `comfyui-generation-time-report.md`: investigative findings on two 10-minute ComfyUI timeouts
- Conclusion: timeouts match configured 600s budget exactly; generation time attributable to `--directml --lowvram --cpu-vae` backend, not code misconfiguration
- Recommendation: no code fix

### Frontend (Web) Changes (WU-4)

- New module: `apps/web/lib/studio/degrade-badge.ts` with `getDegradeReason()` predicate
- Modified `Envelope` interface: added `degradeReason?: string | null`
- New component: `SlideDegradeBadge` (subtle informational chip, styled after `InferredFieldsDisclosure`)
- Wired into: carousel viewer corner, story viewer corner, slide-strip thumbnail (dot variant)
- Autosave preserves marker via `[key: string]: unknown` round-trip

---

## Spec Merges

### New Specs Created

**`openspec/specs/content-studio/spec.md`** (was missing)
- Created from client-aesthetic-fidelity-fixes delta
- Added requirements: CAFF-CS-01 (per-slide degrade indicator), CAFF-CS-02 (autosave round-trip)

### Specs Extended

**`openspec/specs/profile-analysis/spec.md`**
- Appended delta: Three-Role Typography Extraction, Per-Slide Photo-Transparency Signal
- Added 4 requirements: CAFF-PA-01 through CAFF-PA-04
- All additive; existing profile-analysis behavior unchanged for clients lacking new signals

**`openspec/specs/content-generation/spec.md`**
- Appended delta: Faithful ClientAesthetic Degrade, Typography Wiring, Per-Slide Transparency Render
- Added 7 requirements: CAFF-CG-01 through CAFF-CG-07
- All additive; FlatColor short-circuit remains first and unchanged; scrim card scoped to non-ClientAesthetic only

---

## Verification Summary

### Final Test Results

| Suite | Passed | Failed | Status |
|-------|--------|--------|--------|
| Backend (.NET) | 3721 | 0 | PASS |
| Frontend (Web) | 391 | 0 | PASS |

### Verify Report Result (Final-State Facts)

- **Result**: PASS WITH WARNINGS
- **CRITICAL issues**: 0
- **WARNING count**: 1
  - **Issue**: `TypographyNormalizer` is not role-aware for new caption typography role
  - **Impact**: Does not violate any spec-delta requirement (normalizer only rewrites generic-only declarations; caption role is a structural add, not a normalization defect)
  - **Disposition**: Filed as GitHub issue #79 (`Incoders-Tools/incoders-hive`), explicitly out of scope for this change's completion
- **SUGGESTION count**: 1
  - Log the TypographyNormalizer gap (done)

### Verification Criteria

| Criterion | Status | Notes |
|-----------|--------|-------|
| Photo-degrade trigger A renders flat, no AI designer call | PASS | Verified with forced-failure fixtures |
| AI-designer-null trigger B renders flat, no scrim card for ClientAesthetic | PASS | Verified with `ActiveFailingDesigner` fixture |
| Non-ClientAesthetic jobs keep legacy scrim card | PASS | Regression test confirmed |
| FlatColor short-circuit never reaches new triggers | PASS | Regression test confirmed |
| Degrade marker written on degraded slides only | PASS | Serialization round-trip verified |
| Content Studio shows per-slide degrade badge | PASS | Frontend logic tested, autosave round-trip verified |
| Extractor extracts all three typography roles | PASS | Defensive parsing tested |
| Per-slide transparency round-trips and renders per `specIndex` | PASS | Reconciliation tested, opacity applied conditionally |
| A transparency array longer/shorter than `BackgroundPattern` is reconciled | PASS | Tested; array reconciliation never throws |
| **Reprocess reference job** | DEFERRED | Criterion not executed during verify; requires ~30+ min against local dev DB/ComfyUI infra; correctly deferred as outside verification scope (no blocking issue) |
| ComfyUI findings report committed, states no code fix | PASS | `comfyui-generation-time-report.md` included, recommendation stated |
| MUST gates pass (`Run-Gates.ps1`) | PASS | Architecture gate green; no provider/EF type crossing into Domain/Application |

---

## Commits to Dev

All 8 work units committed sequentially to `dev` branch (per project convention: no PR chain, direct commits):

1. `89bba6d` — WU-0 ComfyUI findings report (docs)
2. `2ece6eb` — WU-1a Trigger A + B degrade routing, scrim-card removal
3. `e92c138` — WU-1b Envelope degrade marker + serialization
4. `d494a30` — WU-2a Typography extraction (prompt, DTO, Map())
5. `b4577a1` — WU-2b Typography render (caption members, ResolveTypography, AllowedFonts)
6. `351f64b` — WU-3a Per-slide transparency (extractor + persistence round-trip)
7. `58191dd` — WU-3b Per-slide transparency (render, BuildTemplateBackedMarkup opacity)
8. `2cee9e1` — WU-4 Content Studio degrade badge
9. `3a8a1ea` — (follow-up fix or verification commit)
10. `a96444b` — (follow-up fix or verification commit)
11. `9a7c229` — (final state commit)

---

## Deferred/Follow-Up Work

### Separate GitHub Issues (Out of Scope)

| Issue | Description | Status |
|-------|-------------|--------|
| **#79** | TypographyNormalizer not role-aware for caption role | Open — requires regex-based normalizer design work |
| **#78** (tracking issue) | Client-aesthetic-fidelity-fixes | Will receive closing summary comment (NOT closed — user owns PR merge) |

### Deferred Verification Criterion

- **Reprocess Lucia Fernandez reference job**: Deferred by design (criterion accepted as deferred in proposal). Requires ~30+ min against local dev DB/ComfyUI infrastructure; execution deferred to manual testing/reference-job verification step outside SDD cycle scope. No code blockers.

---

## Archive Contents

- `exploration.md` — root cause analysis and approaches
- `proposal.md` — settled decisions, scope, delivery plan (revision 2)
- `design.md` — technical decisions D1-D7, data flow, file changes, test strategy
- `tasks.md` — 8 work units with all tasks marked complete
- `comfyui-generation-time-report.md` — investigative findings, no code fix recommended
- `archive-report.md` — this file

**Moved from**: `openspec/changes/client-aesthetic-fidelity-fixes/`
**Moved to**: `openspec/changes/archive/2026-08-24-client-aesthetic-fidelity-fixes/`

---

## Key Learnings

1. Additive-nullable schema fields with init-defaulted members enable zero-migration-cost rollout for CarouselVisualComposer-scoped ClientAesthetic enhancements across Domain/Application/Infrastructure layers.

2. The `CompositionSpecEnvelope` envelope pattern (`[key: string]: unknown` + `JSON.stringify` autosave round-trip) is a zero-friction alternative to DTO plumbing for operator-facing metadata that lives in JSON blobs and needs no DB schema change.

3. Degrade-trigger predicates as pure, testable static methods (not inlined processor logic) reduce hidden test coverage gaps for failure-path code that is invisible in happy-path runs.

4. Per-slide signal reconciliation (e.g., `BackgroundTransparencyReconciler`) following `BackgroundPatternReconciler`'s tolerant-pad pattern preserves canary detection of genuine array-length bugs while preventing silent failures from carousel-length variance.

5. TypeScript unknown-key preservation via `serializeEnvelopes(parseEnvelopes(...))` is a proven pattern for schema-extension without breaking existing frontends or migrations, provided tests lock the round-trip identity.

---

*Archive report generated 2026-08-24.*
*SDD cycle complete. Change is closed.*
