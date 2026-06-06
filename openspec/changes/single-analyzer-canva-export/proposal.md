# Make every single-analyzer asset both downloadable (editable PPTX) and Canva-editable

Single-analyzer users today receive flat PNG/ZIP assets they cannot edit. This change (Fase B, B3) closes review items **#4 (editable download)** and **#5 (Export to Canva)** so that every generated format can be (a) downloaded as an **editable PowerPoint (PPTX)** and (b) **opened directly as an editable Canva design**. It builds on the B1b designer/renderer contract and the B2 Canva connector seed, adding the export seams, per-user OAuth, and the markup persistence the export path depends on.

## At a glance

| Field | Value |
|-------|-------|
| Change | `single-analyzer-canva-export` (Fase B / B3) |
| Review items | #4 editable download, #5 Export to Canva |
| Editable download | PPTX via **OpenXML SDK** (`DocumentFormat.OpenXml`, new Infrastructure dependency) |
| Canva export | **Canva Design Import API** (any paid plan), OAuth2 auth-code + **PKCE**, **per-user** tokens |
| Delivery | **Single PR** with **`size:exception`** (~900-1180 lines), navigable via work-unit commits |
| Forward-compat (B1b) | **Conditional PASS** — no designer/renderer change required |
| Mandatory hidden scope | Persist the validated designer markup (today it is dropped at write time) |

## Intent — why now

Review feedback #4 and #5 share one user expectation: an asset the platform generates should not be a dead end. The user must be able to take it further — refine wording, swap a logo, tweak layout — either offline in PowerPoint or online in Canva. B1b already produces structured, slot-tagged HTML that makes this feasible without re-generating content. B2 already seeded the Canva Connect connector (client_id / client_secret / scopes). B3 is the moment to turn that latent capability into two concrete, user-facing affordances.

**Success looks like:** from the single-analyzer Package tab, a user clicks "Download PPTX" and gets an editable `.pptx` whose text lives in real, editable text boxes; or clicks "Export to Canva", completes a one-time Canva connect, and lands in an editable Canva design (edit URL valid ~30 days).

## Scope

### In scope

- **Editable PPTX export + download** — build a `.pptx` from the stored designer markup: text slots become editable text boxes, logo/background become image layers, decorative CSS is flattened into a background image layer. New backend endpoint streams the file.
- **Designer-markup persistence (mandatory hidden scope)** — persist the guardrail-validated markup produced during render so the on-demand export endpoint can rebuild the asset deterministically (see [Mandatory hidden scope](#mandatory-hidden-scope-markup-persistence)).
- **Per-user Canva OAuth (PKCE) + token storage** — new per-user token entity, repository port, EF adapter, and migration; OAuth authorize + callback endpoints; PKCE flow with short-lived access tokens and single-use rotating refresh tokens.
- **Canva Design Import client** — a port + Infrastructure HTTP adapter that imports a PPTX into Canva and returns the edit URL + expiry.
- **Export use cases** — an export-to-PPTX query handler and an export-to-Canva command handler in Application.
- **API endpoints** — PPTX download, Canva OAuth authorize/callback, and export-to-Canva.
- **UI** — "Download PPTX" and "Export to Canva" buttons in the existing format/package row, plus the connect-to-Canva flow and a connection-status indicator (`apps/web/components/social/single-analyzer-client.tsx`, `apps/web/lib/api/content-generation.ts`).
- **Tests** — offline PPTX adapter tests, export use-case tests with fakes, OAuth flow tests.
- **Docs** — update `docs/`, `openspec/specs/`, and the relevant `.github/project-ai/instructions/*.md` in the same change (Documentation Sync rule).

### Out of scope (explicit)

- **Canva Autofill API + Brand Templates** — Enterprise-only. Deferred. Surface as a feature-detect path so a future Enterprise tier can light it up without rework.
- **Any change to the B1b designer/renderer markup contract** — already compatible (see below). No new `data-slot`/`data-layer` rules, no composer changes.
- **Non-Canva export targets** (Google Slides, Figma, etc.) — not part of #4/#5.
- **Org-level Canva tokens / reusing `OrganizationIntegrationCredential` for users** — the per-user dimension is intentionally a new seam, not a retrofit.

## Forward-compat with B1b — confirmed

The exploration's gate verdict is **Conditional PASS**, and the proposal relies on it: **no designer or renderer change is needed.**

- Every text node is tagged `data-slot="{name}"` (`OpusVisualDesigner.cs:166`, system-prompt Rule 4), so the PPTX builder can enumerate text boxes and read their copy.
- The background is isolated under `<div data-layer="background">` vs. `<div data-layer="content">` (`OpusVisualDesigner.cs:163`, Rule 3), so a text-less background image is extractable by re-rendering with the content layer hidden.
- `VisualGuardrailValidator` validates tags/palette/typography/overflow but does **not** strip `data-slot`/`data-layer`, so the markup that reaches the exporter is the markup that cleared guardrails.

The "Conditional" qualifier is real: the contract guarantees slot **identity**, not slot **geometry**. Mapping HTML positions to PPTX text-box coordinates is the highest implementation risk and is captured as an open design question, not a designer change.

## Mandatory hidden scope: markup persistence

The export endpoint runs **on demand**, after generation, so it must retrieve the previously-validated markup for a given analysis/format/client. Today it cannot:

- The validated markup exists only in memory during render: `ContentGenerationJobProcessor.cs:316` sets `DesignerMarkup = guarded.Value.Markup` on the in-memory spec.
- Every persistence path then drops it: `CompositionSpecJson: null` at `ContentGenerationJobProcessor.cs:722, 737, 763, 789`.

Important nuance confirmed against the code: a nullable `composition_spec_json` jsonb column **already exists** on the format-result row (`ContentGenerationFormatResultData.cs:21`, mapped in `AppIdentityDbContext.cs:724`, persisted through `EfContentGenerationFormatResultRepository.cs:31`). So the persistence gap is a **write-time** gap, not a missing column. Whether B3 reuses that existing column or adds a dedicated `designer_markup` column — and therefore whether a migration is strictly required — is a design decision flagged below, not a foregone conclusion. Either way, persisting the validated markup is **in-scope and non-negotiable**: without it, export is impossible without an expensive, non-deterministic re-design (a second Opus call).

## Approach — seams and hexagonal placement

The work decomposes into seams that respect the inward-pointing dependency rule (Domain ← Application ← Infrastructure/Api). Provider concerns (OpenXML, Canva HTTP, PKCE) stay behind ports.

| Seam | Port (Application) | Adapter (Infrastructure) | Use case (Application) | Edge (Api/Web) |
|------|--------------------|--------------------------|------------------------|----------------|
| PPTX export | `IPptxExportAdapter` | `PptxExportAdapter` (OpenXML) | `ExportFormatToPptxQuery` | `GET .../download-pptx` |
| Markup persistence | (existing repo contract) | EF format-result repo + optional migration | render pipeline write-site | — |
| Per-user tokens | `ICanvaUserTokenRepository` | EF adapter + entity + migration | — | — |
| Canva OAuth | `ICanvaOAuthService` | `CanvaOAuthService` (PKCE, HTTP) | — | OAuth authorize + callback endpoints |
| Canva import | `ICanvaDesignImportClient` | `CanvaDesignImportClient` (HTTP) | `ExportFormatToCanvaCommand` | `POST .../export-to-canva` |
| UI | — | — | — | `single-analyzer-client.tsx`, `content-generation.ts` |

**Narrative.** The PPTX seam is the cleanest: the adapter takes raw validated markup + parsed copy slots + dimensions and returns `byte[]` — no network, fully offline-testable. The export-to-PPTX query reads the persisted markup and invokes the adapter; its endpoint streams `application/vnd.openxmlformats-officedocument.presentationml.presentation`. The Canva path is heavier because it needs identity: a new per-user token store (the largest new seam — nothing like it exists today; `google-drive` only stores a single org-level refresh token), a PKCE OAuth service that exchanges and rotates tokens, and a Design Import client that uploads the PPTX and returns the edit URL. The export-to-Canva command composes the PPTX adapter output with the import client, guarded by a valid per-user token; the UI triggers the connect flow first, then opens the returned edit URL.

**No provider types leak inward.** OpenXML, Canva HTTP, and PKCE types live only in Infrastructure. Domain and Application see ports and DTOs only — verified that Domain currently holds no provider-specific types.

**Testability (Strict TDD).** The change is testable without live services: the PPTX adapter is exercised offline with `StubVisualDesigner.BuildStubMarkup()` (canonical `data-slot`/`data-layer` HTML), asserting slide count, dimensions, text-box copy, and background-layer presence via OpenXML structural assertions (no golden files). The Canva client sits behind a port with a fake for use-case tests; the live Infrastructure adapter gets an integration test that skips when credentials are absent (mirroring the existing `ChromiumAvailabilityCheck` pattern).

## Delivery: single PR with `size:exception`

The user has explicitly chosen a **single PR** over chained PRs, with full knowledge that the estimated scope is **~900-1180 changed lines** — roughly 2.5-3x the 400-line review budget. To keep that single PR reviewable, work is committed as **work-unit commits** following the natural seam order (PPTX adapter → background helper → export query+endpoint → token store → OAuth → import client → export command+endpoint → frontend), so a reviewer can step through one self-contained unit at a time. The `size:exception` label and this rationale are recorded so the budget breach is a deliberate, auditable decision rather than an oversight.

## Risks and mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **400-line budget breach** (~900-1180 lines in one PR) | Reviewer fatigue, missed defects | `size:exception` accepted by the user; work-unit commits make the diff navigable seam by seam; tests land with each unit. |
| **PPTX coordinate fragility** | Text boxes misplaced if Opus emits flex/grid instead of absolute positions | Open design question (below): prescribe `position:absolute` on slots vs. measure via Playwright `getBoundingClientRect`. Highest implementation risk. |
| **Canva refresh-token race** | Single-use rotating refresh token fails under concurrent (multi-tab) use | Open design question (below): serialize refresh per `(userId, integrationId)`. Edge case for solo use, real for multi-tab. |
| **Background re-render cost** | Extra Chromium pass per export to extract the background-only image | Re-render with `[data-layer="content"]` hidden, reusing `ChromiumBrowserPool`; export is on-demand and infrequent, so cost is acceptable. |
| **Markup-persistence migration** | Schema/migration change in a strict-gated repo | A nullable `composition_spec_json` column already exists; design phase decides reuse vs. dedicated column, minimizing or avoiding migration surface. |

## Open design questions (for the design phase)

These are **not** decided here. They are handed to `sdd-design`:

1. **Canva refresh-token concurrency** — how to serialize refresh-token use per `(userId, integrationId)` so single-use rotation survives concurrent requests (optimistic concurrency, advisory lock, or a dedicated refresh lock).
2. **PPTX coordinate recovery** — how to obtain reliable text-box coordinates: prescribe `position:absolute` on `data-slot` elements, or measure geometry via a Playwright `getBoundingClientRect` pass before building the PPTX.
3. **Markup persistence shape** — reuse the existing nullable `composition_spec_json` column or add a dedicated `designer_markup` column (and therefore whether an EF migration is required).

## Affected spec capabilities (for the spec phase)

| Capability | Why it is touched |
|------------|-------------------|
| `content-generation` | Editable PPTX export, designer-markup persistence, export use cases and endpoints, download affordance. |
| `integrations` | Per-user Canva OAuth (PKCE), per-user token storage, Canva Design Import client, export-to-Canva flow. |

`sdd-spec` should write delta specs for both capabilities; the per-user OAuth/token-storage seam is the larger spec surface.

## Next step

Proceed to `sdd-spec` and `sdd-design` (can run in parallel; both read this proposal).
