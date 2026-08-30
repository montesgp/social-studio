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

### Requirement: CB-05 — Orphaned Pieces Are Identified in the Bank

A piece whose owning `ContentSet` cannot be resolved through set membership is ORPHANED: Content Studio cannot open it and no editorial action on it is meaningful. The Bank MUST identify such a piece on its card, and MUST offer permanent deletion as the resolution instead of editorial actions that cannot complete.

#### Scenario: An orphaned piece is marked and offers only deletion

- GIVEN a content item that appears in the Bank listing with no resolvable owning content set
- WHEN the Bank renders that item's card
- THEN the card is marked as orphaned with the reason that it has no content set and cannot be opened
- AND the card offers permanent deletion instead of approve/reject/stage/publish

### Requirement: CB-06 — Permanent Deletion Removes Every Trace of a Piece

Deleting a content item MUST be a purge: the item row, its stored asset objects, its publication records and its set-membership rows are all removed, so the deletion reclaims space rather than leaving unreachable rows and blobs behind. Deletion MUST be available for a single piece and across a Bank selection, and the bulk path MUST report per-item outcomes rather than failing the whole batch.

A piece carrying a LIVE publication (Scheduled, Publishing or Published) MUST be refused, because the purge is irreversible and would destroy the record of what actually reached a network. Publication records that never reached a network (PublishFailed, Cancelled) are purged with the item.

#### Scenario: Deleting a piece removes its assets, publications and membership

- GIVEN a content item that belongs to a set, has stored assets and has a cancelled publication record
- WHEN it is deleted
- THEN the item, its stored asset objects, its publication records and its set-membership rows are all removed

#### Scenario: A published piece is refused and left intact

- GIVEN a content item with a publication in the Published state
- WHEN it is deleted
- THEN the deletion is refused with an explicit error
- AND the item, its assets and its publication records are left intact

#### Scenario: Bulk deletion reports per-item outcomes

- GIVEN a selection of Bank pieces where some carry a live publication
- WHEN the selection is deleted in bulk
- THEN the pieces without a live publication are purged
- AND the refused pieces remain in the Bank
- AND the response reports the succeeded and failed counts with a per-item error code

### Requirement: CB-07 — A Bank Selection Opens as One Content Studio Session

An operator MUST be able to select several Bank pieces and open them together in a single Content Studio session, whose left rail lists every selected piece in the order it was selected. The selection MAY span content sets, so the session is NOT set-scoped: no owning `ContentSet` is loaded and set-scoped actions (the presentation export) stay unavailable. Orphaned pieces are excluded from the session and reported, since they cannot be opened at all.

#### Scenario: A cross-set selection opens in one session

- GIVEN a Bank selection of pieces belonging to different content sets
- WHEN the operator opens the selection in Content Studio
- THEN Content Studio opens once with every selected piece listed in the requested order
- AND no owning content set is required or loaded

#### Scenario: Orphaned pieces are left out of the session

- GIVEN a selection that includes at least one orphaned piece and one openable piece
- WHEN the selection is opened in Content Studio
- THEN only the openable pieces are loaded
- AND the operator is told the orphaned pieces were left out because they can only be deleted

### Requirement: CB-08 — Previews Distinguish Loading From Unavailable

A Bank card MUST distinguish a preview that has not arrived yet from one that never will. While an asset is being fetched the card shows a loading affordance; when the asset cannot be served the card shows an explicit unavailable placeholder. A failed asset MUST NOT fall back to the text preview, which would make a broken visual piece indistinguishable from a text piece.

Asset fetches MUST be deferred until the card approaches the viewport, so opening the Bank for a client with a large history does not issue one request per piece at once.

#### Scenario: A piece whose asset cannot be served is marked unavailable

- GIVEN a visual content item whose stored asset cannot be retrieved
- WHEN its Bank card renders
- THEN the card shows an explicit "preview unavailable" placeholder
- AND it does NOT render the item's text as if it were a text piece

## Non-Requirements (v1)

- No background/on-generation-completion materialization — lazy-on-load only.
- No bulk editorial actions (approve/reject/stage) across a Bank selection — bulk deletion only.
- No recovery path that re-adopts an orphaned piece into a content set; deletion is the only resolution.
- No metrics/insights aggregation on Bank data.
