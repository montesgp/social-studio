# Proposal: Client-Aesthetic Generation Hardening

## Change ID
`client-aesthetic-generation-hardening`

## Status
Proposal — revision 2. Builds on `exploration.md` (verified codebase map with file/line citations).
Follow-up to `client-aesthetic-fidelity-fixes` (archived 2026-08-24). The proposal question round is
**closed**: all three decisions are settled below. Specs and design next.

## Settled decisions

| # | Decision | Effect on this proposal |
|---|---|---|
| 1 | **Detailer node (item 3.2) is investigation-only.** WU-5 ships a written feasibility report, not a workflow-graph change. | Confirmed as originally scoped. No change. |
| 2 | **Checkpoint curation (item 3.4) stays out of scope.** An owner-side ops/config decision; a repo-level default would silently override org configuration. | Confirmed as originally scoped. Remains in Out of Scope, handed to the owner separately. |
| 3 | **The brand mark MUST support BOTH text and icon/symbol marks.** Owner's reasoning: new clients onboard continuously and their mark type cannot be predicted, so the pipeline must work for any case. | **Rejects** the previously recommended "text-only v1, icon deferred" framing. Icon marks move from non-goal to in-scope; WU-6 and WU-7 are re-scoped and split (see Delivery). |

## Intent

**Business problem.** The prior change shipped and the owner ran a real generation against a real
client (Lucia Fernandez). The output is still visibly not that client's aesthetic. The owner's own
framing: *"debemos lograr tener un pipeline mucho más sofisticado. El que tenemos ahora entrega
resultados muy pobres en comparación a la estética del cliente"* — and, pointedly, the owner
compared the pipeline's output unfavourably to simply asking ChatGPT for an image. That comparison
is the real bar. A content pipeline that produces something a client would not post is not a
partially-working pipeline; it is an unusable one, because every generated asset still needs a
human to redo it by hand. The value proposition of the whole Content Generation capability rests on
the generated asset being *recognisably the client's*.

**Current-state gap.** The gap is no longer extraction. Across items 1, 2 and 4 below the pipeline
already *knows* the client's aesthetic and then does not use it:

| Gap | Nature |
|---|---|
| 1. Typography sizes | `HeadingSize`/`BodySize` are correctly computed from the client's measured `SizeRatio` and then read by **nobody** — three markup builders use hardcoded pixel literals. `CaptionSize` was wired; the other two roles never were. |
| 2. Text colour on AI-photo slides | The client's WCAG-validated `ColorPairings` are extracted and used correctly on the deterministic flat path, then ignored on the enforced-photo path, which hardcodes white + text-shadow for every role. |
| 3. ComfyUI provider quality | Faces/hands render deformed and duplicated. `BackgroundOnly` is set on every creative-slide request and silently dropped by the ComfyUI adapter (the OpenAI adapter honours it). The negative prompt has no anatomy terms. The workflow graph has no detailer node. |
| 4. Signature / brand mark | Not a wiring gap: the extractor never captures the mark's content or its kind, so render emits an empty coloured chip no matter what the client's real mark is — text signature or icon alike. |

**Outcome.** A generated slide reads as *this client's* slide: their measured heading/body scale,
their observed text-on-background colour choices, their actual mark — rendered as real text when the
mark is a signature and as the client's real symbol when it is an icon — on a background that is a
background: no accidental people, no rendered text, no deformed hands.

**Framing note.** "Sophisticated pipeline" here does not mean a new architecture. Items 1 and 2 are
the sophistication already paid for and not yet delivered. Item 3 is the one place where real new
generation capability is required, and the only place with a dependency this repository does not
control. Item 4, under decision 3, is now the largest genuinely-new build in this change.

## Scope

### In Scope

1. **Typography size consumption.** `BuildStoryTemplateSlots`, `BuildCarouselTemplateSlots` and
   `BuildFlatCarouselSlots` read `spec.Typography.HeadingSize`/`.BodySize` (and `CaptionSize` for
   the signature role) instead of the hardcoded `88px`/`74px`/`38px`/`36px`/`30px`/`25px` literals.
   Existing literals become the fallback when the resolver yields nothing.
2. **`AiVisualDesigner` prompt honesty.** `BuildSystemPrompt`'s fixed "headline is HUGE (about 7-13%
   of canvas height)" line is derived from the client's actual resolved sizes rather than asserted.
3. **Text colour on the enforced-photo path.** Story/carousel template slots resolve text colour
   from `aestheticProfile.ColorPairings` — the same pattern `BuildFlatCarouselSlots`'s `textColor`
   parameter already proves — with the current white + shadow treatment retained as fallback when
   no pairing resolves. Contrast-guard behaviour is a spec-phase requirement, not an afterthought.
4. **ComfyUI `BackgroundOnly` parity (item 3.1).** `ComfyUiImageModelClient` reads
   `request.BackgroundOnly` and injects the background-only directive into the positive prompt,
   matching `OpenAiImageModelClient.BackgroundOnlyDirective`. Directive text is shared, not
   duplicated per adapter.
5. **Anatomy terms in the ComfyUI negative prompt (item 3.3).** Extend `DefaultNegativePrompt` with
   anatomy-defect terms (extra limbs, extra fingers, bad hands, malformed hands, duplicate face,
   cloned face, mutated). Its existing text-suppression purpose is preserved and documented.
6. **Face/hand detailer feasibility (item 3.2) — investigation only (settled decision 1).**
   A written report answering: which detailer node family is viable, whether it requires a
   custom-node package on the ComfyUI backend, how the workflow behaves when that node is absent,
   and whether a graceful-degrade graph is possible. Ships a report, not a graph change.
7. **Brand-mark schema extension — both mark types (settled decision 3).**
   `BrandMarkDetection`/`BrandMarkDto` gain, all additive and nullable:
   - `MarkType` discriminator — `"text" | "icon" | "unknown"`; anything unrecognised maps to
     `"unknown"` and renders as today.
   - `Text` — the literal signature string, meaningful only for `MarkType: "text"`.
   - `SourceImagePath` + `BoundingBox` — where the clearest occurrence of an icon mark sits. The
     extractor already indexes its input images ("Image 0", "Image 1", …) for caption annotation; it
     returns a source **index**, and the extractor — the only component that knows the ordering —
     resolves that index to the archived `ProfilePostImageRef.StoragePath` before returning, so no
     downstream consumer depends on prompt ordering. `BoundingBox` is normalised (0..1) with
     validation and a minimum-area guard.
   Parsing is defensive per the `FFS-PA-02` convention: missing/unknown/malformed → null, never an
   exception; older persisted profiles keep deserialising.
8. **Text-mark rendering.** `BuildBrandMarkSlot` renders the extracted signature text through the
   client's already-resolved caption typography when `MarkType` is `"text"`.
9. **Icon-mark asset pipeline.** A new Application port (working name `IBrandMarkAssetExtractor`)
   crops the mark's bounding box out of the client's already-archived reference image and persists
   the crop via the existing `IGeneratedAssetStorage`. Infrastructure implements it with **SkiaSharp,
   already referenced** by `Incoders.Hive.Infrastructure.csproj` (3.119.0) — no new package. The
   archiver stores reference images **as-downloaded, not thumbnailed**
   (`SupabasePostMediaArchiver.cs:15`), so crop resolution is usable. This is deliberately a *crop*,
   not live background removal or regeneration.
10. **Icon-mark rendering + reference resolution.** `BuildBrandMarkSlot` emits an `<img>` pointing at
    the stored crop instead of the empty coloured `<div>`. **Knock-on requirement found during this
    revision**: `StudioVisualMarkupResolver` today inlines *only* the envelope's
    `BackgroundImageRef` literal, so a brand-mark storage path would reach the browser unresolved.
    The resolver — and the `PptxExportAdapter` export path, which reads the same refs — must resolve
    the brand-mark ref too. Without this the icon renders as a broken image in Studio and in export.
11. **Degrade path for icon marks.** When `MarkType` is `"icon"` but the bounding box is absent,
    invalid, implausibly small, or the crop cannot be produced or stored, the renderer falls back to
    today's `PositionHint` + `ColorHex` coloured chip. Approximate-but-consistent beats a wrong or
    broken image; the degrade is logged, not silent.
12. **Spec and documentation sync** for content-generation, comfyui-image-provider,
    profile-analysis and content-studio-background-rendering, plus the ADR and PRD §11 amendment
    required by decision 3 (see below).

### Out of Scope — explicit non-goals

| Deferred | Why now is wrong |
|---|---|
| **Checkpoint model curation / recommendation (item 3.4)** — settled decision 2 | An ops/config concern, not a code defect. `ComfyUiSettings.DefaultCheckpoint` reads an org credential with no fallback by design. A repo-level default would silently override org configuration. Handed to the owner separately. |
| **Background removal / matting on the icon crop** | The crop is a rectangular region of a real post image and may carry the surrounding background. Matting is a separate capability with its own model dependency. v1 ships the honest rectangular crop; transparency is a follow-up. |
| **Regenerating or vectorising the client's logo** | Reproducing a mark the pipeline invented is worse than an approximate one; only real captured pixels are used. |
| **Shipping a detailer node into `DefaultComfyUiWorkflowTemplate.json`** | Depends on custom-node packages installed on a ComfyUI instance this repo does not control; a missing node fails the whole workflow. Gated behind the WU-5 report. |
| Automated image-quality scoring | This repository has no way to score generated-image quality; item 3 effectiveness is verified by owner-run real generations only. |
| Re-architecting the enforced-background precedence rule | Unchanged exactly as shipped. |
| Resolution / aspect-ratio changes | 1024x1280 (4:5) is not implicated by the duplication evidence. |
| New extraction of typography or colour data | Already extracted correctly. |

## Capabilities

### New Capabilities
None. The icon pipeline introduces a new *port*, not a new capability — the behaviour belongs to
`content-generation`.

### Modified Capabilities (additive deltas)

- `content-generation`: markup builders MUST consume resolved typography sizes for heading/body/
  caption roles; the enforced-photo text path MUST resolve colour from `ColorPairings` with the
  white+shadow fallback; `BuildBrandMarkSlot` MUST render extracted signature text for `text` marks
  and a stored crop `<img>` for `icon` marks, with the specified degrade to the coloured chip.
- `comfyui-image-provider`: the adapter MUST honour `ImageGenerationRequest.BackgroundOnly`; the
  default negative prompt MUST include anatomy-defect suppression alongside text suppression.
- `profile-analysis`: `BrandMarkDetection` gains additive nullable `MarkType`, `Text`,
  `SourceImagePath` and `BoundingBox` fields with defensive parsing, following the `FFS-PA-02`
  additive-nullable precedent (no EF migration — the profile is JSON-serialised).
- `content-studio-background-rendering`: markup reference resolution MUST cover the brand-mark asset
  ref, not only `BackgroundImageRef`, in both the Studio resolver and the PPTX export path.

## Approach

Replicate patterns this repository has already proven wherever one exists. Items 1–5 and 8 each have
an existing correct call site to copy. Items 9–10 are the genuinely new build.

| Piece | Layer | Path |
|---|---|---|
| Typography size consumption in 3 markup builders; text- and icon-mark rendering in `BuildBrandMarkSlot` | Application | `Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` |
| Text-colour resolution on story/carousel template slots | Application | same file (`BuildStoryTemplateSlots`, `BuildCarouselTemplateSlots`) |
| `BrandMarkDetection` mark-type / text / source-path / bounding-box fields | Application (Shared) | `Application/Features/Social/ContentGeneration/Shared/BrandMarkDetection.cs` |
| `IBrandMarkAssetExtractor` port | Application (Abstractions) | `Application/Abstractions/Social/ContentGeneration/` |
| SkiaSharp crop + store adapter | Infrastructure | new file alongside `Infrastructure/Social/ContentGeneration/` |
| Brand-mark ref resolution (Studio + export) | Application / Infrastructure | `Application/Features/Social/ContentStudio/Shared/StudioVisualMarkupResolver.cs`, `Infrastructure/Rendering/PptxExportAdapter.cs` |
| Size-derived prompt line | Infrastructure | `Infrastructure/Social/ContentGeneration/AiVisualDesigner.cs` |
| `BackgroundOnly` directive + anatomy negative-prompt terms | Infrastructure | `Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs` |
| Shared background-only directive text | Infrastructure | extracted from `Infrastructure/Ai/Providers/OpenAiImageModelClient.cs` |
| Extractor prompt: mark type, text, source index, bounding box; index→`StoragePath` resolution; defensive parsing | Infrastructure | `Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs` |
| Detailer feasibility report | Docs | `openspec/changes/client-aesthetic-generation-hardening/comfyui-detailer-feasibility-report.md` |

### Reused vs. new — concretely

| Reused unchanged | New |
|---|---|
| `CompositionDefaults.ResolveTypography` / `SizeFor` / `SizeForCaption` | Nothing; only the call sites change |
| `CaptionSize` wiring pattern (the one role already wired) | Same pattern applied to heading and body |
| `BuildFlatCarouselSlots`'s `textColor` parameter and pairing resolution | Same resolution on the enforced-photo template slots |
| `OpenAiImageModelClient.BackgroundOnlyDirective` semantics | Directive text extracted to one shared location so a third adapter cannot drift |
| `FFS-PA-02` additive-nullable + defensive-parse convention | Four additive nullable fields on `BrandMarkDetection` |
| `IGeneratedAssetStorage.PutAssetAsync` / `OpenAsync`, `StoredAssetKey` | A brand-mark relative path under the existing analysis folder |
| `ProfilePostImageRef` archived reference images (stored as-downloaded) | Read back as the crop source |
| SkiaSharp 3.119.0, already referenced by Infrastructure | The crop operation itself, behind a new port |
| `StudioVisualMarkupResolver`'s path→data-URI resolution mechanism | Extended to a second ref field (brand mark) |
| Enforced-background precedence rule | Unchanged |

### Ordering rationale

Items 1–5 wire already-available values into existing call sites and carry low regression risk; they
ship first as a fast track. The brand-mark track ships second, schema before render, text-mark render
before icon-mark render — text is the cheap, low-risk half and must not be held hostage to the icon
half's bounding-box uncertainty. The detailer investigation is independent and blocks nothing.

## ADR determination

`openspec/config.yaml` sets `require_adr_for_architecture_changes: true`.

**Revision-2 change: an ADR is now REQUIRED.** Revision 1 stated that no port was introduced. Under
settled decision 3 that is no longer true — the icon track introduces a new Application port
(`IBrandMarkAssetExtractor`) with an Infrastructure adapter that performs image manipulation, a
processing category this codebase has not previously placed behind a port. A short ADR covering the
port boundary, the crop-not-matting decision, and the degrade contract ships with WU-7b.

Items 1–5 and the text-mark half remain ADR-free: no bounded area, no persistence model, no provider
category, no credential type. **Conditional trigger unchanged**: if the WU-5 report concludes a
detailer node should ship with a hard ComfyUI custom-node dependency, *that* follow-up needs its own
ADR. This change does not pre-approve it.

## PRD Traceability

`openspec/config.yaml` sets `require_prd_traceability: true`.

| PRD anchor | This change | Honesty note |
|---|---|---|
| §1 Product Summary / §3 Goals — automated content production | Directly serves it: output the client can actually post | — |
| §9 Acceptance Criteria | Fidelity of generated assets to the client's aesthetic | — |
| §10 Content Generation — Format Taxonomy | Story and carousel are the two affected builders | — |
| §11 Profile Analysis (Mundo 2) | Brand-mark extraction now captures mark type, content, and an icon's location and derived asset | The section describes analysis, not asset derivation from analysed images |

**Revision-2 change: a §11 amendment IS required.** Revision 1 flagged this as conditional on
decision 3 resolving toward icon support. It did. Deriving and storing a durable brand-mark asset
from analysed reference imagery materially expands the Profile Analysis contract; the amendment
ships in WU-8.

## Delivery forecast — sequential commits to `dev`

Direct sequential commits to `dev` (no PRs, no chained PRs), tracked via a GitHub issue.
Budget: **800 changed lines per work unit**. Estimates are `additions + deletions`, Strict TDD
included (tests ≈ 1:1). WU-6 and WU-7 are **split** relative to revision 1 — the same pattern used
for the prior change's WU-1/WU-2 split — because decision 3 pushed the combined brand-mark unit well
past 800 lines.

| # | Work unit | Est. lines | Over budget? |
|---|---|---|---|
| WU-1 | Typography size consumption in the 3 markup builders + fallback behaviour | ~380 | No |
| WU-2 | `AiVisualDesigner` size-derived prompt line | ~140 | No |
| WU-3 | Text-colour resolution from `ColorPairings` on the enforced-photo path + fallback + contrast guard | ~420 | No |
| WU-4 | ComfyUI `BackgroundOnly` parity (shared directive) + anatomy negative-prompt terms | ~300 | No |
| WU-5 | Detailer feasibility report (investigation only, no production code) | ~200 | No |
| WU-6a | `BrandMarkDetection` + DTO: `MarkType` discriminator and `Text`; extractor prompt; defensive parsing | ~460 | No |
| WU-6b | `SourceImagePath` + normalised `BoundingBox`; prompt extension; index→`StoragePath` resolution; validation and minimum-area guard | ~400 | No |
| WU-7a | **Text**-mark rendering in `BuildBrandMarkSlot` via resolved caption typography | ~340 | No |
| WU-7b | `IBrandMarkAssetExtractor` port + SkiaSharp crop adapter + persist via `IGeneratedAssetStorage` + ADR | ~640 | No |
| WU-7c | **Icon**-mark `<img>` rendering + brand-mark ref resolution in `StudioVisualMarkupResolver` and `PptxExportAdapter` + degrade-to-chip path | ~560 | No |
| WU-8 | Spec deltas (4 capabilities) + PRD §11 amendment + doc sync | ~420 | No |
| | **Total** | **~4,260** | 11 commits, none exceeding 800 |

`Decision needed before apply: No` — all three questions are settled.
`Chained PRs recommended: No`
`800-line budget risk: Low` — highest single unit is WU-7b at ~640, after the split. Without the
split, a combined brand-mark unit would have landed near 2,400 and breached the budget outright.

**Ordering constraints**: WU-1 → WU-7a (text-mark rendering consumes resolved typography).
WU-6a → WU-6b → WU-7b → WU-7c. WU-7a depends only on WU-6a, so the text half can ship before any
bounding-box work exists. WU-2, WU-4 and WU-5 are independent and reorderable.

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| **The vision extractor cannot return a reliable bounding box.** Vision models are materially weaker at precise spatial localisation than at the categorical/ratio fields (`ColorPairing`, `AestheticTypography`) this pipeline already extracts. Stated plainly rather than papered over: this is the single least-certain assumption in the change. | **High** | WU-6b lands the field and its validation independently of any rendering, so unreliability is measured before WU-7b/WU-7c are committed. If measurement shows boxes are unusable, WU-7b/WU-7c are cut and icon marks ship the WU-6a-labelled approximate `PositionHint` + `ColorHex` treatment. The text half (WU-6a/WU-7a) is unaffected either way. |
| The crop contains surrounding background, so the icon renders as a rectangle over the slide | High (accepted) | Explicit v1 non-goal (matting deferred). Crops are taken from a mark that recurs on the client's *own* slides, so the surrounding background is usually the client's own flat colour. Owner reviews the first real crop before WU-7c is considered verified. |
| Reference image resolution is too low for a legible crop | Low | Verified during this revision: `SupabasePostMediaArchiver` stores images **as-downloaded, not thumbnailed**. A minimum-area guard rejects crops below a usable pixel threshold and degrades to the chip. |
| A brand-mark `<img>` reaches the browser as an unresolved storage path | Medium | Found during this revision — `StudioVisualMarkupResolver` resolves only `BackgroundImageRef`. Explicitly in scope (item 10) and part of WU-7c, with the export path covered too. |
| Text over a photo becomes illegible once white+shadow is replaced by a client pairing | Medium | Fallback to white+shadow when no pairing resolves; contrast guard specified in the spec phase. |
| `BackgroundOnly` on ComfyUI produces empty or blank backgrounds | Medium | Owner-run real generation required before the commit is considered verified. |
| Anatomy negative-prompt terms have no measurable effect | Medium (accepted) | Pure prompt-text addition, trivially revertible; unverifiable in-repo by design. |
| Detailer node requires a custom-node package absent from the target ComfyUI instance | High | Precisely why WU-5 is investigation-only (settled decision 1). |
| Fourth extraction-JSON contract addition this quarter invites model non-compliance, and this one is the largest yet (four fields, one structured) | Medium-High | Defensive parsing per convention: missing/unknown/malformed → null, never throw. Split across WU-6a/WU-6b so a compliance regression is attributable to one half. |
| New port + image-manipulation adapter expands the architecture surface | Medium | ADR ships with WU-7b; SkiaSharp is already a referenced dependency, so no new package or native asset enters the build. |
| Typography sizes derived from an extreme `SizeRatio` overflow the slide | Medium | Clamp bounds specified in the spec phase, at the resolver rather than per-builder. |
| The owner's "much more sophisticated" bar is not met even after all items ship | Medium | Stated honestly: items 1/2/4 close known fidelity gaps; item 3's photorealism ceiling is largely checkpoint-bound, and checkpoint choice is out of scope by settled decision 2. |
| Verified only against one client (Lucia Fernandez) | Medium | Success criteria require a second client profile with a materially different `SizeRatio`/palette, and — new in this revision — coverage of both mark types across the two profiles where available. |

## Rollback Plan

- **WU-1, WU-2, WU-3, WU-7a** — pure render-path changes with hardcoded-literal fallbacks retained.
  Reverting a single commit restores the previous markup exactly; no persisted data is affected.
- **WU-4** — prompt-text only. Revert restores the previous prompt strings.
- **WU-5** — a document. Nothing to roll back.
- **WU-6a, WU-6b** — additive nullable fields, no EF migration (profile is JSON-serialised).
  Reverting leaves previously persisted values ignored, exactly as older profiles are ignored today;
  no data loss and no deserialisation failure in either direction.
- **WU-7b** — reverting removes the port and adapter. Crops already written to storage become orphan
  objects under the analysis folder; they are inert (nothing reads them once WU-7c is also reverted)
  and are removed by the existing per-client/per-analysis deletion paths. No cleanup migration.
- **WU-7c** — reverting restores the coloured-chip render. Because the degrade path (item 11) is
  itself the pre-change behaviour, a partial revert of WU-7c alone leaves a consistent renderer.
- No database migration exists in this change, so there is no non-trivially-reversible artifact.
- Per-commit rollback is sufficient throughout; there is no ordering in which a partial revert leaves
  the pipeline broken.

## Dependencies

- Existing `CompositionDefaults.ResolveTypography`, `ColorPairing`/`ColorPairingDerivation`,
  `OpenAiImageModelClient.BackgroundOnlyDirective`, `IGeneratedAssetStorage`, `ProfilePostImageRef`
  and the reference-image archival pipeline — reused read-only.
- SkiaSharp 3.119.0, **already referenced** by `Incoders.Hive.Infrastructure.csproj`. No new package.
- A running ComfyUI instance with a configured `default_checkpoint` credential, for owner-side
  verification of WU-4 and for the WU-5 investigation. **Owner-side prerequisite.**
- At least one real client aesthetic profile with populated `SizeRatio`, `ColorPairings`, archived
  reference images, and a visible brand mark — ideally one text-mark and one icon-mark client.
- No new migrations, credentials or provider categories.

## Success Criteria

- [ ] No hardcoded heading/subtitle/body/signature pixel literal remains as a *primary* value in
      `BuildStoryTemplateSlots`, `BuildCarouselTemplateSlots` or `BuildFlatCarouselSlots`; each is
      reachable only as a fallback.
- [ ] Two client profiles with materially different `SizeRatio` values produce visibly different
      rendered heading sizes (asserted on markup, not by eye).
- [ ] `AiVisualDesigner.BuildSystemPrompt` no longer contains a fixed percentage range independent
      of the client's resolved sizes.
- [ ] A slide on the enforced-photo path renders text in a colour taken from the client's
      `ColorPairings`; a profile with no resolvable pairing still renders the white+shadow fallback.
- [ ] `ComfyUiImageModelClient` injects the background-only directive when `request.BackgroundOnly`
      is true, and the directive text is defined in exactly one place shared with the OpenAI adapter
      (asserted by test, so a third adapter cannot silently drift).
- [ ] The ComfyUI default negative prompt contains anatomy-defect terms while retaining every
      existing text-suppression term.
- [ ] An owner-run real generation confirms background-only ComfyUI prompts still produce usable,
      non-blank backgrounds.
- [ ] The detailer feasibility report exists and states, explicitly, whether a custom-node package is
      required on the ComfyUI backend and what happens when it is absent.
- [ ] `BrandMarkDetection` carries `MarkType`, `Text`, `SourceImagePath` and `BoundingBox`; a profile
      persisted before this change deserialises with all four as null and throws nothing.
- [ ] An unrecognised `MarkType` value deserialises to `"unknown"` and renders exactly as today.
- [ ] A `text` mark renders its extracted text through the client's resolved caption typography.
- [ ] An `icon` mark with a valid bounding box produces a stored crop asset under the analysis
      folder and renders as an `<img>` in the brand-mark slot.
- [ ] The stored brand-mark ref resolves to a fetchable reference in the Studio preview **and** in
      PPTX export — asserted, not assumed.
- [ ] An `icon` mark with a missing, invalid, or below-minimum-area bounding box, or whose crop
      cannot be stored, renders the `PositionHint` + `ColorHex` chip and emits a degrade log entry.
- [ ] The bounding-box reliability measurement from WU-6b is recorded, and if it fails, the decision
      to cut WU-7b/WU-7c is written down rather than silently absorbed.
- [ ] Spec deltas merged for content-generation, comfyui-image-provider, profile-analysis and
      content-studio-background-rendering; ADR for the brand-mark asset port merged; PRD §11
      amendment merged.
- [ ] All MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`); Strict TDD followed.

## Resolved in this revision

The revision-1 `## Proposal question round` is closed. Its three questions map to settled decisions
1 (detailer: investigation-only, confirmed), 2 (checkpoint curation: out of scope, confirmed) and
3 (signature: **both text and icon**, which overrode the recommended text-only v1). See
**Settled decisions** above. No open questions remain; spec and design may proceed.
