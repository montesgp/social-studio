# Delta: Image Generation — ComfyUI as Real SupportsImageConditioning Provider

<!-- Change: comfyui-image-generation-provider -->
<!-- Applies to: openspec/specs/image-generation/spec.md (additive delta) -->

## Purpose

Record that the `generate-image` step now has a real, style-conditioning-capable provider
(`comfyui`) selectable via configuration. The existing CAC-IMG-01 capability-detection
mechanism already routes reference images to any provider that declares
`SupportsImageConditioning = true`; this delta confirms `ComfyUiImageModelClient` satisfies
that contract end-to-end. No existing requirement is modified. Orgs not routed to
`comfyui` are unaffected.

---

## PRD Traceability

| Requirement ID | Proposal Section |
|---|---|
| CUI-IMG-01 | §Confirmed Product Direction #2 — style-conditioned generation; `SupportsImageConditioning = true` |
| CUI-IMG-02 | §Scope / Rollback Plan — additive, config-gated; non-comfyui orgs unaffected |

---

## ADDED Requirements

### Requirement: CUI-IMG-01 — comfyui Provider Satisfies SupportsImageConditioning; End-to-End Conditioning Resolves

When `comfyui` is configured as the `generate-image` route provider for an org,
`ComfyUiImageModelClient.Capabilities.SupportsImageConditioning` MUST be `true`, causing
the existing CAC-IMG-01 mechanism to populate `ImageGenerationRequest.Init` with the
client's reference images. The adapter MUST produce a style-conditioned background image
using those reference images. No provider is hardcoded in the Application layer; the
conditioning capability MUST be detected at call time from the provider's metadata.

#### Scenario: comfyui route configured — reference images pass through conditioning (CUI-IMG-01-a)

- GIVEN an org with `generate-image` routed to `comfyui`
- AND the client has durable archived reference images in the aesthetic profile
- WHEN the two-step image-generation flow executes for a creative slide
- THEN `ImageGenerationRequest.Init` is populated with a bounded set of reference images
- AND `ComfyUiImageModelClient` receives those reference images and returns style-conditioned bytes
- AND no provider name is hardcoded in the Application orchestration layer

#### Scenario: comfyui route configured; no reference images — text-prompt workflow executes (CUI-IMG-01-b)

- GIVEN an org with `generate-image` routed to `comfyui`
- AND the client has no durable archived reference images
- WHEN the image-generation flow executes for a creative slide
- THEN `ImageGenerationRequest.Init` is null (per CAC-IMG-01-c)
- AND the adapter executes a text-prompt-only workflow variant and returns bytes
- AND the job continues and completes normally

#### Scenario: ComfyUI failure during conditioned generation — CSS fallback; job completes (CUI-IMG-01-c)

- GIVEN an org with `generate-image` routed to `comfyui`
- AND the ComfyUI server fails or times out during the workflow
- WHEN the adapter returns a failure result
- THEN the image-generation pipeline degrades the slide to the CSS designer background
- AND the failure is logged per IMG-05
- AND the job completes normally (no job-level failure raised)

---

### Requirement: CUI-IMG-02 — Additive and Config-Gated; Orgs Not Routed to comfyui Unaffected

The addition of `comfyui` as a `generate-image` provider MUST NOT change any behavior for
orgs that are not configured to use it. Existing providers (`openai`, `claude-p`, etc.)
MUST continue to function exactly as before. The CSS-only path (no `generate-image` route
configured) MUST also remain unaffected.

#### Scenario: Org routed to OpenAI — comfyui never invoked; existing behavior unchanged (CUI-IMG-02-a)

- GIVEN org A has `generate-image` routed to OpenAI and org B has no image route
- WHEN both orgs run carousel generation jobs
- THEN org A's image step invokes `OpenAiImageModelClient` as before
- AND org B uses the CSS designer background as before
- AND `ComfyUiImageModelClient` is never invoked for either org

#### Scenario: Removing comfyui route config reverts org to prior behavior (CUI-IMG-02-b)

- GIVEN an org whose `generate-image` route is changed from `comfyui` to absent
- WHEN a carousel job runs
- THEN the CSS designer background is used for creative slides (per IMG-06)
- AND no error is raised and no job fails

---

## MODIFIED Requirements

None. `IMG-01` through `IMG-08` and `CAC-IMG-01` through `CAC-IMG-03` remain valid and
unchanged. `ComfyUiImageModelClient` satisfies the existing `IImageModelClient` port and
capability-detection contracts without modification to any Application layer requirement.
`IMG-06` (routing via `IWorkflowConfigurationResolver`) already supports multiple providers
by design; `comfyui` is a new selectable option within that existing mechanism.

## REMOVED Requirements

None.
