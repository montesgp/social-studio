# Spec Delta: Content Studio — Client-Wide Listing Seam

<!-- Change: content-bank-planner -->
<!-- Applies to: openspec/specs/content-studio/spec.md (additive delta; no editorial behavior change) -->

## Purpose

Expose a client-wide listing seam on `IContentItemRepository` so the Content Bank capability can query all items for a client across every `ContentSet` and `ContentOrigin` without duplicating query logic or bypassing the repository abstraction. This is purely additive: no existing repository method, `ReviewState` behavior, or editorial rule changes.

## ADDED Requirements

### Requirement: CS-BANK-01 — Client-Wide Listing Method on IContentItemRepository

`IContentItemRepository` MUST expose a new listing method that returns `ContentItem`s scoped to `(OrganizationId, ClientId)` only — independent of `ContentSet` or `ContentOrigin.GeneratorKind` — with optional date-range and `ReviewState` filter parameters. The EF adapter implementing `IContentItemRepository` MUST implement this method in the same change (architecture gate: interface and adapter change together). Existing methods (`GetByIdAsync`, `ListByRunAsync`, `ListBySetAsync`) MUST remain unchanged in signature and behavior.

#### Scenario: New method returns items across sets and origins

- GIVEN a client with items across two different `ContentSet`s
- WHEN the new client-wide listing method is called for that client
- THEN items from both sets are returned in a single result

#### Scenario: Existing repository methods are unaffected

- GIVEN the `IContentItemRepository` interface after this change
- WHEN `GetByIdAsync`, `ListByRunAsync`, and `ListBySetAsync` are invoked as before
- THEN each returns the same result shape and behavior as before this change

## MODIFIED Requirements

None. This delta only adds a new repository method; no existing `content-studio` requirement is altered.

## REMOVED Requirements

None.
