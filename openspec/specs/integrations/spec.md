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
