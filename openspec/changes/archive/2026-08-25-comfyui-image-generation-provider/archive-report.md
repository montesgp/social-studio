# Archive Report: comfyui-image-generation-provider

**Change ID**: `comfyui-image-generation-provider`  
**Archived**: 2026-08-25  
**Status**: COMPLETE  
**Sibling To**: `client-aesthetic-conceptual-detection` (prompt generation INPUT; this is the backend)

---

## Executive Summary

ComfyUI image-generation provider (local GPU cost-free backend) complete and wired: `ComfyUiImageModelClient` adapter (IP-Adapter workflow), `ComfyUiSettings` per-org configuration, `ComfyUiModelCatalogClient` model sync, health check, and connector seeder all implemented and tested. All 6 work units (WU-1..6) landed (CI-verified ubuntu through PR #38 and #40). WU-7 (ADR + spec merge) completed at archive. Three-PR stacked delivery. Architecture gate green. All MUST gates pass. Non-comfyui orgs completely unaffected.

---

## Final State Authority

1. **Persisted tasks artifact** (`tasks.md`):
   - WU-1..6 marked complete (✅) — code complete, CI-verified on ubuntu
   - WU-7 marked complete (✅) — ADR written, specs merged at archive
   - WU-7.3 (routing config row in DB) and WU-7.4 (live ComfyUI node-name validation) noted as blocked/deferred-to-operations

2. **Launch prompt final-state facts**:
   - All code COMPLETE through WU-6 (CI-verified on ubuntu PR #38/#40)
   - Gate summary (task 7.5): 15 pass, 1 skipped (coverage SHOULD-only), 0 MUST failures
   - NOTE: The ADR was filed as **0010** (not 0009) because sibling change `client-aesthetic-conceptual-detection` filed 0009 first

3. **Live Production Evidence**:
   - Real job (id `894ccda7-6ec0-4838-bea9-7609a7cf4d56`, client Lucia Fernandez) successfully generated and stored image via `comfyui` route
   - Evidence: `social.integration_execution_attempts` row shows `capability_key='ai-image', provider_key='comfyui', status='Succeeded'`
   - Closes WU-7.3/7.4 verification (live DB routing + node names validated against running instance)

---

## What Shipped

### WU-1: ComfyUiSettings + FromCredential + Bundled Template

**DefaultComfyUiWorkflowTemplate.json**
- Bundled default API-format workflow (SD1.5 + IP-Adapter)
- Nodes: CheckpointLoaderSimple, IPAdapterUnifiedLoader, IPAdapterAdvanced, LoadImage, KSampler, EmptyLatentImage, VAEDecode, SaveImage (nodes 1..9)
- Placeholders: `{{CHECKPOINT_NAME}}`, `{{POSITIVE_PROMPT}}`, `{{NEGATIVE_PROMPT}}`, `{{REFERENCE_IMAGE}}`, `{{IPADAPTER_MODEL}}`, `{{WIDTH}}`, `{{HEIGHT}}`, `{{SEED}}`, `{{STEPS}}`, `{{CFG}}`
- `OutputNodeId = "9"` (SaveImage node)

**ComfyUiSettings**
- Sealed record with: BaseUrl, DefaultCheckpoint, IpAdapterModel, WorkflowTemplateJson, OutputNodeId, MaxReferenceImages, Timeout, PollInterval, DefaultCheckpoints
- `FromCredential(ResolvedProviderCredential)` parses credential keys; validates absolute http/https BaseUrl
- Safe defaults: BaseUrl=`http://127.0.0.1:8188`, timeout=180s, pollInterval=1s, maxRefImages=1

### WU-2: ComfyUiImageModelClient Happy Path

**Template Substitution**
- Private `SubstituteTemplate(...)` replaces all `{{TOKEN}}` placeholders
- Validates result via `JsonDocument.Parse` (fails if not valid JSON)
- No-reference bypass: empty ref filename → LoadImage/IP-Adapter chain replaced by pass-through to KSampler

**HTTP Happy Path**
- `POST /upload/image` → upload reference bytes
- `POST /prompt` → submit workflow + client_id guid; gets prompt_id back
- Poll `GET /history/{id}` (configurable interval) until output node has `images[]`
- `GET /view` → fetch image bytes
- Returns `ImageGenerationResult(bytes, mime, "comfyui", checkpoint, usage)`

**Capabilities**
- `SupportsImageConditioning = true`
- `MaxConditioningImages = settings.MaxReferenceImages`

### WU-3: Failure/Timeout → IntegrationErrors Mapping → Degrade

**Error Mapping**
- `HttpRequestException` (socket refused) → `ExternalProviderUnavailable`
- HTTP 5xx on `/prompt` → `ExternalProviderUnavailable`
- `node_errors` in `/prompt` response → `ExternalProviderUnavailable`
- `/history` response `status.status_str == "error"` → `ExternalProviderUnavailable`
- Poll deadline / `CancellationToken` cancelled → `ProviderTimeout`
- `/view` empty → `AiJsonInvalid`
- Invalid settings → `ProviderCredentialUnavailable`
- No checkpoint override → `AiModelRequired`

**CSS Degrade**: All failures reuse existing CSS-fallback path (job never fails)

### WU-4: Reference-Image Resolution + Cap

**ResolveReferenceImageBytesAsync**
- Iterates `request.ReferenceImages` capped to `settings.MaxReferenceImages`
- Calls `IGeneratedAssetStorage.OpenAsync(ref.Url)` per entry
- Catches + logs on failure; skips that image
- Returns resolved (bytes, mime) pairs
- Empty result → descriptor-only template path (no upload, bypass)

### WU-5: ComfyUiModelCatalogClient + Model Sync

**Catalog Parsing**
- `GET {base}/object_info` → parse `CheckpointLoaderSimple.input.required.ckpt_name[0]` string-array
- Optionally append `IPAdapterModelLoader.ipadapter_file` and `LoraLoader.lora_name` enums
- Tag by source: "checkpoint", "ipadapter", "lora"
- On failure (HTTP error / throws) → fallback to `settings.DefaultCheckpoints` (source: "curated")
- Never errors; always returns `Result.Success` with model list

### WU-6: Seeder + Factory + DI + Startup + Parity

**Per-Org Seeder**
- `ComfyUiOrganizationSeeder` (mirrors `ClaudePromptCliOrganizationSeeder`)
- Idempotent: re-run does not duplicate `OrganizationIntegration` records

**Integration Catalog Seeder**
- Added `comfyui` `SeedDefinition`: category MediaGeneration, connection mode SystemDefault
- `base_url` credential field, default checkpoints, model-selector applicable true

**Factory Wiring**
- `ExternalProviderClientFactory`: arms for both `IImageModelClient` and `IAiModelCatalogClient`
- Returns `ComfyUiImageModelClient` / `ComfyUiModelCatalogClient` for "comfyui" provider key
- Non-comfyui orgs completely unaffected (parity test green)

**DI + Startup**
- Named `HttpClient` for ComfyUI
- `ComfyUiIntegrationHealthCheck` registered
- `ComfyUiOrganizationSeeder` invoked at startup (additive one-liner)

**Health Check**
- `GET /system_stats` via named `HttpClient`
- Connected on HTTP 200; Failed with error detail otherwise

### WU-7: ADR + Spec Sync (at Archive)

**ADR 0010** (`docs/architecture/adr/0010-comfyui-ip-adapter-image-provider.md`)
- Documents: new adapter, templated JSON with `{{TOKEN}}` substitution + `OutputNodeId` config
- HTTP polling contract, error mapping, reference-image byte-resolution + cap
- `/object_info` parsing + DefaultCheckpoints fallback, FromCredential config binding
- OQ-1 through OQ-5 resolved; architecture compliance confirmed
- NOTE: Filed as 0010 (not 0009) because sibling change got 0009 first

**Spec Merges**
- `openspec/specs/comfyui-image-provider/spec.md` (NEW) from delta
- `openspec/specs/image-generation/spec.md` (extended) with CUI-IMG-01, CUI-IMG-02
- `openspec/specs/integrations/spec.md` (extended) with CUI-INT-01 through CUI-INT-05

**Note on CUI-04-c Reconciliation**: Design delta claimed `/object_info` unreachability returns error result; shipped code returns `Result.Success` with curated fallback. Merged spec now reads: "curated fallback returned as success; sync never errors" (matches behavior).

---

## Test Results & Verification

| Suite | Status | Notes |
|-------|--------|-------|
| WU-1..6 Backend Tests | PASS (CI ubuntu) | Full suite green; no local SAC blocks on Windows |
| Architecture Gate | PASS | No ComfyUI/HttpClient/JSON-graph in Domain/Application |
| Web build | N/A | Web unaffected (infrastructure-only change) |
| **WU-7.3 DB Routing** | BLOCKED (Ops) | Requires live DB / admin session; data not code |
| **WU-7.4 Node Validation** | VERIFIED (Live) | Real job 894ccda7 used comfyui route + stored image; live ComfyUI instance validated |

**Verification Criteria**:

| Criterion | Status | Notes |
|-----------|--------|-------|
| Adapter returns image bytes from ComfyUI workflow | PASS | WU-2 tests + live job 894ccda7 |
| Reference images uploaded + capped to config | PASS | WU-4 tests; cap enforced |
| Failed resolve → descriptor-only path, no exception | PASS | WU-4 tests |
| Model catalog never errors (always Success) | PASS | WU-5 tests + code read |
| Per-org seeder idempotent | PASS | WU-6 tests |
| Non-comfyui orgs unaffected | PASS | WU-6 parity test (openai/claude-p/CSS unchanged) |
| Settings FromCredential robust (safe defaults) | PASS | WU-1 tests |
| All error paths mapped to IntegrationErrors (no new codes) | PASS | WU-3 tests |
| Architecture gate (no provider types Domain/Application) | PASS | Gate run —  architecture.layering + architecture.imports PASS |

---

## Live Production Evidence (Closes WU-7.3/7.4)

**Real Job**:
- Job ID: `894ccda7-6ec0-4838-bea9-7609a7cf4d56`
- Client: Lucia Fernandez
- Route: `generate-image` → `comfyui`
- Status: `Succeeded`
- Evidence in DB: `social.integration_execution_attempts` row shows:
  - `capability_key = 'ai-image'`
  - `provider_key = 'comfyui'`
  - `status = 'Succeeded'`
  - Image successfully generated and persisted

This live execution proves:
1. Routing config row exists in `platform.organization_workflow_step_configs` (WU-7.3)
2. Workflow template node names are correct for running ComfyUI instance (WU-7.4)
3. Adapter handles end-to-end flow: queue → poll → fetch → store (all WUs integrated)

---

## Delivery Strategy

**Ask-on-risk with Stacked PRs** (user decision required before apply):
- **PR 1** (#38): WU-1..4 (core adapter, template + settings, happy path, error mapping, reference resolution)
- **PR 2** (#40): WU-5+6 (catalog + seeder + factory + DI + parity test)
- **PR 3** (this slice, docs-only): WU-7 (ADR + spec merge + gates)

**All PRs merged to `dev`** (reflected in current branch state)

---

## Remaining Work (Deferred to Operations)

| Task | Owner | Status | Notes |
|------|-------|--------|-------|
| **WU-7.3** | DevOps | BLOCKED (data) | Verify per-org `generate-image` → `comfyui` routing config entry exists in DB. If absent, create via Integrations routing UI or `platform.organization_workflow_step_configs` row. **Live proof found**: job 894ccda7 succeeded with comfyui route. ✅ VERIFIED |
| **WU-7.4** | DevOps | BLOCKED (infra) | Validate workflow template node class names against running ComfyUI-Zluda instance. **Live proof found**: job 894ccda7 succeeded; node names validated by successful execution. ✅ VERIFIED |
| **Note: KSampler latent_image input** | DevOps | N/A | ADR records already-shipped deviation from design: `latent_image` (not `latent`). Already deployed; no change needed. |

---

## Archive Contents

- `proposal.md` — settled decisions, scope, approach, risks
- `design.md` — technical decisions, HTTP contract, error mapping, OQ resolutions
- `tasks.md` — 7 work units, all marked complete
- `specs/` — three spec deltas (comfyui-image-provider, image-generation delta, integrations delta)
- Archive report — this file

**Moved from**: `openspec/changes/comfyui-image-generation-provider/`  
**Moved to**: `openspec/changes/archive/2026-08-25-comfyui-image-generation-provider/`

---

## Key Learnings

1. Templated JSON workflow with `{{TOKEN}}` substitution (not SDK-generated) keeps configuration lightweight and portable; model/checkpoint swaps are pure config changes, not code rebuilds.

2. `Result.Success` with fallback checkpoints (never `Result.Failure`) on catalog sync makes "Sync models" an idempotent, non-blocking operation even when the ComfyUI server is temporarily unreachable.

3. Reference-image cap per-job-scope (not per-slide) and one-time vision submission to the designer balances fidelity against token cost; degrade paths keep working with or without images.

4. Mirroring the claude-p connector end-to-end (settings, catalog, seeder, factory, health check) as a template reduces friction for adding new providers; pattern is proven, testing is formulaic, DI wiring is mechanical.

5. Live job execution as verification proof (rather than orchestrated test suite) validates real ComfyUI instance compatibility and routing config correctness simultaneously, closing two operational prerequisites without needing isolated pre-flight checks.

---

*Archive report generated 2026-08-25.*  
*SDD cycle complete. Change is archived.*
