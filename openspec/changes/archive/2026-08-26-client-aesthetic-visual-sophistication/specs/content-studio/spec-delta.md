# Spec Delta: Content Studio — Ornament Ref Resolution and Token-Aware Degrade Badge Labels

<!-- Change: client-aesthetic-visual-sophistication -->
<!-- Applies to: openspec/specs/content-studio/spec.md (additive delta + MODIFIED CAFF-CS-01) -->

## Purpose

`StudioVisualMarkupResolver` and `PptxExportAdapter` today resolve only the background and
brand-mark refs. Once ornament rendering (`content-generation` `CAVS-CG-04`) emits a
`data-slot="ornament"` `<img>` pointing at a stored crop, that ref must resolve the same way or it
reaches the browser/export unresolved. Separately, `SlideDegradeBadge` currently hardcodes the label
"Fallback design" for every `DegradeReason` token; the new `ornament-unavailable` token needs its
own label because an ornament omission is not a whole-slide fallback.

---

## PRD Traceability

| Requirement ID | Proposal Section / Anchor |
|---|---|
| CAVS-CS-01 | Scope item 9 — ornament ref resolution in Studio and PPTX export |
| CAVS-CS-02 | Settled decision 6; scope item 8 — token-aware degrade badge label map |

---

## ADDED Requirements

### Requirement: CAVS-CS-01 — StudioVisualMarkupResolver and PptxExportAdapter Resolve the Ornament Asset Ref

`StudioVisualMarkupResolver` MUST resolve a slide's ornament asset ref (the storage path emitted by
ornament rendering) through the same asset-serving port already used for `BackgroundImageRef` and
the brand-mark ref, so the Studio preview can fetch it by URL/reference. `PptxExportAdapter` MUST
resolve the same ref so an exported PPTX renders the ornament crop rather than a broken/missing
image reference. A slide with no ornament asset ref MUST resolve with no change to its existing
background/brand-mark/markup resolution.

#### Scenario: Slide with an ornament storage-path ref resolves in the Studio preview (CAVS-CS-01-a)

- GIVEN a slide whose ornament slot references a stored crop asset by storage path
- WHEN the Studio preview loads that slide
- THEN the ornament `<img>` resolves to a fetchable reference and renders
- AND no broken-image state is shown

#### Scenario: PPTX export resolves the ornament crop (CAVS-CS-01-b)

- GIVEN a slide with a stored ornament crop asset ref
- WHEN the item is exported to PPTX
- THEN the exported slide includes the resolved ornament image
- AND no unresolved storage-path string leaks into the exported file

#### Scenario: Slide with no ornament ref is unaffected (CAVS-CS-01-c, regression)

- GIVEN a slide with no ornament asset ref (no ornament detected, or ornament degraded)
- WHEN the Studio preview loads that slide, or the item is exported to PPTX
- THEN resolution behaves exactly as before this change; no ornament resolution is attempted

---

### Requirement: CAVS-CS-02 — Degrade Badge Renders a Token-Aware Label

`SlideDegradeBadge` MUST render a label distinguishing the `ornament-unavailable` token (an
element-level ornament omission) from the existing whole-slide fallback tokens, via a token→label
map, instead of hardcoding "Fallback design" for every token. Each pre-existing whole-slide
`DegradeReason` token MUST continue to render its correct pre-change "Fallback design" label
unchanged. The `ornament-unavailable` marker MUST NOT leak into a PPTX export, mirroring the
existing whole-slide marker guarantee.

#### Scenario: Ornament-unavailable token renders its own distinct label (CAVS-CS-02-a)

- GIVEN a carousel slide whose `CompositionSpecEnvelope.DegradeReason = "ornament-unavailable"`
- WHEN the operator opens the Content Studio carousel/story viewer
- THEN the degrade badge renders a label distinguishing an ornament omission from a whole-slide
  fallback design (not the generic "Fallback design" text)

#### Scenario: Pre-existing whole-slide tokens keep their correct label (CAVS-CS-02-b, regression)

- GIVEN a carousel slide whose `DegradeReason` carries one of the pre-existing whole-slide degrade
  tokens shipped by `client-aesthetic-fidelity-fixes`
- WHEN the operator opens the Content Studio carousel/story viewer
- THEN the degrade badge renders the pre-change "Fallback design" label for that slide, unchanged

#### Scenario: Ornament-unavailable marker never leaks into PPTX export (CAVS-CS-02-c)

- GIVEN a slide whose envelope carries `DegradeReason = "ornament-unavailable"`
- WHEN the item is exported to PPTX
- THEN the exported file contains no trace of the degrade marker or its label

---

## MODIFIED Requirements

### Requirement: CAFF-CS-01 — Studio Slide Viewer Surfaces a Per-Slide Degrade Indicator

The Content Studio carousel/story viewer and slide strip (`apps/web`) MUST render a
per-slide badge/indicator for any slide whose `CompositionSpecEnvelope` carries the degrade
marker written by `content-generation` CAFF-CG-04 or by the ornament degrade path
(`ornament-unavailable`, `CAVS-CS-02`). The badge's label MUST be resolved through a token→label
map (`CAVS-CS-02`) rather than a single hardcoded string, so different tokens render distinct,
correct labels. A slide whose envelope carries no marker MUST render with no badge, identical to
pre-change behavior.
(Previously: the badge rendered for any `DegradeReason` token, always with the hardcoded
"Fallback design" text, regardless of which token was present.)

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

#### Scenario: Badge label reflects the specific token present (CAVS-CS-01-d, new)

- GIVEN two slides, one carrying a whole-slide degrade token and one carrying
  `ornament-unavailable`
- WHEN the operator opens the Content Studio carousel/story viewer
- THEN each slide's badge renders the label corresponding to its own token
- AND neither badge renders the other token's label

---

## REMOVED Requirements

None.

---

## Implementation Boundary Notes (non-normative)

- `StudioVisualMarkupResolver.cs` (Application) and `PptxExportAdapter.cs` (Infrastructure) both
  read the same composition-envelope refs; both MUST be extended together, mirroring the pattern
  `CAGH-SBR-01`/`CAGH-SBR-02` established for the brand-mark ref.
- The token→label map lives in `apps/web/lib/studio/degrade-badge.ts`; the badge component is
  `apps/web/components/social/content-studio-panel.tsx` (`SlideDegradeBadge`).
- `DegradeReason` remains a single-token field; promoting it to a multi-token list is out of scope
  (see `content-generation` `CAVS-CG-05`'s precedence rule and unconditional log as the resolution
  for the collision case).
