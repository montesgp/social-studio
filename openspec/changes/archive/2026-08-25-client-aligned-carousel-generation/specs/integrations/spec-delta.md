# Delta: Integrations — Per-Slide Image-Gen Degradation Observability

<!-- Change: client-aligned-carousel-generation -->
<!-- Applies to: openspec/specs/integrations/spec.md (additive delta) -->

## Purpose

Make the currently silent per-slide image-gen degradation (CSS fallback) observable by
writing execution-attempt telemetry for every `generate-image` attempt per creative slide.
Job completion semantics are unchanged (degradation remains non-fatal and the job status
remains `Completed`); the failure is no longer invisible. Config-driven provider routing
(CIG-INT-01 through CIG-INT-04) is unchanged and confirmed end-to-end by this change.

---

## PRD Traceability

| Requirement ID | Gap / Wiring |
|---|---|
| CAC-INT-01 | Wiring CRITICAL — `social.integration_execution_attempts` empty today; per-slide degradation invisible |

---

## ADDED Requirements

### Requirement: CAC-INT-01 — Per-Slide Image-Gen Attempt Recorded in Execution-Attempt Telemetry

For every `generate-image` call on a creative slide, the system MUST write a row to the
execution-attempt telemetry store (`social.integration_execution_attempts` or the
equivalent Application-layer port) carrying at minimum:

- Organization id
- Job id
- Slide index (zero-based)
- Step key (`generate-image`)
- Outcome (`success` or `degraded-to-css` with a structured reason)

Writing the attempt record MUST NOT block or fail the generation job; a telemetry write
failure MUST be caught, logged, and NOT propagated. Silent per-slide degradation MUST NOT
change job completion semantics: the job status remains `Completed` and `error_code` remains
null regardless of how many slides degraded.

#### Scenario: Successful image render — attempt recorded as success (CAC-INT-01-a)

- GIVEN a `ClientAesthetic` creative slide where image-gen produces a valid background
- WHEN the slide's generation step completes
- THEN an execution-attempt row is written with outcome = `success`, the slide index,
  and step key `generate-image`

#### Scenario: Image-gen degrades to CSS — attempt recorded as degraded; job still Completed (CAC-INT-01-b)

- GIVEN a `ClientAesthetic` creative slide where image-gen fails and falls back to the CSS background
- WHEN the fallback is applied
- THEN an execution-attempt row is written with outcome = `degraded-to-css` and the failure reason
- AND the job's overall status remains `Completed`
- AND `error_code` on the job record remains null
- AND the execution-attempt row is the mechanism that surfaces the degradation

#### Scenario: Telemetry write failure — logged and not propagated; job continues (CAC-INT-01-c)

- GIVEN the execution-attempt write call throws an exception
- WHEN the exception occurs during the telemetry write
- THEN the exception is caught and logged
- AND the generation job continues and completes normally
- AND no job-level failure is raised due to the telemetry failure

---

## MODIFIED Requirements

None. `CIG-INT-01` through `CIG-INT-04` (generate-image workflow step registration,
OpenAI as first MediaGeneration provider option, org-level IMAGE route resolution,
and catalog-driven UI rendering) remain valid and unchanged. Provider routing stays
config-driven and provider-agnostic; no hardcoded provider or model is introduced.

## REMOVED Requirements

None.
