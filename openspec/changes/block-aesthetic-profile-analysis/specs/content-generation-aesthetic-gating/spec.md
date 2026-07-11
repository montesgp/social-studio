# Delta: Content Generation — Block First-Time ClientAesthetic Generation on Profile Analysis

<!-- Change: block-aesthetic-profile-analysis (Issue #67) -->
<!-- Applies to: openspec/specs/content-generation/spec.md (CAG-CG-01, CAG-CG-02, CAG-CG-04) -->

## Purpose

For a `ClientAesthetic` client with **no usable analyzed aesthetic** (first-time / empty),
generation MUST block on the just-enqueued/in-flight `ProfileAnalysisJob` — bounded by
the profile-analysis job budget — instead of degrading to Creative or generating with an
empty aesthetic. When no own account is configured, generation MUST fail immediately and
actionably. The existing non-blocking background refresh for the **stale-but-present**
case is UNCHANGED.

## ADDED Requirements

### Requirement: BAPA-CG-01 — Blocking Wait When No Usable Aesthetic Exists

When `policy == ClientAesthetic` and no usable analyzed aesthetic exists (first-time /
empty) and an own account is resolved, the job MUST transition to status
`AnalyzingProfile` and wait for the enqueued/in-flight `ProfileAnalysisJob` to reach a
usable analyzed state, bounded by the profile-analysis job budget. On success, generation
MUST proceed using the freshly analyzed aesthetic. On timeout, the job MUST end `Failed`
with an actionable message; it MUST NOT degrade to Creative and MUST NOT hang.

#### Scenario: Analysis completes within budget — generation proceeds with fresh aesthetic

- GIVEN a `ClientAesthetic` client with no usable analyzed aesthetic and a resolved own account
- WHEN a generation job starts and the enqueued `ProfileAnalysisJob` reaches a usable analyzed state before the budget elapses
- THEN the job status transitions through `AnalyzingProfile` before generation begins
- AND generation proceeds using the freshly analyzed `ClientAestheticProfile`

#### Scenario: Analysis exceeds the budget — job fails actionably, never hangs

- GIVEN a `ClientAesthetic` client with no usable analyzed aesthetic and a resolved own account
- WHEN the enqueued `ProfileAnalysisJob` does not reach a usable analyzed state within the profile-analysis job budget
- THEN the generation job ends `Failed` with an actionable timeout message
- AND generation is NOT attempted with an empty or partial aesthetic
- AND the job does not hang beyond the bounded wait

### Requirement: BAPA-CG-02 — No Own Account Fails Immediately and Actionably

When `policy == ClientAesthetic` and no own account is configured for the client, the
generation job MUST fail immediately with an actionable error instructing the user to
configure an own account to generate in this modality. The job MUST NOT enqueue a
profile analysis, MUST NOT transition to `AnalyzingProfile`, and MUST NOT degrade to
Creative or any other silent fallback.

#### Scenario: ClientAesthetic client with no own account fails fast

- GIVEN a `ClientAesthetic` client with no own account configured
- WHEN a generation job is requested
- THEN the job fails immediately with an actionable error naming the missing own-account configuration
- AND no `ProfileAnalysisJob` is enqueued
- AND the job never reaches `AnalyzingProfile`

## MODIFIED Requirements

### Requirement: CAG-CG-01 — ClientAesthetic Path Orchestrates Refresh-if-Stale Before Every Generation, Blocking Only When No Usable Aesthetic Exists

When `policy == ClientAesthetic` (`CaseC`), the `ContentGenerationJobProcessor`
pre-loop seam MUST run a **ClientAestheticEnsureFresh** orchestration step before
any slide generation begins. This step:

- Resolves the client's own account via an Application-layer port
  (`IClientOwnAccountRepository` or equivalent).
- Reads the freshness of the latest `Analyzed` `ProfileAiAnalysis` for the
  `(orgId, clientId)` pair against the client's effective threshold
  (`client.AestheticRefreshThresholdDays ?? 30` days).
- When a usable analyzed aesthetic already exists (fresh OR stale-but-present): enqueues a
  background refresh when stale (fire-and-forget) and returns the best-available
  `ClientAestheticProfile` immediately, without awaiting the enqueued refresh (see
  CAG-CG-04). Generation proceeds without delay.
- When NO usable analyzed aesthetic exists (first-time / empty): does NOT degrade to
  Creative. Instead, per BAPA-CG-01/BAPA-CG-02, either blocks on the enqueued/in-flight
  analysis (own account present) or fails immediately and actionably (no own account).

(Previously: the no-analysis case unconditionally degraded to Creative, logged a warning,
enqueued a background refresh, and let generation proceed immediately without waiting —
see removed Scenario CAG-CG-01-c. That silent-degradation path is replaced by
BAPA-CG-01/BAPA-CG-02.)

#### Scenario: Fresh analysis — used directly, no refresh enqueued (CAG-CG-01-a)

- GIVEN a `CaseC` client with no custom threshold (`AestheticRefreshThresholdDays = null`;
  effective threshold = 30 days)
- AND the latest `Analyzed` `ProfileAiAnalysis` completed `AnalyzedAtUtc = UtcNow - 3 days`
  (within the 30-day system-default threshold)
- WHEN `ContentGenerationJobProcessor` processes a generation job for that client
- THEN the `ClientAestheticProfile` is built from the existing analysis data
- AND no refresh job is enqueued
- AND generation proceeds immediately using the fresh profile

#### Scenario: Stale analysis — used immediately, refresh enqueued in background (CAG-CG-01-b)

- GIVEN a `CaseC` client with no custom threshold (effective threshold = 30 days)
- AND the latest `Analyzed` `ProfileAiAnalysis` completed
  `AnalyzedAtUtc = UtcNow - 45 days` (beyond the 30-day threshold)
- WHEN `ContentGenerationJobProcessor` processes a generation job for that client
- THEN the `ClientAestheticProfile` is built from the stale analysis data
- AND a background refresh job is enqueued (fire-and-forget) via `IProfileAnalysisJobQueue`
- AND generation proceeds immediately without waiting for the enqueued refresh
- AND the generated content is NOT blocked or delayed by the background refresh

### Requirement: CAG-CG-02 — Own Account Resolution; Degrade Only When a Usable Aesthetic Already Exists

The orchestration step MUST resolve the client's own Instagram account via
`IClientOwnAccountRepository` (or equivalent Application port). The processor has
only a `ClientId`; it MUST NOT hardcode or guess the account.

- If a usable analyzed aesthetic already exists (fresh or stale-but-present) and no own
  account is found, the step MUST skip the refresh enqueue, log a warning, and degrade to
  the best-available existing aesthetic data. The generation job MUST continue (unchanged
  from prior behavior).
- If NO usable analyzed aesthetic exists and no own account is found, the job MUST fail
  immediately and actionably per BAPA-CG-02 — it MUST NOT degrade to Creative.

(Previously: absence of an own account always degraded to best-available data or Creative
and let generation continue, regardless of whether a usable aesthetic existed.)

#### Scenario: Own account found — refresh can be enqueued (CAG-CG-02-a)

- GIVEN a `CaseC` client with a registered own account `A1`
- AND the analysis is stale
- WHEN the orchestration step runs
- THEN the own account `A1` is resolved via the Application port
- AND a refresh job targeting `A1` is enqueued via `IProfileAnalysisJobQueue`
- AND generation continues

#### Scenario: No own account, usable-but-stale aesthetic present — degrades as before (CAG-CG-02-b)

- GIVEN a `CaseC` client with NO registered own account
- AND a usable analyzed aesthetic already exists (stale-but-present)
- WHEN the orchestration step runs
- THEN the orchestration step logs a warning that no own account was found
- AND NO refresh job is enqueued
- AND generation continues with the best-available existing aesthetic data
- AND no exception is thrown and the job is NOT aborted

### Requirement: CAG-CG-04 — Refresh Enqueue Is Fire-and-Forget; Generation Only Waits When No Usable Aesthetic Exists

The background refresh **enqueue call** MUST remain asynchronous and non-blocking: the
generation pipeline MUST NOT await, poll, or coordinate with the enqueued
`ProfileAnalysisJob` as part of the enqueue step itself, and any enqueue failure MUST be
caught, logged, and MUST NOT propagate to the generation job. This applies to ALL cases,
including the blocking first-time path — the *enqueue* is still fire-and-forget.

Separately, per BAPA-CG-01, when NO usable aesthetic exists, the generation job MUST
perform a distinct, explicitly bounded wait for the enqueued job's analyzed outcome
before generating. This wait is orthogonal to the enqueue call and applies ONLY to the
no-usable-aesthetic case; the fresh and stale-but-present cases remain fully non-blocking
end-to-end as before.

(Previously: generation never waited on the analysis pipeline under any circumstance.)

#### Scenario: Refresh enqueue failure does not abort generation (CAG-CG-04-a)

- GIVEN a stale-analysis `CaseC` client
- AND the `IProfileAnalysisJobQueue` enqueue call throws an exception
- WHEN the orchestration step runs
- THEN the exception is caught and logged as a warning
- AND the generation job continues without the refresh
- AND the generation pipeline does NOT propagate the enqueue exception

#### Scenario: Stale-but-present case still proceeds without waiting (CAG-CG-04-b)

- GIVEN a stale-analysis `CaseC` client and a successful enqueue
- WHEN the orchestration step returns
- THEN the processor immediately proceeds to the slide generation loop
- AND no await, Task.Delay, or polling loop is inserted between the enqueue and generation
- AND the generation job completes in a time unaffected by the async analysis pipeline

## REMOVED Requirements

### Requirement: CAG-CG-01-c — No Analysis Degrades to Creative

(Reason: silent degradation to Creative on a first-time/no-analysis `ClientAesthetic`
client contradicts issue #67 — the client should get its analyzed aesthetic or an
actionable failure, never a silent fallback.)
(Migration: replaced by BAPA-CG-01 — blocking wait bounded by the profile-analysis job
budget — and BAPA-CG-02 — immediate actionable failure when no own account exists.)
