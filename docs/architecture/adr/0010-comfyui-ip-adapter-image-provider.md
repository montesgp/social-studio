# ADR 0010 - ComfyUI IP-Adapter Local Image Provider

## Status
Accepted

## Context

Incoders Hive must generate carousel BACKGROUND images "in each client's visual spirit"
(text is composited separately). Before this change, the platform had no usable
zero-marginal-cost image backend:

- `claude -p` (the Claude Code agent CLI) CANNOT generate images (`ExitCode=1`); its
  `IImageModelClient` adapter already advertises `SupportsImageConditioning = false`.
- The Anthropic API does not generate images at all.
- `OpenAiImageModelClient` (gpt-image-1) is the only wired image provider, but requires an
  API key the org does not have and bills USD per image.

The `comfyui-image-generation-provider` change adds a new Infrastructure adapter,
`ComfyUiImageModelClient`, behind the EXISTING bytes-only `IImageModelClient` port
(ADR 0007, extended by ADR 0008 with `DescribeAsync` / `ImageModelCapabilities` /
`ReferenceImages`). It drives a local ComfyUI server over HTTP (upload reference image
→ queue `/prompt` → poll `/history` → fetch `/view`) using a templated IP-Adapter
style-conditioning workflow, so dev/prod can run image generation at $0 marginal cost on
a local GPU while advertising `SupportsImageConditioning = true`.

This is a new Infrastructure connector touching the integration catalog
(`IntegrationCatalogSeeder`, `ExternalProviderClientFactory`, per-org seeder, health
check, DI) — `require_adr_for_architecture_changes` in `openspec/config.yaml` requires an
ADR at apply time. Five open questions from the proposal (OQ-1..OQ-5) are resolved below,
each matching `design.md` §0 verbatim.

## Decision

### New Infrastructure adapter surface

| Type | Path | Role |
|------|------|------|
| `ComfyUiImageModelClient : IImageModelClient` | `Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs` | IP-Adapter HTTP workflow; resolves reference bytes from `IGeneratedAssetStorage`; `Capabilities.SupportsImageConditioning = true`; maps every failure to `IntegrationErrors` |
| `ComfyUiSettings` | `Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiSettings.cs` | Per-org config record + `FromCredential(ResolvedProviderCredential)`; `ProviderKey = "comfyui"` |
| `ComfyUiModelCatalogClient : IAiModelCatalogClient` | `Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiModelCatalogClient.cs` | `/object_info` checkpoint sync with curated fallback |
| `ComfyUiOrganizationSeeder` + extensions | `Incoders.Hive.Infrastructure/Integrations/ComfyUiOrganizationSeeder*.cs` | Idempotent per-org `OrganizationIntegration` provisioning (mirrors `ClaudePromptCliOrganizationSeeder`) |
| `ComfyUiIntegrationHealthCheck : IExternalIntegrationHealthCheck` | `Incoders.Hive.Infrastructure/Integrations/HealthChecks/ComfyUiIntegrationHealthCheck.cs` | `GET /system_stats` connectivity probe |

Additive modifications only: a `comfyui` `SeedDefinition` in `IntegrationCatalogSeeder`
(category `MediaGeneration`, connection mode `SystemDefault`, `base_url` credential
field, curated default-checkpoint models), a `"comfyui"` switch arm in
`ExternalProviderClientFactory` for both `IImageModelClient` and `IAiModelCatalogClient`,
DI registrations (named `HttpClient`, health check, seeder) in `DependencyInjection.cs`,
and a startup seed call in `Program.cs`. No Domain or Application type is added or
modified. No schema migration.

### OQ-1 — Templated API-format JSON graph, `{{TOKEN}}` substitution, declared output node

`ComfyUiSettings.WorkflowTemplateJson` stores the ComfyUI **API-format** workflow graph
(node id → `class_type` + `inputs`) as a string with named `{{TOKEN}}` placeholders
(`{{CHECKPOINT_NAME}}`, `{{POSITIVE_PROMPT}}`, `{{NEGATIVE_PROMPT}}`,
`{{REFERENCE_IMAGE}}`, `{{IPADAPTER_MODEL}}`, `{{WIDTH}}`, `{{HEIGHT}}`, `{{SEED}}`,
`{{STEPS}}`, `{{CFG}}`). `ComfyUiImageModelClient.SubstituteTemplate` performs a plain
`string.Replace` per token (prompt text JSON-escaped before substitution), then
`JsonDocument.Parse`-validates the result — a parse failure maps to
`IntegrationErrors.AiJsonInvalid`. The adapter reads the finished output via the
settings-declared `OutputNodeId` (default `"9"`), never a hardcoded node id. A bundled
default SD1.5 + IP-Adapter template ships as an embedded resource
(`DefaultComfyUiWorkflowTemplate.json`) used when `WorkflowTemplateJson` is null/empty.
When no reference image resolves, the template's `LoadImage`/IP-Adapter chain is bypassed
via a pass-through model edge so the descriptor-prompt-only path still produces a valid
graph.

**Rationale.** Keeping the graph as a plain string template in Infrastructure config
means a model/graph swap is a config change, never a code change — this is the core "same
connector, better prod backend via config" product requirement. A declared `OutputNodeId`
decouples the adapter from node numbering so alternative templates need no code change
either.

**Rejected.** A hardcoded C# graph builder (couples node topology to code, defeats the
config-swap goal). A structured node-graph object model (over-engineered for v1; the
proposal explicitly asks for placeholder substitution). Storing the UI-format (non-API)
graph — ComfyUI's `/prompt` endpoint requires the API format.

### OQ-2 — Polling HTTP contract; OpenAI-identical `IntegrationErrors` mapping

The adapter drives the workflow via HTTP polling, not the ComfyUI websocket channel:
`POST /upload/image` (per resolved reference) → `POST /prompt` (with a generated
`client_id`) → poll `GET /history/{prompt_id}` every `PollInterval` until
`outputs[OutputNodeId].images` is present (bounded by `Timeout` and the caller's
`CancellationToken`) → `GET /view` for the final bytes. Every failure branch maps onto the
**exact same** `IntegrationErrors` codes `OpenAiImageModelClient` already returns
(`ExternalProviderUnavailable`, `ProviderTimeout`, `AiJsonInvalid`,
`ProviderCredentialUnavailable`, `AiModelRequired`) — no new error code is introduced. No
unhandled exception ever propagates out of `GenerateAsync`.

**Rationale.** Polling needs no persistent socket and is deterministically unit-testable
with a fake `HttpMessageHandler` (no live ComfyUI server required for Strict TDD).
Reusing the OpenAI adapter's exact error vocabulary means `IntegrationExecutionPolicy` and
`ImageGenerationGateway` require ZERO changes: a ComfyUI failure degrades to the CSS
designer background exactly like an OpenAI failure does today, and the job status stays
`Completed`.

**Rejected.** Websocket `/ws` completion (stateful, harder to fake-test, no resilience
benefit for a single queued prompt). Inventing new ComfyUI-specific error codes (would
force changes to the degrade/telemetry path downstream, which this change must not touch).

### OQ-3 — `/object_info` checkpoint parsing; curated fallback; "Sync models" never errors

`ComfyUiModelCatalogClient.ListModelsAsync` calls `GET {base_url}/object_info` and reads
`CheckpointLoaderSimple.input.required.ckpt_name[0]` (the installed-checkpoint enum) into
the model list (`metadata.source = "checkpoint"`), optionally appending
`IPAdapterModelLoader.ipadapter_file` (`source = "ipadapter"`) and
`LoraLoader.lora_name` (`source = "lora"`) enums when present. On **any** failure —
invalid settings, unreachable server, non-2xx response, or malformed JSON — the method
returns `Result.Success(ComfyUiSettings.DefaultCheckpoints as source="curated")`. The
catalog sync call therefore **never** returns `Result.Failure`; "Sync models" always
succeeds, at worst falling back to the curated list. This mirrors the
`Result<IReadOnlyList<AiProviderModel>>` contract and curated-fallback resilience shape
already used by `ClaudePromptCliModelCatalogClient`.

**Rationale.** The checkpoint enum IS the authoritative installed-model list; the curated
fallback keeps the model-selector UX usable even when the local ComfyUI server is down or
still starting up — the identical resilience posture claude-p already uses for its own
catalog sync.

**Rejected.** A hardcoded model list (drifts from what is actually installed on the
user's GPU). Returning `Result.Failure` when the server is unreachable (breaks the
model-sync UX for exactly the scenario — a dev-machine local GPU service — where
transient unavailability is most likely).

### OQ-4 — `ComfyUiSettings` bound via `FromCredential`, mirroring claude-p

`ComfyUiSettings.FromCredential(ResolvedProviderCredential)` reads the credential-bag
keys `base_url` (default `http://127.0.0.1:8188`), `default_checkpoint`,
`ipadapter_model`, `workflow_template_json` (empty → bundled default),
`output_node_id` (default `"9"`), `max_reference_images` (default `1`), `timeout_ms`
(default `600000` — sized for SDXL on non-CUDA/DirectML hardware where a single image can
take ~6 min), and `poll_interval_ms` (default `1000`). An invalid or non-absolute
`base_url` (and other malformed values) returns
`Result.Failure(IntegrationErrors.ProviderCredentialUnavailable)`; all other missing keys
fall back to safe defaults. `ProviderKey = "comfyui"` is a compile-time constant shared by
the catalog seed, the seeder, and the factory switch arms.

**Rationale.** Identical binding shape to `ClaudePromptCliSettings.FromCredential` keeps
per-org config and safe-default fallback in one validated place. Storing the workflow
template itself as a credential value is what lets production point the SAME connector at
a stronger backend/model purely through configuration, with no code change — the proposal's
explicit "same connector, better prod backend via config" requirement.

**Rejected.** An `IOptions`-bound `appsettings` block (not per-org; every other connector
in this system is configured per organization). Hardcoded base URL/timeout constants
(defeats prod retargeting entirely).

### OQ-5 — `/upload/image` + filename reference; `IGeneratedAssetStorage` byte-resolution; capped

Each resolved reference image is uploaded via `POST /upload/image` (multipart) and
referenced by its RETURNED server-side filename in the workflow's `LoadImage` node — not
inline base64 (ComfyUI's core `LoadImage` node has no base64 input without a custom node).
`ComfyUiImageModelClient.ResolveReferenceImageBytesAsync` opens reference bytes from
`IGeneratedAssetStorage.OpenAsync(refImage.Url)` **inside the adapter**, exactly as
`OpenAiImageModelClient` already does, capped to `settings.MaxReferenceImages`
(`Capabilities.MaxConditioningImages`, default `1`). A failed `OpenAsync` call is logged
and the reference is skipped, not fatal; if zero references resolve, the descriptor-prompt
-only template path executes with no upload call and no error.

**Rationale.** Reusing the existing storage-open seam (verified in
`OpenAiImageModelClient`) keeps the Application layer entirely byte-free — Application
code passes only a `Url` string handle via `ImageGenerationRequest.ReferenceImages`
(ADR 0008 §2); Infrastructure resolves bytes. The cap bounds the IP-Adapter input count
per generation.

**Rejected.** Inline base64 in the graph (brittle across ComfyUI installs; no core node
consumes it). Resolving bytes in the Application layer (violates the byte-free boundary
established by ADR 0008). Unbounded reference counts (open-ended cost/latency; the
default single-image IP-Adapter workflow takes one, and batch templates can raise the cap
purely via config).

### Architecture-gate compliance

No ComfyUI-specific type, raw `HttpClient`, or JSON-graph type appears in any Domain or
Application signature. Every ComfyUI HTTP/graph/parse/byte-resolution concern is confined
to `ComfyUiImageModelClient`, `ComfyUiModelCatalogClient`, and `ComfyUiSettings`
(Infrastructure only). The adapter is built via `ActivatorUtilities.CreateInstance` in
`ExternalProviderClientFactory` — the adapter type itself is NOT registered in DI, the
same pattern already used for `OpenAiImageModelClient`. `TreatWarningsAsErrors=true` is
respected; no `!` nullable-suppression operator is introduced by this change. The image
port stays bytes-only (`IImageModelClient`, `ImageGenerationRequest`,
`ImageGenerationResult`, `ImageModelCapabilities` are all reused verbatim from ADR 0007 /
ADR 0008 — no signature change). `pwsh -File scripts/gates/Run-Gates.ps1` is run at apply
time (WU-7.5) and MUST report no architecture-gate violation before this ADR is
considered fully satisfied.

## Consequences

- Adding `comfyui` as a `generate-image` provider is entirely additive and config-gated:
  removing the seed + routing reverts an org to its prior image provider with no schema
  or behavior change for any other org (CUI-INT-05, CUI-IMG-02).
- `ImagePromptWriter`/`ImageGenerationGateway`/`IntegrationExecutionPolicy` need zero
  changes — the adapter's error surface is a strict subset of the codes those components
  already handle.
- Adding a future non-ComfyUI image provider (or a second ComfyUI workflow variant)
  requires only a new adapter implementing `IImageModelClient` (or a new
  `ComfyUiSettings.WorkflowTemplateJson` credential value); no pipeline change.
- Two apply-time environment tasks remain OPEN and are NOT satisfied by this ADR alone —
  see **Operational prerequisites** below. They require a live database and a running
  ComfyUI-Zluda instance, neither of which is available in this authoring environment.
- `ComfyUiOrganizationSeeder` auto-provisions the `comfyui` **connector** (an
  `OrganizationIntegration` row) for every active tenant at startup — this is DIFFERENT
  from routing the `generate-image` **workflow step** to `comfyui`, which remains a
  per-org, per-step admin choice (see Operational prerequisites, item 1).

## Operational prerequisites

These two items are DATA/ENVIRONMENT tasks (WU-7.3, WU-7.4) that cannot be executed from
this authoring environment (no live database, no running ComfyUI-Zluda instance). They
are tracked as open checklist items in `tasks.md` and the Definition of Done until an
operator completes them.

### 1. Confirm/create the per-org `generate-image` → `comfyui` routing entry (WU-7.3)

The `comfyui` **connector** is auto-seeded for every active organization by
`ComfyUiOrganizationSeeder` (idempotent, runs at API startup) — this creates the
`platform.organization_integrations` row but does NOT route any workflow step to it.
Routing the `generate-image` step is a separate, explicit per-org configuration stored in
`platform.organization_workflow_step_configs`
(`Incoders.Hive.Infrastructure/Identity/AppIdentityDbContext.cs`, entity
`OrganizationWorkflowStepConfigurationData`, repository
`EfWorkflowStepConfigurationRepository`).

**Preferred path — admin UI (no direct DB access needed):**
1. Confirm the target org has a `Connected` `comfyui` integration: Settings → Integrations
   → provider card `ComfyUI` should show `Connected` (auto-seeded; if missing, an API
   restart re-runs `SeedComfyUiOrganizationIntegrationsAsync`).
2. Settings → Integrations → Workflow routing → `single-analyzer` workflow →
   `content-analysis` analysis type → `generate-image` step (`ai-image` operation) → add
   a route with provider `ComfyUI` and, once model sync has run, select a checkpoint.
3. Save. This calls `PUT /api/integrations/workflow-configurations/{organizationId}/single-analyzer`
   (`PlatformAdmin`-only) which persists via `ReplaceWorkflowAsync`.

**Equivalent API/data shape** (for scripted or admin-tool provisioning), the row needed
in `platform.organization_workflow_step_configs`:

```
organization_id            = <target org id>
workflow_key               = 'single-analyzer'
analysis_type               = 'content-analysis'
step_key                   = 'generate-image'
operation_key               = 'ai-image'
network_key                 = NULL
provider_id                 = <platform.integration_providers.id WHERE provider_key = 'comfyui'>
provider_key                 = 'comfyui'
organization_integration_id  = <platform.organization_integrations.id for (org, comfyui) — must already exist>
model_id                     = <a checkpoint name from ComfyUiModelCatalogClient sync, e.g. 'sd15.safetensors', or NULL to accept the settings default>
priority                     = 1
is_enabled                    = true
```

Verification query (read-only, to CONFIRM before/after, not to bypass the app):

```sql
select s.workflow_key, s.analysis_type, s.step_key, s.provider_key, s.model_id, s.priority, s.is_enabled
from platform.organization_workflow_step_configs s
where s.organization_id = '<target org id>'
  and s.workflow_key = 'single-analyzer'
  and s.step_key = 'generate-image';
```

If the query returns zero rows, no image provider is routed for that org and slides fall
back to the CSS designer background (correct pre-configuration behavior, not a bug).

### 2. Validate default workflow template node class names against the live ComfyUI-Zluda install (WU-7.4)

`DefaultComfyUiWorkflowTemplate.json`
(`Incoders.Hive.Infrastructure/Ai/Providers/DefaultComfyUiWorkflowTemplate.json`) declares
the following `class_type` values:

| Node id | `class_type` |
|---------|--------------|
| `1` | `CheckpointLoaderSimple` |
| `2`, `3` | `CLIPTextEncode` |
| `4` | `LoadImage` |
| `5` | `IPAdapterUnifiedLoader` |
| `6` | `IPAdapterAdvanced` |
| `7` | `EmptyLatentImage` |
| `8` | `KSampler` |
| `vae` | `VAEDecode` |
| `9` | `SaveImage` |

**Known deviation from `design.md` §3.3 (already corrected in the shipped template):**
the design table originally showed the `KSampler` latent input as `latent = ["7",0]`, but
real ComfyUI core `KSampler` nodes name that input `latent_image`, not `latent`. The
shipped `DefaultComfyUiWorkflowTemplate.json` (node `8`) correctly uses
`"latent_image": ["7", 0]`. This ADR records the deviation so it is not "fixed" back to
the design's incorrect wording in a future edit.

**Validation checklist (operator, against the running ComfyUI-Zluda instance):**
1. `GET {base_url}/object_info` (default `http://127.0.0.1:8188/object_info`).
2. For each `class_type` in the table above, confirm a top-level key with that EXACT name
   exists in the response.
3. `IPAdapterUnifiedLoader` / `IPAdapterAdvanced` require the ComfyUI_IPAdapter_plus
   custom-node pack to be installed; if either key is absent, install the pack (or the
   equivalent IP-Adapter node set for the target ComfyUI-Zluda build) before routing any
   org to `comfyui`.
4. If any class name differs (e.g. an older/newer IPAdapter node pack renames
   `IPAdapterAdvanced`), update `DefaultComfyUiWorkflowTemplate.json` (or supply a
   per-org `workflow_template_json` credential override) to match the installed node
   names — no code change is required either way, per OQ-1.
5. Re-run the WU-2/WU-3/WU-4 unit tests (`dotnet test -c Release`) after any template
   edit — they pin the substitution/HTTP-sequence contract, not the specific node names,
   so they remain valid regardless of which concrete template is bundled.

## Related

- Proposal / Spec / Design: `openspec/changes/comfyui-image-generation-provider/`
- Design decisions: `openspec/changes/comfyui-image-generation-provider/design.md` §0 (OQ-1..OQ-5), §10, §11
- Builds on: ADR 0007 (Image Generation Gateway Port), ADR 0008 (Image Conditioning
  Capability, `DescribeAsync`, Telemetry Correlation)
- Spec requirements realized: `openspec/specs/comfyui-image-provider/spec.md` (CUI-01..CUI-06),
  `openspec/specs/image-generation/spec.md` (CUI-IMG-01, CUI-IMG-02),
  `openspec/specs/integrations/spec.md` (CUI-INT-01..CUI-INT-05)
- Architecture gate: `pwsh -File scripts/gates/Run-Gates.ps1`
- Test suite: `Incoders.Hive.Tests/Incoders.Hive.Tests.csproj` (WU-1..WU-6 commit range: 529f75d..86e6f8f)
