# Tasks: content-image-generation

**Branch:** `feat/content-format-taxonomy` (current working branch).
**TDD mode:** STRICT — `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj`. Every behavior task: RED → GREEN.

---

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1,600–2,200 |
| 400-line budget risk | **High** |
| Chained PRs recommended | **Yes** — new ports + gateway + decorator + OpenAI adapter + catalog step + processor wiring + retirement + tests all together far exceed 400 lines |
| Decision needed before apply | **Yes** — delivery_strategy is `ask-on-risk`; owner must confirm PR-slice boundary set before sdd-apply starts |
| Chain strategy | TBD with owner (`stacked-to-main` vs `feature-branch-chain`) |

### Suggested PR slice boundaries

| Slice | WUs | Goal | Can land independently? |
|-------|-----|------|------------------------|
| PR-A | WU-1, WU-2, WU-3 | Ports + records + profile field + prompt-writer — pure Application, no infra needed | Yes — builds and tests pass; no runtime path changes |
| PR-B | WU-4, WU-5, WU-6 | Creative-background service + image gateway + instrumentation decorator + DI | Depends on PR-A (interfaces must exist) |
| PR-C | WU-7, WU-8, WU-9, WU-10 | OpenAI adapter + catalog step + processor wiring + retirement + ADR + gates | Depends on PR-B |

> **Note:** The seam order within each PR slice follows the design's §13 ordering so each step compiles and tests pass incrementally.

---

## Suggested Work Units

| WU | Goal | Seam order | PR slice |
|----|------|-----------|----------|
| WU-1 | Port contracts: `IImageGenerationGateway`, `IImageModelClient`, `IImagePromptWriter`, `IClientAestheticImageBackgroundService` + request/result records | First — compiler gate before any impl | A |
| WU-2 | `ClientAestheticProfile.CreativeSlideIndices` additive field + default-resolution helper | After WU-1 | A |
| WU-3 | `IImagePromptWriter` implementation over the TEXT route | After WU-1, WU-2 | A |
| WU-4 | `ClientAestheticImageBackgroundService` (cap + parallel + reuse-by-prompt-hash + per-slide CSS fallback) | After WU-3 | B |
| WU-5 | `ImageGenerationGateway` + `ImageGenerationGatewayInstrumentationDecorator` + manual DI decoration | After WU-1 | B |
| WU-6 | `OpenAiImageModelClient` adapter (OpenAI Images API behind the port, bytes-only) | After WU-1 | B |
| WU-7 | `generate-image` / `ai-image` step in `WorkflowConfigurationCatalog` (code-only, MediaGeneration) + OpenAI MediaGeneration provider seeding | After WU-1 | C |
| WU-8 | `ContentGenerationJobProcessor` wiring (ForcedDefault overlay + parity test + resilience test) | After WU-4, WU-5, WU-7 | C |
| WU-9 | Retire `IImageGenerationClient` / `NullImageGenerationClient` + DI cleanup | After WU-8 | C |
| WU-10 | ADR + docs/openspec spec sync + `state.yaml` + run gates | Last | C |

---

## WU-1 — Port Contracts: Bytes-Only Image Ports + Request/Result Records + Contract Tests

*Refs: IMG-01, IMG-08; design §3.1, §6.*

This is the compiler gate. All downstream WUs depend on these interfaces existing and being clean of provider types.

### Phase 1.A — Port definitions + records (Application)

- [ ] 1.1 **[RED]** Add `IImageGenerationGatewayContractTests.cs` — use reflection to assert:
  - `IImageGenerationGateway.GenerateAsync(ImageGenerationRequest, CancellationToken)` returns `Task<Result<ImageGenerationResult>>`
  - No OpenAI, `System.Net.Http`, Supabase, or EF types appear in any parameter/return type
  - `ImageGenerationRequest`, `ImageGenerationResult`, `ImageInitReference`, `ImageUsage` are records containing only primitives, `byte[]`, and Application-layer types
  - `IImageModelClient` has the same `GenerateAsync` signature as `IImageGenerationGateway` (mirror contract)
  - Mirrors the `IVisualDesignerContractTests` pattern.
  (`Incoders.Hive.Tests/Application/IImageGenerationGatewayContractTests.cs`)

- [ ] 1.2 **[RED]** Add `IImagePromptWriterContractTests.cs` — reflect `IImagePromptWriter.WriteAsync(ImagePromptRequest, CancellationToken)` returns `Task<Result<string>>`; assert `ImagePromptRequest` carries only `Guid`, `int`, `string`, and `ClientAestheticProfile`; no provider types. (`Incoders.Hive.Tests/Application/IImagePromptWriterContractTests.cs`)

- [ ] 1.3 **[RED]** Add `IClientAestheticImageBackgroundServiceContractTests.cs` — reflect `BuildCreativeBackgroundsAsync(ClientAestheticImageRequest, CancellationToken)` returns `Task<IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>>`; assert `ClientAestheticImageRequest` contains only primitives, domain types, and `ClientAestheticProfile`; no provider types. (`Incoders.Hive.Tests/Application/IClientAestheticImageBackgroundServiceContractTests.cs`)

- [ ] 1.4 **[GREEN]** Create `IImageGenerationGateway.cs` in `Application/Abstractions/Ai/` — define:
  - `IImageGenerationGateway` with `GenerateAsync(ImageGenerationRequest, CancellationToken) → Task<Result<ImageGenerationResult>>`
  - `IImageModelClient` with identical signature
  - `ImageGenerationRequest` sealed record (fields per design §3.1: `OrganizationId`, `CapabilityKey`, `PromptVersionKey`, `Prompt`, `Width`, `Height`, `PreferredProviderKey`, `PreferredModel`, `ImageInitReference? Init = null`)
  - `ImageGenerationResult` sealed record (`byte[] Bytes`, `string Mime`, `string ProviderKey`, `string? Model`, `ImageUsage? Usage = null`)
  - `ImageInitReference` sealed record (OQ-7 reserved: `byte[]? Bytes`, `string? Url`, `string Mime`)
  - `ImageUsage` sealed record (`int ImageCount`, `long? OutputBytes = null`)
  (`Incoders.Hive.Application/Abstractions/Ai/IImageGenerationGateway.cs`)

- [ ] 1.5 **[GREEN]** Create `IImagePromptWriter.cs` in `Application/Abstractions/Social/ContentGeneration/` — define `IImagePromptWriter` + `ImagePromptRequest` sealed record (`Guid OrganizationId`, `Guid? ClientId`, `int SlideIndex`, `string SlideCopySummary`, `ClientAestheticProfile Aesthetic`). (`Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IImagePromptWriter.cs`)

- [ ] 1.6 **[GREEN]** Create `IClientAestheticImageBackgroundService.cs` in `Application/Abstractions/Social/ContentGeneration/` — define `IClientAestheticImageBackgroundService` + `ClientAestheticImageRequest` sealed record (`Guid OrganizationId`, `Guid? ClientId`, `Guid JobId`, `Guid FormatResultId`, `string FormatKey`, `int SlideCount`, `IReadOnlyList<int> CreativeSlideIndices`, `ClientAestheticProfile Aesthetic`, `IReadOnlyList<string> PerSlideCopySummaries`). (`Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IClientAestheticImageBackgroundService.cs`)

- [ ] 1.7 Run `dotnet test` — WU-1 contract tests pass; no regression.

---

## WU-2 — `ClientAestheticProfile.CreativeSlideIndices` + Default-Resolution Helper

*Refs: CIG-BSP-02, CIG-BSP-03; design §0 OQ-1, OQ-2, §7.*

### Phase 2.A — Profile field (additive, nullable)

- [ ] 2.1 **[RED]** Add `CreativeSlideIndicesTests.cs` — assert:
  - `ClientAestheticProfile` construction with `CreativeSlideIndices = null` compiles; existing construction sites unchanged (back-compat)
  - `with` expression changing only `CreativeSlideIndices` compiles
  - Field is `IReadOnlyList<int>?`
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/CreativeSlideIndicesTests.cs`)

- [ ] 2.2 **[GREEN]** Modify `ClientAestheticProfile.cs` — append `IReadOnlyList<int>? CreativeSlideIndices = null` as a trailing nullable parameter; no existing parameter order or defaults changed. (`Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ClientAestheticProfile.cs`)

### Phase 2.B — Default-resolution helper

- [ ] 2.3 **[RED]** Add `CreativeSlideIndexResolverTests.cs` — assert:
  - `Resolve(null, slideCount: 5)` → `[1, 5]` (first + last, 1-based) (OQ-1 default)
  - `Resolve(null, slideCount: 1)` → `[1]` (single-slide degenerate case)
  - `Resolve([0, 9], slideCount: 5)` → `[]` (all out-of-range, silently clamped/excluded; 1-based so 0 is out-of-range, 9 > 5) (CIG-BSP-02-c)
  - `Resolve([1, 3], slideCount: 5)` → `[1, 3]` (explicit set, in range)
  - `Resolve([1, 1, 3], slideCount: 5)` → `[1, 3]` (deduped)
  - `Resolve([1, 2, 3], slideCount: 5).Take(MaxCreativeSlidesPerPackage)` → `[1, 2]` (capped to 2, OQ-2)
  - `MaxCreativeSlidesPerPackage = 2` is a named constant accessible from the resolver
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/CreativeSlideIndexResolverTests.cs`)

- [ ] 2.4 **[GREEN]** Create `CreativeSlideIndexResolver.cs` — static class with `Resolve(IReadOnlyList<int>? indices, int slideCount)` that: applies 1-based range filter (1..slideCount), dedupes, takes first `MaxCreativeSlidesPerPackage`; defines `public const int MaxCreativeSlidesPerPackage = 2`. (`Incoders.Hive.Application/Features/Social/ContentGeneration/Background/CreativeSlideIndexResolver.cs`)

- [ ] 2.5 Run `dotnet test` — WU-2 tests pass; no regression.

---

## WU-3 — `IImagePromptWriter` over the TEXT Route

*Refs: IMG-03, CIG-CG-04; design §3.2, §0 OQ-6.*

### Phase 3.A — Prompt-writer implementation

- [ ] 3.1 **[RED]** Add `ImagePromptWriterTests.cs` — using a fake `IAiCompletionGateway`:
  - Given a rich `ClientAestheticProfile` (descriptors, motifs, palette), `WriteAsync` sends a structured JSON request via `IAiCompletionGateway` with `CapabilityKey = "ai-generation"` and `PromptVersionKey = "image-prompt/v1"`
  - Response `{ imagePrompt: "…" }` is parsed and the string returned as `Result.Success`
  - Empty `imagePrompt` in the response → `Result.Failure` (degrade trigger; IMG-03-b)
  - Gateway failure (returns `Result.Failure`) → `Result.Failure` (degrade trigger)
  - `SlideCopySummary` appears in the assembled prompt (on-topic prompting)
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/ImagePromptWriterTests.cs`)

- [ ] 3.2 **[GREEN]** Create `ImagePromptWriter.cs` — implements `IImagePromptWriter`; uses `IAiCompletionGateway.CompleteJsonAsync<T>` with capability `"ai-generation"` and prompt-version `"image-prompt/v1"`; builds the structured prompt from `ImagePromptRequest.Aesthetic` (descriptors, motifs, palette, voice, reference context) + `SlideCopySummary`; parses `{ imagePrompt }` response; returns `Failure` on empty/null or gateway error. (`Incoders.Hive.Application/Features/Social/ContentGeneration/Background/ImagePromptWriter.cs`)

- [ ] 3.3 Add `"image-prompt/v1"` constant to `ContentGenerationPromptVersions.cs` (additive, no bump to existing keys). (`Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ContentGenerationPromptVersions.cs`)

- [ ] 3.4 Run `dotnet test` — WU-3 tests pass; no regression.

---

## WU-4 — `ClientAestheticImageBackgroundService` (cap + parallel + reuse-by-hash + fallback)

*Refs: IMG-03, IMG-04, IMG-05, CIG-CG-03, CIG-CG-04; design §3.3, §4.1, §5, §0 OQ-3, OQ-4.*

### Phase 4.A — Service implementation (Application)

- [ ] 4.1 **[RED]** Add `ClientAestheticImageBackgroundServiceTests.cs`:

  **Happy path:**
  - Given `CreativeSlideIndices = [1, 4]`, a fake `IImagePromptWriter` returning prompts, a fake `IImageGenerationGateway` returning bytes, and a fake `IGeneratedAssetStorage` returning a durable URL → `BuildCreativeBackgroundsAsync` returns `{ 1: ForcedDefault(url1, "ai-generated-background"), 4: ForcedDefault(url4, "ai-generated-background") }`
  - Result map contains exactly the successful slides (both keys present)

  **Cap enforcement (OQ-2):**
  - Given `CreativeSlideIndices = [1, 2, 3]` (3 items, over cap=2) → only slides 1 and 2 are processed; slide 3 is not attempted

  **Bounded parallelism (OQ-4):**
  - `Task.WhenAll` resolves all ≤2 slides; both prompts are written before both images are rendered (verified via call order tracking in fake)

  **Per-slide fallback on prompt failure (IMG-03-b / CIG-CG-03):**
  - Prompt writer returns `Failure` for slide 1 → slide 1 is absent from the result map; slide 4 proceeds and succeeds

  **Per-slide fallback on image-gen failure (IMG-05-a / CIG-CG-03):**
  - Image gateway returns `Failure` for slide 4 → slide 4 absent; slide 1 succeeds and is present

  **Per-slide fallback on storage failure (IMG-04-b / CIG-CG-03):**
  - `IGeneratedAssetStorage.PutAssetAsync` throws for slide 1 → slide 1 absent; slide 4 not affected

  **Reuse on unchanged prompt hash (OQ-3):**
  - Given an asset already exists at `(org, client, formatResultId, "carousel/slide_1.png")` AND prompt hash unchanged → `PutAssetAsync` is NOT called again; the stored URL is reused

  **Regenerate on prompt hash change (OQ-3):**
  - Given stored asset exists BUT prompt hash differs → image gateway IS called; `PutAssetAsync` IS called with new bytes

  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/ClientAestheticImageBackgroundServiceTests.cs`)

- [ ] 4.2 **[GREEN]** Create `ClientAestheticImageBackgroundService.cs` — implements `IClientAestheticImageBackgroundService`; constructor takes `IImagePromptWriter`, `IImageGenerationGateway`, `IGeneratedAssetStorage`; applies `CreativeSlideIndexResolver.MaxCreativeSlidesPerPackage` cap; runs bounded `Task.WhenAll` for ≤2 slides; for each slide: computes prompt hash → checks stored asset (OQ-3 reuse) → calls prompt writer → calls image gateway → calls `PutAssetAsync` using key `(org, client, formatResultId, $"carousel/slide_{slideIndex}.png")`; on any step failure: catches, logs slide index + reason, omits that slide from the map; returns map of successes only. (`Incoders.Hive.Application/Features/Social/ContentGeneration/Background/ClientAestheticImageBackgroundService.cs`)

- [ ] 4.3 Run `dotnet test` — WU-4 tests pass; full suite green.

---

## WU-5 — `ImageGenerationGateway` + Instrumentation Decorator + Manual DI Decoration

*Refs: IMG-01, IMG-02, IMG-06; design §2.3, §2 (naming check), §0 OQ-5.*

### Phase 5.A — Gateway concrete (Infrastructure)

- [ ] 5.1 **[RED]** Add `ImageGenerationGatewayTests.cs` — mirror `AiCompletionGatewayTests` pattern:
  - Given a fake `IWorkflowConfigurationResolver` returning a route (provider + model) for `generate-image`, and a fake `IImageModelClient` factory → `GenerateAsync` creates the client and returns its result (IMG-01-b)
  - Given resolver returns null (no IMAGE route) → gateway returns `Result.Failure` without calling any client (IMG-06-a)
  - `OrganizationId` from the request is forwarded to the resolver
  (`Incoders.Hive.Tests/Infrastructure/Ai/ImageGenerationGatewayTests.cs`)

- [ ] 5.2 **[GREEN]** Create `ImageGenerationGateway.cs` — implements `IImageGenerationGateway`; resolves `generate-image` step via `IWorkflowConfigurationResolver`; creates `IImageModelClient` via `IExternalProviderClientFactory`; delegates `GenerateAsync`; mirrors `AiCompletionGateway.cs` flow. (`Incoders.Hive.Infrastructure/Ai/ImageGenerationGateway.cs`)

### Phase 5.B — Instrumentation decorator (Infrastructure)

- [ ] 5.3 **[RED]** Add `ImageGenerationGatewayInstrumentationDecoratorTests.cs` — mirror `AiDecoratorInstrumentationTests`:
  - Successful call → one `UsageEvent` persisted with `org`, provider key, model, success outcome (IMG-02-a)
  - Failed call → one `UsageEvent` with failure outcome; event is written before propagating the error (IMG-02-b)
  - `ProviderCallContext.Current == null` → throws (mirrors text decorator behavior)
  (`Incoders.Hive.Tests/Infrastructure/Observability/ImageGenerationGatewayInstrumentationDecoratorTests.cs`)

- [ ] 5.4 **[GREEN]** Create `ImageGenerationGatewayInstrumentationDecorator.cs` — wraps `IImageGenerationGateway`; requires active `ProviderCallContext.Current`; writes one `UsageEvent` per call (success or failure); mirrors `AiCompletionGatewayInstrumentationDecorator.cs` exactly. (`Incoders.Hive.Infrastructure/Observability/ImageGenerationGatewayInstrumentationDecorator.cs`)

### Phase 5.C — Manual DI decoration

- [ ] 5.5 **[RED]** Add/extend `ContentGenerationDiResolutionTests.cs` or equivalent — assert `IImageGenerationGateway` resolves to the decorated gateway (i.e., the instrumentation decorator wraps the concrete gateway); assert `OpenAiImageModelClient` is accessible via the provider client factory for key `"openai"`.

- [ ] 5.6 **[GREEN]** Modify `DependencyInjection.cs` — register `ImageGenerationGateway` and its decorator following the SAME manual-decoration pattern as `AiCompletionGateway` (lines ~220-227 of the existing DI file). Register `IClientAestheticImageBackgroundService → ClientAestheticImageBackgroundService` and `IImagePromptWriter → ImagePromptWriter`. (`Incoders.Hive.Infrastructure/DependencyInjection/DependencyInjection.cs`)

- [ ] 5.7 Run `dotnet test` — WU-5 tests pass; no regression.

---

## WU-6 — `OpenAiImageModelClient` Adapter (Bytes-Only, SDK Behind Port)

*Refs: IMG-01, IMG-08; design §2.3.*

### Phase 6.A — OpenAI adapter (Infrastructure)

- [ ] 6.1 **[RED]** Add `OpenAiImageModelClientTests.cs`:
  - Given a mocked OpenAI Images API response with image bytes → `GenerateAsync` returns `Result.Success(ImageGenerationResult { Bytes, Mime = "image/png", ProviderKey = "openai" })`
  - No OpenAI SDK type appears in the returned `ImageGenerationResult` (bytes-only contract; IMG-08)
  - Given a 4xx/5xx OpenAI error response → `GenerateAsync` returns `Result.Failure` (no unhandled exception)
  - Given `Init` is non-null → reserved slot accepted without error (OQ-7; not used in v1 but must not throw)
  (`Incoders.Hive.Tests/Infrastructure/Ai/OpenAiImageModelClientTests.cs`)

- [ ] 6.2 **[GREEN]** Create `OpenAiImageModelClient.cs` — implements `IImageModelClient`; uses OpenAI Images API (`dall-e-3` / `gpt-image-1` per the request's `PreferredModel`); posts with `Width` / `Height` (1024×1024 default per OQ-2); maps response bytes to `ImageGenerationResult`; catches provider errors → `Result.Failure`; ALL OpenAI SDK types confined to this file. (`Incoders.Hive.Infrastructure/Ai/OpenAiImageModelClient.cs`)

- [ ] 6.3 Run `dotnet test` — WU-6 tests pass; no regression.

---

## WU-7 — `WorkflowConfigurationCatalog` Step + OpenAI MediaGeneration Provider Seeding

*Refs: CIG-INT-01, CIG-INT-02, CIG-INT-03, CIG-INT-04; design §2.3.*

### Phase 7.A — Catalog entry (code-only)

- [ ] 7.1 **[RED]** Add `WorkflowConfigurationCatalogImageStepTests.cs` — assert:
  - `WorkflowConfigurationCatalog` contains a step with key `"generate-image"` (or the platform-canonical key `"ai-image"`)
  - That step's `AllowedProviderCategories` includes `MediaGeneration`
  - `SupportsModelSelection = true`
  - `FindStep("generate-image")` resolves it (CIG-INT-01-a)
  - The entry is absent in a pre-change snapshot of the catalog (CIG-INT-01-b — can be a comment; no DB migration present)
  (`Incoders.Hive.Tests/Application/Integrations/WorkflowConfigurationCatalogImageStepTests.cs`)

- [ ] 7.2 **[RED]** Add/extend `WorkflowConfigurationCatalogImageStepTests.cs` — assert:
  - The catalog includes an OpenAI provider entry for the `MediaGeneration` category (CIG-INT-02-a shape check)
  - `IWorkflowConfigurationResolver` with the org-A stub returns a route for `generate-image` with provider `"openai"` and model `"dall-e-3"` (CIG-INT-03-a)
  - `IWorkflowConfigurationResolver` with the org-B stub (no `generate-image` route) returns null for that step (CIG-INT-03-b)

- [ ] 7.3 **[GREEN]** Modify `WorkflowConfigurationCatalog.cs` — add `generate-image` (or `ai-image`) entry with `AllowedProviderCategories: [MediaGeneration]` and `SupportsModelSelection: true`; this is a single `WorkflowDefinition` record addition, no migration. (`Incoders.Hive.Application/Features/Integrations/WorkflowConfigurations/WorkflowConfigurationCatalog.cs`)

- [ ] 7.4 **[GREEN]** Seed OpenAI as a `MediaGeneration` provider option — add the capability mapping to the existing OpenAI `IntegrationProvider` registration (the provider already exists; this is a category/step mapping addition only). Verify `IWorkflowConfigurationResolver` resolves the step per the existing resolver seam without additional changes.

- [ ] 7.5 Run `dotnet test` — WU-7 tests pass; no regression.

---

## WU-8 — `ContentGenerationJobProcessor` Wiring (ForcedDefault Overlay + Parity + Resilience)

*Refs: CIG-CG-01, CIG-CG-02, CIG-CG-03, CIG-CG-04, IMG-05, IMG-06; design §4.1, §4.2, §5.*

### Phase 8.A — Parity test (existing CSS path must remain unchanged)

- [ ] 8.1 **[RED]** Add `ProcessorImageBackgroundParityTests.cs` — using the existing `ContentGenerationJobProcessorVisualTests` harness:
  - When `IClientAestheticImageBackgroundService` is NOT registered (null injectable) for a `ClientAesthetic` carousel → all slides take the CSS designer path; `BuildCreativeBackgroundsAsync` is never called; plan contains only `Creative`/`ClientAesthetic` selections (CIG-CG-01-b path; IMG-06-a)
  - When the client's `BackgroundSourcingPolicy` is NOT `ClientAesthetic` → image path is never entered; existing behavior unchanged (CIG-CG-01-c)
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Pipeline/ProcessorImageBackgroundParityTests.cs`)

### Phase 8.B — Resilience test (image failure → CSS fallback, job completes)

- [ ] 8.2 **[RED]** Add `ProcessorImageBackgroundResilienceTests.cs`:
  - Given a `ClientAesthetic` carousel with `CreativeSlideIndices = [1, 5]`, a registered `IClientAestheticImageBackgroundService`, and the service returning an empty map (all slides failed) → all slides use the CSS designer; job completes with success (CIG-CG-03-b; IMG-05-a)
  - Given the service returns a partial map (slide 1 succeeded, slide 5 absent) → slide 1 receives `ForcedDefault` via `ResolveEnforcedBackground`/`BuildTemplateBackedMarkup`; slide 5 takes CSS designer; job completes (CIG-CG-03-a)
  - Failure/exception thrown by the service itself (uncaught) → caught in the processor; all slides degrade to CSS; job completes; no unhandled exception propagates (IMG-05)
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Pipeline/ProcessorImageBackgroundResilienceTests.cs`)

### Phase 8.C — ForcedDefault overlay (production wiring)

- [ ] 8.3 **[GREEN]** Modify `ContentGenerationJobProcessor.cs` — add pre-loop creative-background build step:
  1. If `policy == ClientAesthetic` AND `imageBackgroundService is not null`:
     a. Compute `creativeIdx = CreativeSlideIndexResolver.Resolve(profile.CreativeSlideIndices, slideCount)`
     b. If `creativeIdx` is non-empty, call `await imageBackgroundService.BuildCreativeBackgroundsAsync(req)` wrapped in a top-level try/catch (exception → empty map; log warning)
     c. Apply `plan.WithForcedDefaults(map)` to overlay `ForcedDefault` onto successful creative slots
  2. In the existing per-slide loop, `plan.ForSlide(i)` naturally routes creative slots (with `ForcedDefault`) through the existing `ResolveEnforcedBackground` → `BuildTemplateBackedMarkup` path — no change to that loop
  3. Add `IClientAestheticImageBackgroundService?` as a nullable constructor dependency (nullable = CSS-path passthrough when absent)
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs`)

- [ ] 8.4 **[GREEN]** Add `CarouselBackgroundPlan.WithForcedDefaults(IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>)` extension/method — returns a new plan where each key in the map replaces the plan entry at that 1-based slide index; indices not in the map are unchanged. Add corresponding unit test in `CarouselBackgroundPlanTests.cs`. (`Incoders.Hive.Application/Features/Social/ContentGeneration/Background/` or on `CarouselBackgroundPlan.cs` in Domain if the design allows; per design §2.1 the plan is reused as-is for the mixed-selection case — add the helper wherever the type lives)

- [ ] 8.5 Run `dotnet test` — WU-8 parity + resilience + wiring tests pass; full suite green.

---

## WU-9 — Retire `IImageGenerationClient` / `NullImageGenerationClient` + DI Cleanup

*Refs: IMG-07; design §0 OQ-5, §2.2.*

- [ ] 9.1 **[RED]** Add `RetiredImageGenerationClientTests.cs` — using reflection or a compilation probe:
  - Assert no type in the `Incoders.Hive.Application` assembly named `IImageGenerationClient` exists (or: if it does exist, assert it is decorated with `[Obsolete]` and carries a reference to this change)
  - Assert no call site in the generation pipeline (by scanning for usages of the old interface GUID/name) remains active (IMG-07-a)
  - Alternatively: the test simply asserts the old type is gone and the build still compiles (the build will fail at this step if any remaining caller exists — that IS the RED step)
  (`Incoders.Hive.Tests/Application/RetiredImageGenerationClientTests.cs`)

- [ ] 9.2 **[GREEN]** Delete `IImageGenerationClient.cs` from `Application/Abstractions/Social/ContentGeneration/`. (`Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IImageGenerationClient.cs`)

- [ ] 9.3 **[GREEN]** Delete or locate and delete `NullImageGenerationClient.cs` (the null implementation). Verify by searching the codebase for `NullImageGenerationClient` — exactly one DI registration at `DependencyInjection.cs:~286` is the only remaining reference. Remove it.

- [ ] 9.4 **[GREEN]** Remove the `IImageGenerationClient` / `NullImageGenerationClient` DI registration from `DependencyInjection.cs`. (`Incoders.Hive.Infrastructure/DependencyInjection/DependencyInjection.cs`)

- [ ] 9.5 Run `dotnet test` — full suite green; no compile errors from retired types. Confirm the `IImageGenerationClient` retirement test passes.

---

## WU-10 — ADR + Docs/OpenSpec Spec Sync + `state.yaml` + Gates

*Refs: design §10, §12; repo `CLAUDE.md` docs-sync MUST; `ai-agent.gate.md` §6.*

- [ ] 10.1 **[GREEN]** Author ADR `docs/architecture/adr/0007-image-generation-gateway-port.md` — document:
  - New `IImageGenerationGateway` / `IImageModelClient` bytes-only port family (mirrors `IAiCompletionGateway`; rationale: separate byte channel)
  - Retirement of `IImageGenerationClient` / `NullImageGenerationClient` (dead code; lacked `OrganizationId`)
  - `ClientAestheticProfile.CreativeSlideIndices` additive nullable field (OQ-1 default: first+last; extractor-ready seam)
  - `MaxCreativeSlidesPerPackage = 2`, target 1024×1024 (OQ-2 DECISION-NEEDED flag for GA)
  - Reuse-by-prompt-hash vs regenerate (OQ-3)
  - Sync-parallel bounded `Task.WhenAll` (OQ-4)
  - TEXT route reuse for image-prompt write (OQ-6)
  - `ImageInitReference? Init` reserved slot (OQ-7)
  - Architecture gate compliance: no OpenAI types in Domain/Application
  (`docs/architecture/adr/0007-image-generation-gateway-port.md`)

- [ ] 10.2 **[GREEN]** Merge spec deltas into living specs:
  - Merge `openspec/changes/content-image-generation/specs/image-generation/spec.md` into `openspec/specs/image-generation/spec.md` (new capability, create file)
  - Merge `openspec/changes/content-image-generation/specs/content-generation/spec-delta.md` into `openspec/specs/content-generation/spec.md` (additive append)
  - Merge `openspec/changes/content-image-generation/specs/background-sourcing-policy/spec-delta.md` into `openspec/specs/background-sourcing-policy/spec.md` (additive append)
  - Merge `openspec/changes/content-image-generation/specs/integrations/spec-delta.md` into `openspec/specs/integrations/spec.md` (additive append)

- [ ] 10.3 **[GREEN]** Update `openspec/changes/content-image-generation/state.yaml` — set `status: apply-complete`; record `adr: docs/architecture/adr/0007-image-generation-gateway-port.md`; populate `merged_specs` list. (`openspec/changes/content-image-generation/state.yaml`)

- [ ] 10.4 **[deferred to human]** Run `pwsh -File scripts/gates/Run-Gates.ps1`; report the gate summary. Do NOT mark complete if any MUST gate fails.

---

## Parallelism Notes

| Parallelism | WUs | Constraint |
|-------------|-----|-----------|
| Sequential only | WU-1 → WU-2 → WU-3 → WU-4 | Each step uses the types defined in the prior one |
| WU-5 and WU-6 can start once WU-1 is done | WU-5, WU-6 | Only need `IImageModelClient` from WU-1 |
| WU-7 can start once WU-1 is done | WU-7 | Catalog step is independent of the service impl |
| WU-8 requires WU-4 + WU-5 + WU-7 complete | WU-8 | Needs the service, gateway, and catalog step wired |
| WU-9 requires WU-8 complete | WU-9 | Confirms no caller remains before retirement |
| WU-10 requires all prior WUs complete | WU-10 | Final ADR + gates |

---

## Definition of Done

- [ ] WU-1..WU-10 tasks completed and committed in seam order.
- [ ] `dotnet test` passes with 0 failures across all WUs.
- [ ] Port contract tests pass for `IImageGenerationGateway`, `IImageModelClient`, `IImagePromptWriter`, `IClientAestheticImageBackgroundService` — no provider types in any signature.
- [ ] Parity test confirms: no `IClientAestheticImageBackgroundService` registered → existing CSS path unchanged.
- [ ] Resilience test confirms: image-gen failure → CSS fallback, job completes, no unhandled exception.
- [ ] `IImageGenerationClient` and `NullImageGenerationClient` are deleted; no remaining call sites compile against them.
- [ ] `ContentGenerationPromptVersions` carries `"image-prompt/v1"` constant.
- [ ] No OpenAI SDK type appears in Domain or Application projects (architecture gate).
- [ ] `WorkflowConfigurationCatalog` contains `generate-image` (MediaGeneration); OpenAI seeded as first provider option.
- [ ] ADR `0007-image-generation-gateway-port.md` authored under `docs/architecture/adr/`.
- [ ] Spec deltas merged into `openspec/specs/` living files.
- [ ] `pwsh -File scripts/gates/Run-Gates.ps1` exits 0; no MUST gate failure.
