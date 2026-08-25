# Spec Delta: Integrations — Model Sync and Workflow Step Configuration

## New requirement: Organization-scoped AI model sync

When an organization AI provider integration is connected or validly testable, the system MUST allow an explicit model synchronization action.

### Scenario: Claude models are synchronized from official API

- GIVEN an organization has a connected Claude integration with valid credentials
- WHEN an admin runs `Sync models`
- THEN the backend calls the Claude models API using that organization's credentials
- AND persists returned model ids for that organization/provider integration
- AND the response includes the synchronized model list and sync timestamp

### Scenario: Model selector uses persisted models

- GIVEN an organization has synchronized Claude models
- WHEN Settings → Integrations renders the model selector for Claude
- THEN the selector options come from the persisted organization models
- AND static provider catalog models are used only when no synchronized models exist

### Scenario: Models are isolated by organization

- GIVEN organizations A and B both have Claude integrations
- WHEN organization A synchronizes models
- THEN organization B does not see or select organization A's synchronized rows

## New requirement: System-admin workflow configuration

The system MUST provide sysadmin-only workflow step configuration per organization.

### Scenario: Sysadmin configures Single Analyzer workflow routing

- GIVEN a platform sysadmin selects organization A and workflow `single-analyzer`
- WHEN the sysadmin saves rows for `content-analysis` and `profile-analysis`
- THEN the rows are persisted under organization A
- AND they declare step, provider, model or actor, priority and enabled state

### Scenario: Organization admin cannot administer workflow routing

- GIVEN an authenticated organization admin
- WHEN the admin calls workflow configuration endpoints
- THEN the API returns 403
- AND no workflow routing rows are returned or mutated

## New requirement: Single Analyzer resolves configured workflow steps

### Scenario: Content analysis uses configured scrape actor and AI model

- GIVEN organization A configures `single-analyzer/content-analysis/scrape-video/instagram` with Apify actor `actor-a`
- AND configures `single-analyzer/content-analysis/analyze-content` with Claude model `claude-x`
- WHEN a user from organization A runs Single Analyzer on an Instagram URL
- THEN the backend resolves those configured values before external API calls
- AND Apify is called with `actor-a`
- AND AI analysis is called with provider Claude and model `claude-x`

### Scenario: Actor fallbacks run by priority

- GIVEN a scrape step has actor `actor-a` priority 1 and `actor-b` priority 2
- WHEN `actor-a` fails with a fallback-eligible provider failure
- THEN the backend logs the failure
- AND executes `actor-b`
- AND persists/logs diagnostics showing fallback usage

### Scenario: Tenant isolation is enforced at runtime

- GIVEN organization A and B have different workflow step configurations
- WHEN a user from organization A executes Single Analyzer
- THEN only organization A integrations, credentials, models and workflow rows are considered
- AND no row from organization B can be used

## New requirement: Explicit workflow logs

### Scenario: Logs identify resolved workflow path

- GIVEN a Single Analyzer run starts
- WHEN configuration is resolved and external calls are about to run
- THEN Information logs include organization id, workflow key, analysis type, step key, provider key, model id or actor id, priority and fallback flag
- AND workflow start/end/failure logs include enough context to reconstruct the execution path

