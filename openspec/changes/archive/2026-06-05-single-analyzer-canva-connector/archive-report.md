# Archive Report — single-analyzer-canva-connector (Fase B / B2)

**Archived**: 2026-06-05
**Status**: COMPLETE — verified PASS, merged, archived.

## Scope (item #1 — Canva connector config)
Upgraded the Canva integration seed from a single `access_token` field to the Canva Connect API BYOK shape (`client_id` secret+required, `client_secret` secret+required, `scopes` optional with a sensible default Connect scope string). Category Design, connection mode BringYourOwnKey. Mirrors the existing `google-drive` OAuth seed. Existing Canva provider GUID preserved (`b2610b54-a258-48d3-b2d2-12f70ac99d52`); no EF migration (credential fields are JSON on the provider row); no frontend change (the credential modal renders fields dynamically). Credentials stored encrypted per org and resolvable via `IIntegrationResolver`.

## Verification
- 11 Canva-specific tests (CANVA-01..04) GREEN; full suite 1103 passed / 0 failed / 2 skipped.
- All 16 MUST gates pass. Verify-report: 0 CRITICAL, 0 WARNING (engram obs #583).
- Scope discipline confirmed: NO OAuth authorization flow, NO per-user token exchange/refresh, NO Canva API call, NO Export button, NO Design Import/PPTX/Autofill.

## Spec merge
Delta merged additively into the canonical `openspec/specs/integrations/spec.md` (CANVA-DELTA section) during apply (WU-05).

## Files
- `Incoders.Hive.Infrastructure/Integrations/IntegrationCatalogSeeder.cs` (seed upgrade)
- `Incoders.Hive.Tests/Application/Integrations/CanvaConnectorSeederTests.cs` (new)
- `Incoders.Hive.Tests/Application/Integrations/CanvaConnectorFlowTests.cs` (new)
- `Incoders.Hive.Tests/Application/Integrations/OrganizationIntegrationsFlowTests.cs` (harness Canva entry updated)
- `openspec/specs/integrations/spec.md` (CANVA-DELTA merged)
- `docs/operators/canva-connector-setup.md` (new operator guide)

## Residual / what remains (B3 — separate future change)
B2 stores app-level Canva credentials only. The following are explicitly DEFERRED to **B3 (#4 editable + #5 Export-to-Canva)**:
- OAuth authorization-code user-flow (redirect to Canva, callback endpoint, exchange code for per-USER access/refresh tokens, refresh rotation).
- Any real Canva API call (Asset upload, Design Import of PPTX → editable Canva design).
- PPTX generation (OpenXML) as the editable download/import artifact.
- The "Export to Canva" button in single-analyzer.
- Autofill/Brand Templates remain Enterprise-only → feature-detect.
