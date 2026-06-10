# Proposal: Configurable Workflow Integrations

## Summary

Make workflow execution provider/model/actor choices configurable per organization, starting with Single Analyzer content and profile analysis. The change also adds organization-scoped AI model synchronization so selectors use persisted provider models instead of static catalog lists.

## Problem

Single Analyzer already resolves credentials through Settings → Integrations, but several execution decisions are still fixed at code/profile level:

- AI model choices come from static provider catalog lists or the currently selected default model.
- Claude model availability is not synchronized from the provider API for the organization's credentials.
- Apify actor routing is stored in `actor_mappings_json`, but it is provider-specific credential JSON, not an auditable workflow-step configuration surface.
- Existing fallback infrastructure exists (`IntegrationRouteCandidate`, `IIntegrationExecutionPolicy`) but is not connected to a persisted workflow-step configuration table after the old routing-policy tables were removed.
- Settings → Integrations is available to organization admins for normal integration credentials, while workflow routing must be system-admin-only.

This leaves operational routing too implicit. If the platform admin changes a workflow step provider/model/actor, the next workflow run should use that database configuration without code changes.

## Goals

1. Persist synchronized AI models by organization integration/provider.
2. Add a system-admin workflow configuration tool under Settings → Integrations.
3. Resolve Single Analyzer workflow steps dynamically from organization-scoped configuration.
4. Preserve tenant isolation: no organization can use another organization's credentials/models/workflow routes.
5. Add explicit structured logs for workflow start/end, organization, analysis type, step, provider/model/actor, primary/fallback, and failures.
6. Cover new behavior and relevant regressions with deterministic automated tests.

## Non-goals

- Do not replace the current integrations catalog/credential model.
- Do not introduce a generic visual workflow engine.
- Do not add live Gemini/OpenAI model sync in this first slice beyond the port/adapter shape.
- Do not expose workflow routing configuration to organization admins.

## First functional scope

### AI model synchronization

- Add a `Sync models` action for AI provider integrations.
- Claude sync calls Anthropic's official models API using the organization's configured credentials.
- Synced models are persisted for that organization/provider integration.
- Model selectors prefer persisted synchronized models. Static provider catalog models remain a seed/fallback only.

### Configurable workflow routing

System admin can configure per organization:

- Workflow: `SingleAnalyzer`
- Analysis type: `ContentAnalysis` or `ProfileAnalysis`
- Step: e.g. `ScrapeInstagramVideo`, `ScrapeTikTokVideo`, `ScrapeInstagramProfile`, `AnalyzeContent`, `AnalyzeProfile`, `GenerateContent`, `SendToCanva`
- Provider/connector: e.g. `apify`, `data365`, `claude`, `openai`, `canva`
- Optional model for AI steps
- Optional actor id for Apify/data-extraction steps
- Priority/fallback order
- Network key and operation key where relevant

### Runtime resolution

Single Analyzer resolves routes by organization before external calls:

- Content/video scraping resolves data-extraction candidates by platform/network and operation.
- Profile scraping resolves profile scrape candidates with fallback priorities.
- AI analysis resolves AI provider + model by configured step first, then falls back to current default-AI selector only when no route exists.
- Content generation uses the configured generation step when present; otherwise current default-AI selector remains fallback.

## Risks and tradeoffs

| Area | Decision | Tradeoff |
|---|---|---|
| Workflow config table vs reusing `actor_mappings_json` only | Add explicit workflow-step config | More schema/API work, but auditable and provider-agnostic. |
| Static catalog model list | Keep as fallback/seed only | Existing setups keep working; selectors become correct after sync. |
| Claude first | Implement live sync only for Claude initially | Meets first scope while leaving port ready for Gemini/OpenAI. |
| System-admin-only tool | Separate backend policy and UI section | More endpoints, but avoids security theater based only on hidden UI. |

