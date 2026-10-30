# Tasks: Single-Analyzer Canva Connector (Fase B / B2 — item #1)

<!-- Change: single-analyzer-canva-connector -->
<!-- Spec: openspec/changes/single-analyzer-canva-connector/specs/integrations/spec-delta.md -->
<!-- Proposal: openspec/changes/single-analyzer-canva-connector/proposal.md -->

---

## Review Workload Forecast

| Metric | Value |
|---|---|
| **Estimated total changed lines** | ~120–180 (~40–70 production in one Infrastructure file + ~80–110 test + ~20–30 docs) |
| **Chained PRs recommended** | No |
| **400-line budget risk** | Low |
| **Decision needed before apply** | No |

Single PR to `dev`. No chaining required. Frontend is verify-only (zero or trivial lines).

---

## Work Units

---

### WU-01 — RED: Seeder schema tests

**Spec IDs: CANVA-01, CANVA-04**
**Layer: Infrastructure (test)**
**TDD order: test FIRST — must be RED before WU-02**

- [x] **[TEST]** Add test class `SocialStudio.Tests/Application/Integrations/CanvaConnectorSeederTests.cs`.

  Test: `CanvaProvider_HasConnectApiByokCredentialSchema`
  - Seed the catalog via `IntegrationCatalogSeeder.SeedAsync` against the in-memory harness from `OrganizationIntegrationsFlowTests` (reuse `InMemoryIntegrationProviderCatalogRepository` + `InMemoryUnitOfWork` + `TestClock`).
  - Assert the `canva` provider has exactly 3 credential fields with keys `client_id`, `client_secret`, `scopes`.
  - Assert `client_id`: `IsSecret=true`, `IsRequired=true`.
  - Assert `client_secret`: `IsSecret=true`, `IsRequired=true`.
  - Assert `scopes`: `IsSecret=false`, `IsRequired=false`.
  - Assert `Category == IntegrationProviderCategory.Design` and `SupportsBringYourOwnKey == true`.
  - Assert no field with key `access_token` exists.
  - _Spec: CANVA-01-a_

  Test: `CanvaProvider_HasStableGuid`
  - After seeding, assert provider id equals `b2610b54-a258-48d3-b2d2-12f70ac99d52`.
  - _Spec: CANVA-01-b_

  Test: `CanvaProvider_ScopesField_HasDefaultScopeStringInPlaceholderOrHelpText`
  - After seeding, retrieve the `scopes` field.
  - Assert `Placeholder` or `HelpText` contains `design:content:write` and `profile:read`.
  - _Spec: CANVA-04-a_

- [x] **[VERIFY RED]** Run `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj --filter "FullyQualifiedName~CanvaConnectorSeeder"` — expect failure (schema still has `access_token`).

---

### WU-02 — RED: Idempotency test

**Spec IDs: CANVA-02**
**Layer: Infrastructure (test)**
**TDD order: test FIRST — must be RED before WU-03**

- [x] **[TEST]** Add to `CanvaConnectorSeederTests.cs`:

  Test: `SeedAsync_CalledTwice_ProducesExactlyOneCanvaRow`
  - Call `SeedAsync` twice on the same in-memory harness.
  - Assert exactly one provider row with key `canva`.
  - Assert the provider id after the second call equals `b2610b54-a258-48d3-b2d2-12f70ac99d52`.
  - _Spec: CANVA-02-a_

  Test: `SeedAsync_CalledTwice_CredentialSchemaIsUpdated`
  - Seed once with the LEGACY schema (temporarily build a provider with just `access_token`), insert directly into the harness repository, then call `SeedAsync` once.
  - Assert the resulting provider has the Connect schema (3 fields, no `access_token`).
  - Assert still exactly one provider row.
  - _Spec: CANVA-02-b_

- [x] **[VERIFY RED]** Run `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj --filter "FullyQualifiedName~CanvaConnectorSeeder"` — all 5 tests RED.

---

### WU-03 — RED: Configure + resolve flow tests

**Spec IDs: CANVA-03**
**Layer: Application (test)**
**TDD order: test FIRST — must be RED before WU-04**

- [x] **[TEST]** Add test class `SocialStudio.Tests/Application/Integrations/CanvaConnectorFlowTests.cs`.
  Reuse the same harness pattern (`IntegrationsHarness` inline or extract to a shared file — use the existing class from `OrganizationIntegrationsFlowTests` as the template).
  The harness MUST seed Canva with the NEW Connect schema (`client_id`, `client_secret`, `scopes`).

  Test: `ConfigureCanva_WithClientIdAndSecret_StatusIsConnected`
  - Call `ConfigureOrganizationIntegration` for `canva`, BYOK, `IsEnabled=true`, `client_id="cid"`, `client_secret="csec"`.
  - Assert success. Assert `GetByProvider("canva").Status == IntegrationStatus.Connected`.
  - _Spec: CANVA-03-a_

  Test: `ConfigureCanva_WithScopes_StatusIsConnectedAndScopesResolvable`
  - Configure with `client_id`, `client_secret`, `scopes="asset:read"`, BYOK, `IsEnabled=true`.
  - Assert success. Assert resolver credentials contain key `scopes`.
  - _Spec: CANVA-03-b_

  Test: `ConfigureCanva_BlankClientId_ReturnsRequiredCredentialMissing`
  - Configure with `client_id=""`, `client_secret="csec"`, BYOK, `IsEnabled=true`.
  - Assert failure with code `Integration.RequiredCredentialMissing`.
  - _Spec: CANVA-03-c_

  Test: `ConfigureCanva_BlankScopes_DoesNotBlockConnection`
  - Configure with `client_id="cid"`, `client_secret="csec"`, no `scopes` key, BYOK, `IsEnabled=true`.
  - Assert success and status `Connected`.
  - _Spec: CANVA-03-d_

  Test: `CanvaIntegration_NeverConfigured_StatusIsNotConfigured`
  - Get overview for a fresh organization.
  - Assert the `canva` entry has `Status == IntegrationStatus.NotConfigured`.
  - _Spec: CANVA-03-e_

  Test: `GetByProvider_Canva_DoesNotExposeRawSecretValues`
  - Configure with `client_id="cid"`, `client_secret="csec"`.
  - Call `GetByProvider("canva")`.
  - Assert `ConfiguredCredentialKeys` contains `client_id` and `client_secret`.
  - Assert no property in the response DTO carries the literal value `"cid"` or `"csec"` (raw unprotected string).
  - _Spec: CANVA-03-f_

- [x] **[VERIFY RED]** Run `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj --filter "FullyQualifiedName~CanvaConnectorFlow"` — all 6 tests RED (harness seeds old schema).

---

### WU-04 — GREEN: Upgrade the Canva seed entry

**Spec IDs: CANVA-01, CANVA-02, CANVA-03, CANVA-04**
**Layer: Infrastructure**
**TDD order: production code AFTER tests are RED**
**File: `SocialStudio.Infrastructure/Integrations/IntegrationCatalogSeeder.cs`**

- [x] Replace lines 51-62 (the current Canva `SeedDefinition`) with:

  ```csharp
  new(
      new Guid("b2610b54-a258-48d3-b2d2-12f70ac99d52"),
      "canva",
      "Canva",
      IntegrationProviderCategory.Design,
      "Canva Connect API app credentials (client-id + client-secret). OAuth user flow is B3.",
      "canva",
      supportsBringYourOwnKey: true,
      supportsManagedByIncoders: false,
      supportsSystemDefault: false,
      systemDefaultReadOnly: false,
      [
          new IntegrationCredentialField("client_id",     "Client ID",     true,  true,  null,
              "Canva Connect app client ID"),
          new IntegrationCredentialField("client_secret", "Client secret", true,  true,  null,
              "Canva Connect app client secret"),
          new IntegrationCredentialField("scopes",        "Scopes",        false, false,
              "asset:read asset:write brandtemplate:meta:read design:meta:read design:content:read design:content:write profile:read",
              "Space-separated Connect API scopes. Leave blank to use the default set above.")
      ],
      []),
  ```

  > The `scopes` `Placeholder` carries the default scope string; `IsRequired=false` ensures a blank value does not trigger `RequiredCredentialMissing`.

- [x] Also update the `canva` provider in the test harness `InMemoryIntegrationProviderCatalogRepository.SeedProviders` inside `OrganizationIntegrationsFlowTests.cs` to use the new three-field schema, so existing tests remain green:
  ```csharp
  // replace the existing canva CreateProvider call:
  CreateProvider(
      "canva", "Canva", IntegrationProviderCategory.Design, "canva",
      supportsByok: true, supportsManaged: false, supportsSystemDefault: false, readonlySystemDefault: false,
      [
          new IntegrationCredentialField("client_id",     "Client ID",     true,  true),
          new IntegrationCredentialField("client_secret", "Client secret", true,  true),
          new IntegrationCredentialField("scopes",        "Scopes",        false, false)
      ],
      nowUtc),
  ```

- [x] **[VERIFY GREEN]** Run `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj --filter "FullyQualifiedName~CanvaConnector"` — all 11 tests GREEN.
- [x] **[REGRESSION]** Run `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj` — all existing tests GREEN (no new failures).
- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1` — all MUST gates pass.
- [ ] Commit: `feat(infra): upgrade Canva seed to Connect API BYOK shape (client_id, client_secret, scopes)`

---

### WU-05 — Docs sync

**Spec IDs: CANVA-01 through CANVA-04**
**Layer: Docs**
**Parallel: can run after WU-04 is committed**

- [x] Append the Canva Connect delta section to `openspec/specs/integrations/spec.md` (mirror the `INT-DELTA-01` additive pattern already in that file).

  Content to append:

  ```markdown
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
  ```

- [x] Create `docs/operators/canva-connector-setup.md` following the `profile-analysis-setup.md` pattern. Content:
  - Where to get the Connect app `client_id` / `client_secret` (Canva Developer Portal → Your apps).
  - The default scope string and when to extend it.
  - Note that B2 stores app credentials only; per-user authorization is B3.
  - Migration note: if the org previously had an `access_token` value, it is now inert; re-enter Connect app credentials to reach `Connected`.

- [x] Run `pwsh -File scripts/gates/Run-Gates.ps1` — all MUST gates pass.
- [ ] Commit: `docs: add Canva Connect connector setup guide and integrations spec delta`

---

## Spec Coverage Matrix

| Spec ID | Work Units |
|---|---|
| CANVA-01 | WU-01, WU-04 |
| CANVA-02 | WU-02, WU-04 |
| CANVA-03 | WU-03, WU-04 |
| CANVA-04 | WU-01, WU-04 |
| Docs sync | WU-05 |
