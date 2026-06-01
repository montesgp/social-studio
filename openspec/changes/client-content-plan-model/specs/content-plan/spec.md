# Content Plan Specification

<!-- PRD traceability: link PRD section when assigned -->
<!-- ADR required: docs/architecture/adr/ — architecture change -->

## Purpose

Define the Content Plan: the per-period, persisted, regenerable working surface that
a community manager uses before generating content. It succeeds `ClientMonthlyObjective`
as the per-period context aggregate and introduces Period, Provenance, Content Strategy,
and Content Calendar as first-class domain concepts.

---

## Requirements

### Requirement: CP-01 — Period Value Object

The system MUST represent a content planning period as a typed Value Object.
The monthly variant MUST carry `Year` (1–9999) and `Month` (1–12) and MUST be
the only implemented variant in this change. The VO MUST be designed to accommodate
future cadences without requiring changes to the Content Plan aggregate identity.

#### Scenario: Valid monthly period accepted

- GIVEN Year = 2026 and Month = 3
- WHEN a Period is constructed
- THEN the Period is valid and its string representation is stable and deterministic

#### Scenario: Invalid month rejected

- GIVEN Year = 2026 and Month = 13
- WHEN a Period is constructed
- THEN construction fails with a validation error

#### Scenario: Invalid year rejected

- GIVEN Year = 0 and Month = 6
- WHEN a Period is constructed
- THEN construction fails with a validation error

---

### Requirement: CP-02 — Provenance Value Object

The system MUST decorate each generatable node (Objective, Content Strategy,
Content Calendar, and individual calendar slots where applicable) with a
`Provenance` value containing:
- `Source`: Manual | AI
- `GeneratedAt`: UTC timestamp (required when Source = AI, optional otherwise)
- `Status`: one of Empty | Draft | Generated | Edited
- `PromptVersion`: optional string, captured when Source = AI

Provenance is an audit trace ONLY. It MUST NOT block reads, writes, or any
downstream operation based on its values.

#### Scenario: Manual provenance on user-written node

- GIVEN a user saves an Objective text directly (no AI involved)
- WHEN the Objective node is persisted
- THEN its Provenance.Source is Manual and Provenance.Status is Draft or Edited

#### Scenario: AI provenance on AI-filled node

- GIVEN the AI fills the Content Strategy node
- WHEN the node is persisted
- THEN Provenance.Source is AI, Provenance.GeneratedAt is set, and Provenance.Status is Generated

#### Scenario: User edits AI-generated node

- GIVEN a Content Strategy node with Provenance.Source = AI
- WHEN the user manually edits the text and saves
- THEN Provenance.Source becomes Manual and Provenance.Status becomes Edited

#### Scenario: Provenance does not block reads

- GIVEN a Content Plan whose Content Strategy has Provenance.Status = Draft
- WHEN the Content Plan is fetched
- THEN the response includes all nodes regardless of their Provenance values

---

### Requirement: CP-03 — Content Plan Aggregate (Upsert Semantics)

The system MUST persist a Content Plan keyed by `(client_id, Period)` with
upsert semantics: creating if absent, overwriting if present. The Content Plan
owns three nodes, each with Provenance:
- **Objective**: period objective text (non-empty, max 3000 chars) + KPIs (max 20 items)
- **Content Strategy**: per-period strategy text (optional), manually editable and AI-fillable
- **Content Calendar**: ordered list of slots (may be empty on initial save)

The Content Plan MUST reference Generated Content Package IDs as foreign references
only — it MUST NOT own package internals.

#### Scenario: Create Content Plan for new period

- GIVEN a client with no Content Plan for Period (2026, 6)
- WHEN a upsert command is submitted with a valid Objective text and no calendar slots
- THEN a new Content Plan is created for that (client, Period) pair
- AND the Objective node carries the submitted text with Provenance.Source = Manual

#### Scenario: Overwrite existing Content Plan

- GIVEN a client with an existing Content Plan for Period (2026, 6)
- WHEN a upsert command is submitted with updated Objective text
- THEN the Content Plan is overwritten (all fields replaced) and UpdatedAtUtc advances

#### Scenario: Empty Objective rejected

- GIVEN a upsert command with a blank Objective text
- WHEN the command is processed
- THEN the command is rejected with a validation error
- AND no Content Plan is created or modified

#### Scenario: Get by client and period

- GIVEN a client with a Content Plan for Period (2026, 6)
- WHEN a get-by-(client, period) query is issued
- THEN the full Content Plan is returned with all nodes and their Provenance

#### Scenario: Get returns not-found for missing period

- GIVEN a client with no Content Plan for Period (2025, 1)
- WHEN a get-by-(client, period) query is issued
- THEN a not-found result is returned

#### Scenario: List by client returns all periods

- GIVEN a client with Content Plans for Period (2026, 5) and (2026, 6)
- WHEN a list-by-client query is issued
- THEN both Content Plans are returned, each with its full node set

---

### Requirement: CP-04 — Content Calendar Slots

The Content Calendar MUST be an ordered list of slots. Each slot MUST carry:
- `Day`: an integer representing the day-of-month (1–31, validated against the Period's month)
- `ContentFormat`: a non-empty string describing the content format for that slot
- `Provenance`: per-slot Provenance

Slots MAY be empty on initial Content Plan creation. Slot order MUST be preserved.
The list MAY be partially filled (some days without slots).

#### Scenario: Add slots to Content Calendar

- GIVEN an existing Content Plan with an empty calendar
- WHEN a upsert is submitted with three ordered slots for days 1, 8, 15
- THEN all three slots are persisted in that order with their respective ContentFormat

#### Scenario: Day out of range for period rejected

- GIVEN a Content Plan for Period (2026, 2) (February — 28 days in 2026)
- WHEN a upsert is submitted with a slot for day 30
- THEN the command is rejected with a validation error for the invalid day

#### Scenario: Slot order preserved on retrieval

- GIVEN a Content Plan with slots saved in order [day 5, day 12, day 20]
- WHEN the Content Plan is retrieved
- THEN the slots are returned in the same order

---

### Requirement: CP-05 — Content Plan Is a Regenerable Working Surface

The system MUST treat the Content Plan as a working surface subject to deliberate
overwrite by the user. Historical Generated Content Packages that reference this
plan's period MUST NOT be modified, deleted, or re-derived when the Content Plan
is upserted or regenerated.

The system MUST NOT maintain version history of the Content Plan itself.
History is preserved in the immutable Generated Content Package records.

#### Scenario: Content Plan overwrite leaves existing packages intact

- GIVEN a client's Content Plan for Period (2026, 5) with two referenced package IDs
- WHEN a upsert replaces the Content Plan entirely
- THEN the referenced Generated Content Packages are unchanged
- AND the new Content Plan may carry different or empty package references

#### Scenario: No automatic re-generation on plan save

- GIVEN a Content Plan upsert is submitted
- WHEN the command is processed
- THEN no content generation job is triggered automatically
- AND the response confirms the plan save only

---

### Requirement: CP-06 — ClientMonthlyObjective Superseded

The system MUST treat `ClientMonthlyObjective` as superseded by the Content Plan's
Objective node. Once the Content Plan migration is applied, operations that previously
targeted `ClientMonthlyObjective` (upsert, get-by-client-and-month, list-by-client)
MUST be served by the Content Plan endpoints using Period as the key.
The `ClientMonthlyObjective` aggregate and its table MUST NOT coexist as an active
write path after migration.

#### Scenario: Existing monthly objective data preserved

- GIVEN existing records in `social.client_monthly_objectives`
- WHEN the data migration runs
- THEN each record is migrated to a Content Plan Objective node for the corresponding (client, Period)
- AND the migrated Provenance.Source is Manual (data predates AI provenance)
- AND the original uniqueness on (client_id, year, month) is preserved as (client_id, Period)
