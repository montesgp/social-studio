# Spec Delta: Profile Analysis — CASE C Aesthetic Guidance Seam

<!-- Change: content-background-sourcing-policy -->
<!-- Applies to: openspec/specs/profile-analysis/spec.md (additive delta) -->

## Purpose

Specify the **read-only seam** added to the `profile-analysis` capability that allows
the CASE C background sourcing strategy to consume aesthetic guidance from a completed
`ProfileAiAnalysis` record.

After this change is applied:

- `ProfileAiAnalysis.DetectedBackgroundImages` text descriptors are readable through an
  Application-layer port (`IProfileAnalysisReadRepository` or equivalent) and can be
  passed to the AI visual designer as CASE C aesthetic context.
- No profile-analysis behavior, analysis pipeline, or persistence schema is changed.
- No Apify call is added or modified by this delta.

The full CASE C image pipeline (real post-image URLs via Apify) is OUT OF SCOPE and
deferred to a future phase.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| PA-SEAM-01 | Proposal §Decision Tree — CASE C (seam-ready only, text descriptors) |
| PA-SEAM-02 | Proposal §Scope — profile-analysis read seam; no analysis behavior change |
| PA-SEAM-03 | Proposal §Out of Scope — real image URLs deferred; seam only |

---

## ADDED Requirements

---

### Requirement: PA-SEAM-01 — DetectedBackgroundImages Is Readable Through an Application Port for CASE C

The system MUST expose a read path in the Application layer that, given a `clientId`,
retrieves the `DetectedBackgroundImages` text descriptors from the most recently
**completed** (`Analyzed` status) `ProfileAiAnalysis` record for that client. The read
path MUST be available for the CASE C strategy to consume without the strategy having
any direct dependency on Infrastructure or EF Core types.

Only the `Analyzed` status is considered "completed" for this purpose. Partial, failed,
or in-progress analyses MUST NOT be returned.

#### Scenario: Read path returns descriptors for a client with a completed analysis (PA-SEAM-01-a)

- GIVEN a client with id `C1`
- AND a `ProfileAiAnalysis` record in status `Analyzed` for `C1` containing
  `DetectedBackgroundImages = "Soft gradient washes, minimal texture overlays"`
- WHEN the Application read port is called with `clientId = C1`
- THEN it returns the `DetectedBackgroundImages` text value
  `"Soft gradient washes, minimal texture overlays"`

#### Scenario: Read path returns null/empty when no completed analysis exists (PA-SEAM-01-b)

- GIVEN a client with id `C2` that has no `ProfileAiAnalysis` record (or only failed/in-progress ones)
- WHEN the Application read port is called with `clientId = C2`
- THEN it returns null or an empty/absent result (no exception thrown)

#### Scenario: Read path selects the most recent completed analysis (PA-SEAM-01-c)

- GIVEN a client with id `C3` that has two `ProfileAiAnalysis` records in status `Analyzed`:
  one from `2026-01-01` and one from `2026-06-01`
- WHEN the Application read port is called with `clientId = C3`
- THEN the `DetectedBackgroundImages` text from the `2026-06-01` record is returned
- AND the older record is not used

#### Scenario: In-progress or failed analyses are excluded (PA-SEAM-01-d)

- GIVEN a client with id `C4` that has one `ProfileAiAnalysis` in status `Analyzing`
  and no completed record
- WHEN the Application read port is called with `clientId = C4`
- THEN the result is null or empty (the in-progress record is not returned)

---

### Requirement: PA-SEAM-02 — Profile Analysis Pipeline and Persistence Are Unchanged

The profile-analysis scraping, AI analysis, metric computation, persistence schema, status
lifecycle, and API endpoints MUST remain identical to their pre-change state. This delta
does NOT add any write path, new pipeline step, or schema column to the
`social.profile_analyses` table.

#### Scenario: Profile analysis pipeline runs identically after seam is added (PA-SEAM-02-a)

- GIVEN a profile analysis request is submitted
- WHEN the pipeline runs (scrape → analyze → persist)
- THEN all pipeline stages execute without modification
- AND the `ProfileAnalysis` record is persisted with the same columns and status lifecycle
  as defined in the existing `profile-analysis` spec (PA-13, PA-14)
- AND no new column, table, or migration is introduced for profile-analysis by this change

#### Scenario: Existing ProfileAnalysis API endpoints unchanged (PA-SEAM-02-b)

- GIVEN the completed implementation of this change
- WHEN the existing profile analysis API endpoints (start, poll, fetch, list) are called
- THEN they respond with the same contract as before this change
- AND no new field or behavior has been added to or removed from those endpoints

---

### Requirement: PA-SEAM-03 — No Apify Image-Fetch Call in CASE C Seam Path

The CASE C seam MUST consume only the `DetectedBackgroundImages` text already stored in
the `ProfileAiAnalysis` AI analysis JSON. It MUST NOT add any call to Apify or any
image-retrieval service to fetch post background images. The seam boundary is strictly
text-in / text-out at the Application read port.

#### Scenario: CASE C strategy execution makes no Apify or image-fetch call (PA-SEAM-03-a)

- GIVEN a client with policy `CaseC` and a completed `ProfileAiAnalysis`
- WHEN the CASE C strategy executes during content generation
- THEN no call to `IApifyGateway`, `IImageFetchService`, or any equivalent Infrastructure
  image-retrieval adapter is made
- AND only the stored `DetectedBackgroundImages` text is read from the read port

---

## MODIFIED Requirements

None. This is an additive delta; no existing `profile-analysis` spec requirement is changed.

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative, for test authoring)

- PA-SEAM-01 MUST be covered by unit tests against the read port handler: mock the repository,
  assert ordering by completion date, assert null on no completed record.
- PA-SEAM-02 regression coverage is inherited from existing profile-analysis tests; no new
  pipeline tests are required by this delta.
- PA-SEAM-03 MUST be enforced by a unit test on the CASE C strategy that injects a mock
  Apify gateway and asserts it is never called when the strategy executes.
- Provider types (EF, Npgsql) MUST NOT appear in the Application read port interface or its
  handler; the architecture gate enforces this at CI.
