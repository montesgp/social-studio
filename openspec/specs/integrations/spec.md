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
- Expose API endpoints for listing providers, listing organization integrations, reading one organization integration by provider key, connecting/configuring, deleting, testing connection, and selecting default AI provider/model.
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
- Users can select default AI provider/model for the organization.

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
    "actorId": "get-leads~all-in-one-instagram-scraper"
  },
  {
    "networkKey": "instagram",
    "variantKey": "profile",
    "actorId": "apify~instagram-profile-scraper"
  }
]
```

Where:
- Entry `[0]` (array-first) is the **default actor** (`defaultActorId = iMcuOj3FW9xLIbqET`).
- Entry `[1]` (array-second) is the **backup actor** (`backupActorId = dSCLg0C3YEZ83HzYX`).
- JSON array order is preserved by `List<T>` deserialisation; no explicit `priority` field is needed.
- The existing single-video entry (`networkKey="instagram"` without `variantKey="profile"`) MUST remain untouched.

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

When the **default actor** for `(networkKey="instagram", variantKey="profile")` returns an Apify-level transport error, a non-2xx HTTP response, an empty dataset, or an array with no item where `resultType == "profile"`, the system:

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
