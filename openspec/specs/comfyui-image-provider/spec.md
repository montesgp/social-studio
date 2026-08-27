# ComfyUI Image Provider Specification

<!-- Merged from openspec/changes/comfyui-image-generation-provider/specs/comfyui-image-provider/spec-delta.md — change applied 2026-07-01. -->
<!-- Merged from openspec/changes/comfyui-detailer-preflight/specs/comfyui-image-provider/spec-delta.md — change applied 2026-08-27 (CUI-07/08/09 added, CUI-03 modified). -->

## Purpose

Define the requirements for `ComfyUiImageModelClient`, a zero-marginal-cost local-GPU
`IImageModelClient` adapter that drives a ComfyUI server via HTTP to produce
style-conditioned slide backgrounds. Covers the adapter, settings, model-catalog sync,
architecture isolation, and testability contract.

---

## PRD Traceability

| Requirement ID | Proposal Section |
|---|---|
| CUI-01 | §Scope — `ComfyUiImageModelClient`, `IImageModelClient`, `SupportsImageConditioning` |
| CUI-02 | §Scope — resilience: degrade to CSS fallback; job NEVER fails |
| CUI-03 | §Scope — `ComfyUiSettings` (base URL, checkpoint, workflow template, timeout, detailer opt-out) |
| CUI-04 | §Scope — `ComfyUiModelCatalogClient`, `/object_info` model sync |
| CUI-05 | §Scope Boundaries — architecture isolation: no provider types in Domain/Application |
| CUI-06 | §Scope Boundaries / Strict TDD — testability without a live ComfyUI server |
| CUI-07 | `comfyui-detailer-preflight` — tri-state node-presence probe |
| CUI-08 | `comfyui-detailer-preflight` — template selection, conservative-fallback invariant |
| CUI-09 | `comfyui-detailer-preflight` — probe-result cache, TTL, asymmetric invalidation |

---

## Requirements

### Requirement: CUI-01 — Adapter Implements IImageModelClient; IP-Adapter Workflow Returns Image Bytes

`ComfyUiImageModelClient` MUST implement `IImageModelClient`. Its `Capabilities` property
MUST return `SupportsImageConditioning = true`. Given a digested text prompt and a
populated `ImageGenerationRequest.Init` (reference-image bytes), the adapter MUST submit
a parameterized IP-Adapter workflow to the ComfyUI server, poll for completion, and fetch
and return the output image bytes. When `Init` is null or empty, the adapter MUST execute
a text-prompt-only workflow variation. No provider SDK or ComfyUI HTTP type MAY appear in
the adapter's public contract.

#### Scenario: Style-conditioned request — returns image bytes (CUI-01-a)

- GIVEN a configured `ComfyUiImageModelClient` with a reachable ComfyUI server
- AND an `ImageGenerationRequest` with a digested prompt and reference images in `Init`
- WHEN `GenerateAsync` is invoked
- THEN image bytes are returned
- AND `Capabilities.SupportsImageConditioning` is `true`

#### Scenario: Unconditioned request (no reference images) — returns image bytes (CUI-01-b)

- GIVEN an `ImageGenerationRequest` with only a text prompt and `Init` null
- WHEN `GenerateAsync` is invoked
- THEN image bytes are returned using a text-only workflow variant
- AND no error is raised for the absent conditioning data

#### Scenario: HTTP workflow follows queue → poll → fetch sequence (CUI-01-c)

- GIVEN a fake HTTP handler that validates call sequence
- WHEN `GenerateAsync` is invoked
- THEN the adapter first submits the workflow to the queue endpoint
- AND then polls the history/status endpoint until completion
- AND then fetches the output bytes from the retrieval endpoint

---

### Requirement: CUI-02 — Any ComfyUI Failure or Timeout Returns a Failure Result; Job NEVER Fails

When the ComfyUI server is unreachable, returns an HTTP error, or exceeds the configured
polling timeout, the adapter MUST return a failure result to the caller. The adapter MUST
NOT propagate an unhandled exception. The image-generation pipeline MUST degrade the
affected slide to the CSS designer background per the IMG-05 resilience contract. The
format job MUST complete normally regardless of how many slides fail image generation.

#### Scenario: Server unreachable — failure result; CSS fallback; job completes (CUI-02-a)

- GIVEN the ComfyUI server is unreachable (connection refused)
- WHEN the adapter attempts the workflow
- THEN the adapter returns a failure result (no unhandled exception propagated)
- AND the pipeline applies the CSS designer background for that slide
- AND the job completes normally

#### Scenario: Server returns HTTP error — same failure path (CUI-02-b)

- GIVEN the ComfyUI server returns a non-2xx HTTP response
- WHEN the adapter processes the response
- THEN a failure result is returned
- AND the CSS fallback is applied; the job completes normally

#### Scenario: Poll exceeds configured timeout — failure result (CUI-02-c)

- GIVEN the ComfyUI server does not complete the workflow within the configured timeout
- WHEN the polling deadline elapses
- THEN the adapter returns a failure result without throwing

---

### Requirement: CUI-03 — ComfyUiSettings Carries Base URL, Checkpoint, Workflow Template, Timeout, Detailer Opt-Out; Per-Org Configurable

`ComfyUiSettings` MUST expose at minimum: base URL (default `http://127.0.0.1:8188`),
checkpoint identifier, workflow template reference, polling timeout, and a detailer
opt-out flag (default unset — probe governs). Settings MUST be per-org configurable
without a code change, following the same shape as `ClaudePromptCliSettings`. Changing
the base URL in settings MUST redirect all ComfyUI HTTP calls to the configured endpoint
with no code change. The opt-out flag MUST only ever force the plain template; it MUST
NOT force the augmented template on an unsupporting backend.
(Previously: no detailer opt-out flag; only base URL/checkpoint/template/timeout.)

#### Scenario: Custom base URL in settings — adapter uses configured endpoint (CUI-03-a)

- GIVEN `ComfyUiSettings.BaseUrl` is set to a custom endpoint for an org
- WHEN the adapter makes HTTP calls
- THEN all HTTP calls target the configured endpoint, not the default `127.0.0.1:8188`

#### Scenario: Base URL unset — adapter defaults to `http://127.0.0.1:8188` (CUI-03-b)

- GIVEN `ComfyUiSettings.BaseUrl` is not set
- WHEN the adapter makes HTTP calls
- THEN all HTTP calls target `http://127.0.0.1:8188`

---

### Requirement: CUI-07 — Node-Presence Probe Returns a Tri-State Result; Unknown Is Never Coerced to Supported

Before submitting a prompt, the adapter MUST determine detailer support via `GET
{baseUrl}/object_info`, reusing `CUI-04`'s `class_type`-presence parsing. The probe MUST
return exactly one of `Supported`, `NotSupported`, `Unknown`, and MUST NOT throw into the
generation path. `Supported` MUST require both node classes present in a well-formed
response; every other shape (neither/one present, non-2xx, timeout, transport exception,
malformed body) MUST resolve to `NotSupported` or `Unknown`, never `Supported`.

#### Scenario: Both classes present — Supported (CUI-07-a)
- GIVEN a well-formed response listing `FaceDetailer` and `UltralyticsDetectorProvider`
- WHEN the probe runs
- THEN it returns `Supported`

#### Scenario: Partial presence — NotSupported (CUI-07-b)
- GIVEN a response listing only one of the two classes
- WHEN the probe runs
- THEN it returns `NotSupported`, not `Supported`

#### Scenario: Transport failure or malformed body — Unknown, no exception (CUI-07-c)
- GIVEN a non-2xx response, timeout, transport exception, or unparseable body
- WHEN the probe runs
- THEN it returns `Unknown` and no exception propagates

---

### Requirement: CUI-08 — Template Selection Requires an Affirmative Probe; Every Other Path Selects Plain

The adapter MUST select the augmented template only when the probe (fresh or cached) is
`Supported` AND the opt-out flag is unset. `NotSupported`, `Unknown`, and opt-out MUST all
select plain. On any backend where the probe is never `Supported` — including local dev
today — the submitted graph and generation outcome MUST match pre-change behaviour
byte-for-byte, with no added latency beyond one cached probe call.

#### Scenario: Positive probe selects the augmented template (CUI-08-a)
- GIVEN a `Supported` probe result and the opt-out flag unset
- WHEN a generation is submitted
- THEN the graph contains `FaceDetailer` and `UltralyticsDetectorProvider`, wired between
  the sampler and the output node

#### Scenario: Negative, unknown, or opted-out probe selects plain (CUI-08-b, regression)
- GIVEN a `NotSupported`/`Unknown` result, or a `Supported` result with the opt-out flag
  set
- WHEN a generation is submitted
- THEN the plain template is submitted byte-identical to pre-change behaviour, and
  opt-out makes no probe call

---

### Requirement: CUI-09 — Probe Result Cached Per Base URL, 10-Minute TTL, Asymmetric Invalidation

The adapter MUST cache a resolved probe result (not the raw payload) keyed by normalized
`baseUrl` for 10 minutes. `Unknown` results MUST NOT be cached. A cached `Supported`
result MUST be invalidated immediately when a submission using the augmented template
fails with a node-registration error attributable to the detailer subgraph, so the next
job for that `baseUrl` submits plain. A cached `NotSupported` result MUST NOT be
proactively invalidated and MUST simply expire at TTL.

#### Scenario: Cache hit avoids re-probing within TTL (CUI-09-a)
- GIVEN a cached result under 10 minutes old
- WHEN a second generation targets the same `baseUrl`
- THEN no second `/object_info` call is made

#### Scenario: Positive invalidated by a detailer node_errors failure (CUI-09-b)
- GIVEN a cached `Supported` result and a submission that fails with detailer-attributable
  `node_errors` (accepted residual: node registration does not guarantee model weights)
- WHEN the next generation targets the same `baseUrl`
- THEN plain is submitted without waiting for TTL expiry

#### Scenario: Negative result rides out the full TTL (CUI-09-c)
- GIVEN a cached `NotSupported` result
- WHEN any signal other than TTL expiry occurs
- THEN the cached result is unchanged until 10 minutes elapse

---

### Requirement: CUI-04 — ComfyUiModelCatalogClient Lists Installed Checkpoints from /object_info

`ComfyUiModelCatalogClient` MUST query the ComfyUI server for installed checkpoints (and
compatible conditioning models) and return them as model entries compatible with the
existing model-sync contract. This allows the Integrations routing UI to populate model
selectors for the `generate-image` step, providing the same UX as the OpenAI and
claude-p catalog sync.

#### Scenario: Catalog client returns installed checkpoints (CUI-04-a)

- GIVEN a ComfyUI server with at least one installed checkpoint
- WHEN the catalog client requests available models
- THEN the response contains at least one model entry with a non-empty identifier
- AND no provider-specific type appears in the returned model list contract

#### Scenario: No checkpoints installed — empty list; no error (CUI-04-b)

- GIVEN a ComfyUI server with no installed checkpoints
- WHEN the catalog client requests available models
- THEN an empty model list is returned and no error is raised

#### Scenario: Server unreachable during sync — curated fallback returned as a success; sync never errors (CUI-04-c)

- GIVEN the ComfyUI server is unreachable, returns a non-2xx response, or returns a
  malformed `/object_info` payload at catalog sync time
- WHEN the catalog client requests available models
- THEN a `Result.Success` is returned containing `ComfyUiSettings.DefaultCheckpoints`
  tagged as `source = "curated"` entries
- AND no unhandled exception propagates and no `Result.Failure` is ever returned by
  `ListModelsAsync` — "Sync models" NEVER errors, regardless of server reachability

---

### Requirement: CUI-05 — No ComfyUI or HTTP Types in Domain or Application Layers

All ComfyUI HTTP concerns, workflow parameterization, and provider-specific types MUST
be confined to the Infrastructure adapter. Domain and Application layers MUST NOT
reference any ComfyUI-specific, raw `HttpClient`, or provider SDK type. The architecture
gate (`pwsh -File scripts/gates/Run-Gates.ps1`) MUST pass with no violations.

#### Scenario: Architecture gate passes after implementation (CUI-05-a)

- GIVEN the completed implementation
- WHEN `pwsh -File scripts/gates/Run-Gates.ps1` is executed
- THEN the architecture gate reports no violations
- AND no ComfyUI or HTTP provider type is found in Domain or Application projects

---

### Requirement: CUI-06 — Adapter Is Fully Testable Without a Live ComfyUI Server

All adapter, settings, and catalog behaviors MUST be testable via a fake HTTP message
handler (or equivalent test double) injected in place of the real HTTP client. No test
MUST require a live ComfyUI server. This mirrors the existing `MultimodalAestheticStyleExtractorTests`
and OpenAI adapter test patterns. Tests MUST be written before implementation (Strict TDD).

#### Scenario: Conditioned generation test passes with fake HTTP handler (CUI-06-a)

- GIVEN a test configuring the adapter with a fake handler that returns fixture image bytes
- WHEN `GenerateAsync` is invoked with a prompt and reference images
- THEN the returned bytes match the fixture
- AND no real HTTP call is made

#### Scenario: Failure path testable with fake handler returning an error response (CUI-06-b)

- GIVEN a test configuring a fake handler that returns an HTTP error
- WHEN `GenerateAsync` is invoked
- THEN a failure result is returned
- AND no real HTTP call to a ComfyUI server is made

#### Scenario: Catalog sync testable with fake handler returning checkpoint data (CUI-06-c)

- GIVEN a test configuring a fake handler that returns a fixture `/object_info` response
- WHEN the catalog client requests models
- THEN the returned model list matches the fixture checkpoints
- AND no real ComfyUI server is required

---

## Added Requirements: BackgroundOnly Parity, Anatomy Negative-Prompt Terms (client-aesthetic-generation-hardening)

### Requirement: CAGH-CUI-01 — Adapter Honours request.BackgroundOnly, Injecting the Directive Into the Submitted Prompt

`ComfyUiImageModelClient` MUST read `ImageGenerationRequest.BackgroundOnly`. When `true`, the
adapter MUST inject the background-only directive text into the prompt it submits to the ComfyUI
server, matching `OpenAiImageModelClient`'s directive semantics. When `false` or unset, the adapter's
submitted prompt MUST be unaffected by this requirement (no directive text injected, no other prompt
content changed as a result of this flag).

#### Scenario: BackgroundOnly=true injects the directive into the submitted prompt (CAGH-CUI-01-a)

- GIVEN an `ImageGenerationRequest` with `BackgroundOnly = true`
- WHEN `GenerateAsync` submits the workflow to the ComfyUI server
- THEN the submitted prompt text contains the background-only directive text

#### Scenario: BackgroundOnly=false does not change adapter prompt behaviour (CAGH-CUI-01-b, regression)

- GIVEN an `ImageGenerationRequest` with `BackgroundOnly = false` (or unset)
- WHEN `GenerateAsync` submits the workflow
- THEN the submitted prompt does NOT contain the background-only directive text
- AND the submitted prompt is otherwise identical to pre-change behaviour for the same request

#### Scenario: Directive text is defined once, shared with the OpenAI adapter (CAGH-CUI-01-c)

- GIVEN the completed implementation
- WHEN the background-only directive text is inspected in both adapters
- THEN both adapters reference the same single defined directive text (no duplicated/drifted string)

**Implementation note (non-normative)**: the directive is injected via the shared
`Infrastructure/Ai/Providers/ImagePromptDirectives.BackgroundOnly` constant, referenced directly by
both `OpenAiImageModelClient` and `ComfyUiImageModelClient` (`tasks.md` WU-4, task 4.1, closed by a
follow-up batch under issue #80). CAGH-CUI-01-c is satisfied at the source level, not just
semantically: both adapters trace back to the exact same defined constant, asserted by
`ImagePromptDirectivesTests.cs`.

---

### Requirement: CAGH-CUI-02 — Default Negative Prompt Includes Anatomy-Defect Terms Alongside Existing Text-Suppression Terms

`ComfyUiImageModelClient.DefaultNegativePrompt` MUST include anatomy-defect suppression terms
(at minimum: extra limbs, extra fingers, bad hands, malformed hands, duplicate face, cloned face,
mutated) in addition to every text-suppression term already present. No pre-existing
text-suppression term MAY be removed. `DefaultNegativePrompt` is the sole negative-prompt source
used by every ComfyUI generation request today (no per-org override path exists in the codebase);
this requirement covers only that single constant.

#### Scenario: Anatomy terms present in the default negative prompt (CAGH-CUI-02-a)

- GIVEN the default negative prompt used for a ComfyUI generation request
- WHEN its content is inspected
- THEN it contains anatomy-defect terms (extra limbs, extra fingers, bad/malformed hands, duplicate
  or cloned face, mutated)

#### Scenario: Existing text-suppression terms are retained (CAGH-CUI-02-b, regression)

- GIVEN the default negative prompt before this change contained a set of text-suppression terms
- WHEN the post-change default negative prompt is inspected
- THEN every pre-change text-suppression term is still present

## Modified Requirements (client-aesthetic-generation-hardening)

None. `CAGH-CUI-01` and `CAGH-CUI-02` are additive: they close gaps in existing adapter behaviour
(`BackgroundOnly` was never read; the negative prompt already existed and gains terms) without
changing any previously specified requirement (`CUI-01`..`CUI-06`).

## Removed Requirements (client-aesthetic-generation-hardening)

None.

*Merged from `openspec/changes/client-aesthetic-generation-hardening/specs/comfyui-image-provider/spec-delta.md` — change applied 2026-08-25.*

---

## Modified Requirements (comfyui-detailer-preflight)

`CUI-03` was amended in place (above, in `## Requirements`) to add the `DetailerEnabled`
opt-out flag; the rest of the requirement is unchanged.

## Removed Requirements (comfyui-detailer-preflight)

None. `CUI-07`, `CUI-08`, `CUI-09` are additive.

*Merged from `openspec/changes/comfyui-detailer-preflight/specs/comfyui-image-provider/spec-delta.md`
— change applied 2026-08-27. Phase 1-6 (probe, cache, augmented template, selection, invalidation,
opt-out flag) shipped as real code; Phase 7 (owner-run manual E2E confirming the live splice point
and `FaceDetailer` input contract) is pending human action and does not block this merge.*
