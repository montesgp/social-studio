# Proposal: Client Style Fidelity

## Change ID
`client-style-fidelity`

## Intent

**Business problem.** A ClientAesthetic carousel must look like the client made it: same typeface,
same text colours, same line rhythm, same decoration vocabulary — only the ideas and images change.
Job `9cbc3df7-1de1-4831-89f7-285dd80dbfec` (Demo Roasters) showed the pipeline *observes* the
client well but several fixed rules overwrite those observations:

| Symptom | Verified root cause |
|---|---|
| Wide, heavy headline font | Extractor named "Anton" (style right, face wrong — the client uses a League Gothic-like face). `FontFamilyResolution` collapses every detected family to `Liberation Sans` (only bundled sans, Regular only), and `HtmlChromiumContentRenderer` injects **only the emoji font**, so Chromium draws the host fallback font with synthetic bold. |
| White bar on photo slides | `BuildTemplateBackedMarkup` tests `FormatKey == "carousel"`; the job format is `feed-carousel`, so carousels render with the **Story** template and its hardcoded accent pill. The carousel path also adds a hardcoded `accentDecor` bar. |
| White rectangle top-right | Icon brand mark without a crop falls back to an empty colour chip. |
| Slide 2: lime background, black text | `ColorPairingDerivation` treats the Accent role as a background candidate (lime → black text by contrast) and `ResolvePalette` rotates pairings by slide index. The client never uses a lime background. |
| No green keyword, long lines | The extractor recorded "one lime keyword inside a white condensed headline", but nothing applies it; headline width is a fixed 872–888 px. |

**Outcome.** Generation reproduces the client's real typeface (from a bundled OFL catalog, chosen by
visual comparison), the colour combinations they actually use (grounded in pixels, weighted by how
often they use them), their keyword highlight, and their headline line length — and renders no
decoration the client does not use.

## Owner decisions (2026-10-10)

| # | Decision |
|---|---|
| 1 | **Fonts come from a curated catalog of OFL families embedded in the repository.** No runtime download. A commercial client font maps to the closest catalog family. |
| 2 | **Nothing decorative is hardcoded for a ClientAesthetic job.** Reverses `client-aesthetic-visual-sophistication` decision #4 ("the accent pill keeps rendering unconditionally"): owner statement — *"no debemos asumir nada hardcode, todo debe ser en función al estilo, estética y branding del cliente"*. Cold-start jobs (no aesthetic profile) keep today's output. |
| 3 | **Colour pairings must be observed, never derived from role hierarchy alone.** A background colour that does not appear in the client's own pixels is not a pairing. |

## Scope

1. Bundled OFL font catalog with real weights; injected into the Chromium render and served to
   Content Studio.
2. Extractor v9: catalog-constrained typography with candidates and a sample crop; visual font
   match; observed colour combinations with slide counts; heading block width; stricter divider
   detection.
3. Pixel grounding of colour pairings.
4. Composition: carousel template chosen by layout; no hardcoded pill/bar/chip for ClientAesthetic
   jobs; pairing allocation by observed frequency; heading highlight; measured headline width;
   weight clamped to the family; neutral tracking.
5. Copy generator: optional `titleHighlight` per slide when the client highlights keywords.

## Out of Scope

- Runtime font download (Google Fonts API).
- Slide-structure changes (e.g. photo slides without text).
- Ornament crop rendering (still gated by `client-aesthetic-visual-sophistication` WU-6 verdict).

## Schema-fatigue norm

No new top-level field is added to `AestheticExtraction` or `ClientAestheticProfile`. New data lives
inside the existing nested records: `AestheticTypography` (catalog candidates, width, block width
ratio) and `ColorPairing` (slide count, slide kind). All additions are trailing-nullable.

## Rollback

Each work unit is one commit. Reverting the extractor commit leaves v8 profiles valid; reverting the
composition commit restores the previous markup builders; the font catalog is inert when unused.
