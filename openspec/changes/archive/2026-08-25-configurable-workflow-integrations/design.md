# Design: Configurable Workflow Integrations

## Existing components verified

| Existing component | Current role | Reuse decision |
|---|---|---|
| `platform.integration_providers` / `IntegrationProvider.AvailableModels` | Global provider catalog with static model list | Keep as seed/fallback. Do not use as the primary selector source after sync. |
| `platform.organization_integrations` | Organization-scoped provider state | Keep. The `is_default_ai` column is **dropped** (migration `RemoveOrganizationIntegrationDefaultAi`); the "default AI" is now the workflow priority-1 route. |
| `platform.organization_integration_credentials` | Encrypted org credentials | Reuse for sync and runtime resolution. |
| `IIntegrationResolver` | Resolves connected provider credentials by organization | Extend/compose with workflow resolver; keep credential isolation here. |
| `IDefaultAiSelector` | Former default-AI selection rule | **Removed entirely** (front + back, incl. `SetDefaultAi`/`GetSocialDefaultAiProvider` endpoints). AI selection comes solely from the priority-ordered workflow config; priority-1 IS the default. No Default-AI fallback. |
| `IIntegrationExecutionPolicy` + `IntegrationRouteCandidate` | Existing retry/fallback execution primitive | Reuse for priority/fallback execution where possible. |
| `ApifySocialContentExtractor` | Single-video Apify extraction using `actor_mappings_json` | Refactor to accept explicit actor candidate(s) from workflow config; keep legacy mapping fallback. |
| `ApifyProfileScrapeResolver` | Profile scraping with primary + backups from `actor_mappings_json` | Refactor to use workflow actor candidates first; keep legacy profile mapping fallback. |
| `SingleContentAnalyzer` / `ProfileAnalyzerOrchestrator` | Workflow orchestration | Add workflow-resolution and structured logging before external calls. |
| Settings → Integrations UI | Organization credential configuration | Add sysadmin-only workflow configuration section and model sync action. |

## Data model

### `platform.organization_ai_models`

Organization-scoped model inventory synchronized from a provider.

Columns:

- `id uuid`
- `organization_id uuid`
- `organization_integration_id uuid`
- `provider_id uuid`
- `provider_key varchar(64)`
- `model_id varchar(200)`
- `display_name varchar(240)`
- `capabilities_json text`
- `metadata_json text`
- `is_available bool`
- `synced_at_utc timestamptz`
- `created_at_utc timestamptz`
- `updated_at_utc timestamptz`

Unique index:

- `(organization_id, provider_id, model_id)`

### `platform.organization_workflow_step_configs`

System-admin-owned workflow routing rows.

Columns:

- `id uuid`
- `organization_id uuid`
- `workflow_key varchar(80)` — e.g. `single-analyzer`
- `analysis_type varchar(80)` — e.g. `content-analysis`, `profile-analysis`
- `step_key varchar(120)` — e.g. `scrape-instagram-video`, `analyze-content`
- `operation_key varchar(80) null` — e.g. `scrape-video`, `scrape-profile`, `ai-analysis`
- `network_key varchar(64) null` — e.g. `instagram`, `tiktok`, `youtube`
- `provider_id uuid`
- `provider_key varchar(64)`
- `organization_integration_id uuid`
- `model_id varchar(200) null`
- `actor_id varchar(240) null`
- `priority int`
- `is_enabled bool`
- `created_at_utc timestamptz`
- `updated_at_utc timestamptz`

Indexes:

- `(organization_id, workflow_key, analysis_type)`
- unique active priority per `(organization_id, workflow_key, analysis_type, step_key, network_key, operation_key, priority)`

## Application ports

- `IAiModelCatalogClient`: provider adapter contract to list models.
- `IOrganizationAiModelRepository`: persistence port.
- `IWorkflowStepConfigurationRepository`: persistence port.
- `IWorkflowConfigurationResolver`: returns ordered candidates for a workflow step.

Provider-specific model clients live in Infrastructure:

- `ClaudeModelCatalogClient` uses `GET https://api.anthropic.com/v1/models` with `x-api-key` and `anthropic-version`.
- `OpenAiModelCatalogClient` and `GeminiModelCatalogClient` can be added behind the same port. The official APIs support model-list endpoints, but first implementation only wires Claude live sync.

## Runtime routing

### Single Analyzer content/video

1. Start workflow log with organization, user, workflow, analysis type.
2. Resolve content scrape step from `organization_workflow_step_configs` by:
   - workflow: `single-analyzer`
   - analysis type: `content-analysis`
   - operation: `scrape-video`
   - network: normalized platform key.
3. If no config exists, fall back to current profile defaults (`apify` + legacy `actor_mappings_json`).
4. Execute primary candidate; on failure, log fallback decision and try next priority.
5. Resolve AI analysis step SOLELY from the priority-ordered workflow config (`analyze-content`/`ai-analysis`):
   - try priority-1 (the default); on failure log `WorkflowFallbackSelected` and try the next route (a backup may switch provider entirely or keep the same provider with a different model);
   - if no route is configured, fail with `integrations.ai_route_not_configured` (no Default-AI fallback).

### Single Analyzer profile

1. Resolve scrape step by:
   - workflow: `single-analyzer`
   - analysis type: `profile-analysis`
   - operation: `scrape-profile`
   - network: `instagram`.
2. Use configured Apify actor priority order first.
3. Fallback to existing `variantKey="profile"` `actor_mappings_json` only when no explicit workflow config exists.
4. Resolve AI analysis step SOLELY from the workflow config (`analyze-profile`/`ai-analysis`), priority-ordered with backup iteration; no Default-AI fallback. No route → `integrations.ai_route_not_configured`.

### Content generation and delivery

First slice prepares routes for:

- `generate-content` AI step.
- `render-package` internal step (no external provider required).
- `send-to-canva` design connector step.

Single Analyzer generation uses the workflow `generate-content`/`ai-generation` route (priority-1; the resolver returns only Connected integrations, so a disconnected primary auto-yields the backup). It is a SINGLE stage — all formats share the same resolved agent (no per-format differentiation). No route → `integrations.ai_route_not_configured`.

## Backend API

### Existing organization integration endpoints

Add:

- `POST /api/integrations/organization/{providerKey}/models/sync`
- `GET /api/integrations/organization/{providerKey}/models`

Organization admins may sync their own organization provider models; sysadmins may do the same for the active organization context selected via tenant header.

### New sysadmin-only workflow config endpoints

Group: `/api/integrations/workflow-configurations`

Policy: `PlatformAdmin`.

- `GET ?organizationId=&workflowKey=`
- `PUT /{organizationId}/{workflowKey}` full replacement/upsert for workflow rows
- optional `GET /catalog/workflows` for UI workflow/step metadata

Backend must reject non-sysadmin callers even if the frontend hides the UI.

## Frontend

Settings → Integrations keeps the existing organization integration cards.

Add:

1. `Sync models` button on AI provider cards.
2. AI model selector uses `organizationAiModels` when present; falls back to provider `availableModels`.
3. Sysadmin-only section/tab: `Workflow routing`.
4. Filters:
   - organization
   - workflow
5. Stage-based editor:
   - stage shell owns analysis type, step, operation, and network selectors
   - each stage renders one or more route cards for provider/model/actor selection
   - route cards are drag-and-drop sortable, and the UI derives priority from the visual order
   - first card is the primary route; every following card is an explicit backup route/fallback
   - backup routes may change provider, AI model, or actor as long as the connector is available for the selected organization

## Logging contract

Use structured `Information` logs for normal resolution/execution:

- `WorkflowStart`
- `WorkflowConfigurationResolved`
- `WorkflowStepResolved`
- `WorkflowProviderAttemptStart`
- `WorkflowProviderAttemptSucceeded`
- `WorkflowProviderAttemptFailed`
- `WorkflowFallbackSelected`
- `WorkflowCompleted`
- `WorkflowFailed`

Every log includes:

- `OrganizationId`
- `UserId` where available
- `WorkflowKey`
- `AnalysisType`
- `StepKey`
- `NetworkKey` where relevant
- `ProviderKey`
- `ModelId` or `ActorId` where relevant
- `Priority`
- `IsFallback`
- `AnalysisId` / correlation id

## Test plan

- Domain/application tests for model sync upsert and org isolation.
- Infrastructure tests for Claude model-list mapping via fake `HttpMessageHandler`.
- Application resolver tests for provider/model/actor resolution, priorities, and fallbacks.
- API tests for sysadmin-only workflow config endpoints and organization-admin 403.
- Single Analyzer tests proving configured provider/model/actor overrides legacy defaults and fallback behavior remains intact.
- Logging tests using in-memory logger for workflow/step/provider/model/actor/fallback fields.
- Regression tests for existing test connection behavior and current Single Analyzer happy path.
