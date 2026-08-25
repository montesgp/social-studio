# Design — content-image-generation (Real AI Raster Backgrounds for ClientAesthetic Creative Slides)

Hexagonal design that gives `BackgroundSourcingPolicy.ClientAesthetic` carousels REAL generated raster
backgrounds on a configurable subset of "creative" slides while the rest keep the existing CSS designer.
A new provider-agnostic image gateway (bytes-only port + usage-ledger instrumentation mirroring the text
gateway) renders pixels via the org's workflow-configured IMAGE route; a two-step flow uses the org's TEXT
route to WRITE the per-slide prompt and the IMAGE route to RENDER it. Generated pixels are persisted to
Supabase Storage and injected into the EXISTING per-slide compositing as
`BackgroundSelection.ForcedDefault(url, "ai-generated-background")`, so editable text is rebuilt over the
raster with near-zero studio change. Every provider concern (OpenAI SDK) stays behind an Infrastructure
adapter; Domain and Application stay provider-free. Image generation is NEVER on the completion critical
path — any prompt-writer or image-gen failure logs and falls back to the CSS designer background.

This document fixes the open questions (OQ-1..OQ-7), the component map, the new flow (component + sequence),
the strict-hexagonal boundary mapping, data-model changes, migration verdict, a test-first ordering aligned
with Strict TDD, and risks. It does NOT enumerate task steps — that is the `sdd-tasks` phase.

## Status
Design — architecture decisions resolved. Implements the technical HOW for `proposal.md`. New Application
port family (`IImageGenerationGateway` + `IImageModelClient`), a new Infrastructure adapter (OpenAI), and an
additive `ClientAestheticProfile.CreativeSlideIndices` field → an ADR under `docs/architecture/adr/` is
authored at apply time (`require_adr_for_architecture_changes`). One DECISION-NEEDED remains (OQ-2 hard cap
default value) — a sane default is proposed below and is owner-tunable, so it does not block the slice.

## Scope reminder (Non-Goals are hard boundaries)
Ships: a workflow-configured `generate-image` step (MediaGeneration), the byte-returning image gateway/ports
+ usage ledger, the OpenAI v1 adapter, two-step prompt-then-render, durable Supabase storage of pixels,
per-slide creative-vs-flat routing for `ClientAesthetic` carousels, injection via `ForcedDefault`, and the
non-negotiable CSS-fallback resilience. Does NOT: auto-detect which slides are creative (extractor — v1 ships
a configurable set, data model forward-compatible), does NOT add a second image provider (seam supports any;
only OpenAI built), does NOT do image-to-image / reference-image conditioning (port reserves a nullable slot,
not used), and does NOT touch Story/FeedPost creative backgrounds (carousel `ClientAesthetic` only).

---

## 0. The decisions (read this first)

| # | Open question | Decision | One-line rationale |
|---|---------------|----------|--------------------|
| **OQ-1** | v1 creative-slide selection default | **`ClientAestheticProfile.CreativeSlideIndices: IReadOnlyList<int>?` (additive nullable, 1-based). When `null` → default = `{ first, last }`** (slide 1 and slide N); a single-slide carousel yields `{ 1 }`. The extractor later populates this field. | First+last is the most common real "bookend creative" feed pattern (cover + closer); the nullable field is the extractor-ready seam with a deterministic default. |
| **OQ-2** | Per-job image-gen cap / cost guardrail | **Hard cap `MaxCreativeSlidesPerPackage = 2` (constant, owner-tunable); target resolution 1024×1024** (square, matches carousel canvas family and OpenAI's cheapest tier). Selection is capped to the first N of `CreativeSlideIndices`. **DECISION-NEEDED:** confirm `2` and `1024²` with the owner before GA (proposed default ships). | Bounds latency/cost to a known maximum per package; never an open-ended fan-out. |
| **OQ-3** | Regenerate vs reuse on studio re-generation | **Reuse the stored background by default.** A creative slide's generated image is content-addressed by `(org, client, job/result, slideIndex, promptHash)`; on re-generation, if a stored asset exists for the same key AND the format's AI prompt inputs are unchanged, REUSE it. Regenerate only when the prompt hash changes or an explicit `forceRegenerate` flag is set. | Image gen is the most expensive step; re-running the studio for a copy edit must not re-bill pixels. Prompt-hash invalidation keeps it correct. |
| **OQ-4** | Sync-parallel inline vs deferred backfill | **Sync-parallel inline, bounded.** Generate creative-slide images inside the format loop via `Task.WhenAll` across the (≤2) creative slides BEFORE the per-slide render loop, bounded by the OQ-2 cap. No new worker/job state. | v1 has ≤2 creative slides per package; bounded parallelism keeps latency ≈ one image call while preserving the existing single-pass completion semantics. Deferred backfill would add a job-state machine for no v1 payoff. |
| **OQ-5** | Port consolidation | **Introduce `IImageGenerationGateway` + `IImageModelClient` (new, bytes-only, org-routed). RETIRE `IImageGenerationClient` + `NullImageGenerationClient`** (never called; signature lacks `OrganizationId`). | A clean org-routed, ledger-instrumented gateway mirrors the proven text-AI shape; evolving the old port would carry its `OrganizationId`-less, `RenderedAsset`-typed baggage. The old port is dead code — removing it is low-blast (covered by tests). |
| **OQ-6** | Prompt-writer route reuse | **Reuse the org's existing TEXT route (`generate-content` / `ai-generation`) for the image-prompt write in v1.** A dedicated `image-prompt` sub-step is a deferred follow-up if independent versioning/observability is needed. | One fewer step to configure per org; the prompt-writer is a small structured-JSON call that the existing `IAiCompletionGateway` already serves. The prompt-version key namespaces it for observability without a new catalog step. |
| **OQ-7** | Image-to-image readiness | **Reserve a nullable `ReferenceImage` (init-image) field on the image request record; NOT used in v1** (always null). | The deferred image-conditioning follow-up plugs in without a contract change; a nullable trailing field keeps v1 construction sites unchanged. |

Each decision is expanded in [Decisions in depth](#9-decisions-in-depth).

---

## 1. Architecture approach

Dependency rule, unchanged: **Domain ← Application ← Infrastructure / Api**. No OpenAI/Supabase/`HttpClient`/
ASP.NET types in Domain or Application. The image gateway is the byte-returning sibling of the existing
`IAiCompletionGateway` (which returns JSON, not bytes — verified `IAiCompletionGateway.cs`), and it is
instrumented by a usage-ledger decorator that mirrors `AiCompletionGatewayInstrumentationDecorator`
(verified) so image calls land in the same usage ledger with a `ProviderCallContext` scope.

- **Domain** (`Domain/Social/ContentGeneration/`): unchanged. `BackgroundSelection.ForcedDefault(Url, Name)`
  ALREADY models "a specific background image is forced; the designer composes text ON it" (verified
  `BackgroundSelection.cs`) — exactly what a generated raster needs. NO new DU variant: a generated image is
  a `ForcedDefault`. `CarouselBackgroundPlan` is reused as-is. The creative-vs-flat decision lives on the
  PLAN (mixed per-slide selections), which the plan's `IsCoherent` already tolerates.
- **Application** gains the byte-returning port family and the two-step orchestration:
  - `IImageGenerationGateway` (port) + `IImageModelClient` (port) — bytes-only, org-routed.
  - `ImageGenerationRequest` / `ImageGenerationResult` records (prompt, size, nullable init-image; bytes + mime).
  - An `IImagePromptWriter` use case (reuses `IAiCompletionGateway` for the TEXT route — OQ-6) producing a
    per-slide image prompt from `ClientAestheticProfile`.
  - A creative-background producer (`ClientAestheticImageBackgroundService`) that, given the creative-slide
    set, writes prompts → renders pixels → persists to Supabase → returns `ForcedDefault` URLs, capped and
    parallel-bounded (OQ-2/OQ-4), with the resilience fallback to the CSS path.
  - `ClientAestheticProfile.CreativeSlideIndices` (additive nullable field — OQ-1).
  - The `ClientAestheticStrategy` and/or the processor's pre-loop chooses creative vs flat per slide.
- **Infrastructure** carries only wiring: `OpenAiImageModelClient : IImageModelClient` (OpenAI Images API
  behind the port; SDK/HTTP types never leak), the `ImageGenerationGateway` concrete + its instrumentation
  decorator, the `generate-image` route resolution via the existing `IWorkflowConfigurationResolver`, the
  `ExternalProviderClientFactory` registration for the image client, Supabase persistence via the EXISTING
  `IGeneratedAssetStorage` (verified — `PutAssetAsync(StoredAssetKey, StoredAssetContent)` already returns a
  durable `StoredAssetRef` with a public-resolvable storage path), and the `WorkflowConfigurationCatalog`
  code-only `generate-image` step + provider category seeding.
- **Api / Web**: none required. The Integrations web UI renders catalog steps from the API automatically
  (verified — the catalog is the single source; `MediaGeneration` already exists as a provider category).

Generation runs INSIDE the existing async content-generation pipeline (`ContentGenerationJobProcessor`,
verified). No new worker, no new job state. Completion NEVER awaits a successful image — failure degrades.

---

## 2. Component map — ports, adapters, layers

### 2.1 Domain (pure)
| Element | Path | Change |
|---------|------|--------|
| `BackgroundSelection.ForcedDefault` | `Domain/Social/ContentGeneration/BackgroundSelection.cs` | **Reused (no change)** — generated image = `ForcedDefault(url, "ai-generated-background")` |
| `CarouselBackgroundPlan` (mixed per-slide selections) | `Domain/Social/ContentGeneration/CarouselBackgroundPlan.cs` | **Reused (no change)** — plan carries `ForcedDefault` on creative slots, `Creative`/`ClientAesthetic` on flat slots |

### 2.2 Application (ports + use cases)
| Element | Kind | Path | Change |
|---------|------|------|--------|
| `IImageGenerationGateway` | Port | `Application/Abstractions/Ai/IImageGenerationGateway.cs` | **New** — `GenerateAsync(ImageGenerationRequest) → Result<ImageGenerationResult>`; bytes-only, org-routed |
| `IImageModelClient` | Port | `Application/Abstractions/Ai/IImageGenerationGateway.cs` (sibling) | **New** — provider client contract the factory creates (mirrors `IAiModelClient`) |
| `ImageGenerationRequest` | record | same file | **New** — `(Guid OrganizationId, string CapabilityKey, string PromptVersionKey, string Prompt, int Width, int Height, string? PreferredProviderKey, string? PreferredModel, ImageInitReference? Init = null)` |
| `ImageGenerationResult` | record | same file | **New** — `(byte[] Bytes, string Mime, string ProviderKey, string? Model, ImageUsage? Usage)` |
| `ImageInitReference` | record | same file | **New (reserved, OQ-7)** — `(byte[]? Bytes, string? Url, string Mime)`; null in v1 |
| `IImagePromptWriter` | Port (use case) | `Application/Abstractions/Social/ContentGeneration/IImagePromptWriter.cs` | **New** — `WriteAsync(ImagePromptRequest) → Result<string>`; reuses `IAiCompletionGateway` (OQ-6) |
| `ClientAestheticImageBackgroundService` | Use case | `…/ContentGeneration/Background/ClientAestheticImageBackgroundService.cs` | **New** — prompt → render → persist → `ForcedDefault`; cap+parallel+fallback |
| `IClientAestheticImageBackgroundService` | Port | `Application/Abstractions/Social/ContentGeneration/IClientAestheticImageBackgroundService.cs` | **New** — processor-facing seam (nullable DI: absent = CSS path) |
| `ClientAestheticProfile.CreativeSlideIndices` | value object field | `…/ContentGeneration/Shared/ClientAestheticProfile.cs` | **Modified** — append `IReadOnlyList<int>? CreativeSlideIndices = null` (OQ-1) |
| `ContentGenerationJobProcessor` | Use case | `…/Pipeline/ContentGenerationJobProcessor.cs` | **Modified** — pre-loop: build creative-slide background map for `ClientAesthetic` carousels; inject `ForcedDefault` into the plan per creative slot; flat slots unchanged |
| `IImageGenerationClient` | Port | `Application/Abstractions/Social/ContentGeneration/IImageGenerationClient.cs` | **Removed** — retired (OQ-5; never called) |

### 2.3 Infrastructure (adapters, wiring only)
| Element | Path | Change |
|---------|------|--------|
| `ImageGenerationGateway : IImageGenerationGateway` | `Infrastructure/Ai/ImageGenerationGateway.cs` | **New** — resolves `generate-image` route via `IWorkflowConfigurationResolver` + `IIntegrationResolver`; creates `IImageModelClient` via `IExternalProviderClientFactory`; mirrors `AiCompletionGateway` |
| `ImageGenerationGatewayInstrumentationDecorator` | `Infrastructure/Observability/ImageGenerationGatewayInstrumentationDecorator.cs` | **New** — writes one `UsageEvent` per image call from `ProviderCallContext.Current`; mirrors `AiCompletionGatewayInstrumentationDecorator` |
| `OpenAiImageModelClient : IImageModelClient` | `Infrastructure/Ai/OpenAiImageModelClient.cs` | **New** — OpenAI Images API (`dall-e-3` / `gpt-image-1`); SDK/HTTP behind the port; returns bytes |
| `WorkflowConfigurationCatalog` — `generate-image` step | `Application/.../WorkflowConfigurations/WorkflowConfigurationCatalog.cs` | **Modified (code-only)** — add `generate-image` step under `single-analyzer`/`content-analysis`, category `MediaGeneration`, `SupportsModelSelection: true` |
| `SupabaseStorageAdapter` (via `IGeneratedAssetStorage`) | existing | **Reused** — `PutAssetAsync` persists generated PNG; durable `StoredAssetRef.StoragePath` referenced by the envelope |
| OpenAI image client registration | `Infrastructure/DependencyInjection/DependencyInjection.cs` | **Modified** — register `IImageGenerationGateway` (decorated), `OpenAiImageModelClient` in `ExternalProviderClientFactory`; REMOVE the `IImageGenerationClient`/`NullImageGenerationClient` registration |

> **Naming check (verified):** the text gateway pair is `AiCompletionGateway` (concrete) wrapped by
> `AiCompletionGatewayInstrumentationDecorator` and resolved via a manual DI factory (Scrutor unavailable;
> `DependencyInjection.cs:220-227`). The image gateway MUST follow the same manual-decoration pattern.

---

## 3. The new types (contracts)

### 3.1 `IImageGenerationGateway` + `IImageModelClient` (Application, bytes-only)
```csharp
public interface IImageGenerationGateway
{
    Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request, CancellationToken ct = default);
}

// The provider client the factory creates (mirror of IAiModelClient).
public interface IImageModelClient
{
    Task<Result<ImageGenerationResult>> GenerateAsync(
        ImageGenerationRequest request, CancellationToken ct = default);
}

public sealed record ImageGenerationRequest(
    Guid OrganizationId,
    string CapabilityKey,        // "ai-image"
    string PromptVersionKey,     // "image-gen/v1"
    string Prompt,
    int Width,
    int Height,
    string? PreferredProviderKey,
    string? PreferredModel,
    ImageInitReference? Init = null);   // RESERVED (OQ-7) — null in v1

public sealed record ImageGenerationResult(
    byte[] Bytes,
    string Mime,                 // "image/png"
    string ProviderKey,
    string? Model,
    ImageUsage? Usage = null);   // images count / cost basis for the ledger

public sealed record ImageInitReference(byte[]? Bytes, string? Url, string Mime); // OQ-7
public sealed record ImageUsage(int ImageCount, long? OutputBytes = null);
```
- **Bytes-only** is the whole point: `IAiCompletionGateway.CompleteJsonAsync<T>` returns JSON (verified), so
  image generation needs its own byte channel. No provider type appears in any of these records.
- `CapabilityKey`/`PromptVersionKey`/`PreferredProviderKey`/`PreferredModel` mirror `AiJsonCompletionRequest`
  so the instrumentation decorator can populate the usage event identically.

### 3.2 `IImagePromptWriter` (Application use case, reuses TEXT route — OQ-6)
```csharp
public interface IImagePromptWriter
{
    Task<Result<string>> WriteAsync(ImagePromptRequest request, CancellationToken ct = default);
}

public sealed record ImagePromptRequest(
    Guid OrganizationId,
    Guid? ClientId,
    int SlideIndex,
    string SlideCopySummary,                       // the slide's generated copy, for on-topic prompting
    ClientAestheticProfile Aesthetic);             // descriptors, motifs, palette, voice, reference context
```
- Implemented over `IAiCompletionGateway` with a structured JSON response (`{ imagePrompt: string }`) using
  capability `ai-generation`, prompt-version `image-prompt/v1`. The org's TEXT route writes the prompt; the
  IMAGE route renders it. On weak/empty aesthetic signal OR a failed/empty write → return failure → caller
  degrades that slide to the CSS path (resilience).

### 3.3 `IClientAestheticImageBackgroundService` (the processor seam)
```csharp
public interface IClientAestheticImageBackgroundService
{
    // Returns a map: 1-based slideIndex → ForcedDefault for the creative slides that succeeded.
    // Slides absent from the map keep their flat (Creative/ClientAesthetic) selection.
    Task<IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>> BuildCreativeBackgroundsAsync(
        ClientAestheticImageRequest request, CancellationToken ct = default);
}

public sealed record ClientAestheticImageRequest(
    Guid OrganizationId,
    Guid? ClientId,
    Guid JobId,
    Guid FormatResultId,
    string FormatKey,
    int SlideCount,
    IReadOnlyList<int> CreativeSlideIndices,       // already capped to MaxCreativeSlidesPerPackage
    ClientAestheticProfile Aesthetic,
    IReadOnlyList<string> PerSlideCopySummaries);  // for on-topic prompts
```
- This service is the ONLY place that orchestrates prompt-write → render → persist → `ForcedDefault`, runs
  `Task.WhenAll` bounded by the cap (OQ-4), and applies the reuse-or-regenerate rule (OQ-3). It returns ONLY
  successes; the processor merges them into the plan and leaves flat slides untouched. Registered as a
  NULLABLE dependency on the processor — when absent (no image route, policy ≠ ClientAesthetic), the
  existing CSS path runs unchanged (rollback-safe).

---

## 4. Flow — component + sequence

### 4.1 Component view (pre-loop creative-background build, then the existing render loop)
```
ContentGenerationJobProcessor.RunAsync  (ClientAesthetic carousel)
  policy == ClientAesthetic, aestheticProfile loaded (existing WU-2 path)
  │
  ├─ strategy.Plan(...) → CarouselBackgroundPlan  (flat slides: Creative/ClientAesthetic)   [existing]
  │
  ├─ creativeIdx = (profile.CreativeSlideIndices ?? { 1, slideCount })          [OQ-1]
  │                 .Where(in 1..slideCount).Distinct().Take(MaxCreativeSlides)  [OQ-2 cap]
  │
  ├─ IF imageBackgroundService is not null AND creativeIdx not empty:
  │     map = await imageBackgroundService.BuildCreativeBackgroundsAsync(req)    [OQ-3/OQ-4]
  │            └─ per creative slide (Task.WhenAll, bounded):
  │                 prompt  = IImagePromptWriter.WriteAsync(...)      [org TEXT route, ProviderCallContext]
  │                 pixels  = IImageGenerationGateway.GenerateAsync(...) [org IMAGE route, ProviderCallContext + ledger]
  │                 stored  = IGeneratedAssetStorage.PutAssetAsync(...)  [Supabase, durable URL]
  │                 → ForcedDefault(stored.PublicUrl, "ai-generated-background")
  │            (any step fails for a slide → omit it from the map → that slide degrades to CSS)
  │
  ├─ plan = plan.WithForcedDefaults(map)   // overlay ForcedDefault onto creative slots; flat slots intact
  │
  └─ for specIndex in 0..specs.Count-1:                                          [existing loop, unchanged]
        selection = plan.ForSlide(specIndex + 1)
        designed  = designer.ComposeAsync(..., selection)                        [existing]
        enforced  = ResolveEnforcedBackground(selection)   // ForcedDefault → enforced template URL  [existing]
        markup    = enforced is not null ? BuildTemplateBackedMarkup(spec, enforced, aesthetic) : aiMarkup ?? CssFallback
        render + persist envelope (composition_spec_json: markup + geometry + background URL ref)  [existing]
```
- **The injection point is verified.** In `ContentGenerationJobProcessor.cs` (~449-460, 524-537), the
  per-slide `selection` drives `ResolveEnforcedBackground` (~1133): a `ForcedDefault` returns the enforced
  template URL → `BuildTemplateBackedMarkup` composites the `<img>` background + `data-slot` editable text →
  per-slide `CompositionSpecEnvelope` (markup + geometry + background ref). The studio rebuilds editable
  text over the rasterized background. By making the creative slot's selection a `ForcedDefault(generatedUrl)`
  we ride the EXACT existing mechanism — near-zero studio change. Flat slots keep `Creative`/`ClientAesthetic`.

### 4.2 Sequence — one creative slide (slide 1) of a ClientAesthetic carousel
```
Processor (pre-loop)
  → creativeIdx = { 1, 4 } for a 4-slide carousel (default first+last)         [OQ-1]
  → imageBackgroundService.BuildCreativeBackgroundsAsync(req)
      → slide 1:
          ProviderCallContext.Begin(org, client, "content-generation", "image-prompt:carousel", "image-prompt/v1", corr)
          promptWriter.WriteAsync(...) → "warm hand-drawn botanical scene, …"   [org TEXT route]
          ProviderCallContext.Begin(org, client, "content-generation", "generate-image:carousel", "image-gen/v1", corr)
          gateway.GenerateAsync(prompt, 1024, 1024) → bytes (PNG)               [org IMAGE route → ledger event]
          storage.PutAssetAsync(key{slide_1.png}, png) → StoredAssetRef(path)   [Supabase, durable]
          → map[1] = ForcedDefault(publicUrl, "ai-generated-background")
      → slide 4: same, in parallel (Task.WhenAll, cap 2)
  → plan.WithForcedDefaults(map) → PerSlide = [ForcedDefault(s1), Creative, Creative, ForcedDefault(s4)]
Processor (loop)
  → slide 1: selection = ForcedDefault → ResolveEnforcedBackground → BuildTemplateBackedMarkup(img bg + editable text)
  → slide 2/3: selection = Creative → AI/CSS designer (flat)                    [existing]
  → 4 slides rendered; slides 1 & 4 are REAL generated rasters with editable copy on top
```

### 4.3 Two-step routing (no provider hardcoding)
```
TEXT route  : IWorkflowConfigurationResolver.ResolveStep("single-analyzer","content-analysis","generate-content")  → org's AiModel
IMAGE route : IWorkflowConfigurationResolver.ResolveStep("single-analyzer","content-analysis","generate-image")    → org's MediaGeneration (OpenAI v1)
```
Both resolve per ORGANIZATION via the existing resolver (verified seam). Absent an IMAGE route → the image
gateway returns a failure → every creative slide degrades to CSS (the package still completes).

---

## 5. Degrade matrix (resilience — non-negotiable)

| Condition | Behavior |
|-----------|----------|
| No org IMAGE route configured | `imageBackgroundService` returns empty map (or is skipped); ALL creative slides keep their flat selection; package completes on CSS |
| Policy ≠ `ClientAesthetic` | Image path skipped entirely; existing behavior unchanged |
| `CreativeSlideIndices` null | Default to `{ first, last }` (OQ-1); proceed |
| Prompt-writer fails / empty prompt | That slide omitted from the map → CSS fallback for that slide; others proceed |
| Image-gen fails / times out | That slide omitted from the map → CSS fallback; logged; never throws into the loop |
| Supabase PUT fails | That slide omitted from the map → CSS fallback; logged (mirrors existing storage-failure handling) |
| Stored asset already exists, prompt unchanged (OQ-3) | REUSE the stored URL; no new prompt/image call (cost saved) |
| Cap exceeded (more than `MaxCreativeSlides` indices) | Take the first N; the rest stay flat |
| Image generation slow | Bounded `Task.WhenAll` (≤2); completion still single-pass; no new job state |

Image generation is NEVER on the completion critical path — every failure is a per-slide degrade to the
existing CSS designer background, exactly as the proposal's success criteria require.

---

## 6. Strict-hexagonal boundary mapping (which type lives where)

| Concern | Layer | Type(s) |
|---------|-------|---------|
| Background selection / plan | **Domain** | `BackgroundSelection.ForcedDefault`, `CarouselBackgroundPlan` (reused) |
| Aesthetic profile + creative-slide set | **Application** | `ClientAestheticProfile.CreativeSlideIndices` (additive) |
| Image gateway/port + request/result records | **Application** | `IImageGenerationGateway`, `IImageModelClient`, `ImageGenerationRequest/Result`, `ImageInitReference`, `ImageUsage` (bytes/`Result` only) |
| Prompt-writer + creative-bg orchestration | **Application** | `IImagePromptWriter`, `IClientAestheticImageBackgroundService` + impl |
| Provider SDK (OpenAI) | **Infrastructure** | `OpenAiImageModelClient` ONLY |
| Route resolution + ledger instrumentation | **Infrastructure** | `ImageGenerationGateway`, `ImageGenerationGatewayInstrumentationDecorator` |
| Durable storage | **Infrastructure** | `SupabaseStorageAdapter` via `IGeneratedAssetStorage` (reused) |
| Catalog step (code-only) | **Application** | `WorkflowConfigurationCatalog` (`generate-image`) |
| Integrations UI | **Api/Web** | none — auto-rendered from the catalog |

Architecture-gate contract: no OpenAI/HTTP/Supabase type ever appears in a Domain/Application signature; the
bytes flow as `byte[]`, the result as `Result<T>`. A port contract test (`Port_HasNoInfrastructureTypes`,
following the existing `IVisualDesignerContractTests` pattern) pins this for `IImageGenerationGateway`,
`IImageModelClient`, and `IImagePromptWriter`.

---

## 7. Data model + migration verdict

- **`ClientAestheticProfile.CreativeSlideIndices`** — additive nullable Application VO field. The profile is
  derived from the latest completed `ProfileAiAnalysis` at read time (verified — `ClientAestheticProfile` is
  a read-side projection, not a persisted aggregate). v1 sets it from a configurable source; the extractor
  later fills it. **NO EF migration is required for v1** if the value defaults via the reader (the field can
  remain `null` and fall back to first+last). If the owner wants a per-client persisted override before the
  extractor lands, that is an additive nullable column follow-up — flagged but NOT required for this slice.
- **`WorkflowConfigurationCatalog`** — the `generate-image` step is a **code-only** static list entry
  (verified — `WorkflowConfigurationCatalog.cs` is a static `IReadOnlyList<WorkflowDefinition>`). **NO
  migration.** Org provider/model selection persists through the existing integration-config tables, which
  already support `MediaGeneration` routes.
- **Generated images** — persisted via the existing `IGeneratedAssetStorage` / Supabase Storage bucket
  (verified — `StoredAssetKey { org, client, analysis/result, relativePath }`). For generation we key by
  `(org, client, formatResultId, "carousel/slide_{i}.png")`. **NO migration** — reuses the generated-asset
  pattern. The envelope stores the storage path/URL, NOT a data-URI (keeps `composition_spec_json` lean).

**Migration verdict: none required for the v1 slice.** Everything is code-only (catalog), additive-nullable
(profile field), or reuses existing durable storage.

---

## 8. Test-first ordering (Strict TDD — `dotnet test`)

1. **Port contracts** — `IImageGenerationGateway`, `IImageModelClient`, `IImagePromptWriter`,
   `IClientAestheticImageBackgroundService`: `Port_HasNoInfrastructureTypes` (no OpenAI/HTTP/Supabase types
   in any signature); request/result records are bytes/`Result` only. (RED→GREEN.)
2. **`ClientAestheticProfile.CreativeSlideIndices`** — additive nullable; existing construction sites + `with`
   expressions compile unchanged; default-resolution helper returns `{ first, last }` when null, clamps to
   range, dedupes, caps to `MaxCreativeSlides`. (OQ-1/OQ-2.)
3. **`IImagePromptWriter`** — writes a prompt from the aesthetic profile via a fake `IAiCompletionGateway`;
   weak/empty signal or gateway failure → failure result (degrade trigger). (OQ-6.)
4. **`ClientAestheticImageBackgroundService`** — prompt→render→persist→`ForcedDefault` happy path with fakes;
   per-slide failure (prompt/image/storage) omits that slide from the map (degrade); cap truncation; bounded
   parallelism; REUSE on unchanged prompt hash vs REGENERATE on change/force (OQ-3). The map contains ONLY
   successes.
5. **`ImageGenerationGateway` (Infrastructure)** — resolves the `generate-image` route via a fake resolver;
   creates the client via the factory; returns bytes; failure when no route. Mirror `AiCompletionGatewayTests`.
6. **`ImageGenerationGatewayInstrumentationDecorator`** — one `UsageEvent` per call; throws when
   `ProviderCallContext.Current` is null (mirror `AiDecoratorInstrumentationTests`); failure path records a
   `failed` event with the error code.
7. **`OpenAiImageModelClient`** — maps the OpenAI Images response to `ImageGenerationResult` bytes; no SDK
   type escapes the adapter; error mapping to `Result.Failure`.
8. **`WorkflowConfigurationCatalog`** — the `generate-image` step exists under `content-analysis`,
   `AllowedProviderCategories: [MediaGeneration]`, `SupportsModelSelection: true`; `FindStep` resolves it.
9. **`ContentGenerationJobProcessor` wiring** — for a `ClientAesthetic` carousel with an image route, the
   creative slots become `ForcedDefault` and route through `ResolveEnforcedBackground`/`BuildTemplateBackedMarkup`
   (editable text over raster); flat slots unchanged; NO image service registered → existing CSS path
   (parity test); image failure → CSS fallback (resilience test). Reuse the existing
   `ContentGenerationJobProcessorVisualTests` harness.
10. **Retirement** — remove `IImageGenerationClient`/`NullImageGenerationClient`; assert no remaining
    references compile (OQ-5); update DI.
11. **DI resolution** — `ContentGenerationDiResolutionTests`-style: `IImageGenerationGateway` resolves
    decorated; `OpenAiImageModelClient` resolvable via the factory.
12. **Gates + ADR + docs/spec sync** — `pwsh -File scripts/gates/Run-Gates.ps1`.

---

## 9. Decisions in depth

### OQ-1 — Creative-slide set = `CreativeSlideIndices?`, default first+last
**Decision.** Add `IReadOnlyList<int>? CreativeSlideIndices` (1-based) to `ClientAestheticProfile`. When null,
default to `{ first, last }` (slide 1 and slide N; a 1-slide carousel → `{ 1 }`). The future extractor
populates this field from the client's real feed pattern.
**Rationale.** Real "bookend creative" feeds put the strongest brand imagery on the cover and closer; first+last
is the highest-signal default with the lowest cost. The nullable field is the forward-compatible seam the
proposal demands without committing to the extractor now.
**Rejected.** "First 2" (misses the closer, a common creative slot). "N-from-AverageSlidesPerCarousel"
(over-engineered for v1; couples selection to a derived stat before any extractor evidence). A hardcoded
non-configurable set (no extractor seam).

### OQ-2 — Hard cap + target resolution (DECISION-NEEDED on exact values)
**Decision.** `MaxCreativeSlidesPerPackage = 2`; target 1024×1024 PNG. Selection is capped to the first N of
`CreativeSlideIndices`.
**Rationale.** Two creative slides per package bounds the worst-case to two image calls — predictable latency
and cost; 1024² is OpenAI's cheap square tier and matches the carousel canvas family. **DECISION-NEEDED:** the
owner should confirm `2` and `1024²` (and any per-org budget guard) before GA. The proposed defaults ship and
are owner-tunable constants, so this does not block the slice.
**Rejected.** Unbounded fan-out (open-ended cost). Per-job dollar budget enforcement in v1 (the usage ledger
already records cost; a hard budget gate is a follow-up once real spend data exists).

### OQ-3 — Reuse stored background by default; regenerate on prompt-hash change or force
**Decision.** Content-address each creative background by `(org, client, formatResultId, slideIndex, promptHash)`.
On re-generation, REUSE the stored asset if it exists and the prompt inputs are unchanged; regenerate only when
the prompt hash differs or an explicit `forceRegenerate` flag is set.
**Rationale.** Image generation is the most expensive step; a studio copy edit (or a benign re-run) must not
re-bill pixels. Prompt-hash keying keeps reuse CORRECT — a real aesthetic/copy change invalidates the cache and
triggers a fresh render.
**Rejected.** Always regenerate (cost + nondeterminism on every studio save). Always reuse (stale image after a
real prompt change). Time-based expiry (arbitrary; prompt-hash is the precise invalidation signal).

### OQ-4 — Sync-parallel inline, bounded
**Decision.** Generate creative-slide images inline, before the per-slide render loop, via `Task.WhenAll`
across the (≤2) creative slides. No deferred worker, no new job state.
**Rationale.** With the OQ-2 cap at 2, bounded parallelism makes total image latency ≈ one call while keeping
the EXISTING single-pass completion semantics (the processor already runs async in a background worker). A
deferred/backfill design would introduce a "partially generated" job state and a re-entrancy machine for zero
v1 payoff. The resilience contract (per-slide degrade) is simpler inline.
**Rejected.** Deferred async backfill (new job state, re-entrancy, completion-semantics complexity). Fully
sequential (needless latency for independent slides).

### OQ-5 — Introduce the new gateway; retire the old port
**Decision.** Add `IImageGenerationGateway` + `IImageModelClient` (bytes-only, org-routed, ledger-instrumented);
DELETE `IImageGenerationClient` + `NullImageGenerationClient`.
**Rationale.** The new pair mirrors the proven `IAiCompletionGateway`/`IAiModelClient` shape (org routing,
factory creation, decorator instrumentation) — verified patterns. The old `IImageGenerationClient` is never
called, lacks `OrganizationId` (cannot route per org), and returns `RenderedAsset` (wrong shape for a bytes
gateway). Evolving it would carry dead baggage; removing it is low-blast (a single `NullImageGenerationClient`
DI registration at `DependencyInjection.cs:286`).
**Rejected.** Evolve `IImageGenerationClient` by adding `OrganizationId` (keeps the `RenderedAsset` baggage and
the non-instrumented shape). Keep both (two image ports, one dead — confusion + drift).

### OQ-6 — Reuse the TEXT route for the prompt write in v1
**Decision.** The image-prompt write reuses the org's existing `generate-content`/`ai-generation` TEXT route via
`IAiCompletionGateway`, namespaced by prompt-version `image-prompt/v1`. No dedicated catalog sub-step in v1.
**Rationale.** The prompt-writer is a small structured-JSON call the existing text gateway already serves; one
fewer step to configure per org. The prompt-version key gives observability (a distinct ledger
`PipelineStepKey`) without a new catalog entry. A dedicated `image-prompt` step is an additive follow-up if
independent provider/model selection for prompt-writing is ever needed.
**Rejected.** A dedicated `image-prompt` catalog step now (extra org configuration burden for no v1 benefit;
the text route already produces good prompts).

### OQ-7 — Reserve a nullable init-image slot, unused in v1
**Decision.** Add `ImageInitReference? Init = null` to `ImageGenerationRequest` (and a `ReferenceImages`-derived
mapping is left to the follow-up). Always null in v1.
**Rationale.** The deferred image-to-image / reference-conditioning follow-up (passing the client's real
creatives as init images — the profile already carries `ReferenceImages`) plugs in WITHOUT a contract change.
A nullable trailing field keeps every v1 construction site unchanged and the port stable.
**Rejected.** No reserved slot (a later contract change ripples through the gateway, adapter, and every call
site). Build image-to-image now (explicit non-goal; OpenAI init-image semantics differ per model and add cost
the slice does not need).

---

## 10. Architecture-gate compliance
- **No provider types in Domain/Application.** `IImageGenerationGateway`, `IImageModelClient`, the request/
  result/usage records, `IImagePromptWriter`, `IClientAestheticImageBackgroundService`, and
  `ClientAestheticProfile.CreativeSlideIndices` are pure (`byte[]`/`Result`/Domain types only). The OpenAI SDK
  lives ONLY in `OpenAiImageModelClient` (Infrastructure). Verified against `architecture-boundaries.rules.md`
  and the `ai-agent.gate.md` MUST list.
- **No hardcoded provider/model.** Both the TEXT and IMAGE routes resolve per org via
  `IWorkflowConfigurationResolver` (verified seam) — OpenAI is seeded as the first `MediaGeneration` option,
  not wired as a hard dependency.
- **Usage ledger parity.** The image gateway is wrapped by an instrumentation decorator that requires an active
  `ProviderCallContext` (mirrors the text decorator, verified) so image spend lands in the same ledger.
- **Docs-sync (MUST):** `openspec/specs/*` updated for the new `image-generation` capability and the additive
  `content-generation` / `background-sourcing-policy` / `integrations` deltas in the spec phase.
- **ADR (MUST):** new Application port family + retirement of `IImageGenerationClient` + the additive profile
  field → an ADR under `docs/architecture/adr/` authored at apply time.
- Run `pwsh -File scripts/gates/Run-Gates.ps1` before completion; do not mark complete on any MUST failure.

## 11. Risks and mitigations (design-level)

| Risk | Severity | Mitigation |
|------|----------|------------|
| Image-gen latency/cost on the job path | **High** | Cap = 2 creative slides (OQ-2); bounded `Task.WhenAll` (OQ-4); reuse on unchanged prompt (OQ-3); never blocks completion (per-slide CSS degrade). |
| OpenAI SDK type leaks into Application | **Medium** | Bytes-only ports; SDK only in `OpenAiImageModelClient`; `Port_HasNoInfrastructureTypes` contract tests; architecture gate. |
| Prompt-writer produces off-style/empty prompt | **Medium** | Derive from `ClientAestheticProfile`; empty/weak write → per-slide CSS fallback (resilience). |
| Reuse-vs-regenerate staleness | **Medium** | Prompt-hash content-addressing (OQ-3): a real prompt/aesthetic change invalidates the cache; benign re-runs reuse. |
| `composition_spec_json` bloat | **Low** | Store pixels in Supabase; envelope references the storage URL, never a data-URI (verified `IGeneratedAssetStorage`). |
| Retiring `IImageGenerationClient` breaks a caller | **Low** | Verified never called; single DI registration to remove; retirement test asserts no references. |
| No image route configured for an org | **Low** | Gateway returns failure → all creative slides degrade to CSS; package completes; success criteria met. |

## 12. Spec impact noticed (for `sdd-spec` / verify)
- **image-generation (new capability):** `generate-image` catalog step (MediaGeneration); `IImageGenerationGateway`
  + `IImageModelClient` bytes-only ports + usage-ledger decorator; two-step prompt-then-render; OpenAI v1
  adapter; durable Supabase persistence; reuse-vs-regenerate (OQ-3); cap + resolution (OQ-2); reserved
  init-image slot (OQ-7); the resilience/fallback contract.
- **content-generation (modified):** `ClientAesthetic` creative slides composited from a generated raster via
  `BackgroundSelection.ForcedDefault` (editable text on top); per-slide creative-vs-flat routing driven by
  `CreativeSlideIndices` (default first+last); CSS-designer fallback on any image failure.
- **background-sourcing-policy (modified):** the `ClientAesthetic` strategy emits a generated-image background
  (`ForcedDefault`) for creative slides and the CSS-descriptor background for flat slides.
- **integrations (modified):** new `generate-image` step under `MediaGeneration`; OpenAI seeded as the first
  provider option; UI auto-renders from the catalog.

## 13. Next step
`sdd-tasks` — slice these components into work units in seam order: (1) bytes-only image ports + request/result
records + contract tests → (2) `ClientAestheticProfile.CreativeSlideIndices` + default-resolution helper →
(3) `IImagePromptWriter` over the TEXT route → (4) `ClientAestheticImageBackgroundService` (cap + parallel +
reuse + fallback) → (5) `ImageGenerationGateway` + instrumentation decorator + DI → (6) `OpenAiImageModelClient`
adapter → (7) `generate-image` catalog step → (8) processor wiring (`ForcedDefault` overlay + parity/resilience
tests) → (9) retire `IImageGenerationClient`/`NullImageGenerationClient` → (10) ADR + docs/spec sync + gates.
Tasks owns the checklist; this design owns the HOW.
</content>
</invoke>
