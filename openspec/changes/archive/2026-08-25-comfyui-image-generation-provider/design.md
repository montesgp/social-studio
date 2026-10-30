# Design — comfyui-image-generation-provider (Configurable Local IP-Adapter Image Backend)

Hexagonal design that gives the `generate-image` step a real, zero-cost LOCAL image backend by adding ONE
Infrastructure adapter behind the EXISTING bytes-only `IImageModelClient` port. `ComfyUiImageModelClient`
translates the digested prompt + the client's reference creatives into a templated IP-Adapter ComfyUI
workflow (API-format JSON), drives a local ComfyUI server over HTTP (upload reference → queue `/prompt` →
poll `/history` → fetch `/view`), and returns image bytes — advertising `SupportsImageConditioning = true`.
It mirrors the `claude-p` connector end-to-end (settings, model-catalog sync, per-org seeder, capability
registration, provider factory, health check, DI) and reuses the conditioning contract that the
`client-aligned-carousel-generation` change already shipped (`ImageGenerationRequest.ReferenceImages`,
`ImageModelCapabilities`, `IImageGenerationGateway.DescribeAsync`).

This change is **Infrastructure-only**. It adds NO Domain or Application types: the port, the request shape,
the capability record, and the byte-resolution seam (`IGeneratedAssetStorage`) all exist already. Every
ComfyUI HTTP/JSON-graph concern is CONFINED to the new adapter + settings, exactly as OpenAI/Claude HTTP
concerns are today (verified `OpenAiImageModelClient`, `ClaudePromptCliImageModelClient`).

It resolves the proposal's five open questions (OQ-1..OQ-5), maps component changes, fixes the HTTP contract +
error mapping onto the existing degrade path, the `/object_info` catalog shape, the `ComfyUiSettings` binding,
the reference-delivery flow, a Strict-TDD-friendly test-first order (no live server), and a sliced
implementation order. It does NOT enumerate task steps — that is the `sdd-tasks` phase.

## Status
Design — architecture decisions resolved. Implements the technical HOW for `proposal.md`. Purely additive:
all new files live under `Infrastructure/Ai/Providers/` and `Infrastructure/Integrations/`; the only
modifications are additive switch arms in `ExternalProviderClientFactory`, a new `SeedDefinition` in
`IntegrationCatalogSeeder`, and DI registrations. No schema migration. No Domain/Application change. A new
Infrastructure provider adapter touching the integration catalog → an ADR under `docs/architecture/adr/` is
authored at apply time (`require_adr_for_architecture_changes`). Targets branch `feat/claude-p-provider`.

## Scope reminder (Non-Goals are hard boundaries)
Ships: `ComfyUiImageModelClient` (IP-Adapter HTTP workflow, conditioning, degrade-on-failure), `ComfyUiSettings`
(per-org config), `ComfyUiModelCatalogClient` (`/object_info` checkpoint sync), connector seed + per-org
seeder + capability registration + provider-factory wiring + HTTP health check, DI. Does NOT: build the
aesthetic/conceptual DETECTION or concept-ideation prompt-writing (the sibling `client-aesthetic-conceptual-
detection` change — THIS change CONSUMES the digested prompt, never produces it), does NOT install or operate
ComfyUI (user-managed ComfyUI-Zluda + weights on the local GPU), does NOT train per-client LoRAs, does NOT
change behaviour for orgs not routed to `comfyui` (additive, config-gated), and does NOT alter the CSS-fallback
resilience contract (a failing ComfyUI server degrades exactly as today; the job never fails).

---

## 0. The decisions (read this first)

| # | Open question | Decision | One-line rationale |
|---|---------------|----------|--------------------|
| **OQ-1** | Workflow representation | Store the IP-Adapter graph as an **API-format JSON template string** in `ComfyUiSettings.WorkflowTemplateJson` with named `{{TOKEN}}` placeholders; the adapter substitutes scalars (prompt, dims, seed, checkpoint, IP-Adapter model, reference filename) by string replace, then `JsonDocument.Parse`-validates, and reads the output via the settings-declared `OutputNodeId`. A concrete default SD1.5 + IP-Adapter template ships as the fallback. | A templated string keeps the graph 100% in Infrastructure config (swap model/graph with NO code), matches OQ-1's "placeholder substitution" wording, and the declared `OutputNodeId` decouples the adapter from node numbering. |
| **OQ-2** | HTTP contract | **Polling**, not websocket: upload each reference via `POST /upload/image` → `POST /prompt` with a generated `client_id` → poll `GET /history/{prompt_id}` every `PollInterval` until the `OutputNodeId` has `images[]` (bounded by `Timeout` + `CancellationToken`) → `GET /view` for bytes. Every ComfyUI failure maps onto the SAME `IntegrationErrors` codes `OpenAiImageModelClient` uses, so the existing execution policy degrades to CSS identically. | Polling needs no persistent socket and is trivially testable with a fake `HttpMessageHandler`; reusing the OpenAI error codes means ZERO change to the degrade/telemetry path. |
| **OQ-3** | Catalog parsing | `ComfyUiModelCatalogClient` GETs `/object_info`, reads `CheckpointLoaderSimple.input.required.ckpt_name[0]` (the checkpoint enum) into the model list, optionally appends IP-Adapter/LoRA loader enums tagged by `metadata.source`; on any failure it falls back to `ComfyUiSettings.DefaultCheckpoints` so "Sync models" NEVER errors. Mirrors the `ClaudePromptCliModelCatalogClient` `Result<IReadOnlyList<AiProviderModel>>` contract. | The checkpoint enum IS the installed-model list; the curated fallback preserves the model-sync UX when the server is down — identical resilience shape to claude-p. |
| **OQ-4** | Settings + per-org config | `ComfyUiSettings` record bound via `FromCredential(ResolvedProviderCredential)` (mirrors `ClaudePromptCliSettings.FromCredential`): `base_url` (default `http://127.0.0.1:8188`), `default_checkpoint`, `ipadapter_model`, `workflow_template_json` (or empty → bundled default), `output_node_id`, `timeout_ms`, `poll_interval_ms`, `max_reference_images`. `ProviderKey = "comfyui"`. | Same credential-bag binding as claude-p keeps per-org config and the safe-default fallback in ONE place; the workflow template as a credential means prod points the SAME connector at a stronger graph/model purely via config. |
| **OQ-5** | Reference-image delivery | `POST /upload/image` (multipart) then reference by RETURNED filename in the workflow `LoadImage` node — NOT inline base64. Resolve reference bytes from `IGeneratedAssetStorage.OpenAsync(refImage.Url)` INSIDE the adapter (exactly as `OpenAiImageModelClient`), capped to `Capabilities.MaxConditioningImages` (= `max_reference_images`, default 1). | ComfyUI core `LoadImage` consumes a server-side filename, not base64; reusing the existing storage-open seam keeps the Application layer byte-free and the cap bounds the IP-Adapter input count. |

Each decision is expanded in [Decisions in depth](#8-decisions-in-depth).

---

## 1. Architecture approach

Dependency rule, unchanged: **Domain ← Application ← Infrastructure / Api**. No ComfyUI/`HttpClient`/JSON-graph
type appears in Domain or Application. Every provider concern stays behind the EXISTING `IImageModelClient`
port; the gateway, routing, credential resolution, execution policy, capability-describe, and the conditioning
request shape are REUSED as-is from `content-image-generation` + `client-aligned-carousel-generation`.

The adapter plugs into the verified seam in `ImageGenerationGateway.ExecuteCandidateAsync`, which calls
`_clientFactory.Create<IImageModelClient>(credential)` and then `client.GenerateAsync(request)`. The factory
resolves the concrete adapter by `credential.ProviderKey`. Adding `comfyui` therefore means: a new adapter
class + a new switch arm — no gateway, routing, or pipeline change. Capability is read by
`ImageGenerationGateway.DescribeAsync` from `client.Capabilities`, so advertising
`SupportsImageConditioning = true` is a one-line property on the adapter (verified gateway flow).

The connector becomes selectable per org through the same four mirror points as claude-p: a catalog
`SeedDefinition` (so it appears in the integrations catalog), `ComfyUiOrganizationSeeder` (per-org connect),
the `ExternalProviderClientFactory` switch (so the gateway can build it), and a health check (so "Test
connection" probes the real server). Routing the `generate-image` step to `comfyui` is a per-org workflow-
configuration choice (data), not code — when unset, the org's existing image route is untouched.

---

## 2. Component map — ports, adapters, layers

### 2.1 Domain / Application
**No change.** `IImageModelClient`, `IImageGenerationGateway.DescribeAsync`, `ImageModelCapabilities`,
`ImageGenerationRequest` (incl. `ReferenceImages`/`Init`/`BackgroundOnly`/`CorrelationId`),
`ImageGenerationResult`, `ImageInitReference`, and `IGeneratedAssetStorage` all already exist and are reused
verbatim. (Verified `IImageGenerationGateway.cs`, `IGeneratedAssetStorage.cs`.)

### 2.2 Infrastructure (adapters, settings, wiring — all new/additive)
| Element | Path | Change |
|---------|------|--------|
| `ComfyUiImageModelClient : IImageModelClient` — IP-Adapter HTTP workflow; resolves ref bytes from `IGeneratedAssetStorage`; `SupportsImageConditioning = true`; maps every failure to `IntegrationErrors` | `Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs` | **New** |
| `ComfyUiSettings` record + `FromCredential` (`ProviderKey="comfyui"`, defaults, `DefaultCheckpoints`) | `Infrastructure/Ai/Providers/ComfyUiSettings.cs` | **New** |
| `ComfyUiModelCatalogClient : IAiModelCatalogClient` — `/object_info` checkpoint sync + curated fallback | `Infrastructure/Ai/Providers/ComfyUiModelCatalogClient.cs` | **New** |
| `ComfyUiOrganizationSeeder` + `ComfyUiOrganizationSeederExtensions` — per-org connect (mirrors claude-p seeder) | `Infrastructure/Integrations/ComfyUiOrganizationSeeder*.cs` | **New** |
| `ComfyUiIntegrationHealthCheck : IExternalIntegrationHealthCheck` — HTTP probe `GET {base_url}/system_stats` (200 = connected) | `Infrastructure/Integrations/HealthChecks/ComfyUiIntegrationHealthCheck.cs` | **New** |
| `IntegrationCatalogSeeder` — add a `comfyui` `SeedDefinition` (category `MediaGeneration`, models = default checkpoints, BYOK+Managed+SystemDefault, `base_url` credential field) | `Infrastructure/Integrations/IntegrationCatalogSeeder.cs` | **Modified (additive)** |
| `ExternalProviderClientFactory` — add `"comfyui"` arms to `IImageModelClient` and `IAiModelCatalogClient` switches | `Infrastructure/Integrations/ExternalProviderClientFactory.cs` | **Modified (additive)** |
| DI — register health check, seeder, named `HttpClient`; (factory builds the adapter/catalog via `ActivatorUtilities`) | `Infrastructure/DependencyInjection/DependencyInjection.cs` | **Modified (additive)** |
| Startup — invoke `SeedComfyUiOrganizationIntegrationsAsync` alongside the claude-p seed | `ApiService/Program.cs` | **Modified (additive)** |

> **Wiring check (verified):** `ExternalProviderClientFactory.Create<T>` builds the target type via
> `ActivatorUtilities.CreateInstance(_serviceProvider, targetType, credential)`. The adapter therefore needs
> only its non-credential deps (`IHttpClientFactory`, `IGeneratedAssetStorage`, `ILogger`) registered in DI;
> the adapter type itself is NOT registered, exactly like `OpenAiImageModelClient`.

---

## 3. The contracts (Infrastructure-internal — no port change)

### 3.1 `ComfyUiSettings` (OQ-4)
```csharp
public sealed record ComfyUiSettings(
    string BaseUrl,                 // default http://127.0.0.1:8188
    string? DefaultCheckpoint,      // ckpt_name; PreferredModel/SelectedModel overrides
    string? IpAdapterModel,         // ipadapter_file enum value
    string? WorkflowTemplateJson,   // API-format graph w/ {{TOKEN}} placeholders; null/empty -> bundled default
    string OutputNodeId,            // node id whose history output carries images[] (default "9")
    int MaxReferenceImages,         // cap -> Capabilities.MaxConditioningImages (default 1)
    TimeSpan Timeout,               // overall budget (default 180s)
    TimeSpan PollInterval,          // history poll cadence (default 1s)
    IReadOnlyList<string> DefaultCheckpoints) // curated fallback for catalog sync
{
    public const string ProviderKey = "comfyui";
    public static Result<ComfyUiSettings> FromCredential(ResolvedProviderCredential credential);
}
```
Binding mirrors `ClaudePromptCliSettings.FromCredential`: read credential keys (`base_url`,
`default_checkpoint`, `ipadapter_model`, `workflow_template_json`, `output_node_id`, `max_reference_images`,
`timeout_ms`, `poll_interval_ms`), validate (base URL well-formed absolute http(s); positive ints), else
`Result.Failure(IntegrationErrors.ProviderCredentialUnavailable)`. Missing keys fall back to safe defaults.

### 3.2 `ComfyUiImageModelClient.Capabilities`
```csharp
public ImageModelCapabilities Capabilities { get; } =
    new(SupportsImageConditioning: true, MaxConditioningImages: /* settings.MaxReferenceImages */ 1);
```

### 3.3 Default IP-Adapter workflow template shape (OQ-1)
A standard **SD1.5 + IPAdapter style-conditioning** API-format graph (node id → `class_type` + `inputs`),
shipped as the bundled default when `WorkflowTemplateJson` is empty. Placeholders in **bold**:

| Node id | `class_type` | Key inputs (placeholders) |
|---------|--------------|---------------------------|
| `1` | `CheckpointLoaderSimple` | `ckpt_name = {{CHECKPOINT_NAME}}` |
| `2` | `CLIPTextEncode` (positive) | `text = {{POSITIVE_PROMPT}}`, `clip = ["1",1]` |
| `3` | `CLIPTextEncode` (negative) | `text = {{NEGATIVE_PROMPT}}`, `clip = ["1",1]` |
| `4` | `LoadImage` | `image = {{REFERENCE_IMAGE}}` (uploaded filename) |
| `5` | `IPAdapterUnifiedLoader` | `preset/ipadapter = {{IPADAPTER_MODEL}}`, `model = ["1",0]` |
| `6` | `IPAdapterAdvanced` | `model = ["5",0]`, `image = ["4",0]`, `weight = 0.8` |
| `7` | `EmptyLatentImage` | `width = {{WIDTH}}`, `height = {{HEIGHT}}`, `batch_size = 1` |
| `8` | `KSampler` | `model = ["6",0]`, `positive = ["2",0]`, `negative = ["3",0]`, `latent = ["7",0]`, `seed = {{SEED}}`, `steps = {{STEPS}}`, `cfg = {{CFG}}` |
| `vae` | `VAEDecode` | `samples = ["8",0]`, `vae = ["1",2]` |
| `9` | `SaveImage` | `images = ["vae",0]` ← **`OutputNodeId = "9"`** |

Substitution: `string.Replace` each `{{TOKEN}}` with the JSON-escaped value (prompt escaped via
`JsonEncodedText`/`JsonSerializer.Serialize(string)` then trimmed of quotes), then `JsonDocument.Parse` to
validate; a parse failure → `IntegrationErrors.AiJsonInvalid`. `{{REFERENCE_IMAGE}}` is the filename returned
by `/upload/image`; when no reference resolves, the template's `LoadImage`/IP-Adapter chain is bypassed by
substituting a pass-through model edge (`model = ["1",0]` on KSampler) — the descriptor-prompt-only path.

### 3.4 HTTP call sequence + error mapping (OQ-2)
```
0. settings  = ComfyUiSettings.FromCredential(credential)        // fail -> ProviderCredentialUnavailable
   checkpoint = request.PreferredModel ?? credential.SelectedModel ?? settings.DefaultCheckpoint
                                                                   // missing -> AiModelRequired
   bytes      = await ResolveReferenceImageBytesAsync(request)    // IGeneratedAssetStorage, cap MaxConditioningImages
1. upload  POST {base}/upload/image (multipart "image")  per ref  -> { name, subfolder, type }
2. graph   = SubstituteTemplate(settings.WorkflowTemplateJson, prompt, dims, seed, checkpoint, ipadapter, name)
3. queue   POST {base}/prompt  { prompt: graph, client_id: <guid> } -> { prompt_id }
4. poll    GET {base}/history/{prompt_id}  every PollInterval until outputs[OutputNodeId].images present
5. fetch   GET {base}/view?filename=..&subfolder=..&type=output    -> image bytes
return Result.Success(new ImageGenerationResult(bytes, mime, "comfyui", checkpoint, new ImageUsage(1, bytes.Length)))
```

| Failure | Maps to (same as `OpenAiImageModelClient`) |
|---------|---------------------------------------------|
| Base URL unreachable / `HttpRequestException` / socket | `IntegrationErrors.ExternalProviderUnavailable` |
| Overall `Timeout` elapsed / `OperationCanceledException` (ct) | `IntegrationErrors.ProviderTimeout` |
| `/prompt` non-2xx, queue rejection, `node_errors` present | `IntegrationErrors.ExternalProviderUnavailable` |
| `/history` shows `status.status_str == "error"` | `IntegrationErrors.ExternalProviderUnavailable` |
| Output node has no `images[]` after timeout, or `/view` empty | `IntegrationErrors.AiJsonInvalid` |
| Template substitution produces invalid JSON | `IntegrationErrors.AiJsonInvalid` |
| Missing base URL / invalid settings | `IntegrationErrors.ProviderCredentialUnavailable` |
| Missing checkpoint/model | `IntegrationErrors.AiModelRequired` |

Because these are the EXACT codes the OpenAI adapter already returns, `IntegrationExecutionPolicy` and the
gateway treat a ComfyUI failure identically → the slide degrades to CSS, the job stays `Completed`.

### 3.5 Catalog parse (OQ-3)
```
GET {base}/object_info ->
  root["CheckpointLoaderSimple"]["input"]["required"]["ckpt_name"][0]  // string[] enum
    -> AiProviderModel(name, name, { source = "checkpoint" })
  (optional) ["IPAdapterModelLoader"]…["ipadapter_file"][0]            -> { source = "ipadapter" }
             ["LoraLoader"]…["lora_name"][0]                           -> { source = "lora" }
on unreachable/parse-fail -> Result.Success(settings.DefaultCheckpoints -> source="curated")  // never errors
```
Same `Result<IReadOnlyList<AiProviderModel>>` contract and curated-fallback resilience as
`ClaudePromptCliModelCatalogClient`.

---

## 4. Flow

```
generate-image (org routed to comfyui)
 └─ ImageGenerationGateway.GenerateAsync(req{ Prompt, ReferenceImages?, Width, Height })   [unchanged seam]
     └─ ExternalProviderClientFactory.Create<IImageModelClient>(comfyui-credential)
         └─ ComfyUiImageModelClient.GenerateAsync(req):
             settings.FromCredential ─► resolve checkpoint ─► resolve ref bytes (IGeneratedAssetStorage, cap N)
             POST /upload/image (per ref) ─► substitute template ─► POST /prompt(client_id)
             ─► poll /history/{id} (PollInterval, bounded by Timeout/ct) ─► GET /view ─► bytes
             on ANY failure ─► Result.Failure(IntegrationErrors.*)  ─►  gateway/policy degrade to CSS (job Completed)

DescribeAsync(org) ─► factory.Create ─► client.Capabilities = (true, MaxReferenceImages)   [no provider call]
model sync         ─► factory.Create<IAiModelCatalogClient> ─► ComfyUiModelCatalogClient.ListModelsAsync ─► /object_info
health check       ─► ComfyUiIntegrationHealthCheck ─► GET /system_stats ─► 200 = Connected
```

---

## 5. Degrade matrix (resilience — unchanged contract)

| Condition | Behaviour |
|-----------|-----------|
| ComfyUI base URL unreachable | `ExternalProviderUnavailable` → slide degrades to CSS; job `Completed` |
| Generation exceeds `Timeout` / cancelled | `ProviderTimeout` → degrade to CSS |
| `/prompt` queue/node error or history error | `ExternalProviderUnavailable` → degrade to CSS |
| Output node yields no image / `/view` empty | `AiJsonInvalid` → degrade to CSS |
| Reference handle fails `IGeneratedAssetStorage.OpenAsync` | Log + skip that reference; continue (descriptor path if none resolve) |
| No reference images / none resolve | Descriptor-prompt-only path (LoadImage/IP-Adapter bypassed) |
| Invalid `ComfyUiSettings` (bad base URL) | `ProviderCredentialUnavailable` → degrade to CSS |
| Missing checkpoint | `AiModelRequired` → degrade to CSS |
| `/object_info` unreachable during sync | Curated `DefaultCheckpoints` returned; "Sync models" succeeds |
| Org NOT routed to `comfyui` | Entire adapter unreferenced; existing image route unchanged |

ComfyUI is NEVER on the completion critical path — every failure is a per-slide degrade through the EXISTING
resilience path (verified `ImageGenerationGateway` + execution policy). No new resilience code.

---

## 6. Strict-hexagonal boundary mapping

| Concern | Layer | Type(s) |
|---------|-------|---------|
| Image port, request/result/capability, init/reference contract | **Application** (reused) | `IImageModelClient`, `IImageGenerationGateway`, `ImageGenerationRequest`, `ImageModelCapabilities`, `ImageInitReference` |
| Storage byte-open seam | **Application** (reused) | `IGeneratedAssetStorage` |
| ComfyUI HTTP calls, workflow JSON graph, template substitution, `/object_info` parse, error mapping, byte resolution | **Infrastructure** | `ComfyUiImageModelClient`, `ComfyUiModelCatalogClient` ONLY |
| Per-org config binding | **Infrastructure** | `ComfyUiSettings` |
| Connector seed / per-org seed / factory / health check | **Infrastructure** | catalog seed, `ComfyUiOrganizationSeeder`, factory arm, `ComfyUiIntegrationHealthCheck` |

Architecture-gate contract: no ComfyUI/`HttpClient`/JSON-graph type appears in any Application/Domain
signature; the port stays bytes-only. The existing `IImageGenerationGateway` port-contract test continues to
pin this — no new Application surface is introduced.

---

## 7. Data model + migration verdict
**No schema migration.** The `comfyui` catalog row is seeded through the EXISTING
`IntegrationProviderCatalogRepository.UpsertAsync` (additive `SeedDefinition`); per-org connection rows are
created by the seeder through the EXISTING `OrganizationIntegration.Create` path (same as claude-p). Settings
live in the EXISTING provider-credential bag (no new columns). Reference bytes use the EXISTING
`IGeneratedAssetStorage`. **Migration verdict: none — catalog seed + DI only.**

---

## 8. Decisions in depth

### OQ-1 — Templated API-format JSON graph + token substitution + declared output node
**Decision.** `ComfyUiSettings.WorkflowTemplateJson` holds the API-format graph with `{{TOKEN}}` placeholders;
the adapter substitutes scalars, `JsonDocument.Parse`-validates, and reads the result via `OutputNodeId`. A
bundled SD1.5 + IPAdapter default (§3.3) ships for when the credential template is empty.
**Rationale.** Keeps the entire graph as Infrastructure CONFIG — a model/graph swap is config, not code (the
proposal's direction #3); token substitution matches OQ-1 literally; a declared `OutputNodeId` decouples the
adapter from node numbering so alternative templates work without code change.
**Rejected.** A hardcoded C# graph builder (couples node topology to code — defeats config-swap). A full
structured node-graph object model in Infrastructure (over-engineered for v1; the proposal asks for placeholder
substitution). Storing the UI-format (non-API) graph (ComfyUI `/prompt` requires the API format).

### OQ-2 — Polling over websocket; OpenAI-identical error mapping
**Decision.** `upload → /prompt → poll /history → /view`, bounded by `Timeout` + `CancellationToken`; map every
failure onto the SAME `IntegrationErrors` codes `OpenAiImageModelClient` returns.
**Rationale.** Polling needs no persistent socket, is deterministic, and is trivially unit-testable with a fake
`HttpMessageHandler` returning a scripted sequence; identical error codes mean the existing execution-policy/
degrade/telemetry path needs ZERO change.
**Rejected.** Websocket `/ws` completion (stateful, harder to fake-test, no resilience benefit for a single
queued prompt). Inventing new ComfyUI-specific error codes (would force degrade-path changes downstream).

### OQ-3 — `/object_info` checkpoint enum → models; curated fallback
**Decision.** Parse `CheckpointLoaderSimple.ckpt_name` (and optionally IP-Adapter/LoRA loader enums) into the
model list; fall back to `ComfyUiSettings.DefaultCheckpoints` on any failure. Same contract as
`ClaudePromptCliModelCatalogClient`.
**Rationale.** The checkpoint enum IS the authoritative installed-model list; the curated fallback keeps the
model-sync UX working when the server is down — the exact resilience shape claude-p already uses.
**Rejected.** A hardcoded model list (drifts from what's actually installed). Failing the sync when the server
is unreachable (breaks the model-sync UX — claude-p deliberately avoids this).

### OQ-4 — `ComfyUiSettings` bound via `FromCredential`, mirroring claude-p
**Decision.** A `ComfyUiSettings` record with `FromCredential(ResolvedProviderCredential)` reading
`base_url`/`default_checkpoint`/`ipadapter_model`/`workflow_template_json`/`output_node_id`/`max_reference_
images`/`timeout_ms`/`poll_interval_ms`, with safe defaults and `ProviderKey = "comfyui"`.
**Rationale.** Identical binding shape to `ClaudePromptCliSettings` keeps per-org config + safe-default fallback
in one validated place; the workflow template as a credential is what lets prod retarget the SAME connector via
config with no code change.
**Rejected.** An `IOptions`-bound `appsettings` block (not per-org; ComfyUI config must vary by org like every
other connector). Hardcoded base URL/timeout (defeats prod retargeting).

### OQ-5 — `/upload/image` + filename reference; storage byte-resolution; capped
**Decision.** Upload each reference via `POST /upload/image` (multipart) and reference the returned filename in
`LoadImage`; resolve bytes from `IGeneratedAssetStorage.OpenAsync(refImage.Url)` inside the adapter, capped to
`Capabilities.MaxConditioningImages` (= `max_reference_images`).
**Rationale.** ComfyUI core `LoadImage` consumes a server-side filename, not base64; reusing the existing
storage-open seam (verified in `OpenAiImageModelClient`) keeps the Application layer byte-free; the cap bounds
the IP-Adapter input count.
**Rejected.** Inline base64 in the graph (no core ComfyUI node consumes it without a custom node — brittle
across installs). Hauling bytes in the Application layer (violates the byte-free boundary). Unbounded
references (open-ended; the default single-image IP-Adapter takes one — batch templates raise the cap via
config).

---

## 9. Test-first ordering (Strict TDD — `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj -c Release`)
No live ComfyUI server: drive the adapter with a fake `HttpMessageHandler` (scripted upload→prompt→history→
view sequence) + a fake `IGeneratedAssetStorage`, exactly as `OpenAiImageConditioningTests` does.
1. **`ComfyUiSettings.FromCredential`** — defaults (base URL, timeout, poll, cap); valid/invalid `base_url`;
   positive-int parsing; `ProviderKey`. (OQ-4.)
2. **Template substitution** — every `{{TOKEN}}` replaced; prompt JSON-escaped; output is valid JSON; invalid
   result → `AiJsonInvalid`; no-reference bypass path. (OQ-1.)
3. **Happy path** — fake handler scripts upload→`/prompt`→`/history`(pending then done)→`/view`; returns bytes,
   `ProviderKey="comfyui"`, model = resolved checkpoint, `SupportsImageConditioning = true`. (OQ-2/OQ-5.)
4. **Reference resolution** — bytes pulled from fake storage, capped to `MaxConditioningImages`; failed
   `OpenAsync` logged + skipped; zero resolved → descriptor path. (OQ-5.)
5. **Error mapping** — unreachable→`ExternalProviderUnavailable`; overall timeout/ct→`ProviderTimeout`; node
   error→`ExternalProviderUnavailable`; empty output→`AiJsonInvalid`; bad settings→`ProviderCredentialUnavailable`;
   missing checkpoint→`AiModelRequired`. (OQ-2.)
6. **`ComfyUiModelCatalogClient`** — parse `ckpt_name` enum from a fake `/object_info`; IP-Adapter/LoRA tags;
   unreachable → curated `DefaultCheckpoints`. (OQ-3.)
7. **Factory** — `Create<IImageModelClient>`/`Create<IAiModelCatalogClient>` for `"comfyui"` returns the
   ComfyUI types; unknown stays null.
8. **Seeder + health check** — per-org seed is idempotent (skips existing); `GET /system_stats` 200 → healthy,
   non-2xx/unreachable → unhealthy with code.
9. **Gates + ADR + docs/spec sync** — `pwsh -File scripts/gates/Run-Gates.ps1`.

---

## 10. Architecture-gate compliance
- **No provider types in Domain/Application.** All ComfyUI HTTP/graph/parse/byte-resolution code lives ONLY in
  `ComfyUiImageModelClient` + `ComfyUiModelCatalogClient` + `ComfyUiSettings`. The port stays bytes-only.
- **No hardcoded provider/model.** The image route resolves per org via `IWorkflowConfigurationResolver`
  (verified gateway); checkpoint/model come from the request/credential/settings, never pinned.
- **Additive + reversible.** New files + additive switch arms + one `SeedDefinition` + DI. Removing the seed +
  routing reverts the image route with no schema or behaviour change for other orgs.
- **Docs-sync (MUST):** `openspec/specs/*` deltas for `image-generation` and `integrations` authored in the
  spec phase.
- **ADR (MUST):** the new ComfyUI image provider + connector → an ADR under `docs/architecture/adr/` at apply
  time.
- Run `pwsh -File scripts/gates/Run-Gates.ps1` before completion; do not mark complete on any MUST failure.

## 11. Risks and mitigations (design-level)

| Risk | Severity | Mitigation |
|------|----------|------------|
| ComfyUI server unreachable/slow on the dev GPU | **Medium** | Configurable `Timeout` + cancellation; degrade to CSS (job never fails); health check surfaces connectivity |
| Workflow template brittle across model installs | **Medium** | Template + `OutputNodeId` configurable in `ComfyUiSettings`; `/object_info` validates available checkpoints; substitution validated via `JsonDocument.Parse` |
| Reference-delivery contract drift (upload vs node naming) | **Medium** | Upload returns the canonical filename; `LoadImage` references it; fake-handler tests pin the sequence |
| Polling never terminates on a stuck prompt | **Medium** | Overall `Timeout` budget + `CancellationToken` bound the poll loop → `ProviderTimeout` |
| ComfyUI/HTTP types leak into Application | **Low** | All concerns behind the adapter; port bytes-only; existing port-contract test |
| Routing change affects non-`comfyui` orgs | **Low** | Additive `SeedDefinition` + per-org config-gated routing; default behaviour unchanged when unset |
| Prod license non-compliance (Flux dev) | **Low** | Default to commercial-OK checkpoints (SD1.5/SDXL/Qwen/IP-Adapter); document the caveat |

## 12. Spec impact noticed (for `sdd-spec` / verify)
- **image-generation (modified):** a new `comfyui` `IImageModelClient` adapter with `SupportsImageConditioning =
  true`; IP-Adapter HTTP workflow (upload→`/prompt`→poll→`/view`); ComfyUI failure maps onto the existing CSS
  degrade path; capability surfaced through the existing `DescribeAsync`.
- **integrations (modified):** a new `comfyui` connector — catalog `SeedDefinition` (category `MediaGeneration`,
  `base_url` credential field, default-checkpoint models), per-org seeder, `ExternalProviderClientFactory`
  arms (`IImageModelClient` + `IAiModelCatalogClient`), HTTP health check, DI.

## 13. Sliced implementation order (single PR on `feat/claude-p-provider`) + next step
Seam order, settings → adapter → catalog → connector registration:
1. **Settings** — `ComfyUiSettings` + `FromCredential` + defaults + bundled default template constant.
2. **Adapter** — `ComfyUiImageModelClient`: reference byte-resolution (reuse the `OpenAiImageModelClient`
   pattern), template substitution, upload→`/prompt`→poll→`/view`, error mapping, `Capabilities`.
3. **Catalog** — `ComfyUiModelCatalogClient` `/object_info` parse + curated fallback.
4. **Connector registration** — `IntegrationCatalogSeeder` `SeedDefinition`, `ComfyUiOrganizationSeeder`
   (+extensions), `ExternalProviderClientFactory` arms, `ComfyUiIntegrationHealthCheck`, DI + startup seed.
5. **ADR + docs/spec sync + gates.**

`sdd-tasks` owns the checklist; this design owns the HOW. Next: `sdd-tasks` (after the spec is also ready).
