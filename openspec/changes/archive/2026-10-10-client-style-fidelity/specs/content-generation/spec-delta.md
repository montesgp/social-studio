# Spec Delta: Content Generation — Client Style Fidelity

<!-- Change: client-style-fidelity -->
<!-- Applies to: openspec/specs/content-generation/spec.md -->

## ADDED Requirements

### Requirement: CSF-CG-01 — Carousel Template Is Chosen by Layout, Not by Format Key

`BuildTemplateBackedMarkup` MUST use the carousel template for every format whose catalog layout is
Carousel on a non-Story surface (`feed-carousel` and the legacy `carousel` alias). Story surfaces
keep the Story template.

#### Scenario: feed-carousel uses the carousel template (CSF-CG-01-a)

- GIVEN a `feed-carousel` slide with an enforced background
- WHEN the markup is built
- THEN the title renders in `data-slot="title"` from the carousel template
- AND the Story `headline` slot and its accent pill are not emitted

### Requirement: CSF-CG-02 — No Hardcoded Decoration for ClientAesthetic Slides

When a ClientAesthetic design context is present, the deterministic builders MUST NOT emit the
accent pill, the full-width accent bar, or an empty brand-mark chip. A divider renders only from a
measured `DecorationMotifs.DividerRule`. An icon brand mark without an extracted crop renders
nothing. Without a design context (cold start) the markup stays byte-identical to the previous
behaviour. Supersedes the unconditional accent pill of `client-aesthetic-visual-sophistication`
decision #4 and the empty-chip fallback of `CAGH-CG-06-a` for ClientAesthetic slides.

#### Scenario: client without a divider gets no bar (CSF-CG-02-a)

- GIVEN a ClientAesthetic photo slide whose profile has no divider rule
- WHEN the markup is built
- THEN no `data-slot="accent"` element and no decorative bar element is present

#### Scenario: icon mark without crop renders nothing (CSF-CG-02-b)

- GIVEN a detected icon brand mark and no extracted crop
- WHEN the brand-mark slot is built
- THEN no `data-slot="brand-mark"` element is emitted

### Requirement: CSF-CG-03 — Colour Pairings Follow Observed Frequency

When the profile's pairings carry an observed `SlideCount`, flat slides MUST take flat pairings
allocated in proportion to their slide counts (largest remainder, dominant pairing first), and photo
slides MUST take the observed photo pairing when one exists. Pairings without `SlideCount` keep the
legacy rotation.

#### Scenario: dominant pairing fills a short carousel (CSF-CG-03-a)

- GIVEN observed flat pairings with slide counts 10 and 1
- WHEN a 3-slide carousel is composed
- THEN every slide uses the dominant pairing

### Requirement: CSF-CG-04 — Rendered Type Uses the Client's Catalog Font

Typography resolved from a client profile MUST name a bundled catalog family, clamp each weight to
the weights that family ships, and render with `letter-spacing:0`. The renderer MUST embed every
catalog family referenced by the markup, so no host font substitutes it.

#### Scenario: condensed client font renders with its own face (CSF-CG-04-a)

- GIVEN a heading role resolved to `League Gothic` at observed weight 800
- WHEN the slide renders
- THEN the markup requests `League Gothic` at weight 400 (the only weight it ships)
- AND the render document contains an `@font-face` rule for `League Gothic`

### Requirement: CSF-CG-05 — Measured Headline Width

When the heading role carries `BlockWidthRatio`, the title slot width MUST be that ratio of the
canvas width, clamped to 30–90 %, with `text-wrap:balance`.

### Requirement: CSF-CG-06 — Keyword Highlight

When an observed pairing carries a highlight colour, the carousel generator MUST request a
`titleHighlight` per slide (and `coverTitleHighlight`) holding an exact substring of the title, and
the builders MUST wrap its first case-insensitive occurrence in
`<span data-highlight="true">` coloured with the pairing's highlight colour. A highlight that is not
a substring of the title is ignored. Titles persist as plain text.

#### Scenario: highlighted words render in the client's accent (CSF-CG-06-a)

- GIVEN title "¿Sabés qué toman tus clientes en tu local?" and highlight "tus clientes"
- AND an observed pairing with highlight `#A6CE3C`
- WHEN the title slot is built
- THEN "tus clientes" is wrapped in a span coloured `#A6CE3C`

### Requirement: CSF-CG-07 — Flat Slides Reflow Around the Measured Headline

The deterministic flat path MUST run the bounded measure-then-render reflow (ADR 0016) over
`title → divider → subtitle`, like the template-backed path. A client's measured headline (larger
size, condensed face, narrow block) has no fixed height, so the subtitle's literal top can land on
it. Supersedes `CLIF-CG-06` for the flat path; AI-designer markup is still never reflowed. The
reflow slot order is chosen by layout (CSF-CG-01), so `feed-carousel` reflows its carousel slots.

#### Scenario: tall measured headline pushes the subtitle down (CSF-CG-07-a)

- GIVEN a flat slide whose rendered title is taller than the gap above the subtitle
- WHEN the slide renders
- THEN the subtitle is moved below the title's measured extent before the final render

## Known Follow-up (not in scope)

`DecorationMotifs` (divider rule, composition archetype) is never threaded into
`ClientAestheticDesignContext`, so `CAVS-CG-01/02` never reach production markup. Wiring it is
deferred: the extractor still reports a `Line` divider for clients that do not use one, so enabling it
today would reintroduce stray bars.
