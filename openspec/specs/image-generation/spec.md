# Image Generation Specification

<!-- Last merged: content-image-generation (WU-10) -->

## Purpose

Define the observable requirements for the provider-agnostic, workflow-configured
AI text-to-image generation port introduced by the `content-image-generation` change.
This capability covers:

- The byte-returning image gateway and its ports.
- Usage-ledger instrumentation mirroring the text-AI gateway.
- Two-step prompt-then-render flow.
- Durable storage of generated images in Supabase Storage.
- The non-negotiable resilience contract (image-gen is never on the completion critical path).

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| IMG-01 | Proposal §Scope — IImageGenerationGateway + IImageModelClient ports, bytes-only contract |
| IMG-02 | Proposal §Scope — Usage-ledger instrumentation (ProviderCallContext decorator) |
| IMG-03 | Proposal §Scope — Two-step: text route writes image prompt; image route renders pixels |
| IMG-04 | Proposal §Scope — Durable Supabase Storage; URL/path reference, never data-URI |
| IMG-05 | Proposal §Scope — Resilience: failure → CSS fallback; NEVER fails the job |
| IMG-06 | Proposal §Scope — Provider-agnostic routing via IWorkflowConfigurationResolver |
| IMG-07 | Proposal §Affected Areas — IImageGenerationClient/NullImageGenerationClient superseded |
| IMG-08 | Proposal §Scope — Architecture gate: no provider types in Domain/Application |

---

## Requirements

### Requirement: IMG-01 — Image Gateway Returns Bytes; Contract Is Provider-Agnostic

The system MUST expose an `IImageGenerationGateway` port (Application layer) whose method
returns image bytes. The port contract MUST be expressed in terms of primitives and
Application-layer value objects only — no provider SDK types MAY appear in Domain or
Application. The gateway routes to an `IImageModelClient` adapter resolved by provider key.

#### Scenario: Gateway returns bytes without provider types in the call site (IMG-01-a)

- GIVEN an org with an IMAGE route configured (provider + model)
- WHEN a caller invokes `IImageGenerationGateway` with a prompt, org id, and generation parameters
- THEN the method returns raw image bytes (e.g., `byte[]` or equivalent)
- AND no OpenAI, Stability AI, or other provider SDK type is present in the gateway interface or its parameters

#### Scenario: Provider adapter resolved by provider key at runtime (IMG-01-b)

- GIVEN two registered `IImageModelClient` adapters for distinct provider keys
- WHEN the gateway is invoked with org id that resolves to provider key A
- THEN adapter A handles the call
- AND adapter B is never invoked

---

### Requirement: IMG-02 — Usage Ledger Instrumented via ProviderCallContext Decorator

Every call through `IImageGenerationGateway` MUST be instrumented by a usage-ledger
decorator that records the `OrganizationId`, provider key, model id, and outcome
(success/failure) in the usage ledger. The decorator MUST mirror the pattern already
used by the text-AI completion gateway.

#### Scenario: Successful image call recorded in usage ledger (IMG-02-a)

- GIVEN a configured IMAGE route and a successful pixel render
- WHEN the gateway completes the call
- THEN a usage-ledger entry is persisted with the org id, provider key, model id, and success outcome

#### Scenario: Failed image call also recorded (IMG-02-b)

- GIVEN a configured IMAGE route and a provider error (e.g., timeout)
- WHEN the gateway call fails
- THEN a usage-ledger entry is persisted with the failure outcome before the error is propagated to the caller
- AND the failure outcome does not prevent the entry from being written

---

### Requirement: IMG-03 — Two-Step Prompt-Then-Render Flow

For each creative slide, the system MUST execute two distinct, sequenced steps:

1. **Prompt step**: the org's TEXT route (existing `generate-content` text step or a
   dedicated image-prompt sub-step, as decided in design) produces a per-slide image
   prompt derived from `ClientAestheticProfile` (style descriptors, motifs, palette,
   reference context).
2. **Render step**: the org's IMAGE route sends the image prompt to an image-capable
   provider and receives raster bytes.

The prompt step output MUST be observable (loggable) independently of the render step.
No provider is hardcoded for either step.

#### Scenario: Text route produces image prompt; image route renders pixels (IMG-03-a)

- GIVEN a `ClientAesthetic` carousel job and an org with a TEXT route and an IMAGE route configured
- WHEN the two-step flow executes for a creative slide
- THEN step 1 produces a text image prompt using the org's TEXT route and `ClientAestheticProfile` signals
- AND step 2 sends that prompt to the org's IMAGE route and receives raster bytes
- AND no provider is referenced by name in the orchestration logic (Application layer)

#### Scenario: Empty or weak image prompt from step 1 triggers CSS fallback (IMG-03-b)

- GIVEN the TEXT route returns an empty or unusable image prompt
- WHEN step 2 would execute
- THEN the image render step is skipped
- AND the CSS designer background is used for that slide
- AND the event is logged with the job id, slide index, and reason ("empty prompt")
- AND the job continues and completes normally

---

### Requirement: IMG-04 — Generated Images Stored in Supabase Storage; Referenced by URL

Generated image bytes MUST be persisted to Supabase Storage (reusing the existing
generated-asset pattern) immediately after render. The composition envelope MUST reference
the image by a durable URL or storage path — NEVER as a data-URI or inline base-64.

#### Scenario: Generated image persisted before being used in composition (IMG-04-a)

- GIVEN a successful image render returning bytes
- WHEN the bytes are wired into the composition
- THEN the bytes are first uploaded to Supabase Storage
- AND the composition envelope carries the resulting durable URL/path as the background reference
- AND no base-64 or data-URI representation of the image appears in `composition_spec_json`

#### Scenario: Storage upload failure triggers CSS fallback (IMG-04-b)

- GIVEN a successful image render but a Supabase Storage upload failure
- WHEN the upload fails
- THEN the CSS designer background is used for that slide
- AND the failure is logged with the job id, slide index, and error detail
- AND the job continues and completes normally

---

### Requirement: IMG-05 — Image Generation Is Never on the Completion Critical Path

Any failure in the image-generation pipeline (prompt write, pixel render, or storage
upload) MUST be caught and handled. The format/job MUST complete normally with a CSS
designer background for the affected slide(s). A failure in image-gen MUST NOT propagate
an unhandled exception to the job runner or mark the job as failed.

#### Scenario: Image provider hard error — job still completes with CSS fallback (IMG-05-a)

- GIVEN a `ClientAesthetic` carousel job with one creative slide configured
- AND the IMAGE route provider returns a 5xx error
- WHEN image generation is attempted for the creative slide
- THEN the error is caught and logged
- AND the CSS designer background is used for that slide instead
- AND the remaining slides are generated normally
- AND the job completes with status success (not failed)

#### Scenario: Image provider timeout — same fallback behavior (IMG-05-b)

- GIVEN the IMAGE route provider times out for a creative slide
- WHEN image generation times out
- THEN the timeout is caught and logged
- AND the CSS designer background is used for that slide
- AND the job completes normally

---

### Requirement: IMG-06 — Provider Routing via IWorkflowConfigurationResolver; No Config = CSS Only

The IMAGE generation step MUST be resolved per org through the existing
`IWorkflowConfigurationResolver` using the `generate-image` step key (or equivalent
`MediaGeneration` step). If the org has no IMAGE route configured, the image-gen flow
MUST NOT be entered; the CSS designer background is used for ALL slides (creative and flat
alike) without logging an error — absence of config is a valid, expected state.

#### Scenario: No IMAGE route configured — CSS designer used silently for all slides (IMG-06-a)

- GIVEN a `ClientAesthetic` carousel job
- AND the org has no `generate-image` / IMAGE route configured
- WHEN the creative-slide background is resolved
- THEN the CSS designer background is used
- AND no warning or error is logged for the unconfigured route
- AND the job completes normally

#### Scenario: IMAGE route configured for org A but not org B — isolated routing (IMG-06-b)

- GIVEN org A has a `generate-image` route (OpenAI) and org B has none
- WHEN both orgs run `ClientAesthetic` carousel jobs
- THEN org A's creative slides go through the two-step image-gen flow
- AND org B's creative slides use the CSS designer (no image gen attempted)
- AND neither job's routing influences the other

---

### Requirement: IMG-07 — Legacy IImageGenerationClient Port Superseded

`IImageGenerationClient` (previously wired to `NullImageGenerationClient`) has been
superseded by the new `IClientAestheticImageBackgroundService` orchestrating port and its
constituent `IImageGenerationGateway` / `IImageModelClient` adapters introduced in this
change. Both the legacy port and its null adapter have been deleted (WU-9).

#### Scenario: No call site invokes IImageGenerationClient after the change (IMG-07-a)

- GIVEN the completed implementation
- WHEN all usages of `IImageGenerationClient` are inspected
- THEN no active generation pipeline path calls it
- AND the port is removed from the codebase (deletion confirmed in WU-9 commit)

---

### Requirement: IMG-08 — No Provider Types in Domain or Application Layers

Provider SDK types (e.g., OpenAI client classes, request/response DTOs from provider
packages) MUST NOT appear in Domain or Application layer projects. All provider-specific
code MUST reside in Infrastructure adapters.

#### Scenario: Architecture gate passes with no provider types in Application (IMG-08-a)

- GIVEN the completed implementation
- WHEN `pwsh -File scripts/gates/Run-Gates.ps1` is executed
- THEN the architecture gate reports no violations
- AND no OpenAI SDK type is referenced in Domain or Application projects

---

## Implementation Boundary Notes (non-normative)

- IMG-01/IMG-08: ports defined in `Application/Abstractions`; OpenAI adapter in `Infrastructure`.
- IMG-02: `ImageGenerationGatewayInstrumentationDecorator` wraps `IImageGenerationGateway` at DI resolution time.
- IMG-03: two-step orchestration lives in `ClientAestheticImageBackgroundService` (Application);
  prompt derivation reads `ClientAestheticProfile` fields via `IImagePromptWriter`.
- IMG-04: storage upload uses `IImageStorageWriter` Application port; no Supabase SDK types in Application.
- IMG-05/IMG-06: resilience boundary is a try/catch in `ContentGenerationJobProcessor` before the per-slide loop.
- IMG-07: `IImageGenerationClient` and `NullImageGenerationClient` deleted in WU-9; DI registration removed.
- Sidecar reuse: `carousel/slide_N.png.hash` → `{SHA256hex}|{storagePath}` avoids redundant generation.

---

<!-- Merged from openspec/changes/client-aligned-carousel-generation/specs/image-generation/spec-delta.md — change applied 2026-06-28 -->

## Added Requirements: Reference-Image Conditioning, Background-Only Contract, and Guardrail Retry (client-aligned-carousel-generation)

### Requirement: CAC-IMG-01 — `ImageGenerationRequest.Init` Carries Reference Images When Provider Supports Image Input

When the configured `generate-image` provider declares image-input capability,
`ImageGenerationRequest.Init` MUST be populated with a bounded set of the client's
durable archived reference images (sourced from the enriched aesthetic profile).
When the provider does NOT declare image-input support, `Init` MUST remain null and
the existing descriptor prompt (style descriptors + motifs from `ClientAestheticProfile`)
MUST drive generation. Provider capability detection MUST be performed at call time from
the configured provider's metadata; no provider is hardcoded.

#### Scenario: Image-input-capable provider — `Init` carries reference images (CAC-IMG-01-a)

- GIVEN a `ClientAesthetic` carousel job with durable reference images and a provider that
  declares image-input support
- WHEN `ImageGenerationRequest` is built for a creative slide
- THEN `Init` carries a bounded set of the client's archived reference images
- AND no provider SDK type appears in the Application layer call site

#### Scenario: Provider without image input — `Init` null; descriptor prompt drives generation (CAC-IMG-01-b)

- GIVEN the configured provider does NOT declare image-input support
- WHEN `ImageGenerationRequest` is built for a creative slide
- THEN `Init` is null
- AND the existing descriptor prompt (style descriptors, motifs) drives generation
- AND no error is raised for the absent image conditioning

#### Scenario: No archived reference images available — descriptor fallback; job continues (CAC-IMG-01-c)

- GIVEN a `ClientAesthetic` client with no durable archived reference images in the profile
- WHEN `ImageGenerationRequest` is built
- THEN `Init` is null regardless of provider image-input support
- AND the descriptor prompt is used and the job continues and completes normally

---

### Requirement: CAC-IMG-02 — Background-Only Generation Contract

The image-generation prompt MUST explicitly instruct the configured provider to generate
ONLY the slide background (photographic content, flat color, decorators, gradients).
The model MUST NOT produce text labels, logos, `@handle`, or signature elements in the
generated output. Text, `@handle`, logo, and signature are composited by the platform
post-render and MUST remain editable in the Content Studio.

#### Scenario: Prompt enforces background-only; post-render composition adds text (CAC-IMG-02-a)

- GIVEN a `ClientAesthetic` creative slide going through the image-generation pipeline
- WHEN the image-generation prompt is assembled
- THEN the prompt explicitly instructs the model to produce a background only
  (no text, no logos, no handles, no signatures)
- AND text and handle layers are composited on top of the generated background as editable elements

#### Scenario: Non-ClientAesthetic slides are unaffected by the background-only contract (CAC-IMG-02-b)

- GIVEN a carousel job where the client does NOT use the `ClientAesthetic` image-gen path
- WHEN generation runs
- THEN the background-only prompt constraint does NOT apply
- AND the existing generation path executes unchanged

---

### Requirement: CAC-IMG-03 — `VisualGuardrailValidator` Bounded Retry Per Creative Slide; Exceed Max → CSS Fallback

After each image render for a creative slide, the output MUST be validated by
`VisualGuardrailValidator`. If validation fails and the retry budget is not exhausted,
the render MUST be retried using the same configured provider. When the retry budget
is exhausted without a passing result, the CSS designer background MUST be used for
that slide and the failure MUST be logged. This is consistent with the IMG-05
resilience contract; the job MUST NOT be marked failed.

#### Scenario: First render passes validation — background accepted (CAC-IMG-03-a)

- GIVEN a creative slide render that produces a guardrail-passing background
- WHEN `VisualGuardrailValidator` runs
- THEN the background is accepted and used in composition

#### Scenario: First render fails; retry within budget passes — background accepted (CAC-IMG-03-b)

- GIVEN a first render that fails guardrail validation
- AND a second render (within retry budget) that passes
- WHEN `VisualGuardrailValidator` runs on the second render
- THEN the second background is accepted and the job proceeds normally

#### Scenario: All retries exhausted — CSS fallback; job completes (CAC-IMG-03-c)

- GIVEN all renders for a creative slide exhaust the retry budget without passing guardrails
- WHEN the retry budget is exhausted
- THEN the CSS designer background is used for that slide
- AND the failure is logged with job id, slide index, and reason
- AND the job continues and completes normally (no job-level failure is raised)

*Merged from `openspec/changes/client-aligned-carousel-generation/specs/image-generation/spec-delta.md` — change applied 2026-06-28.*

---

<!-- Merged from openspec/changes/client-aesthetic-conceptual-detection/specs/image-generation/spec-delta.md — change applied 2026-07-01 -->

## Added Requirements: Concept-Decided, Ratio-Aware, Text-Free Digested Instruction (client-aesthetic-conceptual-detection)

### Purpose

Specify the observable contract change at the image-generation boundary: when
`ConceptualMethod` is known, the per-slide image prompt arriving at the image model
MUST be a pre-decided, digested instruction (concept already chosen in the client's
method) rather than a thin style-descriptor string. The prompt remains text-free
(consistent with CAC-IMG-02) and is ratio-aware when `ImageTypeRatio` is known.
When `ConceptualMethod` is null, the existing descriptor prompt is used unchanged
(cold-start parity).

### Client Exemplars (Anchors for Acceptance Tests)

| Client | Expected Prompt Shape |
|---|---|
| Atlas (`@atlas.editorial`) | Specific editorial metaphor concept for the slide topic; mono-color photographic or flat cue per ratio; no text/logo/handle in the image |
| Comet Comedy | Specific tender watercolor scene for the slide topic; ratio-consistent background-type cue; no text/logo/handle in the image |

---

### Requirement: CACD-IMG-01 — Per-Slide Image Prompt Is Concept-Decided and Ratio-Aware When `ConceptualMethod` Is Non-Null

When the image generation instruction for a creative slide was produced by
`ImagePromptWriter` with a non-null `ConceptualMethod`, the prompt arriving at the
image model MUST be a pre-decided, digested instruction: the visual concept is already
specified (a concrete metaphor or literal scene, not a generic style description).
The prompt MUST remain text-free — it MUST NOT instruct the model to place text,
labels, logos, `@handle`, or signatures in the generated output (consistent with
CAC-IMG-02). The prompt MUST include a ratio-aligned background-type cue when
`ImageTypeRatio` is non-null.

Prompts for Atlas-type clients and Comet-type clients MUST be distinguishably
different for the same slide topic.

#### Scenario: Metaphorical-editorial client → digested editorial metaphor prompt (CACD-IMG-01-a)

- GIVEN a `ClientAesthetic` creative slide for an Atlas-type client with
  a specific invented editorial metaphor in the prompt (e.g., a staged still-life
  composition representing the slide topic)
- WHEN the image model receives the prompt
- THEN the prompt contains the specific concept description (not a generic "editorial
  style background" descriptor)
- AND the prompt does NOT instruct the model to place text, labels, logos, or handles
  in the generated image (text-free background-only contract, consistent with CAC-IMG-02)
- AND the prompt includes a cue toward photographic or flat-color per the detected ratio

#### Scenario: Literal-illustration client → digested illustrative scene prompt (CACD-IMG-01-b)

- GIVEN a `ClientAesthetic` creative slide for a Comet-type client with a specific
  invented tender watercolor scene in the prompt
- WHEN the image model receives the prompt
- THEN the prompt contains the specific scene description (not a generic "illustration
  style background" descriptor)
- AND the prompt does NOT instruct the model to place text, labels, logos, or handles
  in the generated image
- AND the prompt includes a ratio-consistent background-type cue

#### Scenario: Same topic, different clients → distinguishably different prompts (CACD-IMG-01-c)

- GIVEN an Atlas-type and a Comet-type client, both generating a creative slide
  for the same topic "finding calm in chaos"
- WHEN the image prompts for each are assembled
- THEN the Atlas prompt contains a metaphorical editorial concept
- AND the Comet prompt contains a literal illustrative scene
- AND the two prompts are discernibly different from each other

#### Scenario: All existing resilience contracts (IMG-05, CAC-IMG-03) apply unchanged (CACD-IMG-01-d)

- GIVEN a creative slide with a concept-decided prompt and a provider error or
  guardrail failure
- WHEN the image-generation pipeline handles the failure
- THEN the CSS designer background fallback is used for that slide
- AND the job does NOT fail (consistent with IMG-05 and CAC-IMG-03)
- AND no new failure mode is introduced by the concept-decided prompt

---

### Requirement: CACD-IMG-02 — Cold-Start: Null `ConceptualMethod` → Existing Descriptor Prompt; Full Pipeline Unchanged

When `ConceptualMethod` is null on `ClientAestheticProfile`, the image generation
prompt MUST be the same descriptor-based prompt produced before this change. No
concept ideation section and no ratio cue MUST be added. The full image-generation
pipeline (IMG-01 through IMG-08, CAC-IMG-01 through CAC-IMG-03) MUST behave
identically to its pre-change state for a cold-start client.

#### Scenario: `ConceptualMethod` null → descriptor prompt; pipeline identical to pre-change (CACD-IMG-02-a)

- GIVEN a `ClientAesthetic` client with `ConceptualMethod = null`
  and `ImageTypeRatio = null`
- WHEN the image generation pipeline runs for a creative slide
- THEN the prompt sent to the image model is the existing descriptor-based prompt
  (no ideation section, no ratio section)
- AND the remaining pipeline (guardrail retry, CSS fallback, storage upload,
  reference-image conditioning, background-only contract) executes identically
  to its pre-change behavior
- AND the job completes normally

---

## Modified Requirements (client-aesthetic-conceptual-detection)

None. `CAC-IMG-02` (background-only / text-free generation contract) is reinforced:
concept-ideated prompts MUST still be text-free. `IMG-05`, `IMG-06`, and `CAC-IMG-03`
resilience contracts apply unchanged regardless of whether the prompt is concept-decided
or descriptor-based.

*Merged from `openspec/changes/client-aesthetic-conceptual-detection/specs/image-generation/spec-delta.md` — change applied 2026-07-01.*
