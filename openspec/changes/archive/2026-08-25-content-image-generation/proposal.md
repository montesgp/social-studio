# Proposal: Content Image Generation (Real AI Raster Backgrounds for Creative Slides)

## Change ID
`content-image-generation`

## Status
Proposal — DRAFT for owner review. Builds on `content-aesthetic-generation-phase-2` (feed-derived multi-signal `ClientAestheticProfile`) and `content-background-sourcing-policy` (`ClientAesthetic` strategy + `BackgroundSelection`). Specs and design: TBD after approval.

## Intent / Problem

For `ClientAesthetic` clients (e.g. Atlas), real feeds mix ~2 **creative** slides — actual raster images in the client's visual style — with flat color+text slides. Today the engine renders ALL slides via the HTML/CSS designer (flat backgrounds + descriptors). It CANNOT produce a real image-style creative slide, so AI output is visibly not what the client posts.

Verified gaps:
- `IImageGenerationClient` exists but is wired to `NullImageGenerationClient` ("image-generation.not-supported-v1") and is NEVER called; its signature lacks `OrganizationId`, so it cannot do per-org routing.
- `IAiCompletionGateway` returns JSON, NOT bytes — image generation needs a SEPARATE byte-returning port.
- A previous hybrid that pasted the client's REAL finished slides verbatim was REMOVED — the compositing mechanism is correct; only the image SOURCE must become a GENERATED image.
- Anthropic does NOT generate raster pixels; it can WRITE the per-slide image prompt. Pixels require an image-capable provider/model selected by the org.

## Scope

### In Scope (first shippable slice)
- **Configurable `generate-image` workflow step** added to `WorkflowConfigurationCatalog` (code-only static list, no migration), category `MediaGeneration`. Org selects provider/model exactly like existing text routes; the Integrations web UI renders it automatically.
- **Provider-agnostic image gateway**: new `IImageGenerationGateway` + `IImageModelClient` ports (Application) returning image bytes, with a `ProviderCallContext` usage-ledger instrumentation decorator mirroring the text-AI gateway. **OpenAI** (`dall-e-3` / `gpt-image-1`) seeded as the FIRST `MediaGeneration` adapter — behind an Infrastructure adapter, NOT a hard dependency.
- **Two-step generation**: Step 1 the org's TEXT route writes a per-slide image prompt from `ClientAestheticProfile` (style descriptors, motifs, palette, reference-image context); Step 2 the org's IMAGE route renders pixels.
- **Durable storage**: persist generated images in Supabase Storage (like format assets); reference by URL/path in the composition envelope — NOT as data-URI.
- **Inject as editable-text slides**: for `ClientAesthetic` creative slides, feed the generated image via `BackgroundSelection.ForcedDefault(url, "ai-generated-background")` so the existing `ContentGenerationJobProcessor` / `BuildTemplateBackedMarkup` path composites it as `data-layer=background` with editable `data-layer=content` text; studio rebuilds editable text over the raster (near-zero studio change).
- **Creative-vs-flat selection (v1)**: a configurable creative-slide set on `ClientAestheticProfile` (default first+last, or first 2); flat slides keep the existing CSS designer. Data-model-ready for the future extractor.
- **Resilience (non-negotiable)**: prompt-writer OR image-gen failure → log + fall back to the CSS designer background; NEVER fail the format/job on image-gen error; image-gen is never on the critical path for completion.

### Out of Scope (deferred follow-ups)
- **Auto-detection of which/how many slides are "creative" per client** (extractor tags the creative-slide pattern from the client's real feed). v1 ships a configurable set; the data model is forward-compatible.
- **Second image provider adapter** beyond OpenAI (the seam supports any; only OpenAI v1 is built now).
- **Image-to-image / reference-image conditioning** (passing the client's real creatives as init images) — design-compatible, not built.
- **Story/FeedPost creative image backgrounds** — v1 targets `ClientAesthetic` carousel creative slides; other formats untouched.

### Scope Boundaries
- Provider SDKs (OpenAI, etc.) stay behind Infrastructure adapters; NO provider types in Domain/Application (architecture gate).
- The `generate-image` step routes per ORGANIZATION via `IWorkflowConfigurationResolver`, mirroring `single-analyzer`/`generate-content` — not hardcoded to one provider.
- Generated-background responsibility ends at producing a `BackgroundSelection`; rendering/guardrails/studio are reused as-is.

## Capabilities

### New Capabilities
- `image-generation`: provider-agnostic, workflow-configured text-to-image generation — `generate-image` step, image gateway/ports + usage ledger, two-step prompt-then-render, durable Supabase storage of generated images, and the resilience/fallback contract.

### Modified Capabilities (additive deltas)
- `content-generation`: `ClientAesthetic` creative slides composited from a generated raster background (editable text on top) via the existing `BackgroundSelection.ForcedDefault` path; CSS-designer fallback on failure; per-slide creative-vs-flat routing.
- `background-sourcing-policy`: `ClientAesthetic` strategy emits a generated-image background for creative slides and the CSS-descriptor background for flat slides.
- `integrations`: new `generate-image` configurable step under `MediaGeneration`; OpenAI seeded as the first provider option (resolver + catalog).

## Approach

Add `generate-image` to `WorkflowConfigurationCatalog` (code-only) so each org picks an image provider/model like its text routes. Introduce `IImageGenerationGateway` + `IImageModelClient` Application ports returning bytes, instrumented by a usage-ledger decorator mirroring the text gateway; ship an OpenAI Infrastructure adapter as the first `MediaGeneration` provider. For `ClientAesthetic` creative slides, run two steps: the org's TEXT route writes the per-slide image prompt from `ClientAestheticProfile`; the org's IMAGE route renders pixels. Persist pixels to Supabase Storage and reference by URL. Feed the URL as `BackgroundSelection.ForcedDefault(url, "ai-generated-background")` so the existing per-slide compositing produces a raster background with EDITABLE text; the studio rebuilds editable text over the raster unchanged. Flat slides keep the CSS designer. Any prompt-writer or image-gen failure logs and falls back to the CSS background — image-gen is never on the completion critical path.

## Affected Areas (intent level)

| Area | Layer | Impact | Description |
|------|------|--------|-------------|
| `Application/.../WorkflowConfigurations/WorkflowConfigurationCatalog.cs` | Application | Modified | Add `generate-image` step (MediaGeneration); code-only, no migration |
| `Application/Abstractions/.../IImageGenerationGateway` + `IImageModelClient` (new) | Application | New | Byte-returning image ports + ProviderCallContext usage ledger |
| `Application/Abstractions/Ai/IImageGenerationClient.cs` | Application | Modified/Removed | Replace/retire the Null-wired port; add `OrganizationId` routing or supersede |
| `Application/.../ContentGeneration/Formats/Carousel` + Orchestration | Application | Modified | Two-step prompt-then-render; per-slide creative-vs-flat routing; fallback |
| `Domain/Social/.../ClientAestheticProfile` | Domain | Modified | Add creative-slide set (nullable; extractor-ready) |
| `Infrastructure/.../Ai/OpenAiImageModelClient` (new) | Infrastructure | New | First MediaGeneration adapter (`dall-e-3` / `gpt-image-1`); SDK behind port |
| `Infrastructure/.../Storage` (generated-asset pattern) | Infrastructure | Reused/Modified | Persist generated images to Supabase Storage; durable URL refs |
| `Application/.../ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` | Application | Modified | Inject generated background as `ForcedDefault`; reuse compositing/editable text |
| `ApiService/Endpoints/Social/...Integrations` (auto-rendered) | Api | None/Minimal | Integrations UI renders the new step from the API automatically |

## Risks

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| Image-gen latency/cost added to the job path | Medium/High | Bound creative slides per job; never block completion; fallback to CSS; cap/guardrail (OQ-2). |
| Provider abstraction leaks (OpenAI types reaching Application) | Medium | Strict ports; bytes-only contract; adapter in Infrastructure; architecture gate. |
| `composition_spec_json` bloat | Low | Store images in Supabase; reference by URL, never data-URI. |
| Prompt-writer produces off-style/empty prompt | Medium | Derive from `ClientAestheticProfile`; on weak/empty signal fall back to CSS background. |
| Regenerate vs reuse on studio re-generation unclear | Medium | Flagged as OQ-3 for design; default lean = reuse stored background unless explicitly regenerated. |
| `NullImageGenerationClient` callers / signature change | Medium | Port was never called; superseding/retiring it is low-blast; cover with tests (Strict TDD). |

## Rollback Plan
- The `generate-image` step is additive in the catalog; removing it (or an org leaving it unconfigured) reverts to CSS-designer backgrounds with no schema change.
- New ports/adapter and the `ClientAestheticProfile` creative-set field are additive; the field is nullable (reversible drop).
- Generated-background injection is gated on policy + a configured image route + the creative-slide set; absent any of these, the existing CSS path runs unchanged.
- Supabase generated-image storage reuses the existing generated-asset pattern; no destructive migration.

## Dependencies
- `content-aesthetic-generation-phase-2` (`ClientAestheticProfile` style descriptors/motifs/palette/reference context) — source for the image prompt.
- `IWorkflowConfigurationResolver` + `WorkflowConfigurationCatalog` (per-org dispatch) — the routing seam.
- `IntegrationProviderCategory.MediaGeneration` (already exists) — the provider category for image routes.
- Supabase Storage adapter (`IGeneratedAssetStorage` pattern) — durable generated-image hosting.
- Existing per-slide compositing (`BuildTemplateBackedMarkup`, `CompositionSpecEnvelope`, `BackgroundSelection.ForcedDefault`) — reused for editable text over raster.
- An org-configured IMAGE route on a real provider (OpenAI v1) — required to render pixels; absent it, CSS fallback runs.

## Success Criteria
- [ ] An org can configure a `generate-image` provider/model in the Integrations UI exactly like a text route; OpenAI appears as the first MediaGeneration option.
- [ ] For a `ClientAesthetic` carousel, the designated creative slides render a REAL generated raster background in the client's style; flat slides keep the CSS designer.
- [ ] The two-step flow uses the org's TEXT route for the per-slide image prompt and the org's IMAGE route for pixels — no provider hardcoding.
- [ ] Generated images are stored in Supabase and referenced by URL (not data-URI); `composition_spec_json` stays lean.
- [ ] Text on a creative slide is EDITABLE in the Content Studio (rebuilt over the raster background).
- [ ] Any prompt-writer or image-gen failure logs and falls back to the CSS background; the job NEVER fails on image-gen error.
- [ ] No provider types leak into Domain/Application; all MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`); Strict TDD — ports/routing tested before implementation.

## Open Questions (for spec/design)
- **OQ-1 — Creative-slide selection default.** Exact v1 default for the configurable creative set: first+last, first 2, or N-from-`AverageSlidesPerCarousel`? How is it configured per client until the extractor lands?
- **OQ-2 — Per-job image-gen cap / cost guardrail.** Max generated slides per job/package; org-level budget or rate guard; downscale/target resolution for cost.
- **OQ-3 — Regenerate vs reuse on studio re-generation.** When a package is re-generated/edited in the studio, reuse the stored background or call the image route again? Default lean = reuse unless explicitly regenerated.
- **OQ-4 — Sync-parallel vs deferred generation.** Generate creative-slide images inline (parallel within the job) or as a deferred/async step that backfills, given latency? Impact on completion semantics and the resilience contract.
- **OQ-5 — Port consolidation.** Retire `IImageGenerationClient`/`NullImageGenerationClient` outright vs evolve its signature (add `OrganizationId`) into the new `IImageModelClient`.
- **OQ-6 — Prompt-writer route reuse.** Reuse the existing `generate-content`/`ai-generation` text step for the image-prompt write, or add a dedicated `image-prompt` sub-step for independent versioning/observability.
- **OQ-7 — Image-to-image readiness.** Should v1's prompt/port shape already reserve a reference-image (init-image) slot so the deferred image-conditioning follow-up plugs in without a contract change?
