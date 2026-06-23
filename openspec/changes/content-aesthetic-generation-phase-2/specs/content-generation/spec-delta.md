# Spec Delta: Content Generation — Creatives-First ClientAesthetic Design, No-Grey, Caption Voice

<!-- Change: content-aesthetic-generation-phase-2 -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->

## Purpose

Make `ClientAesthetic` generation actually emulate the client's visual language by feeding the
designer the client's REAL creatives, leading with images (not palette), and removing the
grey-default when no brand manual exists.

1. **Creatives-first designer input** — for `ClientAesthetic`, the `AiVisualDesigner` is fed the
   client's real reference images/backgrounds as the primary signal, complemented by style
   descriptors, motifs and palette-when-marked.
2. **No-grey** — a missing brand manual MUST NOT yield generic greys; colour derives from the
   client's creatives, and no palette is forced when the client has none.
3. **Variety within coherence** — output varies across a package while staying recognizably on
   the client's style.
4. **Caption voice** — caption-derived voice/style reaches copy generators, additive to
   `ConcisionConstraint` and to the Phase 1 `VoiceTone` hint.

Rendering stays HTML/CSS → raster. No image-generation model. The non-blocking refresh-if-stale
contract from Phase 1 (CAG-CG-01..04) is preserved unchanged.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAG2-CG-01 | Proposal §Approach 5; §Guiding Principle — creatives-first multi-signal |
| CAG2-CG-02 | Proposal §Approach 6; §Goals — no-grey, no forced palette |
| CAG2-CG-03 | Proposal §Fidelity Target — variety within coherence |
| CAG2-CG-04 | Proposal §Risks — bounded reference images (cost) |
| CAG2-CG-05 | Proposal §Approach 7 — caption voice into copy generators |
| CAG2-CG-06 | Proposal §Dependencies — multimodal route required; degrade otherwise |

---

## ADDED Requirements

### Requirement: CAG2-CG-01 — ClientAesthetic Designer Input Is Creatives-First and Multi-Signal

For `ClientAesthetic` jobs where an enriched aesthetic profile is available, the designer input
MUST be built led by the client's **real reference creatives** (durable post images/backgrounds),
passed as vision input, complemented by the profile's style/composition descriptors, motifs, and
palette (only when present). No single signal is mandatory; palette MAY be empty. The designer
prompt MUST instruct reproduction of the client's visual language.

#### Scenario: Reference creatives passed to the designer as the primary signal (CAG2-CG-01-a)

- GIVEN a `ClientAesthetic` job with an enriched profile carrying durable reference images and
  style descriptors
- WHEN the designer input is built
- THEN the reference images are included as vision input
- AND the style/composition descriptors are included in the prompt
- AND the prompt directs the model to reproduce the client's visual language

#### Scenario: Empty palette does not block creatives-first design (CAG2-CG-01-b)

- GIVEN an enriched profile with reference images but an EMPTY palette
- WHEN the designer input is built
- THEN the reference images and descriptors still drive the design
- AND no error occurs due to the absent palette
- AND no invented/default brand palette is injected

#### Scenario: No enriched profile — falls back to Phase 1 path (CAG2-CG-01-c)

- GIVEN a `ClientAesthetic` job with only the Phase 1 text-descriptor profile (no enriched payload)
- WHEN the designer input is built
- THEN the Phase 1 descriptor-based behavior is used
- AND generation completes (no failure for missing enriched data)

---

### Requirement: CAG2-CG-02 — No Brand Manual Never Yields Greys; No Palette Is Forced

For `ClientAesthetic`, the absence of a brand manual MUST NOT cause the deterministic grey/neutral
fallback. Colour MUST derive from the client's creatives/enriched profile. When the client has no
marked palette, the designer MAY derive colour freely from the reference creatives; the system MUST
NOT inject a fabricated brand palette to "fill the gap."

#### Scenario: ClientAesthetic with no manual uses feed-derived colour, not grey (CAG2-CG-02-a)

- GIVEN a `ClientAesthetic` client with NO brand manual and an enriched profile with reference creatives
- WHEN content is generated
- THEN the composition's colour derives from the creatives/profile
- AND the generic grey/neutral default path is NOT taken

#### Scenario: Non-ClientAesthetic with no manual keeps existing behavior (CAG2-CG-02-b)

- GIVEN a `Creative`/`AiPalette` client with no brand manual
- WHEN content is generated
- THEN the existing (pre-change) behavior for that policy is unchanged

---

### Requirement: CAG2-CG-03 — Variety Within Coherence Across a Package

For a `ClientAesthetic` package producing multiple assets/slides, the designer MUST be directed to
vary compositions while staying on the client's style, rather than repeating one near-identical
layout. A per-job variation seed (or equivalent) MUST be threaded so variety is reproducible.

#### Scenario: Multi-asset package varies layouts on-style (CAG2-CG-03-a)

- GIVEN a `ClientAesthetic` carousel job of 5 slides with an enriched profile
- WHEN the slides are designed
- THEN the designer prompt carries a variety directive plus a per-job variation seed
- AND the slides are not constrained to one identical template

---

### Requirement: CAG2-CG-04 — Reference Images Are Bounded Per Job (Cost Guard)

The number of reference images passed to the designer MUST be bounded by configuration and passed
at a job granularity appropriate to cost (e.g., a curated set once per job rather than re-sending
the full set per slide). Exceeding the configured cap MUST truncate to the cap, not error.

#### Scenario: Reference set truncated to the configured cap (CAG2-CG-04-a)

- GIVEN an enriched profile with more durable images than the configured reference cap
- WHEN the designer input is built
- THEN only up to the cap is included
- AND no error is raised by the excess

---

### Requirement: CAG2-CG-05 — Caption-Derived Voice Threaded into Copy Generators (Additive)

For `ClientAesthetic`, caption-derived voice/style from the enriched profile MUST be threaded into
copy generators as advisory guidance, ADDITIVE to the existing `ConcisionConstraint` and to the
Phase 1 `VoiceTone` hint. It MUST NOT replace concision and MUST NOT hard-reject AI output.

#### Scenario: Caption voice present in copy prompt alongside concision (CAG2-CG-05-a)

- GIVEN a `ClientAesthetic` job with caption-derived voice in the enriched profile
- WHEN a copy generator assembles its prompt
- THEN the prompt carries the caption-voice advisory section
- AND it also carries the existing concision directive
- AND neither suppresses the other

#### Scenario: No caption voice — no section, no error (CAG2-CG-05-b)

- GIVEN an enriched profile with no caption-derived voice
- WHEN a copy generator assembles its prompt
- THEN no caption-voice section is added
- AND generation completes normally

---

### Requirement: CAG2-CG-06 — Multimodal Generation Route Required for Reference-Image Design; Degrade Otherwise

Passing reference images to the designer requires a multimodal generation route. When the resolved
`generate-content` route is NOT multimodal, the designer MUST degrade to a non-vision path (style
descriptors + palette-when-present) rather than failing, and log that vision input was skipped.

#### Scenario: Non-multimodal generation route degrades to descriptor-only design (CAG2-CG-06-a)

- GIVEN a `ClientAesthetic` job and a non-multimodal `generate-content` route
- WHEN the designer input is built
- THEN reference images are NOT sent
- AND style descriptors (and palette when present) drive the design
- AND a notice is logged that vision input was skipped
- AND generation completes

---

## MODIFIED Requirements

### Requirement Delta: CAG-CG-06 (Phase 1) — Designer Now Receives Real Creatives, Not Only Descriptors

Phase 1 `CAG-CG-06` threaded `BackgroundDescriptors` (text) to the background designer. This change
EXTENDS that path: when an enriched profile is present, the designer ALSO receives the durable
reference creatives and richer descriptors per `CAG2-CG-01`. The Phase 1 descriptor threading
remains valid as the fallback when no enriched profile exists. Phase 1 behavior is NOT superseded.

### Requirement Delta: BSP CASE C Degrade — No-Grey Refinement

The Phase 1 "degrade to Creative/AI-palette when no analysis" path remains, but for
`ClientAesthetic` with an enriched profile present, the grey-default backstop MUST NOT be used for
the colour decision (see `CAG2-CG-02`). The deterministic fallback composition still applies only
when the AI designer fails entirely (resilience backstop), and even then derives colour from the
profile/creatives when available rather than fixed greys.

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- `AiVisualDesigner` (Infrastructure) gains a vision path; reference image URLs/bytes are resolved
  from durable storage refs on the enriched profile (Application shape) — no EF/Apify types inward.
- The no-grey change touches the designer input build and the resilience backstop in
  `ContentGenerationJobProcessor` (`BuildTemplateBackedMarkup` colour source).
- Reference-image cap and granularity are configuration (OQ-4); design decides defaults.
- Prompt-version bumps for modified copy generators and the visual designer (`visual-designer/v1` →
  next) where prompts change.
- Architecture gate: no provider types in Domain/Application; vision payload flows via the existing
  multimodal `IAiCompletionGateway` path.
