# General Strategy Specification

<!-- ADR: docs/architecture/adr/0002-client-content-plan-model.md -->

## Purpose

Define the behavior of the enduring, per-client General Strategy — the persistent, 1:1-per-client
record that captures a client's content pillars, tone keywords, target audience, posting frequency,
and notes. Formerly named `ClientStrategy`; renamed `GeneralStrategy` in the
`client-content-plan-model` change without altering the aggregate's invariants or persistence shape.

---

## Requirements

### Requirement: GSTRAT-01 — Enduring Client Strategy Persistence

The system MUST persist exactly one General Strategy record per client (1:1 cardinality),
keyed by `client_id`, containing content pillars (max 10), tone keywords (max 20),
target audience (optional, max 1000 chars), posting frequency (optional, max 200 chars),
and notes (optional, max 2000 chars).
At least one field MUST carry a non-empty value (empty-signal rule).

#### Scenario: Upsert creates General Strategy when none exists

- GIVEN a client with no existing General Strategy record
- WHEN a upsert command is submitted with at least one non-empty field and valid lengths
- THEN a new General Strategy is persisted, scoped to the client's organization
- AND a success result is returned

#### Scenario: Upsert overwrites existing General Strategy

- GIVEN a client with an existing General Strategy
- WHEN a upsert command is submitted with updated content pillars
- THEN the record is updated and `UpdatedAtUtc` advances
- AND the previous values are replaced, not appended

#### Scenario: Empty-signal rejection

- GIVEN a client with no existing General Strategy
- WHEN a upsert command is submitted with all fields null or whitespace
- THEN the command is rejected with an empty-strategy error
- AND no record is persisted

#### Scenario: Field length violations

- GIVEN a upsert command that provides content pillars exceeding 10 items
- WHEN the command is processed
- THEN the command is rejected with a validation error naming the field
- AND no record is persisted or modified

#### Scenario: Get returns current General Strategy

- GIVEN a client with a persisted General Strategy
- WHEN a get-by-client query is issued
- THEN the full General Strategy (all fields) is returned for that client

---

### Requirement: GSTRAT-02 — General Strategy Does Not Auto-Propagate to Content Plans

The system MUST NOT automatically re-derive or update any Content Plan when a
General Strategy is edited. Updates to General Strategy are recorded on that
aggregate only. Any re-alignment with a Content Plan is a deliberate user action.

#### Scenario: General Strategy update does not cascade

- GIVEN a client with a persisted General Strategy and a persisted Content Plan
- WHEN the General Strategy is updated via the upsert command
- THEN only the General Strategy record changes
- AND all existing Content Plans for that client are unchanged

---

### Requirement: GSTRAT-03 — StrategySnapshot Derives from General Strategy at Generation Time

The system MUST derive `StrategySnapshot` from the current state of the General Strategy
at the moment a generation job starts. The snapshot is frozen and immutable for that job.
Changes to General Strategy after job start MUST NOT affect an in-flight snapshot.

#### Scenario: Snapshot captures current General Strategy fields

- GIVEN a client with a General Strategy containing pillars, tone keywords, and target audience
- WHEN a generation job is started
- THEN the resulting StrategySnapshot contains those field values as of job-start time
- AND Notes are NOT included in the snapshot (Notes is internal context only)

#### Scenario: Post-start edit does not mutate in-flight snapshot

- GIVEN a generation job in progress with a frozen StrategySnapshot
- WHEN the General Strategy is updated concurrently
- THEN the in-flight StrategySnapshot is unchanged for that job

---

## API

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/clients/{clientId}/general-strategy` | Get the current General Strategy for a client |
| PUT | `/clients/{clientId}/general-strategy` | Upsert the General Strategy for a client |

---

## Implementation Notes

- Domain: `SocialStudio.Domain.Social.GeneralStrategies.GeneralStrategy`
- Infrastructure table: `social.general_strategies` (renamed from `social.client_strategies` in migration `Rename_ClientStrategies_To_GeneralStrategies`)
- `StrategySnapshot` (frozen VO for generation jobs) is separate from `GeneralStrategy` and derives from it at job-start time only.
