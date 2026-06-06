# Spec Delta: Integrations — Canva Connect API BYOK Credential Schema

<!-- Change: single-analyzer-canva-connector -->
<!-- Applies to: openspec/specs/integrations/spec.md (additive delta) -->

## Purpose

Replace the stale single-field `access_token` shape on the Canva provider with the
Canva Connect API BYOK credential schema (`client_id`, `client_secret`, `scopes`),
so an organization can configure and self-serve the Canva connector through
Settings > Integrations. No existing integration requirements are modified; this
delta is purely additive. The OAuth authorization-code / per-user token flow is
deferred to B3.

---

## PRD Traceability

| Requirement ID | PRD Anchor |
|---|---|
| CANVA-01 | PRD §9 — "Each organization can configure its own third-party integration credentials … through Settings > Integrations" |
| CANVA-02 | PRD §9 — Seed upsert keeps provider identity stable across re-deploys |
| CANVA-03 | PRD §9 — Credentials stored encrypted; resolver exposes them at runtime |
| CANVA-04 | canva.dev/docs/connect/appendix/scopes — sensible default scope set |

---

## ADDED Requirements

---

### Requirement: CANVA-01 — Canva Provider Exposes Connect API BYOK Credential Schema

The `canva` integration provider MUST define exactly three credential fields after
this change is applied:

| Key | Label | IsSecret | IsRequired |
|---|---|---|---|
| `client_id` | Client ID | true | true |
| `client_secret` | Client secret | true | true |
| `scopes` | Scopes | false | **false** |

The provider MUST have `category = Design` and `SupportsBringYourOwnKey = true`.
The legacy `access_token` field MUST NOT be present on the seeded provider.
The provider GUID MUST remain `b2610b54-a258-48d3-b2d2-12f70ac99d52`.

#### Scenario: Canva provider exposes Connect credential schema (CANVA-01-a)

- GIVEN the integration catalog has been seeded
- WHEN the provider with key `canva` is retrieved from the catalog
- THEN its `CredentialFields` contains exactly `client_id` (secret, required), `client_secret` (secret, required), and `scopes` (non-secret, not required)
- AND the field `access_token` is absent
- AND `Category` is `Design` and `SupportsBringYourOwnKey` is `true`

#### Scenario: Canva provider GUID is stable after seed (CANVA-01-b)

- GIVEN the seeder is run for the first time
- WHEN the catalog is queried for the `canva` provider
- THEN the provider id equals `b2610b54-a258-48d3-b2d2-12f70ac99d52`

---

### Requirement: CANVA-02 — Canva Seed Is Idempotent

Re-running `IntegrationCatalogSeeder.SeedAsync` on a database that already
contains the `canva` provider MUST produce exactly one `canva` row whose `Id` and
`CreatedAtUtc` are unchanged. The credential field schema MUST be updated in place
(upsert by `Key`). No duplicate provider row MAY be created.

#### Scenario: Re-seed preserves GUID and creation timestamp (CANVA-02-a)

- GIVEN a seeded catalog containing the `canva` provider with a known GUID and `CreatedAtUtc`
- WHEN `SeedAsync` is called a second time
- THEN the catalog contains exactly one row with key `canva`
- AND its `Id` equals the original GUID
- AND its `CreatedAtUtc` is unchanged

#### Scenario: Re-seed overwrites credential field schema (CANVA-02-b)

- GIVEN a `canva` provider row previously seeded with the legacy `access_token` field
- WHEN `SeedAsync` is called with the updated Connect schema
- THEN the provider's `CredentialFieldsJson` reflects the new schema (`client_id`, `client_secret`, `scopes`)
- AND no second `canva` row exists

---

### Requirement: CANVA-03 — Organization Can Configure and Resolve Canva BYOK Credentials

An organization MUST be able to configure the `canva` provider in
`BringYourOwnKey` mode by supplying `client_id` and `client_secret` (and
optionally `scopes`). The values MUST be persisted encrypted in
`OrganizationIntegrationCredential`. `IIntegrationResolver.ResolveProviderAsync`
MUST return them in `ResolvedIntegration.Credentials`. Status MUST be `Connected`
when required fields are present and `NotConfigured` when they are absent.

Attempting to configure Canva with `client_id` or `client_secret` blank while
`IsEnabled = true` in BYOK mode MUST fail with `Integration.RequiredCredentialMissing`.
A blank `scopes` value MUST NOT trigger this error.

#### Scenario: Configure success — status becomes Connected (CANVA-03-a)

- GIVEN a fresh organization with no Canva credentials
- WHEN `ConfigureOrganizationIntegration` is called with `client_id="cid"` and `client_secret="csec"` in BYOK mode, `IsEnabled=true`
- THEN the result is success
- AND `GetOrganizationIntegrationByProvider("canva")` returns status `Connected`
- AND `ResolveProviderAsync(orgId, "canva")` returns credentials containing keys `client_id` and `client_secret`

#### Scenario: Configure with optional scopes — still connects (CANVA-03-b)

- GIVEN a fresh organization with no Canva credentials
- WHEN `ConfigureOrganizationIntegration` is called with `client_id`, `client_secret`, and `scopes="asset:read"`, `IsEnabled=true`
- THEN the result is success and status is `Connected`
- AND `ResolveProviderAsync` returns credentials containing `scopes`

#### Scenario: Blank required field blocks connection (CANVA-03-c)

- GIVEN a fresh organization
- WHEN `ConfigureOrganizationIntegration` is called with `client_id=""` (blank) and `client_secret` populated, `IsEnabled=true`
- THEN the result is failure with code `Integration.RequiredCredentialMissing`

#### Scenario: Blank scopes does not block connection (CANVA-03-d)

- GIVEN a fresh organization
- WHEN `ConfigureOrganizationIntegration` is called with `client_id` and `client_secret` populated but `scopes` absent or blank, `IsEnabled=true`
- THEN the result is success and status is `Connected`

#### Scenario: No credentials — status is NotConfigured (CANVA-03-e)

- GIVEN an organization that has never configured Canva
- WHEN the integration overview is requested
- THEN the `canva` integration entry has status `NotConfigured`

#### Scenario: GetByProvider does not echo secret values (CANVA-03-f)

- GIVEN an organization with `client_id` and `client_secret` configured
- WHEN `GetOrganizationIntegrationByProvider("canva")` is called
- THEN the response `ConfiguredCredentialKeys` contains `client_id` and `client_secret`
- AND no raw secret value is present in the non-resolver response path

---

### Requirement: CANVA-04 — Default Scopes Ship a Sensible Connect API Scope String

The `scopes` credential field MUST carry a default value (as `Placeholder` or
`HelpText`) equal to the following space-separated Connect scope string:

```
asset:read asset:write brandtemplate:meta:read design:meta:read design:content:read design:content:write profile:read
```

Because `scopes` is `IsRequired=false`, omitting it or leaving it blank MUST NOT
prevent the org from reaching `Connected` status.

#### Scenario: Scopes field ships default scope string (CANVA-04-a)

- GIVEN the seeded `canva` provider
- WHEN the `scopes` credential field definition is inspected
- THEN its `Placeholder` or `HelpText` contains the default scope string including `design:content:write` and `profile:read`
- AND `IsRequired` is `false`

---

## MODIFIED Requirements

None. Existing integration requirements are unchanged; this delta is purely additive.

---

## REMOVED Requirements

None. (The `access_token` field is removed from the seed data, not from a
formally-spec'd requirement. Its removal is implicit in CANVA-01.)
