# Spec Delta: Profile Analysis — Client Style Fidelity

<!-- Change: client-style-fidelity -->
<!-- Applies to: openspec/specs/profile-analysis/spec.md -->

## ADDED Requirements

### Requirement: CSF-PA-01 — Catalog-Constrained Typography Extraction

The aesthetic extractor (`aesthetic-extractor/v9`) MUST ask the model to name each typography role
with one family from the bundled font catalog, plus up to four ranked catalog candidates, the
role's width class, the exact sample text and the location of its clearest occurrence. The heading
role also reports `blockWidthRatio`. `sizeRatio` is baseline-to-baseline distance over slide height.

### Requirement: CSF-PA-02 — Visual Font Match

When a role has a locatable sample and at least two candidates, the extractor MUST render the
sample text in each candidate face and ask the multimodal route which candidate matches the cropped
sample. The chosen candidate replaces the role's family. Any failure keeps the extractor's family
and never fails the extraction.

### Requirement: CSF-PA-03 — Observed Text Colour Combinations

The extractor MUST capture the text colour combinations the client actually uses (background kind,
background, text, optional highlight, slide count) and persist them as `ColorPairing` entries with
`SlideCount` and `Kind`.

### Requirement: CSF-PA-04 — Pixel-Grounded Pairings

A flat pairing MUST be kept only when its background colour covers at least 30 % of at least one
reference image. This applies to observed and derived pairings alike.

#### Scenario: invented accent background is dropped (CSF-PA-04-a)

- GIVEN reference images that are near-black flat slides and photos, never lime backgrounds
- AND a derived pairing with background `#A6CE3C`
- WHEN pairings are grounded
- THEN the lime-background pairing is removed

### Requirement: CSF-PA-05 — Divider Rule Requires Recurrence

`decorationMotifs.dividerRule` MUST be reported only when the rule appears on at least a third of
the client's text slides.
