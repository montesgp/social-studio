# Design: Client Style Fidelity

## D1 — Bundled OFL font catalog

- 33 OFL families from `google/fonts` (`ofl/` only), embedded under
  `Incoders.Hive.Infrastructure/Rendering/Fonts/Assets/Catalog/<dir>/` with each family's `OFL.txt`.
  Variable fonts keep their full `wght` axis; static families keep only the weights listed in the
  catalog. Italics are not bundled (Chromium synthesises oblique).
- `FontCatalog.Bundled` (Application) is the metadata source of truth: family name,
  classification (`Display`, `Sans`, `Serif`, `Mono`, `Script`), width (`Condensed`, `Normal`,
  `Wide`), and available weight ranges. `SystemSafeList` becomes the Liberation/DejaVu set plus every
  catalog family, so the picker, the guardrail allow-list and the renderer keep one contract (F2).
- `FontCatalog.ClampWeight(family, weight)` returns the nearest weight the family really ships.
  Markup never asks for a weight the face does not have, so Chromium never synthesises bold.
- `IFontProvider.ResolveFaces(family)` returns every embedded face with its weight range. A default
  interface implementation keeps existing fakes compiling.

## D2 — Font injection into Chromium

`HtmlChromiumContentRenderer` keeps the cached emoji block and adds, per render, `@font-face`
rules for every **catalog** family referenced by a `font-family` declaration in the markup
(`font-weight: <min> <max>` per face). Per-family CSS is cached. Liberation/DejaVu stay
non-injected, so cold-start output is unchanged.

## D3 — Font resolution

`FontFamilyResolution.Resolve(detected)`:
1. Exact (case-insensitive) catalog/system-safe match → exact.
2. Any token of the detected string split on `/ , ( ) |` or ` or ` that matches exactly → exact
   (e.g. `"Anton / heavy condensed grotesque"` → `Anton`).
3. Longest catalog name contained in the detected string (`"Poppins SemiBold"` → `Poppins`).
4. Descriptor fallback to a catalog family by classification and width (typewriter → Courier Prime,
   mono → Space Mono, script → Dancing Script, handwritten → Caveat, serif display → Playfair
   Display, serif → Lora, condensed → Oswald, heavy condensed → Anton, geometric → Poppins,
   rounded → Nunito, other sans → Inter).
5. Empty input keeps the legacy `Liberation Sans` default (cold start).

## D4 — Extractor v9 (`aesthetic-extractor/v9`)

Additions to the existing prompt and DTO, all optional:
- **Typography per role:** `family` must be one catalog name (the list is in the prompt);
  `candidates` (≤ 4 catalog names, most similar first); `width`; `sampleText` (exact visible
  words); `sourceImageIndex` + `boundingBox` of the clearest occurrence; heading `blockWidthRatio`
  (headline block width / slide width). `sizeRatio` is defined as baseline-to-baseline distance
  divided by slide height.
- **`textColorCombinations`:** `{ backgroundKind: "flat"|"photo", backgroundHex, textHex,
  highlightHex|null, slideCount }`, only for combinations the client actually uses.
- **Divider rule:** reported only when it appears on at least a third of the text slides.
- Bumping the version marks every v8 profile stale, so the next generation re-extracts.

## D5 — Visual font match

`FontCandidateVisualMatcher` (Infrastructure) runs after mapping when a role has a sample crop and
≥ 2 candidates. It crops the sample with SkiaSharp, renders one candidate sheet through the existing
Chromium renderer (each candidate draws `sampleText` in its own face, labelled 1..N), and asks the
same multimodal route which candidate matches the crop. Any failure keeps the extractor's family.
One extra model call per extraction, at most.

## D6 — Pixel-grounded colour pairings

- Observed combinations map to `ColorPairing` with the new trailing `SlideCount` and `Kind`.
  Without observed combinations the legacy `ColorPairingDerivation` result is used.
- `ColorEvidenceSampler` (Infrastructure, SkiaSharp) downsamples each reference image and
  measures pixel coverage per colour. A flat pairing survives only when its background covers
  ≥ 30 % of at least one reference image (RGB distance ≤ 48). This drops invented backgrounds such as
  black-on-lime for a client that never uses lime backgrounds.
- `ColorPairingEvidence.Filter` (Application, pure) applies the coverage result.

## D7 — Pairing selection

`CompositionDefaults.ResolvePalette(brand, profile, slideIndex, totalSlides, kind)`:
- Observed pairings (`SlideCount` present): flat slides take flat pairings allocated
  proportionally to `SlideCount` (largest remainder, dominant first); photo slides take the photo
  pairing when one exists, else the dominant flat pairing's text colours.
- Legacy pairings (no `SlideCount`): unchanged rotation.

## D8 — Composition without hardcoded decoration

- Template choice by layout: `FormatCatalog` layout Carousel + non-Story surface → carousel
  template (fixes `feed-carousel` rendering with the Story template).
- With an aesthetic context: no accent pill, no `accentDecor` bar, no empty brand-mark chip
  (an icon mark without a crop renders nothing). The measured divider rule is the only rule drawn.
  Cold start keeps today's markup byte-identically.
- Measured typography: weight clamped to the family, `letter-spacing:0`, heading width from
  `blockWidthRatio` (clamped to 30–90 % of canvas width) with `text-wrap:balance`.

## D9 — Keyword highlight

- `CarouselSlide.TitleHighlight` and `CarouselContent.CoverTitleHighlight` (optional) hold the exact
  substring to emphasise. The generator is asked for them only when an observed pairing carries a
  `highlightHex`.
- `VisualCompositionSpec.TextHighlights` (slot → substring) and `HighlightColor` carry it to the
  markup builders, which wrap the first case-insensitive occurrence in
  `<span data-highlight="true" style="color:…">`. Titles stay clean text everywhere else.

## D10 — Content Studio fonts

Anonymous `GET /api/content-studio/fonts.css` returns `@font-face` rules pointing at
`GET /api/content-studio/fonts/{dir}/{file}`. The Studio canvas adds the stylesheet once to
`document.head`, so the editor shows the same faces as the render.
