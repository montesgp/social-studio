# Design — single-analyzer-canva-export (Fase B / B3)

Hexagonal design for **#4 editable PPTX download** and **#5 Export to Canva**. Every new provider concern (OpenXML, Canva HTTP, OAuth) lives behind an Application port with an Infrastructure adapter; Domain and Application stay provider-free. This document fixes the three open design questions, the component map, two sequence flows, the token model, the PPTX mapping rules, and the TDD plan. It does **not** enumerate task steps — that is the `sdd-tasks` phase.

## The three decisions (read this first)

| # | Question | Decision | One-line rationale |
|---|----------|----------|--------------------|
| **D1** | Markup persistence shape | **Reuse `composition_spec_json`** to store the validated designer markup. No migration. | Column is mapped end-to-end but written `null` in 100% of paths and read by nobody — semantically unclaimed, zero collision, zero migration. |
| **D2** | Canva refresh-token race | **EF optimistic concurrency (`xmin` rowversion) on the per-user token row + single in-process retry on conflict.** | Single-use rotating refresh tokens need serialized rotation; `xmin` concurrency is already an established pattern in this DbContext; no new infra (no semaphore registry, no advisory-lock plumbing). |
| **D3** | PPTX coordinate recovery | **Persist per-`data-slot` geometry captured via Playwright `getBoundingClientRect` at render time, stored alongside the markup.** The PPTX adapter consumes geometry as data — it never touches Chromium. | Keeps the PPTX adapter 100% offline-testable; does not constrain the B1b designer to `position:absolute`; geometry is captured once during the render we already run. |

Each decision is expanded with rejected alternatives in [Decisions in depth](#decisions-in-depth).

## Component map — ports, adapters, layers

Dependency rule: **Domain ← Application ← Infrastructure / Api**. No `DocumentFormat.OpenXml`, Canva, or `HttpClient`/`Playwright` types may appear in Domain or Application.

### #4 — Editable PPTX (download)

| Element | Kind | Layer | Path (new unless noted) |
|---------|------|-------|--------------------------|
| `IPptxExportAdapter` | Port | Application | `Application/Abstractions/Social/ContentGeneration/IPptxExportAdapter.cs` |
| `PptxExportAdapter` | Adapter (OpenXML) | Infrastructure | `Infrastructure/Rendering/PptxExportAdapter.cs` |
| `IDesignerMarkupReader` (or reuse `IContentGenerationFormatResultRepository`) | Port | Application | reuse existing repo; see D1 |
| `ExportFormatToPptxQueryHandler` | Use case (query) | Application | `Application/Features/Social/ContentGeneration/Export/` |
| `GET …/formats/{formatKey}/download-pptx` | Endpoint | ApiService | `ApiService/Endpoints/Social/ContentGeneration/` (extend `analyzerGroup`) |
| `DocumentFormat.OpenXml` | NuGet dep | Infrastructure | add to `Incoders.Hive.Infrastructure.csproj` |
| Markup persistence write-fix | Modify | Application | `ContentGenerationJobProcessor.cs` — stop writing `CompositionSpecJson: null` after a successful designer+guardrail pass |

### Markup persistence (mandatory hidden scope, D1)

| Element | Change | Where |
|---------|--------|-------|
| `composition_spec_json` column | **Reused** — no schema change | `social.content_generation_format_results` |
| Persisted payload shape | New JSON envelope `{ markup, slotGeometry[], canvas, schemaVersion }` | written by `ContentGenerationJobProcessor.PersistRenderSuccessAsync` |
| Write site | Replace `CompositionSpecJson: null` at `ContentGenerationJobProcessor.cs:763` with the serialized envelope | success path only |

### #5 — Export to Canva (OAuth + Design Import)

| Element | Kind | Layer | Path |
|---------|------|-------|------|
| `CanvaUserToken` | Domain entity | Domain | `Domain/Integrations/CanvaUserToken.cs` (+ `CanvaUserTokenId`) |
| `ICanvaUserTokenRepository` | Port | Application | `Application/Abstractions/Integrations/ICanvaUserTokenRepository.cs` |
| `CanvaUserTokenData` + EF config | Entity + mapping | Infrastructure | `Infrastructure/Persistence/Postgres/Entities/` + `AppIdentityDbContext` |
| `EfCanvaUserTokenRepository` | Adapter (EF) | Infrastructure | `Infrastructure/Persistence/Postgres/Repositories/` |
| EF migration `Add_CanvaUserTokens` | Migration | Infrastructure | `…/Migrations/` — **the only new migration in B3** |
| `ICanvaOAuthService` | Port | Application | `Application/Abstractions/Integrations/ICanvaOAuthService.cs` |
| `CanvaOAuthService` | Adapter (PKCE + HTTP + rotation) | Infrastructure | `Infrastructure/Social/Canva/CanvaOAuthService.cs` |
| `ICanvaDesignImportClient` | Port | Application | `Application/Abstractions/Social/ContentGeneration/ICanvaDesignImportClient.cs` |
| `CanvaDesignImportClient` | Adapter (HTTP) | Infrastructure | `Infrastructure/Social/Canva/CanvaDesignImportClient.cs` |
| `ExportFormatToCanvaCommandHandler` | Use case (command) | Application | `Application/Features/Social/ContentGeneration/Export/` |
| `GET …/integrations/canva/authorize` + `…/canva/callback` | Endpoints (OAuth) | ApiService | `ApiService/Endpoints/Integrations/` (PKCE authorize + callback) |
| `POST …/formats/{formatKey}/export-to-canva` | Endpoint | ApiService | extend `analyzerGroup` |
| UI buttons + status | Modify | apps/web | `single-analyzer-client.tsx`, `lib/api/content-generation.ts` |

**Reuse, do not extend:** `OrganizationIntegrationCredential` stays org-level (Canva `client_id`/`client_secret`/`scopes` resolved via `IIntegrationResolver.ResolveProviderAsync(orgId, "canva")`). Per-user access/refresh tokens are a **separate** entity keyed by `(userId, organizationIntegrationId)` — the org credential has no user dimension.

## Sequence flow A — Download editable PPTX (on-demand)

```
UI (Download PPTX btn)
  → GET /api/social/single-analyzer/{analysisId}/formats/{formatKey}/download-pptx?clientId=…
      → ExportFormatToPptxQueryHandler
          → IContentGenerationFormatResultRepository: resolve (job, formatKey) → row
          → read composition_spec_json → deserialize { markup, slotGeometry, canvas }
          → IPptxExportAdapter.BuildAsync(markup, slotGeometry, canvas, copySlots)
              → OpenXML: 1 slide per asset; data-slot → text box (text+font+color+geometry);
                data-layer=background → flattened image layer
          → returns byte[] (application/vnd.openxmlformats-…presentationml.presentation)
      → stream file (Content-Disposition: attachment; filename={format}.pptx)
```

- **No Chromium, no network** in this flow. Geometry and the background image reference were captured at original render time (Flow A consumes persisted data).
- If `composition_spec_json` is `null` (asset generated before B3, or a raster fallback with no markup): return a typed `not-exportable` problem (HTTP 409/422) — never trigger a re-design from the download path.

## Sequence flow B — Export to Canva (on-demand, OAuth-gated)

```
UI (Export to Canva btn)
  → POST …/formats/{formatKey}/export-to-canva?clientId=…
      → ExportFormatToCanvaCommandHandler
          → ICanvaUserTokenRepository.GetAsync(userId, orgIntegrationId)
              ├─ missing/revoked → return needs-authorization (with authorize URL)
              │     UI redirects → GET …/integrations/canva/authorize
              │         → CanvaOAuthService: build PKCE challenge, store verifier+state, 302 to Canva
              │         ← GET …/integrations/canva/callback?code&state
              │             → CanvaOAuthService.ExchangeCodeAsync → access+refresh+expiry
              │             → ICanvaUserTokenRepository.UpsertAsync (encrypted at rest)
              │         (UI re-invokes export)
              └─ present but expired → CanvaOAuthService.EnsureFreshAsync (see D2 rotation)
          → build PPTX in memory via IPptxExportAdapter (same as Flow A)
          → ICanvaDesignImportClient.ImportAsync(pptxBytes, accessToken)
              → POST design-import job → poll job until success/failure (bounded)
              → returns { designEditUrl, expiresAt }
      → 200 { editUrl, expiresAt }
  → UI opens editUrl in a new tab
```

- **OAuth is per-user.** The authorize/callback endpoints bind the token to the authenticated `userId` + the org's Canva `organizationIntegrationId`.
- **Design Import is async on Canva's side** — the client submits, then polls the import job with a bounded retry budget; the adapter surfaces a typed timeout error rather than hanging.

## Token storage model

Entity `CanvaUserToken` — table `platform.canva_user_tokens` (mirrors where `organization_integrations` live).

| Field | Type | Notes |
|-------|------|-------|
| `Id` | guid (PK) | |
| `UserId` | guid | authenticated user; part of uniqueness |
| `OrganizationIntegrationId` | guid (FK → `organization_integrations`) | ties the user token to the org's Canva connection |
| `EncryptedAccessToken` | text | protected via `IIntegrationSecretProtector` (D2/security) |
| `EncryptedRefreshToken` | text | single-use rotating; replaced on every refresh |
| `AccessTokenExpiresAtUtc` | timestamptz | drives proactive refresh |
| `Scopes` | text | granted scopes snapshot |
| `RowVersion` (`xmin`) | uint, concurrency token | **`.IsConcurrencyToken()` / Npgsql `xmin`** — serializes refresh rotation (D2) |
| `CreatedAtUtc` / `UpdatedAtUtc` | timestamptz | |

- **Unique index:** `(UserId, OrganizationIntegrationId)` — one Canva token set per user per org connection.
- **Encryption at rest:** reuse the existing `IIntegrationSecretProtector` (`DataProtectionIntegrationSecretProtector`, purpose `incoders.hive.integrations.credentials.v1`). Tokens are encrypted before persistence and only decrypted inside the OAuth service / import client. They are NEVER returned to the UI or logged. This mirrors exactly how `organization_integration_credentials.encrypted_value` is handled today.
- **Refresh strategy (D2):** see below.

## Decisions in depth

### D1 — Reuse `composition_spec_json` (no migration)

**Verified facts (real code):**
- Column mapped end-to-end: entity `ContentGenerationFormatResultData.cs:21`, EF config `AppIdentityDbContext.cs:724` (`jsonb`, nullable), Application record `IContentGenerationFormatResultRepository.cs:26`, repo round-trip `EfContentGenerationFormatResultRepository.cs:31,61,76`.
- Written `null` in **every** production path: `ContentGenerationJobProcessor.cs:722,737,763,789`. Also `null` in every test that constructs the record. **Zero non-null writers exist.**
- **Zero readers** depend on its content (`Grep CompositionSpecJson` → only mappers, migration snapshots, and `: null` constructions).

**Decision:** Store a versioned JSON envelope in `composition_spec_json`:
`{ "schemaVersion": 1, "markup": "<html…>", "canvas": {w,h,dpi}, "slotGeometry": [ {slot, x, y, w, h, fontFamily, fontSizePx, color, textAlign} ], "backgroundImageRef": "asset-hash://…" }`.
Fix the write site so the **success** persist path serializes this envelope instead of `null`.

**Rejected:**
- *Dedicated `designer_markup` column + migration* — adds a migration and a second nullable jsonb column duplicating the role of an already-mapped, unused one. No benefit; more surface.
- *On-demand re-design (`IVisualDesigner.ComposeAsync` again)* — expensive (another Opus call) and **non-deterministic** (different markup each time → geometry drift, broken Canva edit-URL reproducibility). Rejected by the proposal already.

**Risk + mitigation:** column name (`composition_spec_json`) now slightly under-describes its content. Mitigated by `schemaVersion` in the envelope and a one-line entity doc-comment; a future rename is a cosmetic migration, not a data migration.

### D2 — `xmin` optimistic concurrency + single retry for refresh rotation

**Problem:** Canva issues single-use rotating refresh tokens. Two concurrent refreshes (multi-tab) read the same refresh token; the first rotation invalidates it, the second fails permanently.

**Decision:** Serialize rotation through the database row, not the process:
1. `EnsureFreshAsync` reads the token row (captures `xmin`).
2. If still valid → use it. If expired → call Canva token endpoint, then `UpsertAsync` with the captured `xmin` as concurrency token.
3. On `DbUpdateConcurrencyException` → **another request already rotated**: re-read the row (now holds the fresh pair) and use that; do not call Canva again. Single bounded retry.

**Why this stack:** `.IsConcurrencyToken()` / Npgsql `xmin` is **already used** in this DbContext (`AppIdentityDbContextModelSnapshot.cs:33,69`). It is correct across processes/instances (the lock is the DB row), needs no new infrastructure, and the conflict-then-reread pattern exactly matches single-use rotation semantics.

**Rejected:**
- *Application-level `SemaphoreSlim` keyed by `(userId, integrationId)`* — only serializes within ONE process. With multiple ApiService instances behind a load balancer it provides **no** guarantee. Adds a lock registry to maintain/evict.
- *Postgres advisory lock (`pg_advisory_xact_lock`)* — works cross-instance but introduces raw-SQL/transaction-scope plumbing foreign to the current EF-only persistence; heavier than the row-version approach already proven here.

### D3 — Persist Playwright-measured geometry; PPTX adapter stays Chromium-free

**Verified facts (real code):**
- The B1b designer contract guarantees slot **identity** (`data-slot`, `OpusVisualDesigner.cs:167`) and a `background`/`content` layer split (`OpusVisualDesigner.cs:165`), but **not** geometry. Stub markup (`IVisualDesignerContractTests` `BuildStubMarkup`, renderer test `BuildSampleHtml`) carries `width/height/color/font-family` inline but **no `position`/`left`/`top`** — confirming geometry is not guaranteed.
- The success render ALREADY runs Chromium (`HtmlChromiumContentRenderer.cs:111-136`): it sets HTML content and screenshots. Adding one `page.$$eval('[data-slot]', …getBoundingClientRect…)` call during that existing page lifetime is cheap and side-effect-free.

**Decision:** During the existing render pass, capture per-`data-slot` `getBoundingClientRect` (x, y, width, height) plus computed `fontFamily`, `fontSizePx`, `color`, `textAlign`, and persist that array in the `composition_spec_json` envelope (D1). The **PPTX adapter takes geometry + markup as plain inputs** and emits OpenXML text boxes at EMU-converted coordinates. The adapter never imports Playwright.

**Rejected:**
- *Prescribe `position:absolute` in the designer/template contract* — forces a B1b designer/renderer contract change (explicitly OUT of scope per the proposal), constrains Opus's layout freedom (flex/grid produce better visuals), and is fragile if a future model ignores the instruction.
- *Measure live via Playwright inside the PPTX adapter at download time* — makes the PPTX adapter Chromium-dependent, breaking the offline unit-test requirement and adding a second render on every download.

**Forward-compat impact (B1b):** none — no change to designer or renderer markup contract; geometry capture is an additive read on the page we already render. **Offline testability:** preserved — the PPTX adapter is pure (markup + geometry → bytes).

## PPTX mapping rules

| Source | OpenXML target | Mapping |
|--------|----------------|---------|
| One rendered asset | One `SlidePart` | Carousel → N slides (one per `slide_{n}.png`); flyer → 1 slide |
| Slide size | `SlideSize` (EMU) | From `canvas {w,h}`; px→EMU via `914400 / dpi` (default 96 → 9525 EMU/px) |
| `[data-slot]` element | Editable text box (`Shape` + `TextBody`) | Text = inner text; `x,y,w,h` = persisted geometry → EMU; `fontFamily`, `fontSizePx`, `color`, `textAlign` → run properties |
| `[data-layer="background"]` | Single flattened image layer (full-bleed `Picture`) | Background-only PNG captured by re-rendering with `[data-layer="content"]` hidden, persisted at render time as `backgroundImageRef`; embedded as `ImagePart` behind the text boxes |
| Per-format canvas dimensions | Slide dimensions | Flyer 1080×1920; carousel slide 1080×1080 (read from `canvas`, not hard-coded — source of truth is the persisted envelope) |

- Text boxes are **editable** (real `TextBody` runs, not rasterized) — that is the #4 acceptance criterion.
- The background is intentionally a **flattened image** (decorative CSS/gradient cannot round-trip to OpenXML shapes); the editable surface is the text, matching the user expectation "refine wording, swap copy."

## TDD plan (Strict TDD active — `dotnet test`)

| Unit | Offline? | Test seam | Asserts |
|------|----------|-----------|---------|
| `PptxExportAdapter` | **Yes** | Local stub markup helper modeled on `StubVisualDesigner.BuildStubMarkup()` + a synthetic geometry array | slide count = asset count; slide dims = canvas; one text box per `data-slot` with expected copy/font/color; background image part present. **Structural OpenXML assertions, no golden files.** |
| `ExportFormatToPptxQueryHandler` | **Yes** | Fake `IContentGenerationFormatResultRepository` + fake `IPptxExportAdapter` | resolves row by (job, formatKey); deserializes envelope; null `composition_spec_json` → `not-exportable` typed error |
| `CanvaUserToken` repo + `xmin` rotation (D2) | **Yes** | In-memory/SQLite-style fake of `ICanvaUserTokenRepository`; simulate concurrency conflict | conflict → re-read → uses already-rotated pair; no double Canva call |
| `CanvaOAuthService` PKCE builder | **Yes** | Unit test of authorize-URL + verifier/challenge; fake `HttpMessageHandler` for exchange | challenge derives from verifier; state round-trips; token stored encrypted |
| `ExportFormatToCanvaCommandHandler` | **Yes** | Fakes for token repo, OAuth service, import client, PPTX adapter | missing token → needs-authorization; happy path → returns editUrl |
| `CanvaDesignImportClient` (real HTTP) | **No — integration guard** | Skip when Canva credentials absent (mirror `ChromiumAvailabilityCheck` / `[Fact(Skip)]` runtime-skip pattern) | live import job submit + poll |
| Background-only render + geometry capture | **No — Chromium guard** | Runtime-skip when Chromium absent (same pattern as `HtmlChromiumContentRendererTests` browser-gated facts) | geometry array non-empty; background PNG produced |
| `CanvaConnectorFlowTests` (existing, B2) | Yes | Already green | unaffected — per-user token store is additive; org-level config flow unchanged |

**Seam principle:** every provider boundary (OpenXML, Canva HTTP, Chromium) is behind a port so use-case tests run with fakes; only the thin Infrastructure adapter that actually talks to the provider is integration-guarded.

## Architecture-gate compliance

- `IPptxExportAdapter`, `ICanvaOAuthService`, `ICanvaDesignImportClient`, `ICanvaUserTokenRepository` are pure Application interfaces — parameters/returns are framework-free (`byte[]`, records, `Result<T>`). Mirrors the `IVisualDesignerContractTests.Port_HasNoInfrastructureTypes` assertion (no `Infrastructure|Persistence|EfCore|Npgsql|AspNetCore|Playwright` in signatures).
- `DocumentFormat.OpenXml` referenced **only** in `Incoders.Hive.Infrastructure`. Canva `HttpClient` and Playwright geometry capture live **only** in Infrastructure.
- `CanvaUserToken` Domain entity holds NO provider types (plain fields + value-object id), consistent with `OrganizationIntegrationCredential`.
- Run `pwsh -File scripts/gates/Run-Gates.ps1` before completion (architecture + best-practice gates).

## Risks and mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `composition_spec_json` semantic drift (D1 reuse) | Low | `schemaVersion` envelope + doc-comment; cosmetic rename deferrable |
| Geometry capture misses slots if Opus emits nested/transformed slots | Medium | Capture computed rect (post-layout) — handles flex/grid/transform; slots without rect fall back to a deterministic stacked layout, logged |
| Canva Design Import job latency/timeout | Medium | Bounded poll budget + typed timeout error; UI shows retry affordance |
| Single-use refresh race under multi-tab (D2) | Medium→Low | `xmin` concurrency + reread-on-conflict; integration-guarded test |
| PR exceeds 400-line budget (~900-1180 lines) | Accepted | `size:exception` (user-approved); work-unit commits in seam order make the diff navigable |
| OpenXML new dependency surface | Low | Microsoft first-party SDK; Infrastructure-only; structural tests pin behavior |

## Spec impact noticed (for `sdd-spec` / verify)

- **content-generation capability:** add requirements for (a) editable PPTX export query + endpoint, (b) designer-markup + geometry persistence in `composition_spec_json`, (c) the write-site fix that previously dropped markup, (d) the `not-exportable` behavior when markup is absent.
- **integrations capability:** add requirements for (a) per-user Canva OAuth (PKCE) authorize/callback, (b) per-user encrypted token storage with `xmin`-serialized refresh rotation, (c) Canva Design Import client, (d) export-to-canva command + endpoint returning an edit URL.
- The B1b designer/renderer markup contract is **unchanged** — no spec delta there; the geometry capture is an additive Infrastructure read.

## Next step

`sdd-tasks` — slice these components into work units in seam order (PPTX adapter + persistence + export query → per-user token store + OAuth → Design Import client + export command + UI). Tasks owns the checklist; this design owns the HOW.
