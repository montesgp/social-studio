# Spec Delta: Content Generation — Typography Sizes, Photo-Path Text Colour, Brand-Mark Text/Icon Rendering

<!-- Change: client-aesthetic-generation-hardening -->
<!-- Applies to: openspec/specs/content-generation/spec.md (additive delta + MODIFIED FFS-CG-02) -->

## Purpose

Wire three already-extracted client-aesthetic signals into rendering that currently ignores them:
measured typography sizes (heading/body/caption), `ColorPairings` text colour on the enforced-photo
path, and the brand mark's real content (text or cropped icon) instead of an empty coloured chip.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAGH-CG-01 | Scope item 1 — typography size consumption in the 3 markup builders |
| CAGH-CG-02 | Scope item 2 — `AiVisualDesigner` prompt honesty (size-derived, not fixed) |
| CAGH-CG-03 | Scope item 3 — text colour on the enforced-photo path from `ColorPairings` |
| CAGH-CG-04 | Scope item 8 — text-mark rendering via resolved caption typography |
| CAGH-CG-05 | Scope item 10 — icon-mark `<img>` rendering |
| CAGH-CG-06 | Scope item 11 — degrade path for icon marks (missing/invalid bounding box) |
| CAGH-CG-07 | Success Criteria — unrecognised `MarkType` renders exactly as today |

---

## ADDED Requirements

### Requirement: CAGH-CG-01 — Markup Builders Consume Resolved Typography Sizes, Hardcoded Literals Become Fallback

`BuildStoryTemplateSlots`, `BuildCarouselTemplateSlots`, and `BuildFlatCarouselSlots` MUST read
`spec.Typography.HeadingSize` / `.BodySize` (and `.CaptionSize` for the signature role) for their
headline/subtitle/body/signature font sizes. The pre-existing hardcoded pixel literals (`88px`,
`74px`, `38px`, `36px`, `30px`, `25px`) MUST remain reachable only as the fallback when the resolver
yields no value for that role.

#### Scenario: Client with a measured SizeRatio renders at that size (CAGH-CG-01-a)

- GIVEN a `ClientAesthetic` client whose `Typography.HeadingSize` and `.BodySize` are resolved from
  a measured `SizeRatio`
- WHEN a story or carousel slide is composed
- THEN the rendered headline/subtitle font-size matches `HeadingSize`, and body matches `BodySize`
- AND neither hardcoded literal (`88px`/`74px`/`38px`/`36px`/`30px`) appears as the effective value

#### Scenario: Client with no typography data — pre-existing hardcoded fallback unchanged (CAGH-CG-01-b, regression/cold-start)

- GIVEN a client whose `Typography` is null or whose `HeadingSize`/`.BodySize` did not resolve
- WHEN a story or carousel slide is composed
- THEN the rendered font sizes equal the pre-change hardcoded literals exactly
- AND no exception is thrown

#### Scenario: Two clients with materially different SizeRatio render different heading sizes (CAGH-CG-01-c)

- GIVEN two client profiles with materially different `SizeRatio` values, both resolving `HeadingSize`
- WHEN each client's carousel is composed
- THEN the two rendered headline font-sizes differ, asserted on markup

---

### Requirement: CAGH-CG-02 — Designer System Prompt's Size Claim Is Derived From the Client's Resolved Sizes

`AiVisualDesigner.BuildSystemPrompt` MUST NOT assert a fixed headline-size percentage range
independent of the client. When the client's typography sizes are resolved, the prompt's size
guidance line MUST be derived from those resolved values. When no typography is resolved, the
prompt MUST fall back to the pre-existing fixed guidance line, unchanged.

#### Scenario: Resolved typography drives the prompt's size line (CAGH-CG-02-a)

- GIVEN a client with resolved `HeadingSize`/`.BodySize`
- WHEN `BuildSystemPrompt` is constructed for that client's job
- THEN the size-guidance line reflects the client's resolved sizes, not the fixed percentage range

#### Scenario: No resolved typography — fixed guidance line preserved (CAGH-CG-02-b, regression)

- GIVEN a client with no resolved typography
- WHEN `BuildSystemPrompt` is constructed
- THEN the prompt contains the pre-existing fixed percentage-range guidance line, unchanged

---

### Requirement: CAGH-CG-03 — Enforced-Photo Text Colour Resolves From ColorPairings, White+Shadow Is the Fallback

Story/carousel template slots on the enforced-photo path MUST resolve text colour from
`aestheticProfile.ColorPairings` using the same resolution pattern `BuildFlatCarouselSlots`'s
`textColor` parameter already proves. When no pairing resolves, the pre-existing
`color:#FFFFFF` + `text-shadow` treatment MUST be applied unchanged. Whichever colour is applied,
the resulting text-over-background contrast MUST meet a minimum legibility guard; a resolved
pairing colour that would produce insufficient contrast against the slide's photo background MUST
NOT be applied without the compensating shadow/scrim treatment that preserves legibility.

#### Scenario: Photo-background slide uses the client's real text colour (CAGH-CG-03-a)

- GIVEN a `ClientAesthetic` slide on the enforced-photo path whose `ColorPairings` resolves a
  pairing with `TextHex = "#E0E0E0"`
- WHEN the slide's template slots are built
- THEN the rendered text colour is `#E0E0E0`
- AND `color:#FFFFFF` + `text-shadow` is NOT applied

#### Scenario: No resolvable pairing — white+shadow fallback unchanged (CAGH-CG-03-b, regression)

- GIVEN a slide on the enforced-photo path whose `ColorPairings` is null or does not resolve
- WHEN the slide's template slots are built
- THEN the rendered text is `color:#FFFFFF` with `text-shadow`, exactly as before this change

#### Scenario: Legibility guard preserved when a resolved pairing colour is low-contrast (CAGH-CG-03-c)

- GIVEN a resolved pairing whose `TextHex` would be low-contrast against a photo background
- WHEN the slide's template slots are built
- THEN a shadow/scrim compensation is retained so the rendered text remains legible
- AND the pairing colour is still the one rendered (not silently replaced by white)

---

### Requirement: CAGH-CG-04 — Text-Mark Rendering Uses the Client's Resolved Caption Typography

When `BrandMarkDetection.MarkType` is `"text"`, `BuildBrandMarkSlot` MUST render the extracted
`Text` literal through the client's already-resolved caption typography (family/weight/size),
in the previously-empty brand-mark slot. This extends `FFS-CG-02`'s brand-mark slot without
changing its editable-content-layer contract.

#### Scenario: Text mark renders the captured literal text (CAGH-CG-04-a)

- GIVEN a client whose `BrandMarkDetection.MarkType = "text"` and `.Text = "Studio Marisol"`
- WHEN the brand-mark slot is rendered
- THEN the slot renders the literal text `"Studio Marisol"`
- AND the text uses the client's resolved caption typography (family/weight/size)

#### Scenario: MarkType is text but Text is null — no fabricated text (CAGH-CG-04-b)

- GIVEN `MarkType = "text"` and `Text = null`
- WHEN the brand-mark slot is rendered
- THEN no text-mark content is rendered and no exception is thrown

---

### Requirement: CAGH-CG-05 — Icon-Mark Rendering Emits a Stored Crop `<img>`

When `BrandMarkDetection.MarkType` is `"icon"` and a usable bounding box produced a stored crop
asset, `BuildBrandMarkSlot` MUST emit an `<img>` referencing the stored crop asset in the
brand-mark slot, replacing the empty coloured `<div>`.

#### Scenario: Icon mark with a usable bounding box renders as an image (CAGH-CG-05-a)

- GIVEN a client whose `MarkType = "icon"` and whose bounding box produced a stored crop asset
- WHEN the brand-mark slot is rendered
- THEN an `<img>` referencing the stored crop asset appears in the brand-mark slot
- AND no empty coloured `<div>` is rendered for that slide

---

### Requirement: CAGH-CG-06 — Icon-Mark Degrade to PositionHint+ColorHex Chip Is Explicit and Non-Throwing

When `MarkType` is `"icon"` but the bounding box is missing, invalid, implausibly small, or the crop
could not be produced or stored, `BuildBrandMarkSlot` MUST fall back to the pre-existing
`PositionHint` + `ColorHex` coloured-chip treatment. This degrade MUST NOT throw and MUST be logged.

#### Scenario: Missing/unusable bounding box degrades to the coloured chip (CAGH-CG-06-a)

- GIVEN `MarkType = "icon"` and the bounding box is missing, invalid, or below the minimum-area
  guard (or the crop failed to store)
- WHEN the brand-mark slot is rendered
- THEN the slot renders the `PositionHint` + `ColorHex` coloured chip, unchanged from pre-change
  icon-mark handling
- AND no exception is thrown
- AND a degrade log entry is emitted

---

### Requirement: CAGH-CG-07 — Unrecognised MarkType Renders Exactly As Today

An unrecognised `MarkType` value (mapped to `"unknown"` at parse time) MUST render identically to
pre-change `BuildBrandMarkSlot` behaviour — the existing `PositionHint` + `ColorHex` chip when
`BrandMark.Present` is true, or no slot when absent.

#### Scenario: Unknown MarkType renders the pre-existing chip treatment (CAGH-CG-07-a)

- GIVEN `BrandMarkDetection.MarkType = "unknown"` with `Present = true`
- WHEN the brand-mark slot is rendered
- THEN it renders the pre-existing `PositionHint` + `ColorHex` chip, with no text or `<img>` content
- AND no exception is thrown

---

## MODIFIED Requirements

### Requirement: FFS-CG-02 — Accent and Brand-Mark Elements Are Editable Content-Layer Slots, Never Baked

Any accent element or brand-mark element on a rendered slide MUST be emitted as an editable
content-layer element (`data-slot="accent"` / `data-slot="brand-mark"`) and MUST NOT be rasterized
into the background image. The brand-mark injection is identical whichever markup path produced the
slide (the FlatColor short-circuit or the AI/photo designer path) — the AI designer MUST NEVER draw,
trace, or place a brand mark itself. When `MarkType` is `"text"`, the slot renders the extracted
`Text` through the client's resolved caption typography (`CAGH-CG-04`). When `MarkType` is `"icon"`
with a usable bounding box, the slot renders a stored crop `<img>` (`CAGH-CG-05`); otherwise it
degrades to the `PositionHint` + `ColorHex` coloured chip (`CAGH-CG-06`), the same chip this
requirement originally specified as the only treatment.
(Previously: the slot always rendered the `PositionHint` + `ColorHex` coloured chip regardless of
mark content or type — no text or icon content was ever rendered.)

#### Scenario: Brand mark rendered as editable slot (FFS-CG-02-a)

- GIVEN `BrandMark.Present = true` with a position hint and the pairing's text color
- WHEN the slide renders (flat or AI/photo path)
- THEN a `data-slot="brand-mark"` element appears at the detected position in the text color, conservative size
- AND it is not part of the rasterized background

#### Scenario: No brand mark detected — no slot rendered (FFS-CG-02-b)

- GIVEN `BrandMark` is null or `BrandMark.Present = false`
- WHEN the slide renders
- THEN no brand-mark element is emitted and the slide is otherwise unaffected

#### Scenario: Ambiguous/unknown mark fields treated as absent (FFS-CG-02-c)

- GIVEN extraction produced an unknown/ambiguous position or color
- WHEN the persisted `BrandMark` is evaluated at render
- THEN it is treated as not present; no mark is rendered

#### Scenario: Text mark renders real text, icon mark renders a crop image (FFS-CG-02-d, new)

- GIVEN `MarkType = "text"` on one client and `MarkType = "icon"` with a usable bounding box on
  another
- WHEN each client's brand-mark slot renders
- THEN the text-mark client's slot renders its literal `Text`
- AND the icon-mark client's slot renders an `<img>` of the stored crop
- AND neither renders the coloured-chip fallback

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- Typography, colour, and brand-mark rendering changes live in
  `ContentGenerationJobProcessor.cs` (`BuildStoryTemplateSlots`, `BuildCarouselTemplateSlots`,
  `BuildFlatCarouselSlots`, `BuildBrandMarkSlot`).
- `AiVisualDesigner.BuildSystemPrompt` change is Infrastructure-side prompt text only; no contract
  change to `IAiVisualDesigner`.
- The icon-mark crop asset itself (extraction, storage, `IBrandMarkAssetExtractor`) is specified in
  the `profile-analysis` delta; this delta covers only the render-time consumption of the result.
