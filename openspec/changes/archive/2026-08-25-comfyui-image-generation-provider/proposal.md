# Proposal: ComfyUI Image-Generation Provider (Configurable Local Image Backend)

## Change ID
`comfyui-image-generation-provider`

## Status
Proposal — DRAFT for owner review. Sibling to `client-aesthetic-conceptual-detection` (the INPUT side: it produces the digested, concept-ideated, ratio-aware image prompt). THIS change is the GENERATION BACKEND that consumes that prompt. Builds on `content-image-generation` (provider-agnostic `IImageModelClient` port, two-step generation) and `client-aligned-carousel-generation` (`ImageGenerationRequest.ReferenceImages` conditioning slot, `SupportsImageConditioning` capability). Specs and design: TBD after approval.

## Intent / Problem
Incoders Hive must generate carousel BACKGROUND images "in each client's visual spirit" (text is composited separately by us). Today there is no usable image backend:
- `claude -p` (the Claude Code agent CLI, covered by subscription with no per-token cost) CANNOT generate images — image gen returns `ExitCode=1`. Its image client already advertises `SupportsImageConditioning = false`.
- The Anthropic API does not generate images at all.
- `OpenAiImageModelClient` (gpt-image-1) is the only wired image provider, but needs an API key the org does not have AND bills USD per image.

In DEVELOPMENT the user must NOT pay per-image/per-token USD — exactly as `claude -p` covers text generation at zero marginal cost. The need: a configurable, cost-free LOCAL image-generation provider, built as a connector that mirrors the claude-p connector pattern (per-org configurable, model-sync, routed per workflow step), so the `generate-image` step finally has a real image backend.

## Confirmed Product Direction (definition of done)
1. **A real, zero-cost local image backend.** A new `comfyui` provider generates slide backgrounds on the user's local GPU at $0 per image (free `.safetensors` weights from HF/Civitai; only disk + electricity).
2. **Style-conditioned generation.** The provider consumes the digested prompt (content concept) PLUS the client's reference images (visual style) via an IP-Adapter / style-conditioning ComfyUI workflow, advertising `SupportsImageConditioning = true`.
3. **Same connector, better prod backend via config.** Dev runs against the local ComfyUI server; prod points the SAME connector at a stronger backend/model purely through configuration — no code change.
4. **Resilience unchanged.** On failure/timeout the slide degrades exactly as today (CSS fallback); the job NEVER fails.

## Decoupling Principle
The `IImageModelClient` port keeps the pipeline model-agnostic. A new model that runs in ComfyUI = a config/checkpoint swap (NO code). A brand-new non-ComfyUI provider = another adapter (NO pipeline change). License caveat for prod: SD1.5/SDXL/Qwen/IP-Adapter are commercial-OK; the Flux dev family is non-commercial without a paid BFL license.

## Scope

### In Scope (first shippable slice)
- **`ComfyUiImageModelClient : IImageModelClient`** (Infrastructure adapter). Receives the digested prompt + `ImageGenerationRequest.ReferenceImages`; drives a ComfyUI server over HTTP (templated IP-Adapter workflow: POST `/prompt` → poll `/history/{id}` → fetch via `/view`); returns image bytes. Advertises `Capabilities.SupportsImageConditioning = true`. On failure/timeout, degrades via the existing CSS-fallback path (never fails the job).
- **`ComfyUiSettings`** — configurable base URL (default `http://127.0.0.1:8188`), default checkpoint/model, workflow template, timeout. Same shape as `ClaudePromptCliSettings`.
- **`ComfyUiModelCatalogClient`** — model SYNC (like `OpenAiModelCatalogClient` / `ClaudePromptCliModelCatalogClient`) querying ComfyUI `/object_info` for installed checkpoints, so models are selectable per workflow step in the existing model-sync UX.
- **Connector seeder + capability registration + routing** so the provider connects per org and the `generate-image` step routes to `comfyui` (today the image route wrongly falls to claude-p; this makes the image route real). Mirrors `ClaudePromptCliOrganizationSeeder` + `IntegrationCatalogSeeder` + `ExternalProviderClientFactory` + health check.
- **DI wiring.**

### Out of Scope (deferred)
- **Aesthetic/conceptual DETECTION + concept-ideation prompt-writing** — that is the SEPARATE, already-planned `client-aesthetic-conceptual-detection` change. THIS change consumes the digested prompt; it does NOT produce it.
- **Installing/operating ComfyUI** — the user handles ComfyUI-Zluda setup + model downloads on the AMD RX 580. The proposal targets the CONNECTOR, hardware-agnostic.
- **Per-client style LoRA training** — future refinement.
- **Changing behavior for orgs not routed to `comfyui`** — additive only.

### Scope Boundaries
- Hexagonal: Domain ← Application ← Infrastructure/Api. NO ComfyUI/HTTP/provider SDK types in Domain/Application — all ComfyUI HTTP/workflow concerns confined to the Infrastructure adapter (exactly as OpenAI/claude HTTP concerns are today).
- Back-compat / rollback-safe: existing providers and the CSS-fallback resilience path stay unchanged.
- Strict-TDD project; `TreatWarningsAsErrors=true`.

## Capabilities

### New Capabilities
- `comfyui-image-provider`: a configurable local ComfyUI-backed `IImageModelClient` adapter with style-conditioning (IP-Adapter), settings, model-catalog sync, connector seeder, and DI wiring.

### Modified Capabilities
- `image-generation`: the `generate-image` step gains a real, image-conditioning provider (`SupportsImageConditioning = true`) selectable via config; routing for the image step resolves to `comfyui` when configured.
- `integrations`: a new connector definition (catalog seed + per-org seeder + capability registration + health check + provider factory wiring) for `comfyui`.

## Approach
Mirror the claude-p connector end-to-end as the template. Add an Infrastructure `ComfyUiImageModelClient` behind the existing `IImageModelClient` port that translates the digested prompt + reference images into a templated IP-Adapter ComfyUI workflow and drives it over HTTP (queue `/prompt`, poll `/history`, fetch `/view`), returning bytes and mapping any failure/timeout onto the existing degrade path. Add `ComfyUiSettings` for per-org configuration (base URL, checkpoint, workflow template, timeout). Add `ComfyUiModelCatalogClient` that parses `/object_info` for installed checkpoints (and IP-Adapter models/LoRAs) so models are selectable per step. Register the connector (catalog seed, per-org seeder, capability registration, provider factory, health check) and wire DI, so the image route resolves to `comfyui` when configured while every other provider and route stays untouched.

## Affected Areas (intent level)

| Area | Layer | Impact | Description |
|------|-------|--------|-------------|
| `Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs` | Infrastructure | New | `IImageModelClient` adapter; IP-Adapter HTTP workflow; `SupportsImageConditioning = true`; degrade on failure |
| `Infrastructure/Ai/Providers/ComfyUiSettings.cs` | Infrastructure | New | Base URL / checkpoint / workflow template / timeout config (mirrors `ClaudePromptCliSettings`) |
| `Infrastructure/Ai/Providers/ComfyUiModelCatalogClient.cs` | Infrastructure | New | Model sync via `/object_info` (mirrors `OpenAiModelCatalogClient`) |
| `Infrastructure/Integrations/IntegrationCatalogSeeder.cs` | Infrastructure | Modified | Add `comfyui` connector definition + capability registration |
| `Infrastructure/Integrations/ComfyUiOrganizationSeeder*.cs` | Infrastructure | New | Per-org connector seeder (mirrors `ClaudePromptCliOrganizationSeeder`) |
| `Infrastructure/Integrations/ExternalProviderClientFactory.cs` | Infrastructure | Modified | Resolve `comfyui` image client |
| `Infrastructure/Integrations/HealthChecks/ComfyUiIntegrationHealthCheck.cs` | Infrastructure | New | Connector health check (mirrors claude-p) |
| `generate-image` routing config | Infrastructure/Config | Modified | Route image step to `comfyui` when configured; other routes unchanged |
| `Infrastructure/DependencyInjection/DependencyInjection.cs` + `ApiService/Program.cs` | Infrastructure/Api | Modified | DI registration + startup wiring |

## Risks

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| ComfyUI server unreachable / slow on the dev GPU | Medium | Configurable timeout + cancellation; degrade to CSS fallback (job never fails); health check surfaces connectivity |
| Workflow-template / IP-Adapter node parameterization is brittle across model installs | Medium | Make the workflow template configurable in `ComfyUiSettings`; parse `/object_info` to validate available nodes/checkpoints |
| Reference-image delivery format mismatch (upload vs inline base64) | Medium | Confirm contract in design (OQ); adapter owns the translation; bytes-only across the port |
| Provider SDK / HTTP types leaking into Domain/Application | Low | Keep all ComfyUI concerns behind the `IImageModelClient` adapter; architecture gate |
| Routing change accidentally affects non-`comfyui` orgs | Low | Additive, config-gated routing; default behavior unchanged when `comfyui` not configured |
| Prod license non-compliance (Flux dev) | Low | Document license caveat; default to commercial-OK checkpoints (SD1.5/SDXL/Qwen/IP-Adapter) |

## Rollback Plan
- All new files are additive; removing the `comfyui` connector seed + routing reverts the `generate-image` step to its prior provider with no schema or behavior change for other orgs.
- Routing to `comfyui` is config-gated; clearing the config restores prior behavior immediately.
- The CSS-fallback resilience path is reused unchanged, so a misbehaving ComfyUI server degrades gracefully rather than breaking jobs.
- No changes to existing providers (`OpenAi*`, `ClaudePromptCli*`) — they continue to function exactly as before.

## Dependencies
- `content-image-generation` — provider-agnostic `IImageModelClient` port + two-step generation (the seam this provider plugs into).
- `client-aligned-carousel-generation` — `ImageGenerationRequest.ReferenceImages` conditioning slot + `SupportsImageConditioning`/`DescribeAsync` capability contract.
- `client-aesthetic-conceptual-detection` (sibling) — produces the digested, concept-ideated, ratio-aware prompt this provider consumes (input side).
- An operational ComfyUI server (user-managed: ComfyUI-Zluda + an SD1.5 checkpoint + IP-Adapter model on the local GPU, listening on `127.0.0.1:8188`).
- The claude-p connector files as the implementation template (`ClaudePromptCli*`, `IntegrationCatalogSeeder`, `ExternalProviderClientFactory`).

## Success Criteria
- [ ] A configurable `comfyui` provider connects per org and is selectable on the `generate-image` step via the existing model-sync UX.
- [ ] `ComfyUiImageModelClient` produces a real background image from the digested prompt + client reference images via an IP-Adapter workflow, advertising `SupportsImageConditioning = true`.
- [ ] Dev generation runs at $0 per image against the local ComfyUI server; prod can swap backend/model purely via config (no code change).
- [ ] `ComfyUiModelCatalogClient` lists installed checkpoints from `/object_info` so models are selectable per workflow step.
- [ ] On ComfyUI failure/timeout the slide degrades to CSS fallback and the job still completes — resilience contract unchanged.
- [ ] Orgs not routed to `comfyui` see ZERO behavior change; existing providers unaffected.
- [ ] No ComfyUI/HTTP/provider types leak into Domain/Application; all MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`); Strict TDD — adapter, settings, catalog, and seeder behavior tested before implementation.

## Open Questions (for spec/design)
- **OQ-1 — Workflow representation.** How the ComfyUI workflow template is represented/stored/selected, and how the prompt + reference images + IP-Adapter nodes are parameterized/injected into it.
- **OQ-2 — HTTP contract.** Queue `/prompt` → poll `/history` → fetch `/view`; websocket vs polling for completion; timeout/cancellation; precise mapping of ComfyUI errors onto the existing degrade path.
- **OQ-3 — Catalog parsing.** Exact `/object_info` parsing for checkpoints (and IP-Adapter models/LoRAs) into the per-step model list.
- **OQ-4 — Settings + per-org config.** Final `ComfyUiSettings` shape and how the workflow template is stored and selected per org.
- **OQ-5 — Reference-image delivery.** Deliver reference bytes via `/upload/image` vs inline base64 in the workflow graph; how many references map to IP-Adapter inputs.
