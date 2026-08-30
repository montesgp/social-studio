# ADR 0007 - Image Generation Gateway Port

## Status
Accepted

## Context

The `ClientAesthetic` background sourcing policy (ADR 0005, §4) and the ensure-fresh profile
reader (ADR 0006, §2) established a richer aesthetic profile inside the content generation
pipeline. However, the pipeline still generated only CSS-driven backgrounds for all carousel
slides. The only image-generation hook was a placeholder port — `IImageGenerationClient` —
that shipped as a null adapter with no operational path to real image generation.

Four gaps blocked production-grade image backgrounds:

1. **No operational image-gen port.** `IImageGenerationClient` was typed to an internal
   `ImageGenerationRequest` (bytes-only). It had no `OrganizationId`, no model-selection
   surface, and no durable-store contract, making it unsuitable as the backbone of a real
   provider adapter or a testable pipeline stage.

2. **No creative-slide targeting.** There was no mechanism to select which carousel slides
   should receive AI-generated raster backgrounds versus the CSS designer. Without a resolved
   index list, every slide would need either full image generation (expensive) or none.

3. **No durability or prompt reuse.** A sidecar hash scheme (prompt hash → stored image URL)
   was absent. Without it, repeated job runs would regenerate identical images, wasting API
   quota and producing inconsistent packages.

4. **No rollback-safe integration point.** Attaching image generation directly inside the
   per-slide loop would tie job success to provider availability — violating the requirement
   that CSS backgrounds must remain reachable even when the image-gen provider is down or
   unconfigured.

## Decision

### 1. Port family: three new Application-layer interfaces

Three collaborating ports replace `IImageGenerationClient`:

| Port | Namespace | Responsibility |
|---|---|---|
| `IImagePromptWriter` | `Application.Abstractions.Ai` | Writes a context-rich image prompt from slide copy + aesthetic descriptors |
| `IImageGenerationGateway` | `Application.Abstractions.Ai` | Sends a prompt to the selected provider; returns raw bytes + usage metadata |
| `IImageStorageWriter` | `Application.Abstractions.Social.ContentGeneration` | Stores raw bytes durably; returns a storage path for the sidecar reference |

Each port takes only primitives, Domain types, or Application VOs — no provider SDK types
cross the boundary. `IImageGenerationGateway` accepts an `ImageGenerationRequest` that carries
`OrganizationId` and `ModelId`, enabling multi-tenant and model-selection scenarios.

### 2. `IClientAestheticImageBackgroundService` — orchestrating port

A single orchestrating Application port encapsulates the full creative-background pipeline:

```
BuildCreativeBackgroundsAsync(ClientAestheticImageRequest, ct)
    → IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>
```

`ClientAestheticImageRequest` carries: `OrganizationId`, `ClientId`, `JobId`,
`FormatResultId`, `FormatKey`, `SlideCount`, `CreativeSlideIndices` (1-based, already
capped by `CreativeSlideIndexResolver`), `Aesthetic` (`ClientAestheticProfile`), and
`PerSlideCopySummaries`.

The implementation (`ClientAestheticImageBackgroundService`) orchestrates:
1. Cap creative indices to `MaxCreativeSlidesPerPackage = 2`.
2. For each creative slide, compute a SHA-256 hash over the prompt. Check sidecar
   (`carousel/slide_N.png.hash` → `{hash}|{storagePath}`) — skip generation on hit.
3. On miss, call `IImagePromptWriter.WriteAsync` → `IImageGenerationGateway.GenerateAsync` →
   `IImageStorageWriter.StoreAsync` (bounded by `Task.WhenAll` across all slides).
4. Write updated sidecar entry. Emit a `UsageEvent` for billing attribution.
5. Return a map of 1-based slide index → `ForcedDefault(storagePath, "ai-generated-background")`.
   Slides absent from the map (any per-slide failure) degrade to the CSS designer.

### 3. `CreativeSlideIndexResolver` — Domain service

`CreativeSlideIndexResolver.Resolve(IReadOnlyList<int>? indices, int slideCount)`:
- Accepts the profile's `CreativeSlideIndices` (user-configured) and the format's slide count.
- Filters to 1-based indices within `[1, slideCount]`, deduplicates, and caps at
  `MaxCreativeSlidesPerPackage = 2`.
- When `indices` is null or empty, defaults to `[1]` (first slide).

This resolver is a pure static Domain class — no dependencies, fully testable.

### 4. `CarouselBackgroundPlan.WithForcedDefaults` — immutable overlay

A new method on the existing `CarouselBackgroundPlan` record:

```csharp
WithForcedDefaults(IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault> overrides)
    → CarouselBackgroundPlan
```

Replaces per-slide entries at the 1-based indices supplied by the map. Indices outside
`[1, PerSlide.Count]` are silently ignored. The original plan is not mutated; the method
returns a new record instance (`record with {}`).

### 5. Processor integration — nullable dependency, pre-loop build step

`ContentGenerationJobProcessor` gains `IClientAestheticImageBackgroundService?` as a
**nullable constructor parameter** (default `null`). Null = CSS passthrough — no image
generation, no behavioral change, no exception. This is the rollback-safe integration point.

When the service is present AND `preLoopPolicy == ClientAesthetic`, a per-format build step
runs AFTER `formatResultId` is assigned and BEFORE the per-slide spec loop:

1. Resolve creative indices: `CreativeSlideIndexResolver.Resolve(profile.CreativeSlideIndices, slideCount)`.
2. If indices are non-empty, call `BuildCreativeBackgroundsAsync` inside a try/catch.
3. On exception: log a warning, treat as empty map (all slides fall back to CSS). The job MUST NOT fail.
4. Apply `plan = plan.WithForcedDefaults(map)` — only slots in the map change; all other
   slides retain their original plan selection.

### 6. `BackgroundSelection.CreativeSlideIndices` field on `ClientAestheticProfile`

`ClientAestheticProfile` gains `IReadOnlyList<int>? CreativeSlideIndices` (nullable). When
null or empty, `CreativeSlideIndexResolver` defaults to `[1]`.

### 7. OpenAI infrastructure adapter

`OpenAiImageModelClient` implements `IImageGenerationGateway`:
- Reads `api_key` from `ProviderCredentials.FindSecret("api_key")`.
- Uses `OrganizationId` from the request to set the `OpenAI-Organization` header.
- Calls the `/v1/images/generations` endpoint with model, prompt, size, and response format.
- Decodes the base-64 `b64_json` response into `byte[]`; returns `ImageGenerationResult`.

`ImageGenerationGateway` wraps `OpenAiImageModelClient` behind a factory lookup using
`IntegrationProviderCategory.MediaGeneration` + `"generate-image"` step key, allowing other
providers to be plugged in without code changes.

`ImageGenerationGatewayInstrumentationDecorator` adds OpenTelemetry spans and emits a
`UsageEvent` for every successful generation.

### 8. Catalog + seeder additions

`WorkflowConfigurationCatalog` gains a `"generate-image"` step under the `content-analysis`
analysis type:
- Category: `MediaGeneration`, `SupportsModelSelection: true`, `RequiresNetwork: false`.
- Description: optional; absence means CSS backgrounds for all slides.

`IntegrationCatalogSeeder` seeds OpenAI as a `MediaGeneration` provider (separate from the
existing `AiModel` entry) with models `["dall-e-3", "gpt-image-1"]`.

### 9. Retirement of `IImageGenerationClient` / `NullImageGenerationClient`

Both are deleted from the codebase (WU-9). The DI registration is removed.
Nine files lose their `using ImageGenerationRequest = ...` alias workaround.
Three reflection-based retirement guards in `RetiredImageGenerationClientTests.cs` enforce
the deletion does not regress.

## Consequences

- `IClientAestheticImageBackgroundService` is the single entry point for AI raster backgrounds.
  All future provider adapters implement `IImageGenerationGateway`; no other port changes.
- CSS backgrounds are the unchanged default. When the service is absent or policy ≠
  `ClientAesthetic`, the processor's CSS path runs byte-for-byte identically to pre-WU-8.
- Per-slide failure isolation: an exception from any single slide's image generation
  propagates only to that slide's map entry (absent = CSS). The job never fails for image-gen
  errors.
- Prompt reuse via SHA-256 sidecar reduces redundant API calls across job re-runs.
- The `generate-image` catalog step is optional; teams that omit it get CSS for all slides.
- `IImageGenerationClient` and `NullImageGenerationClient` are fully retired — no dead code
  remains in the port surface.

## Related

- Proposal / Spec / Design: `openspec/changes/content-image-generation/`
- Builds on: ADR 0005 (Background Sourcing Strategy Port — `ClientAesthetic` seam)
- Builds on: ADR 0006 (Client Aesthetic Ensure-Fresh Port — `ClientAestheticProfile` richer contract)
- Architecture gate: `IImageGenerationGateway` is in `Application.Abstractions.Ai` — no provider
  SDK types cross the layer boundary; `OpenAiImageModelClient` is Infrastructure-only.
