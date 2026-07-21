# Exploration — Content Bank Planner

> SDD phase: `explore`. A "content planner" tool for the Hive Social module built as a
> read/query + scheduling + publication-orchestration + UI layer on top of the existing
> `Social/ContentStudio` bounded context. Two orthogonal state axes: editorial (`ReviewState`,
> reused) and publication (new `Publication` entity, one per item+destination).

## Current State (assumptions traced to concrete evidence)

### 1. Single-analyzer → ContentItem wiring — CONFIRMED, with a nuance
`MaterializeAnalysisCommand` (`Incoders.Hive.Application/Features/Social/ContentStudio/Materialize/MaterializeAnalysisCommand.cs`)
is wired end-to-end: `POST /api/social/content-studio/analyses/{analysisId}/materialize`
(`Incoders.Hive.ApiService/Endpoints/Social/MapContentStudioEndpoints.cs:163-173`) →
`materializeAnalysis()` (`apps/web/lib/api/content-studio.ts:123`) → `ContentStudioPanel`
(`apps/web/components/social/content-studio-panel.tsx:411-442`). **But it only fires when the user
manually opens the Content Studio panel** (`single-analyzer-client.tsx:3375`, `studioOpen` gate).
There is no automatic/background materialization on generation completion.

**GAP:** an analysis whose owner never opens the Studio panel never becomes a `ContentItem`/`ContentSet`,
so it would not surface in a client-wide Content Bank unless the Bank (or a background job) triggers
materialization for every analysis with completed formats.

### 2. Existing ContentStudio queries — CONFIRMED GAP
`IContentItemRepository` (`Incoders.Hive.Application/Abstractions/Social/ContentStudio/IContentItemRepository.cs`)
exposes only `GetByIdAsync`, `ListByRunAsync`, `ListBySetAsync` — no client-wide / paged / date-range /
state-filter method. `ListContentItemsQuery` is SetId-scoped only. `ContentItem` carries `ClientId`
directly (`Domain/Social/ContentStudio/ContentItem.cs:51`), so a client-wide cross-set query is a new
repository method + query handler — additive, not a redesign. Nothing today aggregates "all items for
client X across all sets/origins" — the core missing capability for the Bank tab.

### 3. `/publications` route — CONFIRMED EMPTY
`apps/web/app/(dashboard)/publications/page.tsx` is a bare `PlaceholderPage`. No publication UI exists.
`content-studio-panel.tsx` (edit text/caption/visual, approve/reject/discard/duplicate, spawn variant,
plan-link, stage) is the right reuse target for "open any card for edits" from the Bank.

### 4. Connectors / providers model — PARTIALLY CONFIRMED, IMPORTANT NUANCE
A general-purpose integration model already exists: `OrganizationIntegration`
(`Domain/Integrations/OrganizationIntegration.cs`) + `IntegrationProvider` catalog + `IIntegrationResolver`,
seeded via `IntegrationCatalogSeeder`. `openspec/specs/integrations/spec.md` confirms the catalog seeds
**TikTok, YouTube, Instagram, Blotato** (plus Gemini, Canva) with a `publishing` provider category.
`.github/project-ai/providers/social/blotato/blotato.instructions.md` earmarks **Blotato as a
multi-network publication distribution provider** — a single Blotato adapter could fan out to
IG/TikTok/X/Meta, vs. one native adapter per network. **No org has connected credentials yet** (matches
"no connectors configured"). `OrganizationIntegration` is one row per (Organization, Provider) — NOT per
social account. A separate existing entity, `ClientOwnAccount`
(`Domain/Social/ClientOwnAccounts/ClientOwnAccount.cs`: ClientId + SocialNetworkId + ProfileUrl + Handle),
models "this client owns this account on this network" but is currently unused. A `Publication`
destination likely needs BOTH: which org-provider connection to use AND which client account to target.

### 5. Scheduling gap — CONFIRMED, no ambiguity
`PlanContext` is a soft `ContentPlanId + Period + CalendarSlotId` link (no datetime). `CalendarSlot` only
has `Day` (day-of-month int). No time-of-day / UTC timestamp anywhere. `Publication.ScheduledForUtc` is
100% new domain surface. Any scheduler/dispatcher to transition `Scheduled → Publishing` at the right
time is new infrastructure (cron/background worker).

### 6. Asset / thumbnail storage — CONFIRMED, reusable as-is
`IGeneratedAssetStorage` (`SupabaseStorageAdapter` implements it), keyed by
`StoredAssetKey(OrganizationId, ClientId, AnalysisId, RelativePath)`. Items stream via
`GET /api/social/content-studio/items/{itemId}/assets/{assetIndex}`. `ListContentItemsQueryHandler`
already resolves storage-path background refs into data-URIs for grid rendering
(`StudioVisualMarkupResolver.ResolveBackgroundRefsAsync`) — reuse this exact resolver for Bank/kanban
thumbnails.

### 7. Persistence conventions — CONFIRMED pattern
ContentStudio tables live under `Infrastructure/Persistence/Postgres/Entities/`
(`ContentStudioItemData`, `ContentStudioSetData`, `ContentStudioSetItemData`, `ContentStudioAssetData`),
mapped into the `social` schema, snake_case columns, additive EF Core migrations. A new `Publication`
aggregate follows the same convention (`social.publications`, separate table from `content_studio_items`).

### 8. openspec change structure — CONFIRMED reference
`openspec/changes/content-studio/` is the template: `proposal.md`, `design.md`, `tasks.md`, `state.yaml`,
`specs/{domain}/spec-delta.md`, plus `specs/content-studio/spec.md` (full spec for the net-new domain).
Expect delta specs against `publishing` (stub today) and likely a new `specs/content-bank/spec.md`.

### 9. Architecture / gates — CONFIRMED, no surprises
Standard hexagonal boundaries: Domain zero deps; Application defines `IPublicationConnector` port;
Infrastructure implements adapters behind it; Api depends on Application only. `ai-agent.gate.md` requires
`scripts/gates/Run-Gates.ps1`, no MUST-gate skips, doc sync in the same change. A new `Publication`
aggregate + `social` table + port/adapter is architecturally unremarkable.

## Affected Areas
- `IContentItemRepository.cs` — new client-wide / date-range / state-filter listing method.
- `Queries/ListContentItemsQuery.cs` — mirror for a cross-set "list by client" query; reuse `StudioVisualMarkupResolver`.
- `MapContentStudioEndpoints.cs` — new Bank listing endpoint(s).
- `Domain/Integrations/OrganizationIntegration.cs`, `openspec/specs/integrations/spec.md` — `ConnectorId` resolution source.
- `Domain/Social/ClientOwnAccounts/ClientOwnAccount.cs` — candidate destination-account reference (currently unused).
- `.github/project-ai/providers/social/blotato/blotato.instructions.md` — Blotato as multi-network distribution provider.
- `apps/web/app/(dashboard)/publications/page.tsx` — placeholder; real target for Bank/Planner/Published UI.
- `apps/web/components/social/content-studio-panel.tsx` — reused per-card edit surface.
- `Domain/Social/ContentStudio/PlanContext.cs`, `.../ContentPlans/CalendarSlot.cs` — no scheduled-datetime; `ScheduledForUtc` is new.
- `Infrastructure/Persistence/Postgres/Entities/ContentStudio*Data.cs` — EF mapping pattern for new `PublicationData`.
- `openspec/changes/content-studio/` — structural template.

## Open Decisions to resolve at proposal time
1. **Publication connector strategy:** native per-network `IPublicationConnector` (Instagram-only first) vs.
   a Blotato-backed adapter that covers multiple networks sooner. The port stays channel-agnostic either way;
   the question is which first implementation to build.
2. **Destination modeling:** does `Publication` reference both `OrganizationIntegration` (connection/credentials)
   and `ClientOwnAccount` (which client handle), or a new parallel connector concept? Recommendation: reuse both.
3. **Materialization trigger:** how overnight-generated / un-opened analyses reach the Bank —
   lazy materialize-on-Bank-load, background job on generation completion, or hybrid.

## Risks
- Materialization-on-demand makes the Bank's "all content for a client" promise incomplete unless
  materialization is proactively triggered — must be resolved explicitly, not assumed.
- Blotato is staged as a multi-network distribution provider but has no adapter yet; building a native
  Instagram-only connector without deciding Blotato's role risks later rework.
- `ConnectorId` alone (org+provider) doesn't capture which client handle to publish to; multi-client
  multi-account reality needs both `OrganizationIntegration` and `ClientOwnAccount` dimensions.
- No scheduled-datetime field exists; `ScheduledForUtc` plus a scheduler/dispatcher is entirely new infra.
- The Bank cross-set query touches `IContentItemRepository` (already implemented by an EF adapter) — add the
  method to both interface and adapter in the same change (architecture gate).

## Ready for Proposal
Yes. All nine investigation points traced to concrete files/symbols; no blocking unknowns. The three open
decisions above are proposal-level, to be resolved with the user before or at `sdd-propose`.
