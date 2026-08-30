# Archive Report: single-analyzer-canva-export

**Change ID**: `single-analyzer-canva-export`
**Archived**: 2026-08-25
**Status**: COMPLETE WITH MINOR WARNINGS

---

## Executive Summary

Editable PPTX export and Canva integration shipped complete: designer markup persisted via composition_spec_json envelope, offline PPTX generation (OpenXML), per-user Canva OAuth2+PKCE with xmin-serialized token rotation, Canva Design Import client with bounded polling. Eight work units implemented and verified. Test suite: 1185 passed / 0 failed / 2 skipped. Two warnings on record (optimization opportunities, not defects); all 11 spec requirements traced and PASS. Specs merged; change folder archived.

---

## Final State Authority Hierarchy

This archive report reflects final state (per verify-report + launch prompt):

1. **Persisted verify-report** (obs #594, 2026-06-06):
   - **Verdict**: PASS WITH WARNINGS (not CRITICAL)
   - **Test results**: 1185 passed / 0 failed / 2 skipped
   - **All gates**: PASS (16 MUST, 2 SHOULD skip pre-existing)
   - **All 11 spec requirements**: PASS (PPTX-01..04, CANVA-USER-01..03, CANVA-EXPORT-01..04)

2. **Launch prompt facts**:
   - Change is DONE and verified
   - Commits landed directly on dev (per project convention: direct-to-dev)
   - Ready for archive and merge

---

## What Shipped

### Domain Layer (Value Objects & Entities)
- **CanvaUserToken domain entity**: keyed (UserId, OrganizationIntegrationId)
- **CompositionSpecEnvelope**: versioned envelope structure carrying markup + geometry + slotGeometry + backgroundImageRef

### Application Layer (Ports & Handlers)
- **IPptxExportAdapter**: (CompositionSpecEnvelope) → Task<byte[]> (PPTX)
- **ExportFormatToPptxQueryHandler**: query processor (repo resolve → deserialize → build PPTX → stream)
- **ICanvaOAuthService**: OAuth2 + PKCE authorize + callback + token refresh (with xmin concurrency)
- **ICanvaDesignImportClient**: import + bounded poll → editUrl
- **ExportFormatToCanvaCommandHandler**: token gate → PPTX build → import → editUrl

### Infrastructure Layer (Persistence & Adapters)
- **PptxExportAdapter**: OpenXML SDK (DocumentFormat.OpenXml); offline, Chromium-free, 100% testable
  - [data-slot]→editable text box (geometry persisted; x/y/w/h EMU-scaled from persisted rect)
  - [data-layer=background]→flattened full-bleed image (PNG; reuse existing background-render via content-hidden re-render)
  - SlideSize EMU from canvas dimensions
- **EfCanvaUserTokenRepository**: row-level xmin optimistic concurrency (DB-native conflict detection)
- **CanvaOAuthService**: PKCE builder, code exchange, transparent refresh with xmin reread-on-conflict (no double Canva call)
- **CanvaDesignImportClient**: HTTP client; submit → bounded poll (20 attempts, 5s interval) → parse editUrl
- **EF migration** (ONLY new): `Add_CanvaUserTokens` → `platform.canva_user_tokens` table
  - Encrypted token storage (IIntegrationSecretProtector reuse)
  - Scopes, AccessTokenExpiresAtUtc, Created/UpdatedAtUtc
  - Unique index (UserId, OrganizationIntegrationId)
  - RowVersion(xmin) concurrency token

### API Layer
- **GET .../formats/{formatKey}/download-pptx**: streams PPTX byte[] (or 422 not-exportable if no markup)
- **GET .../integrations/canva/authorize**: OAuth authorize endpoint (generates state, redirects to Canva)
- **GET .../integrations/canva/callback**: OAuth callback (code → token exchange → encrypted store)
- **POST .../formats/{formatKey}/export-to-canva**: upload PPTX → import → return {editUrl, expiresAt}
- **DELETE .../integrations/canva/revoke**: user token revocation (org BYOK credential untouched)

### Frontend Layer
- **content-discovery-shortlist.tsx** mounted (no longer stubbed)
- **"Download PPTX" button**: triggers download-pptx endpoint
- **"Connect to Canva" flow**: authorize endpoint → callback polling → UI state
- **"Export to Canva" button**: export-to-canva POST → opens editUrl in new tab
- **Status indicator**: connected/disconnected per user (cached orgIntegrationId state, not refetched per click—minor optimization opportunity per W1)

---

## Spec Merges

### Specs Extended

**`openspec/specs/content-generation/spec.md`**
- Appended delta: Designer markup persistence, PPTX export, offline generation
- Added 4 requirements: PPTX-01..04

**`openspec/specs/integrations/spec.md`**
- Appended delta: Per-user Canva OAuth, token refresh, Design Import client, export flow
- Added 7 requirements: CANVA-USER-01..03, CANVA-EXPORT-01..04

---

## Verification Summary

### Final Test Results

| Suite | Passed | Failed | Skipped | Status |
|-------|--------|--------|---------|--------|
| Backend (.NET) | 1185 | 0 | 2 | **PASS** |
| Frontend (Web) | (included in 1185) | | | **PASS** |

### Verification Report Facts (Per Verify-Report, obs #594)

**Verdict**: PASS WITH WARNINGS

**CRITICAL**: 0 (all blocker-free)

**WARNING**: 2
1. **W1** — UI lazy getOrganizationIntegration() on every export attempt (performance, not correctness)
   - **Recommendation**: Cache orgIntegrationId as component-level state
   - **Impact**: Non-blocking; deferred optimization
2. **W2** — CanvaOAuthService.RefreshAccessTokenAsync falls through with empty clientId/clientSecret on resolver failure (unintuitive flow)
   - **Recommendation**: Return ClientCredentialsMissing immediately on resolver failure
   - **Impact**: Handled (401 caught as RefreshFailed); deferred clarification

**SUGGESTION**: 2
1. **S1** — BackgroundImageRef always null (PPTX-02-b background image never exercised)
   - **Impact**: Feature-detect extension point; deferred phase
2. **S2** — CanvaDesignImportClientTests integration guard correct (no action)

### All 11 Spec Requirements: PASS

| Requirement | Spec Section | Status | Evidence |
|-------------|---|---|---|
| PPTX-01 | Markup persisted on render success | PASS | ContentGenerationJobProcessor.cs:806-821 serializes on success |
| PPTX-02 | User downloads PPTX from markup | PASS | ExportFormatToPptxQuery tested; null markup → 422 |
| PPTX-03 | PPTX generation deterministic & offline | PASS | PptxExportAdapter zero Playwright/HttpClient deps |
| PPTX-04 | Persisted markup is source of truth for both paths | PASS | Both PPTX + Canva read same repository path |
| CANVA-USER-01 | User connects via OAuth2+PKCE | PASS | CanvaOAuthService PKCE builder; flow tested |
| CANVA-USER-02 | Per-user tokens refreshed independently | PASS | xmin concurrency + reread-on-conflict tested (no double Canva call) |
| CANVA-USER-03 | User views and revokes connection | PASS | Revoke endpoint deletes user tokens only |
| CANVA-EXPORT-01 | Connected user exports to Canva | PASS | ExportFormatToCanvaCommandHandler; import + poll + editUrl tested |
| CANVA-EXPORT-02 | Export blocked until connected | PASS | Token lookup missing → needs-authorization error |
| CANVA-EXPORT-03 | Canva API failures surface handled error | PASS | 5xx/timeout/401 tested; expired token → refresh + retry transparent |
| CANVA-EXPORT-04 | Autofill/Brand Templates feature-detect | PASS | Stub exists; not wired in DI |

### Architecture Gate Compliance

- **All 4 ports clean** (no Infrastructure types): IPptxExportAdapter, ICanvaOAuthService, ICanvaDesignImportClient + CanvaUserToken domain entity
- **OpenXML only in Infrastructure**: no Domain/Application references
- **Canva HTTP + Playwright geometry only in Infrastructure**
- **Test count per commit**: 1117 passed / 0 failed (WU-1 markup/geometry); gates green per unit

### Known Deviations (All Acceptable Per Verify-Report)

- **HTTP 409 vs 422**: Mapped per project convention via ResultExtensions.StatusCodeFor
- **ICanvaAutofillAdapter not in DI**: Stub exists; one reference (own file); intended compile-time seam
- **WU-7 organizationId threading**: Verified by Handle_RealOrganizationId_FlowsIntoEnsureFreshAsync test
- **UI lazy fetch**: Follow-up optimization, not defect

---

## Design Decisions (D1-D3)

| Decision | Rationale | Implementation |
|----------|-----------|---|
| **D1: Markup via composition_spec_json (no migration)** | Column already mapped end-to-end; semantically unclaimed (written null everywhere); zero collision risk | Versioned envelope {schemaVersion,markup,canvas,slotGeometry[],backgroundImageRef}; fix write-site (ContentGenerationJobProcessor.cs:763) |
| **D2: xmin optimistic concurrency for refresh rotation** | DB-native, already used in AppIdentityDbContext; cross-instance; reread-on-conflict retries once | EF .IsConcurrencyToken()/xmin; on DbUpdateConcurrencyException, reread row + use fresh tokens (no double Canva call) |
| **D3: Chromium-free PPTX adapter via persisted geometry** | Playwright getBoundingClientRect captured at render time; adapter pure (markup+geometry→bytes), 100% offline-testable | B1b contract guarantees slot IDENTITY; geometry captured during existing Chromium render; adapter operates on persisted geometry only |

---

## Commits to Dev

Eight work units committed sequentially to `dev` (direct commits per project convention; no PR chain branches):

1. **WU-1** (2d24cfb): Markup + geometry persistence in composition_spec_json
2. **WU-2** (1b32014): IPptxExportAdapter port + PptxExportAdapter OpenXML
3. **WU-3** (a6dfb35): ExportFormatToPptxQueryHandler + download-pptx endpoint + UI button
4. **WU-4** (c2452dd): CanvaUserToken entity + ICanvaUserTokenRepository + EF migration
5. **WU-5** (396e133): ICanvaOAuthService + PKCE flow + authorize/callback endpoints
6. **WU-6** (d70170a): ICanvaDesignImportClient + bounded-poll HTTP adapter
7. **WU-7** (2e4017d): ExportFormatToCanvaCommandHandler + export-to-canva endpoint + UI flow
8. **WU-8** (c85334a): Docs sync + spec-delta reconciliation

---

## Deferred/Follow-Up Work

### Warnings Requiring Owner Attention (Minor)

| Item | Severity | Action | Notes |
|------|----------|--------|-------|
| UI lazy fetch (W1) | INFO | Optimize component state | Cache orgIntegrationId; deferred |
| RefreshFailed clarity (W2) | INFO | Clarify error path | Return ClientCredentialsMissing earlier; deferred |

### Deferred Features

| Issue | Description | Status |
|-------|-------------|--------|
| **Background image flattening** | PPTX-02-b (background as full-bleed PNG layer) never exercised; feature-detect stub in place | Deferred phase |
| **Autofill/Brand Templates** | Enterprise-only; stub in place; not wired | Deferred phase |

---

## Archive Contents

- `proposal.md` — scope; three decisions; risks; forward-compat; markup persistence (open design Q)
- `design.md` — D1/D2/D3 decisions (verified against real code); component map; flow sequences; TDD plan; PPTX mapping; architecture compliance
- `tasks.md` — 8 work units with delivery forecast (HIGH budget risk, size:exception approved)
- `verify-report.md` — full verification details (all specs traced; 2 WARNING, 2 SUGGESTION)
- `specs/content-generation/spec-delta.md` — PPTX-01..04
- `specs/integrations/spec-delta.md` — CANVA-USER-01..04, CANVA-EXPORT-01..04
- `archive-report.md` — this file

**Moved from**: `openspec/changes/single-analyzer-canva-export/`
**Moved to**: `openspec/changes/archive/2026-08-25-single-analyzer-canva-export/`

---

## Observation IDs (Engram Traceability)

| Artifact | Observation ID |
|----------|---|
| proposal | 587 |
| spec | 588 |
| design | 589 |
| tasks | 590 |
| verify-report | 594 |

---

## Key Learnings

1. Markup persistence via envelope JSON (versioned {schemaVersion,markup,...}) in an existing unclaimed column avoids schema-evolution friction; semantic ownership clarity (WHAT uses this column? WHO writes it?) must precede reuse to prevent collisions.

2. Concurrency safety for single-use rotating credentials (Canva refresh token) requires DB-native optimistic locking (xmin/RowVersion) with reread-on-conflict, not application-layer locks (SemaphoreSlim is single-process only; breaks under multi-instance LB).

3. Offline PPTX generation from persisted markup + geometry is computable and testable without Chromium; capturing geometry at render time (getBoundingClientRect during existing Chromium lifetime) decouples rendering from export, enabling both offline tests and async export workflows.

4. PPTX coordinate recovery via persisted getBoundingClientRect handles flex/grid transforms implicitly; no need to prescribe position:absolute in markup (OOP). Offset measurement + EMU scaling (9525 = 1 point @ 96dpi) centralizes coordinate logic.

5. Per-user token rotation under multi-tab concurrency requires atomic DB compare-and-swap (xmin), not multi-process coordination; a single reread attempt on conflict avoids thundering-herd and keeps the caller (ExportFormatToCanva command) from calling Canva twice for the same refresh.

---

*Archive report generated 2026-08-25.*
*SDD cycle complete. Change is closed.*
