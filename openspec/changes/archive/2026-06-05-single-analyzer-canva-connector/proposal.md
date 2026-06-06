# Proposal: Single-Analyzer Fase B / B2 — Canva Connector (Org-Configurable in Settings > Integrations)

## Change ID
`single-analyzer-canva-connector`

## Summary
Upgrade the existing **Canva** entry in the integration catalog so an ORGANIZATION can configure and store its **Canva Connect API** app credentials in **Settings > Integrations**, using the SAME encrypted, BYOK mechanism every other provider already uses.

Today the Canva seed exposes a single `access_token` field — insufficient for the Connect API, which is an OAuth2 app (Client ID + Client Secret + requested scopes). This change replaces that field with the Connect API credential shape, **mirroring the existing `google-drive` OAuth seed**, so the Canva provider card + dynamic credential modal let the user save `client_id`, `client_secret`, and `scopes`, encrypted per org and resolvable at runtime via `IIntegrationResolver`.

This is the **connector-config slice (B2)** of Fase B. It stores the app-level credentials the org provides — nothing more. The actual OAuth authorization-code/PKCE user flow and any Canva API call are deferred to **B3**.

## Problem
The single-analyzer review item **#1** asks: *"the org should be able to configure the Canva connector with its own variables and values, like every other integration."* Right now it cannot, because:

1. **The seed shape is wrong for Connect.** `IntegrationCatalogSeeder` seeds Canva with one credential field, `access_token` (`IntegrationCatalogSeeder.cs` lines 50-62). The Canva **Connect API** is a server-side OAuth2 app identified by **Client ID + Client Secret**, with a set of requested **scopes** — there is no single static access token to paste (per-user access/refresh tokens are minted later by the OAuth flow, which is B3). Source: canva.dev/docs/connect/authentication, .../appendix/scopes (engram obs #565/#566).
2. **No org can self-serve.** Because the field set doesn't match Connect, an org admin cannot enter the credentials Canva issues for their app, so the connector can never reach a `Connected` state and B3 (export-to-Canva) has nothing to build on.

## Goals
- Replace the Canva seed's single `access_token` field with the Connect API BYOK shape: `client_id` (secret, required), `client_secret` (secret, required), `scopes` (non-secret, optional with a sensible default), keeping category `Design` and `SupportsBringYourOwnKey=true`.
- Mirror the existing `google-drive` OAuth seed pattern exactly (no new domain primitives, no new credential mechanism).
- Store the values **encrypted per org** via the existing `OrganizationIntegrationCredential` path and make them **resolvable** through `IIntegrationResolver.ResolveProviderAsync(orgId, "canva")`.
- Keep the seed/upsert **idempotent** and non-destructive to existing integration rows.
- Surface the new fields automatically in the existing Canva provider card + dynamic credential modal, with correct status badges (`NotConfigured` → `Connected`).
- Stay within strict hexagonal boundaries and pass all MUST gates.

## Out of Scope (explicitly deferred to B3)
- **The OAuth2 authorization-code + PKCE user flow** — redirect to Canva, the callback/redirect endpoint, exchanging the code for per-USER access/refresh tokens, single-use refresh-token rotation, and token persistence. B2 only stores the app-level credentials the org provides; minting and refreshing user tokens is B3 (when we actually call the Canva API to export).
- **Any actual Canva API call** — Design Import, asset upload, PPTX import, exports.
- **The "Export to Canva" action / frontend button** — B3.
- **Autofill API and Brand Templates** — Enterprise-only for both the developer app and every end user (engram obs #566). Feature-detected later, never in B2.
- **Per-format / brand-template mapping fields** — not needed to store app credentials; revisit in B3 if the export path requires them.

## Scope Boundaries
- **Domain stays provider-agnostic.** No Canva-specific types enter Domain or Application. The only domain-shaped data is a list of `IntegrationCredentialField` records inside the seed — identical in kind to every other provider.
- **Seeder is Infrastructure.** The change is confined to `IntegrationCatalogSeeder.cs` (the Canva seed entry) plus its test.
- **No schema/EF migration for the field set.** Credential field definitions are stored as JSON (`CredentialFieldsJson`) on the provider row, not as table columns (verified in `EfIntegrationProviderCatalogRepository.UpsertAsync`). The provider GUID is unchanged (`b2610b54-a258-48d3-b2d2-12f70ac99d52`), so existing `OrganizationIntegration` rows keep their FK and are untouched.
- **Frontend is render-only.** The modal already renders `credentialFields` dynamically, choosing `PasswordInput` for secret fields (`integrations-settings-client.tsx` lines 888-926). New fields appear with **no new component code**. Verify only; touch the file solely if a Canva-specific label/help string or field ordering is desired.

## Approach

### 1. Seed change — mirror `google-drive` (Infrastructure)
In `IntegrationCatalogSeeder.cs`, change the Canva `SeedDefinition` (GUID `b2610b54-…`, key `canva`, category `Design`, icon `canva`, `SupportsBringYourOwnKey=true`, all other modes `false`) so its `CredentialFields` become:

| Key | Label | IsSecret | IsRequired | Notes |
|-----|-------|----------|------------|-------|
| `client_id` | Client ID | true | true | Canva Connect app client id |
| `client_secret` | Client secret | true | true | Canva Connect app client secret |
| `scopes` | Scopes | false | **false** | Space-separated Connect scopes; default provided |

**Default scopes** (from the Canva Connect scope appendix, engram obs #565): `asset:read asset:write brandtemplate:meta:read design:meta:read design:content:read design:content:write profile:read`. Carried as the field's `HelpText`/`Placeholder` default; `scopes` stays `IsRequired=false` so an org can connect without retyping them and we don't trip `RequiredCredentialMissing`.

> Rationale for `scopes` being non-secret + optional: it's configuration, not a secret, and the configure handler rejects connection if ANY required field is blank (`ConfigureOrganizationIntegrationHandler.cs` lines 147-155). Making `scopes` required would force the user to fill it just to save — friction with no benefit, since we ship a correct default.

### 2. Idempotent seed/upsert
No new logic needed — `EfIntegrationProviderCatalogRepository.UpsertAsync` matches by `Key`, and on an existing row overwrites `CredentialFieldsJson` in place (lines 53-76). Re-running the seeder simply rewrites the Canva field set; the provider id, `CreatedAtUtc`, and all dependent rows are preserved. No migration file.

### 3. Encryption + resolution reuse
No code change. Org-entered values flow through the existing `ConfigureOrganizationIntegration` path into `OrganizationIntegrationCredential` (encrypted at rest), and `IIntegrationResolver.ResolveProviderAsync(orgId, "canva")` returns them in `ResolvedIntegration.Credentials` exactly as Apify/Claude/Google Drive creds are returned today. The future Canva client (B3) reads `client_id`/`client_secret`/`scopes` from there.

### 4. Frontend card + modal
Verify the Canva card renders the three fields (secrets masked) and that saving valid `client_id`+`client_secret` flips the badge from `NotConfigured` to `Connected`. The `canva` icon already exists in `providerIcons`. Only edit `integrations-settings-client.tsx` if a Canva-specific help string or field order is explicitly wanted.

## Acceptance Criteria
- [ ] After seeding, the catalog `canva` provider has exactly the credential fields `client_id` (secret, required), `client_secret` (secret, required), `scopes` (non-secret, NOT required); category `Design`; `SupportsBringYourOwnKey == true`.
- [ ] The `access_token` field is no longer present on the Canva provider.
- [ ] Re-running `IntegrationCatalogSeeder.SeedAsync` twice is idempotent: the Canva provider GUID stays `b2610b54-a258-48d3-b2d2-12f70ac99d52`, `CreatedAtUtc` is unchanged, and no duplicate provider row is created.
- [ ] An organization can configure Canva in BYOK mode with `client_id` + `client_secret` (+ optional `scopes`); the values are persisted **encrypted** in `OrganizationIntegrationCredential`.
- [ ] `IIntegrationResolver.ResolveProviderAsync(orgId, "canva")` returns those credential values in `ResolvedIntegration.Credentials`.
- [ ] Attempting to connect Canva with `client_id` or `client_secret` blank (BYOK, enabled) fails with `Integration.RequiredCredentialMissing`; a blank `scopes` does NOT block connection.
- [ ] Status reflects configuration: no credentials → `NotConfigured`; required credentials present → `Connected` (status badge in the modal/card).
- [ ] The Canva provider card + dynamic credential modal render `client_id`/`client_secret` as masked secret inputs and `scopes` as a plain input, with no new frontend component code.

## Testing (Strict TDD)
Backend test runner: `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj`.

- **Seeder test** (new) — assert the seeded `canva` provider exposes the Connect credential schema above and no `access_token` field. Mirror the existing seeder/catalog assertions in `OrganizationIntegrationsFlowTests` (e.g. `Overview_ExposesCurrentProviderCatalogWithIconKeys`) and the provider-catalog expectations.
- **Flow test** (extend `OrganizationIntegrationsFlowTests`) — configure Canva for an org with `client_id`+`client_secret`, assert success + `Connected`; assert blank required field returns `RequiredCredentialMissing`; assert a `GetByProvider` round-trip returns the configured (non-secret) keys and the resolver exposes the credentials.
- **Idempotency test** — seed twice through the harness; assert single Canva row, stable id/created timestamp.
- All tests authored **before** the seed change (red → green). No Standard-Mode fallback.

## Affected Capabilities / Specs

| Spec (`openspec/specs/`) | Effect | Why |
|---|---|---|
| `integrations/spec.md` | Touched (delta) | The Canva provider's credential schema is part of the integrations catalog capability. Add a short delta recording the Connect API BYOK field set and that Canva reaches `Connected` once `client_id`+`client_secret` are configured. The existing "Seed baseline providers … Canva …" line stays accurate. |

## Docs-Sync Note
- `openspec/specs/integrations/spec.md` — add the Canva Connect connector delta (credential schema: `client_id`/`client_secret`/`scopes`; BYOK; `Design`; OAuth user-flow deferred to B3).
- Operator doc — add a short "Configure the Canva connector" section (where to get the Connect app `client_id`/`client_secret`, default scopes, that this stores app credentials only; the per-user authorization happens in B3). Follow the pattern of `docs/operators/profile-analysis-setup.md`.
- `canva.instructions.md` is a stub (engram obs #565); a full Connect API guide belongs to B3 — leave it for now or add only a one-line "B2 stores app credentials" pointer.

## PRD Traceability
- **PRD — integrations capability**: "organizations configure third-party providers with their own credentials, encrypted at rest, through Settings > Integrations." This change brings the Canva provider into compliance with that capability (it was the one provider whose field set didn't match its real auth model).
- **Capability**: `integrations` (catalog + org-scoped BYOK configuration).
- Builds toward Fase B item **#5 Export to Canva** (B3), which depends on this connector being configurable and resolvable.

## Risks
- **Existing Canva credential rows.** Any org that previously stored an `access_token` value keeps that orphan credential, but it no longer matches a defined field, so the connector reads as `NotConfigured` until `client_id`/`client_secret` are entered. This is acceptable (the old token was never wired to a working flow), but the migration note and operator doc MUST state it so operators re-enter Connect app credentials. Decide in design whether to actively prune stale `access_token` values or leave them inert (default: leave inert — pruning adds a data-migration risk for zero functional gain).
- **Secret handling.** `client_id` and `client_secret` are both marked secret so the modal masks them and the API never echoes raw secrets back. Verify the `GetByProvider` response continues to return only `configuredCredentialKeys`, not secret values.
- **Scope correctness vs Canva docs.** The default scope string must match current Connect scope names (scopes are NOT inherited: `*:read` ≠ `*:write`). Cross-check against canva.dev/docs/connect/appendix/scopes at implementation time; treat the default as a starting set the org can edit.
- **Per-user-token reality deferred.** The Connect API issues per-USER access/refresh tokens via OAuth, not an app-level token. B2 stores ONLY the app credentials; do not let reviewers expect a working Canva call here — that is explicitly B3. The proposal scope text guards against scope creep.
- **Idempotency regression.** If a future edit changes the Canva seed GUID, it would orphan existing org integrations. The seeder test pins the GUID to prevent this.

## Review Workload Note
Estimated ~120-180 changed lines (~40-70 production in one Infrastructure file + ~80-110 test), plus a small spec delta and operator-doc section. **Budget risk: LOW. Single PR to `dev`, no chaining.** Frontend is verify-only (likely zero or trivial lines).

## Traceability
- Exploration: engram `sdd/single-analyzer-fase-b/explore` (obs #565) and Canva critical findings (obs #566).
- Sub-phase: Fase B / B2 (connector-config). Depends on nothing in B1; B3 (export) depends on this.
- Capability spec: `openspec/specs/integrations/spec.md`.
- Closest existing template: the `google-drive` OAuth seed in `IntegrationCatalogSeeder.cs`.
