# Delta: Image Generation — Concept-Decided, Ratio-Aware, Text-Free Digested Instruction

<!-- Change: client-aesthetic-conceptual-detection -->
<!-- Applies to: openspec/specs/image-generation/spec.md (additive delta) -->

## Purpose

Specify the observable contract change at the image-generation boundary: when
`ConceptualMethod` is known, the per-slide image prompt arriving at the image model
MUST be a pre-decided, digested instruction (concept already chosen in the client's
method) rather than a thin style-descriptor string. The prompt remains text-free
(consistent with CAC-IMG-02) and is ratio-aware when `ImageTypeRatio` is known.
When `ConceptualMethod` is null, the existing descriptor prompt is used unchanged
(cold-start parity).

---

## PRD Traceability

| Requirement ID | Proposal Anchor |
|---|---|
| CACD-IMG-01 | §Product Direction #1 and #4 — Concept ideation at prompt-writing time; arrive at the image generator with a fully digested instruction |
| CACD-IMG-02 | §Success Criteria — Cold-start: clients with no conceptual data produce output identical to today |

---

## Client Exemplars (Anchors for Acceptance Tests)

| Client | Expected Prompt Shape |
|---|---|
| Atlas (`@atlas.editorial`) | Specific editorial metaphor concept for the slide topic; mono-color photographic or flat cue per ratio; no text/logo/handle in the image |
| Comet Comedy | Specific tender watercolor scene for the slide topic; ratio-consistent background-type cue; no text/logo/handle in the image |

---

## ADDED Requirements

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

## MODIFIED Requirements

None. `CAC-IMG-02` (background-only / text-free generation contract) is reinforced:
concept-ideated prompts MUST still be text-free. `IMG-05`, `IMG-06`, and `CAC-IMG-03`
resilience contracts apply unchanged regardless of whether the prompt is concept-decided
or descriptor-based.

## REMOVED Requirements

None.
