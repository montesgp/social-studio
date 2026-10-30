# Tasks: content-aesthetic-generation

**Branch:** `feat/content-format-taxonomy` — single branch, no new PR branches. All tasks committed on this branch; the human opens one PR at the end.
**TDD mode:** STRICT — `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj`. Every behavior task: RED → GREEN. Make the test fail first, then write the implementation.

---

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1,100–1,500 |
| 400-line budget risk | **High** |
| Chained PRs recommended | **No — single branch, human decides when to PR** |
| Delivery constraint | Single branch; work organized into independent Work Units (WUs) for reviewable diff sections |
| Chain strategy | N/A — single branch delivery |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: size-exception
400-line budget risk: High

### Suggested Work Units (for diff navigation, not separate branches)

| WU | Goal | Seam order | Notes |
|----|------|------------|-------|
| WU-1 | Domain — additive fields on `ProfilePost`, `ProfileStats`, `Client` | First — all subsequent WUs depend on these types | Pure domain, no infra |
| WU-2 | Application — `ClientAestheticProfile` VO + `IClientAestheticProfileReader` port (rename) + `IProfileAnalysisRepository` freshness/debounce methods | After WU-1 | Contract change; single caller compile-enforced |
| WU-3 | Application — `IClientAestheticEnsureFreshService` port + `ClientAestheticEnsureFreshService` impl (test-first risky piece) | After WU-2 | Core new logic; 6 test scenarios ordered |
| WU-4 | Application — Processor pre-loop wiring + `OrchestratorRequest`/`GenerationContext`/`GenerationPromptContext` threading + generator hints (test-first) | After WU-2 | Risky; additive to `ConcisionConstraint` |
| WU-5 | Infrastructure — Apify `childrenCount` mapping + `maxPostsPerProfile=18` + `ProfileMetricCalculator` avg-slides + `EfClientAestheticProfileReader` + `EfProfileAnalysisRepository` freshness/debounce + DI wiring | After WU-2; parallel with WU-3/4 if needed | Adapter-only; no migration |
| WU-6 | Infrastructure — `SocialClientData` column + `EfSocialClientRepository` all-three-method mapping + EF migration + regression round-trip test | After WU-1 domain settles | One small additive migration |
| WU-7 | Api — `SocialClientResponse` + Create/Update commands + `MapSocialClientsEndpoints` for `AestheticRefreshThresholdDays` | After WU-6 | Mirrors `BackgroundSourcingPolicy` pattern |
| WU-8 | Web — `social-clients.ts` types + `use-client-form.ts` + `client-form-panel.tsx` threshold field | After WU-7 | Mirrors policy field pattern |
| WU-9 | ADR + docs sync + spec merge + `state.yaml` update + final gates | Last | Cross-cutting |

> **TDD ordering note:** WU-3 and WU-4 tasks are written in the test-first sequence from design §9. Within each WU, RED tasks MUST precede their GREEN counterparts before moving to the next group.

---

## WU-1 — Domain: Additive Fields (foundation for everything else)

*Refs: CAG-PA-01, CAG-PA-02, CAG-CS-01; design §2.1.*

- [x] 1.1 **[RED]** Add `ProfilePostChildrenCountTests.cs` — assert `ProfilePost` exposes `CarouselChildrenCount int?`; assert it is null for non-carousel posts; assert construction succeeds with null. (`SocialStudio.Tests/Domain/Social/ProfileAnalysis/ProfilePostChildrenCountTests.cs`)
- [x] 1.2 **[GREEN]** Modify `ProfilePost.cs` — add `int? CarouselChildrenCount` as an additive nullable property; update `Create`/`Restore` (default null, no breaking change). (`SocialStudio.Domain/Social/ProfileAnalysis/ProfilePost.cs`)
- [x] 1.3 **[RED]** Add `ProfileStatsAverageSlidesTests.cs` — assert `ProfileStats` exposes `AverageSlidesPerCarousel double?`; assert construction with null value succeeds. (`SocialStudio.Tests/Domain/Social/ProfileAnalysis/ProfileStatsAverageSlidesTests.cs`)
- [x] 1.4 **[GREEN]** Modify `ProfileStats.cs` — add `double? AverageSlidesPerCarousel` nullable property. (`SocialStudio.Domain/Social/ProfileAnalysis/ProfileStats.cs`)
- [x] 1.5 **[RED]** Add `ClientAestheticThresholdTests.cs` — assert `Client.AestheticRefreshThresholdDays` is nullable `int?`; assert `SetAestheticRefreshThresholdDays(14, now)` sets the value; assert `SetAestheticRefreshThresholdDays(null, now)` clears to null; assert `0` and `366` values fail domain validation; assert `Restore` with null threshold works (back-compat). (`SocialStudio.Tests/Domain/Social/Clients/ClientAestheticThresholdTests.cs`)
- [x] 1.6 **[GREEN]** Modify `Client.cs` — add `int? AestheticRefreshThresholdDays`; add `SetAestheticRefreshThresholdDays(int? days, DateTime nowUtc)` setter (mirrors `SetBackgroundSourcingPolicy`); update `Create`/`Restore` signatures with nullable default; validate min 1 / max 365 in setter. (`SocialStudio.Domain/Social/Clients/Client.cs`)
- [x] 1.7 Run `dotnet test` — WU-1 domain tests green; no regression.

---

## WU-2 — Application: Profile VO + Reader Rename + Repository Freshness/Debounce Methods

*Refs: CAG-CG-05, CAG-PA-04, CAG-PA-05; design §3.1, §3.2, §3.4.*

- [x] 2.1 **[RED]** Add `ClientAestheticProfileTests.cs` — assert all four fields present (descriptors, VoiceTone, AverageSlidesPerCarousel, AnalyzedAtUtc); assert null-analysis shape (empty descriptors, all nulls); assert Phase 2 forward-compat (a nullable `BackgroundImageUrls` can be added later without breaking existing construction). (`SocialStudio.Tests/Application/Social/ContentGeneration/Shared/ClientAestheticProfileTests.cs`)
- [x] 2.2 **[GREEN]** Create `ClientAestheticProfile.cs` — sealed record with four fields; no Infrastructure types. (`SocialStudio.Application/Features/Social/ContentGeneration/Shared/ClientAestheticProfile.cs`)
- [x] 2.3 **[RED]** Add `IClientAestheticProfileReaderContractTests.cs` — reflect the interface; assert no Infrastructure/EF/Npgsql types in its signature; assert `GetProfileAsync(orgId, clientId, ct) → ClientAestheticProfile?` (replaces old `GetDescriptorsAsync`). (`SocialStudio.Tests/Application/IClientAestheticProfileReaderContractTests.cs`)
- [x] 2.4 **[GREEN]** Create `IClientAestheticProfileReader.cs`; rename/replace `IClientAestheticDescriptorReader.cs`; update the single call site in `ContentGenerationJobProcessor.cs` to reference the new interface (compiler-enforced). (`SocialStudio.Application/Abstractions/Social/ContentGeneration/IClientAestheticProfileReader.cs`)
- [x] 2.5 **[RED]** Add `IProfileAnalysisRepositoryFreshnessTests.cs` — assert `IProfileAnalysisRepository` now declares `GetLatestAnalyzedCompletedAtAsync(orgId, clientId, ct) → Task<DateTime?>` and `HasNonTerminalAnalysisForClientAsync(orgId, clientId, ct) → Task<bool>`; assert no Infrastructure types in signatures. (`SocialStudio.Tests/Application/IProfileAnalysisRepositoryFreshnessTests.cs`)
- [x] 2.6 **[GREEN]** Modify `IProfileAnalysisRepository.cs` — add the two new method signatures. (`SocialStudio.Application/Abstractions/Social/IProfileAnalysisRepository.cs`)
- [x] 2.7 **[GREEN]** Create `IClientAestheticEnsureFreshService.cs` + `EnsureFreshOutcome` enum — port interface (no implementation yet). (`SocialStudio.Application/Abstractions/Social/ContentGeneration/IClientAestheticEnsureFreshService.cs`)
- [x] 2.8 Run `dotnet test` — WU-2 tests green; compiler confirms single `IClientAestheticDescriptorReader` call site updated.

---

## WU-3 — Application: EnsureFreshService (risky, test-first in design §9 order)

*Refs: CAG-CG-01, CAG-CG-02, CAG-CG-03, CAG-CG-04, CAG-CG-10; design §4.*

**CRITICAL: write each RED test and verify it fails BEFORE writing the GREEN implementation.**

- [x] 3.1 **[RED — design §9 row 3a]** Add `EnsureFreshServiceFreshnessTests.cs` — assert null threshold → effective threshold = 30d; analysis at `UtcNow - 30d` → `Fresh`, no enqueue; analysis at `UtcNow - 30d - 1s` → `RefreshEnqueued`; custom threshold 7d, analysis at `UtcNow - 10d` → `RefreshEnqueued` (custom wins); custom 60d, analysis at `UtcNow - 60d` → `Fresh`. (`SocialStudio.Tests/Application/Social/ContentGeneration/Background/EnsureFreshServiceFreshnessTests.cs`)
- [x] 3.2 **[RED — design §9 row 4]** Add `EnsureFreshServiceDebounceTests.cs` — assert non-terminal analysis exists → `SkippedInFlight`, `IProfileAnalysisJobQueue.EnqueueAsync` NOT called; two clients debounced independently. (`SocialStudio.Tests/Application/Social/ContentGeneration/Background/EnsureFreshServiceDebounceTests.cs`)
- [x] 3.3 **[RED — design §9 row 5]** Add `EnsureFreshServiceOwnAccountTests.cs` — assert no own account → `SkippedNoOwnAccount`, no enqueue, no exception, warning logged; own account found and analysis stale → `RefreshEnqueued`, `GetAllByClientAsync` called. (`SocialStudio.Tests/Application/Social/ContentGeneration/Background/EnsureFreshServiceOwnAccountTests.cs`)
- [x] 3.4 **[RED — design §9 row 6]** Add `EnsureFreshServiceIsolationTests.cs` — mock `IProfileAnalysisJobQueue.EnqueueAsync` throws; assert exception is swallowed (not re-thrown), warning logged, method returns without propagating. (`SocialStudio.Tests/Application/Social/ContentGeneration/Background/EnsureFreshServiceIsolationTests.cs`)
- [x] 3.5 **[GREEN]** Create `ClientAestheticEnsureFreshService.cs` — implement `EnsureFreshAsync` per design §4 pseudocode; depends on `IProfileAnalysisRepository`, `ISocialClientRepository`, `IClientOwnAccountRepository`, `IUnitOfWork`, `IClock`, `IProfileAnalysisJobQueue`, `ILogger`; mirrors `AnalyzeProfileHandler` create→persist→enqueue pattern. (`SocialStudio.Application/Features/Social/ContentGeneration/Background/ClientAestheticEnsureFreshService.cs`)
- [x] 3.6 Create `AestheticFreshness.cs` — `public static class AestheticFreshness { public const int DefaultStalenessThresholdDays = 30; }`. (`SocialStudio.Application/Features/Social/ContentGeneration/Shared/AestheticFreshness.cs`)
- [x] 3.7 Run `dotnet test` — all WU-3 service tests green (freshness, debounce, own-account, isolation).

---

## WU-4 — Application: Processor Wiring + Context Threading + Generator Hints (risky, test-first)

*Refs: CAG-CG-06, CAG-CG-07, CAG-CG-08, CAG-CG-09, CAG-CG-11; design §5, §6.*

- [x] 4.1 **[RED — design §9 row 7]** Add `ProcessorAestheticWiringTests.cs` — mock `IClientAestheticEnsureFreshService` and `IClientAestheticProfileReader`; assert when `policy == ClientAesthetic`: `EnsureFreshAsync` called once, `GetProfileAsync` called once, profile threaded into `OrchestratorRequest.AestheticProfile`, descriptors threaded into `BackgroundPlanRequest.ClientAestheticDescriptors`; assert non-CaseC: neither called. (`SocialStudio.Tests/Application/Social/ContentGeneration/Pipeline/ProcessorAestheticWiringTests.cs`)
- [x] 4.2 **[RED — design §9 row 6 exception isolation]** Add `ProcessorEnsureFreshIsolationTests.cs` — `EnsureFreshAsync` throws; assert generation continues, warning logged, job does NOT fail. (`SocialStudio.Tests/Application/Social/ContentGeneration/Pipeline/ProcessorEnsureFreshIsolationTests.cs`)
- [x] 4.3 **[GREEN]** Modify `OrchestratorContracts.cs` — add `ClientAestheticProfile? AestheticProfile` to `OrchestratorRequest`. (`SocialStudio.Application/Features/Social/ContentGeneration/Orchestration/OrchestratorContracts.cs`)
- [x] 4.4 **[GREEN]** Modify `GenerationContext.cs` — add `ClientAestheticProfile? AestheticProfile` property; build from `OrchestratorRequest.AestheticProfile` in `ContentPackageOrchestrator.RunAsync`. (`SocialStudio.Application/Features/Social/ContentGeneration/Shared/GenerationContext.cs`, `SocialStudio.Application/Features/Social/ContentGeneration/Orchestration/ContentPackageOrchestrator.cs`)
- [x] 4.5 **[GREEN]** Modify `ContentGenerationJobProcessor.cs` — pre-loop: add `try { await ensureFresh.EnsureFreshAsync(orgId, clientId) } catch → log`; `aestheticProfile = await profileReader.GetProfileAsync(orgId, clientId)`; thread `AestheticProfile` into `OrchestratorRequest`; thread `BackgroundDescriptors` into `BackgroundPlanRequest`; inject `IClientAestheticEnsureFreshService` + `IClientAestheticProfileReader` via constructor. (`SocialStudio.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs`)
- [x] 4.6 **[RED — design §9 row 8]** Add `AestheticGuidanceTests.cs` — assert `GenerationPromptContext.AestheticGuidance(profile)` with VoiceTone+avg: guidance text contains voice tone and avg-slides (clamped); avg 12.5 → 10 in hint; avg 1.5 → 3 in hint; null avg → hint section absent; null profile → empty string; VoiceTone null → only avg section (if present); assert `ConcisionConstraint` text still present in assembled carousel prompt AND aesthetic guidance is ADDITIVE (no concision text removed). (`SocialStudio.Tests/Application/Social/ContentGeneration/Shared/AestheticGuidanceTests.cs`)
- [x] 4.7 **[GREEN]** Modify `GenerationPromptContext.cs` — add `public static string AestheticGuidance(ClientAestheticProfile? p)` per design §6; clamp avg 3–10 only in the rendered hint. (`SocialStudio.Application/Features/Social/ContentGeneration/Shared/GenerationPromptContext.cs`)
- [x] 4.8 **[GREEN]** Modify `CarouselGenerator.cs`, `StoryGenerator.cs`, `FeedPostGenerator.cs` — append `AestheticGuidance(ctx.AestheticProfile)` AFTER `ConcisionConstraint`; only `CarouselGenerator` renders avg-slides hint (carousel-only advisory). (`SocialStudio.Application/Features/Social/ContentGeneration/Formats/Carousel/CarouselGenerator.cs`, `.../Story/StoryGenerator.cs`, `.../FeedPost/FeedPostGenerator.cs`)
- [x] 4.9 **[RED]** Add `PromptVersionBumpAestheticTests.cs` — snapshot-assert that all three generators whose prompts changed have version constants incremented beyond their pre-change values (CAG-CG-11). (`SocialStudio.Tests/Application/Social/ContentGeneration/Shared/PromptVersionBumpAestheticTests.cs`)
- [x] 4.10 **[GREEN]** Modify `ContentGenerationPromptVersions.cs` — bump version constants for `Carousel`, `Story`, and `FeedPost` generators. (`SocialStudio.Application/Features/Social/ContentGeneration/Shared/ContentGenerationPromptVersions.cs`)
- [x] 4.11 Run `dotnet test` — WU-4 wiring, isolation, guidance, and version-bump tests green.

---

## WU-5 — Infrastructure: Adapters, Metric, Reader, Repository Freshness, DI

*Refs: CAG-PA-01, CAG-PA-02, CAG-PA-03, CAG-PA-04, CAG-PA-05; design §2.3, §7.*

- [x] 5.1 **[RED — design §9 row 9]** Add/extend `ApifyProfileScrapeAdapterTests.cs` — assert `MapSinglePost` maps actor JSON `{ childrenCount: 6 }` → `ProfilePost.CarouselChildrenCount = 6`; assert absent `childrenCount` → null, no exception; assert non-carousel post → null. (`SocialStudio.Tests/Infrastructure/Social/ProfileAnalysis/ApifyProfileScrapeAdapterTests.cs`)
- [x] 5.2 **[GREEN]** Modify `InstagramProfileApifyScrapeAdapter.cs` `MapSinglePost` — best-effort `GetInt(postEl,"childrenCount")` with fallback `children`/`sidecar` array length; null when absent; assign `CarouselChildrenCount`. (`SocialStudio.Infrastructure/Social/Providers/Apify/InstagramProfileApifyScrapeAdapter.cs`)
- [x] 5.3 **[RED — design §9 row 10]** Add/extend `ApifyProfileScrapeResolverTests.cs` — assert `maxPostsPerProfile` parameter in the built actor request is 18. (`SocialStudio.Tests/Infrastructure/Social/ProfileAnalysis/ApifyProfileScrapeResolverTests.cs`)
- [x] 5.4 **[GREEN]** Modify `ApifyProfileScrapeResolver.cs` line ~269 — `maxPostsPerProfile: 12 → 18`. (`SocialStudio.Infrastructure/Social/Providers/ApifyProfileScrapeResolver.cs`)
- [x] 5.5 **[RED — design §9 row 1]** Add/extend `ProfileMetricCalculatorTests.cs` — assert `AverageSlidesPerCarousel`: mean of non-null carousel child counts rounded to 1 d.p.; nulls excluded from numerator + denominator; all null/empty → null result; no external call made (no AI). (`SocialStudio.Tests/Application/Social/ProfileAnalyzer/ProfileMetricCalculatorTests.cs`)
- [x] 5.6 **[GREEN]** Modify `ProfileMetricCalculator.cs` — compute `AverageSlidesPerCarousel` (mean over `ProfilePost.CarouselChildrenCount` where non-null, rounded to 1 d.p., null otherwise); persist to `ProfileStats.AverageSlidesPerCarousel`. (`SocialStudio.Application/Features/Social/ProfileAnalyzer/Analyze/ProfileMetricCalculator.cs`)
- [x] 5.7 **[RED — design §9 row 2]** Add `EfClientAestheticProfileReaderTests.cs` — assert `GetProfileAsync` returns all four fields from latest `Analyzed` record (`DetectedBackgroundImages` → descriptors, `voiceTone`, `AverageSlidesPerCarousel`, `CompletedAtUtc` → AnalyzedAtUtc); assert null when no Analyzed row; assert most recent of two Analyzed rows selected; assert no Apify/EF type leaks into return value. (`SocialStudio.Tests/Infrastructure/Social/ContentGeneration/EfClientAestheticProfileReaderTests.cs`)
- [x] 5.8 **[GREEN]** Rename/modify `EfClientAestheticDescriptorReader.cs` → `EfClientAestheticProfileReader.cs` — implement `IClientAestheticProfileReader.GetProfileAsync`; deserialize `ai_analysis_json` (`detectedBackgroundImages`, `voiceTone`) and `server_derived_stats_json` (`averageSlidesPerCarousel`); project `CompletedAtUtc` → `AnalyzedAtUtc`; return `null` when no Analyzed row. (`SocialStudio.Infrastructure/Social/ContentGeneration/EfClientAestheticProfileReader.cs`)
- [x] 5.9 **[RED]** Add `EfProfileAnalysisRepositoryFreshnessTests.cs` — seeded EF context: assert `GetLatestAnalyzedCompletedAtAsync` returns MAX `CompletedAtUtc` for Analyzed rows; assert null when no Analyzed row; assert `HasNonTerminalAnalysisForClientAsync` returns true when Requested/Scraping/Analyzing row exists; returns false when only Analyzed/Failed rows exist. (`SocialStudio.Tests/Infrastructure/Persistence/EfProfileAnalysisRepositoryFreshnessTests.cs`)
- [x] 5.10 **[GREEN]** Modify `EfProfileAnalysisRepository.cs` — implement `GetLatestAnalyzedCompletedAtAsync` and `HasNonTerminalAnalysisForClientAsync`. (`SocialStudio.Infrastructure/Persistence/Postgres/EfProfileAnalysisRepository.cs`)
- [x] 5.11 **[GREEN]** Update `InfrastructureContentGenerationExtensions.cs` — register `EfClientAestheticProfileReader` → `IClientAestheticProfileReader` (replacing old descriptor reader registration); register `ClientAestheticEnsureFreshService` → `IClientAestheticEnsureFreshService`. (`SocialStudio.Infrastructure/Social/ContentGeneration/InfrastructureContentGenerationExtensions.cs`)
- [x] 5.12 Run `dotnet test` — WU-5 infra adapter, metric, reader, and freshness tests green.

---

## WU-6 — Infrastructure: EF Column + Repository Mapping + Regression Round-Trip

*Refs: CAG-CS-01; design §2.3, §7 migration verdict.*

- [x] 6.1 **[GREEN]** Modify `SocialClientData.cs` — add `short? AestheticRefreshThresholdDays` property. (`SocialStudio.Infrastructure/Persistence/Postgres/Entities/SocialClientData.cs`)
- [x] 6.2 **[GREEN]** Modify `AppIdentityDbContext.cs` — map `SocialClientData.AestheticRefreshThresholdDays` as column `aesthetic_refresh_threshold_days`, nullable smallint. (`SocialStudio.Infrastructure/Identity/AppIdentityDbContext.cs`)
- [x] 6.3 **[GREEN]** Generate EF migration `Add_AestheticRefreshThresholdDays` — `ALTER TABLE social.clients ADD COLUMN aesthetic_refresh_threshold_days smallint NULL`; no data migration needed; rollback = `DROP COLUMN`. (`SocialStudio.Infrastructure/Persistence/Postgres/Migrations/20260621100602_Add_AestheticRefreshThresholdDays.cs`)
- [x] 6.4 **[RED]** Add `EfSocialClientRepositoryThresholdTests.cs` — assert `ToData` maps `AestheticRefreshThresholdDays` to `SocialClientData`; assert `Update` method body assigns `entity.AestheticRefreshThresholdDays = client.AestheticRefreshThresholdDays` (predecessor bug guard: create client with value 14 → update to 7 → reload → asserts 7, not null); assert `ToDomain`/`Restore` restores the field; assert null round-trips correctly; ALL THREE repository methods exercised. (`SocialStudio.Tests/Infrastructure/Persistence/EfSocialClientRepositoryThresholdTests.cs`)
- [x] 6.5 **[GREEN]** Modify `EfSocialClientRepository.cs` — map `AestheticRefreshThresholdDays` in `ToData`, in the `Update` method body (explicit `entity.AestheticRefreshThresholdDays = client.AestheticRefreshThresholdDays`), and in `ToDomain`/`Restore`. (`SocialStudio.Infrastructure/Persistence/Postgres/EfSocialClientRepository.cs`)
- [x] 6.6 **[GREEN]** Extend `ContentStudioEntityMappingTests.cs` — add round-trip assertion for `aesthetic_refresh_threshold_days` nullable column on `SocialClientData`. (`SocialStudio.Tests/Infrastructure/Persistence/ContentStudioEntityMappingTests.cs`)
- [x] 6.7 Run `dotnet test` — WU-6 migration round-trip and mapping tests green; **1774 passed, 0 failed**.

---

## WU-7 — Api: `AestheticRefreshThresholdDays` on Client ABM Endpoints

*Refs: CAG-CS-02; design §2.3.*

- [x] 7.1 **[RED]** Add `SocialClientsEndpointThresholdTests.cs` — assert GET client response includes `aestheticRefreshThresholdDays`; assert CREATE with value 14 → response `aestheticRefreshThresholdDays = 14`; assert UPDATE to null → persisted null; assert UPDATE with value 0 → 400 Bad Request; assert UPDATE with 366 → 400 Bad Request. (`SocialStudio.Tests/Api/Social/SocialClientsEndpointThresholdTests.cs`)
- [x] 7.2 **[GREEN]** Modify `SocialClientResponse.cs` — add `int? AestheticRefreshThresholdDays`. (`SocialStudio.Application/Features/Social/Clients/**` or `ApiService/` — confirm exact location)
- [x] 7.3 **[GREEN]** Modify `CreateSocialClientCommand.cs` and handler — add `int? AestheticRefreshThresholdDays`; call `client.SetAestheticRefreshThresholdDays(cmd.AestheticRefreshThresholdDays, now)` after creation.
- [x] 7.4 **[GREEN]** Modify `UpdateSocialClientCommand.cs` and handler — add `int? AestheticRefreshThresholdDays`; call `client.SetAestheticRefreshThresholdDays` on the loaded client.
- [x] 7.5 **[GREEN]** Modify `MapSocialClientsEndpoints.cs` — bind `aestheticRefreshThresholdDays` in Create and Update route handlers. (`SocialStudio.ApiService/Endpoints/Social/MapSocialClientsEndpoints.cs`)
- [x] 7.6 Run `dotnet test` — WU-7 API endpoint tests green.

---

## WU-8 — Web: ABM Form Threshold Field

*Refs: CAG-CS-03; design §2.3.*

- [x] 8.1 **[GREEN]** Modify `apps/web/lib/api/social-clients.ts` — add `aestheticRefreshThresholdDays?: number | null` to `SocialClient` type and `SocialClientPayload`. (`apps/web/lib/api/social-clients.ts`)
- [x] 8.2 **[GREEN]** Modify `use-client-form.ts` — wire `aestheticRefreshThresholdDays` field (nullable number); defaults to `null`; mirrors `backgroundSourcingPolicy` field handling. (`apps/web/components/social/clients/use-client-form.ts`)
- [x] 8.3 **[GREEN]** Modify `client-form-panel.tsx` — add optional numeric input for `aestheticRefreshThresholdDays` (null = system default label); mirrors the policy field UI pattern. (`apps/web/components/social/clients/client-form-panel.tsx`)
- [x] 8.4 Frontend build + type-check must pass — `cd apps/web && npx tsc --noEmit` exits 0.

---

## WU-9 — ADR + Docs Sync + State Update + Final Gates

*Refs: CLAUDE.md docs-sync MUST; design §10, §13.*

- [x] 9.1 **[GREEN]** Author ADR `docs/architecture/adr/0006-client-aesthetic-ensure-fresh-port.md` — document: new `IClientAestheticEnsureFreshService` port family, `IClientAestheticProfileReader` contract shape change (replacing `IClientAestheticDescriptorReader`), `Client.AestheticRefreshThresholdDays` per-client threshold, non-blocking fire-and-forget refresh guarantee, additive aesthetic guidance coexistence with `ConcisionConstraint`. (`docs/architecture/adr/0006-client-aesthetic-ensure-fresh-port.md`)
- [x] 9.2 **[GREEN]** Merge spec deltas into canonical specs — append/update `openspec/specs/content-generation/spec.md` with CAG-CG-01 through CAG-CG-11 + CAG-CS-01–03; append/update `openspec/specs/profile-analysis/spec.md` with CAG-PA-01–05; append/update `openspec/specs/background-sourcing-policy/spec.md` with modified BSP-04 and CAG-BSP-04-a–f.
- [x] 9.3 **[GREEN]** Update `openspec/changes/content-aesthetic-generation/state.yaml` — set `status: apply-complete`; set `adr: docs/architecture/adr/0006-client-aesthetic-ensure-fresh-port.md`; populate `merged_specs` list.
- [ ] 9.4 Final gate run: `pwsh -File scripts/gates/Run-Gates.ps1` — deferred to user per branch/commit instructions. Report gate summary.

---

## Definition of Done

- [x] All WU-1..WU-9 tasks completed in seam order.
- [x] `dotnet test` passes with 0 failures; all new ports assert no Infrastructure types.
- [x] `Client.AestheticRefreshThresholdDays` mapped in `ToData`, `Update`, AND `ToDomain` of `EfSocialClientRepository` — regression round-trip test confirms no silent drop.
- [x] `aesthetic_refresh_threshold_days smallint NULL` column added to `social.clients` via EF migration; existing rows remain `NULL`.
- [x] `IClientAestheticDescriptorReader` fully replaced by `IClientAestheticProfileReader`; no dead import remains.
- [x] `EnsureFreshAsync` failure never fails the generation job (isolation test green).
- [x] `AestheticGuidance` is additive — `ConcisionConstraint` text present in same assembled prompt.
- [x] avg-slides clamped 3–10 ONLY in the rendered hint; `ProfileStats.AverageSlidesPerCarousel` stores unclamped value.
- [x] `maxPostsPerProfile == 18` confirmed by resolver test.
- [x] Prompt version constants bumped for all modified generators.
- [ ] Architecture gate passes: no EF/Npgsql/Apify types in Domain or Application. (deferred — `pwsh -File scripts/gates/Run-Gates.ps1` runs after PR)
- [x] ADR under `docs/architecture/adr/` documents new port family + reader contract change.
- [ ] `pwsh -File scripts/gates/Run-Gates.ps1` exits 0. (deferred to human — gate suite runs at PR time)
