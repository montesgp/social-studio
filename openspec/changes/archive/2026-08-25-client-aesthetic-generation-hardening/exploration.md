# Exploration: Client-Aesthetic Generation Hardening

Change name: `client-aesthetic-generation-hardening`

Follow-up to `client-aesthetic-fidelity-fixes` (archived 2026-08-24). That change fixed the
Photo-slide degrade path, added typography/transparency extraction, and a Content Studio degrade
badge. Testing it against a real client (Lucia Fernandez) surfaced four further, distinct
fidelity gaps — none of them about whether data is *extracted*, but about whether extracted data
is actually *used*, plus one genuine ComfyUI pipeline-maturity gap.

## Current State

### Item 1 — Typography size computed but never consumed (highest-impact, cheapest fix)

`CompositionDefaults.ResolveTypography` (4-arg overload, `CompositionDefaults.cs:169-221`)
correctly computes `HeadingSize`/`BodySize` in pixels from the client's real
`AestheticTypography.SizeRatio` (`SizeFor`/`SizeForCaption`, lines 232-244, 269-278). But
`ContentGenerationJobProcessor.cs`'s markup builders never read `spec.Typography.HeadingSize` or
`.BodySize` (zero references, confirmed by grep). Only `CaptionSize` (CTA/signature role) is
wired through. Headline/subtitle sizes are hardcoded pixel literals instead:

- `BuildStoryTemplateSlots`: `font-size:88px` (headline, ~L2126), `36px` (subtitle, ~L2133), `30px`
  (body, ~L2139), `25px` (signature, ~L2151 — hardcoded, not from `CaptionSize` either).
- `BuildCarouselTemplateSlots`: `74px` (title, ~L2175), `38px` (subtitle, ~L2182).
- `BuildFlatCarouselSlots`: `74px` (title, ~L2281), `38px` (subtitle, ~L2287).

`AiVisualDesigner.BuildSystemPrompt` (~L473) also tells the AI designer a fixed generic
instruction — "the headline is HUGE (about 7-13% of canvas height)" — regardless of the client's
actual measured `SizeRatio`.

**This looks like an incomplete follow-up to whatever change added `CaptionSize`**: the resolver
was built correctly for all three roles, but only one of three call sites was ever wired.

### Item 2 — Text color on AI-photo slides hardcoded to white, ignores `ColorPairings`

For a slide with a real generated photo background (`ForcedDefault`, `ContentGenerationJobProcessor.cs`
~L2007-2014 — "an ENFORCED background... ALWAYS wins over the AI designer's markup"),
`BuildTemplateBackedMarkup` → `BuildStoryTemplateSlots`/`BuildCarouselTemplateSlots` hardcode
`color:#FFFFFF` + `text-shadow` for every text role (headline, subtitle, body, cta) and
`color:#E5E7EB` for signature. None of these read `aestheticProfile.ColorPairings` — the client's
real, WCAG-validated text/background color combinations, already extracted and already used
correctly by the deterministic flat path (`BuildFlatCarouselSlots`'s `textColor` parameter,
~L2251-2298).

Separately, `AiVisualDesigner.BuildSystemPrompt` never references `ColorPairings` at all (zero
hits) — it only sees the generic brand palette (`brand.ColorPalette`), not the client's specific
observed text-on-photo choices. Moot in practice, though, since its markup is discarded on this
exact path anyway (the enforced background always wins).

### Item 3 — ComfyUI provider quality: 3 concrete, fixable gaps + 1 bigger one

Diagnosed from a real generation: photorealistic faces/hands rendered deformed, faces duplicated
within one image.

1. **`BackgroundOnly` never enforced on the ComfyUI adapter.** `ImageGenerationRequest.BackgroundOnly`
   is set `true` on every creative-slide request (`ClientAestheticImageBackgroundService.cs`
   ~L360-372) and is meant to inject a "generate ONLY the background; never render text, logos,
   @handles, signatures [or by extension: people]" directive — implemented in
   `OpenAiImageModelClient.cs` (`BackgroundOnlyDirective`, ~L36-42, 58-61, 120-121). **`ComfyUiImageModelClient.cs`
   never reads `request.BackgroundOnly` at all** (zero references) — the prompt reaches KSampler's
   positive `CLIPTextEncode` verbatim (~L182), no directive injected. This is a straight parity bug
   between the two provider adapters.
2. **No face/hand restoration node in the ComfyUI workflow.** `DefaultComfyUiWorkflowTemplate.json`
   has exactly 8 nodes: `CheckpointLoaderSimple`, 2× `CLIPTextEncode`, `LoadImage`,
   `IPAdapterUnifiedLoader`, `IPAdapterAdvanced`, `EmptyLatentImage`, `KSampler`, `VAEDecode`,
   `SaveImage`. No `FaceDetailer`/`ADetailer`/`UltralyticsDetectorProvider`/hand-refiner/impainting
   node of any kind.
3. **Negative prompt lacks anatomy terms.** `ComfyUiImageModelClient.cs`'s `DefaultNegativePrompt`
   (~L47-49) = `"text, letters, words, typography, watermark, signature, caption, logo, label,
   subtitles, writing, font, handwriting, numbers, low quality, blurry, deformed, distorted"` — its
   actual purpose (per its own doc comment) is suppressing baked-in text/watermarks from the
   IP-Adapter reference, not anatomy defects. "Deformed"/"distorted" are present only incidentally;
   no "extra limbs", "bad hands", "extra fingers", "duplicate", "cloned face" etc.
4. **No default checkpoint fallback.** `ComfyUiSettings.DefaultCheckpoint` has no hardcoded value —
   it's read from the org's `default_checkpoint` credential key with no fallback; generation fails
   with `AiModelRequired` if unset. `CuratedDefaultCheckpoints` is catalog-UI-only, not a generation
   default. Whatever checkpoint an org has configured may or may not be tuned for photorealistic
   people — this is a genuine, harder-to-fix model-quality/curation question, not a code defect.

Resolution size (1024x1280, 4:5) is a moderate portrait aspect, not the kind of extreme
wide/tall ratio known to trigger SD-family subject duplication — not implicated by the evidence.

### Item 4 — Client's real signature/brand mark still not reproduced (schema gap, not a wiring gap)

Already known from the prior change (explicitly out of scope there). Confirmed still current:
`BrandMarkDetection` (`BrandMarkDetection.cs:25-28`) carries only `Present`/`PositionHint`/`ColorHex`.
The extraction prompt (`MultimodalAestheticStyleExtractor.cs` ~L163-174) never asks for the mark's
actual text content, nor whether it's a text signature vs. an icon/symbol. `BuildBrandMarkSlot`
(`ContentGenerationJobProcessor.cs` ~L2309-2343) renders an **empty** `<div>` — a solid color chip,
`string.Empty` inner content — regardless of what the client's real mark actually is.

Unlike items 1-3, this is not a wiring gap: the data was never captured in the first place. Needs
schema extension (extractor prompt + DTO + `BrandMarkDetection`) before any rendering change is
possible.

## Affected Areas

- `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs`
  — `BuildStoryTemplateSlots`, `BuildCarouselTemplateSlots`, `BuildFlatCarouselSlots`,
  `BuildBrandMarkSlot` (typography sizes, text color, brand-mark rendering).
- `Incoders.Hive.Infrastructure/Social/ContentGeneration/AiVisualDesigner.cs` — `BuildSystemPrompt`
  (hardcoded size range, no `ColorPairings` reference).
- `Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs` — `BackgroundOnly` not
  read; `DefaultNegativePrompt`.
- `Incoders.Hive.Infrastructure/Ai/Providers/DefaultComfyUiWorkflowTemplate.json` — no
  face/hand-detailer node (larger change, needs a ComfyUI-backend-side custom-node dependency check
  before committing to an approach).
- `Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/BrandMarkDetection.cs`,
  `Incoders.Hive.Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs` —
  brand-mark schema extension.

## Open Questions for Proposal

1. **Item 3.4 (checkpoint curation)** — is choosing/recommending a specific photorealistic-tuned
   checkpoint in scope for this change, or purely an ops/config concern to hand to the owner
   separately? Leaning: out of scope for code, but the proposal should say so explicitly rather than
   silently drop it.
2. **Item 3.2 (face/hand detailer node)** — this is the one item here that isn't a pure code fix:
   it needs new ComfyUI graph nodes, which may require custom-node packages installed on the
   ComfyUI backend itself (outside this repo's control). Proposal should treat this as its own,
   higher-risk work unit with an explicit infra-dependency caveat, separate from the cheap items.
3. **Item 4 (signature)** — text-signature rendering (render the extracted text via the resolved
   typography) is straightforward once captured; icon/logo-type marks are a materially bigger
   scope (crop + asset storage + re-render) — proposal should likely scope text-signature only for
   v1, with icon-type explicitly deferred.

## Risks

- Items 1 and 2 are the safest: wiring already-computed/already-extracted values into existing
  call sites, same pattern as `CaptionSize`/`BuildFlatCarouselSlots`'s `textColor` already prove
  works. Low regression risk.
- Item 3.1 (`BackgroundOnly` on ComfyUI) changes prompt content sent to a live model — needs a
  real-generation check that background-only prompts still produce usable results (not empty/blank
  images) before shipping.
- Item 3.3 (negative prompt) is pure prompt-text addition — very low risk, but effectiveness can't
  be verified without a real ComfyUI run (this repo has no way to score image quality
  automatically).
- Item 3.2 (detailer node) has real infra risk: if the required custom node isn't installed on the
  target ComfyUI instance, the workflow will fail outright. Needs a live-environment check before
  implementation, not just a code change.
- Item 4 changes the extraction JSON contract again (third addition this quarter, after typography
  and transparency) — same "large multi-part JSON, model non-compliance risk" pattern flagged in
  the prior change; needs defensive parsing per the established convention.

## Ready for Proposal

Yes. Recommend structuring as: cheap/safe wiring items (1, 2, 3.1, 3.3) as one fast-moving track;
item 4 (signature schema + render) as a second track; item 3.2 (detailer node) flagged as
higher-risk/infra-dependent, likely its own investigative-first work unit before committing to an
implementation.
