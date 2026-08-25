# Delta: Integrations — comfyui Connector Definition, Seeder, Health Check, and Factory Wiring

<!-- Change: comfyui-image-generation-provider -->
<!-- Applies to: openspec/specs/integrations/spec.md (additive delta) -->

## Purpose

Register `comfyui` as a new `MediaGeneration` integration provider. This covers the
catalog seed, per-org organization seeder, capability registration, health check, and
provider-factory wiring — mirroring the `claude-p` connector pattern. Existing providers
and all non-`comfyui` organizations are unaffected.

---

## PRD Traceability

| Requirement ID | Proposal Section |
|---|---|
| CUI-INT-01 | §Scope — `IntegrationCatalogSeeder`: comfyui connector + capability registration |
| CUI-INT-02 | §Scope — per-org seeder (`ComfyUiOrganizationSeeder`); model-sync applicable |
| CUI-INT-03 | §Scope — `ComfyUiIntegrationHealthCheck`; connectivity surfaced to UI |
| CUI-INT-04 | §Scope — `ExternalProviderClientFactory` resolves `comfyui` to `ComfyUiImageModelClient` |
| CUI-INT-05 | §Rollback Plan / §Scope Boundaries — additive; non-comfyui orgs see zero change |

---

## ADDED Requirements

### Requirement: CUI-INT-01 — comfyui Seeded in IntegrationCatalogSeeder as MediaGeneration Provider; Model-Sync Applicable

`IntegrationCatalogSeeder` MUST include a `comfyui` connector definition with:
- Category `MediaGeneration`
- Capability registration compatible with the `generate-image` workflow step
- Model-selector applicable: `true` (checkpoints synced via `ComfyUiModelCatalogClient`)
- Connection mode `SystemDefault` (local dev server; no BYOK key required initially)

The catalog endpoint (`GET /api/integrations/workflow-configurations`) MUST return
`comfyui` as a selectable provider for the `generate-image` step once seeded.

#### Scenario: comfyui appears in workflow catalog for generate-image step (CUI-INT-01-a)

- GIVEN the comfyui connector has been seeded via `IntegrationCatalogSeeder`
- WHEN `GET /api/integrations/workflow-configurations` is called
- THEN the response includes `comfyui` as a selectable provider for the `generate-image` step
- AND the entry indicates model selector is applicable
- AND no database migration is required (code-only seed)

#### Scenario: comfyui absent from catalog when not seeded (CUI-INT-01-b)

- GIVEN the pre-change codebase (comfyui seed not present)
- WHEN `GET /api/integrations/workflow-configurations` is called
- THEN no `comfyui` provider appears in the catalog response

---

### Requirement: CUI-INT-02 — Per-Org Seeder Creates OrganizationIntegration for comfyui; generate-image Step Becomes Selectable

A `ComfyUiOrganizationSeeder` (mirroring `ClaudePromptCliOrganizationSeeder`) MUST
create the `OrganizationIntegration` record for an org when that org is configured
to use `comfyui`. Once connected, the org MUST be able to select `comfyui` as the
provider for the `generate-image` step and select a checkpoint from the synced model
list. Orgs not seeded with `comfyui` MUST NOT have a `comfyui` integration record.

#### Scenario: Org seeded with comfyui — integration record created; step selectable (CUI-INT-02-a)

- GIVEN an org whose config includes `comfyui`
- WHEN the per-org seeder runs
- THEN an `OrganizationIntegration` record with provider key `comfyui` exists for that org
- AND the Integrations routing UI shows `comfyui` as a selectable provider for `generate-image`
- AND checkpoints synced from `ComfyUiModelCatalogClient` appear in the model selector

#### Scenario: Org not configured for comfyui — no integration record created (CUI-INT-02-b)

- GIVEN an org that is not configured to use `comfyui`
- WHEN the per-org seeder runs
- THEN no `comfyui` `OrganizationIntegration` record is created for that org
- AND the Integrations routing UI does NOT show `comfyui` as a selectable option for that org

---

### Requirement: CUI-INT-03 — ComfyUiIntegrationHealthCheck Reports ComfyUI Server Connectivity

A `ComfyUiIntegrationHealthCheck` (mirroring the claude-p health check) MUST verify
reachability of the configured ComfyUI server base URL and report `Connected` or `Failed`
status in the Integrations UI. A health-check failure MUST NOT prevent the integration
record from existing or block carousel job scheduling; it is informational only.

#### Scenario: ComfyUI server reachable — health check reports Connected (CUI-INT-03-a)

- GIVEN a comfyui integration with a reachable server at the configured base URL
- WHEN the health check runs (e.g., via "Test connection" in the Integrations UI)
- THEN the health check reports `Connected`

#### Scenario: ComfyUI server unreachable — health check reports Failed; job scheduling unaffected (CUI-INT-03-b)

- GIVEN a comfyui integration with an unreachable server
- WHEN the health check runs
- THEN the health check reports `Failed` with a connectivity error detail
- AND no carousel job is prevented from scheduling due to the health-check failure

---

### Requirement: CUI-INT-04 — ExternalProviderClientFactory Resolves comfyui to ComfyUiImageModelClient

`ExternalProviderClientFactory` MUST resolve the `comfyui` provider key to
`ComfyUiImageModelClient` when constructing an `IImageModelClient` for a workflow step.
Resolution MUST follow the existing factory pattern. An unknown or unconfigured provider
key MUST NOT return `ComfyUiImageModelClient`.

#### Scenario: Factory resolves comfyui key to ComfyUiImageModelClient (CUI-INT-04-a)

- GIVEN a request to resolve an `IImageModelClient` for provider key `comfyui`
- WHEN `ExternalProviderClientFactory` handles the request
- THEN the returned client is `ComfyUiImageModelClient`
- AND no other provider client is returned for this key

#### Scenario: Factory with non-comfyui key does not return ComfyUiImageModelClient (CUI-INT-04-b)

- GIVEN a request to resolve an `IImageModelClient` for provider key `openai`
- WHEN `ExternalProviderClientFactory` handles the request
- THEN `ComfyUiImageModelClient` is NOT returned
- AND the existing `OpenAiImageModelClient` is returned as before

---

### Requirement: CUI-INT-05 — All New Connector Artifacts Are Additive; Non-comfyui Orgs and Providers Unaffected

All `comfyui` connector files (seeder, health check, factory wiring, catalog seed) MUST
be purely additive. Removing the `comfyui` seed and routing MUST revert any org back to
its prior provider without schema or behavior change for other orgs. Existing providers
(`openai`, `claude-p`, and all others) MUST function exactly as before.

#### Scenario: Removing comfyui seed — existing providers unaffected; factory unchanged (CUI-INT-05-a)

- GIVEN the comfyui seed and routing are removed from the configuration
- WHEN the application starts and orgs run generation jobs
- THEN all non-comfyui orgs continue using their configured providers unchanged
- AND `ExternalProviderClientFactory` resolves all other provider keys correctly
- AND no error or missing-client exception is raised for non-comfyui orgs

---

## MODIFIED Requirements

None. `CIG-INT-01` through `CIG-INT-04` (generate-image step registration, OpenAI as
first MediaGeneration provider, org-level IMAGE route resolution, and catalog-driven UI)
remain valid and unchanged. `comfyui` is a second `MediaGeneration` provider added to the
same catalog mechanism; no existing requirement block is modified.

## REMOVED Requirements

None.
