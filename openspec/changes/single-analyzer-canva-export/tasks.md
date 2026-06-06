# Tasks — single-analyzer-canva-export (Fase B / B3)

**Capabilities:** content-generation (#4 Editable PPTX) + integrations (#5 Export to Canva)
**Delivery:** ONE PR · `size:exception` (user-approved, ~900-1180 estimated changed lines, ~2.5-3× 400-line budget)
**Chain strategy:** N/A — single PR
**Commit strategy:** Work-unit commits in seam order. Each commit is independently reviewable (behavior + tests in the same commit).
**TDD mode:** STRICT — `dotnet test`. Every implementation work unit follows RED → GREEN → REFACTOR. Write the failing test first.

---

## Review Workload Forecast

| Metric | Value |
|--------|-------|
| Estimated total changed lines | ~900-1180 |
| 400-line budget risk | **High** |
| Chained PRs recommended | **No — user chose single PR** |
| Decision needed before apply | **`size:exception` — already granted by user** |
| Commit strategy | Work-unit commits (8 commits, seam order) |

This run carries `size:exception`. The PR will be navigated by reading commits one-by-one.

---

## Dependencies and ordering

```
WU-1 (markup + geometry persistence write-fix)
  └─► WU-2 (PPTX adapter, consumes persisted geometry as input)
        └─► WU-3 (PPTX query handler + endpoint + UI button, consumes adapter)

WU-4 (CanvaUserToken entity + repo + migration, isolated domain layer)
  └─► WU-5 (ICanvaOAuthService + CanvaOAuthService + OAuth endpoints, needs token repo)
        └─► WU-6 (ICanvaDesignImportClient + CanvaDesignImportClient, isolated HTTP adapter)
              └─► WU-7 (Export-to-Canva command + endpoint + UI, needs all three above + PPTX adapter from WU-2)

WU-8 (docs sync) — depends on WU-7 (last code unit)
```

**Parallel opportunities (after WU-1 completes):**
- WU-2 and WU-4 can proceed in parallel once WU-1 is merged into the working branch.
- WU-6 can proceed in parallel with WU-5 (no dependency between OAuth service and import client).

---

## Work units

---

### WU-1 — Markup + geometry persistence in `composition_spec_json`

**Goal:** Fix the `null` write in `PersistRenderSuccessAsync`; capture per-`[data-slot]` bounding geometry during the existing Chromium render pass; persist a versioned JSON envelope `{ schemaVersion, markup, canvas, slotGeometry[], backgroundImageRef }` in the `composition_spec_json` column. No migration required (column already mapped, was written `null` in 100% of paths).

**Spec requirements satisfied:** PPTX-01 (markup stored on success), PPTX-04 (single source of truth), PPTX-03 (no re-render at download time).

**RED tests to write first:**
- [ ] `CompositionSpecJsonPersistenceTests.cs` (new file, `Incoders.Hive.Tests/Application/Social/ContentGeneration/Pipeline/`):
  - `RenderSuccess_PersistsNonNullCompositionSpecJson` — asserts the record reaching `IContentGenerationFormatResultRepository.UpsertAsync` has a non-null `CompositionSpecJson`.
  - `CompositionSpecJson_HasExpectedSchemaVersion1` — asserts the deserialized envelope `schemaVersion` equals 1.
  - `CompositionSpecJson_ContainsMarkupString` — asserts envelope `markup` field is non-empty.
  - `CompositionSpecJson_ContainsSlotGeometryArray` — asserts `slotGeometry` has at least one entry matching the stub markup's `data-slot` count.
  - `RenderFailure_DoesNotPersistMarkup` — asserts `CompositionSpecJson` remains `null` on `PersistRenderFailureAsync`.
- [ ] `GeometryCaptureTests.cs` (new file, `Incoders.Hive.Tests/Infrastructure/Rendering/`, **Chromium-guarded**):
  - `RenderPass_CapturesSlotGeometry_ForEachDataSlot` — runtime-skip when Chromium absent; asserts geometry array count matches `data-slot` count.
  - `RenderPass_CapturesBackgroundImageRef` — runtime-skip; asserts `backgroundImageRef` is set after background layer render.

**GREEN implementation steps:**
- [ ] Define `CompositionSpecEnvelope` record (+ `SlotGeometry` record) in Application layer:
  - `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/CompositionSpecEnvelope.cs`
  - Fields: `int SchemaVersion`, `string Markup`, `CanvasSpec Canvas`, `IReadOnlyList<SlotGeometry> SlotGeometry`, `string? BackgroundImageRef`
  - `SlotGeometry`: `string Slot`, `double X`, `double Y`, `double Width`, `double Height`, `string? FontFamily`, `double? FontSizePx`, `string? Color`, `string? TextAlign`
  - `CanvasSpec`: `double Width`, `double Height`, `double Dpi`
- [ ] Add geometry capture to `HtmlChromiumContentRenderer.cs` (Infrastructure/Rendering):
  - After `SetContentAsync`, add one `page.EvalOnSelectorAllAsync("[data-slot]", …getBoundingClientRect + getComputedStyle…)` call.
  - Return geometry array alongside `RenderedAsset` (extend `RenderedAsset` or use a separate result type).
  - Capture background PNG by hiding `[data-layer="content"]` and re-screenshotting; persist hash as `backgroundImageRef`.
- [ ] Update `RenderedAsset` (or introduce `RenderResult`) to carry `CompositionSpecEnvelope?` alongside the screenshot bytes.
- [ ] Fix `ContentGenerationJobProcessor.PersistRenderSuccessAsync` (line ~763):
  - Replace `CompositionSpecJson: null` with `CompositionSpecJson: JsonSerializer.Serialize(envelope, JsonOptions)`.
  - Envelope is built from the designer markup available in `FormatStep` and the geometry captured by the renderer.
  - `PersistRenderFailureAsync` stays `null` — do not change.
- [ ] Add doc-comment to `ContentGenerationFormatResultData.cs` `CompositionSpecJson` property: `// schemaVersion 1: { schemaVersion, markup, canvas, slotGeometry[], backgroundImageRef }`.

**Files / layers touched:**
- `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` (modify)
- `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/CompositionSpecEnvelope.cs` (new)
- `Incoders.Hive.Infrastructure/Rendering/HtmlChromiumContentRenderer.cs` (modify)
- `Incoders.Hive.Infrastructure/Persistence/Postgres/Entities/ContentGenerationFormatResultData.cs` (doc-comment only)
- `Incoders.Hive.Tests/Application/Social/ContentGeneration/Pipeline/CompositionSpecJsonPersistenceTests.cs` (new)
- `Incoders.Hive.Tests/Infrastructure/Rendering/GeometryCaptureTests.cs` (new, Chromium-guarded)

**Commit message stub:**
```
feat(content-gen): WU-1 — persist markup+geometry in composition_spec_json (D1+D3)
```

---

### WU-2 — `IPptxExportAdapter` port + `PptxExportAdapter` OpenXML adapter

**Goal:** Application port `IPptxExportAdapter` + Infrastructure adapter that converts `(markup, slotGeometry[], canvas)` → PPTX `byte[]` using `DocumentFormat.OpenXml`. Adapter is 100% offline, never touches Chromium. One `SlidePart` per asset; `[data-slot]` → editable text box; `[data-layer="background"]` → flattened `Picture`; slide dimensions from `canvas` in EMU.

**Spec requirements satisfied:** PPTX-02 (download as editable PPTX), PPTX-03 (deterministic offline).

**RED tests to write first:**
- [x] `PptxExportAdapterTests.cs` (new, `Incoders.Hive.Tests/Infrastructure/Rendering/`):
  - `BuildAsync_SingleAsset_ProducesOneSlidePart` — uses `StubMarkupBuilder.BuildStubMarkupWithGeometry()` helper + synthetic geometry; asserts one `SlidePart`.
  - `BuildAsync_SlideSize_MatchesCanvasInEmu` — asserts `SlideSize` EMU values derived from `canvas {w,h,dpi=96}` (1080×1920 flyer: 10287000×18288000 EMU; 1080×1080 carousel: 10287000×10287000 EMU). Note: correct value is 1080×9525=10287000, not 10286400.
  - `BuildAsync_DataSlot_ProducesEditableTextBox` — asserts each `data-slot` produces a `Shape` with `TextBody`; text = inner text; not rasterized.
  - `BuildAsync_TextBox_CoordinatesMatchGeometryInEmu` — asserts `sp:spPr/a:xfrm` offset and extents match persisted geometry converted at 9525 EMU/px.
  - `BuildAsync_TextBox_FontAndColorMatchGeometry` — asserts run properties (`fontFamily`, `fontSizePx`, `color`) flow through.
  - `BuildAsync_Background_ProducesPicturePart` — asserts background layer present as `Picture` (full-bleed), behind text boxes.
  - `BuildAsync_NullGeometry_FallsBackToStackedLayout` — asserts graceful fallback when geometry is missing/null for a slot.
  - `BuildAsync_Deterministic_SameInputSameStructure` — call twice, compare `SlidePart` XML structure (node names + text, not byte equality).
- [x] `IPptxExportAdapterContractTests.cs` (new, `Incoders.Hive.Tests/Application/`):
  - `Port_HasNoInfrastructureTypes` — reflects `IPptxExportAdapter` and asserts no `Infrastructure|Persistence|EfCore|Npgsql|AspNetCore|Playwright|DocumentFormat` in parameter or return type names (mirrors existing `IVisualDesignerContractTests` pattern).

**GREEN implementation steps:**
- [x] Create `IPptxExportAdapter.cs` port in Application:
  - `Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IPptxExportAdapter.cs`
  - Signature: `Task<Result<byte[]>> BuildAsync(CompositionSpecEnvelope envelope, CancellationToken ct)`
- [x] Add `DocumentFormat.OpenXml` NuGet reference to `Incoders.Hive.Infrastructure.csproj`.
- [x] Create `PptxExportAdapter.cs` in Infrastructure:
  - `Incoders.Hive.Infrastructure/Rendering/PptxExportAdapter.cs`
  - Implements `IPptxExportAdapter`.
  - Builds `PresentationDocument` in-memory (`MemoryStream`); adds `SlidePart` per asset canvas; maps `SlotGeometry[]` → text boxes; embeds background `ImagePart` from `backgroundImageRef`.
- [x] Create `StubMarkupBuilder.cs` test helper in Tests:
  - `Incoders.Hive.Tests/Infrastructure/Rendering/StubMarkupBuilder.cs`
  - `BuildStubMarkupWithGeometry()` returns a `CompositionSpecEnvelope` with at least 2 `data-slot` elements and a `data-layer="background"` element, with synthetic geometry values.
- [x] Register `PptxExportAdapter` in `InfrastructureContentGenerationExtensions.cs`.

**Files / layers touched:**
- `Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IPptxExportAdapter.cs` (new)
- `Incoders.Hive.Infrastructure/Rendering/PptxExportAdapter.cs` (new)
- `Incoders.Hive.Infrastructure/Social/ContentGeneration/InfrastructureContentGenerationExtensions.cs` (register)
- `Incoders.Hive.Infrastructure/Incoders.Hive.Infrastructure.csproj` (add OpenXml NuGet)
- `Incoders.Hive.Tests/Infrastructure/Rendering/PptxExportAdapterTests.cs` (new)
- `Incoders.Hive.Tests/Infrastructure/Rendering/StubMarkupBuilder.cs` (new)
- `Incoders.Hive.Tests/Application/IPptxExportAdapterContractTests.cs` (new)

**Commit message stub:**
```
feat(content-gen): WU-2 — IPptxExportAdapter port + OpenXML PptxExportAdapter (D3)
```

---

### WU-3 — `ExportFormatToPptxQueryHandler` + download-pptx endpoint + UI button

**Goal:** Application query handler that reads `composition_spec_json`, deserializes the envelope, delegates to `IPptxExportAdapter`, and returns `byte[]`; ApiService endpoint `GET .../formats/{formatKey}/download-pptx`; "Download PPTX" button in `single-analyzer-client.tsx` and API client function in `lib/api/content-generation.ts`. Null markup → typed `not-exportable` (HTTP 422), never re-design.

**Spec requirements satisfied:** PPTX-02 (user can download), PPTX-04 (single markup read path).

**RED tests to write first:**
- [ ] `ExportFormatToPptxQueryHandlerTests.cs` (new, `Incoders.Hive.Tests/Application/Social/ContentGeneration/Export/`):
  - `Handle_ValidEnvelope_DelegatesToAdapterAndReturnsPptxBytes` — fake repo returns row with non-null `CompositionSpecJson`; fake adapter returns `byte[]`; asserts handler returns those bytes.
  - `Handle_NullCompositionSpecJson_ReturnsNotExportableError` — fake repo returns row with `CompositionSpecJson: null`; asserts `Result.Failure` with `not-exportable` error code; adapter is NEVER called.
  - `Handle_FormatKeyNotFound_ReturnsNotFoundError` — fake repo returns null; asserts `Result.Failure` with `not-found`.
  - `Handle_AdapterFailure_PropagatesError` — fake adapter returns failure; asserts handler propagates the error.
- [ ] `MapContentGenerationEndpointsTests.cs` (extend or new in `Incoders.Hive.Tests/ApiService/`):
  - `DownloadPptx_Endpoint_Returns200WithPptxMimeType` — integration test with minimal DI; fake handler returns bytes; asserts `Content-Type: application/vnd.openxmlformats-officedocument.presentationml.presentation` and `Content-Disposition: attachment`.
  - `DownloadPptx_Endpoint_Returns422WhenNotExportable` — handler returns `not-exportable`; asserts HTTP 422.

**GREEN implementation steps:**
- [ ] Create `ExportFormatToPptxQuery.cs` + `ExportFormatToPptxQueryHandler.cs` in Application:
  - `Incoders.Hive.Application/Features/Social/ContentGeneration/Export/ExportFormatToPptxQuery.cs`
  - `Incoders.Hive.Application/Features/Social/ContentGeneration/Export/ExportFormatToPptxQueryHandler.cs`
  - Handler: resolve format result by `(jobId, formatKey)`; deserialize `CompositionSpecJson`; if null → `not-exportable`; delegate to `IPptxExportAdapter.BuildAsync`; return bytes.
- [ ] Extend `MapContentGenerationEndpoints.cs` — add `GET .../formats/{formatKey}/download-pptx` route to `analyzerGroup`:
  - `Incoders.Hive.ApiService/Endpoints/Social/ContentGeneration/MapContentGenerationEndpoints.cs`
  - Returns `File(bytes, "application/vnd.openxmlformats-…", "{formatKey}.pptx")` on success; maps `not-exportable` → 422.
- [ ] Add `downloadFormatAsPptx(jobId, formatKey)` function to `apps/web/lib/api/content-generation.ts`.
- [ ] Add "Download PPTX" button to `apps/web/components/social/single-analyzer-client.tsx` in the format card actions area; calls `downloadFormatAsPptx` and triggers browser download.

**Files / layers touched:**
- `Incoders.Hive.Application/Features/Social/ContentGeneration/Export/ExportFormatToPptxQuery.cs` (new)
- `Incoders.Hive.Application/Features/Social/ContentGeneration/Export/ExportFormatToPptxQueryHandler.cs` (new)
- `Incoders.Hive.ApiService/Endpoints/Social/ContentGeneration/MapContentGenerationEndpoints.cs` (extend)
- `apps/web/lib/api/content-generation.ts` (extend)
- `apps/web/components/social/single-analyzer-client.tsx` (extend)
- `Incoders.Hive.Tests/Application/Social/ContentGeneration/Export/ExportFormatToPptxQueryHandlerTests.cs` (new)
- `Incoders.Hive.Tests/ApiService/MapContentGenerationEndpointsTests.cs` (extend or new)

**Commit message stub:**
```
feat(content-gen): WU-3 — PPTX query handler + download-pptx endpoint + UI button
```

---

### WU-4 — `CanvaUserToken` domain entity + `ICanvaUserTokenRepository` port + EF adapter + migration

**Goal:** Domain entity `CanvaUserToken` (+ `CanvaUserTokenId`) keyed `(userId, organizationIntegrationId)`, with `xmin` optimistic concurrency. Application port `ICanvaUserTokenRepository`. Infrastructure: `CanvaUserTokenData` EF entity, EF config in `AppIdentityDbContext`, `EfCanvaUserTokenRepository` adapter. Single EF migration `Add_CanvaUserTokens` creating `platform.canva_user_tokens`.

**Spec requirements satisfied:** CANVA-USER-01 (token stored on OAuth), CANVA-USER-02 (token refresh rotation, infrastructure for D2), CANVA-USER-03 (revoke deletes only that user's tokens).

**RED tests to write first:**
- [ ] `CanvaUserTokenTests.cs` (new, `Incoders.Hive.Tests/Domain/Integrations/`):
  - `CanvaUserToken_Create_SetsAllFields` — asserts constructor sets `UserId`, `OrganizationIntegrationId`, encrypted token fields, and `AccessTokenExpiresAtUtc`.
  - `CanvaUserToken_Id_IsValueObject` — asserts `CanvaUserTokenId` wraps a `Guid`, equality by value.
- [ ] `ICanvaUserTokenRepositoryContractTests.cs` (new, `Incoders.Hive.Tests/Application/`):
  - `Port_HasNoInfrastructureTypes` — reflects interface, asserts no provider type names in signatures.
- [ ] `EfCanvaUserTokenRepositoryTests.cs` (new, `Incoders.Hive.Tests/Infrastructure/Persistence/`):
  - `UpsertAsync_StoresNewToken` — uses in-memory/SQLite EF context; asserts row stored.
  - `GetAsync_ReturnsStoredToken` — asserts round-trip.
  - `UpsertAsync_XminConflict_ThrowsDbUpdateConcurrencyException` — simulates stale `xmin`; asserts `DbUpdateConcurrencyException` is thrown (the handler in `CanvaOAuthService` is tested in WU-5).
  - `DeleteAsync_RemovesOnlyTargetUserRow` — asserts only the specified `(userId, orgIntegrationId)` row is removed.

**GREEN implementation steps:**
- [ ] Create Domain entity:
  - `Incoders.Hive.Domain/Integrations/CanvaUserToken.cs` — entity with `CanvaUserTokenId Id`, `Guid UserId`, `Guid OrganizationIntegrationId`, `string EncryptedAccessToken`, `string EncryptedRefreshToken`, `DateTime AccessTokenExpiresAtUtc`, `string Scopes`, `DateTime CreatedAtUtc`, `DateTime UpdatedAtUtc`.
  - `Incoders.Hive.Domain/Integrations/CanvaUserTokenId.cs` — value-object wrapping `Guid`.
- [ ] Create Application port:
  - `Incoders.Hive.Application/Abstractions/Integrations/ICanvaUserTokenRepository.cs`
  - Methods: `Task<CanvaUserToken?> GetAsync(Guid userId, Guid organizationIntegrationId, CancellationToken ct)`, `Task UpsertAsync(CanvaUserToken token, CancellationToken ct)`, `Task DeleteAsync(Guid userId, Guid organizationIntegrationId, CancellationToken ct)`.
- [ ] Create Infrastructure EF data entity:
  - `Incoders.Hive.Infrastructure/Persistence/Postgres/Entities/CanvaUserTokenData.cs` — EF data class mapping to `platform.canva_user_tokens`.
- [ ] Configure `CanvaUserTokenData` in `AppIdentityDbContext.cs`:
  - Table `platform.canva_user_tokens`; `xmin` concurrency token (`.IsConcurrencyToken()`); unique index `(UserId, OrganizationIntegrationId)`.
- [ ] Create EF repository:
  - `Incoders.Hive.Infrastructure/Persistence/Postgres/Repositories/EfCanvaUserTokenRepository.cs`
- [ ] Generate migration `Add_CanvaUserTokens`:
  - `Incoders.Hive.Infrastructure/Persistence/Postgres/Migrations/` — creates `platform.canva_user_tokens` table, unique index, `xmin` rowversion.
- [ ] Register `EfCanvaUserTokenRepository` in Infrastructure DI.

**Files / layers touched:**
- `Incoders.Hive.Domain/Integrations/CanvaUserToken.cs` (new)
- `Incoders.Hive.Domain/Integrations/CanvaUserTokenId.cs` (new)
- `Incoders.Hive.Application/Abstractions/Integrations/ICanvaUserTokenRepository.cs` (new)
- `Incoders.Hive.Infrastructure/Persistence/Postgres/Entities/CanvaUserTokenData.cs` (new)
- `Incoders.Hive.Infrastructure/Identity/AppIdentityDbContext.cs` (extend)
- `Incoders.Hive.Infrastructure/Persistence/Postgres/Repositories/EfCanvaUserTokenRepository.cs` (new)
- `Incoders.Hive.Infrastructure/Persistence/Postgres/Migrations/` (new migration `Add_CanvaUserTokens`)
- `Incoders.Hive.Tests/Domain/Integrations/CanvaUserTokenTests.cs` (new)
- `Incoders.Hive.Tests/Application/ICanvaUserTokenRepositoryContractTests.cs` (new)
- `Incoders.Hive.Tests/Infrastructure/Persistence/EfCanvaUserTokenRepositoryTests.cs` (new)

**Commit message stub:**
```
feat(integrations): WU-4 — CanvaUserToken entity + ICanvaUserTokenRepository + EF migration
```

---

### WU-5 — `ICanvaOAuthService` port + `CanvaOAuthService` adapter + authorize/callback endpoints

**Goal:** Application port `ICanvaOAuthService` (PKCE authorize-URL builder, code exchange, `EnsureFreshAsync` with D2 xmin retry). Infrastructure adapter `CanvaOAuthService` using `HttpClient`. ApiService endpoints `GET .../integrations/canva/authorize` and `GET .../integrations/canva/callback`. Reads org BYOK credential via `IIntegrationResolver`. Stores/reads encrypted tokens via `ICanvaUserTokenRepository` using `IIntegrationSecretProtector`.

**Spec requirements satisfied:** CANVA-USER-01 (PKCE auth code flow, state validation), CANVA-USER-02 (token refresh rotation + serialization via xmin), CANVA-USER-03 (revoke path).

**RED tests to write first:**
- [ ] `ICanvaOAuthServiceContractTests.cs` (new, `Incoders.Hive.Tests/Application/`):
  - `Port_HasNoInfrastructureTypes` — reflects interface signatures.
- [ ] `CanvaOAuthServiceTests.cs` (new, `Incoders.Hive.Tests/Infrastructure/Social/Canva/`):
  - `BuildAuthorizeUrl_GeneratesPkceChallengeDerivedFromVerifier` — asserts `code_challenge` is SHA-256/Base64Url of `code_verifier`.
  - `BuildAuthorizeUrl_StateRoundTrips` — asserts state parameter is stored and returned in callback.
  - `BuildAuthorizeUrl_RejectsStateMismatch` — asserts callback with wrong state returns failure.
  - `ExchangeCodeAsync_CallsTokenEndpoint_StoresEncryptedTokens` — fake `HttpMessageHandler` returns token response; asserts `ICanvaUserTokenRepository.UpsertAsync` called with encrypted values; asserts plaintext tokens NOT stored.
  - `EnsureFreshAsync_TokenNotExpired_DoesNotCallCanva` — asserts Canva token endpoint never called.
  - `EnsureFreshAsync_TokenExpired_RefreshesAndRotates` — fake HTTP returns new pair; asserts `UpsertAsync` called with new tokens.
  - `EnsureFreshAsync_XminConflict_ReadsAgainDoesNotCallCanvaSecondTime` — fake repo throws `DbUpdateConcurrencyException` on first `UpsertAsync`, then returns refreshed row on re-read; asserts Canva token endpoint called EXACTLY once.
  - `RevokeAsync_DeletesTokenRow` — asserts `ICanvaUserTokenRepository.DeleteAsync` called.

**GREEN implementation steps:**
- [ ] Create Application port:
  - `Incoders.Hive.Application/Abstractions/Integrations/ICanvaOAuthService.cs`
  - Methods: `Task<string> BuildAuthorizeUrlAsync(Guid userId, Guid orgIntegrationId, CancellationToken ct)`, `Task<Result> ExchangeCodeAsync(Guid userId, Guid orgIntegrationId, string code, string state, CancellationToken ct)`, `Task<Result<string>> EnsureFreshAsync(Guid userId, Guid orgIntegrationId, CancellationToken ct)`, `Task RevokeAsync(Guid userId, Guid orgIntegrationId, CancellationToken ct)`.
- [ ] Create Infrastructure adapter:
  - `Incoders.Hive.Infrastructure/Social/Canva/CanvaOAuthService.cs`
  - PKCE: generate `code_verifier` (32-byte random / Base64Url), derive `code_challenge` (SHA-256 / Base64Url).
  - State: store `(state, verifier)` in `IDistributedCache` or `ICacheStore` keyed by session/user; expire after 10 min.
  - `EnsureFreshAsync`: implements D2 xmin-retry flow (read → check expiry → refresh if needed → `UpsertAsync` with xmin → on `DbUpdateConcurrencyException` re-read once → use rotated row).
  - Resolves org credential via `IIntegrationResolver.ResolveProviderAsync(orgId, "canva")`.
  - Encrypts/decrypts tokens via `IIntegrationSecretProtector`.
- [ ] Create OAuth endpoints in `MapIntegrationsEndpoints.cs` (or new `MapCanvaOAuthEndpoints.cs`):
  - `Incoders.Hive.ApiService/Endpoints/Integrations/MapIntegrationsEndpoints.cs` (extend)
  - `GET .../integrations/canva/authorize` → calls `ICanvaOAuthService.BuildAuthorizeUrlAsync`; 302 redirect to Canva.
  - `GET .../integrations/canva/callback` → calls `ICanvaOAuthService.ExchangeCodeAsync`; on success 302 to UI success page; on failure 302 to error page.
- [ ] Register `CanvaOAuthService` + `HttpClient` (named `"canva-oauth"`) in Infrastructure DI.

**Files / layers touched:**
- `Incoders.Hive.Application/Abstractions/Integrations/ICanvaOAuthService.cs` (new)
- `Incoders.Hive.Infrastructure/Social/Canva/CanvaOAuthService.cs` (new)
- `Incoders.Hive.ApiService/Endpoints/Integrations/MapIntegrationsEndpoints.cs` (extend)
- `Incoders.Hive.Infrastructure/Social/ContentGeneration/InfrastructureContentGenerationExtensions.cs` (register)
- `Incoders.Hive.Tests/Application/ICanvaOAuthServiceContractTests.cs` (new)
- `Incoders.Hive.Tests/Infrastructure/Social/Canva/CanvaOAuthServiceTests.cs` (new)

**Commit message stub:**
```
feat(integrations): WU-5 — ICanvaOAuthService + PKCE flow + xmin refresh rotation (D2)
```

---

### WU-6 — `ICanvaDesignImportClient` port + `CanvaDesignImportClient` HTTP adapter

**Goal:** Application port `ICanvaDesignImportClient` (submit PPTX → poll import job → return edit URL). Infrastructure adapter using `HttpClient` against Canva Design Import API. Bounded poll budget + typed timeout error. Integration-guarded tests (skip when Canva credentials absent).

**Spec requirements satisfied:** CANVA-EXPORT-01 (Design Import API, edit URL), CANVA-EXPORT-03 (API failures surfaced), CANVA-EXPORT-04 (Autofill/Brand Templates never called).

**RED tests to write first:**
- [ ] `ICanvaDesignImportClientContractTests.cs` (new, `Incoders.Hive.Tests/Application/`):
  - `Port_HasNoInfrastructureTypes` — reflects interface signatures.
  - `Port_DoesNotReferenceAutofillOrBrandTemplatesEndpoints` — asserts no string literal `"autofill"` or `"brand-template"` in port definition (static text check).
- [ ] `CanvaDesignImportClientTests.cs` (new, `Incoders.Hive.Tests/Infrastructure/Social/Canva/`):
  - `ImportAsync_SubmitsJobAndPollsUntilSuccess_ReturnEditUrl` — fake `HttpMessageHandler` sequence: submit → 200 `{jobId}`; first poll → `processing`; second poll → `success` + `editUrl`; asserts returned `editUrl`.
  - `ImportAsync_PollTimeout_ReturnsTypedTimeoutError` — handler returns `processing` on every poll; assert `timeout` error after budget exhausted; no hang.
  - `ImportAsync_SubmitReturns5xx_ReturnsHandledError` — asserts failure result, no exception thrown.
  - `ImportAsync_ExpiredToken_ReturnsUnauthorizedError_NotException` — fake 401 from submit; asserts typed error.
  - `ImportAsync_NeverCallsAutofillOrBrandTemplateEndpoints` — inspects all `HttpRequestMessage.RequestUri` values captured by fake handler; asserts none contain `"autofill"` or `"brand-template"`.
  - `ImportAsync_LiveCanva_Integration` (**integration-guarded**, runtime-skip when `CANVA_ACCESS_TOKEN` env var absent) — real HTTP call; asserts `editUrl` returned.

**GREEN implementation steps:**
- [ ] Create Application port:
  - `Incoders.Hive.Application/Abstractions/Social/ContentGeneration/ICanvaDesignImportClient.cs`
  - Signature: `Task<Result<CanvaImportResult>> ImportAsync(byte[] pptxBytes, string accessToken, CancellationToken ct)`
  - `CanvaImportResult`: record `{ string EditUrl, DateTimeOffset ExpiresAt }`.
- [ ] Create Infrastructure adapter:
  - `Incoders.Hive.Infrastructure/Social/Canva/CanvaDesignImportClient.cs`
  - POST to Canva Design Import API (multipart, `file=pptx`); poll job status endpoint with exponential backoff up to configurable budget (default: 12 attempts × 5 s = 60 s).
  - Returns typed `Result<CanvaImportResult>` for success; `timeout`, `api-failure`, `unauthorized` error codes for known failure modes.
  - NEVER calls `/autofill` or `/brand-templates` endpoints.
- [ ] Register `CanvaDesignImportClient` + `HttpClient` (named `"canva-import"`) in DI.

**Files / layers touched:**
- `Incoders.Hive.Application/Abstractions/Social/ContentGeneration/ICanvaDesignImportClient.cs` (new)
- `Incoders.Hive.Application/Abstractions/Social/ContentGeneration/CanvaImportResult.cs` (new)
- `Incoders.Hive.Infrastructure/Social/Canva/CanvaDesignImportClient.cs` (new)
- `Incoders.Hive.Infrastructure/Social/ContentGeneration/InfrastructureContentGenerationExtensions.cs` (register)
- `Incoders.Hive.Tests/Application/ICanvaDesignImportClientContractTests.cs` (new)
- `Incoders.Hive.Tests/Infrastructure/Social/Canva/CanvaDesignImportClientTests.cs` (new)

**Commit message stub:**
```
feat(integrations): WU-6 — ICanvaDesignImportClient port + bounded-poll HTTP adapter
```

---

### WU-7 — `ExportFormatToCanvaCommandHandler` + export-to-canva endpoint + UI flow

**Goal:** Application command handler that gates on token presence (missing → `needs-authorization` with authorize URL), builds PPTX in-memory via `IPptxExportAdapter`, imports via `ICanvaDesignImportClient`, returns `{ editUrl, expiresAt }`. ApiService endpoint `POST .../formats/{formatKey}/export-to-canva`. "Export to Canva" button in `single-analyzer-client.tsx` + "Connect to Canva" / connected-status UI. Covers CANVA-EXPORT-02 (blocked until connected) and CANVA-EXPORT-04 (Autofill gated / deferred extension point only).

**Spec requirements satisfied:** CANVA-EXPORT-01, CANVA-EXPORT-02, CANVA-EXPORT-03, CANVA-EXPORT-04, CANVA-USER-03 (revoke visible in UI).

**RED tests to write first:**
- [ ] `ExportFormatToCanvaCommandHandlerTests.cs` (new, `Incoders.Hive.Tests/Application/Social/ContentGeneration/Export/`):
  - `Handle_UserNotConnected_ReturnsNeedsAuthorizationWithAuthorizeUrl` — fake token repo returns `null`; asserts `needs-authorization` result with non-empty `authorizeUrl`; PPTX adapter and import client never called.
  - `Handle_UserConnectedExpiredToken_RefreshesAndExports` — fake OAuth service `EnsureFreshAsync` returns fresh token; asserts import client called with that token.
  - `Handle_HappyPath_ReturnEditUrl` — all fakes succeed; asserts returned `{ editUrl, expiresAt }`.
  - `Handle_NullCompositionSpecJson_ReturnsNotExportableError` — fake format result repo returns null markup; asserts `not-exportable` error; import client never called.
  - `Handle_ImportClientTimeout_ReturnsHandledError` — fake import client returns `timeout` error; asserts handler propagates typed error.
  - `Handle_ImportClientUnauthorized_ClearsTokensAndReturnsReconnectError` — fake import client returns `unauthorized`; asserts token repo `DeleteAsync` called; asserts `reconnect-required` error returned.
- [ ] Extend `MapContentGenerationEndpointsTests.cs`:
  - `ExportToCanva_Endpoint_Returns200WithEditUrl` — fake handler; asserts JSON `{ editUrl }`.
  - `ExportToCanva_Endpoint_Returns401WhenNeedsAuthorization` — handler returns `needs-authorization`; asserts 401 + `authorizeUrl` in response.

**GREEN implementation steps:**
- [ ] Create `ExportFormatToCanvaCommand.cs` + `ExportFormatToCanvaCommandHandler.cs`:
  - `Incoders.Hive.Application/Features/Social/ContentGeneration/Export/ExportFormatToCanvaCommand.cs`
  - `Incoders.Hive.Application/Features/Social/ContentGeneration/Export/ExportFormatToCanvaCommandHandler.cs`
  - Handler logic: lookup format result (null markup → `not-exportable`); lookup token (null → `needs-authorization`); `EnsureFreshAsync`; build PPTX (`IPptxExportAdapter`); `ImportAsync` (`ICanvaDesignImportClient`); on 401 → delete token + `reconnect-required`; return `editUrl`.
  - Add deferred extension point: `ICanvaAutofillAdapter` (future, unreachable stub or comment-only interface stub in Application — never wired in DI for current plan).
- [ ] Extend `MapContentGenerationEndpoints.cs` — add `POST .../formats/{formatKey}/export-to-canva`:
  - Returns 200 `{ editUrl, expiresAt }` on success; maps `needs-authorization` → 401 `{ authorizeUrl }`; maps `not-exportable` → 422; maps `timeout`/`api-failure` → 502.
- [ ] Add `exportFormatToCanva(jobId, formatKey)` + `getCanvaConnectStatus()` + `revokeCanvaConnection()` to `apps/web/lib/api/content-generation.ts`.
- [ ] Extend `apps/web/components/social/single-analyzer-client.tsx`:
  - "Export to Canva" button per format card; triggers `exportFormatToCanva`; on `needs-authorization` → shows "Connect to Canva" prompt (opens `authorizeUrl`); on success → opens `editUrl` in new tab.
  - "Connect to Canva" / "Disconnect Canva" status in UI (calls `getCanvaConnectStatus` / `revokeCanvaConnection`).

**Files / layers touched:**
- `Incoders.Hive.Application/Features/Social/ContentGeneration/Export/ExportFormatToCanvaCommand.cs` (new)
- `Incoders.Hive.Application/Features/Social/ContentGeneration/Export/ExportFormatToCanvaCommandHandler.cs` (new)
- `Incoders.Hive.Application/Abstractions/Integrations/ICanvaAutofillAdapter.cs` (new — deferred stub, not wired)
- `Incoders.Hive.ApiService/Endpoints/Social/ContentGeneration/MapContentGenerationEndpoints.cs` (extend)
- `apps/web/lib/api/content-generation.ts` (extend)
- `apps/web/components/social/single-analyzer-client.tsx` (extend)
- `Incoders.Hive.Tests/Application/Social/ContentGeneration/Export/ExportFormatToCanvaCommandHandlerTests.cs` (new)
- `Incoders.Hive.Tests/ApiService/MapContentGenerationEndpointsTests.cs` (extend)

**Commit message stub:**
```
feat(integrations): WU-7 — ExportToCanva command + endpoint + Connect/Export UI flow
```

---

### WU-8 — Docs sync + spec-delta reconciliation

**Goal:** Update `docs/` connector guide for Canva (per-user OAuth flow, scopes, PPTX export notes). Update or add a note in `openspec/changes/single-analyzer-canva-export/specs/` confirming implementation matches spec. No new spec requirements — confirmation only.

**Spec requirements satisfied:** All (final reconciliation).

**RED tests:** None — docs-only work unit. Gates serve as the verification gate (`pwsh -File scripts/gates/Run-Gates.ps1`).

**GREEN implementation steps:**
- [ ] Update or create Canva connector guide:
  - `docs/connectors/canva.md` — section on per-user OAuth (PKCE), required scopes, how Design Import works, PPTX export notes, what Autofill/Brand Templates are and why they are deferred.
- [ ] Add spec reconciliation note to `openspec/changes/single-analyzer-canva-export/specs/content-generation/spec-delta.md` and `specs/integrations/spec-delta.md`:
  - Confirm which spec IDs (PPTX-01…04, CANVA-USER-01…03, CANVA-EXPORT-01…04) are covered by which WUs.
- [ ] Run `pwsh -File scripts/gates/Run-Gates.ps1` and confirm all MUST gates pass.

**Files / layers touched:**
- `docs/connectors/canva.md` (new or extend)
- `openspec/changes/single-analyzer-canva-export/specs/content-generation/spec-delta.md` (extend — reconciliation note)
- `openspec/changes/single-analyzer-canva-export/specs/integrations/spec-delta.md` (extend — reconciliation note)

**Commit message stub:**
```
docs(integrations): WU-8 — Canva connector guide + spec-delta reconciliation
```

---

## Test plan summary

| Work unit | Tests | Offline? | Guard |
|-----------|-------|----------|-------|
| WU-1 | `CompositionSpecJsonPersistenceTests` | Yes (unit) | — |
| WU-1 | `GeometryCaptureTests` | No | `ChromiumAvailabilityCheck` runtime-skip |
| WU-2 | `PptxExportAdapterTests`, `IPptxExportAdapterContractTests` | Yes (unit) | — |
| WU-3 | `ExportFormatToPptxQueryHandlerTests`, endpoint tests | Yes (unit) | — |
| WU-4 | `CanvaUserTokenTests`, `EfCanvaUserTokenRepositoryTests`, contract tests | Yes (unit) | — |
| WU-5 | `CanvaOAuthServiceTests`, contract tests | Yes (unit, fake HTTP) | — |
| WU-6 | `CanvaDesignImportClientTests` (fake HTTP) | Yes | — |
| WU-6 | `CanvaDesignImportClientTests.ImportAsync_LiveCanva_Integration` | No | `CANVA_ACCESS_TOKEN` env var skip |
| WU-7 | `ExportFormatToCanvaCommandHandlerTests`, endpoint tests | Yes (unit) | — |
| WU-8 | None | — | Gates only |

All `dotnet test` runs must be green before declaring a WU complete. Integration-guarded tests are skipped in CI unless credentials are injected.

---

## Definition of done

- [ ] All 8 work units completed and committed in seam order.
- [ ] `dotnet test` passes with 0 failures (integration-guarded tests skipped, not failed).
- [ ] All ports (`IPptxExportAdapter`, `ICanvaUserTokenRepository`, `ICanvaOAuthService`, `ICanvaDesignImportClient`) pass `Port_HasNoInfrastructureTypes` contract assertion.
- [ ] `PptxExportAdapter`, `CanvaOAuthService`, `CanvaDesignImportClient` registered only in Infrastructure DI — not leaked to Domain or Application DI.
- [ ] `DocumentFormat.OpenXml` referenced only in `Incoders.Hive.Infrastructure.csproj`.
- [ ] Migration `Add_CanvaUserTokens` is the ONLY new EF migration in this change.
- [ ] `composition_spec_json` never written as `null` in the success render path.
- [ ] Autofill/Brand Templates endpoints never reachable from active code paths (`ICanvaAutofillAdapter` stub exists but is not wired in DI).
- [ ] `pwsh -File scripts/gates/Run-Gates.ps1` exits 0 — all MUST gates pass.
- [ ] Docs synced: `docs/connectors/canva.md` updated.
- [ ] PR opened against `dev` from `feat/b3-canva-export` branch (single PR, `size:exception` label).
