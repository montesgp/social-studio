# Spec Delta: Image Generation — Topical-Fidelity Instruction on the Cold-Start Prompt Branch

<!-- Change: content-layout-and-image-fidelity -->
<!-- Applies to: openspec/specs/image-generation/spec.md (modifies CACD-IMG-02) -->
<!-- Track A only. D2 keeps text/logo detection out of scope; CAC-IMG-02/CAC-IMG-03 unchanged. -->

## Purpose

`ImagePromptWriter.BuildInput`'s cold-start branch (fires whenever `ConceptualMethod` is
null/`Unknown` — the common case) instructs the writer-model on style and mood only;
`SlideCopySummary` is passed as an unread data field. This delta amends `CACD-IMG-02` so the
cold-start branch also instructs the model to depict the slide's actual subject matter, mirroring
the wording the method-gated branch (`CACD-IMG-01`) already carries. Aesthetic guidance is
retained, not replaced. No guardrail or text-detection contract changes (D2).

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CACD-IMG-02 (modified) | Scope item 1; Success Criteria — topical instruction present, aesthetic guidance retained |
| CLIF-IMG-01 | Success Criteria — method-gated branch regression; Approach "Reused unchanged" |

---

## MODIFIED Requirements

### Requirement: CACD-IMG-02 — Cold-Start: Null `ConceptualMethod` → Descriptor Prompt Now Instructed to Depict the Slide's Topic; Full Pipeline Otherwise Unchanged

When `ConceptualMethod` is null on `ClientAestheticProfile`, the image generation prompt MUST
remain the descriptor-based prompt (style descriptors, motifs, background descriptors, voice
tone, palette) produced before this change, AND MUST additionally include an explicit
topical-fidelity instruction directing the writer-model to depict the slide's subject matter,
referencing `SlideCopySummary`. The topical instruction MUST be additive: it MUST NOT displace,
shorten, or reduce the priority of the existing aesthetic guidance. No concept-ideation section
and no ratio cue are added (unchanged from before). The remaining image-generation pipeline
(IMG-01 through IMG-08, CAC-IMG-01 through CAC-IMG-03) MUST behave identically to its pre-change
state for a cold-start client.

(Previously: the cold-start branch carried only style/mood guidance; `SlideCopySummary` was an
unread data field never referenced by instruction text.)

#### Scenario: Cold-start prompt now instructs the model to depict the slide's topic (CACD-IMG-02-a)

- GIVEN a `ClientAesthetic` client with `ConceptualMethod = null` and a slide whose
  `SlideCopySummary` is about snoring ("ronquidos")
- WHEN the image generation prompt is assembled for that slide
- THEN the assembled prompt contains an explicit instruction to depict the slide's subject
  matter, referencing `SlideCopySummary`
- AND the prompt is no longer style/mood-only

#### Scenario: Aesthetic guidance is retained, not displaced (CACD-IMG-02-b)

- GIVEN the same cold-start prompt from CACD-IMG-02-a
- WHEN the assembled prompt is inspected
- THEN the client's style descriptors, motifs, background descriptors, voice tone, and palette
  guidance are all still present
- AND no ratio cue and no concept-ideation section are added

#### Scenario: Remaining pipeline behaves identically to pre-change (CACD-IMG-02-c, regression)

- GIVEN a cold-start client with `ImageTypeRatio = null`
- WHEN the image generation pipeline runs for a creative slide
- THEN guardrail retry, CSS fallback, storage upload, reference-image conditioning, and the
  background-only contract execute identically to pre-change behavior
- AND the job completes normally

---

## ADDED Requirements

### Requirement: CLIF-IMG-01 — Method-Gated Branch Regression: Already-Topical Prompt Unaffected

The method-gated branch (`CACD-IMG-01`, fires when `ConceptualMethod` is non-null and not
`Unknown`) already instructs the model to depict the slide message. This branch MUST NOT be
modified by this change; its digested, concept-decided, ratio-aware, text-free prompt contract
MUST remain exactly as specified by `CACD-IMG-01`.

#### Scenario: Method-gated branch produces the same prompt shape as before (CLIF-IMG-01-a, regression)

- GIVEN a `ClientAesthetic` client with a non-null, non-`Unknown` `ConceptualMethod`
- WHEN the image generation prompt is assembled for a creative slide
- THEN the prompt is the pre-existing concept-decided, ratio-aware, text-free instruction
  described by `CACD-IMG-01`
- AND no change introduced by this delta alters that branch's wording or structure
