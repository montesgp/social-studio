# Delta: Content Generation Job Status — AnalyzingProfile Phase

<!-- Change: block-aesthetic-profile-analysis (Issue #67) -->
<!-- Applies to: the content generation job status vocabulary (status DTO + polling endpoint + frontend overlay) -->

## Purpose

Add a new job status value, `AnalyzingProfile`, surfaced between `Running` and the
terminal states, so the polling status endpoint and the frontend generation overlay can
represent the blocking first-time `ClientAesthetic` profile-analysis wait introduced by
BAPA-CG-01 (see `content-generation-aesthetic-gating` delta).

## ADDED Requirements

### Requirement: BAPA-STATUS-01 — Job Status Vocabulary Gains AnalyzingProfile

The content generation job status vocabulary MUST include `AnalyzingProfile` as an
additive value alongside the existing `Pending`, `Running`, `Completed`, `Partial`, and
`Failed` values. `AnalyzingProfile` MUST be used ONLY for the blocking wait described in
BAPA-CG-01 (first-time `ClientAesthetic` generation with no usable aesthetic). No other
generation path MUST ever report `AnalyzingProfile`.

#### Scenario: First-time ClientAesthetic job reports AnalyzingProfile

- GIVEN a `ClientAesthetic` generation job with no usable analyzed aesthetic and a resolved own account
- WHEN the job status is polled while the bounded wait for the `ProfileAnalysisJob` is in progress
- THEN the status endpoint returns `AnalyzingProfile`

#### Scenario: Non-ClientAesthetic or usable-aesthetic-present jobs never report AnalyzingProfile

- GIVEN a generation job that is either a non-`ClientAesthetic` policy OR a `ClientAesthetic` client with a usable aesthetic already present (fresh or stale)
- WHEN the job status is polled at any point in its lifecycle
- THEN the status endpoint never returns `AnalyzingProfile` for that job

### Requirement: BAPA-STATUS-02 — Status Consumers Handle AnalyzingProfile Without Breaking on the Closed Set

Existing and future status consumers (frontend hook, overlay, any other status reader)
MUST treat `AnalyzingProfile` as an additive value. Consumers that switch/pattern-match
over the status enum MUST explicitly handle `AnalyzingProfile` (not fall through to an
`Unknown`/default branch that hides progress) since it is a distinct, user-visible phase.

#### Scenario: Status DTO round-trips AnalyzingProfile without serialization errors

- GIVEN the backend sets a job's status to `AnalyzingProfile`
- WHEN the status endpoint serializes the response
- THEN the client receives `AnalyzingProfile` as a valid, recognized status value
- AND no serialization error or fallback-to-unknown occurs

### Requirement: BAPA-STATUS-03 — Frontend Overlay Renders an "Analyzing Profile" Phase with Later Steps Pending

`use-content-generation-job.ts` MUST recognize `AnalyzingProfile` in the polled status.
`single-analyzer-client.tsx` (generation overlay) MUST render a distinct "Analyzing
profile" phase while status is `AnalyzingProfile`, and MUST keep subsequent generation
steps visually pending/blocked until the status transitions away from `AnalyzingProfile`.

#### Scenario: Overlay shows Analyzing profile phase and blocks later steps

- GIVEN a generation job currently reports status `AnalyzingProfile`
- WHEN the frontend polls and renders the generation overlay
- THEN the overlay shows an "Analyzing profile" phase as the current step
- AND all steps after profile analysis are rendered as pending/blocked, not yet started

#### Scenario: Overlay proceeds past Analyzing profile once status changes

- GIVEN the generation overlay is showing the "Analyzing profile" phase
- WHEN the next poll returns a status of `Running`, `Completed`, `Partial`, or `Failed`
- THEN the overlay transitions out of the "Analyzing profile" phase
- AND subsequent steps unblock or reflect the terminal outcome accordingly
