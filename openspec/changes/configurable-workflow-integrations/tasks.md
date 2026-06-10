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
