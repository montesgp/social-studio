# Spec Delta: Content Plan — Approved Content Item Association Seam

<!-- Change: content-studio (Phase 1 defines the seam; plan-side linkage lands in Phase 4) -->
<!-- Applies to: openspec/specs/content-plan/spec.md (additive delta) -->

## Purpose

Define the **soft, one-directional association** from an approved Content Studio item back to a Content Plan calendar slot, so a future client-facing plan view can show which approved content fulfills each planned slot. This delta is additive and does NOT change the Content Plan aggregate, its persistence, or its write path in Phase 1. The Content Plan continues to reference generation output exactly as today (`CalendarSlot.ProducedPackageId`, CP-04/Q4); this delta only reserves the **item-level** association that Content Studio carries on its side.

## PRD Traceability

| Requirement ID | Anchor |
|---|---|
| CP-LINK-01 | Connects `content-studio` (approved items) to `content-plan` (CP-03/CP-04 calendar slots) for the future plan view |

---

## ADDED Requirements

---

### Requirement: CP-LINK-01 — Approved Items May Carry a Soft Plan Association

A Content Studio `ContentItem` MAY carry an optional `PlanContext` referencing a Content Plan by id, a `Period` value mirror (`kind, year, month`), and a `CalendarSlotId` — all as soft values (no EF FK navigation). This association is carried on the **Content Studio side**; the Content Plan aggregate and `social.content_plans` / `social.content_plan_slots` tables are unchanged by this delta. An absent association MUST be valid (Phase 1 items have none).

The Content Plan MUST NOT be required to load Content Studio items to be read or upserted; the association is resolved at read time when a plan view requests it, consistent with the existing soft-reference, orphan-tolerant discipline (ADR 0002).

#### Scenario: Item without a plan association is valid (CP-LINK-01-a)

- GIVEN a content item materialized from a Single Analyzer run with no plan context
- WHEN the item is read
- THEN its `PlanContext` is absent
- AND the item is fully usable for review, edit, approval, and handoff

#### Scenario: Plan read does not depend on Content Studio (CP-LINK-01-b)

- GIVEN a Content Plan for a client and period
- WHEN the plan is fetched or upserted
- THEN the operation succeeds without loading any Content Studio item
- AND no Content Plan table is modified by the presence or absence of associated items

---

## MODIFIED Requirements

None. This delta is additive; no existing `openspec/specs/content-plan/spec.md` requirement is modified. In particular, CP-03 (Plan references packages by id only), CP-04 (calendar slots), and CP-05 (regenerable working surface; immutable packages) are preserved unchanged.

## REMOVED Requirements

None.
