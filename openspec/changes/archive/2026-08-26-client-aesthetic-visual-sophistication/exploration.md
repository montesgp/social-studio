# Exploration: client-aesthetic-visual-sophistication

## Current State

The generation pipeline has TWO independent rendering paths that both consume
`ClientAestheticProfile`/`AestheticExtraction`, but with radically different fidelity to the
client's real style:

1. **AI-designed path** (`AiVisualDesigner.cs`, `BuildSystemPrompt`/`BuildAestheticGuidance`,
   `Incoders.Hive.Infrastructure/Social/ContentGeneration/AiVisualDesigner.cs`): receives
   `StyleDescriptors`, `Motifs`, `ImageTypeRatio` (via `ClientAestheticDesignContext.AllowsDecoration`),
   palette, and typography as prompt input. The model has creative license to draw decoration,
   gradients, shapes, blobs — gated by `AllowsDecoration` (true unless the corpus was measured and
   shows `Decorator <= 0.05`). The prompt explicitly PROHIBITS SVG ("No JavaScript, no SVG, no
   @import") and prohibits inventing logos/marks — decoration must be plain inline-CSS divs
   (gradients, shapes via border-radius/box-shadow, not vector icons).

2. **Deterministic flat path** (`ContentGenerationJobProcessor.cs`, `RenderFaithfulFlat()` →
   `BuildFlatColorMarkup` → `BuildFlatCarouselSlots`): triggered either as the original "FlatColor
   short-circuit" or via `ClientAestheticDegradeDecision.IsEligible` (photo generation failed →
   degrade to faithful flat, per `client-aesthetic-fidelity-fixes`). This path draws EXACTLY: one
   solid background `<div>` (palette.Background), an optional accent `<div>` pill (only if
   `aesthetic.Palette` has an entry with `Role == "Accent"`), a title `<h1>`, optional subtitle `<p>`,
   optional cta `<p>` — all absolutely positioned with fixed pixel coordinates/sizes hardcoded per
   slot. `StyleDescriptors`, `Motifs`, and `ConceptualMethod` are NEVER read here. Confirmed by
   reading the full function bodies (lines ~2304-2414 of `ContentGenerationJobProcessor.cs`).

3. **Brand-mark slot** (`BuildBrandMarkSlot`, same file, lines ~2441-2507) IS
   structured-decoration-aware: it renders either a caption-styled text signature, a real cropped
   `<img>` icon asset (via `IBrandMarkAssetExtractor.ExtractAsync`, crop-only — decodes bytes
   already archived, slices the region `BrandMarkDetection.BoundingBox` already validated), or a
   colored chip fallback — all driven by structured fields (`MarkType`, `PositionHint`, `ColorHex`,
   `BoundingBox`), never free text. This is the ONLY existing precedent in the codebase for "crop a
   real asset from the client's own images and render it deterministically."

## Affected Areas

- `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs`
  — `BuildFlatColorMarkup` (2312), `BuildFlatCarouselSlots` (2364), `BuildCarouselTemplateSlots`
  (2258), `BuildStoryTemplateSlots` (2187), `BuildBrandMarkSlot` (2441) — the deterministic rendering
  primitives; currently text + solid background + accent bar + brand-mark chip/image only.
- `Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IClientAestheticStyleExtractor.cs`
  — `AestheticExtraction` record (line 39) already carries 12 additive trailing-nullable fields
  (`Palette`, `StyleDescriptors`, `Motifs`, `CaptionVoice`, `BackgroundPattern`, `ColorPairings`,
  `ConceptualMethod`, `ImageTypeRatio`, `BrandMark`, `SchemaVersion`, `TypicalSlideCount`,
  `Typography`, `BackgroundTransparency`) accumulated across at least 6 prior changes this quarter.
  `StyleDescriptors`/`Motifs` are `IReadOnlyList<string>` — free-text English sentences from the
  vision model, structurally unusable by a deterministic renderer without NLP parsing.
- `Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ClientAestheticProfile.cs` —
  mirrors the same 12 fields as the persisted/read-side projection.
- `Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ClientAestheticDesignContext.cs`
  — `AllowsDecoration` (line 68) is the ONE existing structured decoration gate (bool derived from
  measured `ImageTypeRatio.Decorator` share vs a 0.05 noise floor). This is the closest existing
  precedent for "structured decoration signal driving behavior," but it only gates a binary yes/no
  for the AI path, not a content-bearing decoration type.
- `Incoders.Hive.Infrastructure/Social/ContentGeneration/AiVisualDesigner.cs` — `BuildSystemPrompt`
  (376) `depthRule` (436) and `aestheticBlock` (403-428) — where `StyleDescriptors`/`Motifs`
  currently get used, only as prose fed to a generative model, and only for photo-classified/AI-eligible
  slides, never for flat slides.
- `Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IBrandMarkAssetExtractor.cs` — the
  asset-crop precedent (SkiaSharp decode + crop by validated bounding box, never throws, degrades to
  chip on any failure).
- No SVG/icon-font/vector-shape rendering capability exists anywhere in `Visual/`, `Composers/`, or
  the flat-render path — confirmed via search (`svg`, `IconFont`, `<svg` all return zero matches in
  `ContentGeneration`). Any deterministic "draw a divider rule + diamond" or "draw a rounded icon"
  primitive would be built from scratch using inline-CSS div/border tricks (achievable for simple
  geometric shapes — a thin rule + centered diamond is a `<div>` + a 45°-rotated square `<div>`, both
  are 2-3 more absolutely-positioned divs, low technical risk) or would need actual vector/icon
  assets (higher risk — no existing pipeline for that).

## Structured-vs-Prose Gap (first-class finding)

The owner's example decoration ("diamond divider", "rounded taupe circles with thin line-icons",
"botanical eucalyptus branches") is currently ONLY captured as English prose inside
`StyleDescriptors`/`Motifs` — an unstructured array with no schema, produced by a vision model's
free description. A deterministic renderer cannot safely branch on "does this string contain the
word 'diamond'" — that is exactly the kind of prose-matching the earlier
`client-aesthetic-fidelity-fixes` change moved AWAY from (it explicitly replaced AI free-license
background generation with the deterministic flat path specifically to guarantee no invented
content). Any deterministic-decoration approach requires NEW structured fields analogous to
`BackgroundPattern`/`ColorPairings`/`ImageTypeRatio` — i.e., a 13th+ addition to
`AestheticExtraction`/`ClientAestheticProfile`, continuing an already-fast-growing schema (12 fields
added across ~6 changes this quarter, per the two already-archived changes
`client-aesthetic-fidelity-fixes` and `client-aesthetic-generation-hardening` plus
`client-aesthetic-conceptual-detection`).

## Approaches

### 1. Structured decoration-motif extraction + deterministic rendering

Add a new structured record (e.g. `DecorationMotifs { HasDividerRule: bool, DividerStyle:
"diamond"|"dot"|"line"|null, HasIconRows: bool, IconRowStyle: string?, HasBotanicalAccents: bool,
CompositionArchetype: string? }`) extracted by the vision model with an explicit enum/bool schema
(mirroring how `BackgroundPattern`/`ImageTypeRatio` were built), then render the few LOW-RISK
primitives (divider rule + dot/diamond, accent shapes, layout archetype switch between
left-column-text vs centered) deterministically as new slots in `BuildFlatCarouselSlots`.

- **Pros**: preserves the "no AI-invented content" guarantee that motivated the faithful-flat path;
  incremental, testable per-field; consistent with the codebase's established pattern (structured
  enums over prose).
- **Cons**: caps out well below "casi imperceptible" — icon rows, botanical photographic accents,
  and real composition-archetype-driven layout swaps are NOT safely reducible to a handful of
  booleans; every new visual nuance the owner wants requires another schema field and another PR
  (schema fatigue — already the 7th+ addition to a record that changed 6 times this quarter);
  genuinely photographic/organic elements (eucalyptus branches, real icon glyphs) cannot be
  deterministically drawn from CSS primitives at all — this approach only reaches "adds a
  rule/diamond/simple accent," not "matches the client's real composition."
- **Effort**: Medium (schema + extraction prompt changes + new deterministic render primitives, each
  individually small).

### 2. Constrained AI-design pass for flat slides, fed ONLY structured facts (not free creative license)

Extend the existing `AiVisualDesigner`/`AllowsDecoration` mechanism so genuinely-flat slides also
route through a design pass, but one that receives ONLY already-extracted structured signals
(palette, typography, `ConceptualMethod`, a new bounded decoration-motif schema) rather than
open-ended `StyleDescriptors` prose, and is constrained to a narrower decoration vocabulary than the
full creative/photo path (e.g. no full free-form background generation, only decoration-layer
additions on top of the deterministic solid background/text).

- **Pros**: reuses infrastructure that already exists (`BuildSystemPrompt`, guardrail validation,
  `AllowsDecoration` gating) instead of building new deterministic-rendering primitives from
  scratch; can reach visual fidelity closer to "imperceptible" because it isn't limited to
  hand-built CSS-primitive vocabulary; naturally extensible as new motifs are discovered without a
  new deterministic-renderer code path per motif.
- **Cons**: reintroduces non-determinism/AI-invention risk on the flat path specifically — the exact
  risk `client-aesthetic-fidelity-fixes` moved away from; needs new guardrails to prevent the AI
  from re-inventing full backgrounds/logos/marks (the existing prompt already prohibits SVG, logos,
  and invented text — would need to also prohibit large photographic elements creeping onto what
  should be a "flat" slide); blurs the flat/AI-photo distinction that the pipeline currently uses as
  a reliability lever (flat = "guaranteed safe fallback").
- **Effort**: Medium-High (new guardrail rules, new prompt block, new eligibility/validation logic,
  regression risk against the just-shipped faithful-flat guarantees).

### 3. Asset-crop precedent extended beyond brand-mark

Apply the `IBrandMarkAssetExtractor` crop pattern to other recurring structured elements detected
with a bounding box (e.g. a divider glyph, a recurring icon glyph, a botanical accent crop) — i.e.,
instead of inventing a generic replica, crop the REAL element from the client's own reference images
and render it as a deterministic `<img>`, exactly as brand-mark icons already work.

- **Pros**: most literally "imperceptible from the client's real content" since it's not an
  approximation at all — it's the client's actual pixels; reuses an already-proven, already-tested
  crop mechanism (SkiaSharp decode + bounding-box slice + never-throws contract); stays fully
  deterministic, no new AI-invention risk.
- **Cons**: requires the vision model to reliably produce accurate bounding boxes for MULTIPLE new
  element types (divider, icon-row glyphs, botanical accents) — unmeasured reliability risk this
  session, and the existing brand-mark icon-crop path already has a flagged-but-unmeasured
  reliability risk noted in the ask; scales poorly to elements that vary per-slide (a botanical
  branch position/shape differs by post) vs. brand-mark which is genuinely recurring/consistent;
  still needs a structured detection schema (bounding box + element-type + recurrence-confidence)
  per new element category — same 7th-field-addition schema-growth concern as Approach 1.
- **Effort**: Medium (per element type; compounds if multiple element categories are pursued in one
  change).

## Recommendation

No single approach reaches "imperceptible" alone, and the explore step should NOT pre-select one —
this is the central open question for `sdd-propose`. That said, a layered starting point is
lower-risk than an all-or-nothing bet:

- Start with Approach 1 for the highest-confidence, lowest-risk primitives that generalize across
  clients (divider rule + dot/diamond, a bounded set of layout archetypes) — these are genuinely
  reducible to booleans/enums and directly extend the existing structured-extraction pattern.
- Treat Approach 3 (asset-crop) as a targeted extension ONLY for elements a given client
  demonstrably reuses verbatim across posts (icon glyphs, a signature botanical crop) — same
  reliability caveat as the existing brand-mark icon path, so it should be scoped and measured
  before broad rollout, not assumed reliable.
- Treat Approach 2 (constrained-AI-for-flat) as the higher-ceiling but higher-risk lever to revisit
  only if 1+3 together are proven insufficient to satisfy the owner's bar — because it directly
  reopens the non-determinism tradeoff the prior change closed, it needs its own explicit risk
  acceptance from the owner, not a default choice.

## Risks

- **Schema fatigue**: `AestheticExtraction`/`ClientAestheticProfile` have already grown by 12
  additive fields across ~6 changes this quarter (`client-aesthetic-conceptual-detection`,
  `client-aesthetic-fidelity-fixes`, `client-aesthetic-generation-hardening`, plus earlier phases).
  A 7th addition (decoration motifs) must be scoped tightly — one bounded record, not several
  loosely-related booleans added incrementally — to avoid further eroding schema maintainability
  and extraction-prompt reliability (more fields per vision-model call risks lower per-field
  extraction accuracy).
- **Vision-model reliability for NEW structured fields is unmeasured** — existing structured fields
  (`BackgroundPattern`, `ColorPairings`, `ConceptualMethod`) already have known cold-start/ambiguous-
  value fallback paths; a new `DecorationMotifs` schema needs the same degrade-to-null discipline,
  not assumed accuracy.
- **Asset-crop bounding-box reliability for new element types (icons, dividers, botanical accents)
  is explicitly unmeasured this session** — the existing brand-mark icon-crop path already carries
  this flag; extending crop-based rendering to more element categories multiplies that unmeasured
  risk.
- **Blurring the flat/photo reliability boundary**: any approach that reintroduces AI creative
  license on the "flat" path (Approach 2) risks regressing the exact guarantee
  `client-aesthetic-fidelity-fixes` shipped to fix — needs explicit owner risk acceptance, not
  silent reintroduction.
- **"Imperceptible" is a very high, largely open-ended bar** — no combination of Approaches 1-3
  alone guarantees it; the proposal phase should set an explicit, testable/reviewable definition of
  "sophisticated enough" (e.g. specific composition archetypes matched, specific motifs rendered)
  rather than leave it as an unbounded aspiration.

## Ready for Proposal

Yes — with the explicit caveat that `sdd-propose` MUST resolve the Approach 1 vs 2 vs 3 (or
layered-combination) architectural question as its first decision, since it determines the schema
shape, the render-pipeline scope, and whether the flat-path's determinism guarantee is preserved or
intentionally relaxed. The orchestrator should present this three-way tradeoff to the user before
the proposal locks in an approach.

## Key Learnings

1. The deterministic flat-rendering path in ContentGenerationJobProcessor.cs never reads
   StyleDescriptors, Motifs, or ConceptualMethod.
2. AestheticExtraction and ClientAestheticProfile have each grown by twelve additive trailing-nullable
   fields across roughly six prior changes this quarter.
3. The AI visual designer prompt explicitly prohibits SVG output and prohibits inventing logos or
   brand marks in generated markup.
4. IBrandMarkAssetExtractor already establishes a working precedent for cropping real decorative
   elements from a client's own reference images via bounding box.
5. No SVG, icon-font, or vector-shape rendering capability exists anywhere in the ContentGeneration
   Visual or Composers namespaces today.
