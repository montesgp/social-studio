# Delta for General Strategy (ClientStrategy Rename/Restructure)

<!-- PRD traceability: link PRD section when assigned -->
<!-- ADR required: docs/architecture/adr/ — architecture change -->

## Context

`ClientStrategy` is an existing, fully-persisted aggregate (Domain/Social/ClientStrategies/,
table `social.client_strategies`, unique `client_id`). This delta renames and aligns it to
"General Strategy" — it is NOT a net-new promotion. The `StrategySnapshot` frozen VO that
derives from it at generation time is a separate concern and is unchanged by this delta.

---

## MODIFIED Requirements

### Requirement: GSTRAT-01 — Enduring Client Strategy Persistence

The system MUST persist exactly one General Strategy record per client (1:1 cardinality),
keyed by `client_id`, containing content pillars (max 10), tone keywords (max 20),
target audience (optional, max 1000 chars), posting frequency (optional, max 200 chars),
and notes (optional, max 2000 chars).
At least one field MUST carry a non-empty value (empty-signal rule).
(Previously: same invariants under the name "ClientStrategy".)

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

## ADDED Requirements

### Requirement: GSTRAT-02 — General Strategy Does Not Auto-Propagate to Content Plans

The system MUST NOT automatically re-derive or update any Content Plan when a
General Strategy is edited. Updates to General Strategy are recorded on that
aggregate only. Any re-alignment with a Content Plan is a deliberate user action
(redefine and regenerate on the Content Plan).

#### Scenario: General Strategy update does not cascade

- GIVEN a client with a persisted General Strategy and a persisted Content Plan
- WHEN the General Strategy is updated via the upsert command
- THEN only the General Strategy record changes
- AND all existing Content Plans for that client are unchanged

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
