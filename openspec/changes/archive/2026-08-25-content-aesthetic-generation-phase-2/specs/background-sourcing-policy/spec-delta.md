# Spec Delta: Background Sourcing Policy — ClientAesthetic Source Precedence (Creatives-First, Palette Optional)

<!-- Change: content-aesthetic-generation-phase-2 -->
<!-- Applies to: openspec/specs/background-sourcing-policy/spec.md (additive delta) -->

## Purpose

Formalize, for `BackgroundSourcingPolicy.ClientAesthetic`, that the style source of truth is the
client's feed-derived multi-signal profile (creatives-first), the brand manual is optional, and a
weak/absent palette is a valid, non-degraded state.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAG2-BSP-01 | Proposal §Guiding Principle — feed is the manual; precedence |
| CAG2-BSP-02 | Proposal §Guiding Principle — palette optional/inconsequential |

---

## ADDED Requirements

### Requirement: CAG2-BSP-01 — ClientAesthetic Source Precedence Is Feed-Profile-First; Brand Manual Optional

For `ClientAesthetic`, the style/colour source of truth MUST be the feed-derived enriched aesthetic
profile (creatives, descriptors, motifs, voice, palette-when-present). A brand manual, if present,
MUST NOT override the feed profile for this policy; its absence MUST NOT be treated as an error or a
degrade. For `ClientDefinedSet` and `AiPalette`, the brand manual remains authoritative (unchanged).

#### Scenario: ClientAesthetic prefers feed profile over an incidental manual (CAG2-BSP-01-a)

- GIVEN a `ClientAesthetic` client that happens to also have a brand manual
- AND an enriched feed profile is available
- WHEN the style source is resolved
- THEN the feed profile drives style/colour
- AND the brand manual does not override it

#### Scenario: ClientAesthetic with no manual is valid, not degraded (CAG2-BSP-01-b)

- GIVEN a `ClientAesthetic` client with NO brand manual and an enriched feed profile
- WHEN the style source is resolved
- THEN the feed profile drives style/colour
- AND no "missing manual" degrade or warning treats this as an error condition

#### Scenario: ClientDefinedSet/AiPalette still authoritative on the manual (CAG2-BSP-01-c)

- GIVEN a `ClientDefinedSet` client with a brand manual
- WHEN the style source is resolved
- THEN the brand manual remains authoritative (pre-change behavior)

---

### Requirement: CAG2-BSP-02 — Palette Is an Optional Signal; Absent Palette Must Not Force a Fabricated One

Under `ClientAesthetic`, the colour palette is one OPTIONAL signal among several. When the enriched
profile carries no marked palette, the system MUST NOT fabricate or inject a default brand palette;
colour MAY be derived freely by the designer from the client's reference creatives. A populated
palette, when present, MUST be honoured as role-ordered guidance.

#### Scenario: Absent palette — colour from creatives, none fabricated (CAG2-BSP-02-a)

- GIVEN a `ClientAesthetic` enriched profile with an empty palette and reference creatives
- WHEN the style source is resolved for the designer
- THEN no fabricated/default brand palette is injected
- AND colour is left to derive from the reference creatives

#### Scenario: Present palette honoured as role-ordered guidance (CAG2-BSP-02-b)

- GIVEN a `ClientAesthetic` enriched profile with a populated role-ordered palette
- WHEN the style source is resolved for the designer
- THEN the palette is passed as role-ordered colour guidance

---

## MODIFIED Requirements

### Requirement Delta: BSP CASE C — From Descriptor-Read To Feed-Profile-First Source

The predecessor `ClientAesthetic` strategy treated the policy as a passive/text-descriptor read with
brand-manual palette as the colour source. This change makes the feed-derived enriched profile the
source of truth for style/colour under this policy, with the brand manual optional. Phase 1's
non-blocking refresh-if-stale orchestration is unchanged; only the source-of-truth precedence and
palette-optionality are refined here.

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- Source precedence is resolved in the Application layer (generation orchestration), keyed off
  `BackgroundSourcingPolicy`. No provider types involved.
- "Derive colour freely from creatives" is realized via the designer vision path (see
  content-generation `CAG2-CG-01`/`CAG2-CG-02`).
