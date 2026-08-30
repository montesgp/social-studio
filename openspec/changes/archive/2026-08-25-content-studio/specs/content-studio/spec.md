# Content Studio Spec

<!-- ADR: docs/architecture/adr/0004-content-studio-review-model.md -->
<!-- Change: content-studio (Phase 1 — Foundation on Single Analyzer) -->

## Purpose

Define the capabilities, invariants, lifecycle, and contracts of **Content Studio**: the post-generation surface where AI-generated deliverables are reviewed, edited, validated, approved, and prepared for publication. Content Studio is **source-agnostic** (consumes any generator's output), **derives** an editable working surface from immutable generation output without mutating it, and models individual and bulk operations as the same operation at different cardinality.

This spec covers **Phase 1** (foundation + Single Analyzer integration). Visual/layout editing, variants/regenerate, bulk operations, client presentations/export, ContentPlan linkage, and Magic Generator/Campaign are defined in the proposal roadmap and added by later change deltas.

## PRD / SDD Traceability

| Requirement | Anchor |
|---|---|
| CS-03, CS-08 | PRD §7 open question "Exact approval workflow for clients"; SDD §6 events between `CanvasDraftCreated` and `PublicationRequested` |
| CS-07 | PRD §7 open question "Whether creative preview is built internally or delegated to Canva first" (answered: internal preview) |
| CS-09 | PRD §6 step 8 "send publication draft to publishing provider"; SDD §5 contexts "Asset/Canvas Creation" → "Publishing" |
| CS-02, CS-10 | SDD §5 bounded-context isolation; ADR 0003 (PA-REG-01 precedent) |
| CS-11 | SDD §8 organization-scoped persistence + RLS |

---

## Requirements

### Requirement: CS-RENDER-01 — Isolated Visual Markup Rendering

The Content Studio surface MUST render visual drafts in an isolated DOM boundary (Shadow DOM or iframe `srcDoc`) that preserves required local CSS from `visualSpecMarkup` without leaking CSS to the host app. The renderer MUST remove or neutralize scripts, inline event handlers, and `javascript:` URLs where feasible. If markup is empty or cannot produce a usable preview, the surface MUST fall back to rendered image assets from `ContentItem.assets`.

### Requirement: CS-SAVE-01 — Debounced Local Autosave

The Content Studio surface MUST autosave caption and visual edits with debounce, show a small Saving/Saved/Error state, and update the edited item locally without refreshing the whole set. Autosave MUST tolerate out-of-order or stale responses so older saves do not overwrite newer local edits, and editing/dragging MUST NOT remount the editor.

### Requirement: CS-01 — ContentItem Is the Atomic Reviewable Aggregate

The system MUST represent a single reviewable/editable content piece as a `ContentItem` aggregate in `Domain/Social/ContentStudio/`, keyed by its own `ContentItemId` and scoped by `OrganizationId` and `ClientId`. A `ContentItem` MUST carry a `ContentOrigin`, a `ContentKind`, an editable `ContentDraft`, a `ReviewState`, and an `EditTrace`. The aggregate MUST be pure domain (no EF / provider / ASP.NET types) and MUST be constructed through a validated `Result<ContentItem>` factory.

#### Scenario: ContentItem is created with required fields

- GIVEN a completed format result for a client with a valid origin, kind, and initial draft
- WHEN a `ContentItem` is materialized
- THEN the result is a successful `Result<ContentItem>` whose `OrganizationId`, `ClientId`, `Origin`, `Kind`, `Draft`, and `State` are populated
- AND the initial `State` is `Draft`
- AND `EditTrace.LastEditedSource` is `Manual` with a null `LastEditedAtUtc`

#### Scenario: ContentItem aggregate is free of infrastructure types

- GIVEN the compiled Domain assembly
- WHEN the `ContentItem` type and its value objects are inspected
- THEN no EF Core, Npgsql, Supabase, or ASP.NET type appears in their members
- AND no type from `Domain/Social/SingleAnalyzer/` appears in their members

---

### Requirement: CS-02 — Source-Agnostic Origin Decouples Studio From Any Generator

A `ContentItem` MUST reference its generator output ONLY through a `ContentOrigin` value object containing `GeneratorKind`, `SourceRunId`, optional `SourcePackageId`, `FormatResultId`, and `FormatKey` — all as plain values/Guids with no entity navigation. `GeneratorKind` MUST be an open enumeration whose first value is `SingleAnalyzer`. Adding a new generator MUST NOT require changing the `ContentItem` or `ContentSet` aggregates.

#### Scenario: Single Analyzer origin is represented without coupling

- GIVEN a content item materialized from a Single Analyzer run
- WHEN its `Origin` is inspected
- THEN `Origin.GeneratorKind` is `SingleAnalyzer`
- AND `Origin.SourceRunId` equals the analysis id and `Origin.FormatResultId` references the durable per-format result
- AND no `SingleAnalyzer` domain type is held by the item

#### Scenario: A second generator kind reuses the same aggregate

- GIVEN the `GeneratorKind` enumeration includes a future value (e.g. `MagicGenerator`)
- WHEN a content item is materialized with that kind
- THEN the same `ContentItem` aggregate is used with no structural change
- AND the only difference is the `Origin.GeneratorKind` value

---

### Requirement: CS-03 — ReviewState Is a Guarded Editorial Lifecycle

The system MUST model the editorial lifecycle as a `ReviewState` enumeration (`Draft, InReview, Edited, Approved, Rejected, Discarded, ReadyForPublication, HandedOff`) with transitions enforced by an explicit `ReviewStateTransitions` table. A transition that is not allowed MUST return a structured domain error (`Result` failure), never a silent change or exception. `HandedOff` MUST be terminal.

#### Scenario: A legal transition succeeds

- GIVEN a content item in state `InReview`
- WHEN it is approved
- THEN the transition `InReview → Approved` succeeds and the state becomes `Approved`

#### Scenario: An illegal transition is rejected

- GIVEN a content item in state `Draft`
- WHEN a transition directly to `HandedOff` is attempted
- THEN the operation returns a failed `Result` with an illegal-transition error
- AND the item's state remains `Draft`

#### Scenario: HandedOff is terminal

- GIVEN a content item in state `HandedOff`
- WHEN any state transition is attempted
- THEN the operation returns a failed `Result`
- AND the state remains `HandedOff`

#### Scenario: Reject requires a reason

- GIVEN a content item in state `InReview`
- WHEN it is rejected with an empty reason
- THEN the operation returns a failed `Result` (reason required)
- AND the state remains `InReview`

---

### Requirement: CS-04 — Items Are Materialized From a Generation Run Idempotently

The system MUST provide a materializer that, given a completed generation run (`OrganizationId`, `ClientId`, `GeneratorKind`, `SourceRunId`, optional `SourcePackageId`), reads the run's durable per-format results and creates one `ContentItem` per format plus one `ContentSet` of kind `GenerationRun`. Materialization MUST be idempotent: invoking it again for the same `(OrganizationId, SourceRunId)` MUST return the existing set without creating duplicate items. Materialization MUST NOT modify any generation row.

#### Scenario: First materialization creates a set of items

- GIVEN a completed Single Analyzer run that produced `story`, `carousel`, and `reel-script`
- WHEN the run is materialized
- THEN a `ContentSet` of kind `GenerationRun` is created for that run
- AND it contains exactly one `ContentItem` per produced format, each in state `Draft`
- AND no `GeneratedPackage` or format-result row is modified

#### Scenario: Re-materialization is idempotent

- GIVEN a run that has already been materialized
- WHEN the run is materialized again
- THEN the existing `ContentSet` is returned
- AND no duplicate items are created

#### Scenario: Draft is initialized from the source without coupling

- GIVEN a format result with persisted designer markup and a rendered asset
- WHEN its content item is materialized
- THEN the item's `Draft` carries the text/markup/asset references copied from the source
- AND subsequent edits to the draft do not alter the source format result

---

### Requirement: CS-05 — ContentSet Groups Items and Rolls Up Review State

The system MUST represent a named collection of items as a `ContentSet` aggregate with a `SetKind` (`GenerationRun` in this phase), soft origin references, ordered soft member references, and a `ReviewSummary` roll-up (counts by `ReviewState`). A set MUST reference items softly (no cascade delete of items when the set is removed). An item MAY belong to more than one set.

#### Scenario: Set exposes a review roll-up

- GIVEN a `GenerationRun` set with 3 items in `Draft` and 1 item in `Approved`
- WHEN the set's `ReviewSummary` is read
- THEN it reports `Total = 4`, `Draft = 3`, `Approved = 1`

#### Scenario: Removing a set does not delete its items

- GIVEN a set referencing two items
- WHEN the set is removed
- THEN the two items still exist and remain independently retrievable

---

### Requirement: CS-06 — Text Editing Mutates Only the Draft

A user MUST be able to edit a content item's caption/script/markdown text. The edit MUST mutate only the item's `ContentDraft`, set the state to `Edited`, and record `EditTrace.LastEditedSource = Manual` with the edit timestamp. The edit MUST validate the text is non-empty and within the target channel's maximum length. The source per-format result MUST remain byte-identical after the edit.

#### Scenario: Editing caption text updates the draft and state

- GIVEN a content item of medium `Text` in state `InReview`
- WHEN its text is edited to a valid new value
- THEN the draft text is updated, the state becomes `Edited`, and `EditTrace.LastEditedAtUtc` is set
- AND the source format result is unchanged

#### Scenario: Over-length text is rejected

- GIVEN a content item whose target channel has a caption maximum length
- WHEN its text is edited to a value exceeding that maximum
- THEN the operation returns a failed `Result` with a validation error
- AND the draft is unchanged

---

### Requirement: CS-07 — Preview Is a Presentation-Only Projection

The system MUST expose, per item, a `ContentPreviewProjection` containing the medium, the target channel, the renderable payload (text + asset references + visual markup when present), and the channel constraints (aspect ratio, maximum caption length, maximum slide/scene count) for the item's `(network, placement)`. Reading the preview MUST NOT mutate the item. The destination emulator rendering is a frontend concern driven by this projection.

#### Scenario: Preview projection reflects the target channel

- GIVEN a content item whose target channel is Instagram Carousel
- WHEN its preview projection is requested
- THEN the projection reports medium `Visual`, the Instagram Carousel constraints (square aspect ratio, slide-count limit), and the renderable payload
- AND the item's state and draft are unchanged after the request

#### Scenario: Preview does not require Canva or an external editor

- GIVEN any materialized content item
- WHEN its preview projection is requested
- THEN the projection is produced from persisted item data only
- AND no external design tool is required to render the preview

---

### Requirement: CS-08 — Per-Item Operations

A user MUST be able to approve, reject (with a non-empty reason), discard, duplicate, and delete a content item. Approve/Reject/Discard MUST be guarded `ReviewState` transitions (CS-03). Duplicate MUST create a new independent `ContentItem` (new identity) that copies the draft/kind/origin but NOT the reviewer, rejection reason, or approval state (the duplicate starts in `Draft`). Delete MUST remove the item and leave any set membership rows as tolerated orphans.

#### Scenario: Approve transitions the item

- GIVEN a content item in state `InReview`
- WHEN a reviewer approves it
- THEN the state becomes `Approved` and `ReviewerUserId` is recorded

#### Scenario: Reject records the reason

- GIVEN a content item in state `InReview`
- WHEN a reviewer rejects it with the reason "off-brand colors"
- THEN the state becomes `Rejected` and `RejectionReason` is "off-brand colors"

#### Scenario: Duplicate starts fresh

- GIVEN an `Approved` content item with a reviewer and no rejection reason
- WHEN it is duplicated
- THEN a new content item exists with a new identity and the same draft/kind/origin
- AND the new item's state is `Draft` with no `ReviewerUserId`

#### Scenario: Delete leaves set membership orphan-safe

- GIVEN a content item that is a member of a `GenerationRun` set
- WHEN the item is deleted
- THEN the item no longer exists
- AND reading the set resolves the missing member gracefully without error

---

### Requirement: CS-09 — Staging Emits an Outbound Publication Handoff

The system MUST allow an `Approved` item to be staged for publication, transitioning `Approved → ReadyForPublication`, building a versioned `PublicationHandoffPayload` (schema version, org/client/item ids, origin, format key, target channel, final copy, final asset references, approval metadata), and emitting it through an `IPublicationHandoff` port. On a successful handoff the item MUST transition `ReadyForPublication → HandedOff`. Content Studio MUST NOT schedule or publish content itself.

#### Scenario: Staging an approved item emits the handoff and marks HandedOff

- GIVEN a content item in state `Approved`
- WHEN it is staged for publication and the handoff port succeeds
- THEN a `PublicationHandoffPayload` with `SchemaVersion = 1` is emitted carrying the final copy, asset references, target channel, and origin
- AND the item transitions to `ReadyForPublication` and then to `HandedOff`

#### Scenario: Staging a non-approved item is rejected

- GIVEN a content item in state `Edited` (not `Approved`)
- WHEN staging for publication is attempted
- THEN the operation returns a failed `Result` (illegal transition)
- AND no handoff payload is emitted

#### Scenario: Content Studio performs no publishing

- GIVEN a staged-and-handed-off content item
- WHEN the handoff completes
- THEN Content Studio has not called any publishing/scheduling provider
- AND the responsibility ends at the emitted handoff payload

---

### Requirement: CS-10 — Immutability Boundary and Context Isolation

Content Studio MUST NOT modify `GeneratedPackage`, format-result, or `ContentPlan` rows. It MUST reference generation output and content plans only by soft Guid (no EF FK navigation to those tables). The `ContentStudio` domain namespace MUST NOT reference any `SingleAnalyzer` domain type and MUST NOT reach into `ContentGeneration` internal types.

#### Scenario: Editing an item never writes to generation tables

- GIVEN a content item materialized from a format result
- WHEN the item is edited, approved, and staged
- THEN no row in `social.generated_packages` or the generation format-result/asset tables is updated
- AND the originating run remains replayable from its immutable record

#### Scenario: No cross-context type leakage

- GIVEN the compiled Domain and Application assemblies
- WHEN `ContentStudio` namespaces are inspected for dependencies
- THEN they contain no `using` of `Domain.Social.SingleAnalyzer`
- AND they hold no reference to `GeneratedPackage`/format-result entity types — only soft Guids

---

### Requirement: CS-11 — Organization and Client Scoping

Every Content Studio table MUST carry `organization_id`, and every query MUST be tenant-isolated. An item or set MUST be accessible only within its owning organization. Item and set rows MUST also carry `client_id` for client-scoped review.

#### Scenario: Cross-organization access is denied

- GIVEN a content item owned by organization A
- WHEN a user from organization B requests it
- THEN access is denied and the item is not returned

#### Scenario: Items are listable by client within an organization

- GIVEN organization A with items for client X and client Y
- WHEN items are listed for client X
- THEN only client X's items are returned

---

### Requirement: CS-12 — Single Analyzer Integration Is Additive

The Content Studio surface MUST integrate into the existing Single Analyzer flow without changing that flow. Opening the Studio surface for a completed analysis MUST (idempotently) materialize and load the run's `ContentSet`. The existing Single Analyzer analysis, generation, and Package-tab behavior MUST remain unchanged.

#### Scenario: Studio surface loads a run's set without altering Single Analyzer

- GIVEN a completed Single Analyzer analysis with a generated package
- WHEN the user opens the Content Studio surface for that analysis
- THEN the run is materialized (or the existing set is returned) and its items are shown for review
- AND the existing analysis record, generated package, and Package-tab download behavior are unchanged

#### Scenario: Opening Studio is safe before any review

- GIVEN a freshly completed analysis whose run has never been opened in Studio
- WHEN the Studio surface is opened
- THEN the set is created on first open (lazy materialization)
- AND no generation re-run is triggered
