# Spec Delta: Content Generation — Active ClientAesthetic Path, Richer Profile, Advisory Hints

<!-- Change: content-aesthetic-generation -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->

## Purpose

Specify the **additive behavioral changes** this change introduces to the
`content-generation` capability:

1. **Active ClientAesthetic orchestration** — the `ClientAesthetic` generation path
   (`BackgroundSourcingPolicy.CaseC`) transitions from a passive descriptor read to an
   active refresh-if-stale orchestration. At generation time the processor resolves the
   client's own account, checks analysis freshness against the client's effective
   staleness threshold (`client.AestheticRefreshThresholdDays ?? 30` days; system
   default 30 days), and fires a non-blocking background refresh when the analysis is
   stale or absent. Generation NEVER waits; it proceeds immediately with best-available data.

2. **Richer `ClientAestheticProfile` value object** — replaces the flat
   `IReadOnlyList<string>` descriptor return. Carries: `BackgroundDescriptors`,
   `VoiceTone?`, `AverageSlidesPerCarousel?`, `AnalyzedAtUtc?`. Designed with nullable
   fields so Phase 2 image URLs slot in without a breaking change.

3. **Profile threaded into copy generators (advisory)** — `VoiceTone` and
   `AverageSlidesPerCarousel` are carried in `GenerationContext` / `GenerationPromptContext`
   as advisory hints. They are ADDITIVE to the existing advisory `ConcisionConstraint`
   and MUST coexist with it; concision guidance is never replaced or hard-blocked by
   aesthetic hints.

4. **Refresh debounce** — at most one in-flight background refresh is enqueued per client
   at any given time; repeated generations within a short window do not duplicate enqueue
   calls.

No rendering, guardrail, or publishing behavior is modified. Phase 2 (real image URLs)
is out of scope.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAG-CG-01 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — orchestration overview |
| CAG-CG-02 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — own account resolution |
| CAG-CG-03 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — freshness check (per-client threshold; system default 30 days) |
| CAG-CG-04 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — fire-and-forget enqueue |
| CAG-CG-05 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — generate NOW regardless |
| CAG-CG-06 | Proposal §Behavior: Non-Blocking Refresh-if-Stale — no analysis degrade |
| CAG-CG-07 | Proposal §ClientAestheticProfile Model — value object shape |
| CAG-CG-08 | Proposal §Approach — profile threaded to background designer AND copy generators |
| CAG-CG-09 | Proposal §Non-Goals — advisory only; concision coexistence |
| CAG-CG-10 | Proposal §Open Questions OQ-8 — refresh debounce |
| CAG-CG-11 | Proposal §ClientAestheticProfile Model — image-URL-ready nullable fields |

---

## ADDED Requirements

---

### Requirement: CAG-CG-01 — ClientAesthetic Path Orchestrates Refresh-if-Stale Before Every Generation

When `policy == ClientAesthetic` (`CaseC`), the `ContentGenerationJobProcessor`
pre-loop seam MUST run a **ClientAestheticEnsureFresh** orchestration step before
any slide generation begins. This step:

- Resolves the client's own account via an Application-layer port
  (`IClientOwnAccountRepository` or equivalent).
- Reads the freshness of the latest `Analyzed` `ProfileAiAnalysis` for the
  `(orgId, clientId)` pair against the client's effective threshold
  (`client.AestheticRefreshThresholdDays ?? 30` days).
- Enqueues a background refresh when the analysis is stale or absent (fire-and-forget).
- Returns the best-available `ClientAestheticProfile` immediately, without awaiting the
  enqueued refresh.

The generation job MUST proceed regardless of freshness state. The orchestration step
MUST NOT be on the critical path that could delay or block slide generation.

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

#### Scenario: No analysis — degrades to Creative, refresh still enqueued (CAG-CG-01-c)

- GIVEN a `CaseC` client with NO `Analyzed` `ProfileAiAnalysis` record
- WHEN `ContentGenerationJobProcessor` processes a generation job for that client
- THEN the ClientAesthetic path degrades to Creative (AI-palette behavior equivalent
  to the CaseB / no-aesthetic-context fallback)
- AND a structured warning is logged identifying the `clientId`, `orgId`, and the
  reason ("no analysis available")
- AND a background refresh job is enqueued (fire-and-forget)
- AND generation proceeds immediately; the job is NOT aborted

---

### Requirement: CAG-CG-02 — Own Account Resolution; Degrade Safely When Absent

The orchestration step MUST resolve the client's own Instagram account via
`IClientOwnAccountRepository` (or equivalent Application port). The processor has
only a `ClientId`; it MUST NOT hardcode or guess the account. If no own account is
found for the client, the step MUST skip the refresh enqueue (there is nothing to
scrape), log a warning, and degrade to the best-available aesthetic data (or Creative
if no analysis exists). The generation job MUST continue.

#### Scenario: Own account found — refresh can be enqueued (CAG-CG-02-a)

- GIVEN a `CaseC` client with a registered own account `A1`
- AND the analysis is stale
- WHEN the orchestration step runs
- THEN the own account `A1` is resolved via the Application port
- AND a refresh job targeting `A1` is enqueued via `IProfileAnalysisJobQueue`
- AND generation continues

#### Scenario: No own account — refresh skipped, generation continues (CAG-CG-02-b)

- GIVEN a `CaseC` client with NO registered own account
- WHEN the orchestration step runs
- THEN the orchestration step logs a warning that no own account was found
- AND NO refresh job is enqueued
- AND generation continues with the best-available aesthetic data (or Creative if no
  analysis exists)
- AND no exception is thrown and the job is NOT aborted

---

### Requirement: CAG-CG-03 — Staleness Threshold Is Per-Client Configurable; System Default Is 30 Days

The effective staleness threshold for a `CaseC` client MUST be resolved as:

```
effectiveThreshold = client.AestheticRefreshThresholdDays ?? AestheticFreshness.DefaultStalenessThresholdDays
```

where `AestheticFreshness.DefaultStalenessThresholdDays = 30`.

An analysis whose `AnalyzedAtUtc` is more than `effectiveThreshold` days before the
current UTC time is considered stale. An analysis within `effectiveThreshold` days is
considered fresh.

`AestheticRefreshThresholdDays` is a nullable `int?` on the `Client` domain aggregate.
Validation: minimum 1 day, maximum 365 days. `null` means "use the 30-day system default."
The field is persisted on `social.clients` (additive nullable column
`aesthetic_refresh_threshold_days smallint NULL`) and exposed in the client ABM
(Create/Update request + response + web form).

#### Scenario: Client with null threshold uses 30-day system default (CAG-CG-03-a)

- GIVEN a `CaseC` client with `AestheticRefreshThresholdDays = null`
- AND an analysis completed at exactly `UtcNow - 30 days` (boundary)
- WHEN the freshness check runs
- THEN the effective threshold is 30 days
- AND the analysis is treated as fresh (no refresh enqueued)

#### Scenario: One second beyond the 30-day system-default threshold — treated as stale (CAG-CG-03-b)

- GIVEN a `CaseC` client with `AestheticRefreshThresholdDays = null`
- AND an analysis completed at `UtcNow - 30 days - 1 second`
- WHEN the freshness check runs
- THEN the effective threshold is 30 days
- AND the analysis is treated as stale (refresh is enqueued)

#### Scenario: Client with custom threshold — custom value is honored (CAG-CG-03-c)

- GIVEN a `CaseC` client with `AestheticRefreshThresholdDays = 7`
- AND an analysis completed at `UtcNow - 10 days` (beyond the 7-day custom threshold
  but within the 30-day system default)
- WHEN the freshness check runs
- THEN the effective threshold is 7 days (client-specific, not the system default)
- AND the analysis is treated as stale (refresh is enqueued)

#### Scenario: Client with custom threshold at boundary — treated as fresh (CAG-CG-03-d)

- GIVEN a `CaseC` client with `AestheticRefreshThresholdDays = 60`
- AND an analysis completed at exactly `UtcNow - 60 days`
- WHEN the freshness check runs
- THEN the effective threshold is 60 days
- AND the analysis is treated as fresh (no refresh enqueued)

---

### Requirement: CAG-CG-04 — Refresh Is Fire-and-Forget; Generation Never Waits

The background refresh enqueue MUST be asynchronous and non-blocking relative to the
generation job. The generation pipeline MUST NOT await, poll, or coordinate with the
enqueued `ProfileAnalysisJob`. The enqueue call MUST complete promptly (a channel
write or equivalent) and the generation pipeline continues on the NEXT line of code.
Any failure in the enqueue call MUST be caught and logged; it MUST NOT propagate to
the generation job and cause it to fail.

#### Scenario: Refresh enqueue failure does not abort generation (CAG-CG-04-a)

- GIVEN a stale-analysis `CaseC` client
- AND the `IProfileAnalysisJobQueue` enqueue call throws an exception
- WHEN the orchestration step runs
- THEN the exception is caught and logged as a warning
- AND the generation job continues without the refresh
- AND the generation pipeline does NOT propagate the enqueue exception

#### Scenario: Generation proceeds on the next operation after enqueue without polling (CAG-CG-04-b)

- GIVEN a stale-analysis `CaseC` client and a successful enqueue
- WHEN the orchestration step returns
- THEN the processor immediately proceeds to the slide generation loop
- AND no await, Task.Delay, or polling loop is inserted between the enqueue and generation
- AND the generation job completes in a time unaffected by the async analysis pipeline

---

### Requirement: CAG-CG-05 — ClientAestheticProfile Value Object Carries Richer Data

The `ClientAestheticProfile` value object (Application layer) MUST carry:
- `BackgroundDescriptors: IReadOnlyList<string>` — text descriptors from
  `ProfileAiAnalysis.DetectedBackgroundImages`.
- `VoiceTone: string?` — copy-style/voice signal from the profile analysis; nullable.
- `AverageSlidesPerCarousel: double?` — server-derived stat; nullable.
- `AnalyzedAtUtc: DateTime?` — freshness provenance; nullable (null when no analysis).

The type MUST be designed with nullable Phase 2 fields (e.g., `BackgroundImageUrls`) as
a forward-compatibility contract so image URLs can be added later WITHOUT a breaking
change to any consumer. No image-URL field is populated or read in Phase 1.

#### Scenario: Profile built from a completed analysis carries all four fields (CAG-CG-05-a)

- GIVEN a completed `ProfileAiAnalysis` with `DetectedBackgroundImages = ["soft gradients"]`,
  `VoiceTone = "Warm and motivational"`, `AverageSlidesPerCarousel = 5.3`,
  `CompletedAtUtc = 2026-06-01T10:00:00Z`
- WHEN `ClientAestheticProfile` is constructed from that analysis
- THEN `BackgroundDescriptors = ["soft gradients"]`
- AND `VoiceTone = "Warm and motivational"`
- AND `AverageSlidesPerCarousel = 5.3`
- AND `AnalyzedAtUtc = 2026-06-01T10:00:00Z`

#### Scenario: Profile built from no analysis — all nullable fields are null (CAG-CG-05-b)

- GIVEN no completed `ProfileAiAnalysis` exists for the client
- WHEN a degraded `ClientAestheticProfile` is constructed for the Creative fallback path
- THEN `BackgroundDescriptors` is empty
- AND `VoiceTone` is null
- AND `AverageSlidesPerCarousel` is null
- AND `AnalyzedAtUtc` is null

#### Scenario: Phase 2 image URL fields absent without breaking existing consumers (CAG-CG-05-c)

- GIVEN the completed Phase 1 implementation
- WHEN a Phase 2 nullable `BackgroundImageUrls: IReadOnlyList<string>?` field is added
  to `ClientAestheticProfile`
- THEN all existing Phase 1 consumers compile and behave identically
- AND no existing mapping, constructor, or consumer requires modification

---

### Requirement: CAG-CG-06 — ClientAestheticProfile Threaded into Both Background Designer and Copy Generators

The `ClientAestheticProfile` resolved during the pre-loop orchestration step MUST be
passed to:
- The **AI background designer** (`AiVisualDesigner` / `CarouselBackgroundPlan`) via the
  existing `BackgroundSelection.ClientAesthetic` path — `BackgroundDescriptors` used for
  aesthetic context (existing behavior, now richer).
- **All copy generators** (`CarouselGenerator`, `StoryGenerator`, `FeedPostGenerator`)
  via `GenerationContext` / `GenerationPromptContext` — `VoiceTone` and
  `AverageSlidesPerCarousel` included as advisory hints.

Neither path MAY receive the profile through an Infrastructure or EF type. The profile
flows through Application types only.

#### Scenario: Background designer receives BackgroundDescriptors (CAG-CG-06-a)

- GIVEN a `CaseC` generation job with a `ClientAestheticProfile` carrying
  `BackgroundDescriptors = ["moody dark gradients", "minimal white space"]`
- WHEN the carousel background plan is computed
- THEN the AI visual designer prompt includes those descriptor strings as aesthetic context
- AND no Infrastructure or EF Core type is present in the data path from profile to prompt

#### Scenario: Copy generators receive VoiceTone and AverageSlidesPerCarousel hints (CAG-CG-06-b)

- GIVEN a `CaseC` generation job with `VoiceTone = "Playful and energetic"` and
  `AverageSlidesPerCarousel = 6.0`
- WHEN a `CarouselGenerator` builds its copy prompt
- THEN the prompt carries a copy-style section referencing `VoiceTone` as an advisory
  style hint
- AND the prompt carries a carousel-structure section referencing the avg-slides hint
- AND neither hint is absent from the assembled prompt

---

### Requirement: CAG-CG-07 — VoiceTone Is Advisory; Read Directly Into Generation Context; Not Imputed Into BrandManual

`VoiceTone` from the `ClientAestheticProfile` MUST be read directly into
`GenerationContext` / `GenerationPromptContext` at generation time. It MUST NOT be
written to or merged with `BrandManual.VoiceAndTone` or any persisted brand field.
It operates as a transient, advisory signal that layers over (not replaces) the
brand-manual voice guidance already present in the generation context.

#### Scenario: VoiceTone from aesthetic profile does not mutate BrandManual (CAG-CG-07-a)

- GIVEN a `CaseC` client with `BrandManual.VoiceAndTone = "Professional and concise"`
  and `ClientAestheticProfile.VoiceTone = "Energetic"`
- WHEN generation runs
- THEN `BrandManual.VoiceAndTone` remains `"Professional and concise"` after the job
- AND no write or update to any `BrandManual` or `BrandManualSnapshot` field occurs
  as a result of this generation job
- AND both guidance values are present in `GenerationPromptContext` (brand manual tone
  AND aesthetic profile tone)

#### Scenario: VoiceTone null — no advisory section in prompt (CAG-CG-07-b)

- GIVEN a `CaseC` client with `ClientAestheticProfile.VoiceTone = null`
- WHEN generation runs
- THEN the copy generators receive no advisory VoiceTone section
- AND no null reference exception is thrown
- AND generation completes normally

---

### Requirement: CAG-CG-08 — AverageSlidesPerCarousel Is Advisory; Clamped 3–10; Never Hard-Rejects AI Output

`AverageSlidesPerCarousel` from the `ClientAestheticProfile` MUST be threaded into
carousel copy-generator prompts as a **soft advisory hint only**. The hint MUST be
clamped to the range `[3, 10]` before inclusion in the prompt. The system MUST NOT
reject, retry, or fail AI-generated copy solely because the slide count deviates from
this hint. The hint coexists with and MUST NOT replace the existing advisory
`ConcisionConstraint`.

#### Scenario: Avg-slides hint present in carousel prompt (CAG-CG-08-a)

- GIVEN a `CaseC` carousel job with `AverageSlidesPerCarousel = 7.0`
- WHEN `CarouselGenerator` assembles its copy prompt
- THEN the prompt contains an advisory sentence communicating that the client typically
  uses approximately 7 slides per carousel
- AND the hint is labeled as advisory (e.g., "typically uses around N slides")

#### Scenario: Avg-slides clamped to 10 when raw value exceeds range (CAG-CG-08-b)

- GIVEN `AverageSlidesPerCarousel = 12.5` (above the 10 ceiling)
- WHEN the hint is computed for the prompt
- THEN the value included in the prompt is 10 (clamped)
- AND no exception is thrown

#### Scenario: Avg-slides clamped to 3 when raw value is below range (CAG-CG-08-c)

- GIVEN `AverageSlidesPerCarousel = 1.5` (below the 3 floor)
- WHEN the hint is computed for the prompt
- THEN the value included in the prompt is 3 (clamped)

#### Scenario: Avg-slides null — hint section absent from prompt (CAG-CG-08-d)

- GIVEN `AverageSlidesPerCarousel = null` (childrenCount unavailable)
- WHEN `CarouselGenerator` assembles its copy prompt
- THEN no avg-slides advisory section appears in the prompt
- AND generation proceeds normally without the hint

#### Scenario: AI output with slide count deviating from hint is NOT rejected (CAG-CG-08-e)

- GIVEN a carousel prompt with avg-slides hint of 6
- AND the AI returns copy structured for 8 slides
- WHEN the copy guardrail runs
- THEN the AI output is accepted
- AND no retry or failure is triggered solely because the AI chose a different slide count

---

### Requirement: CAG-CG-09 — Copy-Style and ConcisionConstraint Coexist; Concision Is Not Replaced

The `VoiceTone` advisory hint and the `AverageSlidesPerCarousel` advisory hint MUST
be **additive** to the existing advisory `ConcisionConstraint` already defined in the
`content-generation` spec (requirements `CG-CON-01` through `CG-CON-04` from the
predecessor change). Neither aesthetic hint replaces, overrides, or removes the
concision directive. Both coexist in the same prompt. When in conflict, no system
rule adjudicates between them; the AI receives both and decides. Neither is treated
as a hard constraint.

#### Scenario: Carousel prompt carries both concision directive and aesthetic hints (CAG-CG-09-a)

- GIVEN a `CaseC` carousel job with `VoiceTone = "Bold"` and `AverageSlidesPerCarousel = 5`
- WHEN `CarouselGenerator` assembles its copy prompt
- THEN the prompt contains the existing concision directive (one concept across slides,
  per-slot length guidance)
- AND the prompt ALSO contains the VoiceTone advisory section
- AND the prompt ALSO contains the avg-slides advisory section
- AND no section replaces or suppresses the others

#### Scenario: Non-CaseC jobs still carry concision directives without aesthetic hints (CAG-CG-09-b)

- GIVEN a generation job for a client with policy `CaseA` (not `CaseC`)
- WHEN copy generators assemble their prompts
- THEN the existing concision directive is present
- AND no VoiceTone or AverageSlidesPerCarousel hint section is added
- AND no null reference exception is thrown

---

### Requirement: CAG-CG-10 — Refresh Debounce: At Most One In-Flight Refresh Per Client

When a ClientAesthetic generation job triggers a background refresh, the system MUST
check whether a refresh for the same `clientId` is already in-flight (queued or
running). If one is already in-flight, the new enqueue call MUST be skipped silently
(no duplicate enqueue, no error). The debounce window is per-client; different clients
are independent.

#### Scenario: First generation triggers refresh; second within debounce window skips enqueue (CAG-CG-10-a)

- GIVEN a `CaseC` client with a stale analysis
- AND a first generation job enqueues a background refresh for that client
- WHEN a SECOND generation job runs for the same client before the refresh completes
- THEN the second job detects the in-flight refresh and does NOT enqueue another refresh
- AND only ONE refresh job exists in the queue for this client
- AND both generation jobs complete normally

#### Scenario: Different clients are debounced independently (CAG-CG-10-b)

- GIVEN two `CaseC` clients `C1` and `C2`, both with stale analyses
- WHEN generation jobs run for `C1` and `C2` simultaneously
- THEN a refresh is enqueued for `C1` AND a refresh is enqueued for `C2`
- AND neither client's debounce state affects the other

---

### Requirement: CAG-CG-11 — Prompt Version Bumped for All Modified Copy Generators

Every copy-generator whose prompt is modified by this change (to carry VoiceTone
and/or AverageSlidesPerCarousel advisory hints) MUST have its entry in
`ContentGenerationPromptVersions` incremented. No previously-versioned prompt that
receives changes in this change may retain its old version constant.

#### Scenario: Prompt versions incremented for all modified generators (CAG-CG-11-a)

- GIVEN the completed implementation
- WHEN `ContentGenerationPromptVersions` is inspected
- THEN every generator whose prompt was modified by this change has a version value
  higher than its pre-change value
- AND no modified generator retains its pre-change version constant

---

## MODIFIED Requirements

### Requirement Delta: BSP-04 — CASE C Degrade Path Adds Refresh Fire-and-Forget

`BSP-04` from the predecessor change (`content-background-sourcing-policy`) specified
that CASE C degrades to CASE B behavior and logs a warning when no completed
`ProfileAiAnalysis` exists. This change **adds** a second obligation to that path:

> When CASE C degrades due to no-analysis, a background refresh job MUST ALSO be
> enqueued (fire-and-forget) so that the NEXT generation can use a fresh analysis.

The existing `BSP-04-b` scenario (degrade + log) remains valid; it is supplemented
by the new `CAG-CG-01-c` scenario (degrade + log + enqueue refresh). BSP-04-b is
NOT superseded; both obligations apply.

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative, for test authoring)

- **CAG-CG-01** MUST be covered by unit tests on the orchestration service with a mocked
  repository: (a) fresh analysis → profile returned, no enqueue; (b) stale analysis →
  profile returned, enqueue called once; (c) no analysis → Creative fallback, enqueue
  called once, warning logged.
- **CAG-CG-02** MUST be covered by a unit test where `IClientOwnAccountRepository` returns
  null → no enqueue call, no exception, generation proceeds.
- **CAG-CG-03** parameterized tests: (a) `AestheticRefreshThresholdDays = null`, analysis at `UtcNow - 30d` → fresh; (b) `null` threshold, analysis at `UtcNow - 30d - 1s` → stale (enqueued); (c) custom threshold 7 days, analysis at `UtcNow - 10d` → stale (custom threshold wins over system default); (d) custom threshold 60 days, analysis at `UtcNow - 60d` → fresh. Assert enqueue behavior for each.
- **CAG-CG-04** MUST be covered by a test where the queue mock throws → exception caught,
  generation continues, warning logged.
- **CAG-CG-05** is a pure value-object unit test: verify all four fields, verify null
  profile shape, verify forward-compat shape.
- **CAG-CG-06** MUST be verified by unit tests on `ContentGenerationJobProcessor` that
  (a) assert `BackgroundDescriptors` reach `AiVisualDesigner` prompt, and (b) assert
  `VoiceTone` and `AverageSlidesPerCarousel` reach copy-generator prompts.
- **CAG-CG-07** MUST be verified by asserting no mutation occurs on any brand-manual
  or snapshot object during the generation run.
- **CAG-CG-08** MUST be verified by parameterized tests: raw value 12.5 → prompt receives
  10; raw value 1.5 → prompt receives 3; null → no section in prompt; AI output with
  deviant slide count → accepted (no retry triggered).
- **CAG-CG-09** prompt-content test: assert both concision directive text AND aesthetic
  hint text are present in the same assembled prompt string for CaseC; assert non-CaseC
  prompt has concision directive but NO aesthetic hint text.
- **CAG-CG-10** MUST be verified by a unit test that calls the debounce check twice for
  the same client — assert `IProfileAnalysisJobQueue.Enqueue` is called exactly ONCE.
- **CAG-CG-11**: compile-time / snapshot test on `ContentGenerationPromptVersions` — CI
  fails if a modified generator's constant matches its pre-change value.
- Architecture gate: no EF Core or Npgsql type may appear in `ClientAestheticProfile`
  or the orchestration service interface. The `pwsh -File scripts/gates/Run-Gates.ps1`
  gate enforces this.
