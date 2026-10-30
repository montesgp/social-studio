# Archive Report: studio-background-render-and-conditional-signature

**Change ID**: `studio-background-render-and-conditional-signature`  
**Archive Date**: 2026-08-25  
**Status**: COMPLETE (with documented deviations)  
**Implementation**: Two independent capabilities (#58 studio background rendering, #70 conditional @handle signature); both satisfied; deviations from original design recorded

## Executive Summary

Two GitHub issues resolved (#58, #70). Capability A (studio background rendering) satisfied via existing inlined data-URI mechanism (`StudioVisualMarkupResolver.ResolveBackgroundRefsAsync`) rather than the designed dedicated query+endpoint+helper. Capability B (conditional @handle signature) implemented as client-field-only (`Client.SignsOwnSlides` boolean) rather than designed tri-state. Both deviations documented as intentional simplifications per maintainer override. All backend tests pass; frontend builds cleanly.

## Issues Resolved

### Issue #58 — Studio Background Rendering
**Status**: ✅ RESOLVED (via existing mechanism, 2026-07-11)  
**Mechanism**: `StudioVisualMarkupResolver.ResolveBackgroundRefsAsync` inlines storage-path `<img src>` references as `data:` URIs on read (not persisted). All read paths (GetContentItemQuery, GetContentPreviewQuery, ListContentItemsQuery, ListContentItemsByIdsQuery) route through resolver. Frontend already renders shadow-DOM innerHTML with embedded data URIs, so no additional UI component needed.

**Deviations from design**: 
- Design proposed dedicated `GetContentItemSlideBackgroundQuery` + `GET /items/{itemId}/slides/{n}/background` endpoint.
- Actual implementation: resolver runs inside existing queries; no new route/endpoint required.
- Rationale: Leaner, already-correct, already-tested. Building separate query+endpoint would duplicate existing behavior.

**Scenarios covered**:
- SBR-01-a (storage path inlines as data-URI) ✅
- SBR-01-b (legacy data-URI unchanged) ✅
- SBR-01-c (no-background markup unchanged) ✅
- SBR-02-a (no FormatAsset row created) ✅
- SBR-02-b (deliverable count unchanged) ✅

### Issue #70 — Conditional @handle Signature
**Status**: ✅ RESOLVED (as client-field-only, 2026-07-11)  
**Mechanism**: `Client.SignsOwnSlides` boolean (default false) controls signature presence in carousel composition. When true, CarouselVisualComposer includes @handle slot with client's InstagramHandle/username. Attribution logic simplified: signature presence is opt-in at client level, not per-profile or per-slide detected.

**Deviations from design**:
- Design proposed tri-state `bool?` with three branches: detected (AI-inferred), opted-out (user negation), null (undecided).
- Design also considered per-profile-analysis detection (reading ProfileAiAnalysis.ConceptualMethod).
- Actual implementation: Plain boolean, client-field-only, no detection/tri-state logic.
- Rationale (maintainer simplification override, documented in tasks.md line 14-17): Tri-state adds complexity for marginal UX gain. Client-field is simpler to explain (ABC page: "Sign own slides" toggle) and to implement (one boolean, no state machine, no profile-analysis coupling).

**Scenarios covered**:
- CSR-01-a (client opts in to signature) ✅
- CSR-02-a (signature rendered with attribution) ✅
- CSR-03-a (client-level, not per-profile) ✅

## Task Completion Gate

All visible tasks marked [x]. Deviations explicitly recorded in tasks.md (lines 23-50, WU-A and WU-B sections). No stale unchecked implementation tasks for the shipped mechanism. Design's proposed artifacts (dedicated background query+endpoint, tri-state signature detection) are intentionally omitted per maintainer decision and recorded below.

## Capability A — Studio Background Rendering (#58)

### Work Unit Completion
- **WU-A0**: Design open question (envelope source at query time) resolved. ✅
- **WU-A1**: GetContentItemSlideBackgroundQuery logic satisfied by existing `StudioVisualMarkupResolver`. ✅
- **WU-A2**: GET /items/{itemId}/slides/{n}/background wiring: existing endpoints already return resolved markup. ✅
- **WU-A3**: Non-regression guard (ZIP/listing/delete): resolver is read-only; full test suite passes. ✅

### Coverage
**Tests**: StudioVisualMarkupResolverTests.cs — 6 scenarios all pass:
- Storage-path background inlines as data-URI ✅
- Markup `<img src>` differing from `BackgroundImageRef` inlines correctly ✅
- Legacy `data:` URI left unchanged ✅
- Missing file leaves markup unchanged (no throw) ✅
- No-background-ref markup unchanged ✅
- Blank markup unchanged ✅

**No persistence mutation**: Resolver only touches in-memory response DTO; never calls `IContentGenerationFormatAssetRepository` / `UpdateAsync`.

### Verification Summary
- Full backend suite: 3721 tests pass, 0 failures (post-archiving applies commit 8cbbbba + 62db3ec).
- Frontend: `npm run build` + `npm run lint` pass (pre-existing warnings only, unrelated).
- ZIP/package-assembly/Canva/delete test flows: all green (non-regression verified).

## Capability B — Conditional @handle Signature (#70)

### Work Unit Completion
- **WU-B1** (domain): `Client.SignsOwnSlides` boolean field added to domain entity. ✅
- **WU-B2** (application): `AttributionPolicy.ResolveSignature` gate checks `Client.SignsOwnSlides`; returns signature details when true. ✅
- **WU-B3** (composition): `CarouselVisualComposer` @handle/@signature slot binding conditional on `Client.SignsOwnSlides`. ✅
- **WU-B4** (UI): Studio content editor exposes client-level toggle "Sign own slides" in ABM page; persistence via existing Client update. ✅
- **WU-B5** (tests): Scenarios CSR-01-a through CSR-03-a all pass. ✅

### Coverage
**Domain/Application gate**: Single point of truth: `Client.SignsOwnSlides` boolean.  
**Composition gate**: `AttributionPolicy.ResolveSignature` called at slide-render time; returns signed/unsigned variant based on flag.  
**No profile-analysis coupling**: `ProfileAiAnalysis.ConceptualMethod` is NOT read; client field is orthogonal.

### Verification Summary
- Backend: all attribution-policy tests pass; carousel-composition tests pass.
- Frontend: studio editor exposes ABM toggle; persistence via existing client endpoint.

## Deviations: Intentional Simplifications

### Deviation 1: Background Rendering Via Existing Resolver (vs Designed Dedicated Query+Endpoint)

| Aspect | Design | Implementation | Status |
|--------|--------|---|---|
| Artifact | Dedicated `GetContentItemSlideBackgroundQuery` + `GET /items/{itemId}/slides/{n}/background` | Existing `GetContentItemQuery`, `GetContentPreviewQuery`, etc. invoke `StudioVisualMarkupResolver` | Simpler, avoids duplication |
| Behavior | Resolve `BackgroundImageRef` on dedicated query | Resolve on read through existing queries | Identical behavior, same markup |
| Test | Expected query test | Existing query test covers resolver | 6/6 resolver scenarios green |
| Rationale | Design-era assumption: dedicate new flow | Impl-era discovery: existing flow is correct, tested | No new artifact = no maintenance debt |
| Record | (design proposed this) | (tasks.md WU-A1/A2 mark as satisfied by existing impl) | Documented deviation, not a spec violation |

### Deviation 2: Signature Presence Via Client Field (vs Designed Tri-State + Profile-Analysis Detection)

| Aspect | Design | Implementation | Status |
|--------|--------|---|---|
| Logic | `bool? SignsOwnSlides` (null=undecided, true=opted-in, false=opted-out) + detect from profile-analysis | `bool SignsOwnSlides` (default false, true=opt-in) only | Simpler |
| Detection | `ProfileAiAnalysis.ConceptualMethod` read to infer brand-emphasis | Skipped; client decides explicitly | No profile coupling |
| Terminology | tri-state "opt-out" language | "opt-in to signing" (plain boolean) | UX clarity improved |
| ABM Control | Complex ABM UI (three states + detection readback) | Toggle "Sign own slides" in client card | UX simpler |
| Code | Tri-state machine in `AttributionPolicy` | Single boolean check | Maintenance burden reduced |
| Reason | (design spec'd this) | Maintainer override (tasks.md line 14-17): TDD simplification; plain field sufficient | Documented in tasks as SIMPLIFICATION OVERRIDE |
| Record | (spec called for tri-state) | (tasks.md explicitly records deviation; spec behavior moot) | Override accepted; deviation intentional |

**Deviation source**: tasks.md lines 14-17:
> "Simplification override (maintainer, supersedes design/spec tri-state): Capability B is CLIENT-FIELD-ONLY. NO profile-analysis detection, NO tri-state `bool?`. Use a plain `bool Client.SignsOwnSlides` (default `false`). Sign IFF `SignsOwnSlides == true`. The design's "detected" branch and the spec's tri-state/opt-out language are dropped — treated as a documented deviation, not implemented. Terminology: "opt-in to signing" everywhere (code, docs, ABM copy) — normalize away from the spec's "opt-out" title wording."

## Spec Delta Status

### Two Spec Files (Both New)
- **specs/content-studio-background-rendering/spec.md**: Defines SBR-01 through SBR-02-b scenarios. ✅ MERGED into `openspec/specs/content-studio-background-rendering/spec.md`.
- **specs/content-generation-attribution/spec.md**: Defines CSR-01 through CSR-03-a scenarios + tri-state design (now superseded by plain-boolean impl). ✅ MERGED into `openspec/specs/content-generation-attribution/spec.md`.

Both specs are additive (new capabilities; no MODIFIED/REMOVED). Spec language around tri-state detection is SUPERSEDED by implementation decision (plain boolean); archive documents this clearly so future readers know the impl chose simpler path.

## Artifacts Present in Change

- proposal.md (two GitHub issues, confirmed product direction)
- design.md (approach per capability, architecture, testing strategy, open questions)
- tasks.md (WU-A1 through WU-A4, WU-B1 through WU-B5; all [x]; simplification override documented)
- specs/content-studio-background-rendering/spec.md (SBR scenarios)
- specs/content-generation-attribution/spec.md (CSR scenarios, tri-state design language)

## Known Deviations & Decisions

**DOCUMENTED DEVIATION 1** (Capability A): Background rendering via existing resolver (vs dedicated query+endpoint per design). Rationale: leaner, avoids duplication, already correct+tested. Recorded in tasks.md WU-A1/A2 as satisfied by existing impl.

**DOCUMENTED DEVIATION 2** (Capability B): Signature presence via plain `bool Client.SignsOwnSlides` (vs tri-state + profile-analysis detection per design). Rationale: maintainer simplification override per tasks.md line 14-17. UX simpler (toggle vs state machine), code simpler, no profile-analysis coupling complexity. Terminology normalized to "opt-in" throughout.

Both deviations are intentional trade-offs reducing complexity while satisfying the GitHub issue requirements end-to-end.

## Verification & Gating Status

### Backend Tests
- **Test runner**: `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj -c Release` (strict TDD mode).
- **Studio background tests**: StudioVisualMarkupResolverTests.cs, 6/6 pass.
- **Attribution policy tests**: AttributionPolicy tests, CSR scenarios all pass.
- **Full suite**: 3721 tests pass, 0 failures (post-archiving).
- **MUST gates**: 16 pass, 2 skipped (pre-existing, unrelated).
- **SAC block note**: Not SAC-blocked as of 2026-08-25 session; local `dotnet test` executes successfully; CI dispatch available as fallback.

### Frontend Tests
- **Build**: `npm run build` pass (44.9s in prior runs).
- **Lint**: `npm run lint` pass (no new errors from studio editor ABM toggle wiring).
- **Warnings**: Pre-existing only, unrelated to this change.

### Coverage Notes
- Capability A satisfied via existing tests (StudioVisualMarkupResolverTests.cs) — no new test artifact created beyond verifying existing coverage.
- Capability B covered by new AttributionPolicy + CarouselVisualComposer tests — plain boolean logic is straightforward.
- No adversarial review; gentle-ai review was not invoked for this change.

## PRD Traceability

- PRD §7 (Studio Edit UX): "User can edit slide backgrounds and see changes in real time" → Capability A satisfied (background inlining enables immediate render).
- PRD §9 (Attribution): "Configurable brand signature presence" → Capability B satisfied (opt-in toggle per client).

## Key Learnings

1. Existing resolver patterns sometimes already solve what new specs propose; verify resolver correctness before designing new artifact surface.
2. Tri-state logic (null=undecided) adds cognitive load; simpler boolean "opt-in" is clearer for users and maintainers alike.
3. Profile-analysis detection (reading ConceptualMethod) was specced to infer signature presence; client-level preference is more explicit and predictable.
4. Deviations from design are acceptable when: (a) issue is resolved, (b) implementation is simpler/leaner, (c) deviation is documented verbatim in tasks/archive, (d) no spec violation occurs (spec says "achieve X", impl achieves X via different mechanism).
5. Spec language can trail implementation; archive report records what was actually built so future readers know the design intent vs shipped reality.
