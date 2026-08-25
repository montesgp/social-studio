# Spec Delta: Publishing — Inbound Content Handoff Contract

<!-- Change: content-studio (Phase 1 defines the inbound contract; the pipeline consumer is a separate future change) -->
<!-- Applies to: openspec/specs/publishing/spec.md (additive delta; publishing spec is currently a stub) -->

## Purpose

Define the **inbound handoff contract** that the (future) publication pipeline consumes from Content Studio. Content Studio's responsibility ends when an approved item is staged and a handoff is emitted; the publishing capability owns everything after that (draft/canvas creation, provider send, status tracking — per the `publication-pipeline` stub). This delta specifies WHAT crosses the boundary and the guarantees on it, so the pipeline can be built against a stable contract. It does NOT implement publishing.

## PRD Traceability

| Requirement ID | Anchor |
|---|---|
| PUB-HANDOFF-01 | PRD §6 step 8 "send publication draft to publishing provider"; SDD §6 event between `CanvasDraftCreated` and `PublicationRequested` |
| PUB-HANDOFF-02 | SDD §5 responsibility split "Asset/Canvas Creation" → "Publishing" |

---

## ADDED Requirements

---

### Requirement: PUB-HANDOFF-01 — Publishing Consumes a Versioned PublicationHandoffPayload

The publishing capability MUST accept content from Content Studio only as a versioned `PublicationHandoffPayload` emitted through the `IPublicationHandoff` port. The payload MUST carry: `SchemaVersion`, `OrganizationId`, `ClientId`, `ContentItemId`, the source `ContentOrigin` (for end-to-end traceability), `FormatKey`, `TargetChannel` (network + placement), the final `Copy` (when textual), the final rendered `Assets` (storage URIs + MIME), an optional `SchedulingHint`, and approval metadata (`ApprovedAtUtc`, `ApprovedByUserId`). The pipeline MUST be able to branch on `SchemaVersion` for forward compatibility.

#### Scenario: A handed-off item delivers a complete payload (PUB-HANDOFF-01-a)

- GIVEN an approved content item staged for publication
- WHEN the handoff is emitted
- THEN the payload has `SchemaVersion = 1` and carries org/client/item ids, origin, format key, target channel, final copy, final asset references, and approval metadata
- AND the payload is sufficient for the pipeline to create a publication draft without re-reading Content Studio internals

#### Scenario: Pipeline can branch on schema version (PUB-HANDOFF-01-b)

- GIVEN a payload with a `SchemaVersion` field
- WHEN a future payload increments the version with additive fields
- THEN a consumer built for version 1 can still read the version-1 fields it understands
- AND no consumer is required to re-read Content Studio aggregates to interpret the payload

---

### Requirement: PUB-HANDOFF-02 — Content Studio Does Not Publish

Content Studio MUST NOT create publication drafts, call publishing providers, or schedule content. Its terminal responsibility is emitting the handoff payload and marking the item `HandedOff`. All scheduling and provider interaction belong to the publishing capability.

#### Scenario: Handoff is the boundary (PUB-HANDOFF-02-a)

- GIVEN a content item transitioning to `ReadyForPublication`
- WHEN the handoff payload is emitted and the item becomes `HandedOff`
- THEN Content Studio has not contacted any publishing or scheduling provider
- AND any provider send is performed later by the publishing capability, not by Content Studio

---

## MODIFIED Requirements

None. The `publishing` spec is currently a stub (`Capabilities: TBD`); this delta adds the inbound contract without modifying existing requirements.

## REMOVED Requirements

None.
