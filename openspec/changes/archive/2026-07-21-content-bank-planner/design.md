# Design: Content Bank Planner

## Change ID
`content-bank-planner`

## Status
Design — architecture decisions resolved. Implements the technical HOW for `proposal.md` and scopes the v1 first slice (Instagram-only publication on top of the existing `Social/ContentStudio` context). Recommended ADR: `docs/architecture/adr/0005-publication-axis-and-two-level-credentials.md` (see §14).

## Scope reminder (Non-Goals are hard boundaries)
This design covers the **shape, persistence, lifecycle, and contracts** of the publication axis, the client-wide Content Bank query, the two-level credential model, the `IPublicationConnector` port with its first native Instagram adapter, and the `/publications` UI. It scopes implementation to **v1 — Instagram-only, publish-now end-to-end**. It does **NOT** design: the Meta/TikTok/YouTube/X adapters, the timed scheduler/dispatcher worker, background materialization on generation completion, Blotato consolidation, bulk scheduling, or post-publish metrics. It **defines** the seams for all of those so they land without redesign. The publication axis **references** `ContentItem` softly by id and **never** mutates `ReviewState`, `ContentDraft`, or any Content Studio internal.

---

## 1. Architecture Approach

Same hexagonal, isolated-bounded-context pattern proven by `ContentStudio` and `ProfileAnalysis`, plus the table-driven state-machine pattern proven by `ReviewStateTransitions` / `ProfileAnalysisStatusTransitions`:

- **Domain** (`Domain/Social/Publications/`) holds the pure `Publication` aggregate, its Value Objects (`PublicationDestination`, `PublicationAttempt`), the `PublicationStatus` enum, and an explicit `PublicationStatusTransitions` table. Plus the new `Domain/Social/ClientAccountConnections/` context for the per-client publish credential. No provider, EF, or ASP.NET types. **No `using Incoders.Hive.Domain.Social.ContentStudio`** — only soft `Guid` ids (`ContentItemId.Value`) cross the boundary. The publication axis is orthogonal by construction.
- **Application** (`Features/Social/Publications/**`, ports under `Abstractions/Social/Publications/`) holds the vertical MediatR command/query slices, the `IPublicationConnector` port (channel-agnostic), the `IPublicationRepository` and `IClientAccountConnectionRepository` ports, and the destination-resolution service. The client-wide Bank listing is an additive method on the existing `IContentItemRepository` plus a new `ListClientContentBankQuery` handler.
- **Infrastructure** holds EF `*Data` entities + fluent config in `AppIdentityDbContext`, the repositories, the additive migration `Add_Publications`, and the **native Instagram Graph API adapter** implementing `IPublicationConnector`. All Graph API types, tokens, and HTTP calls stay here.
- **Api** maps minimal endpoints under a `content-bank` / `publications` route group; depends on Application only.
- **Frontend** replaces the `/publications` placeholder with a tabbed surface `[Bank · Planner · Published]`, reusing `content-studio-panel.tsx` verbatim for per-card edits.

We deliberately **do not** introduce EF navigations between `Publication` and `ContentStudioItemData` / `ClientOwnAccount` / `OrganizationIntegration`. `Publication` is keyed by its own id and references others by plain `Guid`, so each loads independently and the architecture gate stays green (no cross-aggregate object graph, no provider leakage).

---

## 2. Resolution of the Open Questions

### OQ-1 — Per-client publish credential: **new `ClientAccountConnection` aggregate** (not a `ClientOwnAccount` extension)
**Decision.** Introduce a NEW aggregate `Domain/Social/ClientAccountConnections/ClientAccountConnection` that holds the per-client-account publish credential. It carries `ClientId`, `OrganizationId`, `SocialNetworkId`, `OrganizationIntegrationId` (the org-level app this account authorizes against), `ExternalAccountId` (the Graph API IG business account id / user id), and an encrypted access token stored as ciphertext produced by the existing `IIntegrationSecretProtector`. `ClientOwnAccount` stays as-is (an unused profile-reference model: `ProfileUrl` + `Handle`, no credentials).

**Rationale.** `ClientOwnAccount` models "this client owns this profile URL" — a *reference* concept with URL validation, sort order, and reserved for a future strategy feature. Publish credentials are a *secrets* concept with a completely different lifecycle (rotate, test, expiry, revoke) and a different security posture (ciphertext, never returned to the client). Overloading `ClientOwnAccount` would blur a reference entity with a secret store, drag URL-validation invariants onto a credential, and couple two features that evolve independently. A separate aggregate keeps the secret lifecycle isolated and mirrors how `OrganizationIntegrationCredential` is already a dedicated secret entity separate from `OrganizationIntegration`.

**Secret storage — follows the existing repo pattern, invents nothing.** `OrganizationIntegrationCredential.EncryptedValue` and `CanvaUserTokenData.EncryptedAccessToken` both store DataProtection ciphertext produced by `IIntegrationSecretProtector.Protect(...)` (`DataProtectionIntegrationSecretProtector`, protector purpose `"incoders.hive.integrations.credentials.v1"`). `ClientAccountConnection` stores the per-account token the SAME way: the Application command protects the plaintext before constructing the aggregate; the aggregate and its `*Data` row only ever hold ciphertext; the Instagram adapter receives the *unprotected* token from a resolver at publish time (§7). **No plaintext token is ever persisted or returned in any DTO.**

**Rejected.** Extend `ClientOwnAccount` in place — couples a reference model to a secret store, forces URL/handle invariants onto credentials, and re-shapes a table reserved for another feature.

### OQ-2 — `Scheduled` state + `ScheduledForUtc` in v1: **modeled now, no dispatcher**
**Decision.** The `Publication` aggregate models both the `Scheduled` status and a nullable `ScheduledForUtc` in v1, and supports **two clean entry paths**:
- **publish-now** → factory `PublishNow(...)` starts the aggregate directly in `Publishing` with `ScheduledForUtc = null`.
- **schedule** → factory `Schedule(..., scheduledForUtc)` starts in `Scheduled` with a non-null future `ScheduledForUtc`.

No infrastructure fires `Scheduled → Publishing` automatically in v1. The transition `Scheduled → Publishing` exists in the table so the future dispatcher (a background worker) is a purely additive consumer that loads due `Scheduled` publications and calls the same `BeginPublishing()` transition the publish-now path already uses.

**Rationale.** Modeling the state/field now costs one enum value + one nullable column and makes the later worker additive, not a redesign. The aggregate stays honest about both entry paths from day one, and the publish-now slice ships and is testable end-to-end without any timed infra.

**Rejected.** Add `Scheduled`/`ScheduledForUtc` only with the dispatcher slice — would force a domain + migration reshape later and leave the Planner kanban unable to represent a "Scheduled" column in v1.

### OQ-3 — Planner kanban column mapping: **projection over (editorial ReviewState × publication PublicationStatus), one card per (item ∪ publication)**
**Decision.** The Planner is a read projection over BOTH axes. Editorial state **gates entry**; publication state **drives the columns**. The card model:

- A `ContentItem` with **no** `Publication` yet renders as an **item-card** and is placed by its `ReviewState`.
- A `ContentItem` with **N** `Publication`s renders as **N publication-cards** (one per destination), each placed by its own `PublicationStatus`. The item-card disappears once at least one publication exists for it (its remaining un-published destinations are still reachable from the panel).

Column / lane contract:

| Column / Lane | Source axis | Placement rule |
|---|---|---|
| **Ready** | editorial | item-cards where `ReviewState ∈ {Approved, ReadyForPublication}` and the item has no `Publication` |
| **Scheduled** | publication | publication-cards where `PublicationStatus = Scheduled` |
| **Publishing** | publication | publication-cards where `PublicationStatus = Publishing` |
| **Published** | publication | publication-cards where `PublicationStatus = Published` (terminal) |
| **Failed** (lane) | publication | publication-cards where `PublicationStatus = PublishFailed` (retry / cancel actions live here) |
| **Rejected** (side access, not a pipeline column) | editorial | items where `ReviewState = Rejected` — reachable via a filter chip, not a drop target |
| **Cancelled** (collapsed) | publication | publication-cards where `PublicationStatus = Cancelled` — hidden by default, shown via filter |

Only items with `ReviewState ∈ {Approved, ReadyForPublication}` are **actionable** for creating a publication (the "create publication" action is disabled otherwise). Draft/InReview/Edited items are visible in the **Bank** grid but not in the Planner pipeline. A multi-destination item is therefore represented as multiple independent publication-cards, each advancing on its own — matching the "one `Publication` per (ContentItem + destination)" invariant exactly.

**Rationale.** Keeping column = single publication status (except the editorial "Ready" entry column) means each card has exactly one unambiguous home, and multi-destination fan-out needs no special "multi-status" card. The projection is a pure read; dragging a card issues the corresponding command (create-publication / retry / cancel) — the board never holds authority over state.

### OQ-4 — Bank default window: **UI-weighted, full history queryable** (confirming the proposal lean)
**Decision.** The last-7-days weighting is a **UI default sort/filter**, not a query constraint. The repository method accepts an optional `fromUtc`/`toUtc` range and returns all matches; the Bank UI defaults the range picker to the last 7 days and lets the operator widen it. This keeps the query general and the "all content for a client" promise intact.

### OQ-5 — Attempt/retry policy: **manual retry only in v1; every attempt recorded; no hard max-attempts block**
**Decision.** Retry is a manual UI action only (`POST .../retry`). Each publish attempt appends an ordered `PublicationAttempt` (attempt number, started/finished timestamps, outcome, error message, external reference) to the aggregate. v1 has **no automatic retry** and **no hard max-attempts guard that blocks further retries** — the operator decides. The aggregate exposes `AttemptCount` and the latest attempt so the UI can surface "attempt N" and warn after a soft threshold (UI-only nudge, default 3). A hard cap is a product decision (flagged in §15), trivially added later as an aggregate guard because attempts are already first-class.

**Rationale.** Automatic retry/backoff is dispatcher territory (deferred). Recording every attempt now gives full auditability and makes both a future hard cap and future auto-retry additive. A blocking cap in v1 risks stranding a publication on a transient provider outage with no operator escape hatch.

---

## 3. Domain Model — Publication axis

Namespace `Incoders.Hive.Domain.Social.Publications`. All types pure; `Result<T>` for validated construction/transitions; `Restore(...)` for trusted EF re-hydration; private + parameterless EF constructor (mirrors `ContentItem`).

### 3.1 Enum + transitions — `PublicationStatus` / `PublicationStatusTransitions`
```
public enum PublicationStatus
{
    Scheduled     = 0,   // queued for a future ScheduledForUtc; no infra fires it in v1
    Publishing    = 1,   // an attempt is in flight against the connector
    Published     = 2,   // provider confirmed; terminal (holds external ref)
    PublishFailed = 3,   // last attempt failed; retryable to Publishing
    Cancelled     = 4    // operator abandoned before Published; terminal
}
```
Allowed transitions (static `PublicationStatusTransitions`, mirroring `ReviewStateTransitions` exactly — a `HashSet<(From, To)>` with `IsValid` / `IsTerminal`):
```
Scheduled     -> Publishing            // due-fire (v1: manual/publish path) 
Scheduled     -> Cancelled             // abandon before it runs
Publishing    -> Published             // success
Publishing    -> PublishFailed         // attempt failed (connector error) OR operator reclaim of a stuck Publishing row
PublishFailed -> Publishing            // manual retry (OQ-5)
PublishFailed -> Cancelled             // give up
Published     -> (terminal)
Cancelled     -> (terminal)
```
- `IsTerminal(status) => status is Published or Cancelled`.
- Any transition not in the map returns `PublicationErrors.IllegalPublicationTransition` (structured `Result` failure), never a silent no-op (same discipline as `ContentStudioErrors.IllegalReviewTransition`).

### 3.2 Value Object — `PublicationDestination`
```
public sealed record PublicationDestination(
    Guid ClientAccountConnectionId,   // which per-client publish account (ClientAccountConnection)
    Guid SocialNetworkId,             // which network (Instagram in v1)
    string NetworkKey);               // canonical provider key, e.g. "instagram" (allowlist-validated in Application)
```
- Resolves at publish time to (org-level `OrganizationIntegration` app access) + (per-client `ClientAccountConnection` token + `ExternalAccountId`). The VO itself holds only soft ids + the network key; no token, no provider type. `NetworkKey` is validated against an Application-side allowlist (v1: `instagram` only), catalog-open like `TargetChannel.Network`.

### 3.3 Value Object — `PublicationAttempt`
```
public sealed record PublicationAttempt(
    int AttemptNumber,             // 1-based, monotonically increasing
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    PublicationAttemptOutcome Outcome,   // enum: Pending, Succeeded, Failed
    string? ErrorMessage,          // provider/domain error when Failed (never a raw token/PII)
    string? ExternalReference);    // provider post/media id when Succeeded
```
- Append-only, ordered by `AttemptNumber`. The aggregate owns the list; a new attempt is opened on each `BeginPublishing`/retry and closed by `MarkPublished`/`MarkFailed`.

### 3.4 Aggregate — `Publication`
```
public sealed class Publication : Entity<PublicationId>
{
    public Guid OrganizationId { get; private set; }
    public Guid ClientId { get; private set; }
    public Guid ContentItemId { get; private set; }        // SOFT ref — never navigated, never mutated
    public PublicationDestination Destination { get; private set; }
    public PublicationStatus Status { get; private set; }
    public DateTime? ScheduledForUtc { get; private set; } // null for publish-now
    public IReadOnlyList<PublicationAttempt> Attempts { get; }  // ordered, append-only
    public string? ExternalReference { get; private set; } // provider post id when Published
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public int AttemptCount => Attempts.Count;

    // Entry path A — schedule for later (Scheduled). No dispatcher fires it in v1.
    public static Result<Publication> Schedule(
        PublicationId id, Guid organizationId, Guid clientId, Guid contentItemId,
        PublicationDestination destination, DateTime scheduledForUtc, DateTime nowUtc);

    // Entry path B — publish immediately (starts in Publishing, opens attempt #1).
    public static Result<Publication> PublishNow(
        PublicationId id, Guid organizationId, Guid clientId, Guid contentItemId,
        PublicationDestination destination, DateTime nowUtc);

    public static Publication Restore(...);   // trusted EF re-hydration

    // Scheduled -> Publishing (opens a new attempt). Used by publish-now internally and by the future dispatcher.
    public Result BeginPublishing(DateTime nowUtc);
    // Publishing -> Published (closes the open attempt as Succeeded, stores external ref).
    public Result MarkPublished(string externalReference, DateTime nowUtc);
    // Publishing -> PublishFailed (closes the open attempt as Failed with the error).
    public Result MarkFailed(string errorMessage, DateTime nowUtc);
    // PublishFailed -> Publishing (manual retry, opens attempt #N+1). OQ-5.
    public Result Retry(DateTime nowUtc);
    // Scheduled | PublishFailed -> Cancelled.
    public Result Cancel(DateTime nowUtc);
    // Publishing -> PublishFailed. Operator/reaper recovery of a row stuck in Publishing
    // (process crashed after persist-before-call, before the connector callback returned).
    // Closes the open attempt as Failed with a reclaim reason; the publication is then retryable/cancellable.
    public Result ReclaimStuck(string reason, DateTime nowUtc);
}
```
- Invariants the aggregate enforces itself: `Schedule` requires `scheduledForUtc > nowUtc`; `PublishNow`/`Schedule` require a non-empty `contentItemId` and a valid `Destination`; `MarkPublished` requires a non-empty `externalReference`; `MarkFailed` requires a non-empty `errorMessage`; every status change routes through `PublicationStatusTransitions.IsValid` and returns `IllegalPublicationTransition` otherwise.
- **Orthogonality guard:** `Publication` references `ContentItemId` as a bare `Guid` and imports NO Content Studio type. It never reads or writes `ReviewState`. Editorial gating (only `Approved`/`ReadyForPublication` items may spawn a publication) is enforced in the **Application** command by loading the item read-only through `IContentItemRepository` and checking its state — never by coupling the aggregates.

---

## 4. Domain Model — per-client publish credential

Namespace `Incoders.Hive.Domain.Social.ClientAccountConnections`.

### 4.1 Aggregate — `ClientAccountConnection`
```
public sealed class ClientAccountConnection : Entity<ClientAccountConnectionId>
{
    public Guid OrganizationId { get; private set; }
    public ClientId ClientId { get; private set; }
    public SocialNetworkId SocialNetworkId { get; private set; }
    public OrganizationIntegrationId OrganizationIntegrationId { get; private set; } // org-level app this account authorizes against
    public string ExternalAccountId { get; private set; }   // Graph API IG business account / user id
    public string EncryptedAccessToken { get; private set; } // ciphertext (IIntegrationSecretProtector.Protect)
    public DateTime? TokenExpiresAtUtc { get; private set; }
    public ClientAccountConnectionStatus Status { get; private set; } // Connected | Failed | Disabled
    public DateTime? LastTestedAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<ClientAccountConnection> Create(
        ClientAccountConnectionId id, Guid organizationId, ClientId clientId,
        SocialNetworkId socialNetworkId, OrganizationIntegrationId organizationIntegrationId,
        string externalAccountId, string encryptedAccessToken, DateTime? tokenExpiresAtUtc, DateTime nowUtc);

    public static ClientAccountConnection Restore(...);

    public Result RotateToken(string encryptedAccessToken, DateTime? tokenExpiresAtUtc, DateTime nowUtc); // ciphertext in
    public void MarkTestResult(bool isSuccess, string? error, DateTime nowUtc);  // mirrors OrganizationIntegration.MarkTestResult
    public Result Disable(DateTime nowUtc);
}
```
- `Create`/`RotateToken` accept **already-encrypted** values — the aggregate never sees plaintext, exactly like `OrganizationIntegrationCredential.Create(encryptedValue)`. Validation: non-empty `externalAccountId`, non-empty ciphertext, non-empty `organizationIntegrationId`.
- `Status`/`MarkTestResult`/`LastError` mirror `OrganizationIntegration`'s connection-test lifecycle so a "Test connection" action is uniform across levels.

---

## 5. Client-wide Content Bank (additive read seam)

The Bank is a client-wide read projection, additive to Content Studio — no editorial behavior changes.

### 5.1 Repository seam (additive method on the EXISTING port + its EF adapter — same change, architecture gate)
Add to `IContentItemRepository` (mirroring `ListByRunAsync`/`ListBySetAsync` shape):
```
Task<IReadOnlyList<ContentItem>> ListByClientAsync(
    Guid organizationId,
    Guid clientId,
    ContentBankFilter filter,   // { DateTime? FromUtc, DateTime? ToUtc, ReviewState? State }
    CancellationToken ct);
```
- Implemented in the existing EF adapter using the existing `ix_content_studio_items_client_id` index (`client_id`) plus `created_at_utc` range and optional `review_state` predicate. Cross-set, cross-origin — `ContentItem` already carries `ClientId` directly, so this is a query, not a redesign. Ordered `created_at_utc DESC` (recency-weighted).
- `ContentBankFilter` is an Application-layer record (no provider types).

### 5.2 Query slice — `ListClientContentBankQuery`
```
public sealed record ListClientContentBankQuery(
    Guid OrganizationId, Guid ClientId, DateTime? FromUtc, DateTime? ToUtc, string? State)
    : IRequest<Result<IReadOnlyList<ContentBankItemResponse>>>;
```
- Handler mirrors `ListContentItemsQueryHandler`: loads via `ListByClientAsync`, maps to `ContentBankItemResponse`, and **reuses `StudioVisualMarkupResolver.ResolveBackgroundRefsAsync`** to inline storage-path background refs as data-URIs for grid thumbnails (identical to the existing per-set list). It ALSO joins the publication axis: for each item it attaches a lightweight `PublicationSummaryDto[]` (status + destination + attemptCount) via `IPublicationRepository.ListByContentItemsAsync(orgId, itemIds, ct)` so the Bank/Planner can render both axes in one round-trip.
- `ContentBankItemResponse` = the existing `ContentItemResponse` fields (origin, kind, draft, caption, state, editTrace, visualSpecMarkup thumbnail) **plus** `IReadOnlyList<PublicationSummaryDto> Publications`.

### 5.3 Lazy materialization on Bank load
Opening the Bank for a client first ensures all completed-but-unmaterialized analyses become `ContentItem`s, reusing the existing `MaterializeAnalysisCommand`:
- A `EnsureClientContentMaterializedCommand(orgId, clientId)` handler queries the client's completed analyses that have no corresponding `GenerationRun` set yet (via the existing analysis read port + `IContentSetRepository.GetByRunAsync`) and invokes `MaterializeAnalysisCommand` for each (idempotent — re-running returns the existing set). The Bank endpoint runs this BEFORE the listing query so the "all content for a client" promise holds without a worker.
- This is orchestration only: the command loops existing idempotent commands; it introduces no new materialization logic and no generation-side change.

---

## 6. Application — ports & contracts

### 6.1 New ports (under `Abstractions/Social/Publications/`)
- `IPublicationRepository` — `GetByIdAsync(orgId, PublicationId, ct)`, `ListByContentItemsAsync(orgId, IReadOnlyList<Guid> itemIds, ct)`, `ListByClientAsync(orgId, clientId, ct)`, `AddAsync`, `UpdateAsync`.
- `IClientAccountConnectionRepository` — `GetByIdAsync`, `ListByClientAsync(orgId, clientId, ct)`, `GetByClientNetworkAsync(orgId, clientId, SocialNetworkId, ct)`, `AddAsync`, `UpdateAsync`.
- `IPublicationConnector` (channel-agnostic port; the seam that makes Instagram-first reversible):
```
public interface IPublicationConnector
{
    string NetworkKey { get; }   // "instagram" — selector for the resolver

    Task<Result<PublicationResult>> PublishAsync(
        ResolvedPublicationRequest request, CancellationToken ct);
}

public sealed record ResolvedPublicationRequest(
    Guid OrganizationId,
    Guid ClientId,
    Guid ContentItemId,
    string ExternalAccountId,             // target IG business account id
    string AccessToken,                   // UNPROTECTED at call time only (never persisted/logged)
    string NetworkKey,
    string? Caption,                      // from ContentDraft.Caption (IG ceilings already enforced)
    IReadOnlyList<PublicationAssetRef> Assets); // resolved media URIs (via IGeneratedAssetStorage)

public sealed record PublicationResult(string ExternalReference); // provider media/post id
```
- v1 registers exactly one implementation (`InstagramGraphPublicationConnector`, `NetworkKey = "instagram"`). A `IPublicationConnectorSelector` resolves the right connector by `NetworkKey`; adding Meta/TikTok later = a new adapter registered under its key, zero change to the aggregate, commands, or port.

### 6.2 Two-level credential resolution service — `PublicationDestinationResolver`
An Application service that turns a `PublicationDestination` into a `ResolvedPublicationRequest`'s credential fields:
1. Load the `ClientAccountConnection` by `Destination.ClientAccountConnectionId` (per-client account → `ExternalAccountId` + `EncryptedAccessToken` + `OrganizationIntegrationId`).
2. Resolve org-level app access via the existing `IIntegrationResolver.ResolveProviderAsync(orgId, "instagram")` → `ResolvedIntegration.Credentials` (the developer app id/secret dictionary, already decrypted by the integrations layer). Note: `ResolveProviderAsync` already returns `IntegrationNotConnected` when `Status != Connected`, so no duplicate status check is written here; it also verifies category `publishing`.
3. Unprotect the per-account token via `IIntegrationSecretProtector.Unprotect(connection.EncryptedAccessToken)`.
4. Emit the unprotected token + external account id + any app-level fields the adapter needs, bounded to the single `PublishAsync` call. The unprotected token is never stored in the aggregate, never logged, never returned in a DTO.

**This is the two-level model:** org-level `OrganizationIntegration` = the developer app / API access (which app is calling Graph); per-client `ClientAccountConnection` = which account the app is authorized to publish to + that account's token. Both are required to build a valid Graph call.

### 6.3 Feature slices (`Features/Social/Publications/**`) — v1
- `PublishNow/PublishItemNowCommand` (+ validator) — loads the `ContentItem` read-only, **gates on `ReviewState ∈ {Approved, ReadyForPublication}`** (returns `PublicationErrors.ItemNotPublishable` otherwise), resolves the destination, creates `Publication.PublishNow(...)`, persists, then invokes the connector (see §7 flow), and records the outcome.
- `Schedule/SchedulePublicationCommand` (+ validator: `scheduledForUtc` in the future) — same editorial gate, creates `Publication.Schedule(...)`, persists in `Scheduled`. No firing in v1.
- `Retry/RetryPublicationCommand` — loads a `PublishFailed` publication, `Retry()`, re-runs the connector flow.
- `Cancel/CancelPublicationCommand` — `Scheduled | PublishFailed → Cancelled`.
- `Reclaim/ReclaimStuckPublicationCommand` — recovers a publication stuck in `Publishing` (crash after persist-before-call, before the connector returned): loads it, calls `ReclaimStuck(reason)` → `PublishFailed`, so the operator can then retry or cancel. Guarded by a small staleness threshold on the open attempt's `StartedAtUtc` so it cannot race an in-flight publish.
- `GetPublication/GetPublicationQuery` → `PublicationResponse` (status, destination, attempts, externalRef, scheduledForUtc).
- `ListClientContentBank/ListClientContentBankQuery` (§5.2) + `EnsureClientContentMaterialized/EnsureClientContentMaterializedCommand` (§5.3).
- Client account connection management: `CreateClientAccountConnectionCommand` (protects the plaintext token via `IIntegrationSecretProtector.Protect` BEFORE constructing the aggregate), `RotateClientAccountTokenCommand`, `TestClientAccountConnectionCommand` (calls a connector "verify" path or a cheap Graph `GET me`), `ListClientAccountConnectionsQuery` → `ClientAccountConnectionResponse` (**never** includes the token — only `ExternalAccountId`, status, expiry, lastTested).

All ports return domain types / `Result<T>`; no provider types cross into Application.

---

## 7. Publish-now data flow & integration points

```
  Content Studio (editorial, reused as-is)        Publications (this change)                 Instagram (provider)
  ────────────────────────────────────────        ──────────────────────────                ────────────────────
  ContentItem (Approved/ReadyForPublication)
        │  soft Guid id + read-only state check
        ▼
  PublishItemNowCommand (Application)
        │  1. gate: ReviewState ∈ {Approved, ReadyForPublication}
        │  2. resolve destination (PublicationDestinationResolver)
        │        ├─ ClientAccountConnection (per-client token + external account id)
        │        └─ IIntegrationResolver("instagram") (org app access)  ── two levels
        │  3. Publication.PublishNow(...) → Status = Publishing (attempt #1 open)  ── persisted first
        │  4. resolve media: IGeneratedAssetStorage → asset URIs; Caption from ContentDraft.Caption
        ▼
  IPublicationConnectorSelector.For("instagram")
        ▼
  InstagramGraphPublicationConnector.PublishAsync(ResolvedPublicationRequest)  ───────►  Graph API
        │        (create media container → publish; all Graph types stay here)   ◄───────  media id / error
        ▼
  success → Publication.MarkPublished(externalRef)  → Status = Published (attempt #1 Succeeded)
  failure → Publication.MarkFailed(error)           → Status = PublishFailed (attempt #1 Failed; retryable)
        │  persist aggregate (UpdateAsync)
        ▼
  ContentItem / ReviewState  ── NEVER touched (orthogonal axis; architecture gate green)
```

- **Persist-before-call.** The `Publication` is saved in `Publishing` before the connector call; the attempt is closed to `Succeeded`/`Failed` when the connector returns. If the process crashes before the callback runs, the row would otherwise be stuck in `Publishing` (neither `Retry` nor `Cancel` is legal from `Publishing`) — `ReclaimStuckPublicationCommand` (§6.3) drives such a stale row `Publishing → PublishFailed` so the operator can then retry or cancel. This closes the recovery gap without an automatic reaper in v1 (a background reaper is an additive future consumer of the same transition).
- **Asset resolution** reuses `IGeneratedAssetStorage` (Supabase adapter), the same storage the Content Studio grid streams from. The connector receives resolved URIs / streams, not storage internals.
- **Caption** comes from `ContentDraft.Caption` — the domain-level IG ceilings (2,200 chars / 30 hashtags) are already enforced at construction, so the adapter never re-validates or truncates.

---

## 8. Infrastructure / Persistence

### 8.1 EF entities (`*Data`) and DbContext (schema `social`, all additive; mirrors `ContentStudio*Data`)
- **`PublicationData`** → table `social.publications`:
  `id`, `organization_id`, `client_id`, `content_item_id uuid` (soft Guid — **no** FK to `content_studio_items`, orphan-tolerant),
  destination: `destination_client_account_connection_id uuid`, `destination_social_network_id uuid`, `destination_network_key text`,
  lifecycle: `status smallint`, `scheduled_for_utc timestamptz NULL`, `external_reference text NULL`,
  `created_at_utc`, `updated_at_utc`.
- **`PublicationAttemptData`** → table `social.publication_attempts` (owned collection, one-to-many, cascade):
  `id`, `publication_id uuid` (FK → `publications`, cascade), `attempt_number int`, `started_at_utc timestamptz`, `finished_at_utc timestamptz NULL`, `outcome smallint`, `error_message text NULL`, `external_reference text NULL`. Unique `(publication_id, attempt_number)`.
- **`ClientAccountConnectionData`** → table `social.client_account_connections`:
  `id`, `organization_id`, `client_id uuid`, `social_network_id uuid`, `organization_integration_id uuid`, `external_account_id text`, `encrypted_access_token text`, `token_expires_at_utc timestamptz NULL`, `status smallint`, `last_tested_at_utc timestamptz NULL`, `last_error text NULL`, `created_at_utc`, `updated_at_utc`. Unique `(organization_id, client_id, social_network_id)` (one publish account per client per network in v1).
- Register `DbSet`s in `AppIdentityDbContext`; attempts mapped via `OwnsMany`/one-to-many with cascade; enums (`status`, `outcome`, connection `status`) persisted as `smallint` via `HasConversion<short>()` so no provider mapping leaks to Domain (identical convention to `ContentStudioItemData.ReviewState`).

### 8.2 Indexes, tenancy, FKs
- `ix_publications_client` `(organization_id, client_id)`, `ix_publications_content_item` `(content_item_id)`, `ix_publications_status` `(organization_id, status)`, `ix_publications_scheduled` `(status, scheduled_for_utc)` (ready for the future dispatcher's due-query).
- `publication_attempts`: unique `(publication_id, attempt_number)`, index `(publication_id)`.
- `client_account_connections`: unique `(organization_id, client_id, social_network_id)`, index `(organization_id, client_id)`.
- FKs to `platform.organizations` and `social.clients` on `publications` and `client_account_connections` (cascade), mirroring existing org/client-scoped tables. **No** FK from `publications.content_item_id` to `content_studio_items` (soft Guid — orphan-tolerant, same discipline as `content_studio_set_items.item_id`). `client_account_connections.organization_integration_id` references `platform.organization_integrations(id)` (the org app it authorizes against).
- Every org-owned table carries `organization_id`; RLS aligns with org/client boundaries (SDD multi-tenancy rule).

### 8.3 Migration (additive, one structural step)
1. **`Add_Publications`** — create `social.publications`, `social.publication_attempts`, `social.client_account_connections` with all columns, indexes, and org/client FKs. No data migration (nothing pre-exists). Adds the `ListByClientAsync` query support (no schema change needed — reuses `ix_content_studio_items_client_id`). Gate-verified; no existing table modified.

---

## 9. Api — endpoint shape (v1)

Route group under the Social module (mirrors the `content-studio` grouping):

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/social/content-bank/clients/{clientId}/items` | Client-wide Bank listing; `?fromUtc=&toUtc=&state=`; runs lazy materialization first; returns items + per-item publication summaries |
| GET | `/api/social/publications/{publicationId}` | Get one publication (status, destination, attempts, scheduledForUtc, externalRef) |
| POST | `/api/social/content-bank/items/{itemId}/publish` | Publish-now to a destination `{ clientAccountConnectionId, socialNetworkId }` (editorial gate enforced) |
| POST | `/api/social/content-bank/items/{itemId}/schedule` | Create a `Scheduled` publication `{ destination, scheduledForUtc }` (no v1 firing) |
| POST | `/api/social/publications/{publicationId}/retry` | Manual retry of a `PublishFailed` publication |
| POST | `/api/social/publications/{publicationId}/cancel` | Cancel a `Scheduled`/`PublishFailed` publication |
| POST | `/api/social/publications/{publicationId}/reclaim` | Recover a publication stuck in `Publishing` (crash mid-publish) → `PublishFailed` |
| GET | `/api/social/clients/{clientId}/account-connections` | List the client's publish account connections (no token in the DTO) |
| POST | `/api/social/clients/{clientId}/account-connections` | Create a connection `{ socialNetworkId, organizationIntegrationId, externalAccountId, accessToken }` (token protected server-side) |
| PUT | `/api/social/account-connections/{connectionId}/token` | Rotate the access token |
| POST | `/api/social/account-connections/{connectionId}/test` | Test the connection against the provider |

- All endpoints are organization-scoped via `ICurrentTenant.TenantId` (from `X-Tenant-Id`); `OrganizationId` is NEVER read from the body (Tenant Isolation Rule). Access is validated against `organization_id`.
- Illegal `PublicationStatus` transitions map to `ErrorType.Conflict` → HTTP 409 with code `publications.illegal_transition` (consistent with `content-studio.illegal_transition`). Editorial-gate failures map to 409 `publications.item_not_publishable`. Missing/failed credential resolution maps to 422/409 `publications.destination_unresolved`.
- DTOs: `ContentBankItemResponse`, `PublicationSummaryDto`, `PublicationResponse`, `PublicationAttemptDto`, `ClientAccountConnectionResponse` (token-free). Request bodies carry only soft ids + the plaintext token on create/rotate (over TLS; protected immediately server-side, never echoed back).

---

## 10. Frontend — `/publications` decomposition

Replace `apps/web/app/(dashboard)/publications/page.tsx` (currently a bare `PlaceholderPage`) with a client-scoped tabbed surface. Component decomposition (container/presentational discipline):

- `PublicationsPage` (route) → renders `PublicationsWorkspace` with the active client selector.
- `PublicationsWorkspace` (container) — owns the client id + date-range filter state, fetches `GET /content-bank/clients/{clientId}/items`, holds the shared item+publication data, and renders the tab strip `[Bank · Planner · Published]`.
- **Bank tab** — `ContentBankGrid` (presentational): catalog/grid of item-cards across all sets/origins, thumbnails from the resolved markup (reusing the same data-URI the Studio list uses), a date-range picker (default last 7 days, OQ-4), and a `ReviewState` filter chip row. Clicking a card opens `content-studio-panel.tsx` (reused verbatim) for edits. Approved/ReadyForPublication cards expose a **"Publish now"** action (v1) that opens a `DestinationPicker` (lists the client's `ClientAccountConnection`s for Instagram). The "Schedule" action is **hidden in v1** (§15 — no dispatcher fires scheduled publications yet); it is added when the dispatcher slice ships.
- **Planner tab** — `PublicationPlannerBoard` (presentational kanban): columns per §2/OQ-3 (`Ready · Scheduled · Publishing · Published · Failed`), projecting item-cards and publication-cards. Card actions issue commands (publish-now, retry, cancel); the board is a pure projection and never owns state. Multi-destination items appear as multiple publication-cards. Any card opens `content-studio-panel.tsx`.
- **Published tab** — `PublishedList` (presentational): recency-weighted list of `Published` publications with external reference links, filterable by range.
- Shared: `DestinationPicker`, `PublicationCard`, `PublicationStatusBadge`, `AttemptHistoryPopover`. Data access lives in a new `apps/web/lib/api/publications.ts` (mirroring `content-studio.ts`), typed to the DTOs in §9. `content-studio-panel.tsx` is imported unchanged — no editing logic is duplicated.
- Client account connection management is a small settings surface (`ClientAccountConnectionsPanel`) reachable from the client context; the token input is write-only (never pre-filled, never returned).

---

## 11. Individual ↔ Bulk scaling (forward seam)

The model is cardinality-agnostic: a `Publication` is one per (ContentItem + destination); bulk publish/schedule (deferred) is an Application fan-out over a selection (client + state + explicit item-ids), issuing the per-item `PublishItemNowCommand`/`SchedulePublicationCommand` and returning a partial-success result — the same fan-out property Content Studio uses for bulk. No "bulk aggregate" is introduced. The `ix_publications_scheduled` index and the `Scheduled → Publishing` transition are the exact seams the future dispatcher consumes with zero redesign.

---

## 12. Architecture-gate compliance

- **No** EF Core / Npgsql / Supabase / ASP.NET / Graph API types in Domain or Application. `Publication`, `PublicationStatus`, `PublicationDestination`, `PublicationAttempt`, `ClientAccountConnection` are pure; enum→`smallint` conversions and owned-collection mapping live only in `AppIdentityDbContext` / `*Data`.
- **Context isolation:** `Publications` references **no** `ContentStudio` type — only soft `Guid` ids + a read-only `ReviewState` check performed in the Application command via the existing repository. Verified the same way as CS-10 (no cross-namespace `using` from Domain).
- **Provider isolation:** all Instagram Graph API concerns (HTTP, media containers, tokens) stay behind `InstagramGraphPublicationConnector` in Infrastructure, behind the `IPublicationConnector` port. Adding Meta/Blotato = a new adapter behind the same port.
- **Secret handling:** per-client tokens stored only as ciphertext via `IIntegrationSecretProtector` (existing pattern); unprotected only inside the single `PublishAsync` scope; never logged, never in a DTO.
- Dependency direction preserved: Domain ← Application ← Infrastructure / Api.
- **Architecture change** (`require_adr_for_architecture_changes: true`) → ADR `0005` (§14).
- Docs-sync: `openspec/specs/publishing/` moves from stub toward the first slice; `openspec/specs/content-bank/spec.md` added; `content-studio` and `integrations` deltas merged at archive; PRD/SDD gain a Content Bank Planner section (AGENTS.md doc-sync MUST).

---

## 13. Testing strategy (strict TDD — tests written first)

Every new domain rule, transition, handler, and query has a failing test before implementation:

- **Domain (pure, fast):**
  - `PublicationStatusTransitions` — table-driven test asserting every allowed pair is valid and a representative set of illegal pairs is rejected (mirrors the `ReviewStateTransitions` tests). `IsTerminal` for `Published`/`Cancelled`.
  - `Publication` — `PublishNow` starts in `Publishing` with attempt #1 open; `Schedule` rejects a past `scheduledForUtc` and starts `Scheduled`; `MarkPublished` requires external ref and closes the attempt Succeeded; `MarkFailed` requires a message and sets `PublishFailed`; `Retry` opens attempt #N+1 only from `PublishFailed`; `Cancel` legal only from `Scheduled`/`PublishFailed`; `ReclaimStuck` moves `Publishing → PublishFailed` and closes the open attempt as failed with a reclaim reason; illegal transitions return `IllegalPublicationTransition`.
  - `ClientAccountConnection` — `Create` rejects empty external account id / ciphertext; `RotateToken` replaces ciphertext; `MarkTestResult` sets `Connected`/`Failed` + `LastError`.
- **Application (handlers, with fakes):**
  - `PublishItemNowCommand` — **gate test**: an item not in `{Approved, ReadyForPublication}` returns `ItemNotPublishable` and creates NO publication and does NOT touch the item. Happy path: creates a `Publishing` publication, calls the connector, `MarkPublished` on success / `MarkFailed` on connector failure; persist-before-call verified.
  - `PublicationDestinationResolver` — resolves both levels; fails cleanly when the integration is not `Connected` or the connection is missing; the unprotected token is never surfaced beyond the request.
  - `ListClientContentBankQuery` — returns cross-set/cross-origin items for the client, applies date-range + state filters, resolves thumbnails via `StudioVisualMarkupResolver`, and attaches publication summaries.
  - `EnsureClientContentMaterializedCommand` — materializes only completed-but-unmaterialized analyses; idempotent (second run creates nothing).
  - Credential commands — `Create` protects the plaintext before persistence; the DTO never contains the token.
- **Orthogonality assertion (architecture-style):** a test verifying no `Publication`/publications command mutates `ReviewState` (e.g. by asserting the `ContentItem` row is unchanged across a full publish cycle) + a `using`-graph check that Domain `Publications` imports no `ContentStudio` type.
- **Infrastructure:** `InstagramGraphPublicationConnector` tested against a stubbed Graph HTTP handler (success → external ref; failure → `Result.Failure`); EF mapping round-trip for `PublicationData`/`PublicationAttemptData`/`ClientAccountConnectionData` (Restore ↔ persist). Migration applies cleanly on a fresh DB.
- **Gates:** `pwsh -File scripts/gates/Run-Gates.ps1` green before completion; architecture gate stays green; no MUST-gate skip, no `--no-verify`.

---

## 14. ADR recommendation

Author `docs/architecture/adr/0005-publication-axis-and-two-level-credentials.md` capturing two coupled decisions:

1. **Two-axis model** — the publication lifecycle is a **separate `Publication` aggregate + `social.publications` table + `PublicationStatusTransitions`**, orthogonal to and never mutating the editorial `ReviewState` axis; `Publication` references `ContentItem` by soft `Guid`. Rejected alternative: adding publication states onto `ReviewState` (would couple two independently-evolving lifecycles, break the "one publication per destination" cardinality, and force multi-destination onto a single item state).
2. **Two-level credential model** — publishing requires org-level `OrganizationIntegration` (developer app / API access, resolved via `IIntegrationResolver`) **and** a per-client `ClientAccountConnection` (target account id + encrypted token via `IIntegrationSecretProtector`). Rejected alternatives: (a) `ConnectorId` alone (org+provider) — cannot express which client account to publish to; (b) extending `ClientOwnAccount` — mixes a reference model with a secret store.

Mirror the depth of `0004-content-studio-review-model.md` (context, decision, rationale, rejected alternatives, consequences).

---

## 15. Product decisions — RESOLVED with the user

The five product/business calls the design surfaced are now resolved (design gate, user confirmation):

- **Republish semantics — ALLOWED.** Re-publishing the same item to the same destination creates a NEW independent `Publication`. There is **no uniqueness constraint on `(content_item_id, destination)`** and no duplicate-rejection guard. Spec `PUB-01` was updated to match (each publish action creates a new record; earlier publications are preserved). Cardinality is per publish-action, not per (item, destination).
- **Multiple publish accounts per client per network — NO (v1).** One `ClientAccountConnection` per `(organization_id, client_id, social_network_id)` — the unique constraint in §8.2 stays. Relaxing to multi-account later is an additive migration (drop the unique constraint + a destination-picker UX change), so no rework is baked in.
- **Hard max-attempts cap — NONE (v1).** Every attempt is recorded (OQ-5); no blocking cap. A future hard cap is an additive aggregate guard because attempts are already first-class.
- **Who may connect publish credentials — org `admin`.** Connection management (create/rotate/test `ClientAccountConnection`) is scoped to the org admin role, consistent with `OrganizationIntegration` management.
- **Scheduled-without-dispatcher UX — HIDDEN in v1 UI.** The domain models `Scheduled` + `ScheduledForUtc` and the `Schedule` command/endpoint exist (domain-ready for the future dispatcher), but the v1 UI does **not** expose a "Schedule" action — it would create publications nothing fires. The Planner shows a `Scheduled` column only if a scheduled publication exists; v1 user-facing actions are publish-now / retry / cancel only. See §10.

## 16. Reconciliation with the earlier Content Studio handoff contract

The unmerged `content-studio` publishing delta (`openspec/changes/content-studio/specs/publishing/spec-delta.md`, never archived — `openspec/specs/publishing/spec.md` is still the TBD stub) defined a **push-based** handoff: Content Studio emits a versioned `PublicationHandoffPayload` through an `IPublicationHandoff` port and marks the item `HandedOff`, with the pipeline consuming a self-contained payload and never re-reading Content Studio.

This change adopts a **pull-by-id** model instead: `Publication` references the `ContentItem` by soft `Guid`, the Application command gates read-only on `ReviewState ∈ {Approved, ReadyForPublication}`, and assets/caption are resolved at publish time via the existing `IGeneratedAssetStorage` / `ContentDraft`. Consequences, made explicit:

- The `PublicationHandoffPayload` / `IPublicationHandoff` contract is **superseded and deprecated** by this change and is NOT implemented. At archive, the older publishing delta must not be merged into `openspec/specs/publishing/spec.md`; this change's `specs/publishing/spec.md` is the canonical publishing spec.
- `ContentItem.MarkHandedOff` and the `ReviewState.HandedOff` terminal state are **not reached** by the publication flow (the axes are orthogonal; publishing never mutates `ReviewState`). They remain in the domain as a harmless no-longer-driven state; removing them is out of scope for this change (would touch the Content Studio aggregate). The ADR (§14) records this supersession so the dead state is not mistaken for an integration seam.
- Rationale for pull-by-id over push-payload: no event/outbox infrastructure is needed for v1, the item's current editorial state is always read fresh at publish time (no stale-payload risk), and multi-destination + re-publish (each a new `Publication`) map naturally onto pulling the same item N times rather than emitting N payloads.
