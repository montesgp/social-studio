# Content Studio Spec

## Purpose

Define the observable requirements for the Content Studio capability, including the operator review loop, slide viewer, and per-slide metadata surface.

## Capabilities

- Review and edit generated content (carousels, stories, reels) in a visual slide viewer.
- Display per-slide metadata, including degrade indicators when deterministic fallbacks were applied.
- Render slide-strip thumbnail navigation with visual indicators.
- Support edit-and-autosave workflow with metadata preservation.

## Constraints

- Unknown keys in composition envelopes MUST be preserved through edit/autosave round-trips.
- Operator-visible badges MUST accurately reflect the slide composition state.

## Acceptance Criteria

- Operator can identify slides that used deterministic degrade fallbacks via per-slide badge.
- Badge styling is informational (not error-state).
- Envelope metadata survives JSON serialization/deserialization.
- Autosave round-trip preserves degrade markers.

---

## Per-Slide Degrade Indicator — Change: client-aesthetic-fidelity-fixes

> Added by change `client-aesthetic-fidelity-fixes`. This section is additive; no existing Content Studio requirement is modified.

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

*Added by change `client-aesthetic-fidelity-fixes` — applied 2026-08-24.*

---

## Not Implemented: Ornament Ref Resolution and Token-Aware Degrade Badge — client-aesthetic-visual-sophistication

`client-aesthetic-visual-sophistication` proposed two `content-studio` changes: `CAVS-CS-01`
(`StudioVisualMarkupResolver`/`PptxExportAdapter` resolve an ornament asset ref) and `CAVS-CS-02`
(a token-aware `getDegradeLabel` map giving `"ornament-unavailable"` its own badge label, modifying
`CAFF-CS-01` above). Both were gated on `profile-analysis` `CAVS-PA-04-c`'s ornament bounding-box
reliability measurement, which did **not pass** during `sdd-apply` (see
`openspec/changes/client-aesthetic-visual-sophistication/wu6-reliability-gate.md`). Because no
ornament element or `"ornament-unavailable"` degrade token is ever produced by `content-generation`
in this delivery, neither `CAVS-CS-01` nor `CAVS-CS-02` was implemented, and `CAFF-CS-01` above is
**unmodified** — the badge still hardcodes the "Fallback design" label for every token, exactly as
before this change. No requirement text is merged into this spec for `CAVS-CS-01`/`CAVS-CS-02`; the
full text remains in
`openspec/changes/client-aesthetic-visual-sophistication/specs/content-studio/spec-delta.md` for
reference if/when the reliability gate is re-attempted and passes.
