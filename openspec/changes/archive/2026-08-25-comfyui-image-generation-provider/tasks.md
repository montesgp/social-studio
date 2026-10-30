# Tasks: comfyui-image-generation-provider

**Branch:** `feat/claude-p-provider` (ask-on-risk delivery strategy; PR-split decision required before apply — see Forecast).
**TDD mode:** STRICT — `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj -c Release`. Every behavior task: RED → GREEN.
**WDAC signing note:** Build first, sign the 4 Incoders DLLs (thumbprint `162D6D5A0F56EDA79363B7EB475946778577042F`), then `dotnet test --no-build`.

---

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1,200–1,450 |
| 400-line budget risk | **High** |
| Chained PRs recommended | **Yes** — ask-on-risk; user decision required before apply |
| Suggested split | PR 1 (WU-1..WU-4, core adapter) → PR 2 (WU-5+WU-6, catalog+wiring) → PR 3 (WU-7, ADR+gates) |
| Delivery strategy | `ask-on-risk` |
| Chain strategy | pending (user decision required) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| WU | Goal | Likely PR | Notes |
|----|------|-----------|-------|
| WU-1 | `ComfyUiSettings` + `FromCredential` + defaults + bundled SD1.5/IP-Adapter template resource | PR 1 | Foundation; all downstream WUs depend on settings types |
| WU-2 | `ComfyUiImageModelClient` happy path (template substitution → upload → `/prompt` → poll `/history` → `/view` → bytes; `SupportsImageConditioning=true`) | PR 1 | Core adapter; depends on WU-1 |
| WU-3 | Failure/timeout → `IntegrationErrors` mapping → CSS-degrade resilience (same codes as `OpenAiImageModelClient`) | PR 1 | Error branches of WU-2; same file |
| WU-4 | Reference-image resolution from `IGeneratedAssetStorage` + cap; failed resolve → skip+log; zero-resolve → descriptor path | PR 1 | Conditioning branch of WU-2; same file |
| WU-5 | `ComfyUiModelCatalogClient`: `/object_info` checkpoint-enum parse + optional IP-Adapter/LoRA tags + curated `DefaultCheckpoints` fallback | PR 2 | Catalog sync; independent from WU-2..4 |
| WU-6 | Connector seed + `IntegrationCatalogSeeder` `SeedDefinition` + `ExternalProviderClientFactory` arms + `ComfyUiIntegrationHealthCheck` + DI + startup + non-comfyui parity test | PR 2 | Integration wiring; depends on WU-1..WU-5 |
| WU-7 | ADR + spec merges (new `comfyui-image-provider` spec + image-generation + integrations deltas) + routing data validation + node class name validation + gates | PR 3 | MUST gate; `require_adr_for_architecture_changes` |

---

## WU-1 — `ComfyUiSettings` + `FromCredential` + Bundled Default Template

*Refs: CUI-03, CUI-03-a, CUI-03-b; design §3.1, §0 OQ-4.*

### Phase 1.A — Bundled default workflow template resource

- [x] 1.1 **[GREEN]** Add `DefaultComfyUiWorkflowTemplate.json` — API-format SD1.5 + IP-Adapter graph (nodes 1..9 + `vae`) with `{{CHECKPOINT_NAME}}`, `{{POSITIVE_PROMPT}}`, `{{NEGATIVE_PROMPT}}`, `{{REFERENCE_IMAGE}}`, `{{IPADAPTER_MODEL}}`, `{{WIDTH}}`, `{{HEIGHT}}`, `{{SEED}}`, `{{STEPS}}`, `{{CFG}}` placeholders per design §3.3; `OutputNodeId = "9"` (SaveImage node). Mark as `EmbeddedResource` in Infrastructure `.csproj`.
  (`SocialStudio.Infrastructure/Ai/Providers/DefaultComfyUiWorkflowTemplate.json`)

### Phase 1.B — `ComfyUiSettings` record + `FromCredential` validation

- [x] 1.2 **[RED]** Add `ComfyUiSettingsTests.cs` — assert:
  - `FromCredential` with `base_url` credential key → settings parsed; `ProviderKey == "comfyui"` (CUI-03-a)
  - Missing `base_url` → defaults to `http://127.0.0.1:8188` (CUI-03-b)
  - Invalid/relative `base_url` → `Result.Failure(IntegrationErrors.ProviderCredentialUnavailable)`
  - `timeout_ms`/`poll_interval_ms` parsed as positive ints; absent → 180 000 ms / 1 000 ms defaults
  - `max_reference_images` absent → defaults to `1`
  - `workflow_template_json` absent/empty → `WorkflowTemplateJson` is null (signals bundled default)
  (`SocialStudio.Tests/Infrastructure/Ai/ComfyUiSettingsTests.cs`)

- [x] 1.3 **[GREEN]** Create `ComfyUiSettings.cs` — sealed record with all fields from design §3.1 (`BaseUrl`, `DefaultCheckpoint`, `IpAdapterModel`, `WorkflowTemplateJson`, `OutputNodeId`, `MaxReferenceImages`, `Timeout`, `PollInterval`, `DefaultCheckpoints`); `const string ProviderKey = "comfyui"`; `static Result<ComfyUiSettings> FromCredential(ResolvedProviderCredential)` reads credential keys with safe defaults; validates base URL is absolute http/https.
  (`SocialStudio.Infrastructure/Ai/Providers/ComfyUiSettings.cs`)

- [x] 1.4 Run `dotnet test -c Release` — WU-1 tests pass; existing suite green.

---

## WU-2 — `ComfyUiImageModelClient` Happy Path

*Refs: CUI-01-a, CUI-01-b, CUI-01-c, CUI-IMG-01; design §3.2, §3.3, §3.4, §0 OQ-1/OQ-2.*

### Phase 2.A — Template substitution

- [x] 2.1 **[RED]** Add `ComfyUiTemplateSubstitutionTests.cs` — assert:
  - All `{{TOKEN}}` placeholders replaced (CUI-01-c shape); output passes `JsonDocument.Parse` without exception
  - Prompt with JSON-special chars (`"`, `\`, newline) is correctly JSON-escaped before substitution
  - Template producing invalid JSON after substitution → `Result.Failure(IntegrationErrors.AiJsonInvalid)`
  - No-reference bypass: empty reference filename → `LoadImage`/IP-Adapter chain replaced by pass-through model edge on KSampler; result still valid JSON (design §3.3 bypass)
  (`SocialStudio.Tests/Infrastructure/Ai/ComfyUiTemplateSubstitutionTests.cs`)

- [x] 2.2 **[GREEN]** Create `ComfyUiImageModelClient.cs` (stub + `SubstituteTemplate` only) — `private string SubstituteTemplate(...)` performs `string.Replace` for each `{{TOKEN}}`; reads bundled default via `Assembly.GetManifestResourceStream` when `WorkflowTemplateJson` is null/empty; applies no-reference bypass path; validates result via `JsonDocument.Parse`.
  (`SocialStudio.Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs`)

### Phase 2.B — HTTP happy-path sequence + capabilities

- [x] 2.3 **[RED]** Add `ComfyUiHappyPathTests.cs` — assert with fake `HttpMessageHandler` scripted as:
  `POST /upload/image` → `{name, subfolder, type}`; `POST /prompt` → `{prompt_id}`; `GET /history/{id}` (pending then done with `images[]` under `OutputNodeId`); `GET /view` → fixture bytes:
  - `GenerateAsync` returns `Result.Success` with fixture bytes; `ProviderKey = "comfyui"`; resolved checkpoint in result (CUI-01-a)
  - `Capabilities.SupportsImageConditioning = true`; `MaxConditioningImages = settings.MaxReferenceImages` (CUI-IMG-01)
  - No-reference path: handler receives no `/upload/image` call; returns bytes via text-only variant (CUI-01-b)
  (`SocialStudio.Tests/Infrastructure/Ai/ComfyUiHappyPathTests.cs`)

- [x] 2.4 **[GREEN]** Implement `ComfyUiImageModelClient.GenerateAsync` — `FromCredential` → resolve checkpoint (`PreferredModel ?? SelectedModel ?? DefaultCheckpoint`) → `ResolveReferenceImageBytesAsync` → upload → `SubstituteTemplate` → `POST /prompt` (with `client_id` guid) → poll `GET /history/{prompt_id}` every `PollInterval` until `OutputNodeId` has `images[]` (bounded by `Timeout`/`CancellationToken`) → `GET /view` → `Result.Success(ImageGenerationResult(bytes, mime, "comfyui", checkpoint, usage))`. `Capabilities` property: `new ImageModelCapabilities(SupportsImageConditioning: true, MaxConditioningImages: settings.MaxReferenceImages)`.
  (`SocialStudio.Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs`)

- [x] 2.5 Run `dotnet test -c Release` — WU-2 tests pass; existing suite green.

---

## WU-3 — Failure/Timeout → `IntegrationErrors` Mapping → CSS-Degrade Resilience

*Refs: CUI-02-a, CUI-02-b, CUI-02-c, CUI-06-b; design §3.4 error table, §5.*

### Phase 3.A — Error path tests + implementation

- [x] 3.1 **[RED]** Add `ComfyUiErrorMappingTests.cs` — assert all rows of the design §3.4 error table:
  - Fake handler throws `HttpRequestException` (socket refused) → `ExternalProviderUnavailable` (CUI-02-a)
  - Fake handler returns HTTP 500 on `/prompt` → `ExternalProviderUnavailable` (CUI-02-b)
  - Fake handler returns `node_errors` present in `/prompt` response → `ExternalProviderUnavailable`
  - Fake `/history` response has `status.status_str == "error"` → `ExternalProviderUnavailable`
  - Poll deadline elapsed / `CancellationToken` cancelled → `ProviderTimeout` (CUI-02-c)
  - `/view` returns empty body → `AiJsonInvalid`
  - Invalid settings from `FromCredential` → `ProviderCredentialUnavailable`
  - All checkpoint override paths are null → `AiModelRequired`
  - None of the above paths propagates an unhandled exception (CUI-02-a blanket assertion)
  (`SocialStudio.Tests/Infrastructure/Ai/ComfyUiErrorMappingTests.cs`)

- [x] 3.2 **[GREEN]** Extend `ComfyUiImageModelClient.GenerateAsync` — wrap every HTTP call + poll loop in try/catch; map each failure to exact `IntegrationErrors` code per design §3.4; `OperationCanceledException`/elapsed timeout → `ProviderTimeout`; never propagate. Zero new `IntegrationErrors` codes introduced.
  (`SocialStudio.Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs`)

- [x] 3.3 Run `dotnet test -c Release` — WU-3 tests pass; existing suite green.

---

## WU-4 — Reference-Image Resolution from `IGeneratedAssetStorage` + Cap

*Refs: CUI-01-a, CUI-01-b, CUI-06-a; design §0 OQ-5, §3.4 steps 0–1.*

### Phase 4.A — Reference byte-resolution tests + implementation

- [x] 4.1 **[RED]** Add `ComfyUiReferenceResolutionTests.cs` — assert with fake `IGeneratedAssetStorage`:
  - Bytes returned for each ref URL → `POST /upload/image` called per ref; returned filename substituted as `{{REFERENCE_IMAGE}}` in template (CUI-06-a)
  - Three refs, `MaxReferenceImages = 1` → only first ref uploaded; cap enforced (OQ-5)
  - One ref with `OpenAsync` throwing → logged + skipped; remaining refs proceed without error
  - All refs fail → zero resolved; descriptor-only path executed (no `/upload/image` call); generation continues (CUI-01-b)
  (`SocialStudio.Tests/Infrastructure/Ai/ComfyUiReferenceResolutionTests.cs`)

- [x] 4.2 **[GREEN]** Add `ResolveReferenceImageBytesAsync` private method to `ComfyUiImageModelClient` — iterates `request.ReferenceImages` capped to `settings.MaxReferenceImages`; calls `IGeneratedAssetStorage.OpenAsync(ref.Url)` per entry; catches + logs on failure and skips; returns resolved (bytes, mime) pairs; empty result → descriptor-only template path (no upload, bypass substitution).
  (`SocialStudio.Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs`)

- [x] 4.3 Run `dotnet test -c Release` — WU-4 tests pass; existing suite green.

---

## WU-5 — `ComfyUiModelCatalogClient`: `/object_info` Parsing + Curated Fallback

*Refs: CUI-04-a, CUI-04-b, CUI-04-c, CUI-INT-02 (model sync); design §3.5, §0 OQ-3.*

### Phase 5.A — Catalog client tests + implementation

- [x] 5.1 **[RED]** Add `ComfyUiModelCatalogClientTests.cs` — assert with fake `HttpMessageHandler`:
  - `/object_info` JSON with `CheckpointLoaderSimple.input.required.ckpt_name[0]` string-array → `Result.Success` list of `AiProviderModel` entries; `metadata.source = "checkpoint"` (CUI-04-a; CUI-06-c)
  - `/object_info` JSON also has `IPAdapterModelLoader.input.required.ipadapter_file[0]` → entries tagged `source = "ipadapter"` appended
  - `ckpt_name[0]` is empty array → empty list returned; no error (CUI-04-b)
  - Handler returns HTTP error / throws → `Result.Success` with `settings.DefaultCheckpoints` as `source = "curated"` entries; no exception (CUI-04-c)
  - No provider-specific type in returned `AiProviderModel` list
  (`SocialStudio.Tests/Infrastructure/Ai/ComfyUiModelCatalogClientTests.cs`)

- [x] 5.2 **[GREEN]** Create `ComfyUiModelCatalogClient : IAiModelCatalogClient` — `ListModelsAsync`: `GET {base}/object_info`; parses `CheckpointLoaderSimple.input.required.ckpt_name[0]`; optionally appends `IPAdapterModelLoader.ipadapter_file` and `LoraLoader.lora_name` enums tagged by source; on any failure returns `Result.Success(settings.DefaultCheckpoints → AiProviderModel(source="curated"))`; same `Result<IReadOnlyList<AiProviderModel>>` contract as `ClaudePromptCliModelCatalogClient`.
  (`SocialStudio.Infrastructure/Ai/Providers/ComfyUiModelCatalogClient.cs`)

- [x] 5.3 Run `dotnet test -c Release` — WU-5 tests pass; existing suite green.

---

## WU-6 — Connector Seeder + Catalog Seed + Factory + Health Check + DI + Non-comfyui Parity

*Refs: CUI-INT-01..05, CUI-IMG-02; design §2.2, §13.*

### Phase 6.A — Factory resolution tests

- [x] 6.1 **[RED]** Add `ComfyUiProviderFactoryTests.cs` — assert:
  - `ExternalProviderClientFactory.Create<IImageModelClient>(comfyui-credential)` returns `ComfyUiImageModelClient` (CUI-INT-04-a)
  - `Create<IAiModelCatalogClient>(comfyui-credential)` returns `ComfyUiModelCatalogClient`
  - `Create<IImageModelClient>(openai-credential)` does NOT return `ComfyUiImageModelClient` (CUI-INT-04-b)
  (`SocialStudio.Tests/Infrastructure/Integrations/ComfyUiProviderFactoryTests.cs`)

### Phase 6.B — Health check tests + implementation

- [x] 6.2 **[RED]** Add `ComfyUiIntegrationHealthCheckTests.cs` — assert:
  - Fake handler returns HTTP 200 on `GET /system_stats` → reports `Connected` (CUI-INT-03-a)
  - Fake handler returns non-2xx / throws → reports `Failed` with connectivity detail (CUI-INT-03-b)
  (`SocialStudio.Tests/Infrastructure/Integrations/ComfyUiIntegrationHealthCheckTests.cs`)

- [x] 6.3 **[GREEN]** Create `ComfyUiIntegrationHealthCheck : IExternalIntegrationHealthCheck` — `GET {baseUrl}/system_stats` via named `HttpClient`; `Connected` on HTTP 200, `Failed` with error detail otherwise; mirrors `ClaudePromptCliIntegrationHealthCheck`.
  (`SocialStudio.Infrastructure/Integrations/HealthChecks/ComfyUiIntegrationHealthCheck.cs`)

### Phase 6.C — Per-org seeder + catalog seed

- [x] 6.4 **[RED]** Add `ComfyUiOrganizationSeederTests.cs` — assert:
  - Configured org → `OrganizationIntegration` with `ProviderKey = "comfyui"` created (CUI-INT-02-a)
  - Re-run on same org is idempotent (no duplicate record)
  - Org not in config → no `comfyui` integration record created (CUI-INT-02-b)
  (`SocialStudio.Tests/Infrastructure/Integrations/ComfyUiOrganizationSeederTests.cs`)

- [x] 6.5 **[GREEN]** Create `ComfyUiOrganizationSeeder.cs` + `ComfyUiOrganizationSeederExtensions.cs` — mirrors `ClaudePromptCliOrganizationSeeder`; idempotent upsert via `OrganizationIntegration.Create`; extension method `SeedComfyUiOrganizationIntegrationsAsync`.
  (`SocialStudio.Infrastructure/Integrations/ComfyUiOrganizationSeeder.cs`, `SocialStudio.Infrastructure/Integrations/ComfyUiOrganizationSeederExtensions.cs`)

- [x] 6.6 **[GREEN]** Modify `IntegrationCatalogSeeder.cs` — add `comfyui` `SeedDefinition`: category `MediaGeneration`, connection mode `SystemDefault`, `base_url` credential field, default checkpoints from `ComfyUiSettings.DefaultCheckpoints`, model-selector applicable `true` (CUI-INT-01).
  (`SocialStudio.Infrastructure/Integrations/IntegrationCatalogSeeder.cs`)

### Phase 6.D — Factory wiring + DI + startup

- [x] 6.7 **[GREEN]** Modify `ExternalProviderClientFactory.cs` — add `"comfyui"` arms to both `IImageModelClient` and `IAiModelCatalogClient` switches; instantiate via `ActivatorUtilities.CreateInstance` (adapter type NOT registered in DI, same pattern as `OpenAiImageModelClient`).
  (`SocialStudio.Infrastructure/Integrations/ExternalProviderClientFactory.cs`)

- [x] 6.8 **[GREEN]** Modify `DependencyInjection.cs` — register named `HttpClient` for ComfyUI; register `ComfyUiIntegrationHealthCheck`; register `ComfyUiOrganizationSeeder` (additive only; `IGeneratedAssetStorage` already wired).
  (`SocialStudio.Infrastructure/DependencyInjection/DependencyInjection.cs`)

- [x] 6.9 **[GREEN]** Modify `Program.cs` — invoke `SeedComfyUiOrganizationIntegrationsAsync` at startup alongside the claude-p seed call (additive one-liner).
  (`SocialStudio.ApiService/Program.cs`)

### Phase 6.E — Non-comfyui parity + regression test

- [x] 6.10 **[RED]** Add `ComfyUiNonRoutedOrgParityTests.cs` — assert:
  - Org routed to `openai`: `Create<IImageModelClient>(openai-credential)` returns `OpenAiImageModelClient`; `ComfyUiImageModelClient` never constructed (CUI-IMG-02-a; CUI-INT-05-a)
  - Org with no image route: CSS-only path unchanged; factory not called for image generation
  - All existing provider keys resolve correctly after `"comfyui"` arm addition
  (`SocialStudio.Tests/Infrastructure/Integrations/ComfyUiNonRoutedOrgParityTests.cs`)

- [x] 6.11 Run `dotnet test -c Release` — WU-6 tests pass; full suite green including parity assertions.

---

## WU-7 — ADR + Spec Sync + Routing Data Validation + Node Class Validation + Gates

*Refs: CUI-05, CUI-IMG-02; design §10, §11; `openspec/config.yaml` `require_adr_for_architecture_changes: true`.*

- [x] 7.1 **[GREEN]** Author `docs/architecture/adr/0010-comfyui-ip-adapter-image-provider.md` — document:
  - New `comfyui` Infrastructure adapter: `ComfyUiImageModelClient`, `ComfyUiSettings`, `ComfyUiModelCatalogClient`
  - OQ-1: templated API-format JSON with `{{TOKEN}}` substitution + `OutputNodeId` (graph/model swap via config, zero code change)
  - OQ-2: polling HTTP contract (`upload → /prompt → poll /history → /view`); OpenAI-identical `IntegrationErrors` codes; degrade path unchanged
  - OQ-3: `/object_info` checkpoint-enum → model list; `DefaultCheckpoints` curated fallback; "Sync models" never errors
  - OQ-4: `FromCredential` binding (same shape as `ClaudePromptCliSettings`; per-org config, workflow template as credential)
  - OQ-5: `IGeneratedAssetStorage` byte-resolution + cap; Application layer stays byte-free
  - Architecture gate compliance: no ComfyUI/`HttpClient`/JSON-graph type in Domain or Application
  - NOTE: filed as ADR **0010**, not 0009 — `docs/architecture/adr/0009-pairs-contract-migration-conceptual-method-vision-routing.md` was authored by the sibling `client-aesthetic-conceptual-detection` change and merged to `dev` first, so 0009 was already taken by the time this slice ran.
  (`docs/architecture/adr/0010-comfyui-ip-adapter-image-provider.md`)

- [x] 7.2 **[GREEN]** Merge spec deltas into living specs:
  - Create `openspec/specs/comfyui-image-provider/spec.md` from `openspec/changes/comfyui-image-generation-provider/specs/comfyui-image-provider/spec-delta.md` (new spec — no prior file)
  - Append image-generation delta → `openspec/specs/image-generation/spec.md` (CUI-IMG-01, CUI-IMG-02)
  - Append integrations delta → `openspec/specs/integrations/spec.md` (CUI-INT-01..CUI-INT-05)
  - RECONCILIATION: scenario CUI-04-c wording was corrected during merge — the delta draft said "an error result is returned" for `/object_info` unreachability, but the shipped `ComfyUiModelCatalogClient` (WU-5, verified on this branch) returns `Result.Success` with the curated `DefaultCheckpoints` fallback on ANY failure and never returns `Result.Failure`. The merged living spec now reads "curated fallback returned as a success; sync never errors" to match shipped behavior.

- [ ] 7.3 **[DATA]** (blocked: requires live DB / admin session — see `docs/architecture/adr/0010-comfyui-ip-adapter-image-provider.md` §Operational prerequisites #1) Verify the per-org `generate-image` → `comfyui` ROUTING config entry exists in the database for the target org; create it via the Integrations routing UI or the `platform.organization_workflow_step_configs` row shape documented in the ADR if absent. This is data, not code — must be confirmed before any end-to-end smoke test. (Design apply-time risk a.)

- [ ] 7.4 **[VALIDATE]** (blocked: requires a running ComfyUI-Zluda instance — see `docs/architecture/adr/0010-comfyui-ip-adapter-image-provider.md` §Operational prerequisites #2) Validate default workflow template node class names (`CheckpointLoaderSimple`, `IPAdapterUnifiedLoader`, `IPAdapterAdvanced`, `LoadImage`, `KSampler`, `EmptyLatentImage`, `VAEDecode`, `SaveImage`) against the actual ComfyUI-Zluda install's `/object_info` response. Update `DefaultComfyUiWorkflowTemplate.json` if any class name differs from the installed nodes. (Design apply-time risk b.) NOTE: the ADR already records one already-shipped deviation from `design.md` §3.3 — `KSampler` input `latent_image` (not `latent`) — this remains to be validated against the live install for the OTHER node names.

- [x] 7.5 Run `pwsh -File scripts/gates/Run-Gates.ps1 -Skip tests`; report gate summary. Do NOT mark complete on any MUST gate failure. (`tests` gate skipped: Windows Smart App Control blocks local `dotnet test` for this repo; backend tests were CI-verified on ubuntu through PR 2 (WU-1..WU-6); this slice is docs/spec-only and changes zero application code.) Result: **PASS — 15 pass, 1 skipped (coverage, SHOULD-only), 0 MUST failures.**

---

## Parallelism Notes

| Parallelism | WUs | Constraint |
|-------------|-----|-----------|
| Sequential | WU-1 → WU-2 | Settings types are the compile gate for the adapter |
| Sequential within adapter | WU-2 → WU-3 → WU-4 | Same file; error paths and reference resolution layer onto happy-path skeleton |
| Parallel with WU-2..4 | WU-5 | Separate class; depends only on WU-1 settings types |
| Sequential | WU-2 + WU-5 → WU-6 | Factory and DI wiring require both adapter and catalog types to exist |
| Sequential | All prior → WU-7 | ADR + gate run requires complete implementation |

---

## Definition of Done

- [ ] WU-1..WU-7 tasks completed and committed in seam order (test + implementation in the same commit per behavior). (blocked: WU-7.3/7.4 remain open — environment-dependent, see below)
- [ ] `dotnet test -c Release` exits 0 across the full suite. (WDAC flow: build → sign 4 Incoders DLLs thumbprint `162D6D5A0F56EDA79363B7EB475946778577042F` → `dotnet test --no-build`.) (blocked: Windows Smart App Control blocks local `dotnet test` in this environment; CI-verified on ubuntu for the WU-1..WU-6 commit range through PR 1 (#38) and PR 2 (#40) merges, but not re-run in this docs-only slice)
- [x] Non-comfyui org parity test confirms: orgs routed to `openai`, `claude-p`, or CSS-only are completely unaffected (CUI-IMG-02, CUI-INT-05). CI-verified — `ComfyUiNonRoutedOrgParityTests` (WU-6, PR 2 #40).
- [x] Architecture gate confirms: no ComfyUI/`HttpClient`/JSON-graph type in Domain or Application projects (CUI-05); `TreatWarningsAsErrors=true` — zero `!` suppressors introduced by this change. Verified this slice: `pwsh -File scripts/gates/Run-Gates.ps1 -Skip tests` → `architecture.layering` PASS, `architecture.imports` PASS.
- [x] All error paths return `Result.Failure(IntegrationErrors.*)` — no unhandled exception propagated from `ComfyUiImageModelClient` (CUI-02); no new `IntegrationErrors` codes introduced. CI-verified — `ComfyUiErrorMappingTests` (WU-3, PR 1 #38).
- [x] Reference byte-resolution cap enforced (`MaxConditioningImages`); failed `OpenAsync` logged + skipped; zero-resolve → descriptor-only path with no exception (OQ-5). CI-verified — `ComfyUiReferenceResolutionTests` (WU-4, PR 1 #38).
- [x] `/object_info` catalog unreachable → `DefaultCheckpoints` fallback returned; "Sync models" never errors (CUI-04-c). CI-verified — `ComfyUiModelCatalogClientTests` (WU-5, PR 2 #40); re-confirmed by direct code read of `ComfyUiModelCatalogClient.ListModelsAsync` this slice (always `Result.Success`, never `Result.Failure`).
- [x] Per-org seeder is idempotent; re-run does not duplicate `OrganizationIntegration` records (CUI-INT-02). CI-verified — `ComfyUiOrganizationSeederTests` (WU-6, PR 2 #40).
- [ ] Default workflow template node class names validated against the target ComfyUI-Zluda install (task 7.4 complete). (blocked: requires a running ComfyUI-Zluda instance — see ADR 0010 §Operational prerequisites #2)
- [ ] Per-org `generate-image` → `comfyui` routing config entry confirmed present in data (task 7.3 complete). (blocked: requires live DB / admin session — see ADR 0010 §Operational prerequisites #1)
- [x] ADR `0010-comfyui-ip-adapter-image-provider.md` authored under `docs/architecture/adr/`. (Filed as 0010, not 0009 — see note on task 7.1.)
- [x] Spec deltas merged into `openspec/specs/` living files (`comfyui-image-provider` created; `image-generation` + `integrations` appended).
- [x] `pwsh -File scripts/gates/Run-Gates.ps1` exits 0; no MUST gate failure. (Run with `-Skip tests`; see gate summary on task 7.5. `dotnet test` gate itself remains blocked locally by SAC — see the `dotnet test` DoD line above.)
- [x] PR-split decision recorded per `ask-on-risk` delivery strategy — executed as the 3-PR chain (PR 1: WU-1..4 core adapter, `#38`; PR 2: WU-5+WU-6 catalog+wiring, `#40`; PR 3: WU-7 ADR+gates, this slice) on `stacked-to-main`, evidenced by both prior PRs already merged to `dev`.
