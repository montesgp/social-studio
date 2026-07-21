# Proposal: Content Bank Planner

## Change ID
`content-bank-planner`

## Status
Proposal — defines the full Content Bank Planner capability and scopes the first incremental slice (Instagram-only publication on top of the existing `Social/ContentStudio` context). Exploration: `exploration.md`.

## Intent

Add the missing **planning + publication stage** to the social content lifecycle: a client-wide surface where already-reviewed content is **discovered, triaged, scheduled, and pushed to real social networks**.

Today the pipeline ends at `ReadyForPublication`. Content Studio (change `content-studio`) reviews, edits, and approves individual `ContentItem`s, but its responsibility stops at the editorial handoff. There is no place to see **all content for a client across every set and origin**, no way to decide *when* and *where* a piece goes out, and no adapter that actually publishes to Instagram or Meta. The `/publications` route is a bare placeholder, no organization has connected publish credentials, and no scheduled-datetime concept exists anywhere in the domain.

Content Bank Planner closes that gap as a **read/query + scheduling + publication-orchestration + UI layer ON TOP of the existing `Social/ContentStudio` bounded context**. It reuses the editorial foundation as-is and introduces exactly one new domain concept — the **publication axis** — orthogonal to the editorial axis it already trusts.

## Problem

1. **No client-wide content view.** `IContentItemRepository` exposes only `GetByIdAsync`, `ListByRunAsync`, `ListBySetAsync`. Nothing aggregates "all items for client X across all sets and origins." A content operator cannot see a client's catalog in one place.
2. **Un-opened analyses never materialize.** `MaterializeAnalysisCommand` fires only when a user manually opens the Content Studio panel. An analysis whose owner never opens Studio never becomes a `ContentItem`, so it is invisible to any client-wide Bank.
3. **No publication lifecycle.** `ReviewState` is editorial only. There is no concept of "scheduled for a channel," "publishing," "published," or "publish failed." An approved item has nowhere to go.
4. **No publication connector.** `OrganizationIntegration` models org→provider connections but carries no per-client account credentials, and `ClientOwnAccount` (ClientId + SocialNetworkId + ProfileUrl + Handle) has no credentials at all. Nothing can actually call the Instagram Graph API.
5. **No scheduled-datetime anywhere.** `PlanContext` and `CalendarSlot` model soft plan links and day-of-month only. `ScheduledForUtc` and any dispatcher that fires at the right time are entirely new surface/infra.
6. **The publications UI is empty.** `apps/web/app/(dashboard)/publications/page.tsx` is a `PlaceholderPage`. There is no Bank, no Planner, no Published view.

## Goals

- Introduce a **publication axis** orthogonal to the existing editorial axis: a new `Publication` aggregate, **one per (ContentItem + destination)**, with its own state machine and its own `social.publications` table — never mutating `ReviewState` or any Content Studio internals.
- Add a **client-wide Content Bank query**: a new listing method on `IContentItemRepository` + its EF adapter + a query handler + endpoint, filterable by date range and state, reusing `StudioVisualMarkupResolver` for thumbnails.
- Make un-opened analyses reachable by **lazy materialization on Bank load** (reusing the existing `MaterializeAnalysisCommand`), so the Bank's "all content for a client" promise holds without a background worker in v1.
- Define a **channel-agnostic `IPublicationConnector` port** in Application, implemented first by a **native Instagram adapter** (Graph API) in Infrastructure — architected-ready for Meta and other networks without a redesign.
- Model publication destinations with a **two-level credential model**: org-level `OrganizationIntegration` (the developer app / API access) + per-client account credentials (each client's own account token / external account id per network).
- Deliver a **Bank / Planner / Published UI** on the `/publications` route, reusing `content-studio-panel.tsx` to open any card for edits.
- Keep strict **hexagonal boundaries**: Domain zero-deps, ports in Application, adapters in Infrastructure, Api depends on Application only.

## Scope

### In Scope (v1 — Instagram-only publication, first slice)

- **Publication domain** (`Domain/Social/Publications/`): `Publication` aggregate (one per ContentItem + destination), `PublicationId`, `PublicationDestination` VO (which client account + which network), `PublicationStatus` enum + guarded `PublicationStatusTransitions`, `PublicationAttempt` VO (timestamp, error, externalRef). State machine: `Scheduled → Publishing → Published` (terminal); `Publishing → PublishFailed → (retry) → Publishing`; `Scheduled` / `PublishFailed → Cancelled`. **Two entry paths**: scheduled (starts `Scheduled`) OR **publish-now** (starts directly at `Publishing`).
- **Client-wide Content Bank query**: new client-wide / date-range / state-filter listing method on `IContentItemRepository` **and** its EF adapter (same change — architecture gate), a `ListClientContentBankQuery` handler, and a Bank listing endpoint. Reuse `StudioVisualMarkupResolver.ResolveBackgroundRefsAsync` for thumbnails. Default view weights the last 7 days; older content still queryable.
- **Lazy materialization on Bank load**: opening the Bank for a client materializes all completed analyses not yet turned into `ContentItem`s, reusing the existing `MaterializeAnalysisCommand`.
- **Editorial → publication flow**: from an `Approved` / `ReadyForPublication` item, create a `Publication` for a chosen destination via **publish-now** (immediate `Publishing`).
- **Native Instagram connector**: `IPublicationConnector` port (Application) + a native Instagram Graph API adapter (Infrastructure) that publishes a resolved item to a target client handle.
- **Per-client account credentials for Instagram**: extend `ClientOwnAccount` (or add a new `ClientAccountConnection` entity) to hold per-client-account publish credentials (account token + external account id), resolved against the org-level `OrganizationIntegration`.
- **Persistence**: additive `social.publications` table + EF mapping mirroring the `ContentStudio*Data` convention (snake_case, `social` schema, additive migration).
- **Minimal Bank / Planner UI**: replace the `/publications` placeholder with tabs **[Bank · Planner · Published]**. Bank = catalog/grid triage; Planner = kanban pipeline (columns projected from both axes: Approved → Scheduled → Publishing → Published, plus a Failed lane and Rejected access); Published = recency-weighted list. Date-range filter. `content-studio-panel.tsx` reused to open any card for edits.

### Out of Scope (later slices — roadmapped, not this change)

- **Meta adapter** and **all other networks** (TikTok, YouTube, X). Ports are channel-agnostic and architected-ready; only Instagram is implemented in v1.
- **Scheduler / dispatcher worker** that transitions `Scheduled → Publishing` at the scheduled UTC time. **Timed scheduling is deferred**; v1 ships publish-now only (see Scope Boundaries). The `Scheduled` state and `ScheduledForUtc` field exist in the domain from day one, but no infra fires them automatically in v1.
- **Background materialization** on generation completion (v1 is lazy-on-Bank-load only).
- **Blotato multi-network consolidation** — deferred future consolidation; native per-network adapters first.
- **Published-history recency polish**, metrics/insights capture, and post-publish analytics.
- **Bulk scheduling / bulk publish** across a selection.

### Scope Boundaries

- The publication axis is **orthogonal** to the editorial axis. `Publication` references a `ContentItem` by id (soft ref) and **never** mutates `ReviewState`, `ContentDraft`, or any Content Studio internal.
- **Publish-now can ship without a scheduler.** The `Scheduled` state and `ScheduledForUtc` are modeled now so the later dispatcher is additive, but v1 does not build the timed-firing infra. This keeps the first slice deliverable and testable end-to-end with Instagram.
- A `Publication`'s destination = (which client account) + (which network), resolved against org-level `OrganizationIntegration` (API app) + per-client account credential (target handle). Content belongs to a client within an org; the user selects which of that client's accounts + which network.
- Provider-specific concerns (Graph API types, tokens) stay behind `IPublicationConnector`; no Instagram/Meta SDK type leaks into Domain or Application (architecture gate).

## Capabilities

### New Capabilities

- `content-bank`: a client-wide projection/query of all `ContentItem`s for a client across all sets and origins, with date-range and state filtering, lazy materialization, and grid triage.
- `publishing`: the publication axis — `Publication` aggregate, destination model, `IPublicationConnector` port, and the first native Instagram adapter (moves the `publishing` spec from stub to first real slice).

### Modified Capabilities (additive deltas)

- `content-studio`: expose the **client-wide listing seam** on `IContentItemRepository` (additive method; no editorial behavior change). Delta: `specs/content-studio/spec-delta.md`.
- `integrations`: define **per-client account credentials** layered on top of the existing org-level `OrganizationIntegration` (two-level credential model). Delta: `specs/integrations/spec-delta.md`.

## Approach

A new application feature area `Features/Social/Publications/**` plus the `Domain/Social/Publications/` context. The **publication axis** is a pure-domain `PublicationStatus` machine with explicit, table-driven transitions (`PublicationStatusTransitions`), mirroring `ReviewStateTransitions` and `ProfileAnalysisStatusTransitions`. Each `Publication` is one per (ContentItem + destination); operations (schedule, publish-now, mark-published, mark-failed, retry, cancel) are MediatR command slices that load the aggregate, apply a guarded transition, and persist via a new `IPublicationRepository`.

The **Content Bank** is a client-wide read projection: a new `IContentItemRepository` listing method (client-wide, date-range, state-filter) plus a query handler that reuses `StudioVisualMarkupResolver` for thumbnails — additive, not a redesign, since `ContentItem` already carries `ClientId`. Opening the Bank triggers **lazy materialization** of any completed analyses not yet turned into items (reusing `MaterializeAnalysisCommand`), so the client-wide promise holds without a worker.

**Publication** resolves a destination against two credential levels: the org-level `OrganizationIntegration` (the developer app / API access) and a per-client account credential (`ClientOwnAccount` extended, or a new `ClientAccountConnection`) that carries the target handle's token and external account id. The `IPublicationConnector` port is channel-agnostic; the first adapter is a native **Instagram Graph API** implementation. Publish-now drives the item straight to `Publishing`, calls the connector, and records a `PublicationAttempt` (success → `Published`; failure → `PublishFailed`, retryable).

The **UI** replaces the `/publications` placeholder with three tabs. The Planner kanban is a **projection over both axes** — editorial state gates entry (only Approved/ReadyForPublication items are actionable) and publication state drives the columns. `content-studio-panel.tsx` is reused verbatim to open any card for edits, so no editing logic is duplicated.

**Timed scheduling is deferred by design.** The `Scheduled` state and `ScheduledForUtc` exist so the future dispatcher is a purely additive background worker; v1 ships publish-now end-to-end and leaves the timed-firing infra for a later slice.

## Domain Model (summary — full model in `design.md`)

- **`Publication`** (aggregate root) — one per (`ContentItemId` + `PublicationDestination`). Holds `ScheduledForUtc` (nullable), `PublicationStatus`, ordered `PublicationAttempt`s (timestamp, error, externalRef), tenancy (`OrganizationId`, `ClientId`).
- **`PublicationDestination`** (VO) — which client account + which network; resolves to (org-level integration) + (per-client account credential).
- **`PublicationStatus`** — `Scheduled → Publishing → Published`, plus `PublishFailed` (retryable to `Publishing`) and `Cancelled` (from `Scheduled` / `PublishFailed`); transitions guarded by `PublicationStatusTransitions`.
- **Per-client account credential** — `ClientOwnAccount` extended, or a new `ClientAccountConnection` (ClientId + SocialNetworkId + credentials), holding the per-account token + external account id.
- **Reused as-is**: `ContentItem`, `ReviewState`, `ContentSet`, `TargetChannel`, `ContentOrigin`/`GeneratorKind`, `IGeneratedAssetStorage`, `StudioVisualMarkupResolver`. No Content Studio type is modified.

## First-Slice / MVP Boundary

**v1 must-have (this change, Instagram-only, testable end-to-end):**

1. Client-wide Content Bank listing (repository method + EF adapter + query + endpoint).
2. Lazy materialization on Bank load (reuse `MaterializeAnalysisCommand`).
3. `Publication` domain (aggregate + state machine + `social.publications` table).
4. Editorial → publication flow via **publish-now**.
5. Native Instagram `IPublicationConnector` adapter.
6. Per-client account credentials for Instagram (extend `ClientOwnAccount` or add `ClientAccountConnection`).
7. Minimal Bank / Planner / Published UI on `/publications`.

**Later slices (explicitly deferred):**

- Meta adapter and other networks.
- Scheduler / dispatcher worker for timed publishing (`Scheduled → Publishing` at `ScheduledForUtc`) — **new infra**, deferred.
- Background materialization on generation completion.
- Blotato multi-network consolidation.
- Published-history recency polish + post-publish metrics.

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Publication axis leaks into / mutates the editorial axis | High | `Publication` references `ContentItem` by soft id only; a separate aggregate, table, and repository; never writes `ReviewState` or Content Studio internals. Orthogonal by construction. |
| Lazy-materialize-on-Bank-load leaves the Bank incomplete for un-opened analyses | High | Bank load proactively runs `MaterializeAnalysisCommand` for all completed-but-unmaterialized analyses of the client; background job deferred but the lazy trigger closes the v1 gap. |
| Two-level credential model under-specified (org app vs. per-client handle) | High | Destination explicitly resolves BOTH `OrganizationIntegration` (API app) and a per-client account credential (handle token + external id); `ClientOwnAccount` extended or `ClientAccountConnection` added in this change. |
| Scheduler/dispatcher is new infra and easy to overbuild | Medium | Timed scheduling deferred; v1 ships publish-now only. `Scheduled` state + `ScheduledForUtc` modeled so the later worker is additive, not a redesign. |
| Bank cross-set query touches an already-implemented EF adapter | Medium | Add the listing method to `IContentItemRepository` **and** its EF adapter in the same change (architecture gate); mirror existing `ListBySetAsync` shape. |
| Native Instagram-only now, Blotato consolidation later → rework | Medium | `IPublicationConnector` is channel-agnostic; Blotato/Meta become additional adapters behind the same port. Native-first is a deliberate, reversible choice. |
| Instagram Graph API / token specifics leak upward | Medium | All Graph API types stay behind the Infrastructure adapter; Domain/Application see only the port + a resolved destination. Architecture gate enforces it. |

## Dependencies

- **Existing Content Studio context** (`Domain/Social/ContentStudio/`): `ContentItem`, `ReviewState`, `ContentSet`, `TargetChannel`, `ContentOrigin`/`GeneratorKind` — reused unchanged.
- **Existing materialization**: `MaterializeAnalysisCommand` (`Application/Features/Social/ContentStudio/Materialize/`) — reused for lazy Bank materialization.
- **Existing repositories/queries**: `IContentItemRepository` + EF adapter, `ListContentItemsQuery`, `StudioVisualMarkupResolver` — extended additively.
- **Existing integrations**: `OrganizationIntegration` + `IntegrationProvider` catalog + `IIntegrationResolver` (org-level API access); Instagram already seeded in the catalog.
- **Existing account model**: `ClientOwnAccount` (`Domain/Social/ClientOwnAccounts/`) — extended for per-client credentials, or superseded by a new `ClientAccountConnection`.
- **Existing asset storage**: `IGeneratedAssetStorage` (Supabase adapter) — item assets streamed to Instagram at publish time.
- **Existing UI**: `apps/web/app/(dashboard)/publications/page.tsx` (placeholder target) + `apps/web/components/social/content-studio-panel.tsx` (reused edit surface).
- **Patterns to mirror**: `ReviewStateTransitions` / `ProfileAnalysisStatusTransitions` (state machine), `ContentStudio*Data` (EF persistence), `ContentPlan` soft-reference discipline.

## Doc-Sync & Gates Obligations

- Update `docs/PRD.md` / `docs/SDD.md` with the Content Bank Planner capability and the publication axis in the same change (AGENTS.md doc-sync MUST).
- Move `openspec/specs/publishing/` from stub toward the first real slice; add `openspec/specs/content-bank/` as needed; merge deltas at archive.
- Run `pwsh -File scripts/gates/Run-Gates.ps1` before completion; no MUST gate skip, no `--no-verify`; architecture gate must stay green (Domain zero-deps, ports in Application, provider types behind adapters).
- Strict TDD: every new domain rule, transition, handler, and the Bank query has tests written before implementation.
- Consider an ADR under `docs/architecture/adr/` for the two-axis publication model and the two-level credential decision.

## Success Criteria

- [ ] Opening the Bank for a client lists all that client's `ContentItem`s across every set and origin (including single-analyzer content), filterable by date range and state, with resolved thumbnails; completed-but-unmaterialized analyses are materialized on load.
- [ ] A `Publication` can be created for an `Approved`/`ReadyForPublication` item against a chosen (client account + Instagram) destination via publish-now, driving `Publishing → Published` on success and `Publishing → PublishFailed` (retryable) on failure; every transition is a guarded `PublicationStatusTransitions` move.
- [ ] Creating/advancing a `Publication` never modifies any `ContentItem`/`ReviewState`/Content Studio internal (orthogonal axes; architecture gate green).
- [ ] A native Instagram adapter behind `IPublicationConnector` publishes a resolved item to the target client handle using org-level API access + per-client account credentials; no Graph API type leaks into Domain/Application.
- [ ] The `/publications` route shows Bank / Planner / Published tabs; the Planner kanban projects both axes; any card opens `content-studio-panel.tsx` for edits.
- [ ] `social.publications` is additive (new table, snake_case, `social` schema); no existing table is modified.
- [ ] All MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`); docs/specs updated in the same change.

## Open Questions (for spec/design)

- **OQ-1** — Per-client account credentials: extend `ClientOwnAccount` in place vs. introduce a new `ClientAccountConnection` aggregate. (Lean: new `ClientAccountConnection` to keep credentials separate from the currently-unused profile-reference model; design to confirm.)
- **OQ-2** — Is `ScheduledForUtc` + the `Scheduled` state written in v1 (publish-now only) or added with the dispatcher slice? (Lean: model the state/field in v1 so the later worker is additive; no timed-firing infra in v1.)
- **OQ-3** — Planner kanban column mapping: exact projection of the two axes into columns (Approved → Scheduled → Publishing → Published + Failed lane + Rejected access). (Design to finalize the column/lane contract.)
- **OQ-4** — Bank default window: is the last-7-days weighting a query default or a UI-only sort? (Lean: UI-weighted, full history queryable via date-range filter.)
- **OQ-5** — Attempt/retry policy: max attempts, backoff, and whether retry is manual (UI action) or has any automatic component in v1. (Lean: manual retry only in v1; no auto-retry without the dispatcher.)
