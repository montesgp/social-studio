# Archive Report: content-aesthetic-generation

**Change ID**: `content-aesthetic-generation` (Phase 1)  
**Archived**: 2026-08-25  
**Status**: COMPLETE  
**Completed**: 2026-06-21  
**Builds on**: `content-background-sourcing-policy` (passive CASE C seam)

---

## Executive Summary

Content Aesthetic Generation Phase 1 (Active ClientAesthetic with Non-Blocking Refresh) complete and shipped to production: `IClientAestheticEnsureFreshService` port + implementation, `ClientAestheticProfile` value object with richer signals (descriptors + voice/copy style + AverageSlidesPerCarousel), server-derived metrics, per-client configurablerefresh threshold, and guidance coexistence with concision constraint all implemented and tested. Full suite green (1774 tests). All WU-1..9 completed. ADR 0006 documented. Spec deltas merged into main openspec. Phase 2 (real post images + multimodal extraction) properly gated and deferred.

---

## Final State Authority

1. **Persisted tasks artifact** (`state.yaml`):
   - Status: `apply-complete`
   - Completed: 2026-06-21
   - All WU-1..9 marked complete (✅)
   - ADR: `docs/architecture/adr/0006-client-aesthetic-ensure-fresh-port.md`
   - Merged specs listed in state

2. **Launch prompt final-state facts** (post-verify):
   - ALL CODE COMPLETE end-to-end (stated in task line 209)
   - Full suite green: 1774 tests passed (task 6.7, confirmed by round-trip test execution)
   - `maxPostsPerProfile` raised 12 → 18 (task 5.4 verified; test 556-559 documents intentional supersession)
   - Refresh threshold per-client configurable (default 30d); persisted in `social.clients` (migration + EF mapping, task 6.1..6.5)
   - Phase 2 image capture properly gated (design intent, not in scope)

3. **Live Production State**:
   - Non-blocking refresh orchestration live; ClientAesthetic generation continues immediately
   - Per-client threshold in use (ABM surface live since 2026-06-21)
   - Copy generators receiving aesthetic guidance (additive to concision)

---

## What Shipped

### WU-1: Domain Foundation — Additive Fields

**ProfilePost**: Added `int? CarouselChildrenCount` (carousel children count, null for non-carousel)

**ProfileStats**: Added `double? AverageSlidesPerCarousel` (server-derived mean of carousel child counts, 1 d.p.)

**Client**: Added `int? AestheticRefreshThresholdDays` (staleness threshold, 1–365 days, null = system default 30d)
- `SetAestheticRefreshThresholdDays(int? days, DateTime nowUtc)` setter (validates 1–365 when non-null)
- `Restore` back-compat preserved

### WU-2: Application — Profile VO + Reader + Repository Freshness

**ClientAestheticProfile** Value Object:
- `BackgroundDescriptors`: text descriptors (from Phase 1)
- `VoiceTone`: copy-style voice signal (from Phase 1)
- `AverageSlidesPerCarousel`: server-derived stat (NEW)
- `AnalyzedAtUtc`: freshness timestamp (NEW)
- Designed to accept image URLs later (Phase 2) via nullable fields without breaking change

**IClientAestheticProfileReader** Port (renamed from IClientAestheticDescriptorReader):
- `GetProfileAsync(orgId, clientId, ct) → ClientAestheticProfile?`
- Returns null when no Analyzed analysis exists
- Compiler-enforced single call site update in `ContentGenerationJobProcessor`

**IProfileAnalysisRepository** Extended:
- `GetLatestAnalyzedCompletedAtAsync(orgId, clientId, ct) → Task<DateTime?>`
- `HasNonTerminalAnalysisForClientAsync(orgId, clientId, ct) → Task<bool>`

**IClientAestheticEnsureFreshService** Port:
- New port family for refresh orchestration
- `EnsureFreshOutcome` enum (Fresh, RefreshEnqueued, SkippedInFlight, SkippedNoOwnAccount)

### WU-3: Application — EnsureFreshService (Core Logic)

**ClientAestheticEnsureFreshService** Implementation:
- Resolves client's own account via `IClientOwnAccountRepository`
- Checks freshness: `client.AestheticRefreshThresholdDays ?? 30` days (system default 30d)
- If stale or missing: fire-and-forget enqueue via `IProfileAnalysisJobQueue` (mirrors `AnalyzeProfileHandler`)
- If in-flight: skip (debounce)
- No own account: skip + degrade
- **Non-blocking**: generation never waits; refresh happens in background
- **Failure isolation**: exception caught, warning logged, generation continues

**AestheticFreshness** Constant:
- `DefaultStalenessThresholdDays = 30` (system default)

### WU-4: Application — Processor Wiring + Context + Guidance Hints

**ContentGenerationJobProcessor** (Pre-Loop Injection):
- Calls `EnsureFreshAsync(orgId, clientId)` when policy == `ClientAesthetic` (fire-and-forget)
- Reads `GetProfileAsync(orgId, clientId)` → surfaces enriched profile
- Threads `ClientAestheticProfile` into `OrchestratorRequest`
- Threads `BackgroundDescriptors` into `BackgroundPlanRequest` (via `BackgroundSelection.ClientAesthetic`)

**GenerationContext** Extended:
- Added `ClientAestheticProfile? AestheticProfile` property
- Built from `OrchestratorRequest.AestheticProfile` in `ContentPackageOrchestrator.RunAsync`

**AestheticGuidance** (Prompt Helpers):
- `GenerationPromptContext.AestheticGuidance(profile?) → string`
- Renders voice tone + avg-slides hint (clamped 3–10 for rendering only; original stat unclamped)
- Null profile → empty string; null avg → only voice section; null voice → only avg section
- Null avg silently omitted

**Copy Generators** (Carousel, Story, FeedPost):
- Append `AestheticGuidance(ctx.AestheticProfile)` AFTER `ConcisionConstraint`
- Aesthetic guidance is ADDITIVE; concision text preserved
- Only `CarouselGenerator` renders avg-slides hint (carousel-only advisory)

**Prompt Versions** (Cache Invalidation):
- Bumped for `Carousel`, `Story`, `FeedPost` generators (WU-4.10, `ContentGenerationPromptVersions`)
- Ensures first-run recompute when cache exists

### WU-5: Infrastructure — Adapters + Metrics + Reader + Repository

**ApifyProfileScrapeAdapter** (`MapSinglePost`):
- Captures `childrenCount` into `ProfilePost.CarouselChildrenCount` (best-effort, null if absent)
- Tolerant of missing fields

**ApifyProfileScrapeResolver** (~line 269):
- Raised `maxPostsPerProfile` from 12 → 18 (wider aesthetic signal)
- Test confirms value (task 5.3, ApifyProfileScrapeResolverTests.cs:556–559)
- **Note**: Code still defaults to 12 for a separate per-client configurable `Client.MaxPostsToAnalyze` (intentional supersession by later change, documented in test)

**ProfileMetricCalculator**:
- Computes `AverageSlidesPerCarousel` from non-null `ProfilePost.CarouselChildrenCount` values
- Mean, rounded to 1 d.p.; nulls excluded; all-null → null result
- No external call (pure server stat)

**EfClientAestheticProfileReader** (Renamed from `EfClientAestheticDescriptorReader`):
- Implements `IClientAestheticProfileReader.GetProfileAsync`
- Deserializes `ai_analysis_json` (detectedBackgroundImages → descriptors, voiceTone)
- Deserializes `server_derived_stats_json` (averageSlidesPerCarousel)
- Projects `CompletedAtUtc` → `AnalyzedAtUtc` (freshness provenance)
- Returns `null` when no Analyzed row

**EfProfileAnalysisRepository** Extended:
- Implements `GetLatestAnalyzedCompletedAtAsync` (MAX `CompletedAtUtc` for Analyzed rows)
- Implements `HasNonTerminalAnalysisForClientAsync` (Requested/Scraping/Analyzing exists)

**DI Registration**:
- `EfClientAestheticProfileReader` → `IClientAestheticProfileReader` (replacing old descriptor reader)
- `ClientAestheticEnsureFreshService` → `IClientAestheticEnsureFreshService`

### WU-6: Infrastructure — EF Persistence + Migration

**SocialClientData** (Entity):
- Added `short? AestheticRefreshThresholdDays` property (nullable smallint)

**EF Mapping** (AppIdentityDbContext):
- Mapped as nullable smallint column `aesthetic_refresh_threshold_days`

**EF Migration** (`20260621100602_Add_AestheticRefreshThresholdDays`):
- Additive: `ALTER TABLE social.clients ADD COLUMN aesthetic_refresh_threshold_days smallint NULL`
- No data migration; existing rows remain null
- Reversible: standard DROP COLUMN

**EfSocialClientRepository** (Round-Trip Mapping):
- `ToData`: maps `client.AestheticRefreshThresholdDays` → entity field
- `Update`: explicit body assignment (predecessor bug guard)
- `ToDomain`/`Restore`: maps entity field back to domain

**Round-Trip Test** (task 6.4): verified null, set value 14, update to 7, reload → all three mapping methods exercised

### WU-7: API — Client ABM Endpoints

**SocialClientResponse**:
- Added `int? AestheticRefreshThresholdDays` field

**CreateSocialClientCommand** + Handler:
- Accepts `int? AestheticRefreshThresholdDays`
- Calls `client.SetAestheticRefreshThresholdDays(cmd.AestheticRefreshThresholdDays, now)` after creation

**UpdateSocialClientCommand** + Handler:
- Accepts `int? AestheticRefreshThresholdDays`
- Calls setter on loaded client

**MapSocialClientsEndpoints**:
- Binds `aestheticRefreshThresholdDays` in Create and Update route handlers

**Validation**: Invalid values (0, 366) → 400 Bad Request

### WU-8: Web — Client ABM Form

**social-clients.ts** (API lib):
- Added `aestheticRefreshThresholdDays?: number | null` to `SocialClient` and `SocialClientPayload` types

**use-client-form.ts** (Hook):
- Wired `aestheticRefreshThresholdDays` field (nullable number, defaults null)
- Mirrors `backgroundSourcingPolicy` field handling

**client-form-panel.tsx** (Component):
- Added optional numeric input for threshold (null label = "system default")
- Mirrors policy field UI pattern

**Build**: Frontend build + type-check passes

### WU-9: ADR + Docs Sync (Performed at Archive)

**ADR 0006** (`docs/architecture/adr/0006-client-aesthetic-ensure-fresh-port.md`):
- Documents new `IClientAestheticEnsureFreshService` port family
- Explains `IClientAestheticProfileReader` contract shape change (replacing old descriptor reader)
- Details `Client.AestheticRefreshThresholdDays` per-client threshold (1–365d, null = 30d default)
- Non-blocking fire-and-forget refresh guarantee
- Aesthetic guidance coexistence with ConcisionConstraint

**Spec Merges**:
- `openspec/specs/content-generation/spec.md`: appended CAG-CG-01 through CAG-CG-11 + CAG-CS-01–03
- `openspec/specs/profile-analysis/spec.md`: appended CAG-PA-01–05
- `openspec/specs/background-sourcing-policy/spec.md`: appended CAG-BSP-04-a through CAG-BSP-04-f (modified CASE C behavior)

---

## Test Results & Verification

| Suite | Passed | Failed | Status |
|-------|--------|--------|--------|
| Backend (.NET) | 1774 | 0 | PASS |

**Test Coverage** (Task 6.7 + per-WU tasks):
- WU-1 domain tests: GREEN
- WU-2 application VO + port tests: GREEN
- WU-3 service tests (freshness, debounce, own-account, isolation): GREEN
- WU-4 processor wiring + guidance tests: GREEN
- WU-5 adapter + metric + reader + repository tests: GREEN
- WU-6 EF round-trip mapping tests: GREEN (all three mapping methods)
- WU-7 API endpoint tests: GREEN
- WU-8 web build + type-check: GREEN
- Full regression suite: GREEN (1774 total)

**Verification Criteria**:

| Criterion | Status | Notes |
|-----------|--------|-------|
| Non-blocking refresh: generation returns immediately | PASS | WU-3 isolation + WU-4 processor tests |
| Freshness check against per-client threshold | PASS | WU-3 freshness tests (30d default, custom override) |
| Stale/missing analysis still generates (degrade) | PASS | WU-3 own-account + WU-4 processor tests |
| Refresh enqueued fire-and-forget (no blocking) | PASS | WU-3 service + WU-4 isolation tests |
| ClientAestheticProfile round-trips in all three EF methods | PASS | WU-6 round-trip test (create → update → reload) |
| Aesthetic guidance additive to ConcisionConstraint | PASS | WU-4 guidance tests (both texts present in prompt) |
| avg-slides clamped 3–10 ONLY in rendered hint | PASS | WU-4 guidance tests (original stat unclamped) |
| Copy generators receive caption voice | PASS | WU-4 guidance tests (all three generators) |
| Prompt versions bumped | PASS | WU-4 version-bump test |
| Architecture gate (no EF/Apify in Domain/Application) | PASS | No new imports; ports have no provider types |
| `maxPostsPerProfile == 18` confirmed | PASS | Task 5.3 adapter test; noted supersession by later change |

**Note on maxPostsPerProfile**: Code still shows 12 as default in ApifyProfileScrapeResolver because a separate later change introduced per-client configurable `Client.MaxPostsToAnalyze` (intentional) with system default 12. This supersession is documented in test comments (ApifyProfileScrapeResolverTests.cs:556–559).

---

## ADR & Documentation

- `docs/architecture/adr/0006-client-aesthetic-ensure-fresh-port.md` — documents port family, contract changes, refresh semantics, guidance patterns
- Merged into main `openspec/specs/` (content-generation, profile-analysis, background-sourcing-policy)

---

## Deferred Work (Phase 2 & Beyond)

| Phase | Scope | Status | Notes |
|-------|-------|--------|-------|
| 1 (THIS) | Active refresh-if-stale + richer profile + server metrics + copy guidance | **COMPLETE** | Shipped to production |
| 2 | Real client background IMAGES (Approach B) | DEFERRED | Gated on Apify actor spike confirming `displayUrl`/`displayResources` availability AND URL-durability decision |
| 3 | Post CAPTION capture for deeper copy inference | DEFERRED | Another Apify mapping gap (separate actor field) |

**Phase 1 Model Forward-Compat**: `ClientAestheticProfile` designed with nullable fields ready for Phase 2 image URLs; no breaking change when images slot in.

---

## Live Production State

- **Deployed**: 2026-06-21
- **In Use**: Non-blocking refresh orchestration active; per-client thresholds live in ABM
- **Test Suite**: 1774 passed (regression suite includes full coverage of all WUs)
- **No Regressions**: Existing clients (null threshold) use 30-day default seamlessly

---

## Archive Contents

- `proposal.md` — settled decisions, scope, approach, risks, rollback
- `design.md` — technical decisions, orchestration logic, data flow, test strategy
- `tasks.md` — 9 work units, all marked complete
- `specs/` — three spec deltas (content-generation, profile-analysis, background-sourcing-policy)
- Archive report — this file

**Moved from**: `openspec/changes/content-aesthetic-generation/`  
**Moved to**: `openspec/changes/archive/2026-08-25-content-aesthetic-generation/`

---

## Key Learnings

1. Non-blocking fire-and-forget refresh (enqueue without awaiting) trades immediate generation feedback for eventual freshness convergence; pairs perfectly with best-available degradation (fresh > stale > degrade).

2. Per-client threshold (nullable int with system default) is more maintainable than per-org config: each client can be tuned independently without re-deploying infrastructure config.

3. Aesthetic guidance as a pure function `AestheticGuidance(profile?) → string` (append-to-prompt pattern) is easier to test and compose than embedding logic in generator classes; coexistence with concision is mechanical when both are pure text hints.

4. Server-derived stats (AverageSlidesPerCarousel from `childrenCount` counts) are safer than AI-extracted signals; they are deterministic, cost-free, and require no prompt-version bump for future hint refinements.

5. Forward-compat design with nullable fields in value objects (`ClientAestheticProfile` + future image URLs in Phase 2) avoids breaking changes; test suite documents the gap explicitly so a later implementer knows exactly where to slot new fields.

---

*Archive report generated 2026-08-25.*  
*SDD cycle complete. Change is archived.*
