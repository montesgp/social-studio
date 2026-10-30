# Exploration: content-layout-and-image-fidelity

Change name: `content-layout-and-image-fidelity`

Follow-up to `client-aesthetic-generation-hardening` (2026-08-25) and
`client-aesthetic-visual-sophistication` (2026-08-26). A real generation for client Lucia Fernandez
(carousel about "ronquidos"/snoring) confirmed the prior fixes work (real signature, real
typography), and surfaced four further, distinct issues.

## Current State

### Item 1 — ComfyUI images unrelated to slide content topic

`ImagePromptWriter.BuildInput` (`SocialStudio.Application/Features/Social/ContentGeneration/Background/ImagePromptWriter.cs`,
~L140-216) has two branches:

- **Method-gated branch** (~L150-169, fires only when `ClientAestheticProfile.ConceptualMethod` is
  non-null and not `Unknown`): explicitly instructs "The concept MUST depict the slide message" and
  requires a literal/metaphorical scene of "the message subjects."
- **Cold-start/default branch** (~L172-181, used whenever `ConceptualMethod` is null/`Unknown` — the
  common case for a client without a fully-extracted art-direction profile): "Write ONE concise
  text-to-image prompt for a single carousel slide BACKGROUND image in this client's established
  visual style... Emulate the client's real aesthetic (composition, palette, mood, motifs)..." —
  **never instructs the writer-model to depict the slide's actual topic/subject matter.**
  `request.SlideCopySummary` is passed as a raw JSON data field (~L209) but the instruction text
  never tells the model to use it to determine the visual subject.

`BuildClientAesthetic` (~L222-259) then adds `styleDescriptors`/`motifs`/`backgroundDescriptors`/
`voiceTone`/`palette` — the token budget in the cold-start branch is dominated by aesthetic/style
data, with topical content only leaking through if the writer-model chooses to read
`slideCopySummary` unprompted. For a "ronquidos" (snoring) slide, this branch produces a prompt like
"warm cream-toned clinical still life, soft diffused light" with no mention of a person
sleeping/snoring — matching the observed symptom exactly.

### Item 2 — ComfyUI images with baked-in, non-editable text

`IImageGuardrailValidator`'s doc comment describes validating "background-only" (no foreground
text/logos/handles/signatures). The only registered implementation,
`DegenerateImageGuardrailValidator.cs` (`SocialStudio.Infrastructure/Social/ContentGeneration/`),
does **not** do this — its own doc comment (~L20-24) states explicitly: *"this validator does NOT
yet implement the foreground/background-only inspection described in the port's doc (text/logo
detection needs OCR/ML) — that remains a future concern."* It only rejects near-black/near-flat-color
frames via pixel-luminance sampling (~L26-111).

The only actual defense against baked-in text is `ComfyUiImageModelClient.DefaultNegativePrompt`
(`SocialStudio.Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs`, ~L49-53) — a negative-prompt
string, a soft steering signal with no guaranteed effect. **There is no OCR/ML pass anywhere in the
codebase** to catch a violation after generation.

### Item 3 — Rendered slide text overlaps (headline into subtitle)

Confirmed via a real generated carousel: headline slot rendered at `font-size:149px` (derived from
the client's real `SizeRatio: 0.11` × canvas height 1350px — the newly-shipped measured-typography
wiring working exactly as designed), positioned `top:255px` / `min-height:260px`, containing a full
sentence long enough to wrap 2-3 lines at that font size. The subtitle slot below it sits at a
**hardcoded fixed `top:575px`** (`ContentGenerationJobProcessor.cs`'s `BuildStoryTemplateSlots`
~L2241; `BuildCarouselTemplateSlots` has the identical pattern, title `top:235px`/`min-height:230px`,
subtitle hardcoded `top:540px`, ~L2297/L2304) — it does not reflow based on the headline's actual
rendered height. **No slot's `top` offset anywhere in this file is ever computed from another slot's
measured/rendered height** — every position is a fixed pixel literal.

`BuildTemplateBackedMarkup` (~L2049) is the path that renders a real-photo carousel/story slide
(dispatches to `BuildCarouselTemplateSlots`/`BuildStoryTemplateSlots`, fires when
`enforcedBackground is not null`, ~L1008-1024 — exactly the ComfyUI-generated-image case).
`VisualGuardrailValidator.ValidateOverflow` (`SocialStudio.Application/Features/Social/ContentGeneration/Visual/VisualGuardrailValidator.cs`,
~L552-610) only checks a slot's text against its OWN `data-max-lines` via a static char-count
heuristic — it has no cross-slot geometry comparison and cannot detect this overlap. Worse: **this
validator is never invoked on `BuildTemplateBackedMarkup`'s output at all** — `_guardrails.Validate`
is called only once (~L957), against the AI *designer's* markup, before the priority resolution that
picks enforced-background template markup over AI markup (~L977-1035). The real-photo path bypasses
this weak check entirely.

No font-size clamping/auto-fit logic exists anywhere in `CompositionDefaults.ResolveTypography`/
`SizeFor`/`SizeForCaption` — `MaxSizeRatio = 0.30` only guards against an implausible *measurement*
(bad extraction), not against a long *copy string* at a legitimate ratio. `measuredHeadingSize` is
applied completely rigidly regardless of headline text length.

### Item 4 — Content Studio's text-editing boxes look "much bigger than the actual text"

**Same root cause as item 3, not a separate bug.** `SlotGeometryEntry`
(`CompositionSpecEnvelope.cs`, ~L169-183) is captured via `el.getBoundingClientRect()`
(`HtmlChromiumContentRenderer.cs`'s `SlotGeometryScript`, ~L51-66) on the styled `[data-slot]`
element — this returns the full rendered CSS box, including the `min-height` fixed literal from the
markup builders (e.g. `min-height:260px` headline, `min-height:122px`/`min-height:310px` subtitle),
even when the actual glyph-ink of the text is far shorter. It is not a tight text-ink bounding box.
`content-studio-panel.tsx`'s `Envelope.slotGeometry` loads this backend-provided box directly and
never re-tightens it. **Fixing item 3's oversized/non-reflowing literals fixes this complaint too.**

### Item 5 — No alignment/smart-guides in Content Studio (new feature, not a bug)

Confirmed absent: no snapping/alignment-guide mechanism exists in `content-studio-panel.tsx` (grep
for `snap`/`guide`/`alignment` finds only an unrelated `Snapshot` undo-history type). Drag/resize is
handled by raw `onPointerDown`/`onPointerMove` handlers on the shadow-DOM canvas (~L2843, L2873, plus
per-element handlers at ~L3241/3275/3288/3531/3560/3572). A smart-guides feature would plug into
`onPointerMove` — computing candidate snap targets (canvas center, other slots' edges/centers)
against the dragged element's live position, then conditionally rendering guide lines in the same
render pass.

## Affected Areas

- `SocialStudio.Application/Features/Social/ContentGeneration/Background/ImagePromptWriter.cs` —
  cold-start prompt branch (item 1).
- `SocialStudio.Application/Abstractions/Social/ContentGeneration/IImageGuardrailValidator.cs`,
  `SocialStudio.Infrastructure/Social/ContentGeneration/DegenerateImageGuardrailValidator.cs` — no
  text/logo detection exists (item 2).
- `SocialStudio.Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs` — negative prompt is the
  only (soft) defense (item 2).
- `SocialStudio.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs`
  — `BuildTemplateBackedMarkup`, `BuildStoryTemplateSlots`, `BuildCarouselTemplateSlots` — fixed
  absolute pixel positions, no reflow (item 3); `_guardrails.Validate` never runs on this path's
  output (item 3).
- `SocialStudio.Application/Features/Social/ContentGeneration/Visual/VisualGuardrailValidator.cs` —
  `ValidateOverflow` is single-slot-only, no cross-slot overlap check (item 3).
- `SocialStudio.Application/Features/Social/ContentGeneration/Visual/CompositionDefaults.cs` —
  `ResolveTypography`/`SizeFor`/`SizeForCaption` — no auto-fit/clamping against copy length.
- `SocialStudio.Application/Features/Social/ContentGeneration/Pipeline/CompositionSpecEnvelope.cs`,
  `SocialStudio.Infrastructure/Rendering/HtmlChromiumContentRenderer.cs` — `SlotGeometryEntry`
  capture via `getBoundingClientRect()` (item 4, shared root cause with item 3).
- `apps/web/components/social/content-studio-panel.tsx` — drag/resize handlers, would host item 5.

## Open Questions for Proposal

1. **Item 3's fix mechanism** — two real options: (a) a second measurement pass (render once, read
   each slot's actual Chromium-measured height, recompute subsequent slots' `top` offsets, render
   again) — more accurate, doubles render cost per slide; (b) switch stacked text slots from absolute
   pixel positioning to CSS flow (flexbox/normal document flow within the content layer) — cheaper
   at render time, but is a bigger structural change to markup that Content Studio's editor,
   `PptxExportAdapter`, and every existing markup-parsing consumer currently assume is
   absolutely-positioned. Needs an explicit decision — this is the architecturally biggest item in
   the change.
2. **Item 2's OCR guardrail** — is a real OCR/ML text-detection pass in scope for this change, or
   should it follow the "investigation-first, gate before implementing" pattern already established
   for the ComfyUI face/hand-detailer (deferred in the prior change) and the ornament bounding-box
   crop (cut on a failed reliability gate)? OCR adds a new runtime dependency and per-image latency
   cost on an already-slow ComfyUI path.
3. **Item 5's scope** — a full Figma/Canva-style smart-guides system is substantial frontend work;
   proposal should scope a v1 (e.g. canvas-center guides only, vs. full edge/center snapping against
   every sibling slot).

## Risks

- Item 3's reflow fix is the highest-risk/highest-value item — touches the same rendering path three
  other changes this quarter already modified (typography sizing, text-color, brand-mark, divider/
  archetype decoration); needs careful regression coverage against all of it, including the
  byte-identity golden from the prior change.
- Item 1's fix is cheap but its effectiveness (does the writer-model actually produce
  topically-relevant prompts more often) is not automatically verifiable without live generations —
  same "can't measure offline" pattern as before.
- Item 2 (OCR) risk: false positives/negatives are both costly — a false positive discards a good
  image (wasted ComfyUI time, already the slowest part of the pipeline); a false negative ships
  exactly the defect being fixed. Needs a deliberate accuracy/latency tradeoff decision.
- Schema-fatigue norm (ADR 0015, previous change) does not apply here — none of these items touch
  the aesthetic extraction schema.

## Ready for Proposal

Yes. Recommend structuring as: item 1 (prompt topical-fidelity) as a fast, cheap track; item 3+4
(reflow layout, shared root cause) as the core architectural track needing the mechanism decision
above; item 2 (OCR guardrail) as its own track, likely investigation-first given the pattern
established twice already this quarter; item 5 (alignment guides) as an independent frontend track
with no backend dependency, could ship in parallel with any of the above.
