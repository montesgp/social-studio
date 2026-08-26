# Tasks: Client-Aesthetic Visual Sophistication

## Status: PARTIAL — WU-7/WU-8 cut, gate did not pass

WU-0 through WU-6 (`DecorationMotifs` schema, extraction, persistence, divider render, archetype
geometry presets, ornament detection schema) shipped and are merged into the main specs. WU-7
(ornament crop port) and WU-8 (ornament render + degrade + badge) are **deliberately cut**, not
partially started: task 6.4's reliability gate for the vision model's bounding-box localisation
found zero measurable real bounding-box data in production, per its own stated fallback clause.
Full verdict: `openspec/changes/client-aesthetic-visual-sophistication/wu6-reliability-gate.md`.

**Re-entry condition** (from `wu6-reliability-gate.md`): re-attempt WU-7/WU-8 once either (a) a
real client with a genuinely recurring icon-type brand mark or ornament is analysed through a live
`ai-aesthetic` route and a human reviewer visually confirms the returned bounding box actually
bounds the element on the source image for at least 2 real client feeds, or (b) the org runs the
aesthetic extractor online against archived reference images in a session where a human reviewer
can compare crops to source images directly. Until then, `content-generation` `CAVS-CG-04`/`05`
and `content-studio` `CAVS-CS-01`/`02` remain unmerged spec-delta text, not shipped requirements.

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~3,880 total (proposal rev-2), confirmed per-WU below |
| 800-line budget risk | Low |
| Chained PRs recommended | No |
| Suggested split | Single PR track, 9 sequential commits to `dev` (one per WU) |
| Delivery strategy | single-pr |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Low

(Project budget is 800 changed lines/work-unit per session preflight; `400-line budget risk` line kept literal for the guard contract, read as "800-line" per the cached override above.)

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| WU-0 | Golden capture (pre-change) | commit 1 | N/A (no test, artifact capture) | N/A — fixture snapshot, no runtime | Delete golden file, no code touched |
| WU-1 | `DecorationMotifs` value objects | commit 2 | `dotnet test --filter DecorationMotifsTests` | N/A | Delete new file, unreferenced |
| WU-1b | Port rename | commit 3 | `dotnet test --filter SkiaBrandMarkAssetExtractorTests` | N/A | Revert rename commit |
| WU-2 | Extraction half | commit 4 | `dotnet test --filter MultimodalAestheticStyleExtractorTests` | N/A | Revert extractor diff; field unread |
| WU-3 | Persistence round-trip | commit 5 | `dotnet test --filter AestheticProfilePersistenceTests` | N/A | Revert persistence diff; field unread |
| WU-4 | Divider render + golden guard | commit 6 | `dotnet test --filter FlatCarouselMarkupGoldenTests` | N/A — deterministic builder, no external call | Revert divider branch; null path unaffected |
| WU-5 | Archetype geometry presets | commit 7 | `dotnet test --filter FlatCarouselMarkupGoldenTests` | N/A | Revert preset resolution; falls back to WU-4 geometry |
| WU-6 | Ornament detection + reliability gate | commit 8 | `dotnet test --filter DecorationMotifsTests\|MultimodalAestheticStyleExtractorTests` | Manual: run extraction against 2+ real client feeds, record accept/reject | Revert detection schema; inert |
| WU-7 | Ornament crop port + adapter | commit 9 (gated on WU-6) | `dotnet test --filter SkiaAestheticAssetCropExtractorTests` | N/A | Remove port/adapter; orphan crop objects inert |
| WU-8 | Ornament render + degrade + badge | commit 10 (gated on WU-6) | `dotnet test --filter ClientAestheticDegradeDecisionTests\|ProcessorAestheticWiringTests` then `apps/web` `pnpm test degrade-badge` | Manual: Studio preview + PPTX export on one real slide with ornament | Revert render branch; degrade reverts to pre-change (no token) |
| WU-9 | Spec/doc sync + ADRs | commit 11 | N/A (docs) | N/A | Revert docs only |

### Per-Work-Unit Line Re-Confirmation (against this task breakdown, not the proposal alone)

| WU | Proposal rev-2 est. | Task-breakdown check | Still ≤800? |
|----|---|---|---|
| WU-0 (golden) | not separately estimated | one committed fixture file; goldens excluded from authored review-line count per design's Testing Strategy note | Yes — not counted |
| WU-1 | ~230 | 1 new file (record/enum/const class) + 1 new test file; matches est. | Yes |
| WU-1b (rename, new — not in proposal's 9 WU) | none | `git mv` on 2 files + 1 test-class rename + 3 call-site edits (ctor param, DI, doc comment); rename diffs are near-zero when git detects the move, remainder is a doc-comment/name edit | Yes, small — own commit, no behavior change |
| WU-2 | ~340 | prompt section + DTO + `Map()` + 13th field + 2 test files | Yes |
| WU-3 | ~330 | flagged by design as "mechanical but line-costly": 15th tuple element + DTO + 3 null-tuple edits (L113/121/187) + 2 passthrough files + 1 production call site (`EfClientAestheticProfileReader.cs`) + 1 verify-only call site (`EfProfileAnalysisRepository.cs`) + audit of 4 test files (named `.` access, expected no-op). The audit is verification, not new lines, in the 4 named-access test files; only `EfClientAestheticProfileReader.cs`'s positional deconstruction adds a real line. Net stays close to the proposal's ~330. | Yes — flagged, confirmed still within budget |
| WU-4 | ~380 | divider primitive + golden RED/GREEN cases | Yes |
| WU-5 | ~480 | `FlatGeometryPresets.cs` (new) + wiring + overlap assertion | Yes |
| WU-6 | ~480 | ornament DTO/prompt/`Map()` + reliability-gate task (non-code, no line cost) | Yes |
| WU-7 | ~600 | crop port extension (no new adapter class — reuses renamed WU-1b adapter per D3) + ADR draft | Yes |
| WU-8 | ~640 (largest) | degrade precedence fn + ornament render/log + Studio resolver test + PPTX matcher + frontend map/component (8 subtasks) — largest unit, ~160 lines of headroom against 800 | Yes |
| WU-9 | ~400 | spec merges + 2 ADRs + owner review + gate run (mostly docs) | Yes |

No work unit's task-level breakdown pushes it over the 800-line budget. WU-1b is new relative to the
proposal's 9-unit table but is a rename-only commit with negligible line cost. WU-8 remains the
largest unit and the only one worth watching if scope grows during implementation.

## Phase 0: Golden Capture (blocking prerequisite, before ANY render code change)

- [x] 0.1 Capture the CURRENT (pre-change) `BuildFlatColorMarkup`/`BuildFlatCarouselSlots` output for one representative `CompositionSpec` fixture and commit it verbatim as `Incoders.Hive.Tests/Application/Social/ContentGeneration/Pipeline/Goldens/flat-carousel-cold-start.html`. Run against `dev` HEAD, no code change. Satisfies the byte-identity prerequisite for CAVS-CG-03.
- [x] 0.2 Add `FlatCarouselMarkupGoldenTests.cs` with one RED-to-GREEN scaffolding test that reads the golden file and asserts `Assert.Equal(golden, BuildFlatColorMarkup(...))` against today's unmodified builder — GREEN now, becomes the regression guard once WU-4/WU-5 land. Commit before any render diff.

## Phase 1 (WU-1): `DecorationMotifs` Value Objects — design D1, D2 scaffolding

Spec: CAVS-PA-01, CAVS-PA-02, CAVS-PA-03. File: `Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/DecorationMotifs.cs` (new).

- [x] 1.1 RED: `Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/DecorationMotifsTests.cs` (new) — `Normalize` preserves an invented token verbatim (CAVS-PA-02-b); null/blank → null (CAVS-PA-02-c); `IsRecognised` false for invented token; `TryParseDividerRule` unrecognised → null (CAVS-PA-03-b).
- [x] 1.2 GREEN: create `DecorationMotifs.cs` — `DividerRuleStyle` enum (`Line`, `LineWithDiamond`, `LineWithDot`), `DecorativeOrnamentDetection` record (reuses `BrandMarkBoundingBox`), `DecorationMotifs` record (`DividerRule`, `CompositionArchetype`, `Ornament`), `CompositionArchetypes` static class (`LeftAligned`/`Centered`/`LowerThird` consts, `Normalize`, `IsRecognised`, `TryParseDividerRule`). No `AccentShape` type anywhere (asserted by 1.1's absence, not a positive test).
- [x] 1.3 REFACTOR: confirm `Normalize` never collapses unrecognised tokens (design D1) and `DividerRuleStyle` unrecognised path returns `null`, not a default enum member.

## Phase 1b (WU-1b): Rename `IBrandMarkAssetExtractor` → `IAestheticAssetCropExtractor` — design D3

Touches already-shipped code from `client-aesthetic-generation-hardening`. Own commit, no new behavior. Must land before WU-7 calls the port under its new name.

- [x] 1b.1 Rename `Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IBrandMarkAssetExtractor.cs` → `IAestheticAssetCropExtractor.cs`; rename the interface and update its doc comment to be element-agnostic (brand mark + ornament).
- [x] 1b.2 Rename `Infrastructure/.../SkiaBrandMarkAssetExtractor.cs` → `SkiaAestheticAssetCropExtractor.cs`; update the class name and DI registration in the composition root.
- [x] 1b.3 Update every existing call site: `ContentGenerationJobProcessor.cs` constructor parameter type, DI registration, and rename `SkiaBrandMarkAssetExtractorTests.cs` → `SkiaAestheticAssetCropExtractorTests.cs` (class + references only, no assertion changes).
- [x] 1b.4 Run `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj -c Release --filter SkiaAestheticAssetCropExtractorTests` and the full brand-mark render suite to confirm the rename introduced no behavior change.

## Phase 2 (WU-2): Extraction Half

Spec: CAVS-PA-01, CAVS-PA-02, CAVS-PA-03. File: `Incoders.Hive.Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs`.

- [x] 2.1 RED: extend `MultimodalAestheticStyleExtractorTests.cs` — divider/archetype DTO fields map correctly (CAVS-PA-03-a, CAVS-PA-02-a); unrecognised archetype token preserved verbatim through `Map()` (CAVS-PA-02-b); malformed/absent divider → null, no throw (CAVS-PA-03-b).
- [x] 2.2 RED: bumped the extractor's prompt/schema version test to v7 and asserted it equals `AestheticFreshness.CurrentSchemaVersion` (in `MultimodalAestheticStyleExtractorTests.cs`'s `ExtractorPromptVersion_IsBumpedToV7`/`Extract_StampsSchemaVersion_OnProducedExtraction` — `PromptVersionBumpAestheticTests.cs` tests unrelated `ContentGenerationPromptVersions` constants, not the aesthetic-extractor schema version; no change needed there).
- [x] 2.3 GREEN: added `decorationMotifs { dividerRule, compositionArchetype }` prompt section + DTO members + `Map()` wiring in `MultimodalAestheticStyleExtractor.cs`; bumped `PromptVersion`/`CurrentSchemaVersion` to v7.
- [x] 2.4 GREEN: extended `Abstractions/Social/ContentGeneration/IClientAestheticStyleExtractor.cs` — 13th trailing-nullable field `DecorationMotifs? DecorationMotifs` on `AestheticExtraction`.
- [x] 2.5 REFACTOR: full suite (3864 passed/0 failed/2 skipped) confirms every pre-existing field still parses intact — no dedicated fixture file exists for this; regression coverage is the full test suite (schema-fatigue regression discipline, CAVS-PA-05).

## Phase 3 (WU-3): Persistence Round-Trip — design's flagged "mechanical but line-costly" change

Spec: CAVS-PA-01-b, CAVS-PA-02-b. Files: `Shared/AestheticProfilePersistence.cs`, `Shared/ClientAestheticProfile.cs`, `Shared/ClientAestheticDesignContext.cs`.

- [x] 3.1 RED: extend `AestheticProfilePersistenceTests.cs` — verbatim round-trip of an unrecognised archetype token (`"diagonal-split"` in ⇒ same string out, never `"unknown"`); pre-change blob (no `decorationMotifs` key) ⇒ `DecorationMotifs = null`, no throw (CAVS-PA-01-b).
- [x] 3.2 RED: extend `AestheticExtractionExtensionTests.cs` and `ClientAestheticProfileExtensionTests.cs` for the same round-trip on their respective mapping layers.
- [x] 3.3 GREEN: add `DecorationMotifs` as the 15th trailing-nullable named-tuple element on `AestheticProfilePersistence.Read(string?)`; update the DTO (`StoredAestheticProfile`) with the `DecorationMotifs` member; update the two early-return null-tuples at lines 113, 121, 187 (14 nulls → 15 nulls).
- [x] 3.4 GREEN: update every mechanical call site for the arity change:
  - `Incoders.Hive.Infrastructure/Social/ContentGeneration/EfClientAestheticProfileReader.cs` L85-88 — positional `var (...)` deconstruction gains a 15th element (`decorationMotifs`), passed through to the `ClientAestheticProfile` constructor.
  - `Incoders.Hive.Infrastructure/Persistence/Postgres/EfProfileAnalysisRepository.cs` L209 — named `read.` access; no positional break, verify no new field needed for `AestheticFreshnessSnapshot`.
  - `Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/AestheticProfilePersistenceTests.cs` — every `AestheticProfilePersistence.Read(...)` call (35 occurrences, named `.` access) is unaffected by arity unless a test asserts the full tuple literal; audit and fix any literal-tuple `Assert.Equal` calls.
  - `Incoders.Hive.Tests/Application/Social/ContentGeneration/Visual/ClientTypographyResolutionTests.cs` L295, L312 — named access, verify unaffected.
  - `Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/ColorPairingAccentTests.cs` L133, L148 — named access, verify unaffected.
  - `Incoders.Hive.Tests/Application/Social/ClientAestheticProfileAdmin/UpdateClientAestheticProfileBackgroundPatternHandlerTests.cs` L98 — named access, verify unaffected.
- [x] 3.5 GREEN: add trailing-nullable `DecorationMotifs` passthrough on `Shared/ClientAestheticProfile.cs` and `Shared/ClientAestheticDesignContext.cs`.
- [x] 3.6 REFACTOR: confirm `AestheticProfilePersistence.Write`/`Serialize` round-trips `DecorationMotifs` symmetrically with `BrandMark` (nearest precedent field).

## Phase 4 (WU-4): Divider-Rule Render Primitive — design D2, byte-identity guard is the RED gate

Spec: CAVS-CG-01, CAVS-CG-03. File: `Application/.../Pipeline/ContentGenerationJobProcessor.cs` (`BuildFlatColorMarkup` L2312, `BuildFlatCarouselSlots` L2364).

- [x] 4.1 RED (blocking): confirm `FlatCarouselMarkupGoldenTests.cs` (from Phase 0) still asserts byte-identical output against `Goldens/flat-carousel-cold-start.html` for `DecorationMotifs = null` — this MUST stay green through every remaining Phase 4-8 task.
- [x] 4.2 RED: add divider-present cases to `FlatCarouselMarkupGoldenTests.cs` — `LineWithDiamond` renders `data-slot="divider"` + rotated-square glyph in the resolved accent colour (CAVS-CG-01-a); no `Accent` role resolves ⇒ falls back to text colour (CAVS-CG-01-b); `DividerRule = null` ⇒ no divider element (CAVS-CG-01-c).
- [x] 4.3 GREEN: implement the divider primitive in `BuildFlatCarouselSlots` as one `data-slot="divider"` raw-string-built `<div>` with a nested non-slot glyph child (design D2) — never a second `data-slot`.
- [x] 4.4 REFACTOR: assert exactly one `data-slot="divider"` occurrence in markup per slide; confirm the byte-identity golden (4.1) is still green.

## Phase 5 (WU-5): Composition-Archetype Geometry Presets

Spec: CAVS-CG-02. Files: `Shared/FlatGeometryPresets.cs` (new), `Pipeline/ContentGenerationJobProcessor.cs`.

- [x] 5.1 RED: extend `FlatCarouselMarkupGoldenTests.cs` — `"left-aligned"` reproduces today's literal offsets exactly (CAVS-CG-02-a, must pass against the Phase 0 golden bytes); two profiles differing only in `CompositionArchetype` produce measurably different title/subtitle/cta geometry (CAVS-CG-02-b); invented unrecognised token ⇒ identical to `"left-aligned"` geometry, no exception (CAVS-CG-02-c); `DecorationMotifs = null` or `CompositionArchetype = null` ⇒ `"left-aligned"` geometry (CAVS-CG-02-d).
- [x] 5.2 GREEN: create `FlatGeometryPresets.cs` — `Resolve(string? token)` token → preset lookup covering accent/title/divider/subtitle/cta/ornament slots per the design's geometry table; `_ =>` arm returns the `"left-aligned"` identity preset.
- [x] 5.3 GREEN: wire `FlatGeometryPresets.Resolve` into `BuildFlatColorMarkup`/`BuildFlatCarouselSlots`, replacing the hardcoded `left:104px`/`left:108px`/`text-align:left` literals.
- [x] 5.4 REFACTOR: add an apply-time assertion that no preset's geometry overlaps the title block or the brand-mark `bottom:56` anchor; confirm the accent pill's markup is unchanged from pre-change output in every archetype (CAVS-CG-03-b).

## Phase 6 (WU-6): Ornament Detection Schema + Reliability Gate — GATES WU-7/WU-8

Spec: CAVS-PA-04. Files: `MultimodalAestheticStyleExtractor.cs`, `DecorationMotifs.cs` (extend from Phase 1).

- [x] 6.1 RED: extend `DecorationMotifsTests.cs` — valid ornament bounding box (within 0..1, above `MinimumNormalizedArea`) is retained with resolved `SourceImagePath` (CAVS-PA-04-a); below-minimum-area/out-of-range/malformed box ⇒ `BoundingBox = null`, no throw (CAVS-PA-04-b). (Cases `DecorativeOrnamentDetection_ValidBox_IsRetained`/`DecorativeOrnamentDetection_InvalidBox_BoundingBoxIsNull_NoThrow` already existed in `DecorationMotifsTests.cs` from prior WU work; verified they satisfy this task's acceptance criteria — no duplicate added.)
- [x] 6.2 RED: extend `MultimodalAestheticStyleExtractorTests.cs` — `ornament { sourceImageIndex, boundingBox }` DTO parses; `sourceImageIndex` resolves to `StoragePath` via the existing post-skip `loadedPairs` mechanism (mirrors `CAGH-PA-03`). Added `Ornament_ValidSourceIndexAndBoundingBox_ResolvesBoth`, `Ornament_EarlierImageFailsToOpen_SourceIndexShiftsToPostSkipOrdering`, `Ornament_AbsentKey_IsNull_DecorationMotifsStillMapsOtherSignals`, and an 11-case `DefensiveParsing_Ornament01..11` suite mirroring WU-6b's brand-mark defensive-parsing suite (malformed/out-of-range/zero-area/missing-index cases).
- [x] 6.3 GREEN: added `ornament` prompt section + `OrnamentDto`/`DecorationMotifsDto.Ornament` + `MapOrnament()` wiring in `MultimodalAestheticStyleExtractor.cs`, reusing `BrandMarkDetection.TryValidateBoundingBox`/`MinimumNormalizedArea` verbatim; bumped `PromptVersion`/`AestheticFreshness.CurrentSchemaVersion` to `aesthetic-extractor/v8`.
- [x] 6.4 GATE (blocking, non-code): **NOT PASSED.** Queried the local dev Postgres (`incoders-hive-dev-postgres`, `social.profile_analyses.aesthetic_profile_json`) for real brand-mark bounding-box data from the already-shipped `client-aesthetic-generation-hardening` change (same extraction mechanism, reused verbatim for the ornament). Found 14 populated `brandMark` rows; 0 carry a `boundingBox` value — the only post-v6 row classified its mark as `markType: "text"` (never bounding-box-eligible). No real bounding-box measurement exists to evaluate. Full verdict, query evidence, and the cut decision are recorded in `openspec/changes/client-aesthetic-visual-sophistication/wu6-reliability-gate.md`. Per this task's own fallback clause, **WU-7 and WU-8 are cut from this apply batch**; the schema/extraction (WU-1/WU-6) ship as inert additive-nullable fields with no renderer consuming them yet.

## Phase 7 (WU-7): Ornament Crop Port + SkiaSharp Adapter *(gated on Phase 6.4 passing — GATE NOT PASSED, see `wu6-reliability-gate.md`; cut from this apply batch)*

Spec: CAVS-CG-04 (crop half). Files: new sibling files alongside the renamed WU-1b port/adapter.

- [ ] 7.1 RED: extend `SkiaAestheticAssetCropExtractorTests.cs` (renamed in 1b.3) — ornament crop over the renamed `IAestheticAssetCropExtractor` port succeeds for a valid box and persists via `IGeneratedAssetStorage` under the analysis folder; never-throws contract preserved for a failing crop.
- [ ] 7.2 GREEN: extend `SkiaAestheticAssetCropExtractor` usage (no new adapter class per design D3 — one renamed sibling port serves both brand mark and ornament) to accept the ornament's `SourceImagePath`/`BoundingBox`.
- [ ] 7.3 GREEN: draft `docs/architecture/adr/` — ornament crop-port ADR recording the one-port-vs-sibling-port decision (D3) and the ornament degrade contract (D4). Deferred to land alongside WU-9 if sequencing is tighter, but the decision text is authored here while context is fresh.

## Phase 8 (WU-8): Ornament Render + Degrade Visibility + Token-Aware Badge *(gated on Phase 6.4 passing — GATE NOT PASSED, see `wu6-reliability-gate.md`; cut from this apply batch)*

Spec: CAVS-CG-04 (render half), CAVS-CG-05, CAVS-CS-01, CAVS-CS-02, CAFF-CS-01 (modified). Files: `ContentGenerationJobProcessor.cs`, `Shared/ClientAestheticDegradeDecision.cs`, `StudioVisualMarkupResolver.cs`, `Infrastructure/Rendering/PptxExportAdapter.cs`, `apps/web/lib/studio/degrade-badge.ts`, `apps/web/components/social/content-studio-panel.tsx`.

- [ ] 8.1 RED: extend `ClientAestheticDegradeDecisionTests.cs` — `ResolveDegradeReason(wholeSlideToken, ornamentUnavailable)` precedence: whole-slide token wins (CAVS-CG-05-b); ornament alone ⇒ `"ornament-unavailable"` (CAVS-CG-05-a); neither ⇒ null; successful crop leaves `DegradeReason` untouched (CAVS-CG-05-c).
- [ ] 8.2 GREEN: add `SlideDegradeReason.OrnamentUnavailable` const + `ResolveDegradeReason` pure function to `ClientAestheticDegradeDecision.cs` (design D4).
- [ ] 8.3 RED: extend `FlatCarouselMarkupGoldenTests.cs` — valid ornament box renders `data-slot="ornament"` `<img>` at the deterministic anchor honouring the archetype preset, never overlapping the title block (CAVS-CG-04-a); `DecorativeOrnamentDetection = null` ⇒ no ornament element (CAVS-CG-04-b); no AI model invoked for any flat slide (assert on a spy/mock model client, criterion 6).
- [ ] 8.4 GREEN: wire ornament crop + `<img>` render into `BuildFlatCarouselSlots`, gated by a new local `bool renderedDeterministicFlat` set at each `RenderFaithfulFlat` call site (design D5); call `ResolveDegradeReason` once at envelope construction (`ContentGenerationJobProcessor.cs` L1315), replacing statement-order-dependent logic; emit the ornament failure to the structured logger unconditionally, before and independently of the resolution call.
- [ ] 8.5 RED: extend `ProcessorAestheticWiringTests.cs` — a slide with BOTH a whole-slide degrade trigger AND ornament failure ⇒ envelope carries only the whole-slide token, AND the ornament log entry (via `ILogger` spy) was still written; no AI model invoked on any flat slide.
- [ ] 8.6 RED: extend `StudioVisualMarkupResolver` tests — ornament asset ref resolves through the same asset-serving port as `BackgroundImageRef`/brand-mark ref (CAVS-CS-01-a); no-ref slide unaffected (CAVS-CS-01-c).
- [ ] 8.7 GREEN: confirm `StudioVisualMarkupResolver` needs no code change (design: it already inlines every `src="…"`, L83) — add the regression test only.
- [ ] 8.8 RED: extend `PptxExportAdapterTests.cs` — ornament ref resolves in PPTX export (CAVS-CS-01-b); `ornament-unavailable` marker never leaks into export (CAVS-CS-02-c, mirrors L602-622 precedent).
- [ ] 8.9 GREEN: add a sibling `data-slot="ornament"` regex matcher + skip-in-text-loop handling to `Infrastructure/Rendering/PptxExportAdapter.cs` (L153, L315-350 knock-on).
- [ ] 8.10 RED (frontend, separate review pass, different codebase/language): extend `apps/web/lib/studio/degrade-badge.test.ts` — each token maps to its own distinct label; any token absent from the map falls back to today's "Fallback design" pair (design D6, CAFF-CS-01 regression).
- [ ] 8.11 GREEN (frontend): add a `getDegradeLabel` token→label map to `apps/web/lib/studio/degrade-badge.ts`; update `SlideDegradeBadge` in `apps/web/components/social/content-studio-panel.tsx` to accept `reason` and resolve through the map at its 3 call sites.
- [ ] 8.12 REFACTOR: confirm the shipped `CAFF-CS-01` badge (whole-slide tokens) is unregressed and the byte-identity golden (Phase 4.1) is still green.

## Phase 9 (WU-9): Spec/Doc Sync + ADRs

- [x] 9.1 Merge `specs/profile-analysis/spec-delta.md`, `specs/content-generation/spec-delta.md`, `specs/content-studio/spec-delta.md` into `openspec/specs/{domain}/spec.md` per the ADDED/MODIFIED sections already drafted. Merged ONLY what shipped: `profile-analysis` CAVS-PA-01..05 (schema/extraction, including the PA-04-c gate verdict recorded inline); `content-generation` CAVS-CG-01..03 (divider render, archetype presets, byte-identity guarantee), with CAVS-CG-04/05 explicitly marked "Not Implemented in This Delivery" rather than merged as shipped requirements; `content-studio` gets a "Not Implemented" note only — CAVS-CS-01/02 and the CAFF-CS-01 modification were never merged as requirement text, since nothing in that capability changed.
- [x] 9.2 The Phase 6.4 gate did NOT pass — the ornament crop-port ADR (would have been drafted in 7.3, which never ran since WU-7 is cut) is **not created**. The WU-7/WU-8 cut decision is instead recorded in `wu6-reliability-gate.md` (already present from WU-6) and cross-referenced from the merged specs, `docs/PRD.md`'s §11 amendment, and this file's top-level Status marker.
- [x] 9.3 Authored and merged `docs/architecture/adr/0015-schema-fatigue-norm.md` recording settled decision 7 (CAVS-PA-05), plus a one-line XML-doc `<remarks>` pointer on `AestheticExtraction` (`IClientAestheticStyleExtractor.cs`) and `ClientAestheticProfile` (`ClientAestheticProfile.cs`) naming the ADR.
- [x] 9.4 Updated `profile-analysis` spec's normative text (`CAVS-PA-05`) to state the binding schema-fatigue norm, with a pointer to ADR 0015.
- [ ] 9.5 Owner review checkpoint: one real generation reviewed for each of two clients with materially different signalled archetypes, accept/reject recorded in writing (success criterion 7). **NOT completed by `sdd-apply`** — this is an explicit human/owner action, not an implementation task; no agent session can author it on the owner's behalf. Remains open until the owner performs and records the review.
- [x] 9.6 Ran `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj -c Release` directly (the authoritative signal for this session; `Run-Gates.ps1`'s `tests.backend` step has the known pre-existing Visual-Studio-DLL-lock environment issue documented since WU-5/WU-6, unrelated to this batch's code). See Work Unit Evidence below for the result.

## Key Learnings

1. `AestheticProfilePersistence.Read` returns a 14-element named value tuple, not a class, so its 15th-element addition breaks positional `var (...)` deconstruction at exactly one production call site (`EfClientAestheticProfileReader.cs`) while all other call sites use safe named `.` access.
2. The pre-change byte-identity golden must be captured against unmodified `dev` HEAD before any render task touches `BuildFlatColorMarkup`/`BuildFlatCarouselSlots`, or the null-path regression guarantee becomes an assertion instead of a verified contract.
3. WU-7 and WU-8 both depend on WU-6's real-feed bounding-box reliability measurement, mirroring the same gate pattern `client-aesthetic-generation-hardening` used for WU-6b, and both revert cleanly if the gate fails.
4. Two early-return null tuples inside `AestheticProfilePersistence.Read` (lines 113 and 121) and one more in a related method (line 187) all require the same 14-to-15 null literal edit in lockstep with the schema addition.
5. The frontend `getDegradeLabel` token map in `apps/web/lib/studio/degrade-badge.ts` is a distinct TypeScript review surface from the backend C# work and stays its own task rather than folding into the WU-8 backend task.
