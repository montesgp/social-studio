# Delta: Content Generation — Concept Ideation in ImagePromptWriter, Ratio-Aware Prompt

<!-- Change: client-aesthetic-conceptual-detection -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->

## Purpose

Upgrade `ImagePromptWriter` to perform concept ideation when the client's
`ConceptualMethod` is known, and to produce ratio-aware instructions when
`ImageTypeRatio` is known. When both fields are null (cold-start), the output
MUST be identical to the pre-change prompt.

---

## PRD Traceability

| Requirement ID | Proposal Anchor |
|---|---|
| CACD-CG-01 | §Scope — Concept-ideation prompt-writing: ImagePromptWriter invents the visual metaphor |
| CACD-CG-02 | §Scope — ImagePromptWriter honors the detected photo/flat ratio |
| CACD-CG-03 | §Success Criteria — Cold-start parity at the prompt-writing layer |

---

## Client Exemplars (Anchors for Acceptance Tests)

| Client | Method | Expected Ideation Output |
|---|---|---|
| Atlas (`@atlas.editorial`) | Metaphorical-editorial: idea-driven visual metaphors via mono-color still-life and staged photography | A concrete staged still-life or editorial composition that metaphorically represents the slide's topic; saturated mono-color background; no text in the image |
| Comet Comedy | Literal-illustration: tender watercolor illustrations of children | A specific tender watercolor scene literally depicting the slide's topic with children characters; warm cream background; no text in the image |

---

## ADDED Requirements

### Requirement: CACD-CG-01 — `ImagePromptWriter` Performs Concept Ideation When `ConceptualMethod` Is Non-Null

When `ClientAestheticProfile.ConceptualMethod` is non-null, `ImagePromptWriter.BuildInput`
(or equivalent prompt-assembly method) MUST invent a method-faithful visual concept for
the slide's specific topic — a concrete visual metaphor or literal scene decided in the
client's detected art-direction method. The invented concept MUST appear in the image
generation instruction. The background MUST remain TEXT-FREE (consistent with CAC-IMG-02).
The resulting instruction MUST be discernibly richer than a thin style-descriptor prompt.

Atlas-type prompts MUST contain a specific metaphorical or editorial composition concept.
Comet-type prompts MUST contain a specific tender illustrative scene concept.
Both MUST be distinguishably different for the same input topic.

#### Scenario: Metaphorical-editorial method → specific metaphor invented (CACD-CG-01-a)

- GIVEN a `ClientAesthetic` carousel job with `ConceptualMethod` identifying
  Atlas's metaphorical-editorial method (idea-driven visual metaphors,
  mono-color still-life, staged photography)
- AND a slide topic of "overcoming fear of failure"
- WHEN `ImagePromptWriter` builds the image instruction
- THEN the instruction contains a specific invented visual concept that metaphorically
  represents "overcoming fear of failure" (e.g., a staged object composition or
  editorial scene, NOT a generic "representing fear" background descriptor)
- AND the instruction does NOT include text, labels, handles, or signatures to be
  embedded in the generated image

#### Scenario: Literal-illustration method → specific scene invented (CACD-CG-01-b)

- GIVEN a `ClientAesthetic` carousel job with `ConceptualMethod` identifying
  Comet's literal tender illustration method (watercolor illustrations of children)
- AND a slide topic of "children learning to share"
- WHEN `ImagePromptWriter` builds the image instruction
- THEN the instruction contains a specific tender watercolor scene literally depicting
  children sharing (NOT a generic "illustration style" background descriptor)
- AND the instruction does NOT include text, labels, handles, or signatures to be
  embedded in the generated image

#### Scenario: Same topic, different methods → distinguishably different instructions (CACD-CG-01-c)

- GIVEN both an Atlas-type client (CACD-CG-01-a) and a Comet-type client (CACD-CG-01-b)
  with the same slide topic "growth through challenge"
- WHEN `ImagePromptWriter` builds the instruction for each
- THEN the Atlas instruction contains a metaphorical editorial concept
- AND the Comet instruction contains a literal tender illustrative scene
- AND the two instructions are discernibly different from each other

#### Scenario: `ConceptualMethod` null → no ideation section added (CACD-CG-01-d)

- GIVEN `ClientAestheticProfile.ConceptualMethod = null`
- WHEN `ImagePromptWriter.BuildInput` runs
- THEN no concept ideation section appears in the instruction
- AND the output is the same as the pre-change system's output for the same inputs

---

### Requirement: CACD-CG-02 — `ImagePromptWriter` Is Ratio-Aware When `ImageTypeRatio` Is Non-Null

When `ClientAestheticProfile.ImageTypeRatio` is non-null, `ImagePromptWriter` MUST
include a background-type cue in the image instruction that is consistent with the
detected ratio for the slide's position. The cue MUST guide the model toward a
photographic/staged-photography background OR a flat/mono-color background as
appropriate for the slide type.

#### Scenario: Photo-type slide → photographic background cue included (CACD-CG-02-a)

- GIVEN `ImageTypeRatio` and a slide position that calls for a photographic background
- WHEN `ImagePromptWriter` builds the instruction
- THEN the instruction includes a cue directing the model toward a photographic or
  staged-photography background
- AND the instruction does NOT direct the model toward a flat or mono-color background
  for this position

#### Scenario: Flat-type slide → flat/mono-color background cue included (CACD-CG-02-b)

- GIVEN `ImageTypeRatio` and a slide position that calls for a flat-color background
- WHEN `ImagePromptWriter` builds the instruction
- THEN the instruction includes a cue directing the model toward a flat or mono-color
  background
- AND the instruction does NOT direct the model toward a photographic background
  for this position

#### Scenario: `ImageTypeRatio` null → no ratio cue added; existing instruction unchanged (CACD-CG-02-c)

- GIVEN `ClientAestheticProfile.ImageTypeRatio = null`
- WHEN `ImagePromptWriter` builds the instruction
- THEN no ratio-specific background-type section is added to the instruction
- AND the existing style-descriptor–driven instruction runs unchanged

---

### Requirement: CACD-CG-03 — Cold-Start Parity: Null Conceptual Fields → Instruction Identical to Pre-Change Output

When both `ConceptualMethod` and `ImageTypeRatio` are null on `ClientAestheticProfile`,
`ImagePromptWriter` MUST produce an image instruction identical to the one produced
by the pre-change system for the same inputs. No ideation section and no ratio section
MUST be added.

#### Scenario: Both fields null → legacy-identical instruction (CACD-CG-03-a)

- GIVEN `ClientAestheticProfile.ConceptualMethod = null`
- AND `ClientAestheticProfile.ImageTypeRatio = null`
- WHEN `ImagePromptWriter.BuildInput` runs
- THEN the resulting instruction is identical to the pre-change output for the
  same `ClientAestheticProfile` and slide inputs
- AND no ideation or ratio section appears in the output

---

## MODIFIED Requirements

None. `CAG-CG-06` (profile threaded into background designer and copy generators)
remains valid; CACD-CG-01 and CACD-CG-02 extend `ImagePromptWriter` behavior
when the new profile fields are non-null, additive to the existing path.
`CAC-IMG-02` (background-only contract) is reinforced, not modified: the concept-ideated
instruction MUST still be text-free.

## REMOVED Requirements

None.
