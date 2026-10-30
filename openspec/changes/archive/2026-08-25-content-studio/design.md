# Design: Content Studio

## Change ID
`content-studio`

## Status
Design — architecture decisions resolved. Implements the technical HOW for the proposal. ADR: `docs/architecture/adr/0004-content-studio-review-model.md`.

## Scope reminder (Non-Goals are hard boundaries)
This design covers the **shape, persistence, lifecycle, and contracts** of the Content Studio editorial surface and scopes implementation to **Phase 1** (foundation + Single Analyzer integration). It does **not** design visual/layout editing internals (Phase 2), bulk-operation orchestration (Phase 3), presentations/export (Phase 4), Magic Generator/Campaign (Phase 5), or the publication pipeline itself (separate `publishing` change). It **defines** the seams for all of those so they land without redesign. Content Studio **references** generation output / content plans softly and **never** mutates `GeneratedPackage` or format-result internals.

---

## 1. Architecture Approach

Same hexagonal, isolated-bounded-context pattern proven by `ProfileAnalysis` (Mundo 2) and the sibling-aggregate + soft-reference pattern proven by `ContentPlan`:

- **Domain** (`Domain/Social/ContentStudio/`) holds pure aggregates + Value Objects, `Result<T>` factory/validation, and an explicit `ReviewStateTransitions` table. No provider, EF, or ASP.NET types. **No `using SocialStudio.Domain.Social.SingleAnalyzer` and no reach into `ContentGeneration` internals** — only soft Guids cross the boundary (CS-10, context isolation, mirrors PA-REG-01).
- **Application** (`Features/Social/ContentStudio/**`, ports under `Abstractions/Social/ContentStudio/`) holds vertical MediatR command/query slices + ports. The **materializer** is an application service that reads existing generation output through existing read ports and emits Content Studio aggregates.
- **Infrastructure** holds EF `*Data` entities, fluent config in `AppIdentityDbContext`, repositories, migrations, the handoff adapter, and (Phase 4) the presentation export adapter.
- **Api** maps minimal endpoints per feature under a Content Studio route group.
- **Frontend** adds a lazy-loaded `ContentStudioModule` (isolated like `ProfileAnalyzerModule`), and an additive Studio surface inside Single Analyzer.

We deliberately **do not** introduce EF navigations between Content Studio aggregates and `GeneratedPackage` / `ContentPlan` / `SingleAnalyzerAnalysis`. Aggregates are keyed by their own ids and reference others by plain Guid, so each loads independently and the architecture gate stays green (no cross-aggregate object graph, no provider leakage).

---

## 2. Resolution of the Open Questions

### OQ-1 — Materialization trigger: **lazy pull on first Studio open (Phase 1)**
**Decision.** Content Studio items are materialized **on demand**, the first time the Studio surface is opened for a generation run (or via an explicit `POST .../materialize`). The materializer is idempotent: re-opening returns the existing `ContentSet` rather than duplicating items.

**Rationale.** Lazy pull keeps **zero coupling** to the generation background worker — no new event wiring, no race with package assembly, no change to the generation pipeline. It also means only runs a user actually reviews get materialized (cheaper at Magic Generator scale). The event-driven trigger (`GeneratedPackageAssembled` → auto-materialize) is a clean later optimization once Magic Generator wants pre-warmed sets; the materializer is the same either way.

**Rejected.** Auto-materialize on `GeneratedPackageAssembled` now — adds worker coupling and a generation-side change for no Phase 1 benefit; materializes runs nobody reviews.

### OQ-2 — Edit provenance: **local `EditTrace`, not shared `Provenance`**
**Decision.** Content Studio uses a context-local `EditTrace { LastEditedSource: EditSource(Manual|AI), LastEditedAtUtc, PromptVersion? }`. It does not import `ContentPlans.Provenance`.

**Rationale.** Preserves bounded-context isolation (PA-REG-01 discipline): `ContentStudio` must not depend on `ContentPlans` domain types. `EditTrace` is a smaller, edit-focused trace (who/when/AI-or-human) — it does not need `Provenance`'s `Empty/Draft/Generated/Edited` node-status semantics because `ReviewState` already models the lifecycle. Promoting `Provenance` to a shared `Domain/Social/Shared/` kernel would let both reuse one VO, but that is a cross-cutting refactor with its own migration risk — deferred, not blocked.

**Rejected.** Import `ContentPlans.Provenance` — couples two contexts. Promote to shared kernel now — scope creep beyond this change.

### OQ-3 — Set membership storage: **join table `content_studio_set_items`**
**Decision.** Set→item membership is a join table with explicit `sort_order`, mirroring `content_plan_slots`.

**Rationale.** Keeps membership queryable (filter items by set, count by state for the roll-up, order deterministically) and supports many-to-many memberships (OQ-4) without rewriting a jsonb blob on every change. A jsonb `item_ids` array loses cheap per-state aggregation that the `ReviewSummary` and bulk selection rely on.

**Rejected.** `item_ids` jsonb on the set — loses query/aggregation/order efficiency; pessimal at Magic Generator scale.

### OQ-4 — Item↔set cardinality: **many soft memberships; one canonical home**
**Decision.** An item MAY belong to multiple sets. Its `GenerationRun` set is the canonical "home" (the run it was materialized from). `AdHocSelection` and (Phase 4) `ClientPresentation` sets may reference the same item. Membership is soft (join rows); deleting a set never deletes items, and deleting an item leaves tolerated orphan join rows resolved at read time.

**Rationale.** Matches real use: the same approved carousel can appear in a client presentation and an ad-hoc selection while still belonging to its origin run. Soft membership preserves aggregate independence.

### OQ-5 — Handoff payload versioning: **`schemaVersion` field on the payload**
**Decision.** `PublicationHandoffPayload` carries an integer `SchemaVersion` (starts at `1`), mirroring the `CompositionSpecEnvelope` schemaVersion precedent used for persisted markup.

**Rationale.** A single additive field lets the future pipeline branch on version without a new contract type per revision. Cheap forward-compat seam; no premature contract hierarchy.

---

## 3. Domain Model

Namespace `SocialStudio.Domain.Social.ContentStudio`. All types pure; `Result<T>` for validated construction/transitions; `Restore(...)` for trusted EF re-hydration.

### 3.1 Value Object — `ContentOrigin` (source-agnostic reference)
```
public sealed record ContentOrigin(
    GeneratorKind GeneratorKind,   // enum: SingleAnalyzer = 0, MagicGenerator = 1 (future), ...
    Guid SourceRunId,              // the generation job/run id (analysis id for Single Analyzer)
    Guid? SourcePackageId,         // GeneratedPackage id when applicable
    Guid FormatResultId,           // the durable per-format result this item derives from
    string FormatKey);             // "story" | "carousel" | "reel-script" | "caption" | "hook" | ...
```
- The **only** link from Content Studio to a generator. No `SingleAnalyzer`/`ContentGeneration` type is referenced. Adding Magic Generator = a new `GeneratorKind` value + a materializer input mapping; the aggregate is unchanged.

### 3.2 Value Object — `ContentKind`
```
public sealed record ContentKind(
    string FormatKey,        // canonical format key (matches generation format keys)
    ContentMedium Medium);   // enum: Text, Visual, Video, Document  → drives preview/edit dispatch
```
- `Medium` classifies for preview and edit routing (Text → caption/script edit; Visual → PPTX/carousel; Video → reel/short; Document → presentation). Renderer/preview dispatch branches on `Medium`, never on raw format strings (same discipline as `CompositionOutputKind` dispatch, RS-01).

### 3.3 Value Object — `ContentDraft` (editable working payload)
```
public sealed record ContentDraft(
    string? Text,                       // caption / script / markdown body (editable)
    string? VisualSpecMarkup,           // copy of validated VisualCompositionSpec markup (Phase 2 edits)
    IReadOnlyList<ContentAssetRef> Assets,  // image/background URIs via storage ports (soft)
    TargetChannel TargetChannel);       // network + placement for preview/handoff
```
- Initialized **from** the immutable source format result at materialization, then owned/mutated by the item. The source is never written back.
- `ContentAssetRef(string Role, string StorageUri, string Mime)` — `Role` e.g. `background` | `image` | `rendered`. Plain URIs from existing storage ports; no payloads in the aggregate.

### 3.4 Value Object — `TargetChannel`
```
public sealed record TargetChannel(
    string Network,      // "instagram" | "tiktok" | "youtube"
    string Placement);   // "feed" | "carousel" | "story" | "reel" | "short"
```
- Drives the preview projection's channel constraints and the handoff payload's destination. Network/placement are open strings validated against an allowlist in Application (not a domain enum, to stay catalog-open like the social-network icon allowlist).

### 3.5 Value Object — `EditTrace`
```
public sealed record EditTrace(
    EditSource LastEditedSource,   // enum: Manual, AI
    DateTime?  LastEditedAtUtc,
    string?    PromptVersion)      // set when an AI edit/regenerate produced the current draft (Phase 2+)
{
    public static EditTrace Initial(DateTime nowUtc) => new(EditSource.Manual, null, null);
}
```
- Audit-only; never blocks a transition (same discipline as `Provenance.Status`).

### 3.6 Enum + transitions — `ReviewState` / `ReviewStateTransitions`
```
public enum ReviewState
{
    Draft = 0,             // materialized from generation, untouched
    InReview = 1,          // opened/under review
    Edited = 2,            // draft text/visual changed by a human
    Approved = 3,          // accepted by reviewer
    Rejected = 4,          // not accepted (carries RejectionReason)
    Discarded = 5,         // set aside, removed from active review (recoverable)
    ReadyForPublication = 6, // staged; handoff payload buildable
    HandedOff = 7          // outbound handoff emitted; terminal for Content Studio
}
```
Allowed transitions (enforced by a static `ReviewStateTransitions` map, mirroring `ProfileAnalysisStatusTransitions`):
```
Draft        -> InReview | Edited | Discarded
InReview     -> Edited | Approved | Rejected | Discarded
Edited       -> InReview | Approved | Rejected | Discarded
Approved     -> ReadyForPublication | Edited | Rejected      (re-open allowed before handoff)
Rejected     -> InReview | Edited | Discarded                 (reviewer can re-engage)
Discarded    -> InReview                                       (recover)
ReadyForPublication -> HandedOff | Approved                    (un-stage before handoff)
HandedOff    -> (terminal)
```
- Any transition not in the map returns `ContentStudioErrors.IllegalReviewTransition` (structured `Result` failure), never a silent no-op (CS-03).
- `Edited` is entered by a successful draft mutation; `Rejected` requires a non-empty reason.

### 3.7 Aggregate — `ContentItem`
```
public sealed class ContentItem : Entity<ContentItemId>
{
    public Guid OrganizationId { get; }
    public Guid ClientId { get; }
    public ContentOrigin Origin { get; }
    public ContentKind Kind { get; }
    public ContentDraft Draft { get; private set; }
    public ReviewState State { get; private set; }
    public EditTrace EditTrace { get; private set; }
    public Guid VariantGroupId { get; }        // groups variants; equals own id when no variants
    public bool IsPrimaryVariant { get; }
    public PlanContext? PlanContext { get; private set; }  // soft refs to a content plan slot (Phase 4 seam)
    public string? RejectionReason { get; private set; }
    public Guid? ReviewerUserId { get; private set; }
    public DateTime CreatedAtUtc { get; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<ContentItem> Materialize(MaterializeContentItem input);  // factory from a format result
    public static ContentItem Restore(...);                                       // trusted EF re-hydration

    public Result EditText(string newText, DateTime nowUtc);                      // CS-06 (Phase 1)
    public Result Approve(Guid reviewerUserId, DateTime nowUtc);                  // CS-08
    public Result Reject(Guid reviewerUserId, string reason, DateTime nowUtc);    // CS-08
    public Result Discard(DateTime nowUtc);                                       // CS-08
    public Result StageForPublication(DateTime nowUtc);                           // CS-09 (Approved -> ReadyForPublication)
    public Result MarkHandedOff(DateTime nowUtc);                                 // CS-09 (only after handoff emitted)
    // Phase 2+: ReplaceImage / SwapBackground / EditVisualSpec / SpawnVariant
}
```
- `Duplicate()` and `SpawnVariant()` are application concerns that create a new `ContentItem` (same `VariantGroupId` for variants; new group for a plain duplicate) — the aggregate exposes a `Clone`-style factory; duplication never copies `ReviewerUserId`/`RejectionReason`.
- The aggregate validates its own invariants (e.g. `Reject` requires a reason; `StageForPublication` only from `Approved`; `MarkHandedOff` only from `ReadyForPublication`).

### 3.8 Aggregate — `ContentSet`
```
public sealed class ContentSet : Entity<ContentSetId>
{
    public Guid OrganizationId { get; }
    public Guid ClientId { get; }
    public SetKind Kind { get; }                  // GenerationRun (P1); Campaign/PlanPeriod/ClientPresentation/AdHocSelection (later)
    public SetOrigin Origin { get; }              // soft refs: GeneratorKind?, SourceRunId?, CampaignId?, Period?
    public IReadOnlyList<ContentSetMember> Members { get; }  // ordered soft item refs (ContentItemId + SortOrder)
    public ReviewSummary Summary { get; }          // roll-up counts by ReviewState (computed/projected)
    public DateTime CreatedAtUtc { get; }
    public DateTime UpdatedAtUtc { get; }

    public static Result<ContentSet> CreateForRun(...);   // GenerationRun set
    public static ContentSet Restore(...);
    public Result AddMember(ContentItemId itemId, int sortOrder);
    public Result RemoveMember(ContentItemId itemId);
}
```
- `ReviewSummary(int Total, int Draft, int InReview, int Edited, int Approved, int Rejected, int Discarded, int ReadyForPublication, int HandedOff)` — projected from member item states; the UI and (Phase 3) bulk operations read it instead of N item rows.
- `SetKind`/`SetOrigin` are forward seams: Magic Generator emits `GenerationRun` sets at large N, or a `Campaign` set aggregating several runs.

### 3.9 `PlanContext` (Phase 4 seam, modeled now)
```
public sealed record PlanContext(
    Guid ContentPlanId,
    PeriodRef Period,          // {kind, year, month} — value mirror, not the ContentPlans.Period type
    Guid CalendarSlotId);
```
- Soft refs only; lets an approved item know which plan slot it fulfills, enabling the future client plan view. Uses a local `PeriodRef` value to avoid importing the `ContentPlans` domain type (context isolation). Null in Phase 1.

---

## 4. Data Flow & Integration Points

```
  Generators (producers)                 Content Studio (this change)            Publishing (future)
  ─────────────────────                  ────────────────────────────            ──────────────────
  Single Analyzer run ─┐
    (Analyze→Generate→  │  format results  ┌───────────────────────────┐
     Deliver, immutable)│  + assets        │ Materializer (Application)│
  GeneratedPackage  ────┼───(read-only)───►│  reads via existing read  │
    + format results    │                  │  ports; builds aggregates │
  Magic Generator  ─────┘   (future,        └─────────────┬─────────────┘
    GeneratorKind=1)         same bridge)                 │ creates
                                                          ▼
                                       ContentSet(GenerationRun) ── soft member refs ──► ContentItem*
                                                          │                                  │
                                              ReviewSummary roll-up                ReviewState machine
                                                                                   + ContentDraft (editable)
                                                                                          │
                                          Approve ─► ReadyForPublication ─► ContentApprovedForPublication
                                                                                          │ IPublicationHandoff
                                                                                          ▼
                                                                       PublicationHandoffPayload (versioned)
                                                                       → consumed by publication pipeline (later)
        soft ref ◄────────────────────────────── PlanContext (Phase 4) ──────────► ContentPlan.CalendarSlot
```

**Read contract from generation (additive delta on `content-generation`).** The materializer consumes a completed run's per-format results through existing read ports (`IContentGenerationFormatResultRepository`, format-asset repository, `IGeneratedPackageRepository`) — it does not introduce a new generation write path and does not modify any generation row. The delta only **specifies** that the per-format result exposes, for read: format key, rendered asset URI + MIME, persisted designer markup (when present), and any publication description — i.e. the fields the draft is initialized from. Today's pipeline already persists all of these (PPTX-01 markup persistence, format assets).

**Brand context for edits.** Visual edits (Phase 2) load `BrandManualSnapshot` via the existing `IClientBrandSnapshotLoader` so guardrail validation runs against the same palette/typography the generator used.

---

## 5. Infrastructure / Persistence

### 5.1 EF entities (`*Data`) and DbContext (schema `social`, all additive)
- **`ContentStudioItemData`** → table `social.content_studio_items`:
  `id`, `organization_id`, `client_id`,
  origin: `generator_kind smallint`, `source_run_id uuid`, `source_package_id uuid NULL`, `format_result_id uuid`, `format_key text`,
  kind: `content_medium smallint`,
  draft: `draft_text text NULL`, `draft_visual_spec_markup text NULL`, `draft_assets_json jsonb`, `target_network text`, `target_placement text`,
  lifecycle: `review_state smallint`, `rejection_reason text NULL`, `reviewer_user_id uuid NULL`,
  variants: `variant_group_id uuid`, `is_primary_variant boolean`,
  edit trace (owned): `edit_last_source smallint`, `edit_last_at_utc timestamptz NULL`, `edit_prompt_version text NULL`,
  plan context (owned, nullable): `plan_content_plan_id uuid NULL`, `plan_period_kind smallint NULL`, `plan_year int NULL`, `plan_month int NULL`, `plan_calendar_slot_id uuid NULL`,
  `created_at_utc`, `updated_at_utc`.
- **`ContentStudioSetData`** → table `social.content_studio_sets`:
  `id`, `organization_id`, `client_id`, `set_kind smallint`, origin (`origin_generator_kind smallint NULL`, `origin_source_run_id uuid NULL`, `origin_campaign_id uuid NULL`, `origin_period_kind/year/month NULL`), `created_at_utc`, `updated_at_utc`.
- **`ContentStudioSetItemData`** → join table `social.content_studio_set_items`:
  `id`, `set_id uuid` (FK → `content_studio_sets`, cascade), `item_id uuid` (soft Guid — **no** FK to items, to allow tolerated orphans), `sort_order int`. Unique `(set_id, item_id)`.
- Register `DbSet`s in `AppIdentityDbContext`; `EditTrace` and `PlanContext` mapped via `OwnsOne` (flat prefixed columns); enums (`generator_kind`, `content_medium`, `review_state`, `set_kind`, `edit_last_source`, `plan_period_kind`) persisted as `smallint` via `HasConversion<short>()` so provider mapping never leaks to Domain.

### 5.2 Indexes, tenancy, FKs
- `ix_content_studio_items_org` `(organization_id)`, `ix_content_studio_items_client` `(client_id)`, `ix_content_studio_items_run` `(source_run_id)`, `ix_content_studio_items_state` `(organization_id, review_state)`, `ix_content_studio_items_variant_group` `(variant_group_id)`.
- `ix_content_studio_sets_run` `(origin_source_run_id)`, `ix_content_studio_sets_client` `(client_id)`.
- `content_studio_set_items`: unique `(set_id, item_id)`, index `(item_id)` for reverse lookup.
- FKs to `platform.organizations` and `social.clients` on the item/set tables (cascade), mirroring existing org/client-scoped tables. **No** FK to `social.generated_packages`, format results, or `content_plans` (soft Guids only — orphans tolerated, resolved at read).
- Every org-owned table carries `organization_id` (SDD §8); RLS aligns with org/client boundaries.

### 5.3 Migrations (additive, one structural step)
1. **`Add_ContentStudio`** — create `social.content_studio_items`, `social.content_studio_sets`, `social.content_studio_set_items` with all columns, owned-type columns, indexes, and org/client FKs. No data migration (nothing pre-exists). Gate-verified.

A single additive migration is sufficient for Phase 1 — there is no data move (contrast ADR 0002's 4-step objective migration). Phase 2+ columns (e.g. richer visual-edit state) are added by their own additive migrations later.

---

## 6. Application — ports & contracts

### 6.1 New ports (under `Abstractions/Social/ContentStudio/`)
- `IContentItemRepository` — `GetByIdAsync`, `ListBySetAsync(setId, paging)`, `ListByRunAsync(orgId, sourceRunId)`, `ListByClientAsync(orgId, clientId, filter, paging)`, `AddAsync`, `AddRangeAsync`, `UpdateAsync`. Paged + filterable by `ReviewState` (bulk/large-N ready).
- `IContentSetRepository` — `GetByIdAsync`, `GetByRunAsync(orgId, sourceRunId)`, `AddAsync`, `UpdateAsync`, `GetReviewSummaryAsync(setId)` (aggregation query, not N item loads).
- `IPublicationHandoff` — `Task<Result> HandoffAsync(PublicationHandoffPayload payload, CancellationToken)`. Implemented in Infrastructure as a no-op/outbox publisher in Phase 1 (records the handoff); the publication pipeline becomes the real consumer later.
- (Phase 4) `IPresentationExportAdapter` — `Task<Result<RenderedDocument>> ExportAsync(ClientPresentationSpec spec, PresentationFormat format, CancellationToken)` where `PresentationFormat ∈ {Pptx, Pdf}`. Sibling of `IPptxExportAdapter`; PDF uses the screen-faithful capture approach proven by the Profile Analysis dashboard export (avoid print reflow).

### 6.2 Application service — `ContentItemMaterializer`
- Input: `MaterializeRunRequest(Guid orgId, Guid clientId, GeneratorKind kind, Guid sourceRunId, Guid? sourcePackageId)`.
- Reads the run's per-format results (existing read ports), maps each to a `ContentItem.Materialize(...)` with `ContentOrigin` + initial `ContentDraft` (text from caption/script result; `VisualSpecMarkup` from persisted markup when present; assets from format-asset URIs; `TargetChannel` inferred from the format key, overridable later).
- Idempotent: if a `GenerationRun` set already exists for `(orgId, sourceRunId)`, returns it unchanged (no duplicate items). Creates the `ContentSet` + members in one unit of work.

### 6.3 Feature slices (`Features/Social/ContentStudio/**`) — Phase 1
- `Materialize/MaterializeRunCommand` (+ handler, validator) — lazy materialization (OQ-1).
- `GetItem/GetContentItemQuery` → `ContentItemResponse` (origin, kind, draft, state, editTrace, variant info, planContext).
- `GetSet/GetContentSetQuery` → `ContentSetResponse` (members + `ReviewSummaryDto`).
- `ListItems/ListContentItemsQuery` (by run/set/client, paged, state filter).
- `Preview/GetContentPreviewQuery` → `ContentPreviewProjection` (CS-07).
- `EditText/EditItemTextCommand` (+ validator: non-empty within channel max length) — CS-06.
- `Approve/ApproveItemCommand`, `Reject/RejectItemCommand` (reason required), `Discard/DiscardItemCommand`, `Duplicate/DuplicateItemCommand`, `Delete/DeleteItemCommand` — CS-08.
- `Stage/StageItemForPublicationCommand` → transitions to `ReadyForPublication`, builds `PublicationHandoffPayload`, calls `IPublicationHandoff`, then `MarkHandedOff` on success — CS-09.

All ports return domain types / `Result<T>`; no provider types cross into Application. Edit-time AI (Phase 2 variants/regenerate) resolves provider/model through the existing `IIntegrationResolver`/generation queue — no hardcoded model.

### 6.4 `PublicationHandoffPayload` (outbound contract — `publishing` delta)
```
public sealed record PublicationHandoffPayload(
    int SchemaVersion,                 // = 1 (OQ-5)
    Guid OrganizationId,
    Guid ClientId,
    Guid ContentItemId,
    ContentOrigin Origin,              // traceability back to the generator
    string FormatKey,
    TargetChannel TargetChannel,       // network + placement
    string? Copy,                      // final caption/script text
    IReadOnlyList<ContentAssetRef> Assets,  // final rendered/edited asset URIs
    string? SchedulingHint,            // optional; pipeline owns actual scheduling
    DateTime ApprovedAtUtc,
    Guid ApprovedByUserId);
```

---

## 7. Api — endpoint shape (Phase 1)

Route group under the Social module (mirrors `single-analyzer` / `profile-analyzer` grouping):

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/social/content-studio/runs/{sourceRunId}/materialize` | Lazy-materialize (idempotent) the `GenerationRun` set for a run; returns the set |
| GET | `/api/social/content-studio/sets/{setId}` | Get a set with `ReviewSummary` + members |
| GET | `/api/social/content-studio/sets/{setId}/items` | List items in a set (paged, `?state=` filter) |
| GET | `/api/social/content-studio/items/{itemId}` | Get one item (draft + state + origin + editTrace) |
| GET | `/api/social/content-studio/items/{itemId}/preview` | Preview projection for the item's target channel (CS-07) |
| PUT | `/api/social/content-studio/items/{itemId}/text` | Edit caption/script text (CS-06) |
| POST | `/api/social/content-studio/items/{itemId}/approve` | Approve (CS-08) |
| POST | `/api/social/content-studio/items/{itemId}/reject` | Reject with reason (CS-08) |
| POST | `/api/social/content-studio/items/{itemId}/discard` | Discard (CS-08) |
| POST | `/api/social/content-studio/items/{itemId}/duplicate` | Duplicate (CS-08) |
| DELETE | `/api/social/content-studio/items/{itemId}` | Delete (CS-08) |
| POST | `/api/social/content-studio/items/{itemId}/stage` | Stage → `ReadyForPublication` + emit handoff (CS-09) |

- All endpoints are organization-scoped via the existing auth/tenancy middleware; item/set access is validated against `organization_id`.
- Single Analyzer integration: the Studio tab calls `materialize` (idempotent) on open with the analysis id as `sourceRunId`, then renders the set. The existing Single Analyzer endpoints and Package tab are untouched.
- Illegal `ReviewState` transitions map to `ErrorType.Conflict` → HTTP 409 (consistent with the `content-gen.not_exportable` 409 precedent), with a structured error code `content-studio.illegal_transition`.

---

## 8. Preview, Edit, Export & Handoff strategies

### 8.1 Preview (Phase 1 contract; frontend emulator consolidation)
- Backend `ContentPreviewProjection { Medium, TargetChannel, RenderablePayload (text + asset URIs + visualSpecMarkup), ChannelConstraints }` where `ChannelConstraints` carries aspect ratio, max caption length, max slide/scene count, and safe-area hints per `(network, placement)` from an Application-side channel catalog.
- The **frontend** renders the destination emulator (IG Feed/Carousel/Story/Reel, TikTok, YT Shorts) from the projection. This is the single home for preview; the emulator sketched in `generate-social-content-workflow` is refactored to consume this projection (sequencing note in §11). Preview is **presentation-only** — it never mutates the item.

### 8.2 Edit (Phase 1 text; Phase 2 visual)
- **Text (P1):** mutate `ContentDraft.Text`; validate against channel max length; set `ReviewState=Edited`, `EditTrace.LastEditedSource=Manual`. Source format result untouched (CS-10).
- **Visual (P2):** mutate `ContentDraft.VisualSpecMarkup` (slots/background/image), run the existing `IVisualGuardrailValidator` (palette/typography/overflow) and schema validation **before** re-render, then re-render deterministically via `IContentRenderer`/`IContentRendererWithGeometry`. Background swaps reuse `BackgroundSelection`/`BackgroundTier`; forced-default templates remain hard-enforced (BG-03). Image swaps go through `IClientAssetStorage`. No AI call for a manual visual edit.
- **Variants / regenerate (P2):** create a new `ContentItem` in the same `VariantGroupId` by re-invoking the generator at item granularity through `IContentGenerationJobQueue`; reuse the 2-failure regeneration lock (SAFETY-02) and emit usage events.

### 8.3 Export (Phase 4)
- Per-item editable export already exists (`IPptxExportAdapter`, `GET .../download-pptx`) — reused.
- Client presentations: a `ClientPresentation` set rendered to slides via `IPresentationExportAdapter` → PPTX or PDF; the in-system shareable/navigable view is a frontend route over the set read-projection. PDF capture is screen-faithful (Profile Analysis precedent).

### 8.4 Handoff (Phase 1 contract; consumer later)
- `StageItemForPublicationCommand` transitions `Approved → ReadyForPublication`, builds `PublicationHandoffPayload` (§6.4), calls `IPublicationHandoff.HandoffAsync`, and on success transitions `ReadyForPublication → HandedOff`.
- Phase 1 `IPublicationHandoff` implementation records the handoff (outbox row / log) so the contract is exercised end-to-end without a live pipeline. The `publishing` delta specifies the inbound guarantees. Content Studio performs no scheduling/publishing (responsibility boundary).

---

## 9. Individual ↔ Bulk scaling (the explicit evolution requirement)

The model is cardinality-agnostic by construction:

- **Single Analyzer (now):** one run → one `ContentSet(GenerationRun)` → a few `ContentItem`s. Item operations are the per-item commands in §6.3.
- **Magic Generator (Phase 5):** one run → one (large) `ContentSet(GenerationRun)`, or a `ContentSet(Campaign)` aggregating several runs/periods → many items. **Same** aggregates, **same** commands.
- **Bulk operations (Phase 3):** a `BulkOperationCommand(selection, operation)` where `selection` is a set id, a filter (client+state+run), or an explicit item-id list. The handler resolves the selection via the paged `IContentItemRepository`, applies the corresponding per-item command, and returns a **partial-success result** (succeeded/failed/blocked per item) — reusing the partial-completion semantics already accepted for per-format generation. For large N, bulk runs as a background job with progress, reusing the queue/worker pattern; the `ReviewSummary` is the progress surface.
- No "bulk aggregate" exists — bulk is an application fan-out. This is the design property that lets the jump from single to mass happen **without redesign** (proposal goal).

---

## 10. Architecture-gate compliance

- **No** EF Core / Npgsql / Supabase / ASP.NET types in Domain or Application. `ContentItem`, `ContentSet`, `ReviewState`, `ContentOrigin`, `ContentKind`, `ContentDraft`, `EditTrace`, `TargetChannel`, `PlanContext` are pure. Owned-type mapping and enum→`smallint` conversions live only in `AppIdentityDbContext` / `*Data`.
- **Context isolation:** `ContentStudio` references **no** `SingleAnalyzer` type and **no** `ContentGeneration` internal type — only soft Guids + the additive read contract. Verified the same way PA-REG-01 is (no cross-namespace `using`).
- Dependency direction preserved: Domain ← Application ← Infrastructure / Api.
- Provider-agnostic: any edit-time AI routes through existing ports; no hardcoded model (best-practice + AGENTS.md rule).
- **Architecture change** (`require_adr_for_architecture_changes: true`) → ADR `0004-content-studio-review-model.md` authored.
- Docs-sync: `openspec/specs/*` updated for the new/modified capabilities in the spec phase; PRD/SDD gain a Content Studio section at implementation/archive time (mirrors how Profile Analysis was documented).

## 11. Residual Risks (design-level)
- **Source-agnostic discipline drift** — a future contributor could import a `SingleAnalyzer` type into the materializer for convenience. Mitigation: the materializer takes a `GeneratorKind` + generic ids; an architecture-gate `using` check guards it; CS-10 scenario asserts isolation.
- **Preview duplication window** — until `generate-social-content-workflow` is refactored onto the projection, two emulators may coexist. Mitigation: ship the projection contract in Phase 1; track the wizard refactor as a follow-up; do not build a second emulator inside Studio.
- **Handoff contract churn** — the pipeline is unbuilt, so the payload may need fields later. Mitigation: `SchemaVersion` field; additive evolution; the Phase 1 consumer is an outbox/log that tolerates any version.
- **Orphan soft references** — a deleted package/run leaves items referencing a missing source. Mitigation: tolerated by design (same as `ContentPlan` orphan refs); reads resolve gracefully; item retains its own draft + state independent of the source.
- **Idempotent materialization races** — two concurrent first-opens of the same run. Mitigation: unique `(organization_id, origin_source_run_id)` on the `GenerationRun` set + upsert-on-conflict in the materializer; the second caller gets the existing set.
- **Bulk at large N** (Phase 3/5) — synchronous bulk would block. Mitigation: background bulk job + `ReviewSummary` progress; designed now, built later.
