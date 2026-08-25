# Spec Delta: Content Studio — Per-Slide Degrade Indicator

<!-- Change: client-aesthetic-fidelity-fixes -->
<!-- Applies to: openspec/specs/content-studio/spec.md (additive delta; capability introduced by change `content-studio`, not yet archived at time of writing) -->

## Purpose

Specify the additive Content Studio behavior this change introduces: the Studio slide
viewer surfaces, per slide, that the slide was produced by the deterministic degrade path
(`content-generation` CAFF-CG-04). The marker is read from the existing `Envelope`
`[key: string]: unknown` structure — no DB migration, no `ContentItemDto` change, no Content
Studio API endpoint change.

---

## PRD Traceability

| Requirement ID | Proposal Anchor |
|---|---|
| CAFF-CS-01 | Settled decision 3 — degradation MUST be visible in Content Studio; Scope item 3 |
| CAFF-CS-02 | Approach — "Why the envelope carries the degrade marker" (survives edit/autosave round-trip) |

---

## ADDED Requirements

### Requirement: CAFF-CS-01 — Studio Slide Viewer Surfaces a Per-Slide Degrade Indicator

The Content Studio carousel/story viewer and slide strip (`apps/web`) MUST render a
per-slide badge/indicator for any slide whose `CompositionSpecEnvelope` carries the degrade
marker written by `content-generation` CAFF-CG-04. A slide whose envelope carries no marker
MUST render with no badge, identical to pre-change behavior.

#### Scenario: Slide envelope carries the degrade marker — badge renders

- GIVEN a carousel slide whose `visual_spec_markup` envelope contains the degrade marker
- WHEN the operator opens the Content Studio carousel/story viewer
- THEN a degrade badge/indicator is shown on that slide only
- AND no other slide in the carousel shows the badge unless its own envelope also carries the marker

#### Scenario: Slide envelope has no degrade marker — no badge (regression)

- GIVEN a carousel slide whose envelope carries no degrade marker
- WHEN the operator opens the Content Studio viewer
- THEN no degrade badge is shown for that slide
- AND the viewer renders identically to its pre-change appearance

---

### Requirement: CAFF-CS-02 — Degrade Marker Survives Studio Edit/Autosave Round-Trip

The degrade marker MUST survive a Studio edit + autosave round-trip. Because the `Envelope`
TypeScript interface declares `[key: string]: unknown` and `serializeEnvelopes` re-stringifies
the whole object, the marker MUST remain present (and the badge MUST still render) after any
caption/visual edit and subsequent autosave.

#### Scenario: Marker persists after an edit and autosave cycle

- GIVEN a slide showing the degrade badge (its envelope carries the marker)
- WHEN the operator edits that slide's caption or visual field and autosave completes
- THEN the reloaded envelope still carries the degrade marker
- AND the degrade badge still renders on that slide after reload

---

## MODIFIED Requirements

None. Both requirements above are additive to the Content Studio slide viewer; no existing
Content Studio requirement (materialization, review-state transitions, autosave mechanics
themselves) is changed.

## REMOVED Requirements

None.
