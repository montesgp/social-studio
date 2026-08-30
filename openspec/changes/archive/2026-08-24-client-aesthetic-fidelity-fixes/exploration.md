# Exploration: ClientAesthetic (CASE C) Fidelity Fixes

Change name: `client-aesthetic-fidelity-fixes`
Reference incident: client "Lucia Fernandez" (client_id `0186cd86-1029-4f8a-9e50-32f30b418fec`),
profile_analyses id `3f0e272f-3c95-4d0d-8233-a9f327f33ad3`, content_generation_jobs id
`894ccda7-6ec0-4838-bea9-7609a7cf4d56` (local dev DB).

## Current State

### Item 1 — Photo-degrade fidelity

`ContentGenerationJobProcessor.cs` (~L807-831) only short-circuits to the faithful
`BuildFlatColorMarkup` path when `mappedFlatKind == SlideBackgroundKind.FlatColor`. When the
per-slide `BackgroundPattern` says **Photo** but `ClientAestheticImageBackgroundService.BuildCreativeBackgroundsAsync`
failed for that slide (timeout/guardrail exhaustion — the slide never enters
`creativeMap`/`slidesWithGeneratedImage`), execution falls to the `else` branch (~L832-954):
`backgroundPlan.ForSlide(specIndex+1)` resolves through the legacy 3-tier `BackgroundSelection`
(almost certainly `Creative`, since ClientAesthetic clients rarely have brand `BackgroundTemplates`),
and `AiVisualDesigner.ComposeAsync` is invoked.

Root cause confirmed one layer deeper than previously known: `AiVisualDesigner.BuildInputJson`
(`AiVisualDesigner.cs` L221-261) attaches `NoScrimDirective` **only** to `forced-default` and
`references` tiers — the `Creative` tier's `Description` is just "Generate a brand-appropriate
background freely using the brand palette," with no scrim prohibition. Separately,
`BuildSystemPrompt`'s rule 9 (`depthRule`, L403-415) governs decoration: when
`aesthetic.AllowsDecoration` is true — the DEFAULT whenever `ImageTypeRatio` is unmeasured/null
(`ClientAestheticDesignContext.AllowsDecoration`, L57-58) — the model is explicitly told to add
"gradients, blobs, an accent bar... a subtle shadow or glow." This directly explains the observed
`rgba(0,0,0,0.5)` card overlay in job 894ccda7: the Creative-tier prompt both permits decoration by
default AND has no scrim ban, unlike the image-conditioned tiers. The designer does receive
`aesthetic.Palette` (role-ordered) and vision reference images already, and the system prompt does
say "Honour the role-ordered palette... as the client's colours" when `HasPalette` — but there is no
*hard* palette constraint (only a soft instruction), and no signal telling the model "this slide was
supposed to be a photo and the image generation failed — treat it like the client's flat slides
instead."

### Item 2 — FlatColor slides

Verified correct per the diagnosis session; no further investigation performed, out of scope for
changes.

### Item 3 — Typography

More wiring already exists than initially assumed:

- `AestheticExtraction`/`AestheticProfilePersistence`/`ClientAestheticProfile.Typography`
  (`AestheticTypography{Role,FontFamily,Weight,Italic,SizeRatio}`) — contract present, read/write
  round-trip in `AestheticProfilePersistence.cs` (`StoredTypography`) is complete and correctly
  additive-nullable.
- `EfClientAestheticProfileReader.cs` L108 already threads `Typography` into `ClientAestheticProfile`.
- `CompositionDefaults.ResolveTypography(brand, profile, canvasHeight, inferred)` (4-arg overload,
  `CompositionDefaults.cs` L159-199) already **consumes** `profile.Typography`, resolves per-role
  family via `FontFamilyResolution.Resolve` (classifies detected family → nearest bundled OFL font:
  Liberation Sans/Serif/Mono via token matching), and produces weight/italic/size-ratio-driven
  `CompositionTypography`.
- `CarouselVisualComposer.cs` L127-128 **already calls this 4-arg overload** — so `spec.Typography`
  for ClientAesthetic carousels is already client-aware, and both `BuildFlatColorMarkup` and
  `BuildTemplateBackedMarkup` (which read `spec.Typography.HeadingFamily/BodyFamily`) already render
  it. The AI/photo path's `TypographyNormalizer.Normalize(designed.Value.Markup, spec.Typography,
  catalog)` (ContentGenerationJobProcessor.cs L890-892) also already uses this same resolved
  `spec.Typography` as the effective family for generic-only declarations.
- Other composers (`StoryVisualComposer`, `FeedPostVisualComposer`, etc.) still call the 2-arg
  `ResolveTypography(brand, inferred)` overload — profile-blind — but these are out of scope for this
  change (carousel/ClientAesthetic only).

**The one confirmed, real gap**: `MultimodalAestheticStyleExtractor.cs` — zero references to
Typography anywhere (prompt instruction text, `AestheticExtractionDto`, and `Map()` all omit it
entirely). Nothing downstream can receive real typography data because nothing produces it.
Secondary gap: `ClientAestheticDesignContext.cs` has no `Typography` field, and
`AiVisualDesigner.BuildInputJson` sets `AllowedFonts: brand.Typography.ToArray()` (org Brand Kit
fonts, L286) rather than anything client-derived — so the AI designer's *own* font choice (before
normalizer/guardrail run) is guided by the wrong font list, even though the post-hoc
normalizer/guardrail chain already uses the correct `spec.Typography`. `TypographyNormalizer` only
rewrites CSS-generic-only declarations; a named-but-foreign font the model invents passes through
untouched and is then rejected by the guardrail (whole design falls back), so a wrong `AllowedFonts`
hint is a real failure-mode contributor, not just cosmetic.

### Item 4 — Opacity/transparency

Confirmed: only one `<img>` embedding path exists for a real background (`BuildTemplateBackedMarkup`,
`ContentGenerationJobProcessor.cs` ~L1949-1950): `style="position:absolute;inset:0;width:100%;
height:100%;object-fit:cover;"` — no `opacity`, no blend-mode, no alpha treatment anywhere in the
codebase for the photo layer. The only opacity/overlay code in the whole feature is the
resilience-fallback `scrimBox` (`rgba(0,0,0,0.52)` card, only used when `background is null`, i.e.
total AI-designer failure) — unconditional and unrelated to any client signal. There is currently
**no signal anywhere in the extraction schema** (`AestheticExtractionDto`, `AestheticExtraction`,
`AestheticProfilePersistence`, `ClientAestheticProfile`) that would carry "this client blends/fades
their photo backgrounds." This needs new schema end-to-end: extractor prompt + DTO field,
`AestheticExtraction`/`Persistence`/`ClientAestheticProfile` field (parallel to
`BackgroundPattern`/`ImageTypeRatio`), then a render-time consumer — most naturally a
variable-opacity/blend param threaded into `BuildTemplateBackedMarkup`'s `<img>` `style` (and/or
`ClientAestheticDesignContext` for the AI path, since the AI path renders its own background markup
rather than using `BuildTemplateBackedMarkup`).

### Item 5 — ComfyUI generation-time report (investigative only)

- `ComfyUiSettings.DefaultTimeout = 600_000 ms` (10 minutes exactly) — matches the two observed
  `provider_timeout` failures precisely; this is the configured HTTP-flow budget
  (`ComfyUiImageModelClient.cs` L142), not a bug.
- `ComfyUiSettings.DefaultSteps = 20`, `DefaultCfg = 7.0` — both are standard/moderate values for
  SD1.5/SDXL-class checkpoints, not unusually heavy; no evidence of a hardcoded excessive step count.
- `MaxGenerationPixels = 1_310_720` with `LatentDimensionMultiple = 64`
  (`ClientAestheticImageBackgroundService.cs` L82-90) — documented as measured empirically on
  "ComfyUI 0.3.48, `--directml --lowvram --cpu-vae`": 1024x1024 succeeds, 912x1144 and 1080x1352 both
  OOM despite fewer/similar total pixels (divisibility-by-64 matters more than pixel count on this
  backend). This corroborates the size the successful slide-4 generation ran at (~1024x1280, within
  the 1,310,720-px cap).
- `IntegrationExecutionPolicy.cs`: exponential backoff (2s base, capped 30s) between retries — not
  the source of the 20-minute waste; that came from two full `MaxAttempts` timing out at the full
  600s budget each.

**Assessment**: 9m35s for a single ~1.3-Mpx image at 20 steps on `--directml --lowvram --cpu-vae` is
consistent with known DirectML/CPU-VAE performance characteristics (DirectML lacks CUDA-kernel-level
optimization; forcing VAE decode onto CPU is a well-documented ComfyUI slowdown especially combined
with low-VRAM mode). No code-level misconfiguration was found (steps/cfg/resolution are all
reasonable); the slowness is attributable to the backend hardware/execution-mode configuration, which
is outside this codebase's control. **No code fix recommended** — findings are for the record.

## Affected Areas

- `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs`
  — photo-degrade branch selection (~L807-954); img markup builder `BuildTemplateBackedMarkup`
  (~L1891-1989, incl. `object-fit:cover` at L1950); `BuildFlatColorMarkup` (~L2090+).
- `Incoders.Hive.Infrastructure/Social/ContentGeneration/AiVisualDesigner.cs` — `BuildInputJson`
  background-tier guidance (L221-261, NoScrimDirective scoping), `BuildSystemPrompt` decoration rule
  9 (L403-415), `AllowedFonts` source (L286).
- `Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ClientAestheticDesignContext.cs`
  — needs `Typography` field (and possibly an opacity/blend signal field); construction site is
  `ContentGenerationJobProcessor.cs` L469-481.
- `Incoders.Hive.Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs` —
  needs typography extraction instruction + DTO field + `Map()` assignment; needs the new
  opacity/transparency signal similarly (near `BackgroundPattern`/`ImageTypeCounts`, ~L135-280).
- `Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/AestheticProfilePersistence.cs`,
  `ClientAestheticProfile.cs` — new field for the opacity/blend signal, following the exact
  additive-trailing-nullable pattern already used for `Typography`/`BrandMark`/`ImageTypeRatio`.
- `Incoders.Hive.Application/Features/Social/ContentGeneration/Visual/CompositionDefaults.cs`
  (`ResolveTypography` 4-arg overload — already correct, reference implementation for a possible new
  opacity resolver) and `Fonts/FontFamilyResolution.cs`/`FontCatalog.cs` (already correct, no changes
  needed for the fallback strategy itself).
- `Incoders.Hive.Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs`, `ComfyUiSettings.cs`,
  `Incoders.Hive.Infrastructure/Integrations/IntegrationExecutionPolicy.cs`,
  `Incoders.Hive.Application/Features/Social/ContentGeneration/Background/ClientAestheticImageBackgroundService.cs`
  — item 5, read-only for this change.

## Approaches

### Item 1 (photo-degrade fidelity)

1. **Constrain the existing Creative-tier prompt for a failed-Photo slide** — pass a per-call
   flag/description override so the Creative tier's guidance for that specific slide states the
   slide was meant to be photographic, hard-instructs the model to use the extracted
   `ColorPairings`/`Palette` hexes only, and extends `NoScrimDirective` (or an equivalent) to the
   Creative tier for ClientAesthetic jobs. Pros: minimal new surface, reuses existing prompt
   machinery, keeps "creative but grounded" character. Cons: still non-deterministic (LLM can still
   ignore it); needs a way to thread "this was a failed photo slide" into `BuildInputJson`/
   `ComposeAsync`. Effort: Medium.
2. **Deterministic faithful-degrade template** — when a Photo-classified slide's image generation
   fails, route it through `BuildFlatColorMarkup`/`BuildTemplateBackedMarkup` using
   `CompositionDefaults.ResolvePalette` (same as item 2's proven-faithful flat path) instead of
   calling the AI designer at all. Pros: 100% deterministic, guaranteed on-palette, matches the
   confirmed-faithful precedent (item 2). Cons: loses "photo-like" texture entirely — becomes
   visually identical to a flat slide. Effort: Low-Medium.
3. **Hybrid**: keep the AI Creative-tier call but hard-clamp its background layer post-hoc (a
   guardrail/normalizer step validating the background-layer's color declarations against
   `ColorPairings`, rejecting/replacing off-palette results). Pros: deterministic guarantee + keeps
   AI-generated variety. Cons: new validation code, more complex, first CSS-color-extraction-and-recolor
   logic needed. Effort: High.

### Item 3 (typography)

1. Extend `MultimodalAestheticStyleExtractor`: add typography instruction text (per-role
   family-class/weight/italic/size-ratio, mirroring the existing `imageTypeCounts`/`brandMark` prompt
   sections), add `TypographyDto` to `AestheticExtractionDto`, populate `AestheticExtraction.Typography`
   in `Map()`. This alone lights up the already-working downstream chain (persistence → profile →
   `CarouselVisualComposer` → `CompositionDefaults.ResolveTypography` → deterministic markup +
   AI-path normalizer) with zero further plumbing needed on the deterministic side. Effort: Low.
2. Additionally add `Typography` to `ClientAestheticDesignContext` and thread it into
   `AiVisualDesigner.BuildInputJson`'s `AllowedFonts` (client-observed classes, resolved through
   `FontFamilyResolution`, replacing/supplementing brand-kit fonts) so the model's own choice — before
   normalizer/guardrail correction — is already aligned, reducing guardrail-rejection fallbacks.
   Effort: Low-Medium.

No competing approaches surfaced; both are additive, low-risk, and follow established patterns
exactly — recommend doing both as one item.

### Item 4 (opacity/transparency)

1. Add a new extraction signal (e.g. a blended/faded-background boolean + qualitative descriptor)
   captured alongside `BackgroundPattern`/`ImageTypeRatio` in the same extractor prompt pass,
   following the exact additive-nullable pattern of every other Phase-2 field. Persist via
   `AestheticProfilePersistence` → `ClientAestheticProfile` → `ClientAestheticDesignContext`. At
   render time, apply the signal only in `BuildTemplateBackedMarkup`'s `<img>` `style` (conditional
   `opacity:` / gradient-blend overlay) — and, if the AI/Creative path is also touched by item 1,
   thread the same signal into that markup too. Effort: Medium (schema is cheap; the concrete CSS
   treatment is a design question).
2. Simpler MVP: boolean-only signal mapped to one fixed CSS treatment (e.g. a subtle top-to-bottom
   gradient fade to a palette color at a fixed alpha). Lower design risk, less faithful for clients
   with varied treatment intensity. Effort: Low.

### Item 5

No approaches needed — investigative-only deliverable; findings above are final for this item.

## Open Questions for Proposal

1. **Item 1 approach**: deterministic flat-degrade template (option 2, low risk, reuses proven code)
   vs. constrained/grounded AI Creative-tier prompt (option 1, keeps photo-like texture but stays
   non-deterministic). Needs explicit user/product decision — not something explore should resolve
   unilaterally.
2. **Item 4 signal shape**: boolean + qualitative descriptor (recommended — matches what an LLM can
   reliably read off a photo) vs. a numeric opacity ratio (not reliably extractable from an image by
   a vision model). Needs a proposal-level decision before design/tasks can be written.

Item 3 has no open design question and can proceed straight to spec/design. Item 5 needs no further
action beyond inclusion of this report in the change's documentation trail.

## Risks

- Item 1: whichever approach is chosen, the fix only fires for slides that *fail* image generation —
  testing it requires deliberately forcing a ComfyUI failure (or a mocked failure) in integration
  tests.
- Item 1 approach 2 (deterministic degrade) changes visual behavior for a currently-succeeding-sometimes
  AI path — needs product sign-off that "flat-like" is acceptable for a slide the client's pattern
  calls Photo.
- Item 3: the extractor prompt already returns a large multi-part JSON (palette, motifs,
  conceptualMethod, imageTypeCounts, brandMark, typicalSlideCount) — adding typography increases
  prompt/response size and model-compliance risk; should be added with the same defensive parsing
  style already used elsewhere in `Map()`.
- Item 4: no existing analogous "faded background" extraction question exists in the current prompt
  to model after — genuinely new ground, highest risk of vague/unreliable model output; a fallback
  default ("no transparency treatment," i.e. current behavior) must be preserved for
  cold-start/ambiguous cases, matching the codebase's additive-nullable-null-means-legacy-behavior
  convention.
- Item 5: none — investigative only, no code risk.

## Key Learnings

1. `CarouselVisualComposer.cs` already calls the profile-aware 4-arg `CompositionDefaults.ResolveTypography`
   overload, unlike every other visual composer in the codebase.
2. `AiVisualDesigner`'s `NoScrimDirective` is scoped only to `forced-default` and `references`
   background tiers, never to the `Creative` tier used by the photo-degrade fallback.
3. `ClientAestheticDesignContext.AllowsDecoration` defaults to true whenever `ImageTypeRatio` is
   unmeasured, so an unmeasured client gets decorative shapes and overlays by default.
4. `FontFamilyResolution` already classifies any detected font family into a bundled OFL-licensed
   fallback (Liberation Sans/Serif/Mono) via token matching, fully solving the safe-font-fallback
   question.
5. `ComfyUiSettings.DefaultTimeout` is exactly 600000ms, matching the two observed 10-minute
   `provider_timeout` failures precisely, confirming the timeout source.

## Ready for Proposal

Yes — proceed to `sdd-propose`, with two open design questions to resolve with the user first (see
above). Item 3 can go straight to spec/design; item 5 needs no further code action.
