# ADR 0005 - Publication Axis and Two-Level Credential Model

## Status
Accepted

## Context

Content Studio (ADR 0004) delivers an editorial working surface that ends at `ReadyForPublication`, but the publishing side of the pipeline was still a stub: `openspec/specs/publishing/spec.md` was a TBD placeholder, and the only prior design for the boundary was an **unmerged** Content Studio delta (`openspec/changes/content-studio/specs/publishing/spec-delta.md`) that sketched a push-based handoff (`IPublicationHandoff` port emitting a versioned `PublicationHandoffPayload`, item marked `HandedOff`). That contract was never implemented and never archived.

`content-bank-planner` closes this gap: a client-scoped **Content Bank** (cross-set, cross-origin view of reviewable/approved items) plus a **Planner** that can publish approved items to Instagram now (v1 is publish-now only; scheduling is modeled but not dispatched — see PRD/SDD roadmap). Two design questions had to be resolved before any code:

1. **Where does the publication lifecycle live?** Content Studio's `ReviewState` is an editorial axis (`Draft → InReview → Edited → Approved → ReadyForPublication`, plus `Rejected`/`Discarded`/`HandedOff`). Publishing introduces its own lifecycle (`Scheduled/Publishing/Published/PublishFailed/Cancelled`) with its own retry/attempt semantics, and an item can be published to **multiple destinations**, each progressing independently. Bolting this onto `ReviewState` would collapse two independently-evolving state machines into one and cannot express "N publications for 1 item."
2. **How does the platform authenticate the actual Instagram publish call?** The existing `OrganizationIntegration` model (ADR-adjacent, org-level developer app / API access resolved via `IIntegrationResolver`) identifies which provider an organization has configured, but carries no notion of *which specific client Instagram account* to publish to, nor a place to store that account's own access token.

Both are architecture changes per `require_adr_for_architecture_changes: true`, and they are coupled: the `Publication` aggregate's `PublicationDestination` needs a credential to resolve against, so the two decisions had to be designed together (see `openspec/changes/content-bank-planner/design.md` §3-4, §6).

## Decision

### 1. Two-axis model: `Publication` is a separate aggregate, orthogonal to `ReviewState`

Introduce `Domain/Social/Publications/Publication` as a **new, independent aggregate** with its own table (`social.publications`, plus an owned `social.publication_attempts` collection) and its own guarded status machine (`PublicationStatus` + explicit `PublicationStatusTransitions`: `Scheduled→Publishing→{Published|PublishFailed}`, `PublishFailed→{Publishing (retry), Cancelled}`, `Scheduled→Cancelled`). `Publication` references its source `ContentItem` by a **soft `Guid`** (`ContentItemId`) — no EF navigation, no FK from `publications.content_item_id` to `content_studio_items` — mirroring the soft-reference discipline already established for `ContentOrigin` (ADR 0004) and `ContentPlan.CalendarSlot.ProducedPackageId` (ADR 0002).

Publishing a `ContentItem` **never mutates** `ReviewState`: the publish command loads the item read-only, gates on `ReviewState ∈ {Approved, ReadyForPublication}` (`ItemNotPublishable` otherwise), and the two axes evolve independently thereafter. An item can accumulate any number of `Publication` records — one per publish action, including re-publishes and multi-destination fan-out — because there is intentionally **no uniqueness constraint on `(content_item_id, destination)`** (resolved product decision, design §15: republish is allowed and creates a new independent `Publication`).

**Rejected alternative**: add publication states directly onto `ReviewState` (e.g. extend it with `Publishing`/`Published`/`PublishFailed`). Rejected because it would (a) couple two lifecycles that evolve on different triggers and different failure semantics (editorial review vs. network I/O with retries), (b) make "one item, one state" incompatible with multi-destination publishing, and (c) force every future publishing concern (attempts, scheduling, retries) into the Content Studio bounded context, which ADR 0004 explicitly isolated.

### 2. Two-level credential model: org integration + per-client account connection

Publishing a specific client's content to a specific Instagram account requires **two levels of credential**, modeled as two aggregates:

- **Org level (existing)**: `OrganizationIntegration` — the organization's developer app / API access for a provider, resolved via the existing `IIntegrationResolver`. Unchanged by this decision; it answers "does this org have Instagram API access at all."
- **Client level (new)**: `Domain/Social/ClientAccountConnections/ClientAccountConnection` — one row per `(organization_id, client_id, social_network_id)`, holding the target `ExternalAccountId`, an **encrypted** access token (`IIntegrationSecretProtector.Protect`, never persisted in plaintext, never returned by any query/DTO), an expiry, and a `Connected/Failed/Disabled` status with `LastTestedAtUtc`/`LastError`. It answers "which Instagram account does *this client* publish to, and with what credential."

`PublicationDestinationResolver` (Application layer) composes both: it resolves the org-level integration first (fails with `DestinationUnresolved` if not `Connected`, reusing the existing `IntegrationNotConnected` error from `ResolveProviderAsync`), then resolves the client-level `ClientAccountConnection` (fails with `DestinationUnresolved` if missing), and only then builds a `ResolvedPublicationRequest` that crosses the `IPublicationConnector` port — the unprotected token exists only inside that resolution step and the connector call, never surfacing to a response DTO.

**Rejected alternatives**:
- **(a) `ConnectorId` alone** (org + provider, no client dimension) — cannot express *which* client Instagram account to publish to when an org manages many clients on the same network; every client would collide on one org-level credential.
- **(b) Extending `ClientOwnAccount`** (the existing reference-only "this client owns this external account" record) — `ClientOwnAccount` is a lightweight reference model (account handle/id for analysis purposes), not a secret store; overloading it with encrypted tokens, expiry, and connection-health status mixes two different responsibilities and would force every future analysis-only consumer of `ClientOwnAccount` to reason about credential lifecycle.

Both decisions are additive: `social.publications`, `social.publication_attempts`, and `social.client_account_connections` are new tables via one migration (`Add_Publications`); no existing table is modified.

## Consequences

- The publication lifecycle is a first-class, independently-testable domain (`PublicationStatusTransitionsTests`, `PublicationTests`, an explicit orthogonality test asserting `Domain/Social/Publications/**` never imports `Domain/Social/ContentStudio` types) — mirroring the `ProfileAnalysis` (ADR 0003) and Content Studio (ADR 0004) isolation precedent.
- Multi-destination and re-publish are native, not retrofitted: each publish action is its own `Publication` row with its own attempt history; there is no state to reconcile across destinations.
- The credential model cleanly separates "org has API access" from "client account + secret," so relaxing the v1 constraint of one connection per `(org, client, network)` to multiple accounts later is an additive migration (drop the unique index + a destination-picker UX change) — no rework of the aggregate boundary.
- `ContentItem.MarkHandedOff` / `ReviewState.HandedOff` are **not** reached by this flow (see §16 reconciliation below) and remain dead-but-harmless in the Content Studio aggregate; removing them is explicitly out of scope for this change.
- Hexagonal boundaries hold: `Publication`, `PublicationStatus`, `ClientAccountConnection`, `PublicationDestination`, `PublicationAttempt` are pure domain; `IPublicationConnector` is the only crossing into a network-specific adapter (`InstagramGraphPublicationConnector`); EF mapping/enum conversions/ciphertext columns live only in Infrastructure.
- Cost: two new aggregates, three new tables, one new port + one connector, and a frontend `/publications` workspace — justified by turning "publishing" from a stub into a working v1 slice while keeping the door open for the deferred roadmap (scheduler/dispatcher, additional networks, background materialization, multi-network consolidation).

## Reconciliation with the earlier Content Studio handoff contract (supersession)

The unmerged Content Studio publishing delta defined a **push-based** handoff: reaching `ReadyForPublication` would emit `ContentApprovedForPublication` carrying a versioned `PublicationHandoffPayload` through an `IPublicationHandoff` port, with the item marked `HandedOff` and the pipeline consuming a self-contained payload without re-reading Content Studio.

This change adopts a **pull-by-id** model instead: `Publication` references the `ContentItem` by soft `Guid`; the publish command gates read-only on `ReviewState ∈ {Approved, ReadyForPublication}`; assets and copy are resolved at publish time via the existing `IGeneratedAssetStorage` / `ContentDraft`, always reading the item's current state rather than a payload captured at handoff time.

- The `PublicationHandoffPayload` / `IPublicationHandoff` contract is **superseded and deprecated** by this decision and was never implemented. The older Content Studio publishing delta (`openspec/changes/content-studio/specs/publishing/spec-delta.md`) must NOT be merged into `openspec/specs/publishing/spec.md` at archive time; this change's `specs/publishing/spec.md` is the canonical publishing spec.
- `ContentItem.MarkHandedOff` and the `ReviewState.HandedOff` terminal state are not reached by the publication flow (the two axes are orthogonal — publishing never mutates `ReviewState`). They remain in the domain as a harmless, no-longer-driven state; removing them is out of scope for this change (it would touch the Content Studio aggregate, not the publishing one).
- Rationale for pull-by-id over push-payload: no event/outbox infrastructure is required for v1, the item's current editorial state is always read fresh at publish time (no stale-payload risk), and multi-destination + re-publish (each a new `Publication`) map naturally onto pulling the same item N times rather than emitting N payloads upfront.

## Related
- Proposal: `openspec/changes/content-bank-planner/proposal.md`
- Design: `openspec/changes/content-bank-planner/design.md` (§2-4, §6-7, §14, §16)
- Specs: `openspec/changes/content-bank-planner/specs/content-bank/spec.md`, `openspec/changes/content-bank-planner/specs/publishing/spec.md`
- Deltas: `openspec/changes/content-bank-planner/specs/content-studio/spec-delta.md`, `openspec/changes/content-bank-planner/specs/integrations/spec-delta.md`
- Builds on: ADR 0002 (Content Plan soft package references), ADR 0003 (Profile Analysis bounded-context isolation), ADR 0004 (Content Studio review model — editorial axis this decision stays orthogonal to)
- Supersedes: the unmerged `content-studio` publishing handoff delta (`openspec/changes/content-studio/specs/publishing/spec-delta.md`) — not implemented, not to be archived
- Downstream (deferred): scheduler/dispatcher worker for `Scheduled → Publishing`, additional network connectors (Meta/TikTok/YouTube/X) behind `IPublicationConnector`, background materialization on generation completion, Blotato multi-network consolidation, post-publish metrics/insights
