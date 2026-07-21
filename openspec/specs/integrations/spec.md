# Integrations Spec

## Purpose
Define platform-level integration catalog and organization-scoped provider configuration for Incoders Social workflows.

## Capabilities
- Maintain a global `IntegrationProvider` catalog (provider metadata, frontend icon key, category, supported connection modes, credential schema).
- Maintain `OrganizationIntegration` records per organization (status, mode, defaults, readiness).
- Maintain encrypted `OrganizationIntegrationCredential` values per organization integration.
- Support provider categories: AI model providers, design, data extraction, publishing, and future media generation providers.
- Support connection modes: `BringYourOwnKey`, `ManagedByIncoders`, `SystemDefault`.
- Support statuses: `NotConfigured`, `Connected`, `Failed`, `Disabled`.
- Seed baseline providers: Google Drive, Apify, Data365, Claude, OpenAI, GenSpark AI, TikTok, YouTube, Instagram, plus existing workflow providers Gemini, Canva, and Blotato.
- Expose organization setup checklist groups for Social workflows.
- Single Analyzer setup requires at least one connected content data provider (Apify or Data365) and at least one connected AI model provider.
- Generator setup currently requires at least one connected content data provider (Apify or Data365) and at least one connected AI model provider; design and publishing providers are optional until the generator workflow expands.
- Expose Apify as `SystemDefault` readonly by default while allowing future organization-owned credentials.
- Expose API endpoints for listing providers, listing organization integrations, reading one organization integration by provider key, connecting/configuring, deleting, and testing connection. (The former "select default AI provider/model" endpoint and the `is_default_ai` flag were removed — AI selection is driven entirely by workflow routing.)
- Ensure Social module resolves providers through `IIntegrationResolver` abstraction.

## Constraints
- Organization credentials MUST NOT be stored only in appsettings.
- Organization credential values MUST be encrypted at rest.
- Domain/Application layers remain provider-agnostic.

## Acceptance Criteria
- Settings > Integrations frontend page shows separate Single Analyzer and Generator setup checklists plus grouped provider cards by category.
- Users can connect/configure integrations through a dynamic credential modal.
- Users can test provider connections and see status badges.
- Users can identify readonly/system default providers.
- AI provider/model is selected per organization through workflow routing (priority-ordered routes), not a separate "default AI" toggle: priority-1 is the default and backups (different provider, or same provider with a different model) are tried on failure. When no AI route is configured, analysis/generation fails with `integrations.ai_route_not_configured` (shown to end users as a sanitized, non-technical message with a contact-support option).
- Workflow routing exposes each social network as an independent tab inside its step: every network keeps its own ordered execution routes, and adding a backup applies only to the currently selected network.
- Sysadmins can configure workflow routing from selector-based fields instead of free-text `analysisType`, `stepKey`, `networkKey`, or actor/model ids.
- The workflow selector catalog is backend-owned so new supported workflow steps appear in the routing UI by extending the shared catalog once, not by rewriting the page form.
- Workflow routing network selectors are populated from active platform social networks, never from hardcoded UI values.
- Workflow routing provider selectors show only connected providers compatible with the selected workflow step.
- Workflow routing AI model selectors are populated from the selected provider connector's synced/available models for that organization.
- Workflow routing Apify actor selectors are populated from the selected organization's configured `actor_mappings_json` entries for the matching network + variant.
- Workflow routing is edited per workflow stage, so shared stage selectors (analysis type, step, network) are not duplicated across every route candidate.
- Sysadmins can drag and drop route candidates inside a workflow stage; the UI automatically persists execution priority from that visual order.
- Each workflow stage can define one primary route plus one or more enabled backup routes that may switch provider, model, or actor while still using only connected organization integrations.

### Workflow Routing Selector Contract

- `GET /api/integrations/workflow-configurations` returns both the saved rows and the workflow catalog metadata required to render the selector-driven routing UI.
- The catalog defines workflow key, analysis type, step key, operation key/label, allowed provider categories/keys, network requirements, and whether model/actor selectors apply.
- The backend validates that persisted workflow rows match the catalog, so unsupported provider/step combinations cannot be saved even if a client sends manual values.

---

## Profile Analysis — actor_mappings_json Delta (INT-DELTA-01)

> Added by change `single-analyzer-profile-analysis`. This section is additive; no existing requirements are modified.

### Required actor_mappings_json Entries for Profile Scrape

The Apify `actor_mappings_json` credential MUST contain two entries for profile scraping:

```json
[
  {
    "networkKey": "instagram",
    "variantKey": "profile",
    "actorId": "get-leads~all-in-one-instagram-scraper",
    "isPrimary": true
  },
  {
    "networkKey": "instagram",
    "variantKey": "profile",
    "actorId": "apify~instagram-profile-scraper",
    "isPrimary": false
  }
]
```

Where:
- Entry `[0]` is the **default actor** (`defaultActorId = iMcuOj3FW9xLIbqET`) when `isPrimary=true` or when no primary flag is present.
- Entry `[1]` (array-second) is the **backup actor** (`backupActorId = dSCLg0C3YEZ83HzYX`).
- JSON array order is preserved by `List<T>` deserialisation; `isPrimary` may be used to make the default explicit.
- The existing single-video entry (`networkKey="instagram"` without `variantKey="profile"`) MUST remain untouched.
- The profile scrape adapter MUST build actor-specific inputs:
  - `get-leads/all-in-one-instagram-scraper`: `scrapeMode="instagram-profile-scraper"`, `profiles[]`, `maxPostsPerProfile`, `proxyTier="residential"`.
  - `apify/instagram-profile-scraper`: `usernames[]`.
- The profile response mapper MUST accept both `resultType=="profile"` dataset items and direct profile objects from the official Apify actor, while treating `input_validation_error`, `quality_report`, and `bandwidth_report` as non-profile items.

#### Scenario: Profile entries resolve correct actor ids (INT-01)

- GIVEN an actor_mappings_json credential containing the profile variant entries above
- WHEN the profile scrape adapter resolves actors for (networkKey="instagram", variantKey="profile")
- THEN defaultActorId is iMcuOj3FW9xLIbqET and backupActorId is dSCLg0C3YEZ83HzYX

#### Scenario: Absent profile entries fail fast (INT-01)

- GIVEN an actor_mappings_json credential with no entry for variantKey="profile"
- WHEN a profile analysis is requested
- THEN the system returns 422 with code "Integration.ProfileActorNotConfigured"
- AND no Apify HTTP call is made

### Automatic Backup-Actor Fallback Behavior (INT-02)

When the **default actor** for `(networkKey="instagram", variantKey="profile")` returns an Apify-level transport error, a non-2xx HTTP response, an empty dataset, an input-validation item, or no mappable profile item (`resultType == "profile"` or a direct profile object), the system:

1. Records a `ProviderFailureDiagnostic` for the failed attempt.
2. Automatically retries using the **backup actor** once — in the same pipeline run — without user intervention.
3. If the backup actor also fails, the `ProfileAnalysis` record transitions to `Failed` and both failure reasons are persisted in `ProviderDiagnosticsJson`.

This fallback behavior applies **exclusively** to `variantKey="profile"`. The single-video scrape path has no fallback and MUST NOT be affected (PA-REG-01).

#### Scenario: Fallback recorded in diagnostics (INT-02)

- GIVEN the default actor for (networkKey="instagram", variantKey="profile") fails
- WHEN the automatic fallback executes with the backup actor
- THEN provider_diagnostics_json.fallback_used is true
- AND provider_diagnostics_json.default_actor_error contains the default actor failure reason
- AND provider_diagnostics_json.backup_actor_id contains the backup actor id

#### Profile Analysis Checklist Addition

- Profile Analyzer setup requires `actor_mappings_json` to contain two entries for `variantKey="profile"` (see above).
- Missing entries cause an immediate `Integration.ProfileActorNotConfigured` 422 error with no Apify call made.
- See `docs/operators/profile-analysis-setup.md` for the exact configuration steps.

---

## Canva Connect API BYOK Configuration (CANVA-DELTA-01)

> Added by change `single-analyzer-canva-connector`. Purely additive; no existing requirements modified.

The `canva` integration provider exposes the Canva Connect API BYOK credential
schema: `client_id` (secret, required), `client_secret` (secret, required), and
`scopes` (non-secret, optional, default scope string provided as placeholder).
Category `Design`. The provider reaches `Connected` status once `client_id` and
`client_secret` are configured; a blank `scopes` does not block connection.

The OAuth authorization-code / per-user token flow (PKCE, token exchange,
refresh rotation) is deferred to B3. B2 stores the app-level credentials only.

See `docs/operators/canva-connector-setup.md` for configuration steps.

---

<!-- Merged from openspec/changes/content-image-generation/specs/integrations/spec-delta.md — change applied 2026-06-24 (WU-10) -->

## Added Requirements: generate-image Workflow Step (MediaGeneration) + OpenAI Seed (content-image-generation)

### Requirement: CIG-INT-01 — generate-image Step Registered in WorkflowConfigurationCatalog

The `WorkflowConfigurationCatalog` MUST include a new step entry with:
- **Step key**: `generate-image`
- **Operation category**: `MediaGeneration`
- **Model selector applicable**: true (provider + model are required per route)
- **Network requirement**: none (format-level, not network-level)

The entry MUST be code-only (no database migration required). The backend catalog endpoint
MUST return the new step so the Integrations routing UI can render it without frontend changes.

#### Scenario: generate-image step appears in workflow catalog response (CIG-INT-01-a)

- GIVEN an org with the Integrations routing UI open
- WHEN `GET /api/integrations/workflow-configurations` is called
- THEN the response includes a workflow catalog entry with step key `generate-image`
  under the `MediaGeneration` category
- AND the entry indicates model selector is applicable
- AND no database migration has been applied to produce this entry

#### Scenario: generate-image step absent from catalog when feature is not deployed (CIG-INT-01-b)

- GIVEN the pre-change codebase
- WHEN `GET /api/integrations/workflow-configurations` is called
- THEN no `generate-image` step appears in the catalog response

---

### Requirement: CIG-INT-02 — OpenAI Seeded as First MediaGeneration Provider Option

OpenAI (`dall-e-3` / `gpt-image-1`) MUST appear as a selectable provider/model option
in the Integrations routing UI for the `generate-image` step. OpenAI is seeded as a
`MediaGeneration` provider in `IntegrationCatalogSeeder` (separate from the existing
`AiModel` entry).

#### Scenario: OpenAI available as image provider in routing UI (CIG-INT-02-a)

- GIVEN an org with an OpenAI integration connected
- WHEN the Integrations routing UI renders provider options for the `generate-image` step
- THEN OpenAI appears as a selectable provider
- AND models available for OpenAI (dall-e-3, gpt-image-1) are selectable

#### Scenario: Unconnected provider does not appear for generate-image (CIG-INT-02-b)

- GIVEN an org with OpenAI NOT connected
- WHEN the Integrations routing UI renders provider options for the `generate-image` step
- THEN OpenAI does NOT appear as a selectable option for that org's `generate-image` step

---

### Requirement: CIG-INT-03 — Org-Level IMAGE Route Configured Like Any Text Route

An org MAY configure zero or more routes for the `generate-image` step, each specifying
a provider key and model. The `IWorkflowConfigurationResolver` MUST resolve the IMAGE
route for an org by `generate-image` step key, returning the org's configured
provider+model or null (unconfigured). Resolution behavior MUST mirror `generate-content`.

#### Scenario: Org A image route resolves to OpenAI dall-e-3 (CIG-INT-03-a)

- GIVEN org A has a `generate-image` route configured with provider = OpenAI, model = dall-e-3
- WHEN `IWorkflowConfigurationResolver` is asked for the IMAGE route for org A
- THEN it returns a route context with provider key = openai and model = dall-e-3

#### Scenario: Org B has no image route — resolver returns null (CIG-INT-03-b)

- GIVEN org B has no `generate-image` route configured
- WHEN `IWorkflowConfigurationResolver` is asked for the IMAGE route for org B
- THEN it returns null (or the absence-of-route sentinel)
- AND no error is raised; the caller determines the fallback behavior

---

### Requirement: CIG-INT-04 — Integrations UI Renders generate-image Step Without Frontend Code Change

The Integrations workflow routing UI MUST render the `generate-image` step and its
provider/model selectors automatically from the catalog API response, with no
frontend-specific UI code changes required to support the new step.

#### Scenario: New generate-image step renders in routing UI without frontend code change (CIG-INT-04-a)

- GIVEN the backend catalog returns the `generate-image` step entry
- WHEN the Integrations routing UI loads workflow configuration
- THEN the `generate-image` step section appears in the UI with its provider and model selectors
- AND no new frontend component or conditional rendering code was added to make this work

*Merged from `openspec/changes/content-image-generation/specs/integrations/spec-delta.md` — change applied 2026-06-24.*

---

<!-- Merged from openspec/changes/client-aligned-carousel-generation/specs/integrations/spec-delta.md — change applied 2026-06-28 -->

## Added Requirements: Per-Slide Image-Gen Degradation Observability (client-aligned-carousel-generation)

### Requirement: CAC-INT-01 — Per-Slide Image-Gen Attempt Recorded in Execution-Attempt Telemetry

For every `generate-image` call on a creative slide, the system MUST write a row to the
execution-attempt telemetry store (`social.integration_execution_attempts` or the
equivalent Application-layer port) carrying at minimum:

- Organization id
- Job id
- Slide index (zero-based)
- Step key (`generate-image`)
- Outcome (`success` or `degraded-to-css` with a structured reason)

Writing the attempt record MUST NOT block or fail the generation job; a telemetry write
failure MUST be caught, logged, and NOT propagated. Silent per-slide degradation MUST NOT
change job completion semantics: the job status remains `Completed` and `error_code` remains
null regardless of how many slides degraded.

#### Scenario: Successful image render — attempt recorded as success (CAC-INT-01-a)

- GIVEN a `ClientAesthetic` creative slide where image-gen produces a valid background
- WHEN the slide's generation step completes
- THEN an execution-attempt row is written with outcome = `success`, the slide index,
  and step key `generate-image`

#### Scenario: Image-gen degrades to CSS — attempt recorded as degraded; job still Completed (CAC-INT-01-b)

- GIVEN a `ClientAesthetic` creative slide where image-gen fails and falls back to the CSS background
- WHEN the fallback is applied
- THEN an execution-attempt row is written with outcome = `degraded-to-css` and the failure reason
- AND the job's overall status remains `Completed`
- AND `error_code` on the job record remains null
- AND the execution-attempt row is the mechanism that surfaces the degradation

#### Scenario: Telemetry write failure — logged and not propagated; job continues (CAC-INT-01-c)

- GIVEN the execution-attempt write call throws an exception
- WHEN the exception occurs during the telemetry write
- THEN the exception is caught and logged
- AND the generation job continues and completes normally
- AND no job-level failure is raised due to the telemetry failure

---

<!-- Merged from openspec/changes/comfyui-image-generation-provider/specs/integrations/spec-delta.md — change applied 2026-07-01 -->

## Added Requirements: comfyui Connector Definition, Seeder, Health Check, and Factory Wiring (comfyui-image-generation-provider)

`comfyui` is registered as a new `MediaGeneration` integration provider. This covers the
catalog seed, per-org organization seeder, capability registration, health check, and
provider-factory wiring — mirroring the `claude-p` connector pattern. Existing providers
and all non-`comfyui` organizations are unaffected.

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

## Modified Requirements (comfyui-image-generation-provider)

None. `CIG-INT-01` through `CIG-INT-04` (generate-image step registration, OpenAI as
first MediaGeneration provider, org-level IMAGE route resolution, and catalog-driven UI)
remain valid and unchanged. `comfyui` is a second `MediaGeneration` provider added to the
same catalog mechanism; no existing requirement block is modified.

*Merged from `openspec/changes/client-aligned-carousel-generation/specs/integrations/spec-delta.md` — change applied 2026-06-28.*
## Added Requirement: Apify configuration dialog readability (#46)

The Apify integration configuration dialog MUST provide sufficient desktop width for actor routing
fields while remaining responsive on mobile. The Actor ID column MUST receive more width than the
network and workflow selectors so configured values remain readable.

#### Scenario: Edit Apify actor mappings on desktop

- GIVEN the Apify integration has actor mappings
- WHEN an operator opens its configuration dialog on a desktop viewport
- THEN the dialog is wider than standard provider dialogs
- AND Actor ID values have a dedicated wide input column

---

<!-- Merged from openspec/changes/content-bank-planner/specs/integrations/spec-delta.md — change applied 2026-07-21 (content-bank-planner) -->

## Added Requirements: Per-Client Account Credentials (content-bank-planner)

> Added by change `content-bank-planner`. This section is additive; no existing integration behavior or requirements are modified.

### Requirement: INT-BANK-01 — Per-Client Account Credential Exists Alongside Org-Level Integration

The system MUST persist, per client and per social network, a credential record (extending `ClientOwnAccount` or a new dedicated entity) holding the client account's publish token and external account id for that network. This record MUST be distinct from and layered on top of the org-level `OrganizationIntegration` record for the same provider — resolving a publication destination MUST require both to be present and valid.

#### Scenario: Per-client credential resolves alongside org-level integration

- GIVEN an org with a connected Instagram `OrganizationIntegration` and a client account with a stored Instagram credential
- WHEN a destination is resolved for that client account and Instagram
- THEN both the org-level integration and the per-client credential are returned together

#### Scenario: Org-level integration connected but client has no credential

- GIVEN an org with a connected Instagram `OrganizationIntegration` but a client account with no stored Instagram credential
- WHEN a destination is resolved for that client account and Instagram
- THEN resolution fails with a structured error identifying the missing per-client credential
- AND the org-level integration status is unaffected by this failure

### Requirement: INT-BANK-02 — Per-Client Credentials Are Additive to Existing Integration Behavior

Introducing per-client account credentials MUST NOT change the existing `OrganizationIntegration` catalog, connection modes (`BringYourOwnKey`, `ManagedByIncoders`, `SystemDefault`), statuses, or the Settings > Integrations checklist behavior for Single Analyzer or Generator setup.

#### Scenario: Existing org-level integration checklist is unaffected

- GIVEN an org's existing Single Analyzer and Generator setup checklists
- WHEN per-client account credentials are introduced for Instagram
- THEN the existing checklists continue to evaluate the same org-level connected-provider conditions as before
