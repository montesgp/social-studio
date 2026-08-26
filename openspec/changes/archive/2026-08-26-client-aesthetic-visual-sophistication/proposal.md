# Proposal: Client-Aesthetic Visual Sophistication

## Change ID
`client-aesthetic-visual-sophistication`

## Status
Proposal — revision 2. Builds on `exploration.md` (verified codebase map with file/line citations).
Third change in the ClientAesthetic line, after `client-aesthetic-fidelity-fixes` (archived
2026-08-24) and `client-aesthetic-generation-hardening` (archived 2026-08-25). Revision 2 folds in
the owner's answers to the revision-1 question round: `AccentShape` is cut, `CompositionArchetype`
becomes an open extensible classification rather than a closed enum, ornament degrade becomes
operator-visible, and the schema-fatigue norm is adopted as binding. Two questions remain open in
`## Proposal question round`; neither blocks spec/design.

## Intent

**Business problem.** The two prior changes fixed *defects*: the pipeline knew the client's
aesthetic and failed to use it (typography sizes read by nobody, colour pairings ignored on the
photo path, a degrade that invented a dark scrim, an empty chip where the client's real mark
belonged). Those are closed. What remains is not a defect — it is a **ceiling**. The owner's
framing: *"Debemos ser bien sofisticados al momento de generar y estar lo más cercano posible a la
estética del cliente. Casi debería ser imperceptible que se generó con la herramienta el contenido
o las piezas."* A generated piece must not merely avoid being wrong; it must be indistinguishable
from something the client made by hand. Anything short of that still costs an operator a manual
redo, which is the cost the whole Content Generation capability exists to remove.

**Current-state gap (verified, not assumed).** The deterministic flat path — now the *most-travelled*
path, because `client-aesthetic-fidelity-fixes` routes both degrade triggers into it — renders a
strictly fixed vocabulary: one solid background `<div>`, an optional accent pill, `<h1>` title,
`<p>` subtitle, `<p>` cta, an optional brand mark. All left-aligned at hardcoded pixel offsets
(`left:104px` / `left:108px`, `text-align:left`), unconditionally, for every client
(`ContentGenerationJobProcessor.cs` L2364-2414). It never reads `StyleDescriptors`, `Motifs` or
`ConceptualMethod` — the only place the client's real decorative language is captured — because
those are free-text English sentences from a vision model, and branching a deterministic renderer
on prose is exactly what the fidelity change moved away from.

So today: two clients with wholly different visual identities, both correctly extracted, render the
**same layout skeleton** in different colours and fonts. Colour and type fidelity is solved.
*Composition and ornament fidelity does not exist.*

**Outcome.** A flat slide reproduces the client's own compositional habit (where their text sits on
the canvas) and their own recurring ornament (a divider rule with their glyph; where warranted,
their actual decorative element cropped from their own posts) — while a client with no measured
signal renders byte-identically to today.

## Settled decisions (do not re-open)

| # | Decision | Effect on this proposal |
|---|---|---|
| 1 | **Layered Approach 1 + Approach 3.** Structured decoration-motif extraction with deterministic CSS-primitive rendering (A1), plus a *narrow* extension of the proven `IBrandMarkAssetExtractor` crop pattern to one further recurring element type (A3). | Sets the whole scope. A1 is the backbone; A3 is a single measured, gated extension. |
| 2 | **Approach 2 (constrained AI design pass for flat slides) is OUT OF SCOPE.** Reserved as a higher-ceiling fallback only if 1+3 prove insufficient; activating it requires its own explicit risk-acceptance decision. | See Out of Scope. Reintroducing AI creative licence on the flat path directly reopens the non-determinism risk `client-aesthetic-fidelity-fixes` shipped to close. |
| 3 | **The decoration schema must be the smallest thing that works.** No open-ended decoration *taxonomy*; the schema stays one nested record, additive-nullable. | Drives the two-member shape below. |
| 4 | **`AccentShape` is CUT.** It does not enter the schema, the extractor prompt, the persistence round-trip or the renderer. The accent pill keeps rendering exactly as it does today, unconditionally. | `DecorationMotifs` carries two members, not three. Removes the weakest signal, one extraction-JSON field, and the whole accent-shape render branch. Shrinks WU-1 through WU-4. |
| 5 | **`CompositionArchetype` MUST NOT be a closed C# `enum`.** Owner's reasoning, verbatim: *"Existe alguna forma de no quedar atado? No sabemos como van a ser los futuros clientes, simplemente tenemos 3-4 casos de pruebas de diferentes nichos pero pueden existir un montón. Además esto sería para aesthetic, una vez terminemos todo esto voy a implementar otro tipo de generación que va a ser tipo 'modern'... te lo pongo de ejemplo para que no nos atemos."* Two binding constraints follow: (a) an archetype nobody has seen in today's 3-4 test niches must be representable **without a schema or data migration**; (b) this schema must not be over-fitted to `ClientAesthetic`, because a future, separate `modern` generation mode will bring its own defined content shape. | Archetype becomes a defensively-normalised **`string?`**, following the `BrandMarkDetection.MarkType` precedent (a vision-model-classified category held as a normalised string, never an enum). See "Why a string, precisely" below. |
| 6 | **Ornament degrade MUST be operator-visible in Content Studio** — reversed from revision 1's "render nothing, silent". It reuses the shipped per-slide degrade mechanism (`CompositionSpecEnvelope.DegradeReason` + `lib/studio/degrade-badge.ts` + `SlideDegradeBadge`), not a new one. | See "Ornament degrade visibility" below. Adds work to WU-8 and one `content-studio` spec delta. |
| 7 | **Schema-fatigue norm is BINDING, not advisory.** No further additive field on `AestheticExtraction` / `ClientAestheticProfile` may land without either a consolidation pass or an ADR recorded first. | Recorded as a project convention in `Documentation Sync`, and enforced on this change itself: `DecorationMotifs` is one field slot carrying everything, and `DecorativeOrnamentDetection` nests inside it rather than becoming a fourteenth. |

## Approach 1 — the `DecorationMotifs` schema, precisely

`AestheticExtraction` and `ClientAestheticProfile` each gain **exactly one** additive trailing-nullable
field, `DecorationMotifs? DecorationMotifs`. One bounded record with **two** members:

```
sealed record DecorationMotifs(
    DividerRuleStyle? DividerRule         = null,  // closed enum: Line | LineWithDiamond | LineWithDot
    string?           CompositionArchetype = null) // OPEN, normalised classification token
```

Design rules, each load-bearing:

- **Presence is nullability, never a companion `bool`.** `DividerRule = null` means *unmeasured or
  absent*; there is no `HasDividerRule`. Matches `ConceptualMethod?` / `ImageTypeRatio?`.
- **`DividerRuleStyle` stays a closed enum, correctly.** Its three values are not a *classification
  of the world* — they are the three shapes the CSS primitive can draw (a rule, a rule plus a
  rotated square, a rule plus a circle). The value set is bounded by the renderer, not by the
  clients, so the "we don't know future clients" argument does not apply to it. Unrecognised ⇒
  `null` ⇒ no divider.
- **Every signal has an identity value that reproduces today's output.** Archetype
  `"left-aligned"` MUST render byte-identically to the current builder. Cold-start parity is
  therefore assertable, not argued.
- **Unrecognised / missing / malformed never throws** — the `BrandMarkDetection.NormalizeMarkType`
  defensive-parse convention (FFS-PA-02), extended.
- **No confidence or recurrence-count field.** Model-self-reported confidence is unverifiable and
  would earn a third member nothing; recurrence discipline lives in the extractor prompt ("report
  only what recurs across the analysed slides") and in the null-on-doubt mapping.

Net schema cost: **one new field slot** on each of the two records — the 13th — not three.

### Why a string, precisely — and why it satisfies "no quedar atado"

Verified precedent in this codebase (not invented for this change):

| Existing signal | Representation | Applies here? |
|---|---|---|
| `SlideBackgroundKind` | closed `enum`, unrecognised ⇒ `FlatColor` (`AestheticProfilePersistence.cs` L135) | **No** — its values are renderer-bounded, like `DividerRuleStyle` |
| `TypographyRoles` | `static class` of `const string` (`AestheticTypography.cs` L43-53) | Partly — shows this project already uses open string vocabularies |
| **`BrandMarkDetection.MarkType`** | **`string?` normalised via `NormalizeMarkType`, never throws** (`MultimodalAestheticStyleExtractor.cs` L384) | **Yes — the exact precedent**: a vision-model-classified category held as a defensively-parsed string |

`CompositionArchetype` follows `MarkType`: a `string?` plus a `CompositionArchetypes` static class
holding the recognised `const string` tokens (`"left-aligned"`, `"centered"`, `"lower-third"`), an
`IsRecognised(string?)` predicate, and a `Normalize(string?)` helper (trim + lowercase, null/blank ⇒
`null`).

**One deliberate divergence from `NormalizeMarkType`, and it is the whole point.** `MarkType`
collapses an unrecognised token to `"unknown"`, discarding it — correct there, because it drives a
binary render branch where an unknown value is useless. Here the opposite is true: an unrecognised
archetype token is *a classification we may learn to render later*. So `Normalize` **preserves the
token verbatim** (trimmed, lowercased) instead of collapsing it. The consequences, which are exactly
the properties settled decision 5 demands:

1. A model returning `"diagonal-split"` for a future client **deserialises fine, persists fine, and
   renders as `left-aligned`** — the safe identity preset. Nothing fails, nothing throws, no
   migration runs.
2. That token is **stored** in the profile. When we later decide `"diagonal-split"` deserves a
   preset, adding it is a **code-only change** — a new `const string` plus a new preset branch. No
   schema version bump, no data migration, no re-extraction of existing clients, no OpenSpec
   breaking delta.
3. The future `modern` generation mode can define its own archetype vocabulary over the same field
   without this change having pre-committed the value space to three ClientAesthetic-shaped names.

The cost, stated honestly: the type system no longer guarantees a valid archetype, so validity is a
*test* obligation instead of a *compiler* obligation, and unrecognised tokens are silently rendered
as `left-aligned` rather than being caught at compile time. That trade is accepted deliberately —
the compiler guarantee is worth less than not needing a schema change per client niche.

## Approach 3 — which recurring element actually earns a crop

The exploration is right that crop-by-bounding-box scales well only to elements a client reuses
*verbatim*. Evaluated against what already exists:

| Candidate | Verdict | Why |
|---|---|---|
| **Divider glyph** (diamond / dot on a rule) | **No — A1 covers it** | Perfectly CSS-reducible (a thin `<div>` plus a 45°-rotated square `<div>`). Worse, a hairline rule plus a ~10px glyph would sit at or below `BrandMarkDetection.MinimumNormalizedArea` (0.0009 = 3%×3%) and be rejected by the existing guard, or crop at unusably low resolution. Cropping it would be strictly riskier than drawing it. |
| **Icon-row glyphs** | **No — defer** | An icon *row* is N distinct glyphs, so N bounding boxes, N crops and N unreliability draws in one change; and each glyph carries semantics that must map to generated copy the pipeline writes fresh per job. Rendering the client's real "calendar" icon next to a line of unrelated text is a *worse* fidelity failure than rendering nothing. |
| **Recurring decorative ornament** (a signature botanical sprig, corner flourish, texture stamp) | **Yes — the one extension, gated** | Genuinely organic, so not CSS-reducible at all; typically a single reused brand/stock asset, so verbatim-recurring; large enough to clear the existing minimum-area guard and crop legibly. Position variance is handled by *not* reusing the source position — the crop renders at a deterministic anchor, exactly as `BuildBrandMarkSlot` bottom-anchors an icon mark regardless of where it was found. |

So Approach 3 extends the crop pattern to **exactly one new element type: the recurring decorative
ornament.** It reuses `BrandMarkBoundingBox`, `TryValidateBoundingBox`, `MinimumNormalizedArea`,
`IGeneratedAssetStorage` and the never-throws degrade contract unchanged.

**The ornament track is measured before it renders**, reusing the gate the prior change proved: the
schema + validation unit lands first and its bounding-box reliability is recorded; if boxes measure
unusable, the ornament render unit is **cut** and Approach 1 stands alone. That decision gets
written down, not silently absorbed.

### Ornament degrade visibility (settled decision 6)

On *any* ornament failure (no box, invalid box, below-minimum area, crop or store failure, extractor
unavailable) the renderer still **draws no ornament element** — an invented ornament is precisely
the invention this line of work exists to prevent. What changes from revision 1 is that the omission
is no longer silent to the operator.

**Chosen mechanism: reuse `CompositionSpecEnvelope.DegradeReason`, do not add a parallel marker.**
Verified shape (`CompositionSpecEnvelope.cs` L128-137): it is already a trailing
`string? DegradeReason = null` carrying `[JsonIgnore(WhenWritingNull)]`, so a non-degraded envelope
stays byte-identical; tokens are `const string`s on `SlideDegradeReason`
(`ClientAestheticDegradeDecision.cs` L36-39); the frontend reads it through
`getDegradeReason(envelope)` (`apps/web/lib/studio/degrade-badge.ts`), which accepts *any* non-empty
string, and renders `SlideDegradeBadge` (`content-studio-panel.tsx` L351).

Justification for reuse over a parallel field:
- A new envelope field would violate the schema-fatigue norm this same proposal just made binding
  (settled decision 7) for no representational gain.
- `getDegradeReason` and the envelope's `[key: string]: unknown` index signature already carry any
  new token end-to-end with **zero** serialisation, persistence or contract work.
- `PptxExportAdapter` already asserts the marker never leaks into an export
  (`PptxExportAdapterTests.cs` L602-622); a new token inherits that guarantee for free.

Two consequences, stated rather than glossed:
1. **The badge must become token-aware.** `SlideDegradeBadge` currently hardcodes the label
   "Fallback design" for every token. That label is wrong for an ornament omission — the slide is
   not a fallback design, it is simply missing one decorative element. So WU-8 adds a token→label
   map and a distinct label for `ornament-unavailable`. This is a real frontend change, not free.
2. **`DegradeReason` holds one token, and collision is expected, not rare** — the ornament renders
   on the deterministic flat path, which is *also* where both existing degrade triggers land. A
   trigger-A slide whose ornament crop is missing has two facts and one slot. Resolution:
   **whole-slide degrade tokens take precedence** over the element-level `ornament-unavailable`,
   because the whole-slide fallback is the more consequential fact and already tells the operator
   "this slide did not render normally". The ornament degrade is emitted to the structured log
   **unconditionally**, regardless of which token wins the badge, so diagnosis never loses
   information. Promoting `DegradeReason` to a token list is an explicit non-goal here.

## New deterministic render primitives

Both land in `BuildFlatColorMarkup` / `BuildFlatCarouselSlots`, as editable content-layer
`data-slot` elements, never rasterized into the background layer — the rule `content-generation`
already states for accent and brand-mark elements.

1. **Divider rule** — `data-slot="divider"`. A thin absolutely-positioned `<div>` (~2px, ~180px
   wide) in the client's resolved accent colour, falling back to the slide's text colour when no
   `Accent` palette role resolves. For `LineWithDiamond`, a second ~10×10 `<div>` with
   `transform:rotate(45deg)` centred on it; for `LineWithDot`, the same square with
   `border-radius:999px`. Placed in the existing vertical gap between the title block and the
   subtitle.
2. **Composition archetype geometry presets** — the hardcoded `left:104px` / `left:108px` /
   `text-align:left` literals become a **preset resolved by token lookup**, applied consistently
   across accent, divider, title, subtitle and cta. `"left-aligned"` is today's literals verbatim;
   `"centered"` is symmetric with centred text; `"lower-third"` anchors the text block to the lower
   portion with the upper canvas left open. **Null, blank, or any unrecognised token ⇒
   `"left-aligned"`** — one fallback path, exercised by test with a deliberately-invented token.
3. **Ornament image slot** — `data-slot="ornament"`, an `<img>` at the stored crop, at a
   deterministic anchor sized conservatively so it never overlaps the title block, honouring the
   archetype preset. Absent crop ⇒ slot omitted entirely **and** the degrade token set per above.

## What "sophisticated enough" means for THIS change (testable)

"Imperceptible" is unbounded and cannot be a completion criterion. This change is complete when all
of the following are *asserted on rendered markup*, not judged by eye:

1. A profile signalling `DividerRule = LineWithDiamond` renders a `data-slot="divider"` element on a
   FlatColor slide, coloured from that client's real extracted accent (or text) colour — and a
   profile with `DividerRule = null` emits no divider element at all.
2. Two profiles differing **only** in `CompositionArchetype` produce measurably different slot
   geometry for title, subtitle and cta on the same input copy.
3. A profile carrying an **unrecognised** archetype token renders identically to `"left-aligned"`,
   **and that token is still present in the persisted profile afterwards** — the extensibility
   guarantee, asserted rather than asserted-by-hope.
4. A profile with `DecorationMotifs = null` renders markup **byte-identical** to the pre-change
   output for the same spec. This is the hard cold-start guarantee.
5. A profile signalling an ornament with a valid box produces a stored crop asset and a
   `data-slot="ornament"` `<img>`; the same profile with an invalid/absent/below-minimum box renders
   no ornament element, sets the `ornament-unavailable` degrade token, and emits a degrade log entry.
6. No AI model is called for any flat slide at any point in this change — asserted, not assumed.
7. The owner reviews one real generation per client for at least two clients with materially
   different signalled archetypes, and records accept/reject in writing.

Criteria 1-6 are machine-verifiable. Criterion 7 is the honest, human part of the bar and is stated
as such rather than dressed as a metric.

## Scope

### In Scope

1. **`DecorationMotifs` record** with one closed enum (`DividerRuleStyle`) and one open normalised
   string classification (`CompositionArchetype`), plus a `CompositionArchetypes` const-token class
   with `Normalize` / `IsRecognised`, as pure Application value objects.
2. **Extraction**: extractor prompt fields + DTO + `Map()` for both members, defensively parsed
   (divider unrecognised ⇒ null; archetype unrecognised ⇒ **preserved verbatim**); extractor
   `SchemaVersion` bumped.
3. **Persistence round-trip**: `AestheticExtraction` → `AestheticProfilePersistence` →
   `ClientAestheticProfile` → `ClientAestheticDesignContext`. Additive-nullable, no EF migration
   (the profile is JSON-serialised).
4. **Divider-rule render primitive** in `BuildFlatCarouselSlots`, accent-coloured with text-colour
   fallback.
5. **Composition-archetype geometry presets** replacing the hardcoded offsets, token-resolved, with
   `"left-aligned"` as the byte-identical identity preset and the sole unrecognised-token fallback.
6. **Ornament detection schema**: source-image path + normalised bounding box on a
   `DecorativeOrnamentDetection` nested inside `DecorationMotifs`, reusing `BrandMarkBoundingBox`
   and `TryValidateBoundingBox` verbatim, with the same index→`StoragePath` resolution the extractor
   already performs for brand marks; bounding-box reliability recorded before any render work.
7. **Ornament crop + render** (gated on item 6's measurement): extend the crop capability behind an
   Application port following `IBrandMarkAssetExtractor`, persist via `IGeneratedAssetStorage`,
   render the `data-slot="ornament"` `<img>`.
8. **Ornament degrade visibility**: new `SlideDegradeReason.OrnamentUnavailable` token, precedence
   rule against the existing whole-slide tokens, unconditional degrade log entry, and a token-aware
   label in `SlideDegradeBadge` / `degrade-badge.ts`.
9. **Ornament ref resolution** in `StudioVisualMarkupResolver` and `PptxExportAdapter` — the
   knock-on the prior change discovered for brand marks applies identically here.
10. **Spec deltas + documentation sync** for `profile-analysis`, `content-generation` and
    `content-studio`, plus recording the binding schema-fatigue norm.

### Out of Scope — explicit non-goals

| Deferred | Why now is wrong |
|---|---|
| **`AccentShape` (settled decision 4)** | Cut outright. The accent pill is already rendered, already client-coloured, and is a small element; it was the weakest of the three signals and cost one extraction-JSON field — the change's largest reliability risk — for the least fidelity gain. |
| **Approach 2 — a constrained AI design pass for flat slides (settled decision 2)** | It reopens the exact non-determinism risk `client-aesthetic-fidelity-fixes` closed by making the flat path the *guaranteed-safe* path for both degrade triggers. Its ceiling is genuinely higher, but buying it means giving an AI creative licence on the one path the pipeline currently relies on as a reliability floor. That is an owner risk-acceptance decision with its own change, ADR and guardrail design. Revisit only if 1+3 measurably fall short. |
| **Shipping presets for archetypes beyond the three recognised tokens** | The field is open precisely so a fourth is a cheap later code-only change. Speculating presets now, from 3-4 test niches, is the over-fitting settled decision 5 warns against. |
| **A `modern` generation mode, or generalising this schema to serve it** | Explicitly a future, separate change with its own defined content shape. This schema must not be shaped around it in advance. |
| **Promoting `DegradeReason` to a multi-token list** | The precedence rule plus the unconditional log covers the collision case. A list is a contract widening across backend, Studio and export for a marginal UI gain. |
| **Icon-row glyph crops** | N glyphs ⇒ N bounding boxes ⇒ multiplied unmeasured risk in one change, plus a semantic mapping this change has no basis to make. |
| **Cropping the divider glyph instead of drawing it** | CSS-reducible, and below the existing minimum-area guard. Strictly more risk for strictly less reliability. |
| **Background removal / matting on the ornament crop** | Same non-goal the brand-mark icon crop already carries; the honest rectangular crop ships first. |
| **Applying archetypes / dividers to the AI-photo path or to non-carousel composers** | Scoped to the deterministic flat carousel path. Widening multiplies blast radius across builders the last two changes just stabilised. |
| **Free-text motif matching or NLP over `StyleDescriptors`** | Prose-matching is the failure mode this whole line of work moved away from. An open *token* with a fallback is not prose-matching: it is a single normalised classification with one unrecognised path. |
| **Texture/pattern fills, gradients, or shadow treatments** | Each is a separate visual vocabulary with its own extraction reliability question. |
| **A Content Studio editor for decoration motifs** | Operator-side authoring of the aesthetic is a distinct capability; this change extracts and renders. |

## Capabilities

### New Capabilities
None. The ornament crop introduces a new *port*, not a new capability — the behaviour belongs to
`content-generation`.

### Modified Capabilities (additive deltas)

- `profile-analysis`: extraction gains a bounded `DecorationMotifs` record — one closed nullable
  enum, one open normalised nullable archetype token that MUST be preserved verbatim when
  unrecognised, and a nested `DecorativeOrnamentDetection` (source path + validated normalised
  bounding box); all additive-nullable, absent ⇒ current behaviour; profiles persisted before this
  change deserialise with everything null and throw nothing.
- `content-generation`: the deterministic flat carousel builder MUST render a divider-rule element
  when `DividerRule` is signalled, MUST apply the archetype token's geometry preset across
  accent/divider/title/subtitle/cta, MUST fall back to the `"left-aligned"` preset for a null or
  unrecognised token, MUST render a stored ornament crop as `data-slot="ornament"` when one exists,
  MUST render no ornament element and set the `ornament-unavailable` degrade token when one does
  not, and MUST produce byte-identical markup to today when `DecorationMotifs` is null.
- `content-studio`: markup reference resolution MUST cover the ornament asset ref in both the Studio
  resolver and the PPTX export path; and the per-slide degrade badge MUST render a label
  distinguishing an element-level ornament omission from a whole-slide fallback design.

## Approach — file / layer map

| Piece | Layer | Path |
|---|---|---|
| `DecorationMotifs` + `DividerRuleStyle` enum + `CompositionArchetypes` token class + normalisers | Application (Shared) | `Features/Social/ContentGeneration/Shared/DecorationMotifs.cs` (new) |
| `DecorativeOrnamentDetection` (reuses `BrandMarkBoundingBox`), nested in `DecorationMotifs` | Application (Shared) | `Features/Social/ContentGeneration/Shared/` (new) |
| `DecorationMotifs` on the extraction contract | Application (Abstractions) | `Abstractions/Social/ContentGeneration/IClientAestheticStyleExtractor.cs` (`AestheticExtraction`, trailing nullable) |
| Persisted round-trip + read-side projection | Application (Shared) | `Shared/AestheticProfilePersistence.cs`, `Shared/ClientAestheticProfile.cs` |
| Signal reaches the renderer | Application (Shared) | `Shared/ClientAestheticDesignContext.cs` |
| Divider primitive, archetype presets, ornament `<img>` | Application | `Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` (`BuildFlatColorMarkup` L2312, `BuildFlatCarouselSlots` L2364) |
| `OrnamentUnavailable` degrade token + precedence | Application (Shared) | `Shared/ClientAestheticDegradeDecision.cs` (`SlideDegradeReason`) |
| Ornament crop port | Application (Abstractions) | `Abstractions/Social/ContentGeneration/` (new, mirrors `IBrandMarkAssetExtractor`) |
| SkiaSharp crop + store adapter | Infrastructure | alongside the existing brand-mark crop adapter |
| Extractor prompt, DTO, `Map()`, index→`StoragePath` resolution, schema-version bump | Infrastructure | `Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs` |
| Ornament ref resolution (Studio + export) | Application / Infrastructure | `Features/Social/ContentStudio/Shared/StudioVisualMarkupResolver.cs`, `Rendering/PptxExportAdapter.cs` |
| Token-aware degrade badge label | Web | `apps/web/lib/studio/degrade-badge.ts`, `apps/web/components/social/content-studio-panel.tsx` |

### Reused vs. new

| Reused unchanged | New |
|---|---|
| `BrandMarkBoundingBox`, `TryValidateBoundingBox`, `MinimumNormalizedArea` | Applied to a second element type |
| `BrandMarkDetection.MarkType` normalised-string precedent | `CompositionArchetype`, with verbatim preservation of unrecognised tokens |
| `TypographyRoles` const-string vocabulary shape | `CompositionArchetypes` |
| `SlideBackgroundKind` bounded-enum precedent | `DividerRuleStyle` only |
| `IBrandMarkAssetExtractor` port shape + never-throws contract | One sibling port for the ornament crop |
| `IGeneratedAssetStorage`, `StoredAssetKey`, `StoredAssetRef`, SkiaSharp 3.119.0 | The ornament crop operation |
| `CompositionSpecEnvelope.DegradeReason` field + `getDegradeReason` + `SlideDegradeBadge` | One new token, a precedence rule, and a token→label map |
| Existing accent-colour resolution in `BuildFlatColorMarkup` (`Role == "Accent"`) | Reused verbatim as the divider colour source |
| `StudioVisualMarkupResolver` path→data-URI mechanism | Extended to a third ref field |
| The accent pill markup | **Untouched** — `AccentShape` is cut |
| All colour, palette, pairing and typography resolution | Untouched — this change adds geometry and ornament only |

### Ordering rationale

Schema before render, and CSS-primitive work before crop work. The A1 track (schema → extraction →
persistence → divider → archetype) is fully deterministic and carries no unmeasured dependency, so
it ships first and stands on its own. The A3 ornament track ships second, detection-and-measurement
before crop-and-render, so an unusable bounding-box result cuts only the last two units and leaves
everything already shipped intact.

## ADR determination

`openspec/config.yaml` sets `require_adr_for_architecture_changes: true`.

**A short ADR is required, and it is small.** The A1 track needs none — it adds nullable fields to
existing contracts and geometry branches inside an existing builder, introducing no bounded area,
port, provider category or credential type. (The open-string archetype is a representation choice
recorded in this proposal, not an architectural boundary.) The A3 track introduces a **second**
Application port performing image manipulation. The port *category* was already accepted by the
`client-aesthetic-generation-hardening` ADR for `IBrandMarkAssetExtractor`; the open question this
ADR must answer is narrower and worth recording: **one generalised crop port serving both brand
marks and ornaments, or a second sibling port?** The ADR also records the render-nothing-plus-visible-token
degrade contract for ornaments. If the ornament track is cut by the measurement gate, the ADR is not
needed and is dropped with it.

A **second, one-page ADR** records the binding schema-fatigue norm (settled decision 7), since the
norm constrains future changes and settled decision 7 makes an ADR one of its two permitted escape
hatches — the norm itself must therefore be recorded as one.

## PRD Traceability

`openspec/config.yaml` sets `require_prd_traceability: true`.

| PRD anchor | This change | Honesty note |
|---|---|---|
| §1 Product Summary / §3 Goals — content the client can publish as-is | Directly: composition and ornament fidelity is the remaining gap between "not wrong" and "indistinguishable" | — |
| §9 Acceptance Criteria — fidelity of generated assets | Adds compositional fidelity to the colour/type fidelity already shipped | — |
| §10 Content Generation — Format Taxonomy | Carousel flat path only; no taxonomy change | — |
| §11 Profile Analysis (Mundo 2) | Extraction gains decoration motifs and a second derived-asset element type | §11 was already amended by the prior change to cover deriving durable assets from analysed imagery; the ornament is the same mechanism applied to a second element, so **no further §11 amendment is expected**. Spec phase confirms. |

## Delivery forecast — direct sequential commits to `dev`

Delivery strategy `single-pr`: direct sequential commits to `dev`, no PR chain, no stacked PRs,
tracked via one GitHub issue. Budget **800 changed lines per work unit**; estimates are
`additions + deletions` with Strict TDD included (tests ≈ 1:1).

| # | Work unit | Track | Rev-1 est. | Rev-2 est. | Over budget? |
|---|---|---|---|---|---|
| WU-1 | `DecorationMotifs` + `DividerRuleStyle` enum + `CompositionArchetypes` token class + normalisers (value objects and their tests only) | A1 | ~320 | **~230** | No |
| WU-2 | Extraction half: extractor prompt fields, DTO, `Map()`, schema-version bump, defensive-parse tests incl. verbatim-preservation of an unrecognised archetype token | A1 | ~420 | **~340** | No |
| WU-3 | Persistence round-trip: `AestheticExtraction` → `AestheticProfilePersistence` → `ClientAestheticProfile` → `ClientAestheticDesignContext` | A1 | ~380 | **~330** | No |
| WU-4 | Divider-rule render primitive + byte-identity cold-start assertions | A1 | ~460 | **~380** | No |
| WU-5 | Composition-archetype geometry presets across accent/divider/title/subtitle/cta + `"left-aligned"` identity assertions + unrecognised-token fallback test | A1 | ~520 | **~480** | No |
| WU-6 | `DecorativeOrnamentDetection` schema + extractor prompt + box validation + index→`StoragePath` resolution + **reliability measurement recorded** | A3 | ~480 | **~480** | No |
| WU-7 | Ornament crop port + SkiaSharp adapter + persist via `IGeneratedAssetStorage` + ADRs *(gated on WU-6)* | A3 | ~600 | **~600** | No |
| WU-8 | Ornament `<img>` render + ref resolution (`StudioVisualMarkupResolver`, `PptxExportAdapter`) + `OrnamentUnavailable` token, precedence, log + token-aware badge label *(gated on WU-6)* | A3 | ~520 | **~640** | No |
| WU-9 | Spec deltas (3 capabilities) + doc sync + schema-fatigue norm recorded | — | ~380 | **~400** | No |
| | **Total** | | ~4,080 | **~3,880** | 9 commits, none exceeding 800 |

**Re-forecast honesty note.** The revision request expected the cuts to shrink **WU-7**. They do
not: WU-7 is the ornament crop port and SkiaSharp adapter, and neither `AccentShape` nor the
archetype representation touches it — it stays at ~600. What actually shrank is the A1 track
(WU-1 through WU-5, ~2,100 → ~1,760), because `AccentShape` removed one enum, one prompt field, one
DTO member, one persistence hop, one render branch and their tests; and because a normalised string
plus a token class is less code than a third enum with its own parse/serialise/test surface. What
*grew* is **WU-8** (~520 → ~640), absorbing settled decision 6's degrade token, precedence rule and
token-aware badge label — including a genuine `apps/web` change. WU-8 is now the largest unit at
~640, replacing WU-7.

`Decision needed before apply: No` — the two remaining questions refine verification and the initial
recognised-token set; neither blocks the track structure and both have stated safe defaults.
`Chained PRs recommended: No`
`800-line budget risk: Low` — largest single unit is WU-8 at ~640, with ~160 lines of headroom.

**Ordering constraints**: WU-1 → WU-2 → WU-3 → WU-4 → WU-5 (WU-4 before WU-5: the archetype presets
must reposition the divider the previous unit introduced). WU-6 → WU-7 → WU-8. The A1 and A3 tracks
are otherwise independent and interleavable. WU-9 last. **WU-7 and WU-8 are conditional on WU-6's
recorded bounding-box measurement**; if it fails, both are cut, the ornament ADR is dropped, and the
change completes on the A1 track alone with the cut written down.

## Schema-fatigue position and binding norm (settled decision 7)

`AestheticExtraction` and `ClientAestheticProfile` have each grown by twelve additive
trailing-nullable fields across roughly six changes this quarter. `DecorationMotifs` is the
**thirteenth** field slot; `DecorativeOrnamentDetection` is deliberately nested inside it rather
than added as a fourteenth, and `AccentShape` is cut rather than absorbed.

**Binding norm, adopted:** *no further additive field on `AestheticExtraction` /
`ClientAestheticProfile` may land without either (a) a consolidation pass grouping accumulated
signals into a container, or (b) an ADR recorded before the field is written.* This is a project
convention, not a recommendation: a change that adds such a field without one of the two is
non-compliant and must be sent back.

The reason is not tidiness. Every field added to the extraction contract is another field in a
single vision-model JSON response, and per-field extraction accuracy degrades as that response
grows — so the cost of a new field is paid by *existing, already-shipped* fields, silently. The norm
makes that cost explicit at the point it is incurred. It is recorded in the `profile-analysis` spec
and in the ADR named above.

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| **The vision extractor cannot reliably classify a composition archetype.** Layout classification is a coarser, more subjective judgement than the categorical fields already extracted, and a *confidently wrong* archetype visibly breaks a slide where a null would have been safe. | **High** | `"left-aligned"` is the identity preset and the sole fallback for null, blank, and unrecognised tokens alike. The extractor prompt requires the archetype to recur across analysed slides. WU-5's identity assertions mean a wrong classification degrades to *today's* output, never to broken output. |
| **The open-string archetype loses the compiler's validity guarantee** — a typo or drifted model token renders as `left-aligned` and nobody notices | Medium | Accepted trade (settled decision 5). Mitigated by: a single unrecognised path exercised by test with a deliberately-invented token; verbatim persistence, so a drifting token is *observable in the data* rather than discarded; and `IsRecognised` available for a future diagnostic surface. |
| Ornament bounding-box reliability is unmeasured — the same flag the brand-mark icon crop already carries, now applied to a larger, less crisply-bounded element | **High** | WU-6 lands detection and records the measurement *before* WU-7/WU-8 are committed; unusable ⇒ both units cut and the decision written down. Exactly the gate the prior change proved. |
| The ornament crop carries surrounding background and renders as a rectangle over the slide | High (accepted) | Same accepted non-goal as the brand-mark crop; ornaments recur on the client's *own* flat backgrounds, so the surround is usually the client's own colour. Owner reviews the first real crop before WU-8 is considered verified. |
| **`DegradeReason` token collision** — a whole-slide degraded slide whose ornament also failed shows only one badge | Medium (expected, not rare) | Documented precedence (whole-slide wins) plus an **unconditional** structured log entry for the ornament degrade, so no diagnostic information is lost. Token-list promotion is an explicit non-goal. |
| Extraction-JSON contract growth degrades per-field accuracy across *existing* fields, not only the new ones | Medium | Reduced versus revision 1: one record with **two** members plus a nested ornament object, after cutting `AccentShape`. Every field defensively parsed. Regression fixtures assert existing fields still parse from a full-size response. This is the direct motivation for the now-binding norm above. |
| A divider or archetype change collides with the brand-mark slot or the accent slot, producing overlapping elements | Medium | Presets position accent, divider, title, subtitle, cta *and* respect the brand-mark bottom anchor as one coherent geometry set; overlap is asserted against, not eyeballed. |
| The archetype presets touch a builder that is now the destination of both degrade triggers, so a regression here is a regression on the pipeline's reliability floor | Medium | The byte-identity assertion for `DecorationMotifs = null` is a blocking success criterion, not a nice-to-have; every existing flat-path test must pass unchanged. |
| The divider's accent colour fails contrast against the background for some palettes | Low-Medium | Falls back to the slide's resolved text colour, which is already WCAG-validated through `ColorPairings`; a contrast guard is a spec-phase requirement. |
| An ornament `<img>` reaches the browser or the PPTX export as an unresolved storage path | Medium | Explicitly in scope (item 9) and part of WU-8 — the identical knock-on the prior change discovered late for brand marks. |
| The token-aware badge label change regresses the shipped `CAFF-CS-01` badge | Low | The existing badge is asserted by test; the label map defaults every pre-existing token to today's "Fallback design" string, so only the new token takes a new path. |
| **The owner's "casi imperceptible" bar is still not met after all nine units ship** | Medium (stated honestly) | This change raises the ceiling from "colour and type match" to "composition and ornament match"; it does not claim to reach imperceptibility. The testable definition above is the contracted bar. If it is met and the owner's bar still is not, settled decision 2's Approach 2 is the next lever, and this change deliberately leaves it available and un-prejudiced. |
| Verified against too few clients | Medium | Success criterion 7 requires two clients with materially different signalled archetypes. See open question 2. |

## Rollback Plan

- **Fully additive at the schema level.** `DecorationMotifs` and the nested ornament detection are
  nullable; the profile is JSON-serialised, so **no EF migration exists in this change** and there is
  no data rollback to perform. Reverting leaves previously persisted values ignored exactly as older
  profiles are ignored today — no deserialisation failure in either direction. This holds for the
  open archetype token too: an unrecognised string is inert to a reverted renderer.
- **WU-1/WU-2/WU-3** revert as inert schema: without a render consumer the fields are unread.
- **WU-4/WU-5** are pure render-path changes whose null path is byte-identical to today, so reverting
  either restores the previous markup exactly. WU-5 reverts independently of WU-4 (the divider simply
  returns to its `left-aligned` position); WU-4 reverts independently of WU-5 (the presets keep
  applying to the remaining slots).
- **WU-6** reverts as inert detection schema.
- **WU-7** reverts by removing the port and adapter. Crops already written become orphan objects under
  the analysis folder — inert once WU-8 is also reverted, and removed by the existing
  per-client/per-analysis deletion paths. No cleanup migration.
- **WU-8** reverts to rendering no ornament and writing no `ornament-unavailable` token, which *is* the
  pre-change behaviour. The badge label map reverts independently: dropping it leaves any already-persisted
  `ornament-unavailable` token rendering the generic "Fallback design" label — degraded, never broken.
- Per-commit rollback is sufficient throughout; there is no ordering in which a partial revert leaves
  the pipeline broken.

## Dependencies

- Existing `BrandMarkBoundingBox` / `TryValidateBoundingBox` / `MinimumNormalizedArea`,
  `IGeneratedAssetStorage`, `ProfilePostImageRef` and the reference-image archival pipeline — reused
  read-only.
- Existing `CompositionSpecEnvelope.DegradeReason`, `SlideDegradeReason`,
  `apps/web/lib/studio/degrade-badge.ts` and `SlideDegradeBadge` — extended, not replaced.
- SkiaSharp 3.119.0, **already referenced** by `Incoders.Hive.Infrastructure.csproj`. No new package.
- A configured multimodal extraction route, for the WU-6 bounding-box reliability measurement.
- At least two real client aesthetic profiles with archived reference images and materially different
  compositional habits — ideally one with a visible recurring divider and one with a recurring
  ornament. **Owner-side prerequisite.**
- No new migrations, credentials or provider categories.

## Success Criteria

- [ ] `DecorationMotifs` exists as one record with exactly two members — one closed nullable enum and
      one nullable normalised archetype token. No `AccentShape` type exists anywhere in the codebase.
- [ ] `CompositionArchetype` is **not** a C# `enum`; adding a newly-recognised archetype requires no
      change to any record definition, schema version, or persisted data — asserted by adding a
      throwaway fourth token in test and observing that only renderer code changed.
- [ ] An unrecognised archetype token round-trips **verbatim** through extraction → persistence →
      profile read, and renders as `"left-aligned"`.
- [ ] A profile persisted before this change deserialises with `DecorationMotifs` null and throws nothing.
- [ ] An unrecognised `DividerRuleStyle` token maps to `null` and renders exactly as today.
- [ ] A profile with `DecorationMotifs = null` renders markup **byte-identical** to the pre-change
      output for the same spec, and every pre-existing flat-path test passes unchanged.
- [ ] A profile signalling `LineWithDiamond` renders a `data-slot="divider"` element using the
      client's real accent colour; a profile signalling no divider renders no such element.
- [ ] Two profiles differing only in `CompositionArchetype` produce measurably different slot geometry
      for title, subtitle and cta on identical input copy.
- [ ] `"left-aligned"` reproduces today's `left:104px` / `left:108px` / `text-align:left` geometry exactly.
- [ ] The accent pill's rendered markup is unchanged from pre-change output in every case.
- [ ] No AI model is invoked for any flat slide anywhere in this change — asserted.
- [ ] The ornament bounding-box reliability measurement from WU-6 is recorded; if it fails, the
      decision to cut WU-7/WU-8 is written down rather than silently absorbed.
- [ ] *(If the gate passes)* An ornament with a valid box produces a stored crop asset under the
      analysis folder and renders as a `data-slot="ornament"` `<img>`; the ref resolves to a fetchable
      reference in the Studio preview **and** in PPTX export.
- [ ] *(If the gate passes)* An ornament with a missing, invalid, or below-minimum-area box, or whose
      crop cannot be stored, renders **no ornament element**, sets `DegradeReason =
      "ornament-unavailable"` on a slide carrying no whole-slide degrade token, and emits a degrade
      log entry **unconditionally** — including when a whole-slide token wins the badge.
- [ ] *(If the gate passes)* The Content Studio badge renders a label distinguishing an ornament
      omission from a whole-slide fallback, and the marker still never leaks into a PPTX export.
- [ ] *(If the gate passes)* The ADR recording the one-port-vs-sibling-port decision and the ornament
      degrade contract is merged.
- [ ] The ADR recording the binding schema-fatigue norm is merged, and the norm is stated in the
      `profile-analysis` spec.
- [ ] The owner reviews one real generation for each of two clients with materially different
      signalled archetypes and records accept/reject in writing.
- [ ] Spec deltas merged for `profile-analysis`, `content-generation` and `content-studio`.
- [ ] All MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`); Strict TDD followed;
      architecture gate green (no provider or EF type crossing into Domain/Application).

## Proposal question round

Revision 1's Q1 (`AccentShape`), Q3 (ornament degrade visibility) and Q4 (schema-fatigue norm) are
answered and now live in Settled decisions. Two remain — both refine the change rather than block it,
and both have stated safe defaults, so specs and design can proceed either way.

1. **Which archetypes ship as *recognised* tokens in v1?** Making the field an open string removed
   the "are these the right three, forever?" risk — a fourth is now a code-only change. What is still
   worth your input is the *starting* set. `"left-aligned"` / `"centered"` / `"lower-third"` were
   chosen from what the flat builder can render coherently, not from your clients' actual feeds. If
   the recurring habits you see in the 3-4 test niches are different (e.g. a consistent top-anchored
   block, or a wide side margin), naming them now costs one preset each in WU-5 instead of a separate
   change later. *Assumption if unanswered: those three, and anything else the model reports is
   preserved in the data and rendered as `left-aligned` until we add a preset for it.*
2. **How many clients must the archetype work for before you call it verified?** The proposal says
   two with materially different habits. If your real onboarding pipeline means a new client's
   archetype cannot be predicted, a higher bar — or an explicit "unrecognised ⇒ `left-aligned` is
   always an acceptable outcome" acceptance — changes how success criterion 7 is written.
   *Assumption if unanswered: two clients, and `left-aligned` is an acceptable outcome for any
   unclassifiable client.*
