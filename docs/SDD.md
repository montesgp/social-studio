# SDD - Social Studio

## 1. System Style
Workflow-oriented, event-aware, AI-native SaaS module inside a monorepo orchestrated by .NET Aspire.

## 2. Architecture
- Frontend: Next.js app.
- Backend API: ASP.NET Core using hexagonal architecture.
- AppHost: .NET Aspire orchestration.
- Infrastructure: Supabase initial adapter; other providers must be replaceable.
- External providers: content data, AI models, design, publishing, testing.

## 3. Monorepo Layout
```txt
apps/
  web/                          # Next.js frontend
SocialStudio.AppHost/          # .NET Aspire orchestrator
SocialStudio.ApiService/       # ASP.NET Core API (transport / HTTP mapping)
SocialStudio.Application/      # Use cases, ports, commands/queries
SocialStudio.Domain/           # Entities, value objects, domain events
SocialStudio.Infrastructure/   # Adapters (Supabase, AI providers, etc.)
SocialStudio.ServiceDefaults/  # Shared Aspire service defaults
SocialStudio.Tests/            # Unit + integration tests
docs/
openspec/
```

> Projects sit at the repo root (flat layout), not under `src/`/`tests/`. The solution file is `SocialStudio.slnx`.

## 4. Backend Hexagonal Layers
- Domain: workflow entities, value objects, domain events.
- Application: use cases, ports, commands, queries.
- Infrastructure: Supabase, AI providers, data providers, Canva, Blotato, TestSprite integrations.
- Api: endpoints, auth, HTTP mapping.

## 5. Bounded Contexts
- Identity & Tenancy
- Client Management
- Social Account Management
- Content Research
- Insight Mapping
- AI Idea Generation
- AI Evaluation
- Asset/Canvas Creation
- Publishing
- Analytics & Feedback
- Provider Configuration
- Integrations (platform catalog + organization credentials)

## 6. Workflow Events
- ClientSelected
- ContentResearchRequested
- ReferenceContentRetrieved
- ContentMapped
- IdeaGenerationRequested
- IdeasGenerated
- IdeaEvaluationRequested
- IdeaEvaluated
- CanvasDraftCreated
- PublicationRequested
- PublicationPublished
- MetricsSnapshotCaptured

## 7. AI Orchestration
Model routing is configurable. Use cases call application ports such as `IAiIdeaGenerator`, `IAiIdeaEvaluator`, `ITranscriptionMapper`. Infrastructure resolves provider/model.

## 8. Persistence
Supabase is initial infrastructure provider. Domain and Application must not depend on Supabase SDKs. RLS must support organization/client boundaries.

Client-managed image assets (for example brand manual logos and backgrounds) are uploaded through backend endpoints and stored behind a storage adapter abstraction. Persisted business records keep the resulting URL reference, never raw file payloads.

API startup applies pending EF Core migrations before hosted background workers process schema-dependent jobs. Developer fast-startup shortcuts may skip data seeding, but must not skip migrations because content generation workers rely on tables such as `social.content_generation_format_assets`.

Persistence schemas:
- `identity` for authentication/authorization data.
- `platform` for organizations, memberships, system settings and shared provider/model catalogs.
- `social` for Social Studio / Social Studio module data.

Organization-owned product tables must include `organization_id`. Shared system catalogs such as AI providers and AI models are platform-owned; organization-specific credentials, availability and preferences reference those platform records.

Integrations persistence model:
- `platform.integration_providers` is global catalog data (provider metadata, frontend icon keys, categories, credential schema, supported connection modes).
- `platform.organization_integrations` is organization-scoped provider state (mode/status/default model flags).
- `platform.organization_integration_credentials` stores encrypted organization credential values.
- Product modules such as Social consume integration resolution through an abstraction (`IIntegrationResolver`) instead of reading provider credentials or appsettings directly.
- Social client hard-delete is an explicit MVP cleanup exception reserved for platform sysadmins. It is implemented as an application use case plus infrastructure cascade cleanup; normal client lifecycle remains archive/reactivate for organization admins.
- Organization hard-delete is an explicit MVP cleanup exception reserved for platform sysadmins. It removes the organization root, organization-scoped users (excluding platform sysadmins), memberships, workspace module assignments, settings, integrations, clients and all Social child chains in cascade.
- Client cleanup must remove client-scoped generated storage artifacts through storage ports after database rows are committed, so Domain/Application do not depend on Supabase or local filesystem details.
- Organization cleanup must remove organization-scoped generated packages, durable generated assets and uploaded client asset folders through storage ports after database rows are committed; storage failures are logged as orphan cleanup warnings, not transaction failures.

Social network catalog model:
- Platform social networks store an `icon_key` selected from the frontend social-network icon allowlist, not arbitrary icon text, so admin users can preview the assigned icon and downstream UI can render it consistently.

Platform configuration model:
- Email templates for authentication/system notifications are platform-owned settings stored as typed JSON in `platform.system_settings` under stable `email_templates.*` keys. Sysadmins edit HTML bodies with preview; runtime mail delivery will resolve the same keys.
- API startup seeds branded default templates for confirmation, magic-link, password reset, and invitation flows, without overwriting already configured templates.
- Platform integrations that are not organization/content integrations, such as SMTP email delivery and future WhatsApp messaging, are platform-owned settings stored under stable `platform_integrations.*` keys. Secret values must be protected before persistence and returned to UI only as configured/not-configured flags.
- Workspace module availability is configured by sysadmins per `(organization_id, user_id)` mapping in `platform.organization_user_modules`, with explicit `sort_order` for selector ordering. Organization-scoped users can belong to only one organization at a time; changing the assigned organization removes previous memberships and module assignments. Platform sysadmins have no organization membership and see the full module catalog ordered by module creation date descending.

## 9. Frontend Design
Use Next.js App Router. Build a workflow dashboard around client selection, content research, insight mapping, idea generation/evaluation and publication pipeline. Use shadcn/ui and Radix primitives for accessible components.

## 10. Testing
- Unit tests for domain/application rules.
- Integration tests for provider adapters.
- API contract tests for frontend/backend sync.
- Optional TestSprite validation for E2E workflow journeys.

---

## 11. Content Generation — Format Taxonomy (change: content-format-taxonomy)

> Added by `content-format-taxonomy`. Commits: a2f82fa (domain), b1133f0 (backend), 7edda4b (frontend), plus S5 extensibility proof + doc sync.

The content generation pipeline uses a **three-axis taxonomy** (`Surface × Layout × Medium`) backed by a single immutable `FormatCatalog` in the Domain layer. Every downstream consumer (API whitelist, job processor, visual composer, PPTX exporter, frontend picker) derives from the catalog instead of maintaining hand-edited literal lists.

### Axis Types (`Domain/Social/ContentFormats/`)

| Type | Values | Notes |
|---|---|---|
| `Surface` | Script, Story, Feed | Top-level content intent |
| `Layout` | Single, Carousel | Null for Script (no layout axis) |
| `Medium` | Image, Video, Text | Video representable in v1; no video generator ships yet |

### v1 Curated Catalog (5 deliverable entries + 4 auxiliaries)

| FormatKey | Surface | Layout | Medium | Channel | IsMultiAsset | StorageFolder |
|---|---|---|---|---|---|---|
| reel-script | Script | — | Text | instagram/reel | false | script |
| story-single | Story | Single | Image | instagram/story | false | story-single |
| story-carousel | Story | Carousel | Image | instagram/story | true | story-carousel |
| feed-single | Feed | Single | Image | instagram/feed | false | feed-single |
| feed-carousel | Feed | Carousel | Image | instagram/feed | true | feed-carousel |

Auxiliaries (content-idea, cta-variation, caption, hook): Medium.Text, not shown in the UI picker, included in the API whitelist.

### Key Design Decisions

| Decision | Choice |
|---|---|
| Catalog placement | `FormatCatalog` static class in Domain; Application delegates to it via `ContentFormatCatalog` |
| Alias strategy | `"carousel"` (legacy stored key) is a READ-ONLY alias resolved only via `ResolveForAssembly`; API writes reject it |
| API whitelist | `KnownFormatKeys` derived from `FormatCatalog.KnownKeys()` — no hand-maintained list |
| Storage paths | `def.StorageFolder` drives all paths; new keys write to canonical prefixes (story-single/, story-carousel/, feed-single/, feed-carousel/) |
| Frontend picker | 3 root buttons (Script / Story / Feed) + Single↔Carousel toggle (Story+Feed only) |
| Catalog endpoint | `GET /api/social/content-generation/formats` returns `FormatCatalogEntryDto` list |
| No migration | Format taxonomy is C#-only; `format_key` column remains varchar; legacy rows unchanged |
| Extensibility | Adding a video variant requires one `FormatDefinition` row + generator/composer wiring; axis types and derive-normalize layer are untouched |

### Backward Compatibility

Legacy `format_key = 'carousel'` rows (written before this change) continue to assemble correctly: `NormalizePackageFormatKey` routes them through `ResolveForAssembly("carousel")` to the multi-asset writer branch, producing output under `{client}/carousel/` unchanged.

New generations always use canonical keys (`story-single`, `story-carousel`, `feed-single`, `feed-carousel`) and write to non-colliding prefixes. Legacy `story` and `feed-post` write requests are normalized to `story-single` and `feed-single` for compatibility, but the UI emits only canonical keys. Legacy stored `story`, `feed-post`, and `carousel` rows remain read-compatible; `carousel` remains an assemble-path alias only, not a valid write key.

### Attribution guardrail

Generation prompts treat source-origin identity as inspiration/metadata only. Visual composition resolves final signature text from deterministic target client/brand fields first and rejects referent/competitor/source signatures.

---

## 12. Social Module — Profile Analysis (Mundo 2)

> Added by `single-analyzer-profile-analysis`. ADR: [`docs/architecture/adr/0003-profile-analysis-model.md`](architecture/adr/0003-profile-analysis-model.md).

The **Profile Analyzer** is a second, independent analyzer mode in the Social module. A community manager can analyse an entire Instagram profile — by selecting a client's own Instagram account or pasting a profile URL — and receive an expert AI marketing analysis, view a charted dashboard of account metrics, and optionally impute detected fields (brand colors, fonts, voice tone, content pillars, KPIs, etc.) into the client's Brand Manual, Content Plan, and General Strategy.

### Bounded Context Isolation

`ProfileAnalysis` is a **new first-class aggregate** in `Domain/Social/ProfileAnalysis/` with its own table (`social.profile_analyses`) and EF migration. It MUST NOT share domain types with `Domain/Social/SingleAnalyzer/` (PA-REG-01).

### Pipeline Summary

```
Source (Client+IG own-account | URL paste)
  → AnalyzeProfile (202, record Requested)
  → ScrapingProfile  → Apify default actor  → [backup on fail]
  → ProfileScraped[Partial]  (owner-filter; derive stats)
  → Analyzing  (org default AI, profile-analyzer.analysis.v1)
  → Analyzed | Failed
  → Dashboard (stats + raw posts + comparison-if-client)
  → Impute (per-field, confirmed) → {Brand Manual, Content Plan, General Strategy}
```

See `docs/architecture/diagrams/profile-analysis-flow.md` for the full diagram.

### Key Decisions

| Decision | Choice |
|---|---|
| Aggregate | `ProfileAnalysis` — new namespace, new table, no EF navigations to `SocialClientData` |
| Scrape port | `IProfileScrapeResolver` — does NOT route through `ISocialContentExtractor` |
| Actor routing | `variantKey="profile"` in `actor_mappings_json` — isolated from single-video path |
| Fallback | Automatic default→backup in the same pipeline run (profile variant only) |
| AI flow | `profile-analyzer.analysis.v1` — distinct from `single-analyzer.analysis.v1` |
| Impute | Reuses existing BrandManual/ContentPlan/GeneralStrategy write commands — idempotent |
| Frontend | Lazy-loaded `ProfileAnalyzerModule` — recharts isolated to this module |
| PDF export | Profile dashboard export captures the visible dashboard sections as screen-faithful images before composing the PDF, avoiding browser print reflow that can reorder charts or split cards |

### Operator Setup

See `docs/operators/profile-analysis-setup.md` for the required `actor_mappings_json` entries.

---

## 13. Content Bank Planner (change: content-bank-planner)

> ADR: [`docs/architecture/adr/0005-publication-axis-and-two-level-credentials.md`](architecture/adr/0005-publication-axis-and-two-level-credentials.md). Builds on Content Studio (ADR 0004, §12 above).

Publishing was a stub until this change. `content-bank-planner` adds a client-scoped **Content Bank** (cross-set, cross-origin read seam over reviewable Content Studio items) and a **Planner/Published** workspace to publish approved items to Instagram, first slice = publish-now only.

### Two-Axis Model

`Domain/Social/Publications/Publication` is a **new, independent aggregate** — its own table (`social.publications`, owned `social.publication_attempts` collection) and its own guarded `PublicationStatusTransitions` (`Scheduled→Publishing→{Published|PublishFailed}`, `PublishFailed→{Publishing,Cancelled}`, `Scheduled→Cancelled`). It references `ContentItem` by a **soft `Guid`**, never mutates `ReviewState`, and MUST NOT share domain types with `Domain/Social/ContentStudio/` (orthogonality asserted by a dedicated architecture-style test). There is no uniqueness constraint on `(content_item_id, destination)` — republish and multi-destination each create a new independent `Publication`.

### Two-Level Credential Model

- **Org level** (existing, unchanged): `OrganizationIntegration`, resolved via `IIntegrationResolver`.
- **Client level** (new): `Domain/Social/ClientAccountConnections/ClientAccountConnection` — one row per `(organization_id, client_id, social_network_id)`, holding `ExternalAccountId` + an encrypted token (`IIntegrationSecretProtector`) + connection health (`Connected/Failed/Disabled`). Never returns the token in any response DTO.

`PublicationDestinationResolver` composes both levels into a `ResolvedPublicationRequest` that crosses the `IPublicationConnector` port (`NetworkKey`, `PublishAsync`). `InstagramGraphPublicationConnector` is the only v1 adapter; the port is channel-agnostic so future networks (Meta variants, TikTok, YouTube, X) slot in without touching Domain/Application.

### Content Bank Query

`ListClientContentBankQuery` returns every reviewable `ContentItem` for a client across content sets and generator origins, with attached `PublicationSummaryDto[]` per item, optionally filtered by date range and `ReviewState`. Materialization is **lazy-on-Bank-load**: `EnsureClientContentMaterializedCommand` re-invokes the existing idempotent `MaterializeAnalysisCommand` for any completed-but-unmaterialized analysis before the query runs — no background job in v1.

### Frontend — `/publications`

`PublicationsWorkspace` (client selector + date-range/state filters) renders three tabs: **Bank** (`ContentBankGrid`, publish-now action, opens the existing `content-studio-panel.tsx` unmodified), **Planner** (`PublicationPlannerBoard`, a pure projection over Bank items + publications into `Ready/Scheduled/Publishing/Published/Failed` lanes), **Published** (`PublishedList`, recency-sorted with external links). `ClientAccountConnectionsPanel` is the org-admin-gated settings surface for connecting/rotating/testing Instagram credentials (write-only token input, never pre-filled).

### Key Decisions

| Decision | Choice |
|---|---|
| Publication lifecycle | Separate `Publication` aggregate, orthogonal to `ReviewState` — never mutates it |
| Credential model | Two levels: org `OrganizationIntegration` + per-client `ClientAccountConnection` |
| Republish semantics | Allowed — no `(item, destination)` uniqueness; each publish action is a new `Publication` |
| Scheduling (v1) | Modeled (`Scheduled` + `ScheduledForUtc`) but not dispatched — no background worker yet; hidden from v1 UI |
| Materialization | Lazy, on Bank load, reusing existing idempotent `MaterializeAnalysisCommand` |
| Handoff contract | Pull-by-id (read `ContentItem` fresh at publish time) supersedes the earlier unmerged push-based `IPublicationHandoff`/`PublicationHandoffPayload` design (ADR 0005 §16) |
| Credential management access | Org `admin` role only |

### Deferred Roadmap

Scheduler/dispatcher worker, additional network connectors, background (non-lazy) materialization, Blotato multi-network consolidation, post-publish metrics/insights — see PRD §12 and `openspec/changes/content-bank-planner/state.yaml`.
