# Publishing Spec

<!-- Change: content-bank-planner (v1 — first real slice; supersedes the "TBD" stub at openspec/specs/publishing/spec.md) -->

## Purpose

Define the **publication axis**: how an already-approved piece of content is scheduled or pushed, right now, to a real social network destination, and tracked through a guarded status lifecycle — without ever mutating the editorial axis owned by Content Studio. v1 delivers Instagram only, publish-now only (no timed dispatcher), behind a channel-agnostic connector port.

## Requirements

### Requirement: PUB-01 — Publication Is a Separate Aggregate, Referencing the Item Softly

The system MUST model a `Publication` as its own aggregate root, persisted in its own table, distinct from `ContentItem`. Each publish action MUST create a new `Publication`; the system MUST NOT enforce uniqueness on `(ContentItemId, PublicationDestination)` — the same item MAY be published to the same destination more than once (re-publish), and each such publication is an independent record with its own lifecycle and attempt history. Creating a `Publication` MUST reference the source `ContentItem` by id only (soft reference); it MUST NOT copy, own, or require write access to `ContentDraft` or any other Content Studio internal.

#### Scenario: A new destination for the same item creates a second Publication

- GIVEN a `ContentItem` that already has a `Publication` targeting destination A
- WHEN a `Publication` is created for the same item targeting a different destination B
- THEN a second, independent `Publication` record exists
- AND the original `Publication` for destination A is unaffected

#### Scenario: Re-publishing the same item to the same destination creates a new Publication

- GIVEN a `ContentItem` with an existing `Published` (or `Cancelled`/`PublishFailed`) `Publication` for a given destination
- WHEN a `Publication` is created again for the same item and the same destination
- THEN a new, independent `Publication` record is created
- AND the earlier `Publication` for that destination is unaffected (its status and attempt history are preserved)

### Requirement: PUB-02 — Guarded Publication Status Lifecycle

The system MUST model publication status as an enumeration (`Scheduled`, `Publishing`, `Published`, `PublishFailed`, `Cancelled`) with transitions enforced by an explicit, table-driven transition guard (mirroring `ReviewStateTransitions`). Legal transitions are: `Scheduled → Publishing`; `Publishing → Published` (terminal); `Publishing → PublishFailed`; `PublishFailed → Publishing` (manual retry); `Scheduled → Cancelled`; `PublishFailed → Cancelled`. Any other transition MUST be rejected with a structured domain error, never a silent state change or an exception. `Published` and `Cancelled` MUST be terminal.

#### Scenario: Publish-now enters directly at Publishing

- GIVEN an `Approved`/`ReadyForPublication` content item and a chosen destination
- WHEN a publish-now `Publication` is created
- THEN the `Publication` starts in state `Publishing` (no `Scheduled` step)

#### Scenario: Successful send transitions to Published

- GIVEN a `Publication` in state `Publishing`
- WHEN the connector reports a successful send
- THEN the `Publication` transitions to `Published`
- AND a `PublicationAttempt` recording success is appended

#### Scenario: Failed send transitions to PublishFailed and allows manual retry

- GIVEN a `Publication` in state `Publishing`
- WHEN the connector reports a failure
- THEN the `Publication` transitions to `PublishFailed`
- AND a `PublicationAttempt` recording the error is appended
- AND a subsequent manual retry transitions the `Publication` back to `Publishing`

#### Scenario: Illegal transition is rejected

- GIVEN a `Publication` in state `Published`
- WHEN any further transition is attempted
- THEN the operation returns a failed result
- AND the state remains `Published`

#### Scenario: Cancellation is only allowed from Scheduled or PublishFailed

- GIVEN a `Publication` in state `Publishing`
- WHEN cancellation is attempted
- THEN the operation returns a failed result
- AND the state remains `Publishing`

#### Scenario: A publication stuck in Publishing can be reclaimed to PublishFailed

- GIVEN a `Publication` in state `Publishing` whose publish attempt did not complete (the process crashed before the connector callback returned)
- WHEN an operator reclaim action is invoked for that publication
- THEN the `Publication` transitions to `PublishFailed`
- AND the currently open attempt is closed as failed with a reclaim reason
- AND the publication can subsequently be retried or cancelled

### Requirement: PUB-03 — Two Entry Paths: Scheduled and Publish-Now

The system MUST support creating a `Publication` through two distinct entry paths: (a) **scheduled**, which starts in state `Scheduled` and carries a non-null `ScheduledForUtc`, and (b) **publish-now**, which starts directly in state `Publishing` with no `ScheduledForUtc` requirement. v1 delivers the publish-now path end-to-end. The `Scheduled` state and `ScheduledForUtc` field MUST be modeled and persisted so a later dispatcher is purely additive, but v1 MUST NOT include any background process that transitions `Scheduled → Publishing` automatically.

#### Scenario: Scheduled entry stores ScheduledForUtc without firing

- GIVEN a scheduled `Publication` created with a future `ScheduledForUtc`
- WHEN time passes and no dispatcher exists
- THEN the `Publication` remains in state `Scheduled`
- AND `ScheduledForUtc` is persisted unchanged

#### Scenario: Publish-now requires no scheduled time

- GIVEN a publish-now `Publication` creation request
- WHEN the request omits `ScheduledForUtc`
- THEN the `Publication` is created successfully in state `Publishing`

### Requirement: PUB-04 — Destination Resolves Org-Level Access and Per-Client Account Credentials

A `PublicationDestination` MUST identify both a client account and a target social network. Resolving a destination MUST combine two credential levels: the org-level `OrganizationIntegration` (developer/API app access) and a per-client account credential (the specific client account's own token/external account id for that network). Creating or retrying a `Publication` MUST fail with a structured error if either level is missing or not connected, before any provider call is attempted.

#### Scenario: Destination resolves successfully when both levels are connected

- GIVEN an org with a connected Instagram `OrganizationIntegration` and a client account with connected Instagram credentials
- WHEN a `PublicationDestination` is resolved for that client account and network
- THEN resolution succeeds and returns both the org-level access and the per-client account credential

#### Scenario: Missing per-client credential blocks Publication creation

- GIVEN an org with a connected Instagram `OrganizationIntegration` but a client account with no Instagram credentials configured
- WHEN a `Publication` is requested for that client account and network
- THEN the operation fails with a structured error identifying the missing per-client credential
- AND no provider call is made

### Requirement: PUB-05 — Publication Connector Is Channel-Agnostic; Provider Types Never Leak

The system MUST define an `IPublicationConnector` port in the Application layer, independent of any specific social network SDK or API shape. The first implementation MUST be a native Instagram Graph API adapter in Infrastructure. No Instagram/Graph API type MUST be visible from Domain or Application; the port MUST accept and return only domain/application types.

#### Scenario: Instagram adapter implements the port without leaking provider types

- GIVEN the compiled Application assembly
- WHEN `IPublicationConnector` and its usages are inspected
- THEN no Instagram Graph API or HTTP SDK type appears in the signature
- AND the Instagram adapter implementing it lives in Infrastructure

#### Scenario: Adding a second network requires no port change

- GIVEN `IPublicationConnector` already implemented by the Instagram adapter
- WHEN a second network adapter is added behind the same port (future slice)
- THEN no change to the port signature or to Domain/Application types is required

### Requirement: PUB-06 — Publication Never Mutates the Editorial Axis

Creating, transitioning, or retrying a `Publication` MUST NOT write to `ContentItem.ReviewState`, `ContentDraft`, or any other Content Studio internal. The publication axis and the editorial axis are orthogonal; a `Publication` reads a `ContentItem`'s id and current state to gate creation but never writes back to it.

#### Scenario: Publishing an item does not change its ReviewState

- GIVEN an `Approved` content item
- WHEN a `Publication` is created and progresses through `Publishing → Published`
- THEN the content item's `ReviewState` remains `Approved`
- AND no field on `ContentItem` or `ContentDraft` is modified by the publication flow

#### Scenario: Only Approved or ReadyForPublication items can start a Publication

- GIVEN a content item in state `Draft`
- WHEN creation of a `Publication` for that item is attempted
- THEN the operation fails with a structured error
- AND no `Publication` is created

## Non-Requirements (v1 — explicitly out of scope)

- No Meta adapter or any network other than Instagram.
- No scheduler/dispatcher worker that fires `Scheduled → Publishing` automatically.
- No automatic retry; retry of `PublishFailed` is a manual, explicit action only.
- No Blotato multi-network consolidation.
- No post-publish metrics/insights capture.
- No bulk scheduling or bulk publish across a selection.
