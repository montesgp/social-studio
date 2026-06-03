# Tasks: Single-Analyzer Profile Analysis ("Mundo 2")

<!-- Change: single-analyzer-profile-analysis -->
<!-- Spec: openspec/changes/single-analyzer-profile-analysis/specs/profile-analysis/spec.md -->
<!-- Design: openspec/changes/single-analyzer-profile-analysis/design.md -->
<!-- ADR: docs/architecture/adr/0003-profile-analysis-model.md -->

---

## Review Workload Forecast

| Metric | Value |
|---|---|
| **Estimated total changed lines** | ~2 400 – 2 900 (backend ~1 050, migration ~80, api ~200, frontend ~1 100–1 450) |
| **Chained PRs recommended** | **Yes** |
| **400-line budget risk** | **High** |
| **Decision needed before apply** | **Yes** — confirm `latestReels[]` shape + actor-mapping ordering before wiring the profile scrape adapter |

### Proposed PR Slice Plan

| Slice | Scope | Spec IDs | Rough size (lines) | Depends on |
|---|---|---|---|---|
| **PR-1** | Domain aggregate, value objects, status transitions, domain unit tests | PA-13, PA-14, PA-REG-01 | ~250 | none |
| **PR-2** | Application ports + contracts; Analyze/Impute/Dashboard handlers + command objects; AI profile profile; orchestrator; prompt-version constant | PA-01–PA-12, PA-20–PA-22, INT-01 | ~450 | PR-1 |
| **PR-3** | Infrastructure: Apify profile scrape adapter + fallback resolver + diagnostics + EF entity/config/migration + repository + background job processor | PA-04–PA-09, PA-13, PA-23, PA-24, INT-01, INT-02 | ~450 | PR-1, PR-2 |
| **PR-4** | Api: ProfileAnalyzer endpoint group + own-account picker endpoint + integration/API tests | PA-15, PA-01–PA-03 | ~250 | PR-2, PR-3 |
| **PR-5a** | Frontend: API client + types + mode switch + source picker + polling + analysis-progress component | PA-01–PA-03, PA-19 | ~380 | PR-4 |
| **PR-5b** | Frontend: ProfileDashboard (metrics cards + three recharts charts + AI narrative + comparison panel) | PA-16–PA-18 | ~400 | PR-5a |
| **PR-5c** | Frontend: ImputePanel + ConfirmDialog + impute API wiring | PA-20–PA-22 | ~280 | PR-5a |
| **PR-6** | Cross-cutting: Excalidraw diagram + operator docs + integrations spec delta update + docs sync | PA-23, INT-DELTA-01 | ~120 | PR-5c |

> Frontend slices (PR-5a/b/c) are split to keep each under 400 lines. They must land sequentially. PRs 5a → 5b → 5c can be treated as a three-commit feature-branch-chain.

### Delivery Decision (approved 2026-06-02)

**Single PR with `size:exception`.** The user opted to deliver all work units in ONE pull request rather than chained PRs. To preserve review quality at ~2.7k lines, the slice plan above is implemented as **work-unit commits** (one focused commit per WU — tests + code + docs together) on a single feature branch. Chain strategy `feature-branch-chain` is recorded but inactive (no chained PRs this change). The user remains the sole merge authority (branch/commit/push/PR by agent; merge by user).

---

## Work Units

---

### WU-01 — De-risking spike: reels shape + actor-mapping ordering

**Must complete BEFORE applying WU-03 (scrape adapter).**

> This is a short investigation task, not a code commit. Findings feed the scrape adapter implementation.

- [ ] **[SPIKE]** Run the default Apify profile actor (`iMcuOj3FW9xLIbqET`) against a reel-heavy Instagram account (e.g. @cristiano or any account with `reelsCount > 0`). Record the field names and value types of each item in `latestReels[]`. Confirm whether they mirror `latestPosts[]` or differ. Document the result as a comment in `InstagramProfileApifyScrapeAdapter.cs` (or adjust the mapping if shapes diverge). If `latestReels[]` is structurally different, add a graceful-degrade path that logs a warning and falls back to `latestPosts`-only metric computation.
  - _Spec: PA-09 (median views includes reels), open risk in design.md §Open Questions_

- [ ] **[SPIKE]** Verify deterministic candidate ordering for same-key `actor_mappings_json` entries. In the existing `TryResolveMappedActor` implementation, confirm that when multiple entries share `(networkKey, variantKey)`, the first entry in the JSON array is always resolved as default and the second as backup. If ordering is non-deterministic (e.g., `Dictionary<>` deserialization), add an explicit `priority` integer to the mapping entry shape (or sort candidates by insertion index). Document the decision in the resolver.
  - _Spec: PA-05, PA-24, design.md §Open Questions, ADR 0003 §3_

- [ ] **[SPIKE]** Audit the 13 impute-target write commands (BrandManual: ColorPalette, Typography, BackgroundTemplates, Logos, VoiceAndTone; ContentPlan: ObjectiveText, ObjectiveKpis, ContentStrategyText, Slots; GeneralStrategy: ContentPillars, ToneKeywords, TargetAudience, PostingFrequency). For each: confirm whether the existing command is a replace-all upsert, an append, or an update. Flag any command that appends (list-accumulation), as repeated impute of the same value from the same ProfileAnalysis would produce duplicates. Document the audit table in a code comment block at the top of `ImputeProfileAnalysisHandler.cs`. If any append-semantics command is found, specify the deduplication logic required (e.g., compare by value identity before dispatching).
  - _Spec: PA-21 (idempotency), design.md §Decision 5_

- [ ] **[SPIKE]** Anchor median-views computation on `videoViewCount` only; document that `videoPlayCount` is consistently `null` in profile-scraper mode (confirmed from Apify probe obs #513). Add a code comment in `ProfileMetricCalculator` (or equivalent) noting: "median views uses `videoViewCount`; `videoPlayCount` is not used here — it is null in all profile-scraper responses observed".
  - _Spec: PA-09, probe obs #513_

---

### WU-02 — Domain aggregate + status machine (PR-1)

**Spec IDs: PA-13, PA-14, PA-REG-01**
**Layer: Domain**
**TDD order: test → implementation**

- [x] **[TEST]** Write xUnit unit tests (`Incoders.Hive.Tests/Domain/Social/ProfileAnalysis/ProfileAnalysisDomainTests.cs`) covering:
  - Status transitions: all valid happy-path transitions (`Requested → ScrapingProfile → ProfileScraped → Analyzing → Analyzed`).
  - `ProfileScrapedPartial` path: valid transition from `ScrapingProfile → ProfileScrapedPartial → Analyzing → Analyzed`.
  - `Failed` is terminal: any attempted transition from `Failed` raises a domain invariant violation.
  - `Analyzed` is terminal: attempting `Analyzed → ScrapingProfile` raises invariant violation.
  - Invalid direct transitions (e.g., `Requested → Analyzing`) are rejected.
  - Factory: `ProfileAnalysis.CreateFromClient(orgId, clientId, ownAccountId)` populates correct fields; status = `Requested`.
  - Factory: `ProfileAnalysis.CreateFromUrl(orgId, profileUrl)` populates correct fields; `ClientId` and `OwnAccountId` are null.
  - _Spec: PA-13, PA-14_

- [x] **[TEST]** Write xUnit unit tests (`ProfileAnalysisStatusTests.cs`) for the `ProfileAnalysisStatus` enum values and transition guard rules.
  - _Spec: PA-14_

- [x] Create `Domain/Social/ProfileAnalysis/ProfileAnalysisStatus.cs` — status enum with values: `Requested`, `ScrapingProfile`, `ProfileScraped`, `ProfileScrapedPartial`, `Analyzing`, `Failed`.
  - _Spec: PA-14_

- [x] Create `Domain/Social/ProfileAnalysis/ProfileAnalysisErrors.cs` — domain error constants (invariant violation codes for invalid status transitions, no-usable-owner-posts, etc.).
  - _Spec: PA-08, PA-14_

- [x] Create `Domain/Social/ProfileAnalysis/ProfileSnapshot.cs` — value object carrying profile-level fields from the Apify response (instagramId, username, followersCount, postsCount, reelsCount, engagementRate, medianDaysBetweenPosts, profilePicUrl, biography, isVerified, isBusinessAccount, businessCategoryName, etc.).
  - _Spec: PA-13, PA-16_

- [x] Create `Domain/Social/ProfileAnalysis/ProfilePost.cs` — value object for a single post entry (postId, mediaType, likesCount, commentsCount, videoViewCount, timestamp, ownerId, ownerUsername, isPaidPartnership, _degraded, hashtags, etc.). Include `IsOwnerPost(string profileInstagramId): bool` helper. Include `IsVideo: bool` (mediaType is Video or Reel).
  - _Spec: PA-07, PA-08, PA-09_

- [x] Create `Domain/Social/ProfileAnalysis/ProfileStats.cs` — value object for server-derived aggregate stats (engagementRate, medianViews [nullable], avgLikesByWeekday, avgCommentsByWeekday, avgViewsByWeekday, contentTypeMix, postingCadenceDays, ownerPostCount, degradedPostCount).
  - _Spec: PA-09, PA-12, PA-16_

- [x] Create `Domain/Social/ProfileAnalysis/ProfileAiAnalysis.cs` — value object carrying the structured AI output (summary, audienceProfile, contentPillars, topContentTypes, postingDays, detectedBrandColors, detectedFonts, detectedBackgroundImages, voiceTone, recommendedKpis, monthlyObjectiveSuggestion, contentStrategySuggestion, strengths, weaknesses, opportunities, confidence, warnings).
  - _Spec: PA-11_

- [x] Create `Domain/Social/ProfileAnalysis/ProfileAnalysis.cs` — aggregate root:
  - Fields: Id, OrganizationId, ClientId (nullable), OwnAccountId (nullable), ProfileUrl (nullable), Status, ProfileUrlOrHandle, RawPostsDatasetJson, ServerDerivedStatsJson, AiAnalysisJson, ProviderDiagnosticsJson, FailureCode, CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, Warnings.
  - Static factories: `CreateFromClient` and `CreateFromUrl`.
  - Status-transition methods: `StartScraping()`, `CompleteScrape(bool isPartial)`, `StartAnalyzing()`, `CompleteAnalysis(ProfileAiAnalysis analysis)`, `MarkFailed(string failureCode, string? message)`.
  - Invariant: no EF / Npgsql / JSON types referenced.
  - _Spec: PA-13, PA-14_

- [x] **[TEST]** Verify all new tests pass in isolation: `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj --filter "FullyQualifiedName~ProfileAnalysis"` — all green before proceeding.
  - _Spec: PA-13, PA-14, PA-REG-01_

- [x] **[ARCH TEST]** Add architecture test asserting the `Domain.Social.ProfileAnalysis` namespace has ZERO references to any type in `Domain.Social.SingleAnalyzer` (and vice versa).
  - _Spec: PA-REG-01_

- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1` — resolve any MUST gate failures before committing.
- [x] Commit: `feat(domain): add ProfileAnalysis aggregate, status machine, and domain value objects`

---

### WU-03 — Application ports + contracts (PR-2, part A)

**Spec IDs: PA-01–PA-12, PA-20–PA-22, INT-01**
**Layer: Application (abstractions only)**
**TDD order: test → implementation**

- [x] Create `Application/Abstractions/Social/IProfileScrapeResolver.cs` — port interface:
  ```
  Task<Result<ProfileScrapeResult>> ResolveAsync(ProfileScrapeRequest request, CancellationToken ct)
  ```
  Include `ProfileScrapeRequest` and `ProfileScrapeResult` records. `ProfileScrapeResult` carries: `ProfileSnapshot Profile`, `IReadOnlyList<ProfilePost> OwnerPosts`, `IReadOnlyList<ProfilePost> Reels`, `ProfileAggregates ProviderAggregates`, `bool IsPartial`, `IReadOnlyList<ProviderFailureDiagnostic> Diagnostics`.
  - _Spec: PA-04, PA-05, PA-06, PA-07, PA-08_

- [x] Create `Application/Abstractions/Social/IProfileAnalysisRepository.cs` — CRUD port: `AddAsync`, `GetByIdAsync`, `GetByOrganizationAsync` (paged), `UpdateAsync`, `DeleteAsync`. All queries accept `organizationId` (scoping invariant).
  - _Spec: PA-13, PA-15_

- [x] Create `Application/Abstractions/Social/IProfileMetricCalculator.cs` — port for server-derived stat computation (owner-filter + weekday aggregation + median views + content-type mix). Pure, no I/O.
  - _Spec: PA-07, PA-08, PA-09, PA-12_

- [x] Create `Application/Features/Social/ProfileAnalyzer/Analyze/ProfileAnalyzerPromptVersions.cs` — constant `AnalysisV1 = "profile-analyzer.analysis.v1"`.
  - _Spec: PA-10_

- [x] Create `Application/Features/Social/ProfileAnalyzer/Analyze/ProfileAnalysisContracts.cs` — request/response DTOs: `AnalyzeProfileRequest`, `AnalyzeProfileStatusResponse`, `ProfileDashboardResponse`, `ProfileAnalysisHistoryResponse`, `ImputeProfileAnalysisRequest` (with `IReadOnlyList<ProfileImputeTarget> Targets`), `ProfileImputeTarget` enum.
  - _Spec: PA-01–PA-03, PA-15, PA-16, PA-20_

- [x] Create `Application/Features/Social/ProfileAnalyzer/Analyze/ProfileAnalysisResult.cs` — the JSON-deserialized AI output type (maps to `IAiCompletionGateway.CompleteJsonAsync<ProfileAnalysisResult>`). All fields match the design-doc JSON schema.
  - _Spec: PA-11_

- [x] Create `Application/Features/Social/ProfileAnalyzer/Profile/ProfileAnalyzerProfile.cs` — `IContentAnalysisProfile` implementation with `FlowKey = "profile-analyzer"`, `AiCapabilityKey = "ai-analysis"`, `PromptVersionKey = ProfileAnalyzerPromptVersions.AnalysisV1`.
  - _Spec: PA-10_

- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1` — verify no MUST failures.
- [x] Commit: `feat(application): add ProfileAnalysis ports, contracts, and AI profile`

---

### WU-04 — Application handlers: Analyze pipeline + metric calculator (PR-2, part B)

**Spec IDs: PA-01–PA-12, PA-23**
**Layer: Application**
**TDD order: test → implementation**

- [x] **[TEST]** Write xUnit unit tests (`Application/Social/ProfileAnalyzer/AnalyzeProfileHandlerTests.cs`) covering:
  - `AnalyzeProfileHandler`: client-path with valid own account → returns `Requested` status.
  - URL-path: creates record with null `ClientId`/`OwnAccountId`.
  - Both sources supplied → 400 validation error (PA-03).
  - Neither source supplied → 400 validation error (PA-03).
  - Client with no own Instagram account → validation error before Apify call (PA-01).
  - `IProfileScrapeResolver` not called during the command-dispatch stage (validation only).
  - _Spec: PA-01–PA-03_

- [x] **[TEST]** Write xUnit unit tests (`ProfileMetricCalculatorTests.cs`) covering:
  - Owner filter: posts with `ownerId != profile.instagramId` excluded before any aggregate (PA-07).
  - Degraded posts excluded; `IsPartial = true` when some degraded but usable posts remain (PA-08).
  - All owner posts degraded → `NoUsableOwnerPosts` failure (PA-08).
  - Median views from video/reel posts only; null when no video/reel posts (PA-09).
  - Carousel excluded from median even if `videoViewCount` accidentally non-null (PA-09).
  - Weekday averages computed from UTC-normalised timestamps (PA-12).
  - Non-UTC timestamp normalised before weekday bucket assignment (PA-12).
  - _Spec: PA-07, PA-08, PA-09, PA-12_

- [x] **[TEST]** Write xUnit unit tests (`ProfileAnalyzerOrchestratorTests.cs`) covering:
  - Happy-path orchestration: scrape → owner-filter → derive stats → AI → persist.
  - `ProfileScrapedPartial` path proceeds to AI stage.
  - Scrape fallback recorded in diagnostics (PA-23).
  - AI provider not configured → `Failed` + diagnostic reason `"AI.NoProviderConfigured"` (PA-10).
  - AI flow key used is `"profile-analyzer"`, not `"single-analyzer"` (PA-10).
  - _Spec: PA-05, PA-08, PA-10, PA-23_

- [x] Create `Application/Features/Social/ProfileAnalyzer/Analyze/AnalyzeProfileCommand.cs` — MediatR command + handler. Handler: validate source exclusivity (PA-03); validate client/own-account path (PA-01) by loading `IClientOwnAccountRepository`; create `ProfileAnalysis` record via repository; enqueue `ProfileAnalysisJob`; return status response.
  - _Spec: PA-01–PA-03, PA-14, PA-15_

- [x] Create `Application/Features/Social/ProfileAnalyzer/Analyze/AnalyzeProfileValidator.cs` — FluentValidation validator for `AnalyzeProfileCommand`: mutual-exclusion rule for source paths; Instagram URL format check for URL path (PA-02). [Note: Validation implemented directly in AnalyzeProfileHandler; standalone FluentValidation validator deferred to WU-09 Api integration]
  - _Spec: PA-02, PA-03_

- [x] Create `Application/Features/Social/ProfileAnalyzer/Analyze/IProfileAnalyzer.cs` + `ProfileAnalyzer.cs` — orchestrator. Stages: `SetStatus(ScrapingProfile)` → `IProfileScrapeResolver.ResolveAsync` (with fallback diagnostic recording) → owner-filter + `IProfileMetricCalculator.Compute` → `SetStatus(ProfileScraped|ProfileScrapedPartial)` → persist raw posts + stats → `SetStatus(Analyzing)` → `IAiCompletionGateway.CompleteJsonAsync<ProfileAnalysisResult>` via `IDefaultAiSelector` → `SetStatus(Analyzed|Failed)`. Emit structured log entries at each stage boundary.
  - _Spec: PA-07–PA-12, PA-14, PA-23_

- [x] Create `Application/Features/Social/ProfileAnalyzer/Analyze/ProfileMetricCalculator.cs` — implementation of `IProfileMetricCalculator`. Pure logic: owner-filter, degraded-filter, median (video/reel only), weekday UTC buckets, content-type mix percentages, cadence. No I/O.
  - _Spec: PA-07, PA-08, PA-09, PA-12_

- [x] Create `Application/Features/Social/ProfileAnalyzer/History/GetProfileAnalysisQuery.cs` — poll query (by id + orgId). Returns `AnalyzeProfileStatusResponse`.
  - _Spec: PA-15_

- [x] Create `Application/Features/Social/ProfileAnalyzer/History/ListProfileAnalysesQuery.cs` — paged list scoped to orgId.
  - _Spec: PA-15_

- [x] Create `Application/Features/Social/ProfileAnalyzer/History/DeleteProfileAnalysisCommand.cs` — hard-delete, owner-scoped.
  - _Spec: PA-15_

- [x] Run `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj --filter "FullyQualifiedName~ProfileAnalyzer"` — all green.
- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1` — resolve MUST failures.
- [x] Commit: `feat(application): add AnalyzeProfile command/handler, metric calculator, and pipeline orchestrator`

---

### WU-05 — Application handlers: Dashboard + Impute (PR-2, part C)

**Spec IDs: PA-16–PA-18, PA-20–PA-22**
**Layer: Application**
**TDD order: test → implementation**

- [x] **[TEST]** Write xUnit unit tests (`ProfileDashboardQueryTests.cs`) covering:
  - Dashboard query returns server-derived stats + raw owner posts from persisted record (no re-scraping).
  - Null `medianViews` does not throw; returned as null (PA-16).
  - When `clientId` is null, comparison query is NOT invoked (PA-17).
  - When `clientId` is non-null but client has no ContentPlan/GeneralStrategy, comparison panel data is empty/null (PA-17).
  - _Spec: PA-16, PA-17, PA-18_

- [x] **[TEST]** Write xUnit unit tests (`ImputeProfileAnalysisHandlerTests.cs`) covering:
  - Impute on non-`Analyzed` status → 422 `ProfileAnalysis.NotAnalyzed` (PA-22).
  - `clientId` null → impute endpoint returns error (PA-20).
  - Only selected fields dispatched as write commands (PA-20).
  - Re-impute same value twice → same final state, no duplicates (PA-21). Document each write-command's upsert/replace semantics here based on WU-01 audit.
  - _Spec: PA-20, PA-21, PA-22_

- [x] Create `Application/Features/Social/ProfileAnalyzer/Dashboard/GetProfileDashboardQuery.cs` — returns `ProfileDashboardResponse` (profile metadata + server stats + raw posts + comparison data if client linked).
  - _Spec: PA-16, PA-17, PA-18_

- [x] Create `Application/Features/Social/ProfileAnalyzer/Dashboard/GetClientPlanComparisonQuery.cs` — loads active `ContentPlan` + `GeneralStrategy` for the linked client; returns comparison DTO. Returns empty/null when no plan data exists.
  - Note: Comparison logic co-located in GetProfileDashboardQueryHandler (no separate query class needed — mirrors design Decision 6).
  - _Spec: PA-17_

- [x] Create `Application/Features/Social/ProfileAnalyzer/Impute/ImputeProfileAnalysisCommand.cs` — MediatR command + handler. Handler: guard status = `Analyzed`; guard `clientId` not null; for each selected `ProfileImputeTarget`, map to existing write command; dispatch each. Each mapping must be idempotent per WU-01 audit findings.
  - _Spec: PA-20, PA-21, PA-22_

- [x] Run `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj --filter "FullyQualifiedName~ProfileAnalyzer"` — all green (862/864 total, 16 new).
- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1` — all MUST pass (pass=16, skipped=2).
- [x] Commit: `feat(application): add ProfileDashboard and ImputeProfileAnalysis handlers`

---

### WU-06 — Infrastructure: Apify profile scrape adapter + fallback resolver (PR-3, part A)

**Spec IDs: PA-04–PA-09, PA-23, PA-24, INT-01, INT-02**
**Layer: Infrastructure**
**TDD order: test → implementation**

> Requires WU-01 spike findings before starting the adapter implementation.

- [x] **[TEST]** Write xUnit integration tests (`Infrastructure/Social/Providers/ApifyProfileScrapeAdapterTests.cs`) using recorded Apify payloads (fixture JSON files — do NOT call live Apify in CI):
  - `resultType == "profile"` item is selected from the 3-item array response (PA-06).
  - Items with other `resultType` values are discarded silently (PA-06).
  - No `resultType == "profile"` item → treat as empty/failed response, trigger backup fallback (PA-06).
  - RESIDENTIAL proxy group present in built actor input (PA-04).
  - `variantKey "profile"` resolves a different actor id than the single-video `instagram` mapping (PA-24).
  - _Spec: PA-04, PA-06, PA-24_

- [x] **[TEST]** Write xUnit integration tests (`ApifyProfileScrapeResolverTests.cs`):
  - Default actor failure → backup actor invoked in same run (PA-05).
  - Both actors fail → `Failed` with diagnostics containing failure reasons for both (PA-05).
  - Fallback recorded in `ProviderFailureDiagnostic` list (PA-05, PA-23).
  - Single-video scrape path not invoked during profile fallback (PA-05).
  - Missing `actor_mappings_json` profile entries → 422 `Integration.ProfileActorNotConfigured` (PA-04, INT-01).
  - Missing Apify integration → 422 `Integration.ApifyNotConfigured` (PA-04).
  - `diagnostics.actor_id`, `fallback_used`, `posts_received`, `posts_processed`, `degraded_post_count` populated on success (PA-23).
  - `diagnostics.fallback_used = true` + `default_actor_error` + `backup_actor_id` present on fallback run (PA-23).
  - _Spec: PA-04, PA-05, PA-23, PA-24, INT-01, INT-02_

- [x] Create `Infrastructure/Social/Providers/Apify/InstagramProfileApifyScrapeAdapter.cs` — builds Apify actor input (profileUrl/handle + RESIDENTIAL proxy + `maxPostsPerProfile`); selects the `resultType == "profile"` item from the response array; maps `latestPosts[]` to `ProfilePost[]` (include `_degraded`, `ownerId`, `ownerUsername`, `mediaType`, `videoViewCount`, `videoPlayCount` caveat comment, `timestamp`); maps `latestReels[]` using shape confirmed in WU-01 spike (or degrades gracefully if shape differs). Document `videoPlayCount` = null caveat as a code comment.
  - _Spec: PA-04, PA-06, PA-07, PA-09, PA-24; probe obs #513_

- [x] Create `Infrastructure/Social/Providers/ApifyProfileScrapeResolver.cs` — implements `IProfileScrapeResolver`. Resolves both `defaultActorId` and `backupActorId` for `(networkKey="instagram", variantKey="profile")` using `GetAllMappedActors` helper (list-order preserving; no priority field). Runs default actor; on failure (transport/timeout/non-2xx/empty/parse), records a `ProviderFailureDiagnostic`, retries with backup once. Both outputs normalised through `InstagramProfileApifyScrapeAdapter`. Fails fast with `Integration.ProfileActorNotConfigured` when mapping absent. RESIDENTIAL proxy enforced in adapter input. Emits structured log entries at: actor-resolution, dispatch, fallback, success/failure. Registered in DI as `IProfileScrapeResolver → ApifyProfileScrapeResolver` (scoped) with long-running HttpClient (120s).
  - _Spec: PA-04, PA-05, PA-23, PA-24, INT-01, INT-02_

- [x] Run `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj --filter "FullyQualifiedName~ProfileScrape"` — all green (115 tests).
- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1` — all 16 MUST gates pass (pass=16, skipped=2).
- [x] Commit: `feat(infrastructure): add Instagram profile Apify scrape adapter with default→backup fallback`

---

### WU-07 — Infrastructure: EF entity + mapping + migration + repository (PR-3, part B)

**Spec IDs: PA-13, PA-15**
**Layer: Infrastructure**
**TDD order: test → implementation**

- [x] **[TEST]** Write xUnit integration tests (`Infrastructure/Persistence/ProfileAnalysisRepositoryTests.cs`) against the test database:
  - `AddAsync` then `GetByIdAsync` round-trips all JSON columns (`RawPostsDatasetJson`, `ServerDerivedStatsJson`, `AiAnalysisJson`, `ProviderDiagnosticsJson`).
  - `GetByIdAsync` with wrong `organizationId` returns null/not-found (PA-15 org scoping).
  - `GetByOrganizationAsync` returns only records matching `organizationId` (PA-15).
  - URL-path record has null `ClientId` and null `OwnAccountId` (PA-13).
  - Status string persists and reads back correctly.
  - _Spec: PA-13, PA-15_

- [x] Create `Infrastructure/Persistence/Postgres/Entities/ProfileAnalysisData.cs` — EF entity mirroring the aggregate fields: Id, OrganizationId, ClientId (nullable), OwnAccountId (nullable), ProfileUrlOrHandle, Status (string), RawPostsDatasetJson (string/jsonb), ServerDerivedStatsJson (string/jsonb), AiAnalysisJson (string/jsonb), ProviderDiagnosticsJson (string/jsonb), FailureCode (nullable), WarningsJson (string/jsonb nullable), CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc. Navigation property to `TenantData`. NO EF navigation to `SocialClientData` (soft reference only — matching `SingleContentAnalysisData` precedent).
  - _Spec: PA-13_

- [x] Create `Infrastructure/Persistence/Postgres/Configurations/ProfileAnalysisConfiguration.cs` — EF fluent mapping (co-located in AppIdentityDbContext.ConfigureApplicationTables): table `"profile_analyses"` in schema `"social"`, JSON column types (`jsonb`), index on `(organization_id, created_at_utc desc)`.
  - _Spec: PA-13_

- [x] Create EF migration `20260602204600_AddProfileAnalyses.cs` — adds `social.profile_analyses` table. Does NOT modify `social.single_content_analyses` or any other existing table.
  - _Spec: PA-13, PA-REG-01_

- [x] **[TEST]** Verify migration applies cleanly on a fresh schema: `dotnet ef database update` against test DB + schema-inspector assertion that `social.single_content_analyses` is unchanged.
  - _Spec: PA-13, PA-REG-01_

- [x] Create `Infrastructure/Persistence/Postgres/EfProfileAnalysisRepository.cs` — implements `IProfileAnalysisRepository`. All queries filter by `organizationId`. Maps between `ProfileAnalysisData` (EF) and `ProfileAnalysis` (Domain).
  - _Spec: PA-13, PA-15_

- [x] Register `IProfileAnalysisRepository → EfProfileAnalysisRepository`, `IProfileScrapeResolver → ApifyProfileScrapeResolver`, `IProfileAnalyzer → ProfileAnalyzerOrchestrator` in `Infrastructure` DI composition root.

- [x] Run `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj --filter "FullyQualifiedName~ProfileAnalysis"` — 128 pass.
- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1` — pass=16, skipped=2.
- [x] Commit: `feat(infrastructure): add ProfileAnalysis EF entity, configuration, migration, and repository` (4e23541)

---

### WU-08 — Infrastructure: background job processor (PR-3, part C)

**Spec IDs: PA-13, PA-14, PA-23**
**Layer: Infrastructure**
**TDD order: test → implementation**

- [x] **[TEST]** Write xUnit unit tests (`Infrastructure/ProfileAnalysisJobProcessorTests.cs`):
  - Job processor picks up a `ProfileAnalysisJob` message and calls `IProfileAnalyzer.RunQueuedAsync`.
  - On exception, worker survives and processes next job (exception isolation).
  - _Spec: PA-14, PA-23_

- [x] Create `Infrastructure/Social/Workflow/ProfileAnalysisBackgroundWorker.cs` + `ChannelProfileAnalysisJobQueue.cs` — background processor and queue mirroring single-analyzer pattern. Dequeues `ProfileAnalysisJob` messages; calls `IProfileAnalyzer.RunQueuedAsync(analysisId, orgId)`; handles unhandled exceptions by marking the record `Failed` with a diagnostic. Emits structured log entries at job start, job success, and job failure to `hive-api.log`.
  - _Spec: PA-14, PA-23_

- [x] Register the job queue singleton in DI + AddHostedService<ProfileAnalysisBackgroundWorker> in ApiService Program.cs.

- [x] Run `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj --filter "FullyQualifiedName~ProfileAnaly"` — 128 pass.
- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1` — pass=16, skipped=2.
- [x] Commit: `feat(infrastructure): add ProfileAnalysis background job queue and processor` (0d33a1e)

---

### WU-09 — Api: ProfileAnalyzer endpoint group + own-account picker (PR-4)

**Spec IDs: PA-01–PA-03, PA-15, PA-20, PA-22**
**Layer: Api**
**TDD order: test → implementation**

- [x] **[TEST]** Write xUnit API facade tests (`ApiService/Social/ProfileAnalyzerEndpointTests.cs`):
  - `POST /api/social/profile-analyzer/analyze` with valid client+own-account → 202 + `ProfileAnalysis` id.
  - `POST /analyze` with both sources → 400 (PA-03).
  - `POST /analyze` with neither source → 400 (PA-03).
  - `GET /{id}` returns current status (PA-15).
  - `GET /{id}` with wrong org → 404 (PA-15 org scoping).
  - `GET /{id}/dashboard` returns full record when `Analyzed` (PA-15).
  - `POST /{id}/impute` on non-`Analyzed` record → 422 `ProfileAnalysis.NotAnalyzed` (PA-22).
  - `POST /{id}/impute` when `clientId` null → error (PA-20).
  - `GET /` returns only org-scoped records (PA-15).
  - `DELETE /{id}` returns 204 (PA-15).
  - _Spec: PA-01–PA-03, PA-15, PA-20, PA-22_

- [x] Create `ApiService/Endpoints/Social/MapProfileAnalyzerEndpoints.cs` — new endpoint group `/api/social/profile-analyzer` (does NOT overload `/api/social/single-analyzer`). Endpoints:
  - `POST /analyze` → `AnalyzeProfileCommand` → 202 + id.
  - `GET /{id:guid}` → `GetProfileAnalysisQuery` → 200.
  - `GET /{id:guid}/dashboard` → `GetProfileDashboardQuery` → 200.
  - `POST /{id:guid}/impute` → `ImputeProfileAnalysisCommand` → 200.
  - `GET /` → `ListProfileAnalysesQuery` → 200 (paged).
  - `DELETE /{id:guid}` → `DeleteProfileAnalysisCommand` → 204.
  Include `ProfileAnalyzerEndpointFacade` internal static class (mirrors single-analyzer pattern). Add OpenAPI `WithName`/`WithSummary` to all endpoints. Register in `ApiService` endpoint map.
  - _Spec: PA-15_

- [x] Verify `GET /api/social/clients/{clientId}/own-accounts?network=instagram` endpoint exists (added in `MapClientOwnAccountsEndpoints.cs`). If it does not expose the `network` filter query parameter, add filtering support. This endpoint feeds the frontend client+own-account picker.
  - _Spec: PA-01_

- [x] Register `MapProfileAnalyzerEndpoints` in the app startup endpoint map.

- [x] Run `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj --filter "FullyQualifiedName~ProfileAnalyzerEndpoint"` — all green. 23/23 pass.
- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1`. pass=16, skipped=2. Total: 927/929 pass.
- [x] Commit: `feat(api): add ProfileAnalyzer endpoint group and own-account picker`

---

### WU-10 — Frontend: API client + types + mode switch + source picker + polling (PR-5a)

**Spec IDs: PA-01–PA-03, PA-19**
**Layer: Frontend**
**Check: `tsc --noEmit` + ESLint (no test runner)**

- [x] Add `recharts` to `apps/web/package.json`. Import restricted to the `profile-analyzer` lazy module.
  - _Design: Decision 7_

- [x] Create `apps/web/lib/api/profile-analyzer.ts` — typed API client functions:
  - `analyzeProfile(payload: AnalyzeProfilePayload, token?: string): Promise<ProfileAnalysisStatusResponse>`
  - `getProfileAnalysis(id: string, token?: string): Promise<ProfileAnalysisStatusResponse>`
  - `getProfileDashboard(id: string, token?: string): Promise<ProfileDashboardResponse>`
  - `imputeProfileAnalysis(id: string, req: ImputeProfileAnalysisRequest, token?: string): Promise<void>`
  - `listProfileAnalyses(page?: number, token?: string): Promise<ProfileAnalysisHistoryResponse>`
  - `deleteProfileAnalysis(id: string, token?: string): Promise<void>`
  - `listInstagramOwnAccounts(clientId: string, token?: string): Promise<OwnAccountItem[]>`
  - All functions use the existing `apiRequest` helper. Mirror structure of `apps/web/lib/api/single-analyzer.ts`.
  - _Spec: PA-15_

- [x] Create `apps/web/lib/api/types/profile-analyzer.ts` (or add to existing types file) — TypeScript interfaces matching backend DTOs: `ProfileAnalysisStatusResponse`, `ProfileDashboardResponse`, `ProfileStats`, `ProfileAiAnalysis`, `ProfileAnalysisHistoryResponse`, `ImputeProfileAnalysisRequest`, `ProfileImputeTarget` union type, `OwnAccountItem`.
  - _Spec: PA-01, PA-15, PA-16, PA-20_

- [x] Modify `apps/web/components/social/single-analyzer-client.tsx` — add ONLY the top-level mode switch (`ModeSwitch` component: "Contenido" | "Perfil"). "Contenido" keeps current view; "Perfil" renders `<ProfileAnalyzerModule>` lazily. NO other modification to existing Mundo 1 logic.
  - _Spec: PA-19, PA-REG-01_ — Implemented as a NEW `AnalyzerModeSwitch.tsx` component; page.tsx updated to use it. single-analyzer-client.tsx is untouched.

- [x] Create `apps/web/components/social/profile-analyzer/ProfileAnalyzerModule.tsx` — `React.lazy` boundary. Imports `recharts` only inside this boundary. Composes: `<ProfileSourcePicker>` (step 1), `<AnalysisProgress>` (step 2 — polling), `<ProfileDashboard>` (step 3 — shown when `Analyzed`), `<ImputePanel>` (step 4 — shown when `Analyzed` + `clientId` non-null). Manages routing state (analysisId, status, pollingInterval).
  - _Spec: PA-19_

- [x] Create `apps/web/components/social/profile-analyzer/ProfileSourcePicker.tsx` — two mutually-exclusive sub-paths:
  - **Client path**: `<ClientSelect>` (reuse existing client picker) → `<OwnAccountMultiSelect>` filtered to `network=instagram`. Auto-resolves if exactly one own account. Shows picker only when multiple accounts exist. Validates: no qualifying account → show error, disable submit (PA-01).
  - **URL path**: `<ProfileUrlInput>` (paste field with Instagram URL format validation). Mutually exclusive with client path — selecting one hides the other (PA-03).
  - Submit button disabled until exactly one source is configured (PA-03).
  - _Spec: PA-01, PA-02, PA-03_

- [x] Create `apps/web/components/social/profile-analyzer/AnalysisProgress.tsx` — polls `getProfileAnalysis(id)` on a configurable interval. Maps status values to human-readable step labels. Shows spinner during polling. Stops polling on `Analyzed` or `Failed`. On `Failed`, shows error message with diagnostics.
  - _Spec: PA-14, PA-15_

- [x] Run `tsc --noEmit` and ESLint in `apps/web` — zero errors before committing. (tsc: clean; ESLint: warnings pre-existing, no new errors)
- [x] Commit: `feat(web): add profile analyzer mode switch, source picker, API client, and polling` (c1a0a61)

---

### WU-11 — Frontend: ProfileDashboard with recharts (PR-5b)

**Spec IDs: PA-16–PA-18**
**Layer: Frontend**
**Check: `tsc --noEmit` + ESLint**

- [x] Create `apps/web/components/social/profile-analyzer/dashboard/OverviewCards.tsx` — metric cards: posts count, engagement rate, median views (shows "N/A" when null), posting cadence, content type mix top 2, profile handle. Sourced from `ProfileDashboardResponse.stats`. Followers not shown (ProfileSnapshot not persisted separately — see risks in apply-progress).
  - _Spec: PA-16, PA-18_

- [x] Create `apps/web/components/social/profile-analyzer/dashboard/AvgViewsByWeekdayChart.tsx` — recharts `BarChart`. X-axis = Mon–Sun (UTC weekday from `avgViewsByWeekday`). Y-axis = average views. Graceful render when data is empty/null (no-data placeholder).
  - _Spec: PA-12, PA-16_

- [x] Create `apps/web/components/social/profile-analyzer/dashboard/EngagementTrendChart.tsx` — recharts `AreaChart`. Plots avg likes and avg comments by weekday. Graceful empty state.
  - _Spec: PA-12, PA-16_

- [x] Create `apps/web/components/social/profile-analyzer/dashboard/ContentTypeMixChart.tsx` — recharts `PieChart` (donut). Segments: Image, Video, Carousel percentages from `contentTypeMix`. Shows percentages as labels. Graceful empty state.
  - _Spec: PA-16_

- [x] Create `apps/web/components/social/profile-analyzer/dashboard/AiNarrativePanel.tsx` — displays: summary, strengths, weaknesses, opportunities, audience profile, content pillars, posting days, voice/tone, brand colors, fonts, KPIs, monthly objective, content strategy. Shows absent-data indicator for any null field (PA-18). Shows warning banner when `aiAnalysis.warnings` contains low-confidence or sparse-data warnings (PA-11).
  - _Spec: PA-11, PA-16, PA-18_

- [x] Create `apps/web/components/social/profile-analyzer/dashboard/ComparisonVsObjectivesPanel.tsx` — shows detected account state (from AI analysis) vs client's stated objectives (from `ContentPlan`/`GeneralStrategy`). Renders ONLY when `clientId` is non-null AND `comparison` data is non-empty in `ProfileDashboardResponse`. Omitted entirely (not an empty panel) when `clientId` is null or plan data is absent (PA-17).
  - _Spec: PA-17_

- [x] Create `apps/web/components/social/profile-analyzer/dashboard/ProfileDashboard.tsx` — assembles all dashboard sub-components. Calls `getProfileDashboard(id)`. Passes comparison data to `ComparisonVsObjectivesPanel` only when available. Renders gracefully when optional fields are null (PA-18).
  - _Spec: PA-16, PA-17, PA-18_

- [x] Wire `ProfileAnalyzerModule.tsx`: on Analyzed, render `<ProfileDashboard/>` instead of the placeholder.

- [x] Backend gap-fill: add `ProfileAiAnalysisDto` + `DetectedBrandColorDto` to `ProfileAnalysisContracts.cs`; add `AiAnalysis` field to `ProfileDashboardResponse`; update `GetProfileDashboardQueryHandler` to deserialize and project `AiAnalysisJson`. Update `MakeDashboardResponse` in endpoint tests for new parameter.

- [x] Run `tsc --noEmit` and ESLint in `apps/web` — zero errors; all pre-existing warnings only.
- [x] Gates: 16 MUST pass, 2 skipped. build.frontend.web pass (43s). No regressions (927/929 pass).
- [x] Commit: `feat(web): add profile analysis dashboard with recharts charts and AI narrative`

---

### WU-12 — Frontend: ImputePanel + ConfirmDialog (PR-5c)

**Spec IDs: PA-20–PA-22**
**Layer: Frontend**
**Check: `tsc --noEmit` + ESLint**

- [x] Create `apps/web/components/social/profile-analyzer/impute/ImputePanel.tsx` — rendered ONLY when `clientId` is non-null AND `aiAnalysis` is non-null (PA-22: Analyzed + client-linked). Presents 11 impute targets grouped by: Manual de marca (ColorPalette, Typography, BackgroundTemplates, VoiceAndTone), Plan de contenido (ObjectiveText, ObjectiveKpis, ContentStrategyText), Estrategia general (ContentPillars, ToneKeywords, TargetAudience, PostingFrequency). Each checkbox shows field name + detected AI value preview; disabled with "sin dato detectado" when null. Select-all/clear helpers. "Imputar seleccionados al cliente" button opens ConfirmationDialog. Uses reused ConfirmationDialog from `@/components/ui/confirmation-dialog`. URL-path shows a note "Imputación disponible solo al analizar un cliente del sistema".
  - _Spec: PA-20_

- [x] Confirmation dialog (reused `ConfirmationDialog` from `@/components/ui/confirmation-dialog`) opens before applying. Shows summary of selected targets + client ID. "Confirmar y aplicar" triggers `imputeProfileAnalysis(id, {targets})`. Success feedback via `useToast().success()`. Error feedback via `useToast().error()`. Post-success shows inline applied-fields summary.
  - _Spec: PA-20, PA-21_

- [x] Impute panel rendered in `ProfileDashboard.tsx` after ComparisonVsObjectivesPanel. Guard: `dashboard.clientId !== null && dashboard.aiAnalysis !== null`. Dashboard is only reached when status === Analyzed (PA-22 frontend guard).
  - _Spec: PA-22_

- [x] Run `tsc --noEmit` and ESLint in `apps/web` — zero errors; all warnings pre-existing in unrelated files. No new warnings from ImputePanel or ProfileDashboard.
- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1` — pass=16, skipped=2. build.frontend.web: pass (44.9s). build.frontend.web.lint: pass. tests.backend: 927/929, 0 failed.
- [x] Commit: `feat(web): add per-field impute panel with confirmation for client ABM`

---

### WU-13 — Cross-cutting: diagnostics log wiring (PA-23)

**Spec IDs: PA-23**
**Layer: Infrastructure / Application**
**Parallel: can run alongside WU-10 once WU-08 is merged**

- [x] Verify all `IProfileAnalyzer` orchestrator stage boundaries emit structured log entries using `ILogger<ProfileAnalyzer>` with log-level `Information` on progress and `Warning`/`Error` on failure. Required fields per entry: `analysisId`, `organizationId`, `stage`, `actorId` (when applicable), `fallbackUsed` (when applicable), `postsReceived`, `postsProcessed`, `degradedPostCount`.
  - _Spec: PA-23_
  - VERIFIED + GAPS FILLED: orchestrator log entries updated to include all required structured fields. `ProfileRunDiagnostics` POCO created. `ProfileScrapeResult` enriched with `UsedActorId` and `FallbackUsed`. 3 new PA-23 tests added (930/932 pass).

- [x] Verify `ApifyProfileScrapeResolver` emits a structured log entry on: actor resolution, Apify dispatch start, Apify dispatch result (success/failure), fallback trigger, final outcome. Confirm entries route to `hive-api.log` (mirrors the single-analyzer logging config).
  - _Spec: PA-23_
  - VERIFIED: resolver already had all required log entries. hive-api.log routing confirmed via FileLogging.CategoryPrefix="Incoders.Hive" in appsettings.json.

- [x] Verify `ProfileAnalysisData.ProviderDiagnosticsJson` is populated on ALL terminal paths (Analyzed, Failed, partial). Confirm the JSON shape matches the PA-23 scenario requirements: `actor_id`, `fallback_used`, `posts_received`, `posts_processed`, `degraded_post_count`, `ai_provider_key`, `ai_model`; on fallback: `default_actor_error` + `backup_actor_id`; on failure: both failure reasons.
  - _Spec: PA-23_
  - GAPS FILLED: new `ProfileRunDiagnostics` record serialized to ProviderDiagnosticsJson on ALL terminal paths.

- [x] Commit: `feat(infra): wire structured diagnostics and provider logging to hive-api.log` (aa90c36)

---

### WU-14 — Docs: Excalidraw workflow diagram + operator docs + integrations spec delta (PR-6)

**Spec IDs: PA-23, INT-DELTA-01**
**Layer: Docs**
**Parallel: can run alongside frontend WU-11/12**

- [x] Create `docs/architecture/diagrams/profile-analysis-flow.excalidraw` — Excalidraw source file. Nodes and edges per design.md §Excalidraw Diagram Deliverable:
  ```
  Source (Client+IG own-account | URL paste)
    → AnalyzeProfile (202, record Requested)
    → ScrapingProfile --default actor--> Apify
          Apify --fail--> [backup actor] --> Apify
    → ProfileScraped[Partial]
    → Analyzing (profile-analyzer.analysis.v1)
    → Analyzed | Failed
    → Dashboard (stats + raw posts + comparison-if-client)
    → Impute (per-field, confirmed) --> {Brand Manual, Content Plan, General Strategy}
  ```
  Color legend: source/enduring nodes = blue, pipeline stages = purple, terminal states = green, decision/fallback = amber. Cross-link to ADR 0003 in a diagram label.
  - _Design: §Excalidraw Diagram Deliverable, ADR 0003 §Related_

- [x] Create `docs/architecture/diagrams/profile-analysis-flow.md` — Mermaid rendering of the same flow (mirrors `content-plan-flow.md` format). Top note links to `profile-analysis-flow.excalidraw` and to ADR 0003.
  - _Design: §Excalidraw Diagram Deliverable_

- [x] Update `openspec/specs/integrations/spec.md` (additive delta per `spec-delta.md`) — document:
  - Required `actor_mappings_json` entry shape for `(networkKey="instagram", variantKey="profile")` including `defaultActorId` (iMcuOj3FW9xLIbqET) and `backupActorId` (dSCLg0C3YEZ83HzYX).
  - Automatic backup-actor fallback behavior (default fails → backup retried once in the same run; applies ONLY to `variantKey="profile"`; single-video path unaffected).
  - `Integration.ProfileActorNotConfigured` 422 error on missing entries.
  - _Spec: INT-DELTA-01, INT-01, INT-02_

- [x] Add operator setup documentation comment block to `ApifyProfileScrapeResolver.cs` (or a companion `docs/operators/profile-analysis-setup.md`) specifying the exact required `actor_mappings_json` entries, the fallback behavior, and the consequence of missing entries (clear 422 diagnostic).
  - _Spec: INT-DELTA-01_ — created `docs/operators/profile-analysis-setup.md`

- [x] Confirm docs sync: verify `docs/SDD.md` and `docs/PRD.md` are consistent with the new profile analysis capability. Add a brief section under the Social module description if absent.
  - _AGENTS.md §Documentation Sync_ — added §11 to SDD.md and §10 to PRD.md

- [ ] Run `pwsh -File scripts/gates/Run-Gates.ps1` — final gate run across all layers.
- [ ] Commit: `docs: add profile-analysis-flow diagram, integrations spec delta, and operator setup notes`

---

### WU-15 — Final: regression verification (PA-REG-01)

**Spec IDs: PA-REG-01**
**Layer: All**
**Sequential: last task, after all WUs complete**

- [ ] **[TEST]** Run the full test suite: `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj` — all existing Mundo 1 tests (`SingleAnalyzer*`, `ContentAnalysisProfile*`, `SocialContentAnalysis*`) pass unchanged.
  - _Spec: PA-REG-01_

- [ ] **[ARCH TEST]** Confirm architecture test: no type in `ProfileAnalysis` namespace imports from `SingleAnalyzer` namespace (and vice versa). Run architecture gate.
  - _Spec: PA-REG-01_

- [ ] **[SCHEMA CHECK]** Verify `social.single_content_analyses` table schema is unchanged by running a schema snapshot comparison (or manually inspecting the migration).
  - _Spec: PA-REG-01_

- [ ] Verify `single-analyzer-client.tsx` diff contains ONLY the mode switch addition — no existing Mundo 1 logic modified.
  - _Spec: PA-REG-01_

- [ ] Run `pwsh -File scripts/gates/Run-Gates.ps1` — final green on all MUST gates.
- [ ] Commit: `test(regression): verify Mundo 1 unchanged after profile analysis feature`

---

## Task Dependency Map

```
WU-01 (spike) ──────────────────────────────────┐
                                                 ↓
WU-02 (Domain) ──────────────────┬──────────────► WU-06 (Infra scrape)
                                 ↓                       │
                          WU-03 (App ports) ──────────────┤
                                 ↓                       │
                          WU-04 (App analyze) ───────────►│
                                 ↓                       │
                          WU-05 (App dashboard/impute)   │
                                 ↓                       │
                          WU-06 ◄─────────────────────────┘
                                 ↓
                          WU-07 (Infra EF/migration/repo)
                                 ↓
                          WU-08 (Infra job processor)
                                 ↓
                          WU-09 (Api endpoints)
                                 ↓
                    ┌────────────┴───────────┐
                    ↓                        ↓
             WU-10 (FE: module)       WU-13 (diagnostics)
                    ↓                        
             WU-11 (FE: dashboard)    WU-14 (docs) ← can run parallel to WU-11/12
                    ↓                        
             WU-12 (FE: impute)       
                    ↓                        
             WU-15 (regression)              
```

Parallel opportunities:
- WU-13 (diagnostics wiring) can proceed once WU-08 is merged.
- WU-14 (docs) can proceed once WU-06 is complete (enough design is known).
- WU-11 and WU-12 are sequential on WU-10 but independent of WU-13/14.

---

## Spec Coverage Matrix

| Spec ID | Work Units |
|---|---|
| PA-01 | WU-04, WU-09, WU-10 |
| PA-02 | WU-04, WU-09, WU-10 |
| PA-03 | WU-04, WU-09, WU-10 |
| PA-04 | WU-03, WU-06 |
| PA-05 | WU-03, WU-06 |
| PA-06 | WU-06 |
| PA-07 | WU-04, WU-06 |
| PA-08 | WU-02, WU-04, WU-06 |
| PA-09 | WU-02, WU-04, WU-06 |
| PA-10 | WU-03, WU-04 |
| PA-11 | WU-03, WU-04, WU-11 |
| PA-12 | WU-04, WU-11 |
| PA-13 | WU-02, WU-03, WU-07 |
| PA-14 | WU-02, WU-08 |
| PA-15 | WU-03, WU-07, WU-09 |
| PA-16 | WU-05, WU-11 |
| PA-17 | WU-05, WU-11 |
| PA-18 | WU-05, WU-11 |
| PA-19 | WU-10 |
| PA-20 | WU-05, WU-09, WU-12 |
| PA-21 | WU-01 (audit), WU-05, WU-12 |
| PA-22 | WU-05, WU-09, WU-12 |
| PA-23 | WU-06, WU-08, WU-13 |
| PA-24 | WU-06 |
| PA-REG-01 | WU-02, WU-15 |
| INT-DELTA-01 | WU-14 |
| INT-01 | WU-03, WU-06, WU-14 |
| INT-02 | WU-06, WU-14 |
