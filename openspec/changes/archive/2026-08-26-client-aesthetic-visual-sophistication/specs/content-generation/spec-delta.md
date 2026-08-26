# Spec Delta: Content Generation — Divider Rule, Composition Archetype Presets, Ornament Crop

<!-- Change: client-aesthetic-visual-sophistication -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta) -->

## Purpose

The deterministic flat-carousel path (`BuildFlatColorMarkup` / `BuildFlatCarouselSlots`) renders a
fixed layout skeleton — hardcoded offsets, no divider, no ornament — for every client regardless of
their measured `DecorationMotifs`. This delta wires the divider-rule primitive, the
composition-archetype geometry presets, and the gated ornament crop render into that path, while
guaranteeing byte-identical output when `DecorationMotifs` is null.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAVS-CG-01 | Scope item 4; success criterion — divider-rule render primitive |
| CAVS-CG-02 | Scope item 5; success criterion — archetype geometry presets, `"left-aligned"` identity |
| CAVS-CG-03 | Success criterion — cold-start byte-identical markup when `DecorationMotifs = null` |
| CAVS-CG-04 | Scope item 7; success criterion — ornament crop render, gated on WU-6 measurement |
| CAVS-CG-05 | Scope item 8; settled decision 6 — ornament degrade token, precedence, unconditional log |

---

## ADDED Requirements

### Requirement: CAVS-CG-01 — Divider Rule Renders From the Client's Real Accent or Text Colour

`BuildFlatCarouselSlots` MUST render a `data-slot="divider"` element when
`DecorationMotifs.DividerRule` is non-null, coloured from the client's resolved `Accent` palette
role, falling back to the slide's resolved text colour when no `Accent` role resolves. For
`LineWithDiamond` and `LineWithDot`, an additional glyph element (rotated square / circle
respectively) MUST render centred on the rule. A slide whose `DecorationMotifs` is null, or whose
`DividerRule` is null, MUST render no divider element.

#### Scenario: LineWithDiamond renders a divider using the client's accent colour (CAVS-CG-01-a)

- GIVEN a profile signalling `DecorationMotifs.DividerRule = LineWithDiamond` and a resolved
  `Accent` palette colour
- WHEN a FlatColor slide is composed
- THEN a `data-slot="divider"` rule element renders in the client's real accent colour
- AND a rotated-square glyph element renders centred on the rule

#### Scenario: No Accent role resolves — divider falls back to text colour (CAVS-CG-01-b)

- GIVEN a profile signalling `DividerRule = Line` with no resolvable `Accent` palette role
- WHEN a FlatColor slide is composed
- THEN the `data-slot="divider"` element renders in the slide's resolved text colour

#### Scenario: DividerRule is null — no divider element (CAVS-CG-01-c, regression)

- GIVEN a profile with `DecorationMotifs.DividerRule = null` (or `DecorationMotifs = null`)
- WHEN a FlatColor slide is composed
- THEN no `data-slot="divider"` element is rendered

---

### Requirement: CAVS-CG-02 — Composition Archetype Drives Geometry Presets Across Accent, Divider, Title, Subtitle, CTA

The hardcoded `left:104px` / `left:108px` / `text-align:left` literals MUST become a preset resolved
by token lookup from `DecorationMotifs.CompositionArchetype`, applied consistently across the
accent, divider, title, subtitle, and cta elements. `"left-aligned"` MUST reproduce today's literal
offsets exactly. A null, blank, or unrecognised archetype token MUST resolve to the
`"left-aligned"` preset — the sole fallback path.

#### Scenario: left-aligned reproduces today's literal geometry exactly (CAVS-CG-02-a, regression)

- GIVEN a profile with `CompositionArchetype = "left-aligned"`
- WHEN a FlatColor slide is composed
- THEN the rendered title/subtitle/cta offsets equal the pre-change hardcoded literals
  (`left:104px` / `left:108px`, `text-align:left`) exactly

#### Scenario: Two archetypes produce measurably different geometry (CAVS-CG-02-b)

- GIVEN two profiles identical except for `CompositionArchetype` (e.g. `"left-aligned"` vs.
  `"centered"`)
- WHEN each client's carousel is composed from the same input copy
- THEN the rendered slot geometry for title, subtitle, and cta measurably differs between the two,
  asserted on markup

#### Scenario: Unrecognised archetype token falls back to left-aligned geometry (CAVS-CG-02-c)

- GIVEN a profile with `CompositionArchetype` set to a deliberately-invented, unrecognised token
  (e.g. `"diagonal-split"`)
- WHEN a FlatColor slide is composed
- THEN the rendered geometry equals the `"left-aligned"` preset exactly
- AND no exception is thrown

#### Scenario: Null archetype falls back to left-aligned geometry (CAVS-CG-02-d, regression)

- GIVEN a profile with `DecorationMotifs = null` or `CompositionArchetype = null`
- WHEN a FlatColor slide is composed
- THEN the rendered geometry equals the `"left-aligned"` preset exactly

---

### Requirement: CAVS-CG-03 — DecorationMotifs Null Renders Byte-Identical Markup to Pre-Change Output

A profile with `DecorationMotifs = null` MUST render markup byte-identical to the pre-change
`BuildFlatColorMarkup` / `BuildFlatCarouselSlots` output for the same `CompositionSpec`. This is a
blocking regression guarantee: every pre-existing flat-path test MUST continue to pass unchanged,
and the accent pill's rendered markup MUST be unchanged from pre-change output in every case.

#### Scenario: Null DecorationMotifs produces byte-identical markup (CAVS-CG-03-a, regression, blocking)

- GIVEN a `CompositionSpec` and client profile identical to one used in a pre-change flat-path test,
  except `DecorationMotifs = null`
- WHEN the flat slide markup is built
- THEN the produced markup string is byte-identical to the markup produced by the pre-change
  builder for the same input
- AND no divider, archetype-preset offset change, or ornament element appears anywhere in the output

#### Scenario: Accent pill is unaffected regardless of DecorationMotifs (CAVS-CG-03-b, regression)

- GIVEN any profile, with or without `DecorationMotifs` populated
- WHEN a FlatColor slide is composed
- THEN the accent pill's rendered markup is identical to pre-change output in every case

---

### Requirement: CAVS-CG-04 — Ornament Crop Renders as a Stored Image, Gated on Measured Reliability

*(Gated on `profile-analysis` `CAVS-PA-04-c`'s bounding-box reliability measurement passing.)* When
`DecorativeOrnamentDetection` carries a valid bounding box, the renderer MUST crop the ornament via
an Application port following `IBrandMarkAssetExtractor`'s never-throws contract, persist it via
`IGeneratedAssetStorage`, and render it as a `data-slot="ornament"` `<img>` at a deterministic
anchor sized so it never overlaps the title block, honouring the archetype preset. No AI model MUST
be invoked for any flat slide as part of this rendering.

#### Scenario: Valid ornament bounding box produces a stored crop and renders an image (CAVS-CG-04-a)

- GIVEN a profile whose `DecorativeOrnamentDetection.BoundingBox` is valid and above the
  minimum-area guard
- WHEN a FlatColor slide is composed
- THEN a cropped ornament asset is stored under the analysis folder via `IGeneratedAssetStorage`
- AND a `data-slot="ornament"` `<img>` element referencing that stored asset renders on the slide
- AND no AI model is invoked as part of this render

#### Scenario: No ornament detection — no ornament element (CAVS-CG-04-b, regression/cold-start)

- GIVEN a profile with `DecorativeOrnamentDetection = null` (or `DecorationMotifs = null`)
- WHEN a FlatColor slide is composed
- THEN no `data-slot="ornament"` element is rendered

---

### Requirement: CAVS-CG-05 — Ornament Failure Degrades Silently in the Rendered Creative but Sets an Operator-Visible Token

On any ornament failure (no box, invalid box, below-minimum area, crop or store failure, or
extractor unavailable) the renderer MUST render no ornament element — never an invented ornament.
The failure MUST set `CompositionSpecEnvelope.DegradeReason = "ornament-unavailable"` when no
whole-slide degrade token already applies to that slide. When a whole-slide degrade token already
applies, that token MUST take precedence and remain the rendered badge value, but a structured
degrade log entry for the ornament failure MUST still be emitted unconditionally, regardless of
which token wins the envelope.

#### Scenario: Ornament failure with no whole-slide degrade sets the ornament token (CAVS-CG-05-a)

- GIVEN a profile signalling an ornament whose crop fails (invalid/below-minimum/unavailable box)
  on a slide carrying no whole-slide degrade trigger
- WHEN the slide is composed
- THEN no ornament element renders
- AND `CompositionSpecEnvelope.DegradeReason = "ornament-unavailable"`
- AND a degrade log entry is emitted

#### Scenario: Whole-slide degrade token wins precedence over the ornament token (CAVS-CG-05-b)

- GIVEN a slide that already triggers a whole-slide degrade token AND whose ornament crop also
  fails
- WHEN the slide is composed
- THEN `CompositionSpecEnvelope.DegradeReason` carries the whole-slide token, not
  `"ornament-unavailable"`
- AND a degrade log entry for the ornament failure is still emitted unconditionally, even though
  the envelope carries only the whole-slide token
- AND no ornament element renders

#### Scenario: Successful ornament crop leaves DegradeReason untouched (CAVS-CG-05-c, regression)

- GIVEN a profile whose ornament crop succeeds and whose slide has no whole-slide degrade trigger
- WHEN the slide is composed
- THEN `CompositionSpecEnvelope.DegradeReason` remains null/unset
- AND the `data-slot="ornament"` `<img>` renders

---

## MODIFIED Requirements

None. All requirements above are additive to the deterministic flat-carousel builder; the
cold-start guarantee (`CAVS-CG-03`) constrains the addition rather than changing any existing
requirement's stated behavior.

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- Divider, archetype-preset, and ornament rendering live in `ContentGenerationJobProcessor.cs`
  (`BuildFlatColorMarkup` L2312, `BuildFlatCarouselSlots` L2364).
- `SlideDegradeReason.OrnamentUnavailable` and the precedence rule live in
  `Shared/ClientAestheticDegradeDecision.cs`.
- The ornament crop port and SkiaSharp adapter mirror `IBrandMarkAssetExtractor` and the existing
  brand-mark crop adapter; ref resolution in `StudioVisualMarkupResolver` and `PptxExportAdapter`
  and the badge label are covered by the `content-studio` delta, not this spec.
- Applying archetypes/dividers/ornaments to the AI-photo path or non-carousel composers is
  explicitly out of scope for this change.
