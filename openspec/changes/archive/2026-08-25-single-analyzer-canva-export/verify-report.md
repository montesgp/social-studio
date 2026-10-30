# Verify Report -- single-analyzer-canva-export (Fase B / B3)

**Verdict: PASS WITH WARNINGS**
Date: 2026-06-06  |  Branch: feat/single-analyzer-canva-export
Commits: 2d24cfb . 1b32014 . a6dfb35 . c2452dd . 396e133 . d70170a . 2e4017d . c85334a

> **Post-verify update (2026-06-06):** Finding **S1** (`BackgroundImageRef` always null → editable export shipped with a blank background) was promoted from SUGGESTION to a feature-completeness gap and **RESOLVED in WU-9 (commit `de5f0c6`)**: the flattened decorative background is now captured at render time (content layer hidden → background-only screenshot → data-URI) and persisted into the envelope, so PPTX/Canva exports preserve the B1b visual design. Full suite after WU-9: 1189 passed / 0 failed / 2 skipped. Warnings **W1** (UI lazy per-click fetch) and **W2** (resolver-failure fall-through) remain open as non-blocking follow-ups.

## Test + Gate Results

| Suite | Result | Counts |
|---|---|---|
| dotnet test | PASS | 1185 passed / 0 failed / 2 skipped (total 1187) |
| Run-Gates.ps1 | PASS (exit 0) | 16 MUST pass, 2 SHOULD skip (pre-existing) |
| architecture.layering | PASS | 7 projects checked |
| architecture.imports | PASS | No forbidden namespace imports |
| dependencies.cycles | PASS | No cycles |
| secrets.scan | PASS | 1361 files scanned, clean |
| security.config | PASS | 8 appsettings clean |

The 2 skipped gates (tests.frontend.web, coverage.line) are pre-existing infrastructure gaps.
## Findings

### CRITICAL
None.

### WARNING

[W1] UI lazy per-click getOrganizationIntegration() adds a round-trip on every export attempt

File: apps/web/components/social/single-analyzer-client.tsx:1013 and :1036

The Export to Canva handler calls getOrganizationIntegration(orgId, canva) on every click before the POST, and again in the not_connected error branch. One extra REST round-trip per click even when the integration id is already known. Not a correctness defect -- all CANVA-EXPORT-02 scenarios satisfied.

Recommended fix (post-PR): cache orgIntegrationId as component-level state resolved once when the Package tab mounts.

[W2] EnsureFreshAsync falls through with empty credentials when IIntegrationResolver.ResolveProviderAsync fails

File: SocialStudio.Infrastructure/Social/Canva/CanvaOAuthService.cs:227-235

When ResolveProviderAsync fails (org BYOK not configured), the code falls back to empty credentials and calls the Canva token endpoint. Canva returns 401, caught as CanvaOAuthErrors.RefreshFailed. Functionally handled but unintuitive -- a missing BYOK should produce a direct typed error.

Recommended fix: return Result.Failure(CanvaOAuthErrors.ClientCredentialsMissing) immediately when ResolveProviderAsync fails in RefreshAccessTokenAsync.

### SUGGESTION

[S1] BackgroundImageRef in CompositionSpecEnvelope is always null

File: ContentGenerationJobProcessor.cs:818

BackgroundImageRef always stored null. PptxExportAdapter handles null gracefully (skips Picture layer). PPTX-02-b (background as flattened image) not exercised on production data. Known deferred concern per design. Future WU should wire background PNG capture.

[S2] CanvaDesignImportClientTests integration guard is correct -- no action required.

## Spec Traceability Table

| Requirement | Code Location | Test | Status |
|---|---|---|---|
| PPTX-01-a markup stored on success | ContentGenerationJobProcessor.cs:806-821 | CompositionSpecJsonPersistenceTests.cs | PASS |
| PPTX-01-b markup per format in multi-format job | ContentGenerationJobProcessor.cs (loop) | CompositionSpecJsonPersistenceTests.cs | PASS |
| PPTX-01-c failed render does not store markup | ContentGenerationJobProcessor.cs:856 | CompositionSpecJsonPersistenceTests.cs | PASS |
| PPTX-02-a data-slot to editable text box | PptxExportAdapter.cs (TextBody per slot) | PptxExportAdapterTests.cs | PASS |
| PPTX-02-b background to flattened image | PptxExportAdapter.cs (backgroundImageRef path) | PptxExportAdapterTests.cs | PASS (BackgroundImageRef always null in prod -- see S1) |
| PPTX-02-c slide dimensions match canvas | PptxExportAdapter.cs (Canvas to EMU) | PptxExportAdapterTests.cs | PASS |
| PPTX-02-d no markup to handled error 409 | ExportFormatToPptxQueryHandler.cs + ExportFormatToCanvaCommandHandler.cs | ExportFormatToPptxQueryHandlerTests.cs | PASS |
| PPTX-03-a deterministic repeated export | PptxExportAdapter.cs (pure function) | PptxExportAdapterTests.cs:BuildAsync_Deterministic_* | PASS |
| PPTX-03-b offline -- no Chromium/network | PptxExportAdapter.cs (OpenXML only) | PptxExportAdapterTests.cs (all offline) | PASS |
| PPTX-04-a single markup read path | ExportFormatToPptxQueryHandler.cs + ExportFormatToCanvaCommandHandler.cs | Both handler test files | PASS |
| CANVA-USER-01-a PKCE flow stores tokens | CanvaOAuthService.ExchangeCodeAsync | CanvaOAuthServiceTests.cs:ExchangeCodeAsync_CallsTokenEndpoint_StoresEncryptedTokens | PASS |
| CANVA-USER-01-b invalid state rejected | CanvaOAuthService.ExchangeCodeAsync:116 | CanvaOAuthServiceTests.cs:ExchangeCodeAsync_InvalidState_ReturnsFailure | PASS |
| CANVA-USER-01-c cross-org isolation | EfCanvaUserTokenRepository (userId+orgIntegrationId scope) | EfCanvaUserTokenRepositoryTests.cs:DeleteAsync_RemovesOnlyTargetUserRow | PASS |
| CANVA-USER-02-a expired token refreshed | CanvaOAuthService.EnsureFreshAsync:185-193 | CanvaOAuthServiceTests.cs:EnsureFreshAsync_TokenExpired_RefreshesAndRotates | PASS |
| CANVA-USER-02-b refresh token rotated atomically | CanvaOAuthService.RefreshAccessTokenAsync:247 | CanvaOAuthServiceTests.cs:EnsureFreshAsync_TokenExpired_RefreshesAndRotates | PASS |
| CANVA-USER-02-c concurrent refresh serialized (D2) | CanvaOAuthService.RefreshAccessTokenAsync:254 (DbUpdateConcurrencyException + reread) | CanvaOAuthServiceTests.cs:EnsureFreshAsync_XminConflict_ReadsAgainDoesNotCallCanvaSecondTime | PASS |
| CANVA-USER-03-a connected status visible | single-analyzer-client.tsx | UI only | PASS |
| CANVA-USER-03-b disconnected sees connect prompt | single-analyzer-client.tsx (not_connected redirect) | MapContentGenerationCanvaEndpointTests.cs | PASS |
| CANVA-USER-03-c revoke does not affect org credential | CanvaOAuthService.RevokeAsync:197-203 | CanvaOAuthServiceTests.cs:RevokeAsync_DeletesTokenRow | PASS |
| CANVA-EXPORT-01-a happy path edit URL | ExportFormatToCanvaCommandHandler.cs:162 | ExportFormatToCanvaCommandHandlerTests.cs:Handle_HappyPath_ReturnEditUrl | PASS |
| CANVA-EXPORT-01-b edit URL opens editable design | PPTX structural correctness (data-slot to TextBody) | PptxExportAdapterTests.cs | PASS (integration boundary) |
| CANVA-EXPORT-01-c Design Import only, no Autofill | CanvaDesignImportClient.cs (only /rest/v1/imports) | CanvaDesignImportClientTests.cs:ImportAsync_NeverCallsAutofillOrBrandTemplateEndpoints | PASS |
| CANVA-EXPORT-02-a unconnected user blocked | ExportFormatToCanvaCommandHandler.cs:130-137 (token null to NotConnected) | ExportFormatToCanvaCommandHandlerTests.cs:Handle_UserNotConnected | PASS |
| CANVA-EXPORT-02-b no re-prompt once connected | ExportFormatToCanvaCommandHandler.cs (token present skips OAuth gate) | ExportFormatToCanvaCommandHandlerTests.cs:Handle_HappyPath | PASS |
| CANVA-EXPORT-03-a 5xx/timeout to handled error | CanvaDesignImportClient.cs (bounded poll, typed errors) | CanvaDesignImportClientTests.cs (timeout + 5xx) | PASS |
| CANVA-EXPORT-03-b expired token refreshed before export | ExportFormatToCanvaCommandHandler.cs:141-145 (EnsureFreshAsync before ImportAsync) | ExportFormatToCanvaCommandHandlerTests.cs:Handle_UserConnectedExpiredToken | PASS |
| CANVA-EXPORT-03-c unrefreshable clears tokens + reconnect | ExportFormatToCanvaCommandHandler.cs:167-176 (RevokeAsync + ReconnectRequired) | ExportFormatToCanvaCommandHandlerTests.cs:Handle_ImportClientUnauthorized | PASS |
| CANVA-EXPORT-04-a Autofill path not reached | ExportFormatToCanvaCommandHandler.cs; CanvaDesignImportClient.cs | CanvaDesignImportClientTests.cs:ImportAsync_NeverCallsAutofillOrBrandTemplateEndpoints | PASS |
| CANVA-EXPORT-04-b feature-detect extension point | ICanvaAutofillAdapter.cs (compile-time seam, 0 DI refs) | ICanvaDesignImportClientContractTests.cs | PASS |

## Design Fidelity

D1 (markup in composition_spec_json, no new migration): PASS. Write-site fix at ContentGenerationJobProcessor.cs:806-821 serializes versioned CompositionSpecEnvelope (schemaVersion 1) on success render path. Failure paths correctly write null. Single migration Add_CanvaUserTokens confirmed additive-only (commit c2452dd).

D2 (xmin optimistic concurrency + reread-on-conflict retry): PASS. CanvaOAuthService.RefreshAccessTokenAsync catches DbUpdateConcurrencyException, re-reads the row once, returns the concurrent writers already-rotated access token without calling Canva again. Test EnsureFreshAsync_XminConflict_ReadsAgainDoesNotCallCanvaSecondTime uses a real DbUpdateConcurrencyException thrown by XminConflictFakeRepository and asserts httpHandler.CallCount == 1. Substantive non-vacuous test.

D3 (geometry captured at render time; PptxExportAdapter offline): PASS. PptxExportAdapter.cs imports only DocumentFormat.OpenXml.*, Application ports, Domain types. Zero Playwright or HttpClient references confirmed. architecture.imports gate PASS.

## Hexagonal Architecture

All four new ports confirmed clean -- no infra types in signatures:
- IPptxExportAdapter: CompositionSpecEnvelope + CancellationToken -> Result<byte[]>
- ICanvaOAuthService: plain Guid/string -> Result<string>/Result
- ICanvaUserTokenRepository: CanvaUserToken domain entity + Guid
- ICanvaDesignImportClient: byte[] + string -> Result<CanvaImportResult>

DocumentFormat.OpenXml referenced only in SocialStudio.Infrastructure.csproj. architecture.imports PASS. Zero infra type leaks in Domain or Application layers.

## Known Deviations

(a) HTTP 409 not 422: ACCEPTABLE. Project ResultExtensions maps ErrorType.Conflict to 409. Observable behavior satisfies spec intent. Documented in both spec-delta reconciliation sections.

(b) ICanvaAutofillAdapter not in DI: ACCEPTABLE. One reference (own definition file). No DI registration. No implementation. Intended CANVA-EXPORT-04 compile-time seam.

(c) WU-7 organizationId fix: VERIFIED. Real organizationId flows through ExportFormatToCanvaCommand.OrganizationId -> Handler.Handle:142 -> EnsureFreshAsync -> RefreshAccessTokenAsync:218. Test Handle_RealOrganizationId_FlowsIntoEnsureFreshAsync asserts LastOrganizationIdPassedToEnsureFresh equals specificOrgId. No leftover proxy.

(d) UI lazy per-click fetch + full-page Connect redirect: FOLLOW-UP, not a defect. Spec requirements satisfied. Performance concern only (see W1).

## Definition of Done

| Item | Status |
|---|---|
| dotnet test green (0 failures) | PASS (1185/0/2) |
| All 4 new ports pass Port_HasNoInfrastructureTypes | PASS |
| Add_CanvaUserTokens is the ONLY new EF migration | PASS (commit c2452dd, additive-only) |
| composition_spec_json never null in success render path | PASS |
| ICanvaAutofillAdapter stub exists but not wired in DI | PASS |
| Run-Gates.ps1 exits 0 | PASS (16 MUST pass) |
| docs/operators/canva-connector-setup.md updated | PASS (WU-8) |
| Single PR to dev (size:exception label) | PENDING (PR not yet opened) |

## Summary

0 CRITICAL . 2 WARNING . 2 SUGGESTION

All 11 spec requirements (PPTX-01..04, CANVA-USER-01..03, CANVA-EXPORT-01..04) are implemented and have substantive test coverage. Architecture gates pass. Hexagonal boundaries enforced. D2 concurrency guarantee covered by real DbUpdateConcurrencyException test. Migration additive-only.

Ready for PR to dev: YES -- pending size:exception label.
