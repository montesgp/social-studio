# Content Workflow Spec

## Purpose

Define capabilities, constraints, invariants, events, and acceptance criteria for operator workflow sessions covering content discovery, per-candidate format composition, independent enqueue orchestration, and durable history storage.

## Capabilities

- Discover candidates from configured sources and persist them in a workflow session.
- Compose per-candidate format selections from the real ContentFormatKey catalog.
- Enqueue N candidates independently, each running its own analysis-then-batch sequence without blocking the UI.
- Persist workflow sessions, selections, status transitions, and history such that they survive application restart.
- Filter workflow history by client (optional, backward-compatible).
- Show per-candidate enqueue status and estimated time to completion.
- Render collapsible Generation Progress and Configuration panels (default collapsed in historical summaries).

## Events

- `WorkflowSessionDiscoveryCompleted` — candidate discovery is complete and candidates are persisted.
- `WorkflowCandidateEnqueued` — a candidate has been submitted for analysis-then-batch generation.
- `WorkflowCandidateAnalysisStarted` — a candidate's analysis is in progress.
- `WorkflowCandidateAnalysisFailed` — a candidate's analysis failed (terminal).
- `WorkflowCandidateBatchQueued` — a candidate's batch has been queued (terminal for workflow scope).
- `WorkflowCandidateBatchFailed` — a candidate's batch has failed (terminal).

## Constraints

- Workflow sessions and candidate state MUST be persisted to survive process restart.
- In-memory sessions MUST be discarded at cutover to persistent storage (no backward-compatibility migration).
- A candidate MUST have a composition before enqueue is accepted.
- Enqueue for the same candidate+composition MUST be idempotent via an enqueue key.
- Analysis and batch queueing for a single candidate MUST proceed independently from other candidates in the same enqueue action.
- One failed candidate's analysis MUST NOT block sibling candidates' sequences.
- History read surface MUST NOT expose candidates that have not yet been enqueued (discovered but not submitted).
- The optional `clientId` filter MUST be additive and backward-compatible; omitting it MUST return current unfiltered behavior.
- Client-scoped filtering MUST use a database index for efficient retrieval.

## Acceptance Criteria

- Session-start discovers candidates and persists them with `Discovered` status.
- Operator can select and compose formats for each candidate without any candidate being submitted.
- Enqueuing N candidates in one action posts N independent items to the analysis queue.
- A rejected candidate returns synchronous inline feedback in the enqueue response.
- Accepted candidates transition to `Queued` status and begin analysis while the UI returns control.
- Partial failure (some candidates rejected, others queued) does not roll back the accepted ones.
- History read shows only enqueued/post-enqueue candidates, not discovered-but-unsubmitted ones.
- History is filterable by `clientId` via an optional query parameter.
- Post-enqueue status and estimated minutes are persisted and returned.
- Full gate suite passes with no MUST-gate failures.
- All four test suites pass independently: backend 4000+/0, frontend 400+/0, TypeScript type-check 0 errors, linting 0 errors.

---

## Content Generator Real Pipeline — Change: content-generator-real-pipeline

### Domain: content-workflow-generation-composition (NEW)

#### Purpose

Operator-facing composition and enqueue flow replacing mocked generation. No implementation detail; describes required screen behavior and orchestration outcomes.

#### Requirement: Discovery-only candidate action

The system MUST provide a single candidate-discovery action distinct from generation; it MUST NOT imply content is generated at discovery time.

##### Scenario: Discover candidates

- GIVEN an operator on the Generator screen with a source configured
- WHEN they trigger candidate discovery
- THEN the screen lists discovered candidates only, with no generated content

#### Requirement: Removal of mock generation paths

The system MUST NOT expose Preview tab, Analysis & Conversion tab, per-card fake generators, bulk actions `transcribe_and_build_script`/`build_carousel`, or the redundant Refresh button.

##### Scenario: No mock actions available

- GIVEN the Generator screen is rendered
- WHEN an operator inspects available tabs and actions
- THEN none of the removed elements are present or reachable

#### Requirement: Per-candidate format composition

For each selected candidate, the system MUST let the operator choose one or more formats from the real catalog (reel-script, story-single, story-carousel, feed-single, feed-carousel, content-idea, cta-variation, caption, hook) and MUST render a preview of candidate x chosen-formats before any enqueue action runs.

##### Scenario: Compose formats per candidate

- GIVEN two selected candidates
- WHEN the operator picks different format sets for each
- THEN the preview shows each candidate paired only with its own chosen formats and nothing is yet enqueued

##### Scenario: Empty composition blocks enqueue

- GIVEN a selected candidate with zero formats chosen
- WHEN the operator attempts to enqueue
- THEN the system MUST prevent enqueue for that candidate and indicate a format must be chosen

#### Requirement: Independent per-candidate enqueue orchestration

On one enqueue action covering N candidates, the system MUST, for each candidate independently: issue `AnalyzeSingleContentCommand`, await its real analysis result, then issue `CreateContentGenerationBatchCommand` with exactly the operator-chosen formats for that candidate. The system MUST NOT block the UI with a synchronous poll, and MUST NOT let one candidate's sequence depend on another's outcome.

##### Scenario: N candidates enqueue independently

- GIVEN 3 candidates each with a composed format set
- WHEN the operator enqueues them in one action
- THEN 3 independent analysis-then-batch sequences start and the UI returns control without blocking on completion

##### Scenario: Partial failure isolation

- GIVEN 2 candidates enqueued together
- WHEN one candidate's analysis fails
- THEN that candidate reaches a terminal failed status while the sibling candidate's sequence proceeds unaffected

#### Requirement: Post-enqueue feedback

The system MUST persist a status per candidate/workflow history entry reflecting its sequence progress and MUST show an estimated-time message directing the operator to the content-bank (jobs) screen for results.

##### Scenario: Status visible after enqueue

- GIVEN a candidate has been enqueued
- WHEN the operator views the workflow history entry
- THEN its current status (e.g. analyzing, batch-created, analysis_failed) is shown with an estimated-time message

#### Requirement: Collapsible progress and configuration panels

The system MUST provide "Generation progress" and "Configuration" as collapsible panels, and MUST render both default-collapsed when an operator opens a historical record's summary.

##### Scenario: Historical summary opens collapsed

- GIVEN an operator opens a past workflow record's summary
- WHEN the summary renders
- THEN both panels are collapsed by default and expandable on demand

### Domain: content-workflow-persistence (NEW)

#### Purpose

Durable storage for workflow sessions/candidates/history, replacing the in-memory service.

#### Requirement: Durable workflow session store

The system MUST persist workflow sessions, their candidates, format selections, and status transitions such that they survive an application process restart.

##### Scenario: Session survives restart

- GIVEN a workflow session with enqueued candidates exists
- WHEN the application process restarts
- THEN the session and its candidate statuses are retrievable unchanged

#### Requirement: Cutover discards in-memory sessions

The system MUST NOT attempt to migrate sessions held by `InMemoryContentWorkflowService` at cutover; live in-memory session state MAY be lost.

##### Scenario: Cutover with active session

- GIVEN an in-memory session exists before deployment of the persisted store
- WHEN the persisted store replaces the in-memory service
- THEN no error is raised for the discarded prior session and the new store starts empty for that session

### Domain: content-workflow-history (MODIFIED)

#### ADDED Requirements

#### Requirement: Optional client-scoped history filter

`ListContentWorkflowHistoryQuery` and `GET /api/social/workflows/history` MUST accept an optional `clientId` filter. WHEN provided, results MUST be restricted to that client. WHEN omitted, behavior MUST be unchanged from current unfiltered callers.

##### Scenario: Filter by client

- GIVEN workflow history entries exist for multiple clients
- WHEN a caller requests history with `clientId=X`
- THEN only entries belonging to client X are returned

##### Scenario: Unfiltered call unaffected

- GIVEN existing callers that omit `clientId`
- WHEN they call the endpoint/query as before
- THEN all entries are returned exactly as prior behavior

### Non-Goals (explicit)

- No `ai-ideas` origin mode support in the Generator.
- No real ETA telemetry/instrumentation in v1 — heuristic estimate only.
- No change to single-analyzer's own screen/UI.
