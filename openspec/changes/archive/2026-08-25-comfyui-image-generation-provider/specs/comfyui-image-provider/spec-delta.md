# ComfyUI Image Provider Specification

<!-- Change: comfyui-image-generation-provider -->
<!-- Type: NEW capability — full spec (no prior openspec/specs/comfyui-image-provider/spec.md exists) -->

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
| CUI-03 | §Scope — `ComfyUiSettings` (base URL, checkpoint, workflow template, timeout) |
| CUI-04 | §Scope — `ComfyUiModelCatalogClient`, `/object_info` model sync |
| CUI-05 | §Scope Boundaries — architecture isolation: no provider types in Domain/Application |
| CUI-06 | §Scope Boundaries / Strict TDD — testability without a live ComfyUI server |

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

### Requirement: CUI-03 — ComfyUiSettings Carries Base URL, Checkpoint, Workflow Template, Timeout; Per-Org Configurable

`ComfyUiSettings` MUST expose at minimum: ComfyUI server base URL (default
`http://127.0.0.1:8188`), default checkpoint identifier, workflow template reference,
and polling timeout. Settings MUST be per-org configurable without a code change,
following the same shape as `ClaudePromptCliSettings`. Changing the base URL in settings
MUST redirect all ComfyUI HTTP calls to the configured endpoint with no code change.

#### Scenario: Custom base URL in settings — adapter uses configured endpoint (CUI-03-a)

- GIVEN `ComfyUiSettings.BaseUrl` is set to a custom endpoint for an org
- WHEN the adapter makes HTTP calls
- THEN all HTTP calls target the configured endpoint, not the default `127.0.0.1:8188`

#### Scenario: Base URL unset — adapter defaults to `http://127.0.0.1:8188` (CUI-03-b)

- GIVEN `ComfyUiSettings.BaseUrl` is not set
- WHEN the adapter makes HTTP calls
- THEN all HTTP calls target `http://127.0.0.1:8188`

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

#### Scenario: Server unreachable during sync — error surfaced; no crash (CUI-04-c)

- GIVEN the ComfyUI server is unreachable at catalog sync time
- WHEN the catalog client requests available models
- THEN an error result is returned (no unhandled exception)

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
