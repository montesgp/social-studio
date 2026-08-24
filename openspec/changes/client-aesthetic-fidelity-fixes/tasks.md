# Tasks: ClientAesthetic (CASE C) Fidelity Fixes

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~2660 total (~2540 code + ~120 docs) across 8 units |
| 400-line budget risk | High per-unit vs. default 400; Medium against session's 800-line budget |
| Chained PRs recommended | No |
| Suggested split | 8 sequential commits to `dev`: WU-0, WU-1a, WU-1b, WU-2a, WU-2b, WU-3a, WU-3b, WU-4 |
| Delivery strategy | single-pr (direct sequential commits to `dev`, tracked by one GitHub issue) |
| Chain strategy | n/a — no chaining |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Medium

### Suggested Work Units

| Unit | Goal | Focused test command | Runtime harness | Rollback boundary |
|------|------|----------------------|------------------|--------------------|
| WU-0 | ComfyUI findings report (docs only) | N/A — no code | N/A — documentation | Delete the report file |
| WU-1a | Trigger A + B degrade routing, scrim-card removal for ClientAesthetic | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~ContentGenerationJobProcessorVisualTests` | Forced-failure fixtures (`FixedResultImageService`, `ActiveFailingDesigner`) | Revert routing; scrim card restores, FlatColor short-circuit untouched |
| WU-1b | Envelope `DegradeReason` marker + serialization tests | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~CompositionSpecJsonTests` | Serialization round-trip test | Remove trailing member; marker stops being written |
| WU-2a | Typography extraction: prompt + DTO + `Map()` | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~MultimodalAestheticStyleExtractor` | Extractor unit tests | Revert DTO/prompt fields; `Typography` stays null |
| WU-2b | Typography render: `CompositionTypography` caption + `ResolveTypography` + markup builders + `AllowedFonts` | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~CompositionDefaults` | Composition unit tests | Caption members init-defaulted; independently revertible |
| WU-3a | Per-slide transparency: extractor + DTO + `Map()` + persistence + profile round-trip | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~AestheticProfilePersistence` | Persistence round-trip tests | Schema inert without WU-3b consumer |
| WU-3b | Per-slide transparency render: design context + `BuildTemplateBackedMarkup` opacity | `dotnet test Incoders.Hive.Tests --filter FullyQualifiedName~BuildTemplateBackedMarkupTests` | Render test asserting slide-2-only opacity | Conditional on null signal; reverts independently |
| WU-4 | Content Studio degrade badge | `npm run test -- content-studio-panel` (apps/web) | Vitest component/round-trip test | Absent marker renders no badge either way |

---

## WU-0 — ComfyUI Findings Report

- [x] 0.1 Create `openspec/changes/client-aesthetic-fidelity-fixes/comfyui-generation-time-report.md`: document that the two 10-min failures match `ComfyUiSettings.DefaultTimeout=600000ms`, `DefaultSteps=20`/`DefaultCfg=7.0` are standard, ~9m35s attributable to `--directml --lowvram --cpu-vae`; state explicitly **no code fix recommended**. Satisfies: Scope item 6, Success Criteria "ComfyUI findings report".

## WU-1a — Trigger A + B Degrade Routing (D1)

- [ ] 1a.1 RED — extend `Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/ClientAestheticDegradeDecisionTests.cs` (new file) with the `IsEligible`/`TriggerA`/`TriggerB` truth table (policy × layout × mappedKind × hasGeneratedImage × hasPairings, incl. non-ClientAesthetic → `None`). Satisfies CAFF-CG-01/02/03 scenarios.
- [ ] 1a.2 GREEN — create `Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ClientAestheticDegradeDecision.cs`: `SlideDegradeTrigger` enum + `IsEligible`/`TriggerA`/`TriggerB` per D1.
- [ ] 1a.3 RED — extend `Incoders.Hive.Tests/.../Pipeline/ProcessorImageBackgroundResilienceTests.cs` reusing `FixedResultImageService` (L385) with a partial map to assert trigger A: flat treatment, zero `AiVisualDesigner.ComposeAsync` calls. Satisfies CAFF-CG-01.
- [ ] 1a.4 RED — extend `Incoders.Hive.Tests/.../Pipeline/ContentGenerationJobProcessorVisualTests.cs` reusing `ActiveFailingDesigner` (L704), `FailingGuardrailValidator` (L641), `FC_NeverCalledDesigner` (L1435): assert trigger B flat treatment + no scrim; assert non-ClientAesthetic keeps scrim (CAFF-CG-02 regression); assert FlatColor never reaches either trigger (CAFF-CG-03). Satisfies CAFF-CG-02, CAFF-CG-03.
- [ ] 1a.5 GREEN — modify `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` (~L807-954): add `RenderFaithfulFlat` local helper, branch per design §1, remove unconditional scrim-card render for ClientAesthetic-eligible slides only.

## WU-1b — Envelope Degrade Marker (D2)

- [ ] 1b.1 RED — extend `Incoders.Hive.Tests/.../Pipeline/CompositionSpecJsonTests.cs`: non-degraded envelope JSON has no `degradeReason` key (byte-identity); degraded envelope round-trips token; legacy blob parses with `DegradeReason == null`. Satisfies CAFF-CG-04.
- [ ] 1b.2 GREEN — modify `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/CompositionSpecEnvelope.cs`: add trailing `string? DegradeReason = null` with `[property: JsonIgnore(Condition = WhenWritingNull)]`.
- [ ] 1b.3 GREEN — modify `ContentGenerationJobProcessor.cs`: declare `capturedDegradeReason`, assign `"photo-image-unavailable"`/`"designer-unusable"` tokens in the two degrade branches, pass at envelope construction (~L1153).
- [ ] 1b.4 RED+GREEN — extend the `CompositionSpecJsonPersistenceTests` pattern (`StubMarkupDesigner`/`AlwaysFailingGuardrails`) to assert the marker is written on exactly the degraded slides, and export tests assert no `degradeReason` text leaks into PPTX/ZIP output. Satisfies CAFF-CG-04, Risks row "marker leaks into exports".

## WU-2a — Typography Extraction (D4 prompt/DTO half)

- [ ] 2a.1 RED — extend `Incoders.Hive.Tests` extractor tests for `MultimodalAestheticStyleExtractor` covering: all-three-roles detected; heading+body only (caption null, regression); none detected (cold start). Satisfies CAFF-PA-01.
- [ ] 2a.2 GREEN — modify `Incoders.Hive.Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs`: extend prompt + DTO with caption/overline role, defensive `Map()`.
- [ ] 2a.3 GREEN — modify `Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IClientAestheticStyleExtractor.cs`: no new contract shape needed beyond existing `AestheticExtraction.Typography`; verify role-keyed carry-through compiles.
- [ ] 2a.4 RED+GREEN — extend persistence round-trip tests for `AestheticProfilePersistence`/`ClientAestheticProfile.Typography`: 3-role round-trip; pre-change blob (no caption key) reads caption null, no exception. Satisfies CAFF-PA-02.

## WU-2b — Typography Render (D4 render half + D7)

- [ ] 2b.1 RED — extend `Incoders.Hive.Tests` `CompositionDefaults` typography tests: caption present/absent/overline-alias/accent-alias/caption-only (must still early-out). Satisfies CAFF-CG-06 (Typography on context).
- [ ] 2b.2 GREEN — modify `Incoders.Hive.Domain/Social/ContentGeneration/VisualCompositionSpec.cs`: add `CaptionFamily`, `CaptionSize`, `CaptionWeight=800`, `CaptionItalic`, `RequestedCaptionFamily`, `EffectiveCaptionFamily` to `CompositionTypography`.
- [ ] 2b.3 GREEN — modify `Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/AestheticTypography.cs`: add `TypographyRoles.Caption`/`.Overline`.
- [ ] 2b.4 GREEN — modify `CompositionDefaults.cs` (`ResolveTypography` 4-arg, L159-199): resolve `Caption ?? Overline ?? Accent` role.
- [ ] 2b.5 GREEN — modify `ContentGenerationJobProcessor.cs` markup builders (`BuildCarouselTemplateSlots`, `BuildFlatCarouselSlots`, `BuildStoryTemplateSlots`): consume `EffectiveCaptionFamily`/`CaptionWeight`/`CaptionSize` for `cta`/`signature` slots.
- [ ] 2b.6 GREEN — modify `ClientAestheticDesignContext.cs` (built at processor L469-481): add `Typography` field.
- [ ] 2b.7 RED — extend tests asserting `AllowedFonts` reflects client-derived classes when populated, and legacy brand-kit fallback when absent (regression). Satisfies CAFF-CG-07.
- [ ] 2b.8 GREEN — modify `Incoders.Hive.Infrastructure/Social/ContentGeneration/AiVisualDesigner.cs` (L286): `AllowedFonts = clientClasses.Concat(brand.Typography).Distinct(OrdinalIgnoreCase)` per D7; update `TypographyNormalizer` effective-family-set call site.

## WU-3a — Per-Slide Transparency: Extraction + Persistence (D3)

- [ ] 3a.1 RED — extend extractor tests: consistent per-slide blended habit at indices 1,3; no-treatment cold start; asserts boolean+descriptor only, never numeric alpha. Satisfies CAFF-PA-03.
- [ ] 3a.2 GREEN — modify `MultimodalAestheticStyleExtractor.cs`: prompt + DTO `slideBackgroundTransparency` + defensive `Map()`.
- [ ] 3a.3 GREEN — modify `IClientAestheticStyleExtractor.cs`: add `AestheticExtraction.BackgroundTransparency` (trailing, nullable).
- [ ] 3a.4 RED — new `BackgroundTransparencyReconcilerTests.cs`: shorter/longer/equal/empty/null arrays pad-with-null/truncate, never throws. Satisfies CAFF-PA-04.
- [ ] 3a.5 GREEN — create `Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/SlideBackgroundTransparency.cs` (`bool Blended, string? Descriptor`) and `BackgroundTransparencyReconciler.cs` (`Align`), modeled on `BackgroundPatternReconciler`.
- [ ] 3a.6 GREEN — modify `AestheticProfilePersistence.cs`: `StoredSlideTransparency(bool?, string?)` under `backgroundTransparency` key, trailing `Read` element; modify `ClientAestheticProfile.cs` with trailing nullable list. Wire `Align` at extractor `Map()` site.
- [ ] 3a.7 RED+GREEN — round-trip persistence tests: populated round-trip preserves values; legacy blob without key reads null. Satisfies CAFF-PA-02/03 persistence half.

## WU-3b — Per-Slide Transparency: Render (D3 render + D5)

- [ ] 3b.1 RED — extend `CreativeSlideIndexResolver` tests with `ResolveTransparencyForSlide` sibling to `ResolveKindForSlide`, proportional formula parity. Satisfies CAFF-CG-05, CAFF-CG-06.
- [ ] 3b.2 GREEN — modify `Incoders.Hive.Application/Features/Social/ContentGeneration/Background/CreativeSlideIndexResolver.cs`: add `ResolveTransparencyForSlide`.
- [ ] 3b.3 GREEN — modify `ClientAestheticDesignContext.cs`: add per-slide transparency signal field (built at processor L469-481).
- [ ] 3b.4 GREEN — modify `CompositionDefaults.cs`: add `BlendedPhotoOpacity = 0.72` constant (D5).
- [ ] 3b.5 RED — extend `BuildTemplateBackedMarkupTests`: signal true on slide index 2 only → `<img>` carries `opacity:0.72` over `ResolvePalette` backdrop; every other slide keeps unchanged `object-fit:cover`, no opacity token; no-signal cold start unchanged. Satisfies CAFF-CG-05.
- [ ] 3b.6 GREEN — modify `ContentGenerationJobProcessor.cs` `BuildTemplateBackedMarkup` (~L1891-1989): add optional `SlideBackgroundTransparency? transparency = null` param; caller resolves per `specIndex`.

## WU-4 — Content Studio Degrade Badge (D6, depends on WU-1b)

- [ ] 4.1 RED — extend Vitest suite for `apps/web/components/social/content-studio-panel.tsx`: badge renders for envelope with `degradeReason`, absent otherwise; `serializeEnvelopes(parseEnvelopes(x))` preserves the unknown key through edit+autosave. Satisfies CAFF-CS-01, CAFF-CS-02.
- [ ] 4.2 GREEN — modify `content-studio-panel.tsx`: add `degradeReason?: string | null` to `Envelope` interface; create `SlideDegradeBadge` presentational component (subtle chip, styled after `InferredFieldsDisclosure`); wire into carousel/story viewer corner and slide-strip dot.

## Rules Applied

- Every implementation task is preceded by a RED test task per Strict TDD (`dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj -c Release`; xUnit).
- WU-1a before WU-1b; WU-1a/1b before WU-4 and WU-3b; WU-2a before WU-2b; WU-3a before WU-3b; WU-0 and WU-2 independent of WU-1/WU-3.
- All estimates match proposal's Delivery table; no unit's task list exceeds its stated line budget based on file count and change surface reviewed.
