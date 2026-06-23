# Tasks: content-background-sourcing-policy

**Branch:** `feat/content-format-taxonomy` — single branch, no new PR branches. All tasks committed on this branch; the human opens one PR to `dev` at the end.
**TDD mode:** STRICT — `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj`. Every behavior task: RED → GREEN. Make the test fail first, then write the implementation.

---

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~900–1,300 |
| 400-line budget risk | **High** |
| Chained PRs recommended | **No — single branch, human decides when to PR** |
| Delivery constraint | Single branch; work organized into independent work units (WUs) for reviewable diff sections |
| Chain strategy | N/A — single branch delivery |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: size-exception
400-line budget risk: High

### Suggested Work Units (for diff navigation, not separate branches)

| WU | Goal | Seam order | Notes |
|----|------|-----------|-------|
| WU-1 | `ColorEntry` + `ColorRole` + `WeightedColor` + `BrandManualSnapshot` contract change (§6 items 1-9) | First — compiler blocks partial application | Coordinated breaking unit; all consumers updated together |
| WU-2 | `BackgroundSourcingPolicy` on `Client`; `BackgroundSelection.ClientAesthetic`; `CarouselBackgroundPlan` domain types | After WU-1 | Pure domain, no migration yet |
| WU-3 | Strategy port + A/B/C strategies + resolver + legacy adapter (Application) | After WU-2 | Pure, unit-testable; no infra needed |
| WU-4 | Coherence refactor: `ContentGenerationJobProcessor` pre-loop plan + `CarouselVisualComposer` (C1-C6 sequence) | After WU-3 | HIGH risk; TDD sequence C1-C6 before any production change |
| WU-5 | CASE C seam: `IClientAestheticDescriptorReader` port + `EfClientAestheticDescriptorReader` + descriptor threading in processor | After WU-3 | Parallel with WU-4 if needed; processor changes integrate with WU-4 |
| WU-6 | Infrastructure: EF migration (`Add_BackgroundSourcingPolicy_And_ColorRole`), `EfClientBrandSnapshotLoader`, `AiVisualDesigner` weighted prompt | After WU-1 domain types + WU-2 policy | Depends on WU-1 type shapes; migration only after domain settles |
| WU-7 | Advisory concision directives + prompt-version bumps + concision logging | After WU-3 (generators touch same classes) | Prompt-only; no schema impact |
| WU-8 | API: `MapSocialClientsEndpoints` + `MapClientBrandManualsEndpoints` policy + role fields | After WU-6 (needs infra) | |
| WU-9 | Web: policy selector UI + color-role ordering UI | After WU-8 | |
| WU-10 | ADR + docs sync + spec reconciliation + final gates | Last | |

> **Note on C1-C6 ordering:** tasks 4.1-4.6 map exactly to the design's C1-C6 test-first sequence. They MUST be done in that order before the processor/composer production changes (4.7-4.10).

---

## WU-1 — Structured Color-Role Contract Change (coordinated, compiler-enforced unit)

*Refs: CG-COL-01, CG-COL-02, CG-COL-03; design §6 items 1-9.*
*All nine sub-steps land together — partial application leaves the build red.*

### Phase 1.A — Domain: ColorRole + ColorEntry evolution

- [x] 1.1 **[RED]** Add `ColorEntryRoleTests.cs` — assert `ColorEntry.Create` with `ColorRole? role` + `int rank`; assert `"primary"→Primary`, `"secondary"→Secondary`, `"accent"→Accent`, `"neutral"→Neutral`, unknown→`null`; assert `Rank >= 0` validation; `Restore` tolerates null role + zero rank (back-compat). (`Incoders.Hive.Tests/Domain/Social/ClientBrandManuals/ColorEntryRoleTests.cs`)
- [x] 1.2 **[GREEN]** Create `ColorRole.cs` — `{ Primary=0, Secondary=1, Accent=2, Neutral=3 }`. (`Domain/Social/ClientBrandManuals/ColorRole.cs`)
- [x] 1.3 **[GREEN]** Modify `ColorEntry.cs` — replace `string? Role` with `ColorRole? Role` + `int Rank`; update `Create`/`Restore` signatures; add free-text migration helper `TryParseRole(string?)→ColorRole?`. (`Domain/Social/ClientBrandManuals/ColorEntry.cs`)

### Phase 1.B — Domain: WeightedColor + BrandManualSnapshot shape

- [x] 1.4 **[RED]** Add `WeightedColorTests.cs` — assert `WeightedColor(Hex, Role, Rank)` construction; assert `BrandManualSnapshot.ColorPalette` is `IReadOnlyList<WeightedColor>` and pre-sorted by `(Role ordinal, Rank)`; assert no `IEnumerable<string>` usage compiles. (`Incoders.Hive.Tests/Domain/Social/ContentGeneration/WeightedColorTests.cs`)
- [x] 1.5 **[GREEN]** Create `WeightedColor.cs` — `record WeightedColor(string Hex, ColorRole? Role, int Rank)` in `Domain/Social/ContentGeneration/`. (`Domain/Social/ContentGeneration/WeightedColor.cs`)
- [x] 1.6 **[GREEN]** Modify `BrandManualSnapshot.cs` — change `IReadOnlyList<string> ColorPalette` to `IReadOnlyList<WeightedColor> ColorPalette`. (`Domain/Social/ContentGeneration/BrandManualSnapshot.cs`)
- [x] 1.7 **[GREEN]** Modify `BrandSnapshot.cs` — update `FromBrandData` empty-brand fallback to `Array.Empty<WeightedColor>()`. (`Domain/Social/ContentGeneration/BrandSnapshot.cs`)

### Phase 1.C — Application: cascade consumers (all must compile together)

- [x] 1.8 **[RED]** Add `VisualGuardrailValidatorPaletteTests.cs` — assert off-palette detection reads `WeightedColor.Hex` (not a string). (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Visual/VisualGuardrailValidatorPaletteTests.cs`)
- [x] 1.9 **[GREEN]** Modify `VisualGuardrailValidator.cs` — palette check reads `.Hex` from `WeightedColor`. (`Application/Features/Social/ContentGeneration/Visual/VisualGuardrailValidator.cs`)
- [x] 1.10 **[RED]** Add/extend `BuildTemplateBackedMarkupTests.cs` — assert gradient hex still built from `WeightedColor.Hex` via `CompositionDefaults.ResolvePalette`. (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Pipeline/BuildTemplateBackedMarkupTests.cs`)
- [x] 1.11 **[GREEN]** No production change required — `BuildTemplateBackedMarkup` reads `spec.Palette.Primary`/`.Secondary` from `CompositionPalette` which is already correctly resolved from `WeightedColor.Hex` via `CompositionDefaults.ResolvePalette` (fixed in 1.9 consumer cascade). (`Application/Features/Social/ContentGeneration/Visual/CompositionDefaults.cs`)
- [x] 1.12 **[RED]** Add `AiVisualDesignerWeightedPaletteTests.cs` — assert `BuildInputJson`/`BuildSystemPrompt` includes `(hex, role, rank)` tuples in rank order; assert primary color labeled "Primary" appears before accent; assert prompt contains prevalence weighting language. (`Incoders.Hive.Tests/Infrastructure/Social/ContentGeneration/AiVisualDesignerWeightedPaletteTests.cs`)
- [x] 1.13 **[GREEN]** Modify `AiVisualDesigner.cs` — weighted palette DTO (`WeightedPaletteEntryDto`) + prompt section with role labels + rank order; `BuildSystemPrompt` expresses primary dominant, accent sparingly prevalence. (`Infrastructure/Social/ContentGeneration/AiVisualDesigner.cs`)
- [x] 1.14 **[RED]** Add/extend `GenerationPromptContextTests.cs` — assert `FromBrand` emits `{hex, role, rank}` projection. (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/GenerationPromptContextTests.cs`)
- [x] 1.15 **[GREEN]** Modify `GenerationPromptContext.cs` — `colorPalette` projection emits explicit `{hex, role, rank}` anonymous type. (`Application/Features/Social/ContentGeneration/Shared/GenerationPromptContext.cs`)
- [x] 1.16 **[GREEN]** Updated all test fixtures — compile-time sweep across the test project converted all `string[]` ColorPalette usages to `WeightedColor[]`. (`Incoders.Hive.Tests/**`)
- [x] 1.17 Run `dotnet test` — 1538 passed, 0 failed, 2 skipped (infrastructure DB tests). Full WU-1 green.

---

## WU-2 — Domain: Policy on Client + New Domain Types

*Refs: BSP-07, BSP-05, BSP-REG-01; design §3.1, §3.4, §3.5.*

- [x] 2.1 **[RED]** Add `BackgroundSourcingPolicyTests.cs` — assert `Client.SetBackgroundSourcingPolicy` changes `Policy`; assert `Restore` with null policy defaults to legacy; assert `Create` with null policy starts as null. (`Incoders.Hive.Tests/Domain/Social/Clients/BackgroundSourcingPolicyTests.cs`)
- [x] 2.2 **[GREEN]** Create `BackgroundSourcingPolicy.cs` — `{ Unset=0, ClientDefinedSet=1, AiPalette=2, ClientAesthetic=3 }`. (`Domain/Social/Clients/BackgroundSourcingPolicy.cs`)
- [x] 2.3 **[GREEN]** Modify `Client.cs` — add `BackgroundSourcingPolicy? Policy`; extend `Create`/`Restore` (nullable, defaulted so existing call sites compile); add `SetBackgroundSourcingPolicy(BackgroundSourcingPolicy? policy, DateTime nowUtc)` mutator. (`Domain/Social/Clients/Client.cs`)
- [x] 2.4 **[RED]** Add `BackgroundSelectionClientAestheticTests.cs` — assert `BackgroundSelection.ClientAesthetic(descriptors)` pattern match works in exhaustive switch; assert empty descriptors degrades gracefully. (`Incoders.Hive.Tests/Domain/Social/ContentGeneration/BackgroundSelectionClientAestheticTests.cs`)
- [x] 2.5 **[GREEN]** Modify `BackgroundSelection.cs` — add `sealed record ClientAesthetic(IReadOnlyList<string> Descriptors) : BackgroundSelection`. Update every exhaustive switch in the codebase (compiler locates all sites). (`Domain/Social/ContentGeneration/BackgroundSelection.cs`)
- [x] 2.6 **[RED]** Add `CarouselBackgroundPlanTests.cs` — assert `ForSlide(i)` 1-based; assert clamp on overflow returns last entry; assert `IsCoherent` true when all entries share the same type; assert `stableHash(seed, i) % setCount` gives the same rotation across calls. (`Incoders.Hive.Tests/Domain/Social/ContentGeneration/CarouselBackgroundPlanTests.cs`)
- [x] 2.7 **[GREEN]** Create `CarouselBackgroundPlan.cs` — `record CarouselBackgroundPlan(BackgroundSourcingPolicy Policy, IReadOnlyList<BackgroundSelection> PerSlide, string SeedToken)` with `ForSlide`, `IsCoherent`; add static `StableHash(string seed, int slideIndex)→int` helper. (`Domain/Social/ContentGeneration/CarouselBackgroundPlan.cs`)
- [x] 2.8 Run `dotnet test` — WU-2 domain tests green.

---

## WU-3 — Application: Strategy Port + A/B/C Strategies + Resolver + Legacy Adapter

*Refs: BSP-01, BSP-02, BSP-03, BSP-04, BSP-05, BSP-06; design §4.*

- [x] 3.1 **[RED]** Add `IBackgroundSourcingStrategyContractTests.cs` — assert port has no Infrastructure types; assert `IBackgroundStrategyResolver` likewise; assert `BackgroundPlanRequest` properties are Domain records only. (`Incoders.Hive.Tests/Application/IBackgroundSourcingStrategyContractTests.cs`)
- [x] 3.2 **[GREEN]** Create `IBackgroundSourcingStrategy.cs` + `BackgroundPlanRequest.cs` + `IBackgroundStrategyResolver.cs`. (`Application/Abstractions/Social/ContentGeneration/`)
- [x] 3.3 **[RED]** Add `LegacyTierStrategyTests.cs` — assert `Plan(...)` per-slide matches `BackgroundSelector.Select(...)` for the same template list + format key (behavior parity with today's per-slide selection, refs spec BSP-05 + design C3). (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/LegacyTierStrategyTests.cs`)
- [x] 3.4 **[GREEN]** Create `LegacyTierStrategy.cs` — wraps `BackgroundSelector`; `Policy = Unset`. (`Application/Features/Social/ContentGeneration/Background/LegacyTierStrategy.cs`)
- [x] 3.5 **[RED]** Add `ClientDefinedSetStrategyTests.cs` — assert `setCount=0→Creative` all slides; assert `setCount=1→References([single])` all slides (BSP-02); assert `setCount>1` rotation is deterministic (same seed → same plan), NOT all slides identical (BSP-01a), and retries reproduce the assignment (BSP-01c). (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/ClientDefinedSetStrategyTests.cs`)
- [x] 3.6 **[GREEN]** Create `ClientDefinedSetStrategy.cs` — `Policy = ClientDefinedSet`; uses `CarouselBackgroundPlan.StableHash`. (`Application/Features/Social/ContentGeneration/Background/ClientDefinedSetStrategy.cs`)
- [x] 3.7 **[RED]** Add `AiPaletteStrategyTests.cs` — assert every slide returns `Creative` (BSP-03b); assert strategy carries no reference to background sets. (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/AiPaletteStrategyTests.cs`)
- [x] 3.8 **[GREEN]** Create `AiPaletteStrategy.cs` — `Policy = AiPalette`; all slides `Creative`. (`Application/Features/Social/ContentGeneration/Background/AiPaletteStrategy.cs`)
- [x] 3.9 **[RED]** Add `ClientAestheticStrategyTests.cs` — assert non-empty descriptors → all slides `ClientAesthetic(descriptors)`; assert empty descriptors → all slides `Creative` + warning logged (BSP-04 degradation). (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/ClientAestheticStrategyTests.cs`)
- [x] 3.10 **[GREEN]** Create `ClientAestheticStrategy.cs` — `Policy = ClientAesthetic`; degrades to `Creative` on empty descriptors. (`Application/Features/Social/ContentGeneration/Background/ClientAestheticStrategy.cs`)
- [x] 3.11 **[RED]** Add `BackgroundStrategyResolverTests.cs` — assert `null`/`Unset` → `LegacyTierStrategy`; assert A/B/C → correct strategies; assert injecting a fake CASE-D strategy resolves without modifying A/B/C (BSP-06 OCP test). (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/BackgroundStrategyResolverTests.cs`)
- [x] 3.12 **[GREEN]** Create `BackgroundStrategyResolver.cs` — DI-keyed map; unknown → `LegacyTierStrategy`. (`Application/Features/Social/ContentGeneration/Background/BackgroundStrategyResolver.cs`)
- [x] 3.13 Run `dotnet test` — WU-3 strategy tests green.

---

## WU-4 — Coherence Refactor (C1-C6 — must be in this exact order)

*Refs: CG-COH-01, CG-COH-02; design §6.1, §7.*
**CRITICAL: write C1-C6 tests BEFORE touching processor/composer production code.**

- [x] 4.1 **[C1/RED]** Add `CarouselBackgroundPlanForSlideTests.cs` — assert `ForSlide` 1-based; assert `stableHash` is deterministic for a fixed seed; assert overflow clamp. (These tests pass already from WU-2.6 — verify they are green before proceeding.)
- [x] 4.2 **[C2/RED]** Extend `ClientDefinedSetStrategyTests.cs` — assert same seed → same plan (already covered in 3.5 — verify green). No new file needed unless 3.5 did not cover it.
- [x] 4.3 **[C3/RED]** Extend `LegacyTierStrategyTests.cs` — assert parity: per-slide output of `LegacyTierStrategy.Plan` matches per-slide `BackgroundSelector.Select` for the same format + templates. (Already covered in 3.3 — verify green.)
- [x] 4.4 **[C4/RED]** Add `ProcessorPreLoopPlanTests.cs` — mock `IBackgroundSourcingStrategy`; assert `strategy.Plan(...)` is called EXACTLY ONCE per format for a multi-slide carousel (not once per slide); assert N `plan.ForSlide(i)` reads; assert no `BackgroundSelector.Select` called directly. (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Pipeline/ProcessorPreLoopPlanTests.cs`)
- [x] 4.5 **[C4/GREEN]** Modify `ContentGenerationJobProcessor.cs` — resolve strategy before format loop; fetch CASE-C descriptors only when policy is `ClientAesthetic`; call `strategy.Plan(...)` ONCE before the slide loop; replace per-slide `BackgroundSelector.Select` (line ~315) with `plan.ForSlide(specIndex + 1)`; seed = `job.Id.ToString()` with date fallback. (`Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs`)
- [x] 4.6 **[C5/RED]** Add `CarouselVisualComposerPlanTests.cs` — inject a pre-built `CarouselBackgroundPlan`; assert `AssetSlots` background for slide i equals `plan.ForSlide(i)`; assert no `BackgroundSelector.Select` call is made inside the composer. (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Visual/CarouselVisualComposerPlanTests.cs`)
- [x] 4.7 **[C5/GREEN]** Modify `CarouselVisualComposer.cs` — accept `CarouselBackgroundPlan` (via context or parameter); read `plan.ForSlide(slideIndex)` for the slot background; remove the `BackgroundSelector.Select` call at line ~61. (`Application/Features/Social/ContentGeneration/Visual/Composers/CarouselVisualComposer.cs`)
- [x] 4.8 **[GREEN]** Update `SurfaceScopedCarouselVisualComposer.cs` if it delegates to `CarouselVisualComposer` — thread the plan through. (`Application/Features/Social/ContentGeneration/Visual/Composers/SurfaceScopedCarouselVisualComposer.cs`)
- [x] 4.9 **[C6/RED]** Add `ResolveEnforcedBackgroundRotationTests.cs` — assert that for a `References([rotatedSingle])` plan entry the enforced template is that slide's entry (not always `Entries[0]` from a shared set); assert `ClientAesthetic` returns null from `ResolveEnforcedBackground`. (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Pipeline/ResolveEnforcedBackgroundRotationTests.cs`)
- [x] 4.10 **[C6/GREEN]** Modify `ResolveEnforcedBackground` in `ContentGenerationJobProcessor.cs` (~line 943/955) — confirm first-entry-wins correctly enforces the rotated per-slide template; handle `ClientAesthetic` → return null. (`Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs`)
- [x] 4.11 Run `dotnet test` — all C1-C6 tests green; no regression on existing pipeline tests. **1615 passed, 0 failed, 2 skipped.**

---

## WU-5 — CASE C Seam: Descriptor Reader Port + Infrastructure Adapter

*Refs: BSP-04, PA-SEAM-01, PA-SEAM-02, PA-SEAM-03; design §4.4.*

- [x] 5.1 **[RED]** Add `IClientAestheticDescriptorReaderContractTests.cs` — reflect interface; assert no Infrastructure types in signatures (mirrors `IVisualDesignerContractTests` pattern). (`Incoders.Hive.Tests/Application/IClientAestheticDescriptorReaderContractTests.cs`)
- [x] 5.2 **[GREEN]** Create `IClientAestheticDescriptorReader.cs` — `Task<IReadOnlyList<string>> GetDescriptorsAsync(Guid orgId, Guid clientId, CancellationToken ct)`. (`Application/Abstractions/Social/ContentGeneration/IClientAestheticDescriptorReader.cs`)
- [x] 5.3 **[RED]** Add `EfClientAestheticDescriptorReaderTests.cs` — assert returns `DetectedBackgroundImages` text for the most recent `Analyzed` status record (PA-SEAM-01a); assert returns empty on no completed record (PA-SEAM-01b); assert selects the most recent of two completed records by date (PA-SEAM-01c); assert in-progress/failed excluded (PA-SEAM-01d); assert no Apify call (PA-SEAM-03 — no `IApifyGateway` in signature or call chain). (`Incoders.Hive.Tests/Infrastructure/Social/ContentGeneration/EfClientAestheticDescriptorReaderTests.cs`)
- [x] 5.4 **[GREEN]** Create `EfClientAestheticDescriptorReader.cs` — reads latest `Analyzed` `ProfileAiAnalysis.DetectedBackgroundImages` for `(orgId, clientId)`; returns empty list when none. (`Infrastructure/Social/ContentGeneration/EfClientAestheticDescriptorReader.cs`)
- [x] 5.5 **[GREEN]** Register `EfClientAestheticDescriptorReader` → `IClientAestheticDescriptorReader` in Infrastructure DI. (`Infrastructure/Social/ContentGeneration/InfrastructureContentGenerationExtensions.cs`)
- [x] 5.6 **[GREEN]** Register strategies + resolver in Infrastructure DI (keyed `IBackgroundSourcingStrategy`, then `BackgroundStrategyResolver` → `IBackgroundStrategyResolver`). (`Infrastructure/Social/ContentGeneration/InfrastructureContentGenerationExtensions.cs`)
- [x] 5.7 Run `dotnet test` — WU-5 seam tests green. **10 new tests passed.**

---

## WU-6 — Infrastructure: EF Migration + Snapshot Loader + AiVisualDesigner Wiring

*Refs: BSP-07, BSP-REG-01, CG-COL-02, CG-COL-03; design §10.*

- [x] 6.1 **[GREEN]** Modify `SocialClientData.cs` — add `short? BackgroundSourcingPolicy` property. (`Infrastructure/Persistence/Postgres/Entities/SocialClientData.cs`)
- [x] 6.2 **[GREEN]** Modify `AppIdentityDbContext.cs` — map `SocialClientData.BackgroundSourcingPolicy` as column `background_sourcing_policy`, nullable smallint. (`Infrastructure/Identity/AppIdentityDbContext.cs`)
- [x] 6.3 **[GREEN]** Generate EF migration `Add_BackgroundSourcingPolicy_And_ColorRole` — (a) `ALTER TABLE social.clients ADD COLUMN background_sourcing_policy smallint NULL`; (b) SQL data step: rewrite each `client_brand_manuals.color_palette` jsonb document mapping free-text role → enum ordinal + set `rank = arrayIndex`; idempotent. (`Infrastructure/Persistence/Postgres/Migrations/20260620160922_Add_BackgroundSourcingPolicy_And_ColorRole.cs`)
- [x] 6.4 **[RED]** Add `ClientBrandSnapshotLoaderRoleTests.cs` — using a seeded EF context, assert `EfClientBrandSnapshotLoader` builds `WeightedColor` entries with correct `Role` and `Rank` from persisted data; assert loads `Client.Policy` into snapshot; assert pre-sorted by `(Role ordinal, Rank)` (CG-COL-02). (`Incoders.Hive.Tests/Infrastructure/Social/ContentGeneration/ClientBrandSnapshotLoaderRoleTests.cs`)
- [x] 6.5 **[GREEN]** Modify `EfClientBrandSnapshotLoader.cs` — load `Client.BackgroundSourcingPolicy` into the snapshot context (cast short? → domain enum). (`Infrastructure/Social/ContentGeneration/EfClientBrandSnapshotLoader.cs`)
- [ ] 6.6 Run gates: `pwsh -File scripts/gates/Run-Gates.ps1` — deferred to user per instructions.
- [x] 6.7 **[GREEN]** Add round-trip assertion for `background_sourcing_policy` nullable column on `SocialClientData` to `ContentStudioEntityMappingTests.cs`. (`Incoders.Hive.Tests/Infrastructure/Persistence/ContentStudioEntityMappingTests.cs`)
- [x] 6.8 Run `dotnet test` — **1631 passed, 0 failed, 2 skipped**. WU-6 infra tests green.

---

## WU-7 — Advisory Concision Directives + Prompt-Version Bumps

*Refs: CG-CON-01, CG-CON-02, CG-CON-03, CG-CON-04; design §8.*

- [x] 7.1 **[RED]** Add `ConcisionDirectiveTests.cs` — assert `CarouselGenerator` assembled prompt contains the concision directive text (one-concept across slides); assert `StoryGenerator` and `FeedPostGenerator` prompts contain the super-summary directive (CG-CON-01, CG-CON-02). (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Formats/ConcisionDirectiveTests.cs`)
- [x] 7.2 **[GREEN]** Modify `GenerationPromptContext.cs` — add `public const string ConcisionConstraint = "..."` per design §8.1. (`Application/Features/Social/ContentGeneration/Shared/GenerationPromptContext.cs`)
- [x] 7.3 **[GREEN]** Modify `CarouselGenerator.cs` — append `ConcisionConstraint` to `instruction`; add per-slot length hints (e.g. `title = "string - max ~6 words"`); carousel one-concept phrasing. (`Application/Features/Social/ContentGeneration/Formats/Carousel/CarouselGenerator.cs`)
- [x] 7.4 **[GREEN]** Modify `StoryGenerator.cs` — append `ConcisionConstraint` + super-summary framing. (`Application/Features/Social/ContentGeneration/Formats/Story/StoryGenerator.cs`)
- [x] 7.5 **[GREEN]** Modify `FeedPostGenerator.cs` — append `ConcisionConstraint` + super-summary framing. (`Application/Features/Social/ContentGeneration/Formats/FeedPost/FeedPostGenerator.cs`)
- [x] 7.6 **[RED]** Add `ConcisionViolationLoggingTests.cs` — mock copy generator returning oversized slot text; assert structured `LogWarning` emitted with `{jobId, formatKey, slotName, advisoryBound, actualLength}`; assert pipeline continues (no exception, no retry) (CG-CON-03). (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Pipeline/ConcisionViolationLoggingTests.cs`)
- [x] 7.7 **[GREEN]** Modify `ContentGenerationJobProcessor.cs` — add lightweight post-copy advisory length check; `LogWarning` on violation; no `Result` failure path. (`Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs`)
- [x] 7.8 **[RED]** Add `PromptVersionBumpTests.cs` — snapshot-assert `ContentGenerationPromptVersions.Carousel = "carousel.v3"`, `Story = "story-single.v2"`, `FeedPost = "feed-single.v2"`; assert other constants unchanged (CG-CON-04). (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/PromptVersionBumpTests.cs`)
- [x] 7.9 **[GREEN]** Modify `ContentGenerationPromptVersions.cs` — bump `Carousel = "carousel.v3"`, `Story = "story-single.v2"`, `FeedPost = "feed-single.v2"`. (`Application/Features/Social/ContentGeneration/Shared/ContentGenerationPromptVersions.cs`)
- [x] 7.10 Run `dotnet test` — WU-7 concision tests green. **19 new tests passed. Total: 1672 passed, 0 failed, 2 skipped.**

---

## WU-8 — API: Policy + Color-Role Fields on Existing Endpoints

*Refs: BSP-08; design §2.4.*

- [x] 8.1 **[RED]** Add `SocialClientsEndpointPolicyTests.cs` — assert GET client response includes `backgroundSourcingPolicy`; assert PATCH with valid `"CaseA"` updates policy (BSP-08a/b); assert PATCH with invalid `"CaseX"` returns 400 (BSP-08c). (`Incoders.Hive.Tests/Api/Social/SocialClientsEndpointPolicyTests.cs`)
- [x] 8.2 **[GREEN]** Modify `MapSocialClientsEndpoints.cs` — include `backgroundSourcingPolicy` in GET response DTO; accept nullable `backgroundSourcingPolicy` on create/update; validate enum values; map to `Client.SetBackgroundSourcingPolicy`. (`ApiService/Endpoints/Social/MapSocialClientsEndpoints.cs`) Also modified: `SocialClientResponse.cs`, `CreateSocialClientCommand.cs`, `UpdateSocialClientCommand.cs`, `CreateSocialClientHandler.cs`, `UpdateSocialClientHandler.cs`.
- [x] 8.3 **[RED]** Add `ClientBrandManualsEndpointRoleTests.cs` — assert `ColorPalette` entries in GET response include `role` + `rank` fields. (`Incoders.Hive.Tests/Api/Social/ClientBrandManualsEndpointRoleTests.cs`)
- [x] 8.4 **[GREEN]** Modify `MapClientBrandManualsEndpoints.cs` — include `role` + `rank` in `ColorPalette` entry DTOs on GET/PUT. (`ApiService/Endpoints/Social/MapClientBrandManualsEndpoints.cs`) Note: `ColorEntryDto` already carried role+rank from WU-1; the endpoint already exposed `ColorEntry` (which has role+rank) directly in `ClientBrandManualResponse.ColorPalette`. No additional changes needed — the contract was already complete from WU-1 data model work.
- [x] 8.5 Run `dotnet test` + gates. **1672 passed, 0 failed, 2 skipped.** Gates deferred to user per instructions.

---

## WU-9 — Web: Policy Selector + Color-Role Ordering UI

*Refs: BSP-08d; design §2.4.*

- [x] 9.1 **[GREEN]** Update web API client types — add `backgroundSourcingPolicy?: BackgroundSourcingPolicy` to `SocialClient`; add `rank?: number` to `ColorEntry`; add `BackgroundSourcingPolicy` type. (`apps/web/lib/api/types.ts`) Also updated `SocialClientPayload` in `apps/web/lib/api/social-clients.ts`.
- [x] 9.2 **[GREEN]** Modify `client-form-panel.tsx` + `use-client-form.ts` — add policy selector (dropdown: Legacy/ClientDefinedSet/AiPalette/ClientAesthetic with human-readable labels and description); wire to update endpoint. (`apps/web/components/social/clients/client-form-panel.tsx`, `apps/web/components/social/clients/use-client-form.ts`, `apps/web/components/social/clients/clients-client.tsx`)
- [x] 9.3 **[GREEN]** Modify brand-manual color editor — replace free-text Role input with structured dropdown (Unclassified / Primary / Secondary / Accent / Neutral) + numeric rank field per color entry. (`apps/web/components/social/clients/client-brand-manual-section.tsx`)
- [x] 9.4 Frontend build + type-check must pass (`cd apps/web && npx tsc --noEmit` or equivalent). **`tsc --noEmit` exits 0. ESLint: no warnings or errors.**

---

## WU-10 — ADR + Docs Sync + Spec Reconciliation + Final Gates

*Refs: BSP-06, repo CLAUDE.md docs-sync MUST.*

- [x] 10.1 **[GREEN]** Authored ADR `docs/architecture/adr/0005-background-sourcing-strategy-port.md` documenting the strategy port, carousel coherence, structured ColorRole/rank, CASE C seam, and advisory concision.
- [x] 10.2 **[GREEN]** Created `openspec/specs/background-sourcing-policy/spec.md` (new); appended content-background-sourcing-policy delta to `openspec/specs/content-generation/spec.md`; created `openspec/specs/profile-analysis/spec.md` (new, merging profile-analysis delta).
- [x] 10.3 **[GREEN]** Updated `openspec/changes/content-background-sourcing-policy/state.yaml` — status set to `apply-complete`; `adr:` path recorded; `merged_specs:` list added.
- [ ] 10.4 **[GREEN]** Final gate run: `pwsh -File scripts/gates/Run-Gates.ps1` — deferred to user per instructions.

---

## Definition of Done

- [ ] All WU-1..WU-10 tasks completed and committed in seam order.
- [ ] `dotnet test` passes with 0 failures.
- [ ] All new ports pass `Port_HasNoInfrastructureTypes` assertion (IBackgroundSourcingStrategy, IBackgroundStrategyResolver, IClientAestheticDescriptorReader).
- [ ] `strategy.Plan(...)` is called exactly ONCE per format (verified by ProcessorPreLoopPlanTests).
- [ ] `CarouselVisualComposer` no longer imports or calls `BackgroundSelector` (verified by CarouselVisualComposerPlanTests).
- [ ] `ContentGenerationPromptVersions.Carousel = "carousel.v3"`, `Story = "story-single.v2"`, `FeedPost = "feed-single.v2"`.
- [ ] `social.clients.background_sourcing_policy` column is `smallint NULL`; all existing rows remain `NULL` after migration.
- [ ] `color_palette` jsonb backfill completed idempotently (role + rank fields present in jsonb documents).
- [ ] Architecture gate passes: no EF/Npgsql/ASP.NET types in Domain or Application.
- [ ] ADR under `docs/architecture/adr/` documents the new port family + contract change.
- [ ] `pwsh -File scripts/gates/Run-Gates.ps1` exits 0.
