# Content Bank Spec

<!-- Change: content-bank-planner (v1 — client-wide read projection over ContentStudio) -->

## Purpose

Define the **Content Bank**: a client-wide, cross-set, cross-origin read projection over `ContentItem` that lets a content operator see everything reviewable/reviewed for a client in one place. The Bank is read-oriented; it introduces no new editorial state and does not replace any Content Studio behavior. It also owns **lazy materialization on load**, so content from analyses nobody has manually opened still appears.

Out of scope for v1: background/on-completion materialization, bulk actions across a Bank selection, metrics/insights.

## Requirements

### Requirement: CB-01 — Client-Wide Listing Across All Sets and Origins

The system MUST expose a listing capability that returns all `ContentItem`s for a given `(OrganizationId, ClientId)` regardless of `ContentSet`, `ContentOrigin.GeneratorKind`, or which set/run they belong to — including content produced by the Single Analyzer. Existing set-scoped and run-scoped listings MUST remain unchanged and available alongside the new client-wide listing.

#### Scenario: Bank lists items across multiple sets and origins

- GIVEN a client with content items belonging to two different `ContentSet`s and two different `GeneratorKind` origins (including Single Analyzer)
- WHEN the client-wide Bank listing is requested for that client
- THEN the response includes items from both sets and both origins
- AND no item for that client is excluded due to its set or origin

#### Scenario: Bank listing is scoped to the requesting client only

- GIVEN two clients within the same organization, each with content items
- WHEN the Bank listing is requested for client A
- THEN only client A's items are returned
- AND no item belonging to client B appears in the response

### Requirement: CB-02 — Date-Range and State Filtering

The Bank listing MUST accept an optional date-range filter (matching on the item's relevant timestamp) and an optional `ReviewState` filter. When no date range is supplied, the full client history MUST remain queryable — the UI MAY apply a default recency weighting (e.g., last 7 days), but this MUST be a presentation concern, not a query-level exclusion of older content.

#### Scenario: Filtering by state returns only matching items

- GIVEN a client with items in `Draft`, `Approved`, and `Rejected` states
- WHEN the Bank listing is requested with a state filter of `Approved`
- THEN only `Approved` items are returned

#### Scenario: Filtering by date range narrows results

- GIVEN a client with items spanning several months
- WHEN the Bank listing is requested with an explicit date range covering one week
- THEN only items whose relevant timestamp falls inside that week are returned

#### Scenario: No date range still returns full history

- GIVEN a client with items older than 7 days and items within the last 7 days
- WHEN the Bank listing is requested with no date-range filter
- THEN items older than 7 days are included in the response
- AND any last-7-days emphasis is applied by the UI, not by excluding rows from the query result

### Requirement: CB-03 — Thumbnail Resolution Reuses the Studio Visual Markup Resolver

Each item returned by the Bank listing MUST carry a resolved thumbnail/background reference produced by the existing Studio visual markup resolution mechanism. The Bank MUST NOT introduce a second, divergent thumbnail-resolution path.

#### Scenario: Bank item exposes a resolved thumbnail

- GIVEN a content item with a background/visual markup reference
- WHEN it is returned in a Bank listing
- THEN the item carries a resolved thumbnail reference produced by the same resolver Content Studio uses

### Requirement: CB-04 — Lazy Materialization on Bank Load

Opening the Bank for a client MUST trigger materialization of any completed generation analyses for that client that have not yet produced `ContentItem`s, reusing the existing materialization capability. Materialization MUST remain idempotent: re-opening the Bank MUST NOT create duplicate items for an already-materialized analysis. No background worker fires materialization in v1.

#### Scenario: Un-opened analysis appears after first Bank load

- GIVEN a client with a completed analysis that has never been opened in Content Studio and has no materialized `ContentItem`s
- WHEN the Bank is opened for that client
- THEN the analysis is materialized into a `ContentSet` and its `ContentItem`s
- AND those items appear in the same Bank listing response

#### Scenario: Repeated Bank loads do not duplicate items

- GIVEN a client whose completed analysis was already materialized on a prior Bank load
- WHEN the Bank is opened again for that client
- THEN no duplicate `ContentSet` or `ContentItem`s are created
- AND the same materialized items are returned

## Non-Requirements (v1)

- No background/on-generation-completion materialization — lazy-on-load only.
- No bulk actions across a Bank selection.
- No metrics/insights aggregation on Bank data.
