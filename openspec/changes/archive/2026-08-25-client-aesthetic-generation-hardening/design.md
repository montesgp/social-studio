# Design: Client-Aesthetic Generation Hardening

## Technical Approach

Wire already-extracted client data into existing call sites (WU-1..WU-4), then build the brand-mark
track schema-first (WU-6a/b), text-render (WU-7a), asset pipeline (WU-7b), icon-render (WU-7c).
No new architecture except one Application port. Every builder change keeps its current literal as a
fallback, so a cold-start client renders byte-identically.

### Codebase findings that change the proposal

| # | Finding (verified) | Effect |
|---|---|---|
| F1 | `CompositionTypography.HeadingSize`/`BodySize` are **non-nullable** and default to **28/16 px** (`CompositionDefaults.cs:31-35, 261`). Reading them directly would shrink a story headline 88px→28px for every unmeasured client. | D1 below — nullable *measured* sizes, mirroring `CaptionSize`. |
| F2 | `StudioVisualMarkupResolver` already inlines **every** `src="..."` in the markup, not only `BackgroundImageRef` (`StudioVisualMarkupResolver.cs:90-97`). The proposal's item 10 claim is wrong for Studio. | WU-7c Studio half needs **no** change; scope shrinks. |
| F3 | Render-time inlining is keyed to the single `renderBackgroundStoragePath` (`ContentGenerationJobProcessor.cs:1066-1079`). A brand-mark storage path in the markup is **never** inlined for the Chromium pass → broken image in the rasterized PNG. Not in the proposal. | New WU-7c requirement (D11). |
| F4 | `DegenerateImageGuardrailValidator` already does SkiaSharp image analysis behind an Application port. | The ADR's "category never placed behind a port" premise is false; the ADR is still required (new port) but must cite this precedent. |
| F5 | No org-configurable negative prompt exists — `DefaultNegativePrompt` is the sole source (`ComfyUiSettings` has no key). | "Append vs replace" is moot (D5). |

## Architecture Decisions

| # | Decision | Alternatives rejected | Rationale |
|---|---|---|---|
| D1 | Add `int? MeasuredHeadingSize` / `int? MeasuredBodySize` init-props to `CompositionTypography`, populated only by the 4-arg `ResolveTypography` profile branch via `SizeForCaption`-style nullable resolution. Builders use `measured ?? <literal>`. | Consume `HeadingSize`/`BodySize` directly (F1: catastrophic cold-start regression); threshold heuristics (silent, untestable). | Exact `CaptionSize` precedent; null = "unmeasured" is already the codebase's vocabulary. Clamp stays in `SizeFor` (`MinTypeSize`, `MaxSizeRatio`), not per-builder. |
| D2 | `BuildSystemPrompt` gains a `CompositionTypography` + canvas-height parameter (call site `AiVisualDesigner.cs:311` already has `spec`); emits `about {h}% of canvas height` computed from `MeasuredHeadingSize/canvasHeight`, rounded to whole percent ±1. Unmeasured → the current literal `7-13%` string verbatim. | Delete the line; pass raw px. | Keeps the prompt honest without inventing a number for cold-start clients. |
| D3 | Text colour on the enforced-photo path: thread one `textColor` into `BuildStoryTemplateSlots`/`BuildCarouselTemplateSlots` (mirroring `BuildFlatCarouselSlots`'s existing `textColor` param). Value = `spec.Palette.Text` **only if it clears WCAG ≥ 4.5:1 against black (L=0)**, else `#FFFFFF`. **Text-shadow is retained unchanged.** | Contrast against the actual photo (unknowable at markup time); require ≥4.5 against both black and white (mathematically near-impossible — the product of the two ratios is fixed at 21). | The retained `rgba(0,0,0,0.58-0.62)` shadow guarantees a dark halo, so legibility is governed by contrast against dark. Admits pale client neutrals, rejects dark brand colours that would vanish. |
| D4 | Extract `RelativeLuminance`/`ContrastRatio` from `ColorPairingDerivation`'s privates into a new `Application/.../Shared/WcagContrast.cs` static; `ColorPairingDerivation` delegates to it (behaviour-identical). | Duplicate the maths in the processor. | One WCAG implementation; existing derivation tests keep guarding it. |
| D5 | Shared directive: new `Infrastructure/Ai/Providers/ImagePromptDirectives.cs` — `internal const string BackgroundOnly` moved verbatim from `OpenAiImageModelClient.cs:61-62`. ComfyUI composes it **identically**: `$"{BackgroundOnly}\n\n{request.Prompt}"` before `BuildWorkflowGraph`. | ComfyUI-specific wording. | Identical composition means the two adapters cannot drift; asserted by test. |
| D6 | Negative prompt: extend `DefaultNegativePrompt` **in place**, appending `", extra limbs, extra fingers, missing fingers, fused fingers, bad hands, malformed hands, deformed face, duplicate face, cloned face, two heads, extra person, mutated, disfigured, bad anatomy"`. No settings key added. | Add a `negative_prompt` credential key (scope creep; F5). | Every existing text-suppression term is preserved (asserted). Pre-existing caveat unchanged: an org `WorkflowTemplateJson` lacking the negative token drops it silently. |
| D7 | WU-5 ships `comfyui-detailer-feasibility-report.md` answering exactly: (a) node family (`FaceDetailer`/`UltralyticsDetectorProvider` vs `ADetailer`), (b) the upstream custom-node package each ships in and its bbox/segm model downloads, (c) whether this repo can detect installed nodes — **it can**: the adapter already speaks ComfyUI HTTP, and `GET /object_info` enumerates installed node classes, so a preflight probe is feasible, (d) failure mode when absent (`POST /prompt` 400 → whole workflow fails), (e) whether a graceful-degrade graph (probe → conditional node injection) is viable, (f) a Recommend / Recommend-with-preflight / Do-not-ship verdict. Documentation only. | Ship a graph change. | Settled decision 1. |
| D8 | `BrandMarkDetection` gains four additive nullable fields (see Interfaces). `MarkType` parses case-insensitively to `text`/`icon`; anything else, including null → `unknown`. Bounding box normalised `0..1`. | Enum in Domain; absolute pixel coords. | Normalised coords are what vision models return and survive any source resolution. `FFS-PA-02` defensive-parse convention: malformed → null, never throw. |
| D9 | Index→`StoragePath` resolution happens **inside the extractor**: widen `loadedPairs` (`MultimodalAestheticStyleExtractor.cs:99-115`) from `(AiImageInput, Caption)` to `(AiImageInput, Caption, StoragePath)`. The model's `Image {i}` index is the **post-skip `loadedPairs` index** (failed opens are `continue`d at L108), so resolution MUST index `loadedPairs`, not `referenceImages`. Out-of-range index → all box fields null. | Return the raw index downstream. | Only the extractor knows the ordering; nothing downstream may depend on prompt order. |
| D10 | Extractor schema version bumps **twice**: `aesthetic-extractor/v5` in WU-6a, `/v6` in WU-6b (`AestheticFreshness.CurrentSchemaVersion` + `PromptVersion` are the same const — they cannot drift). | One bump at WU-6a. | Cost: two re-analysis waves. Accepted — a single v5 bump would permanently mark v5 profiles fresh with no bounding box, which is exactly what the `Refresh` verdict exists to prevent. |
| D11 | Generalise render-time inlining (F3): replace `renderBackgroundStoragePath` with a small `HashSet<string> renderInlineRefs` and loop `InlineBackgroundUrlAsync` over it; the brand-mark crop path is added when the icon slot is emitted. | Rely on the Studio resolver (does not run before Chromium). | Without this the *rasterized* asset — the thing that actually ships — has a broken `<img>`. |
| D12 | PPTX (`PptxExportAdapter.cs:86`) resolves exactly one picture ref per envelope. Add the brand-mark ref as a **second** resolved picture, placed from the `data-slot="brand-mark"` captured geometry, reusing `ResolvePictureBytesAsync` unchanged. Unresolvable → omit the picture, export the slide (BGREF-04 semantics). | Rely on `FlattenedImageRef` (absent for non-flattened items). | Minimal, mirrors the existing background path exactly. |
| D13 | ADR `docs/architecture/adr/` — new `IBrandMarkAssetExtractor` port + SkiaSharp adapter; crop-only, no matting, no vision API, no logo regeneration; degrade contract. MUST cite `DegenerateImageGuardrailValidator` as the existing SkiaSharp-behind-a-port precedent (F4) rather than claiming a novel category. | Skip the ADR. | `require_adr_for_architecture_changes: true`. |

## Data Flow

    ProfilePostImageRef[] ──► MultimodalAestheticStyleExtractor
                                │ loadedPairs[i] = (bytes, caption, storagePath)   [D9]
                                ▼
                         BrandMarkDetection{MarkType,Text,SourceImagePath,BoundingBox}
                                │
                 ┌──────────────┴───────────────┐
            MarkType=text                  MarkType=icon
                 │                              │
                 │                    IBrandMarkAssetExtractor        [WU-7b]
                 │                    OpenAsync(SourceImagePath)
                 │                    SkiaSharp crop(BoundingBox)
                 │                    PutAssetAsync(.../brand-mark.png)
                 │                              │ ok            │ fail/invalid
                 ▼                              ▼               ▼
        BuildBrandMarkSlot: <p> text     <img src=path>   PositionHint+ColorHex chip
           (caption typography)                │            (today's behaviour)
                                               ├─► renderInlineRefs → Chromium   [D11]
                                               ├─► StudioVisualMarkupResolver    [F2, no change]
                                               └─► PptxExportAdapter 2nd picture [D12]

## File Changes

| File | Action | Description |
|---|---|---|
| `Application/.../Visual/CompositionDefaults.cs` | Modify | Populate `MeasuredHeadingSize`/`MeasuredBodySize` (D1) |
| `Domain/Social/ContentGeneration/VisualCompositionSpec.cs` | Modify | Two nullable init-props on `CompositionTypography` |
| `Application/.../Pipeline/ContentGenerationJobProcessor.cs` | Modify | WU-1 sizes; WU-3 `textColor`; WU-7a/7c `BuildBrandMarkSlot`; D11 inline refs |
| `Application/.../Shared/WcagContrast.cs` | Create | Shared WCAG luminance/contrast (D4) |
| `Application/.../Shared/ColorPairingDerivation.cs` | Modify | Delegate to `WcagContrast` |
| `Application/.../Shared/BrandMarkDetection.cs` | Modify | Four additive nullable fields |
| `Application/Abstractions/.../IBrandMarkAssetExtractor.cs` | Create | New port (WU-7b) |
| `Infrastructure/Social/ContentGeneration/SkiaBrandMarkAssetExtractor.cs` | Create | SkiaSharp crop + `PutAssetAsync` |
| `Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs` | Modify | Prompt sections, DTO, D9 resolution, D10 version |
| `Infrastructure/Social/ContentGeneration/AiVisualDesigner.cs` | Modify | D2 size-derived prompt line |
| `Infrastructure/Ai/Providers/ImagePromptDirectives.cs` | Create | Shared directive const (D5) |
| `Infrastructure/Ai/Providers/{ComfyUi,OpenAi}ImageModelClient.cs` | Modify | Use shared directive; ComfyUI honours `BackgroundOnly`; D6 terms |
| `Infrastructure/Rendering/PptxExportAdapter.cs` | Modify | D12 second picture |
| `ApiService/Program.cs` (DI) | Modify | Register `IBrandMarkAssetExtractor` |
| `docs/architecture/adr/*-brand-mark-asset-port.md` | Create | D13 |
| `openspec/changes/.../comfyui-detailer-feasibility-report.md` | Create | D7 |
| `Application/.../ContentStudio/Shared/StudioVisualMarkupResolver.cs` | **No change** | F2 |

## Interfaces / Contracts

```csharp
// Application/Features/Social/ContentGeneration/Shared/BrandMarkDetection.cs  (WU-6a + WU-6b)
public sealed record BrandMarkDetection(
    bool Present,
    string? PositionHint = null,
    string? ColorHex = null,
    string? MarkType = null,        // "text" | "icon" | "unknown"; null on pre-v5 profiles
    string? Text = null,            // meaningful only when MarkType == "text"
    string? SourceImagePath = null, // resolved StoragePath (D9); icon only
    BrandMarkBoundingBox? BoundingBox = null);

/// Normalised 0..1, origin top-left. Invalid boxes never reach here — the extractor maps
/// out-of-range, non-finite, inverted, or below-minimum-area values to null (FFS-PA-02).
public sealed record BrandMarkBoundingBox(double X, double Y, double Width, double Height);

// Application/Abstractions/Social/ContentGeneration/IBrandMarkAssetExtractor.cs  (WU-7b)
public interface IBrandMarkAssetExtractor
{
    /// Crops the mark from the archived source image and persists it. NEVER throws:
    /// any failure (open, decode, invalid box, sub-minimum crop, storage) returns Failure
    /// and the caller degrades to the PositionHint+ColorHex chip.
    Task<Result<StoredAssetRef>> ExtractAsync(
        StoredAssetKey targetKey,   // RelativePath = "brand-mark.png" (upsert; mirrors "story.png")
        string sourceImagePath,
        BrandMarkBoundingBox box,
        CancellationToken ct);
}
```

Validation constants (WU-6b): each of `X`,`Y`,`Width`,`Height` finite and in `[0,1]`;
`X+Width ≤ 1.0`, `Y+Height ≤ 1.0`; normalised area ≥ `0.0009` (3%×3%); resulting crop ≥ `24×24` px
after scaling to the source image — otherwise null / degrade.

## Testing Strategy

| Layer | What to test | Approach |
|---|---|---|
| Unit | D1 sizes: measured ratio drives heading px; unmeasured keeps 88/74/36/38/30/25 | Assert on markup string, per builder |
| Unit | D3 guard: pale pairing used; dark pairing → `#FFFFFF`; shadow always present | `WcagContrast` table-driven + markup assertion |
| Unit | D5: both adapters' prompts contain the same const instance; ComfyUI positive prompt gains the directive only when `BackgroundOnly` | Assert against `ImagePromptDirectives.BackgroundOnly` |
| Unit | D6: every pre-existing term still present **and** each new anatomy term present | Term-by-term assertion |
| Unit | **Bounding-box defensive parsing** — the highest-risk surface | See below |
| Unit | Crop pipeline: valid box → correct pixel rect; every failure mode → `Failure`, never throws | `SkiaBrandMarkAssetExtractorTests` (new, `Tests/Infrastructure/Social/ContentGeneration/`) with in-memory `IGeneratedAssetStorage` fake and synthesised SKBitmaps |
| Unit | D9: `Image {i}` resolves to the **post-skip** storage path when an earlier image failed to open | `MultimodalAestheticStyleExtractorTests.cs` with one failing `OpenAsync` |
| Unit | Degrade: icon + null/invalid box → chip markup + warning log | Processor test asserting markup + captured log |
| Integration | D11: brand-mark path present in `renderInlineRefs`; D12: PPTX resolves a second picture, and omits it on failure | Existing processor/PPTX test harnesses |
| Owner-run | WU-4 real ComfyUI generation; first real crop reviewed before WU-7c is verified | Manual, per proposal risk table |

**WU-6b reliability measurement** (proposal's stated highest risk). Two complementary suites, both in
`Tests/Infrastructure/Social/ContentGeneration/`:

1. **Defensive-parsing suite** (`MultimodalAestheticStyleExtractorTests.cs`) — canned model JSON for:
   missing `boundingBox`; non-numeric values; negative coords; `>1.0` values; zero-area; inverted
   (`Width<0`); `X+Width>1.0`; below-minimum-area; box present but `markType:"text"`; box present but
   `present:false`; source index out of range. **Every case MUST yield null and MUST NOT throw.**
2. **Fixture measurement** — 3-5 checked-in reference images with hand-annotated expected regions,
   asserted with a generous IoU floor (≥0.5). These are *measurement*, not a pass/fail gate: WU-6b's
   deliverable is a written reliability verdict recorded in `tasks.md`. If it fails, D13's ADR records
   the decision to cut WU-7b/WU-7c rather than absorbing it silently.

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary. The crop adapter decodes bytes the pipeline itself archived from the
client's own posts, through the same SkiaSharp decode path `DegenerateImageGuardrailValidator`
already applies to provider-returned images; failure is contained by the `Result` + degrade contract.

## Migration / Rollout

No DB migration — the aesthetic profile is JSON-serialised and all new fields are additive nullable.
The D10 version bumps (v5, v6) trigger re-analysis of every client profile through the existing
`AestheticFreshness.Refresh` verdict; this is the intended mechanism, not a migration. Crops written
before a WU-7b revert become inert orphans under the analysis folder, removed by existing
per-client/per-analysis deletion paths.

## Open Questions

- [ ] None blocking. D10's double version bump costs two re-analysis waves; if the owner would rather
      pay one, WU-6a and WU-6b must ship in a single commit — which breaches the 800-line budget.
