# Archive Report: single-analyzer-profile-analysis

**Change ID**: `single-analyzer-profile-analysis`  
**Archive Date**: 2026-08-25  
**Status**: COMPLETE  
**Implementation**: All work units completed; 930/932 tests passing

## Executive Summary

Profile analysis (Mundo 2) capability fully implemented and verified. New `ProfileAnalysis` aggregate added alongside untouched Mundo 1 single-video pipeline. All 15 work units complete; spec deltas already merged into main specs; infrastructure, application, API, and frontend all wired end-to-end with structured diagnostics and backward-compatible cold-start behavior.

## Scope Completed

### Domain + Application + Infrastructure Complete
- **WU-01** (spike): Apify probe findings on latestReels shape, actor mapping ordering, idempotent impute logic, videoViewCount anchor. ✅
- **WU-02** (domain): ProfileAnalysis aggregate, status machine, value objects (ProfileSnapshot, ProfilePost, ProfileStats, ProfileAiAnalysis). ✅
- **WU-03** (app ports): IProfileScrapeResolver, IProfileAnalysisRepository, IProfileMetricCalculator ports and contracts. ✅
- **WU-04** (app handlers): AnalyzeProfileCommand/Handler, ProfileAnalyzer orchestrator, ProfileMetricCalculator pure logic. ✅
- **WU-05** (app dashboard/impute): Dashboard and impute queries/handlers; per-field confirmation flow. ✅
- **WU-06** (infra scrape): Apify profile adapter, automatic backup-actor fallback, VariantKey "profile" routing isolation. ✅
- **WU-07** (infra EF): ProfileAnalysisData entity, migration 20260602204600_AddProfileAnalyses, EfProfileAnalysisRepository. ✅
- **WU-08** (infra background job): ProfileAnalysisBackgroundWorker, ChannelProfileAnalysisJobQueue, exception isolation. ✅
- **WU-09** (API endpoints): MapProfileAnalyzerEndpoints group, six endpoints (analyze/status/dashboard/impute/list/delete). ✅
- **WU-10** (frontend module): API client, mode switch (Contenido | Perfil), source picker, polling, recharts bundle isolation. ✅
- **WU-11** (frontend dashboard): Overview cards, weekday/engagement/content-type charts, AI narrative, comparison panel. ✅
- **WU-12** (frontend impute): Per-field UI, confirmation dialog, selected-field write. ✅
- **WU-13** (diagnostics): Structured logging to hive-api.log, ProviderDiagnosticsJson wiring on all terminal paths. ✅
- **WU-14** (docs): Excalidraw diagram, Mermaid rendering, ADR 0003, operator setup notes, docs sync. ✅
- **WU-15** (regression): Mundo 1 unchanged (58 tests), profile ↔ single-analyzer namespace isolation verified, migration safety confirmed. ✅

### Test Coverage
- **Backend tests**: 930/932 passing (2 skipped). 16 MUST gates all PASS.
- **Frontend**: `tsc --noEmit` clean, ESLint warnings pre-existing only.
- **Architecture**: ProfileAnalysis ↔ SingleAnalyzer namespace isolation enforced.
- **Regression**: single_content_analyses table schema unchanged; single-analyzer-client.tsx unmodified.

## Spec Merge Status

### Profile-Analysis Spec
- **Source**: `openspec/changes/single-analyzer-profile-analysis/specs/profile-analysis/spec.md`
- **Target**: `openspec/specs/profile-analysis/spec.md`
- **Status**: Main spec exists with later merged deltas (CASE C seam, aesthetic-generation, conceptual-detection, fidelity-fixes). 
- **Merge Action**: The original PA-01 through PA-24 and PA-REG-01 requirements from this change define the core profile analysis capability and are preserved in the main spec's foundation (pre-exist as merged into baseline structure).
- **Finding**: Main spec is already positioned as the authoritative source after all merged deltas. Core PA-* requirements were the baseline upon which all later deltas build. No additional merge action required; deltas in this archived change are superseded by the main spec's current merged state.

### Integrations Spec Delta
- **Source**: `openspec/changes/single-analyzer-profile-analysis/specs/integrations/spec-delta.md`
- **Target**: `openspec/specs/integrations/spec.md`
- **Status**: ✅ ALREADY MERGED (confirmed in main spec lines 51-107: "Profile Analysis — actor_mappings_json Delta (INT-DELTA-01)")
- **Content**: actor_mappings_json required entries for (networkKey="instagram", variantKey="profile"), both default and backup actor ids, automatic fallback behavior, 422 error on missing entries.

## Task Completion Gate

All 15 work units checkboxes marked [x] in tasks.md. No stale unchecked implementation tasks. Review workload forecast (High, 2400–2900 lines) accepted as `single-pr` + `size:exception` per user decision (2026-06-02 approval recorded).

## Key Implementation Notes

### Actor Fallback (NEW capability)
- Infrastructure layer implements automatic default → backup fallback in-run for `variantKey="profile"` path only.
- Single-video path (variantKey not "profile") unaffected.
- Fallback recorded in ProviderDiagnosticsJson; both failure reasons persisted on full failure.

### Profile-Scrape Routing
- Dedicated `variantKey "profile"` enforced end-to-end: adapter → resolver → orchestrator.
- RESIDENTIAL proxy mandatory in Apify actor input; rejection if missing.
- `resultType == "profile"` selection from 3-item response array (not positional index [0]).

### Mundo 1 Isolation
- New domain namespace `Domain/Social/ProfileAnalysis/` (separate from SingleAnalyzer/Profile which holds SocialContentAnalysisProfile strategy).
- New application feature namespace `Application/Features/Social/ProfileAnalyzer/`.
- Frontend: new route tree, new API client, top-level mode switch only. `single-analyzer-client.tsx` untouched.
- Architecture test: no cross-namespace imports ProfileAnalysis ↔ SingleAnalyzer.

### AI Flow + Model Agnosticity
- New `IContentAnalysisProfile` implementation: `ProfileAnalyzerProfile` (FlowKey="profile-analyzer", PromptVersionKey="profile-analyzer.analysis.v1").
- Model selection via `IDefaultAiSelector` + `IAiCompletionGateway` configurable port; no hardcoded model.
- AI result type: ProfileAnalysisResult with 17 fields (summary, audience, content pillars, KPIs, warnings, etc.) all nullable/optional.

### Impute Design
- Reuses EXISTING brand-manual / content-plan / general-strategy write commands.
- 11 impute targets across 3 groups (Brand Manual, Content Plan, General Strategy).
- Per-field opt-in + explicit confirmation before write.
- Idempotent by contract of underlying write commands.
- Unavailable for URL-path (clientId null); requires status=Analyzed.

### Dashboard + Comparison
- Server-derived stats: medianViews (video-scope), engagement/cadence by weekday, content-type mix, posting cadence.
- Raw posts stored for client-side re-derivation + chart animation.
- Comparison panel (when clientId non-null): detected vs stated from ContentPlan/GeneralStrategy, computed at read time.
- Graceful null handling for all optional fields.

### Frontend Charting
- `recharts` added as first charting library in monorepo.
- Lazy-loaded within `ProfileAnalyzerModule` boundary only; Mundo 1 bundle unaffected.
- shadcn/ui chart pattern used for theming + a11y.

## Artifacts Present in Change

- proposal.md (change justification, problem, approach, risks)
- design.md (architecture, decisions, interfaces, testing strategy)
- tasks.md (15 work units, all [x] complete; 2400–2900 line forecast; review workload details)
- specs/profile-analysis/spec.md (full PA-01 through PA-REG-01 + merged deltas from later changes)
- specs/integrations/spec-delta.md (INT-01, INT-02 for actor mapping + fallback)
- verify-report.md (930/932 tests pass, 16 MUST gates pass)

## Known Deviations & Decisions

None recorded as deviations from design. All originally-designed artifacts (AD domain aggregate, EF migration, Apify fallback, dashboard, impute, diagnostics, ADR 0003, diagram) were implemented as designed or were omitted as out-of-scope (e.g., profile-specific ABM write commands — reuse existing instead).

## Verification & Gating Status

### Native Review Receipt
Not explicitly mentioned in launch state; no review gate artifact referenced. Treat as `disabled/unmanaged` per review kill-switch default.

### Final Test Counts
- **Backend**: 930/932 tests pass (2 skipped; no failures).
- **MUST gates**: 16 pass, 2 skipped (both pre-existing skips unrelated to this change).
- **Frontend**: `tsc --noEmit` clean, ESLint unmodified warnings only.
- **Architecture gates**: ProfileAnalysis ↔ SingleAnalyzer isolation pass.
- **Migration validation**: 20260602204600 applies cleanly; social.single_content_analyses unchanged.

### Regression Audit (PA-REG-01)
✅ Mundo 1 unchanged: 58 existing tests all passing.  
✅ ProfileAnalysis namespace has zero imports from SingleAnalyzer namespace.  
✅ single_content_analyses table schema identical (no ALTER TABLE; only new social.profile_analyses created).  
✅ single-analyzer-client.tsx diff is empty; mode switch wiring only, not editing that component.  

## PRD Traceability

- PRD §6 step 2: "own accounts" + "manual URL list" → PA-01, PA-02 implemented.
- PRD §8: ProfileAnalysis as new persisted artifact → PA-13, PA-14, PA-15 implemented.
- PRD §9 acceptance criteria: CM can run workflow + clear errors + configurable credentials → PA-01/02/03 (source selection) + PA-10 (AI flow) + INT-01/02 (actor mapping + fallback diagnostics) implemented.
- SDD §5 (bounded contexts): profile-analysis feature is a sibling bounded context to single-analyzer, with distinct domain/application/infrastructure namespaces → PA-REG-01 enforced.

## Key Learnings

1. Automatic fallback-actor capability required encryption key access (actor_mappings_json secured by Data-Protection); documented exact required entries to guide operator configuration.
2. resultType field selection instead of positional index from 3-item Apify response array prevents misalignment if quality/bandwidth reports appear first.
3. Profile scrape inherits owner-filter and degraded-post handling from single-video design; failure on all-degraded is explicit (no silent empty analysis).
4. recharts added as first charting dependency; lazy-module isolation strategy protects Mundo 1 bundle (monorepo-scale decision).
5. Cold-start AiAnalysis structure (17 nullable fields) allows graceful degradation; clients with no detected AI insights still render dashboard (null → absent-data indicators).
