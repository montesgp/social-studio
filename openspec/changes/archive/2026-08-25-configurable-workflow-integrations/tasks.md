# Tasks: Configurable Workflow Integrations

## 1. Foundation and persistence

- [x] Add Domain/Application contracts for organization AI models.
- [x] Add Domain/Application contracts for workflow step configurations.
- [x] Add EF entities/configuration/migration for `organization_ai_models`.
- [x] Add EF entities/configuration/migration for `organization_workflow_step_configs`.
- [x] Add repository implementations and DI registration.

## 2. AI model synchronization

- [x] Add `IAiModelCatalogClient` and sync handler.
- [x] Implement Claude model list adapter using official Anthropic models endpoint.
- [x] Persist synced models scoped to organization integration.
- [x] Expose list/sync endpoints.
- [x] Keep test connection unchanged: credential validation remains separate from model sync.

## 3. Workflow configuration API

- [x] Add workflow config DTOs/commands/queries.
- [x] Add sysadmin-only endpoints with organization/workflow filtering.
- [x] Validate organization isolation and provider/integration ownership.
- [x] Validate AI steps require `model_id`; Apify data steps can require `actor_id`.

## 4. Runtime resolution

- [x] Add workflow configuration resolver returning ordered `IntegrationRouteCandidate`-like candidates.
- [x] Wire Single Analyzer content scrape route resolution.
- [x] Wire Profile Analyzer scrape route resolution.
- [x] Wire content-analysis AI route resolution.
- [x] Wire profile-analysis AI route resolution.
- [x] Wire content-generation AI route resolution.
- [x] Keep existing default-AI and legacy Apify actor mapping as compatibility fallback only when no workflow config exists.

## 5. Logging

- [x] Add structured workflow logs for content analysis.
- [x] Add structured workflow logs for profile analysis.
- [x] Log configuration resolution before external API calls.
- [x] Log fallback attempts and final outcome.

## 6. Frontend

- [x] Add AI model sync action to provider cards.
- [x] Make model selectors prefer persisted synced models.
- [x] Add sysadmin-only workflow routing panel under Settings → Integrations.
- [x] Add organization/workflow filters and editable priority rows.
- [x] Hide workflow routing from organization admins.

## 7. Tests and gates

- [x] Add model sync tests.
- [x] Add persisted model selector/API contract tests.
- [x] Add workflow resolver/fallback/actor tests.
- [x] Add RBAC tests for sysadmin-only workflow configuration.
- [x] Add Single Analyzer no-hardcode regression tests.
- [x] Add explicit workflow logging tests.
- [x] Add test-connection no-regression tests.
- [x] Run `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj`.
- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1`.

## 8. Per-network routing + AI fallback + Default-AI removal (follow-up)

- [x] Frontend: per-network tabs inside each workflow step — each network keeps its own ordered execution routes; "Add backup route" applies only to the active network (replaces the mutating network selector).
- [x] Frontend: align Workflow/Organization row and use labels-above consistently across the routing panel.
- [x] Backend: AI step iterates the priority-ordered workflow routes (try primary, on failure log `WorkflowFallbackSelected` and try the next — backup may switch provider or keep the provider with a different model) in `SingleContentAnalyzer` (content) and `ProfileAnalyzerOrchestrator` (profile).
- [x] Backend: add `WorkflowStart` / `WorkflowCompleted` / `WorkflowFailed` + `WorkflowProviderAttemptStart/Succeeded/Failed` bookend logs so the file-log reflects every connector/provider used.
- [x] Backend: typed `integrations.ai_route_not_configured` error when no AI route is configured (no silent default).
- [x] Backend: **remove the Default-AI subsystem** — `IDefaultAiSelector`/`DefaultAiSelector`, `SetDefaultAi` command/handler/validator + endpoint, `GetSocialDefaultAiProvider` query/handler + endpoint, `IIntegrationResolver.ResolveDefaultAiProviderAsync`, `OrganizationIntegration.IsDefaultAi`/`SetDefaultAi`/`ClearDefaultAi`, and the overview `DefaultAiProviderKey`/`DefaultAiModel`.
- [x] Backend: EF migration `RemoveOrganizationIntegrationDefaultAi` drops the `is_default_ai` column.
- [x] Backend: `AiVisualDesigner` resolves its agent from the workflow `generate-content` route (was `IDefaultAiSelector`).
- [x] Frontend: remove the "Default AI provider / model" card + state + API + `isDefaultAi`/`defaultAiProviderKey`/`defaultAiModel` types; show a sanitized, non-technical "AI not configured" error with a contact-support option.
- [x] Update affected tests (selectors → workflow-resolver stubs; delete tests of removed features) and run the full suite green.
