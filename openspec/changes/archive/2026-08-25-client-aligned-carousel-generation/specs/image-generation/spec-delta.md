# Delta: Image Generation — Reference-Image Conditioning + Background-Only Contract + Guardrail Retry

<!-- Change: client-aligned-carousel-generation -->
<!-- Applies to: openspec/specs/image-generation/spec.md (additive delta) -->

## Purpose

Realize the reserved `ImageGenerationRequest.Init` conditioning slot (OQ-7): pass the
client's archived reference images to the image gateway when the configured provider
supports image input, degrading to the existing descriptor prompt otherwise. Enforce
a background-only generation contract (model MUST NOT draw text, logos, or handles).
Apply `VisualGuardrailValidator` with a bounded retry per creative slide; exhausted
retries fall back to the CSS designer background consistent with IMG-05.

---

## PRD Traceability

| Requirement ID | Gap / Direction |
|---|---|
| CAC-IMG-01 | GAP 3 — `ImageGenerationRequest.Init` always null; reference images never passed to gateway |
| CAC-IMG-02 | Direction #1 — background-only generation; text/`@handle`/logo/signature composited by platform |
| CAC-IMG-03 | Proposal §Scope — `VisualGuardrailValidator` bounded retry; exceed max → CSS fallback |

---

## ADDED Requirements

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

---

## MODIFIED Requirements

None. `IMG-03` (two-step prompt-then-render flow) remains valid; `CAC-IMG-01` extends
step 2 with image conditioning when the provider supports it, consistent with that
flow. `IMG-05` (image-gen never on critical path) remains valid; `CAC-IMG-03`'s
bounded retry is additive and fully consistent with that resilience contract.

## REMOVED Requirements

None.
