# Tasks: client-aligned-carousel-generation

**Branch:** `feat/claude-p-provider` (single PR → `dev`).
**TDD mode:** STRICT — `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj -c Release`. Every behavior task: RED → GREEN.
**Size exception:** This change exceeds the 400-line review budget by design (see Forecast). The user explicitly chose `single-pr` + `size:exception`; no PR split is needed.

---

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1,200–1,800 |
| 400-line budget risk | **High** |
| Chained PRs recommended | **No** — user chose `single-pr`; `size:exception` recorded |
| Suggested split | Single PR on `feat/claude-p-provider` → `dev` |
| Delivery strategy | `single-pr` |
| Chain strategy | `size-exception` |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: size-exception
400-line budget risk: High

### Suggested Work Units

| WU | Goal | Depends on | Notes |
|----|------|-----------|-------|
| WU-1 | Capture VOs + extended profile + extractor DTO + persistence round-trip + WCAG pairing derivation | — | Foundation — all downstream WUs compile against these types |
| WU-2 | Apply structure — `ResolveFromPattern` proportional mapping + `CarouselGenerator` effective slide count | WU-1 | CAC-BSP-01/02/04, CAC-CG-01 |
| WU-3 | Apply colour — `ResolvePalette` rotating-pairing overload + thread `slideIndex` through composer | WU-1 | CAC-CG-02, CAC-BSP-03/04 |
| WU-4 | Image conditioning — `DescribeAsync` capability port + adapter + background-only directive + guardrail retry | WU-1 | CAC-IMG-01/02/03; extends existing `IImageGenerationGateway` |
| WU-5 | Composition slots — `ClientProfileSnapshot.Handle` + snapshot loader + `@handle`/signature copy slot | WU-1 | CAC-CG-03/04; additive Domain field |
| WU-6 | Observability — correlation contract + policy relax + EF migration + per-slide telemetry + DI wiring | WU-1, WU-4 | CAC-INT-01; ONE schema migration |
| WU-7 | ADR + docs/openspec spec sync + run gates | all | MUST gate; `require_adr_for_architecture_changes` |

---

## WU-1 — Capture VOs + Extended Profile + Extractor DTO + Persistence + Pairing Derivation

*Refs: CAC-BSP-01, CAC-BSP-02, CAC-BSP-03, CAC-BSP-04; design §3.1, §3.3, §0 OQ-1/OQ-2.*

### Phase 1.A — Value objects (Application — pure)

- [x] 1.1 **[RED]** Add `SlideBackgroundKindTests.cs` — assert enum members `{ Photo, FlatColor, Decorator }` exist; no infrastructure types referenced.
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/SlideBackgroundKindTests.cs`)

- [x] 1.2 **[GREEN]** Create `SlideBackgroundKind.cs` — `public enum SlideBackgroundKind { Photo, FlatColor, Decorator }`.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/SlideBackgroundKind.cs`)

- [x] 1.3 **[RED]** Add `ColorPairingTests.cs` — assert record construction `ColorPairing(BackgroundHex, TextHex, Rank)` compiles; immutable; `with` expression compiles.
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/ColorPairingTests.cs`)

- [x] 1.4 **[GREEN]** Create `ColorPairing.cs` — `public sealed record ColorPairing(string BackgroundHex, string TextHex, int Rank)`.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ColorPairing.cs`)

### Phase 1.B — Extended `ClientAestheticProfile` (additive, nullable, back-compat)

- [x] 1.5 **[RED]** Add `ClientAestheticProfileExtensionTests.cs` — assert:
  - Existing construction with no new params compiles (cold-start back-compat; CAC-BSP-04-a shape check)
  - `with { BackgroundPattern = null }` compiles and yields null field
  - `with { TypicalSlideCount = 5 }` compiles and yields `5`
  - `with { ColorPairings = [] }` compiles and yields empty list
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/ClientAestheticProfileExtensionTests.cs`)

- [x] 1.6 **[GREEN]** Modify `ClientAestheticProfile.cs` — append three trailing nullable params (after existing `CreativeSlideIndices`):
  `IReadOnlyList<SlideBackgroundKind>? BackgroundPattern = null`, `int? TypicalSlideCount = null`, `IReadOnlyList<ColorPairing>? ColorPairings = null`.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ClientAestheticProfile.cs`)

### Phase 1.C — Extended `AestheticExtraction` + extractor interface

- [x] 1.7 **[RED]** Add `AestheticExtractionExtensionTests.cs` — assert `AestheticExtraction` carries `IReadOnlyList<SlideBackgroundKind>? BackgroundPattern` and `IReadOnlyList<ColorPairing>? ColorPairings`; null-safe construction; existing construction sites compile unchanged (CAC-BSP-01-b shape check).
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/AestheticExtractionExtensionTests.cs`)

- [x] 1.8 **[GREEN]** Modify `AestheticExtraction` record in `IClientAestheticStyleExtractor.cs` — add `IReadOnlyList<SlideBackgroundKind>? BackgroundPattern = null` and `IReadOnlyList<ColorPairing>? ColorPairings = null` as trailing nullable params; all existing construction sites compile unchanged.
  (`Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IClientAestheticStyleExtractor.cs`)

### Phase 1.D — WCAG pairing derivation

- [x] 1.9 **[RED]** Add `ColorPairingDerivationTests.cs` — assert:
  - Given brand-manual `ColorRole` entries and a matching extracted `palette.Role` → `Derive(...)` returns at least one `ColorPairing` (CAC-BSP-03-a)
  - Backgrounds ranked by (role ordinal: Primary < Secondary < Accent, then by rank)
  - Text chosen by highest WCAG contrast ≥ 4.5:1; Neutral-role candidates first, else computed luminance black/white
  - Empty or null palette → empty list; no exception (CAC-BSP-03-b)
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/ColorPairingDerivationTests.cs`)

- [x] 1.10 **[GREEN]** Create `ColorPairingDerivation.cs` — static class `ColorPairingDerivation.Derive(IReadOnlyList<ColorRole> brandColors, IReadOnlyList<AestheticPaletteEntry> extractedPalette)` returns `IReadOnlyList<ColorPairing>`; implements join + rank + WCAG ≥ 4.5:1 text selection with luminance black/white fallback; confined to Application layer.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ColorPairingDerivation.cs`)

### Phase 1.E — `AestheticProfilePersistence` round-trip for new fields

- [x] 1.11 **[RED]** Add/extend `AestheticProfilePersistenceTests.cs` — assert:
  - `Write` serialises `BackgroundPattern`, `TypicalSlideCount`, and `ColorPairings` as additive JSON keys in `aesthetic_profile_json`
  - `Read` on the same blob returns matching values (CAC-BSP-02-c)
  - `Read` on an old blob without the new keys returns null for each new field; no exception (CAC-BSP-02-b / cold-start)
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/AestheticProfilePersistenceTests.cs`)

- [x] 1.12 **[GREEN]** Modify `AestheticProfilePersistence.cs` — add `backgroundPattern`, `typicalSlideCount`, and `colorPairings` keys to the serialisation/deserialisation round-trip; absent keys deserialise to null.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/AestheticProfilePersistence.cs`)

### Phase 1.F — `MultimodalAestheticStyleExtractor` DTO mapping (Infrastructure)

- [x] 1.13 **[RED]** Add/extend `MultimodalAestheticStyleExtractorTests.cs` — assert:
  - Given AI JSON containing `slideMixPattern: ["photo","flat","photo"]` → mapped `BackgroundPattern = [Photo, FlatColor, Photo]` (CAC-BSP-01-a)
  - Given AI JSON with no `slideMixPattern` field → `BackgroundPattern = null`; extraction completes (CAC-BSP-01-b)
  - Given joinable `colorPairings` signal → `ColorPairings` derived via `ColorPairingDerivation.Derive`
  (`Incoders.Hive.Tests/Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractorTests.cs`)

- [x] 1.14 **[GREEN]** Modify `MultimodalAestheticStyleExtractor.cs` — extend response DTO with `slideMixPattern`; map to `IReadOnlyList<SlideBackgroundKind>`; call `ColorPairingDerivation.Derive` using brand colors + extracted palette; populate `AestheticExtraction.BackgroundPattern` and `.ColorPairings`.
  (`Incoders.Hive.Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs`)

- [x] 1.15 Run `dotnet test -c Release` — WU-1 tests pass; existing suite green (cold-start: all-null profile → identical output to pre-change baseline).
  Result: Failed: 0, Passed: 2031, Skipped: 2, Total: 2033 — exit code 0.

---

## WU-2 — Apply Structure: Pattern-Aware Resolver + Effective Slide Count

*Refs: CAC-BSP-01, CAC-BSP-02, CAC-BSP-04, CAC-CG-01; design §3.2, §0 OQ-1/OQ-3.*

### Phase 2.A — `CreativeSlideIndexResolver.ResolveFromPattern`

- [ ] 2.1 **[RED]** Add/extend `CreativeSlideIndexResolverTests.cs` — assert:
  - `ResolveFromPattern(null, 5)` → `[1, 5]` ({first,last} default; CAC-BSP-04-a)
  - `ResolveFromPattern([], 5)` → `[1, 5]` (empty pattern → same default)
  - `ResolveFromPattern([Photo,FlatColor,Photo,FlatColor,Photo], 5)` → `[1, 3, 5]` capped to `[1, 3]` (1:1; cap 2; CAC-BSP-02-a shape)
  - `ResolveFromPattern([Photo,FlatColor,Photo], 5)` → proportional map (`floor((i-1)*3/5)` for i=1..5); Photo indices deduped, capped to 2 (OQ-1 design §3.2)
  - Photo-only pattern with cap applied → at most 2 creative indices returned
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/CreativeSlideIndexResolverTests.cs`)

- [ ] 2.2 **[GREEN]** Add `ResolveFromPattern(IReadOnlyList<SlideBackgroundKind>? pattern, int slideCount)` overload to `CreativeSlideIndexResolver.cs` — null/empty delegates to existing `Resolve`; equal-length = 1:1; otherwise `floor((i-1)*pattern.Count/slideCount)` indexing; returns Photo positions deduped + capped to `MaxCreativeSlidesPerPackage`.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Background/CreativeSlideIndexResolver.cs`)

### Phase 2.B — `CarouselGenerator` effective slide count

- [ ] 2.3 **[RED]** Add `CarouselGeneratorSlideCountTests.cs` — assert:
  - Explicit user-requested count = 4 with `TypicalSlideCount = 7` → effective count = 4 (user wins; CAC-CG-01-a variant)
  - No user request, `TypicalSlideCount = 7` → effective count = 7 (CAC-CG-01-a)
  - No user request, `TypicalSlideCount = 15` → effective count = 10 (clamped; OQ-3)
  - No user request, `TypicalSlideCount = 1` → effective count = 3 (clamped; OQ-3)
  - No user request, `TypicalSlideCount = null` → existing default slide range applied (CAC-CG-01-b)
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Formats/Carousel/CarouselGeneratorSlideCountTests.cs`)

- [ ] 2.4 **[GREEN]** Modify `CarouselGenerator.cs` — replace hardcoded `~3-5` range with: `effective = userRequested ?? clamp(profile.TypicalSlideCount, 3..10) ?? currentDefault`; use `effective` as the target slide count.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Formats/Carousel/CarouselGenerator.cs`)

- [ ] 2.5 Run `dotnet test -c Release` — WU-2 tests pass; no regression.

---

## WU-3 — Apply Colour: Rotating Pairing Overload + Composer Wiring

*Refs: CAC-CG-02, CAC-BSP-03, CAC-BSP-04; design §3.3, §0 OQ-2.*

### Phase 3.A — `CompositionDefaults.ResolvePalette` pairing/slide-index overload

- [ ] 3.1 **[RED]** Add `CompositionDefaultsPairingTests.cs` — assert:
  - Given `ColorPairings = [{Bg:"#1A1A2E", Text:"#E0E0E0", Rank:1}]` and `slideIndex = 2` → palette background = `#1A1A2E`, text = `#E0E0E0` (CAC-CG-02-a; rotation `(2-1) % 1 = 0`)
  - Given 3 pairings and `slideIndex = 4` → `(4-1) % 3 = 0` → first pairing applied
  - Given `ColorPairings = null` and any `slideIndex` → existing positional palette unchanged (CAC-CG-02-b; CAC-BSP-04-a parity)
  - Given `ColorPairings = []` → existing positional palette unchanged
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Visual/CompositionDefaultsPairingTests.cs`)

- [ ] 3.2 **[GREEN]** Add `ResolvePalette(BrandSnapshot brand, ClientAestheticProfile? profile, int slideIndex, InferenceAccumulator inferred)` overload to `CompositionDefaults.cs` — when `profile?.ColorPairings` non-empty: select `pairings[(slideIndex-1) % count]`; return palette with `Background = p.BackgroundHex`, `Text = p.TextHex` and remaining roles from brand/defaults; else delegate to existing `ResolvePalette(brand, inferred)`.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Visual/CompositionDefaults.cs`)

### Phase 3.B — Thread `slideIndex` through `CarouselVisualComposer`

- [ ] 3.3 **[RED]** Add/extend `CarouselVisualComposerPaletteTests.cs` — assert:
  - Composer calls `ResolvePalette` with the 1-based `slideIndex` of each slide (not the array position)
  - Slide 1 and slide 2 in a 2-pairing set receive different palette backgrounds
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Visual/CarouselVisualComposerPaletteTests.cs`)

- [ ] 3.4 **[GREEN]** Modify `CarouselVisualComposer.cs` — pass the 1-based `slideIndex` to `ResolvePalette` for each slide in `ComposeMany`; no structural changes to other composition logic.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Visual/Composers/CarouselVisualComposer.cs`)

- [ ] 3.5 Run `dotnet test -c Release` — WU-3 tests pass; colour parity test (null pairings → positional palette; identical to pre-change output).

---

## WU-4 — Image Conditioning: Capability Port + Adapter + Background-Only + Guardrail Retry

*Refs: CAC-IMG-01, CAC-IMG-02, CAC-IMG-03; design §3.4, §0 OQ-4.*

### Phase 4.A — Port extensions (Application)

- [ ] 4.1 **[RED]** Add/extend `IImageGenerationGatewayContractTests.cs` — assert:
  - `IImageGenerationGateway.DescribeAsync(Guid organizationId, CancellationToken)` returns `Task<Result<ImageModelCapabilities>>`
  - `ImageModelCapabilities` is a record with `bool SupportsImageConditioning` and `int MaxConditioningImages`; no Infrastructure types
  - `ImageGenerationRequest.ReferenceImages` is `IReadOnlyList<ImageInitReference>?`; existing params unchanged
  - `ImageInitReference` carries only `byte[]? Bytes`, `string? Url`, `string Mime` (Application-safe)
  (`Incoders.Hive.Tests/Application/IImageGenerationGatewayContractTests.cs`)

- [ ] 4.2 **[GREEN]** Modify `IImageGenerationGateway.cs` — add `Task<Result<ImageModelCapabilities>> DescribeAsync(Guid organizationId, CancellationToken ct = default)`. Create `ImageModelCapabilities` sealed record `(bool SupportsImageConditioning, int MaxConditioningImages)`.
  (`Incoders.Hive.Application/Abstractions/Ai/IImageGenerationGateway.cs`)

- [ ] 4.3 **[GREEN]** Add `IReadOnlyList<ImageInitReference>? ReferenceImages = null` as a trailing nullable param to `ImageGenerationRequest` sealed record; existing `Init` param kept for back-compat.
  (`Incoders.Hive.Application/Abstractions/Ai/IImageGenerationGateway.cs`)

### Phase 4.B — Adapter capability advertisement + byte resolution (Infrastructure)

- [ ] 4.4 **[RED]** Add `ImageModelClientCapabilityTests.cs` — assert:
  - Supporting adapter returns `SupportsImageConditioning = true` and a positive `MaxConditioningImages` from `Capabilities` without making a provider call
  - Non-supporting adapter (or a second adapter type) returns `SupportsImageConditioning = false`
  - `IImageModelClient.Capabilities` is a property, not a method call to the provider
  (`Incoders.Hive.Tests/Infrastructure/Ai/ImageModelClientCapabilityTests.cs`)

- [ ] 4.5 **[GREEN]** Modify the supporting `IImageModelClient` adapter (Infrastructure) — add `ImageModelCapabilities Capabilities { get; }` to the port interface; implement it to return `SupportsImageConditioning = true` / `MaxConditioningImages = 3` (or provider-specific value); resolve `Url` reference bytes from `IGeneratedAssetStorage` when `ReferenceImages` is non-null and forwarded to the provider; non-supporting adapters return `SupportsImageConditioning = false` and ignore `ReferenceImages`.
  (`Incoders.Hive.Infrastructure/Ai/…ImageModelClient.cs`)

- [ ] 4.6 **[GREEN]** Modify `ImageGenerationGateway.cs` — implement `DescribeAsync`: resolve the IMAGE route client via `IWorkflowConfigurationResolver` + `IExternalProviderClientFactory`; return client's `Capabilities` without calling the provider.
  (`Incoders.Hive.Infrastructure/Ai/ImageGenerationGateway.cs`)

### Phase 4.C — Background-only directive + guardrail retry (Infrastructure + Application)

- [ ] 4.7 **[RED]** Add `BackgroundOnlyDirectiveTests.cs` — assert:
  - On a `ClientAesthetic` creative slide the assembled `ImageGenerationRequest` prompt includes a system directive "generate ONLY the background; never render text, logos, @handles, or signatures" (CAC-IMG-02-a)
  - Non-`ClientAesthetic` slides do NOT receive the background-only directive (CAC-IMG-02-b)
  (`Incoders.Hive.Tests/Infrastructure/Ai/BackgroundOnlyDirectiveTests.cs`)

- [ ] 4.8 **[RED]** Add `VisualGuardrailRetryTests.cs` (or extend existing) — assert:
  - First render passes `VisualGuardrailValidator` → result accepted; no retry (CAC-IMG-03-a)
  - First render fails; second render passes within budget → second result accepted (CAC-IMG-03-b)
  - All retries exhausted without passing → slide degrades to CSS fallback; failure logged with job id, slide index, reason; job not marked failed (CAC-IMG-03-c)
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/VisualGuardrailRetryTests.cs`)

- [ ] 4.9 **[GREEN]** Modify the `IImageModelClient` adapter (Infrastructure) — inject background-only system directive for `ClientAesthetic` creative slides.
  (`Incoders.Hive.Infrastructure/Ai/…ImageModelClient.cs`)

### Phase 4.D — Background service capability-gated conditioning (Application)

- [ ] 4.10 **[RED]** Add `ClientAestheticImageBackgroundServiceConditioningTests.cs` — assert:
  - `DescribeAsync` called ONCE per job (not per slide)
  - `SupportsImageConditioning = true` + reference images present → `ReferenceImages` populated with up to `min(MaxConditioningImages, 3)` `ImageInitReference` Url handles (CAC-IMG-01-a)
  - `SupportsImageConditioning = false` → `ReferenceImages = null`; descriptor prompt drives generation (CAC-IMG-01-b)
  - `SupportsImageConditioning = true` but no reference images on profile → `ReferenceImages = null`; generation continues (CAC-IMG-01-c)
  - `VisualGuardrailValidator` applied per slide with bounded retry; CSS fallback on exhaustion (CAC-IMG-03-c)
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/ClientAestheticImageBackgroundServiceConditioningTests.cs`)

- [ ] 4.11 **[GREEN]** Modify `ClientAestheticImageBackgroundService.cs` — call `gateway.DescribeAsync(orgId)` once before the per-slide loop; build `ImageInitReference` Url handles from `profile.ReferenceImages` (cap `min(MaxConditioningImages, 3)`); set `ReferenceImages` on the request only when `SupportsImageConditioning`; apply `VisualGuardrailValidator` with bounded retry per creative slide; on retry exhaustion: emit per-slide CSS fallback + log.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Background/ClientAestheticImageBackgroundService.cs`)

- [ ] 4.12 Run `dotnet test -c Release` — WU-4 tests pass; no regression.

---

## WU-5 — Composition Slots: `@handle`/Signature + Snapshot Loader

*Refs: CAC-CG-03, CAC-CG-04; design §2.1, §2.3, §0 OQ-7.*

### Phase 5.A — `ClientProfileSnapshot.Handle` (Domain)

- [ ] 5.1 **[RED]** Add `ClientProfileSnapshotHandleTests.cs` — assert `ClientProfileSnapshot.Handle` is `string?`; existing construction with no Handle param compiles; `with { Handle = "@brand" }` compiles.
  (`Incoders.Hive.Tests/Domain/Social/ContentGeneration/ClientProfileSnapshotHandleTests.cs`)

- [ ] 5.2 **[GREEN]** Modify `ClientProfileSnapshot.cs` — add `string? Handle = null` as a trailing nullable field; no existing param order or defaults changed.
  (`Incoders.Hive.Domain/Social/ContentGeneration/…ClientProfileSnapshot.cs`)

### Phase 5.B — Snapshot loader (Infrastructure)

- [ ] 5.3 **[RED]** Add/extend `BrandSnapshotLoaderTests.cs` — assert:
  - Given `ClientOwnAccount.Handle = "@brand"` → `ClientProfileSnapshot.Handle = "@brand"` (CAC-CG-04-a)
  - Given no own account → `Handle = null`; snapshot assembly succeeds (CAC-CG-04-b)
  - No Infrastructure or provider type appears in `ClientProfileSnapshot` or in the loader port signature
  (`Incoders.Hive.Tests/Infrastructure/Social/ContentGeneration/BrandSnapshotLoaderTests.cs`)

- [ ] 5.4 **[GREEN]** Modify brand-snapshot loader (Infrastructure) — load `ClientOwnAccount.Handle` into `ClientProfileSnapshot.Handle`; handle no-own-account case as null.
  (`Incoders.Hive.Infrastructure/Social/ContentGeneration/…SnapshotLoader.cs`)

### Phase 5.C — `@handle`/signature copy slot in `CarouselVisualComposer`

- [ ] 5.5 **[RED]** Add `CarouselVisualComposerHandleSlotTests.cs` — assert:
  - `BrandSnapshot.Handle = "@clientbrand"` → composition envelope carries `@handle` slot = `"@clientbrand"` at parity with `StoryVisualComposer` (CAC-CG-03-a)
  - `BrandSnapshot.Handle = null` → handle/signature slot absent; no exception; slide renders normally (CAC-CG-03-b)
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Visual/CarouselVisualComposerHandleSlotTests.cs`)

- [ ] 5.6 **[GREEN]** Modify `CarouselVisualComposer.cs` — add `handle`/`signature` copy slots via `AttributionPolicy.ResolveSignature`; populate when `Handle` non-null; omit slot (not empty string) when null; mirrors `StoryVisualComposer` pattern exactly.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Visual/Composers/CarouselVisualComposer.cs`)

- [ ] 5.7 Run `dotnet test -c Release` — WU-5 tests pass; handle-absent parity test passes.

---

## WU-6 — Observability: Correlation Contract + Policy Relax + EF Migration + Per-Slide Telemetry + DI

*Refs: CAC-INT-01; design §3.5, §0 OQ-6.*

### Phase 6.A — Correlation contract (Application)

- [ ] 6.1 **[RED]** Add `IntegrationExecutionCorrelationTests.cs` — assert:
  - `IntegrationExecutionAttemptRecord` carries `Guid? CorrelationId = null` and `string? CorrelationKind = null` as additive nullable trailing fields
  - `Guid? AnalysisId` is now nullable (not required); existing records with `AnalysisId` compile unchanged
  - `IntegrationExecutionRequest` gains the same two nullable fields
  (`Incoders.Hive.Tests/Application/Integrations/IntegrationExecutionCorrelationTests.cs`)

- [ ] 6.2 **[GREEN]** Modify `IntegrationExecutionAttemptRecord` and `IntegrationExecutionRequest` in `IIntegrationExecutionAttemptRepository.cs` — make `AnalysisId` nullable (`Guid?`); add trailing `Guid? CorrelationId = null` and `string? CorrelationKind = null`.
  (`Incoders.Hive.Application/Abstractions/Integrations/IIntegrationExecutionAttemptRepository.cs`)

### Phase 6.B — Policy persistence relax (Application)

- [ ] 6.3 **[RED]** Add/extend `IntegrationExecutionPolicyTests.cs` — assert:
  - `AnalysisId` non-null → attempt persisted (existing behaviour)
  - `AnalysisId = null`, `CorrelationId` non-null → attempt persisted (CAC-INT-01-a; new path)
  - Both null → attempt NOT persisted (guard preserved)
  - Persistence write failure → caught, logged, not propagated; job continues (CAC-INT-01-c)
  (`Incoders.Hive.Tests/Application/Integrations/IntegrationExecutionPolicyTests.cs`)

- [ ] 6.4 **[GREEN]** Modify `IntegrationExecutionPolicy.PersistAttemptAsync` — change guard to `if (request.AnalysisId is not null || request.CorrelationId is not null)`; wrap write in try/catch; log on failure; do not propagate.
  (`Incoders.Hive.Infrastructure/Integrations/IntegrationExecutionPolicy.cs`)

### Phase 6.C — EF entity + migration (Infrastructure — the ONE schema migration)

- [ ] 6.5 **[RED]** Add `IntegrationExecutionAttemptMigrationTests.cs` — assert:
  - `IntegrationExecutionAttemptData.AnalysisId` is nullable in the EF entity
  - Entity carries `CorrelationId` (`Guid?`) and `CorrelationKind` (`string?`) columns
  - Round-trip: persisted row with `AnalysisId = null` and `CorrelationId` non-null is readable with correct values
  - Existing analysis-scoped rows (non-null `AnalysisId`) are unaffected
  (`Incoders.Hive.Tests/Infrastructure/Integrations/IntegrationExecutionAttemptMigrationTests.cs`)

- [ ] 6.6 **[GREEN]** Modify `IntegrationExecutionAttemptData.cs` + EF config — make `AnalysisId` nullable; add `CorrelationId` (`Guid?`) and `CorrelationKind` (`string?`) columns.
  (`Incoders.Hive.Infrastructure/Persistence/Postgres/…IntegrationExecutionAttemptData.cs`)

- [ ] 6.7 **[GREEN]** Author additive EF migration — make `integration_execution_attempts.analysis_id` nullable (relax FK to optional); add nullable `correlation_id` (uuid) and `correlation_kind` (text); additive + reversible (drop columns / re-tighten on rollback).
  (`Incoders.Hive.Infrastructure/Persistence/Postgres/Migrations/`)

### Phase 6.D — Per-slide degradation rows from background service (Application)

- [ ] 6.8 **[RED]** Add `PerSlideDegradationTelemetryTests.cs` — assert:
  - Successful image-gen → one attempt row with `outcome = success`, `slideIndex`, `step = "generate-image"` (CAC-INT-01-a)
  - CSS fallback (image-gen exhausted) → one attempt row with `outcome = "degraded-to-css"` + failure reason (CAC-INT-01-b)
  - Telemetry write failure → caught and logged; generation job continues; no job-level failure (CAC-INT-01-c)
  - `job.Status` remains `Completed`; `error_code` remains null regardless of how many slides degrade
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/PerSlideDegradationTelemetryTests.cs`)

- [ ] 6.9 **[GREEN]** Modify `ClientAestheticImageBackgroundService.cs` — for each creative slide: on success emit attempt row `{ CorrelationId = FormatResultId, CorrelationKind = "content-generation/generate-image", status = success, DiagnosticsJson = { slideIndex } }`; on CSS fallback emit `{ status = Failed, DiagnosticsJson = { slideIndex, reason } }`; wrap telemetry write in try/catch.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Background/ClientAestheticImageBackgroundService.cs`)

### Phase 6.E — Gateway correlation thread + DI wiring (Infrastructure)

- [ ] 6.10 **[GREEN]** Modify `ImageGenerationGateway.cs` — thread `CorrelationId`/`CorrelationKind` from `ImageGenerationRequest` into the execution policy call; ensure `IntegrationExecutionPolicy` is wired on the content-generation image path (not only on the analysis path).
  (`Incoders.Hive.Infrastructure/Ai/ImageGenerationGateway.cs`)

- [ ] 6.11 **[GREEN]** Modify `DependencyInjection.cs` — wire `IntegrationExecutionPolicy` into the `ImageGenerationGateway` on the content-generation path; register any new service dependencies introduced in WU-6.
  (`Incoders.Hive.Infrastructure/DependencyInjection/DependencyInjection.cs`)

### Phase 6.F — Processor integration + cold-start parity

- [ ] 6.12 **[RED]** Add/extend processor integration tests (reuse existing `ContentGenerationJobProcessorVisualTests` harness) — assert:
  - Full `ClientAesthetic` carousel with all capture fields populated → pattern indices used, rotating pairings applied, conditioning active, handle/signature slot present
  - Cold-start (all profile fields null) → `{first,last}` indices, positional palette, descriptor background, no handle slot; output identical to pre-change baseline (parity; CAC-BSP-04-a)
  - Image-gen failure on one slide → CSS fallback on that slide + telemetry row + job `Completed` (resilience)
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Pipeline/ProcessorClientAlignedCarouselTests.cs`)

- [ ] 6.13 Run `dotnet test -c Release` — WU-6 tests pass; full suite green.

---

## WU-7 — ADR + Docs/OpenSpec Spec Sync + Gates

*Refs: design §10; `openspec/config.yaml` `require_adr_for_architecture_changes: true`; repo `CLAUDE.md` docs-sync MUST.*

- [ ] 7.1 **[GREEN]** Author ADR `docs/architecture/adr/0008-image-conditioning-capability-describe-telemetry-correlation.md` — document:
  - Extended image-conditioning contract (`IImageGenerationGateway.DescribeAsync` + `ImageModelCapabilities`); rationale: capability advertised declaratively on the adapter, no DB flag, provider-agnostic
  - `ImageGenerationRequest.ReferenceImages` (Url handles; bytes resolved inside adapter); rationale: Application layer stays free of byte-hauling
  - Cap `MaxConditioningImages = 3`; rationale: bounds vision-token cost reusing curated reference set
  - Telemetry correlation contract (`CorrelationId`/`CorrelationKind`); rationale: decouples image-gen telemetry from single-analysis FK; surfaces silent degradation without changing completion semantics
  - Architecture gate compliance: no provider types in Domain/Application; bytes resolved in adapter
  (`docs/architecture/adr/0008-image-conditioning-capability-describe-telemetry-correlation.md`)

- [ ] 7.2 **[GREEN]** Merge spec deltas into living specs:
  - `openspec/changes/client-aligned-carousel-generation/specs/background-sourcing-policy/spec-delta.md` → append to `openspec/specs/background-sourcing-policy/spec.md`
  - `openspec/changes/client-aligned-carousel-generation/specs/content-generation/spec-delta.md` → append to `openspec/specs/content-generation/spec.md`
  - `openspec/changes/client-aligned-carousel-generation/specs/image-generation/spec-delta.md` → append to `openspec/specs/image-generation/spec.md`
  - `openspec/changes/client-aligned-carousel-generation/specs/integrations/spec-delta.md` → append to `openspec/specs/integrations/spec.md`

- [ ] 7.3 **[deferred to human]** Run `pwsh -File scripts/gates/Run-Gates.ps1`; report the gate summary. Do NOT mark complete if any MUST gate fails.

---

## Parallelism Notes

| Parallelism | WUs | Constraint |
|-------------|-----|-----------|
| Sequential | WU-1 → WU-2, WU-3, WU-4, WU-5, WU-6 | WU-1 types are the compiler gate for all downstream WUs |
| WU-2, WU-3, WU-5 can start in parallel once WU-1 is done | WU-2, WU-3, WU-5 | Independent seams: resolver, palette, handle slot |
| WU-4 can start once WU-1 is done | WU-4 | Extends existing `IImageGenerationGateway` |
| WU-6 requires WU-1 + WU-4 complete | WU-6 | Background service conditioning (WU-4) must exist before telemetry wiring |
| WU-7 requires all prior WUs complete | WU-7 | Final ADR + gate run |

---

## Definition of Done

- [ ] WU-1..WU-7 tasks completed and committed in seam order (work-unit commits: test + code in the same commit per behavior).
- [ ] `dotnet test -c Release` passes with 0 failures across the full suite.
- [ ] Cold-start parity test confirms: all-null `ClientAestheticProfile` → output identical to pre-change baseline (no regression on existing clients).
- [ ] Port contract test confirms: no Infrastructure type in `IImageGenerationGateway`, `ImageModelCapabilities`, `ImageGenerationRequest.ReferenceImages`, or `IntegrationExecutionAttemptRecord`.
- [ ] Processor integration test confirms: full capture path active + resilience (image-gen failure → CSS + telemetry + `Completed`).
- [ ] ONE EF migration (`analysis_id` nullable + `correlation_id`/`correlation_kind`) is present and reversible; existing analysis-scoped rows unaffected.
- [ ] No provider/HTTP/EF/Supabase type appears in Domain or Application projects (architecture gate).
- [ ] ADR `0008-image-conditioning-capability-describe-telemetry-correlation.md` authored under `docs/architecture/adr/`.
- [ ] Spec deltas merged into `openspec/specs/` living files.
- [ ] `pwsh -File scripts/gates/Run-Gates.ps1` exits 0; no MUST gate failure.
- [ ] `size:exception` recorded (this file, Review Workload Forecast). Single PR on `feat/claude-p-provider` → `dev`.
