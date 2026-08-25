# Spec Delta: Content Generation — Faithful ClientAesthetic Degrade, Typography Wiring, Per-Slide Transparency Render

<!-- Change: client-aesthetic-fidelity-fixes -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->

## Purpose

Specify the **additive** content-generation behavior this change introduces:

1. **Two deterministic-degrade triggers**, scoped to `ClientAesthetic` carousel slides only:
   trigger A (Photo-classified slide whose image generation failed) and trigger B (AI
   designer returned no usable background). Both route to the same deterministic
   palette-backed flat treatment already proven correct for FlatColor slides, replacing the
   unconditional `rgba(0,0,0,0.52)` scrim card **for ClientAesthetic only**. Every other
   policy keeps the existing scrim-card fallback unchanged.
2. A **degrade marker** recorded on the per-slide `CompositionSpecEnvelope` whenever either
   trigger fires.
3. **Per-slide transparency treatment** applied at render time, resolved per `specIndex`.
4. **`ClientAestheticDesignContext`** carrying client-derived typography and the per-slide
   transparency signal.
5. **`AiVisualDesigner.AllowedFonts`** sourced from client-derived font classes.

The pre-existing `mappedFlatKind == FlatColor` short-circuit is untouched and remains first
in evaluation order (settled decision 6).

---

## PRD Traceability

| Requirement ID | Proposal Anchor |
|---|---|
| CAFF-CG-01 | Settled decision 1 & 6, Scope item 1 — trigger A |
| CAFF-CG-02 | Settled decision 6, Scope item 1 — trigger B, scoped to ClientAesthetic |
| CAFF-CG-03 | Settled decision 6 — FlatColor short-circuit untouched |
| CAFF-CG-04 | Scope item 2 — degrade marker on envelope |
| CAFF-CG-05 | Scope item 5 — per-slide transparency render treatment |
| CAFF-CG-06 | Scope item 4 — design context carries typography + transparency |
| CAFF-CG-07 | Scope item 4 — AllowedFonts from client-derived classes |

---

## ADDED Requirements

### Requirement: CAFF-CG-01 — Trigger A: Failed Photo-Slide Image Generation Degrades to Deterministic Flat Treatment

For a `ClientAesthetic` carousel slide whose per-slide `BackgroundPattern` is `Photo` but
whose image generation failed for that slide (the slide never enters
`creativeMap`/`slidesWithGeneratedImage`), `ContentGenerationJobProcessor` MUST route that
slide through the deterministic palette-backed flat treatment (`BuildFlatColorMarkup` /
`CompositionDefaults.ResolvePalette`). `AiVisualDesigner.ComposeAsync` MUST NOT be called for
that slide.

#### Scenario: Photo slide with forced image-generation failure renders faithfully

- GIVEN a `ClientAesthetic` carousel slide whose `BackgroundPattern` is `Photo`
- AND that slide's image generation is forced to fail (fixture)
- WHEN the slide is composed
- THEN the slide renders through the deterministic flat treatment using the client's extracted `ColorPairings`/palette
- AND `AiVisualDesigner.ComposeAsync` is NOT invoked for that slide
- AND no dark scrim, card overlay, gradient, blob, or glow appears in the slide's markup

---

### Requirement: CAFF-CG-02 — Trigger B: AI Designer Returns No Usable Background Degrades to Deterministic Flat Treatment (ClientAesthetic Only)

For a `ClientAesthetic` carousel slide, when `AiVisualDesigner.ComposeAsync` returns no
usable background (`background is null`), the slide MUST route through the same
deterministic flat treatment used by trigger A. The `rgba(0,0,0,0.52)` scrim card MUST NOT
be used for this slide. This trigger MUST be scoped to `ClientAesthetic` carousel slides
only — every other `BackgroundSourcingPolicy` MUST keep its existing scrim-card fallback
unchanged.

#### Scenario: ClientAesthetic slide with a failed AI designer renders faithfully, no scrim card

- GIVEN a `ClientAesthetic` carousel slide
- AND the AI designer returns no usable background for that slide
- WHEN the slide is composed
- THEN the slide renders through the deterministic flat treatment
- AND no `rgba(0,0,0,0.52)` scrim card (or any dark card overlay) appears anywhere in the slide's markup

#### Scenario: Non-ClientAesthetic job still gets the legacy scrim card (regression)

- GIVEN a carousel slide whose `BackgroundSourcingPolicy` is NOT `ClientAesthetic`
- AND the AI designer returns no usable background for that slide
- WHEN the slide is composed
- THEN the slide renders the pre-existing `rgba(0,0,0,0.52)` scrim card, unchanged from before this change
- AND trigger B's flat-treatment routing is NOT applied

---

### Requirement: CAFF-CG-03 — FlatColor Short-Circuit Remains Untouched and Takes Priority Over Both New Triggers

The pre-existing `mappedFlatKind == SlideBackgroundKind.FlatColor` short-circuit MUST
continue to run first, unmodified, and MUST resolve before either trigger A (CAFF-CG-01) or
trigger B (CAFF-CG-02) is evaluated.

#### Scenario: FlatColor slide never reaches the new triggers (regression)

- GIVEN a `ClientAesthetic` carousel slide whose `BackgroundPattern` already maps to `FlatColor`
- WHEN the slide is composed
- THEN the slide takes the pre-existing `FlatColor` short-circuit path
- AND neither trigger A's failed-image-generation check nor trigger B's designer-null check is evaluated for that slide
- AND the rendered output is identical to pre-change `FlatColor` behavior

---

### Requirement: CAFF-CG-04 — Degrade Marker Recorded on Per-Slide CompositionSpecEnvelope

Whenever trigger A (CAFF-CG-01) or trigger B (CAFF-CG-02) fires for a slide, a trailing
additive-nullable degrade marker MUST be written onto that slide's `CompositionSpecEnvelope`
(mirroring the existing `Deleted` member pattern). A non-degraded slide's envelope MUST be
byte-identical to its pre-change serialized form (marker absent).

#### Scenario: Trigger A fires — envelope carries the marker

- GIVEN a slide that degrades via trigger A
- WHEN its `CompositionSpecEnvelope` is serialized
- THEN the degrade marker is present on the envelope

#### Scenario: Trigger B fires — envelope carries the marker

- GIVEN a `ClientAesthetic` slide that degrades via trigger B
- WHEN its `CompositionSpecEnvelope` is serialized
- THEN the degrade marker is present on the envelope

#### Scenario: Non-degraded slide — envelope unchanged (regression)

- GIVEN a slide that does not trigger either degrade path
- WHEN its `CompositionSpecEnvelope` is serialized
- THEN the marker is absent
- AND the serialized envelope is byte-identical to the pre-change output for the same slide

---

### Requirement: CAFF-CG-05 — Per-Slide Transparency Treatment Applied at Render Time

`BuildTemplateBackedMarkup`'s `<img>` element, and the AI path's own real-photo background,
MUST apply the per-slide photo-transparency signal (sourced from
`ClientAestheticDesignContext`, resolved per `specIndex`). When the signal is present and
true for a slide, an opacity/blend treatment consistent with the extracted qualitative
descriptor MUST be applied. When the signal is absent or false for a slide, the current
unconditional `object-fit:cover` (no alpha/blend treatment) MUST be preserved unchanged.

#### Scenario: Signal present on one slide only — treatment applied per specIndex

- GIVEN a client whose transparency signal is `true` on slide index 2 only, and absent/false on every other slide
- WHEN the carousel is rendered
- THEN slide index 2's `<img>` carries the opacity/blend treatment
- AND every other slide renders the unconditional `object-fit:cover` with no alpha/blend treatment

#### Scenario: No transparency signal anywhere — cold start preserved (regression)

- GIVEN a client whose profile carries no transparency signal on any slide
- WHEN the carousel is rendered
- THEN every slide's `<img>` renders `object-fit:cover` with no opacity/blend treatment, exactly as before this change

---

### Requirement: CAFF-CG-06 — ClientAestheticDesignContext Carries Client-Derived Typography and Per-Slide Transparency

`ClientAestheticDesignContext` MUST expose a `Typography` field (client-derived, resolved
from `profile.Typography`) and the per-slide transparency signal, both additive-nullable.
When either is absent from the source profile, the corresponding context field MUST be
absent/null and downstream behavior MUST be unchanged from before this change.

#### Scenario: Context built from a profile with both signals populated

- GIVEN a `ClientAestheticProfile` with populated `Typography` (3 roles) and a per-slide transparency array
- WHEN `ClientAestheticDesignContext` is constructed
- THEN `Typography` and the per-slide transparency signal are both present on the context

#### Scenario: Context built from a profile with neither signal — cold start (regression)

- GIVEN a `ClientAestheticProfile` with `Typography = null` and no transparency signal
- WHEN `ClientAestheticDesignContext` is constructed
- THEN `Typography` is absent/null and the transparency signal is absent
- AND designer/render behavior for that client is identical to pre-change behavior

---

### Requirement: CAFF-CG-07 — AiVisualDesigner.AllowedFonts Sourced From Client-Derived Font Classes

For a `ClientAesthetic` job whose `ClientAestheticDesignContext.Typography` is populated,
`AiVisualDesigner.BuildInputJson` MUST source `AllowedFonts` from the client-derived font
classes (resolved through `FontFamilyResolution`) instead of the org Brand Kit. When the
client has no typography signal, `AllowedFonts` MUST fall back to the current org-Brand-Kit
behavior, unchanged.

#### Scenario: Client with detected font classes — AllowedFonts reflects them

- GIVEN a `ClientAesthetic` job whose context carries client-derived typography classes
- WHEN `AiVisualDesigner.BuildInputJson` builds its input
- THEN `AllowedFonts` reflects the client-derived classes resolved through `FontFamilyResolution`

#### Scenario: Client with no typography signal — legacy brand-kit behavior (regression)

- GIVEN a `ClientAesthetic` job whose context carries no typography signal
- WHEN `AiVisualDesigner.BuildInputJson` builds its input
- THEN `AllowedFonts` is sourced from the org Brand Kit exactly as before this change

---

## MODIFIED Requirements

None. All requirements above are additive. Trigger B narrows the scrim-card fallback's
applicability to non-ClientAesthetic policies only (CAFF-CG-02); the scrim-card behavior
itself, for those policies, is unchanged and is asserted as a regression scenario rather
than restated as a MODIFIED requirement.

## REMOVED Requirements

None.
