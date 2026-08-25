# ADR 0014 - Brand-Mark Asset Port

## Status
Accepted

## Context

WU-6a/6b (`client-aesthetic-generation-hardening`) taught `BrandMarkDetection` to classify a
recurring client mark as `"text"` or `"icon"` and, for icon marks, to resolve a
`SourceImagePath` (the archived reference image the mark was seen on) plus a validated
normalised `BoundingBox`. WU-7a wired text-mark rendering: the literal `Text` is rendered
through the client's caption typography. Icon marks had no equivalent path — `BuildBrandMarkSlot`
still degraded every icon mark to the pre-existing empty colour chip, because nothing in the
pipeline could turn a bounding box + a reference image into a durable, renderable asset.

Three requirements shaped the fix:

1. The crop MUST be extracted from bytes the pipeline itself already archived — no new vision
   API call, no matting/segmentation, no logo regeneration. This is a narrow image-processing
   operation, not a new AI capability.
2. Every failure mode (source image missing, bytes undecodable, bounding box scales below a
   usable pixel size, storage write fails) MUST degrade to the pre-existing `PositionHint`+
   `ColorHex` chip. The generation job MUST NOT fail because a crop could not be produced.
3. The crop-and-persist operation needed to be independently testable without a real image
   pipeline or a live Chromium/render dependency.

## Decision

### 1. `IBrandMarkAssetExtractor` — new Application port

`Application/Abstractions/Social/ContentGeneration/IBrandMarkAssetExtractor.cs`:

```csharp
Task<Result<StoredAssetRef>> ExtractAsync(
    StoredAssetKey targetKey,      // RelativePath = "brand-mark.png" (upsert; mirrors "story.png")
    string sourceImagePath,
    BrandMarkBoundingBox box,
    CancellationToken ct);
```

Contract: **never throws**. Every failure — the source image cannot be opened, the bytes cannot
be decoded, the scaled crop falls below the minimum pixel size, or `IGeneratedAssetStorage`
fails the write — maps to `Result.Failure`. The caller degrades to the chip; the crop is a
best-effort enhancement, never a hard dependency of a successful generation job.

### 2. `SkiaBrandMarkAssetExtractor` — Infrastructure adapter, same shape as an existing precedent

`Infrastructure/Social/ContentGeneration/SkiaBrandMarkAssetExtractor.cs` implements the port:
`IGeneratedAssetStorage.OpenAsync` → `SKBitmap.Decode` → scale the normalised box to a pixel
rect (clamped to image bounds) → `SKBitmap.ExtractSubset` → re-encode PNG → `PutAssetAsync`.

**This is not a novel architectural category.** `DegenerateImageGuardrailValidator` (behind
`IImageGuardrailValidator`, introduced in an earlier batch of this same change) already performs
SkiaSharp pixel analysis on provider-returned images entirely behind an Application port, with
the identical never-throws-degrades-instead contract. `SkiaBrandMarkAssetExtractor` extends that
established pattern to a second concrete operation (crop instead of degenerate-image detection)
rather than introducing a new way for Infrastructure image-processing to reach the pipeline.

A minimum crop-size guard (`MinimumCropDimensionPx = 24`, i.e. the scaled crop must be at least
24×24 px) rejects boxes that are valid in normalised `[0,1]` space (already checked at
extraction/persistence time by `BrandMarkDetection.TryValidateBoundingBox`, WU-6b) but scale
down to a pixel region too small to be a usable asset once the *real* source image dimensions
are known — a check that can only happen here, at crop time, because it needs the decoded
bitmap's actual width/height.

### 3. DI registration

`InfrastructureContentGenerationExtensions.AddB1bRenderingAndDesigner` registers
`services.AddScoped<IBrandMarkAssetExtractor, SkiaBrandMarkAssetExtractor>()` next to the
existing `IImageGuardrailValidator` registration, reached from `Program.cs` via
`DependencyInjection.cs` — no new bootstrap wiring needed.

### 4. Processor wiring — nullable dependency, side-effect-only this batch

`ContentGenerationJobProcessor` gains `IBrandMarkAssetExtractor?` as a **nullable constructor
parameter** (default `null`), mirroring the established pattern from ADR 0007 §5 and ADR 0006:
null means the icon branch is a pure no-op, byte-for-byte identical to pre-WU-7b behaviour.

When `BrandMark.MarkType == "icon"`, a validated `BoundingBox` is present, `SourceImagePath` is
non-blank, and the port is registered, the processor calls `ExtractAsync` before building the
brand-mark slot. On `Result.Failure`, a warning is logged and the pipeline falls through to the
unchanged chip-rendering path. **This batch (WU-7b) only wires the extraction call and its
failure degrade** — the successful crop is not yet consumed by the rendered markup. Swapping the
chip for an `<img>` referencing the stored crop, and threading the crop path into the render-time
asset-inlining set, is WU-7c's scope.

## Consequences

- `IBrandMarkAssetExtractor` is a narrow, single-purpose port: crop-and-persist only. It does not
  grow into a general image-editing surface — matting, logo regeneration, or vision-model calls
  belong to different ports if ever needed.
- The crop pipeline is fully unit-testable with an in-memory `IGeneratedAssetStorage` fake and
  synthesised `SKBitmap`s (`SkiaBrandMarkAssetExtractorTests`) — no live storage, no Chromium
  render, no vision-model dependency.
- Icon-mark extraction failure is contained exactly like every other degrade path in this
  pipeline (guardrail rejection, designer failure, image-gen failure): logged, never job-fatal.
- WU-7c depends on this port's `Result<StoredAssetRef>` success case to know which storage path
  to reference; it adds no new port, only new call sites (markup `<img>` swap, `renderInlineRefs`,
  PPTX second picture).

### WU-6b Bounding-Box Reliability Verdict

Task 6b.5 called for measuring the multimodal extractor's real-world bounding-box accuracy
(IoU) against hand-annotated reference images. That measurement was **not executed** in the
`sdd-apply` session that produced WU-6b/WU-7a — no live vision-model access and no owner-supplied
reference images with ground-truth boxes were available offline; fabricating an IoU number was
explicitly rejected as worse than no measurement (see `tasks.md`, "Bounding-Box Reliability
Verdict (WU-6b)"). What that session **did** deliver toward the reliability goal was the
defensive-parsing suite (11 cases, `MultimodalAestheticStyleExtractorTests.cs`) proving the code
path never throws or fabricates a value on malformed/adversarial model output.

This ADR's decision: **ship WU-7b/WU-7c anyway**, on the strength of the defensive-parsing
guarantee plus this port's own never-throws/degrade-to-chip contract. If the model's real-world
localisation later proves unreliable, the blast radius is bounded and reversible — a poor
bounding box degrades to a small/failed crop (caught by the 24×24 guard or a garbage image),
which degrades to the pre-existing chip, exactly as if `IBrandMarkAssetExtractor` were absent.
No unresolvable failure mode reaches the generation job. An owner-run IoU measurement against
real client imagery remains a recommended follow-up, not a blocking prerequisite.

## Related

- Proposal / Spec / Design: `openspec/changes/client-aesthetic-generation-hardening/`
- Precedent: `IImageGuardrailValidator` / `DegenerateImageGuardrailValidator` — the existing
  SkiaSharp-behind-a-port shape this ADR extends, not a novel category.
- Builds on: ADR 0007 (Image Generation Gateway Port) §5 — nullable-constructor-parameter,
  rollback-safe integration pattern reused here for `IBrandMarkAssetExtractor`.
- Architecture gate: `IBrandMarkAssetExtractor` is in `Application.Abstractions.Social.ContentGeneration`
  — no SkiaSharp/provider SDK types cross the layer boundary; `SkiaBrandMarkAssetExtractor` is
  Infrastructure-only.
