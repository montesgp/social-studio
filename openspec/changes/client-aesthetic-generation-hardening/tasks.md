# Tasks: Client-Aesthetic Generation Hardening

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | ~4,260 total across 11 work units (per-unit budget, not aggregate) |
| Per-unit review budget (session override) | 800 changed lines |
| 400-line budget risk | Low (per-unit budget is 800, not the skill default 400; largest unit WU-7b ~640) |
| Chained PRs recommended | No — proposal's own delivery model is sequential commits to `dev`, tracked by GitHub issue |
| Suggested split | 11 sequential commits to `dev`, WU-1..WU-8 with WU-6a/6b and WU-7a/7b/7c as sub-commits |
| Delivery strategy | single-pr (session preflight) — resolved by the project's existing `dev`-direct workflow, not a PR chain |
| Chain strategy | pending (no chaining needed; per-unit budget not breached) |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Low

**Re-forecast against the concrete task breakdown**: none of the 11 work units' task lists exceed the
proposal's estimate. WU-7b remains the largest (~640, unchanged) — its 6 tasks (git-status check,
port, adapter+tests, DI registration, wiring, ADR) match the proposal's scope exactly, no growth.
Two flags, neither raises risk above Low:
- **WU-6b**: the 3-5 fixture reference images (task 6b.5) add binary diff weight, not authored code
  lines; treated like generated goldens — excluded from the 800-line authored-risk count but still
  part of the commit's reviewable file set.
- **WU-7c**: gained an explicit confirmation task (7c.4, proving `StudioVisualMarkupResolver` needs
  no change) beyond the proposal's implicit assumption; it is a test-only addition (~20-40 lines),
  not a scope change.

### Suggested Work Units

| Unit | Goal | Commit | Focused test command | Runtime harness | Rollback boundary |
|---|---|---|---|---|---|
| WU-1 | Markup builders consume resolved typography sizes | 1 | `dotnet test --filter FullyQualifiedName~ContentGenerationJobProcessorTests` | N/A — pure markup assertion, no live service | `CompositionDefaults.cs`, `VisualCompositionSpec.cs`, `ContentGenerationJobProcessor.cs`; literal fallback preserved |
| WU-2 | Designer prompt line derived from resolved sizes | 2 | `dotnet test --filter FullyQualifiedName~AiVisualDesignerTests` | N/A — prompt-string assertion | `AiVisualDesigner.cs` only |
| WU-3 | Enforced-photo text colour from `ColorPairings` | 3 | `dotnet test --filter FullyQualifiedName~WcagContrastTests|ColorPairingDerivationTests|ContentGenerationJobProcessorTests` | N/A — markup + contrast-math assertion | `WcagContrast.cs` (new), `ColorPairingDerivation.cs`, `ContentGenerationJobProcessor.cs` |
| WU-4 | ComfyUI `BackgroundOnly` parity + anatomy negative-prompt terms | 4 | `dotnet test --filter FullyQualifiedName~ComfyUiImageModelClientTests` | Owner-run real ComfyUI generation confirms non-blank background (manual, credential-gated) | `ImagePromptDirectives.cs` (new), `ComfyUiImageModelClient.cs`, `OpenAiImageModelClient.cs` |
| WU-5 | Detailer feasibility report | 5 | N/A — documentation only, no test | N/A — investigation deliverable, not executable | single new `.md` file, trivially removable |
| WU-6a | `BrandMarkDetection` MarkType + Text, defensive parsing, v5 | 6 | `dotnet test --filter FullyQualifiedName~BrandMarkDetectionTests|MultimodalAestheticStyleExtractorTests|AestheticProfilePersistenceTests` | N/A — JSON round-trip assertion | `BrandMarkDetection.cs`, `MultimodalAestheticStyleExtractor.cs`, `AestheticProfilePersistence.cs` |
| WU-6b | SourceImagePath + BoundingBox, validation, v6, reliability suite | 7 | `dotnet test --filter FullyQualifiedName~MultimodalAestheticStyleExtractorTests` | N/A for suite; fixture IoU measurement is a recorded verdict — this batch's verdict is "not measurable offline", not a gated run | `BrandMarkDetection.cs`, `MultimodalAestheticStyleExtractor.cs`; fixtures dir NOT created this batch (no real images/ground-truth available offline — see verdict) |
| WU-7a | Text-mark rendering via caption typography | 8 | `dotnet test --filter FullyQualifiedName~ContentGenerationJobProcessorTests` | N/A — markup assertion | `ContentGenerationJobProcessor.cs` (`BuildBrandMarkSlot`) only |
| WU-7b | `IBrandMarkAssetExtractor` port + SkiaSharp adapter + ADR | 9 | `dotnet test --filter FullyQualifiedName~SkiaBrandMarkAssetExtractorTests` | N/A — in-memory storage fake + synthesised bitmaps | `IBrandMarkAssetExtractor.cs`, `SkiaBrandMarkAssetExtractor.cs`, `Program.cs` DI line(s), ADR doc |
| WU-7c | Icon-mark `<img>` render + `renderInlineRefs` + PPTX 2nd picture | 10 | `dotnet test --filter FullyQualifiedName~ContentGenerationJobProcessorTests|PptxExportAdapterTests` | Owner reviews first real crop before this unit is considered verified (manual) | `ContentGenerationJobProcessor.cs`, `PptxExportAdapter.cs`; degrade-to-chip is pre-change behaviour |
| WU-8 | Spec/doc sync, PRD §11 amendment, gate run | 11 | N/A — doc-only except final gate run | `pwsh -File scripts/gates/Run-Gates.ps1` | four spec-delta merges + PRD note; no code |

---

## WU-1: Typography Size Consumption (Application, ~380 lines)

- [x] 1.1 RED+GREEN: `Domain/Social/ContentGeneration/VisualCompositionSpec.cs` — add nullable `int? MeasuredHeadingSize`/`MeasuredBodySize` init-props on `CompositionTypography` (D1). CAGH-CG-01-a.
- [x] 1.2 RED+GREEN: `Application/.../Visual/CompositionDefaults.cs` — populate `Measured*` via the 4-arg `ResolveTypography` profile branch, mirroring `SizeForCaption`'s nullable resolution (D1); test covers measured-present and measured-absent branches. CAGH-CG-01-a/b.
- [x] 1.3 RED+GREEN: `ContentGenerationJobProcessor.cs::BuildStoryTemplateSlots` — `measured ?? 88px/74px/36px` fallback chain (D1); tests: measured value renders, unmeasured client renders exact pre-change literals, no exception. CAGH-CG-01-a/b.
- [x] 1.4 RED+GREEN: same file `BuildCarouselTemplateSlots` — identical pattern (38px/30px literals). CAGH-CG-01-a/b.
- [x] 1.5 RED+GREEN: same file `BuildFlatCarouselSlots` — identical pattern (25px literal; verify already-wired `CaptionSize` role unaffected). CAGH-CG-01-a/b.
- [x] 1.6 Test: two client profiles with materially different `SizeRatio` render different heading font-sizes on carousel markup (assert on markup, not by eye). CAGH-CG-01-c.

## WU-2: AiVisualDesigner Size-Derived Prompt Line (Infrastructure, ~140 lines)

- [x] 2.1 RED+GREEN: `Infrastructure/.../AiVisualDesigner.cs::BuildSystemPrompt` — add `CompositionTypography` + canvas-height params; emit `about {h}% of canvas height` from `MeasuredHeadingSize/canvasHeight`, rounded ±1 (D2); no `IAiVisualDesigner` contract change. CAGH-CG-02-a.
- [x] 2.2 Test: unmeasured client — prompt contains the pre-existing fixed `7-13%` literal verbatim (regression). CAGH-CG-02-b.

## WU-3: Enforced-Photo Text Colour (Application, ~420 lines)

- [x] 3.1 RED+GREEN (sequenced first — WU-3 depends on this extraction): create `Application/.../Shared/WcagContrast.cs` with `RelativeLuminance`/`ContrastRatio` extracted from `ColorPairingDerivation`'s privates (D4); `ColorPairingDerivation` delegates to it, behaviour-identical; existing derivation tests keep passing unmodified as the regression guard.
- [x] 3.2 RED+GREEN: `ContentGenerationJobProcessor.cs::BuildStoryTemplateSlots`/`BuildCarouselTemplateSlots` — thread `textColor` mirroring `BuildFlatCarouselSlots`; use `spec.Palette.Text` only if `WcagContrast.ContrastRatio` ≥4.5:1 against black, else `#FFFFFF`; shadow retained unchanged (D3). CAGH-CG-03-a.
- [x] 3.3 Test: no resolvable pairing keeps white+shadow fallback exactly as before (regression). CAGH-CG-03-b.
- [x] 3.4 Test: low-contrast resolved pairing colour still renders as the pairing colour (not silently replaced by white) with shadow/scrim compensation retained. CAGH-CG-03-c.

## WU-4: ComfyUI BackgroundOnly Parity + Anatomy Terms (Infrastructure, ~300 lines)

- [ ] 4.1 DEFERRED (not started this batch — scoped out): create `Infrastructure/Ai/Providers/ImagePromptDirectives.cs` — `internal const string BackgroundOnly` moved verbatim from `OpenAiImageModelClient.cs:61-62` (D5); update `OpenAiImageModelClient` to reference it; test asserts both adapters reference the same const instance. CAGH-CUI-01-c. This batch was explicitly scoped to `ComfyUiImageModelClient.cs` only (no `OpenAiImageModelClient.cs` edit, no new shared file); a local `BackgroundOnlyDirective` const with the identical mirrored text was added directly in `ComfyUiImageModelClient.cs` instead (see 4.2). A follow-up batch should extract the shared `ImagePromptDirectives.cs` and wire both adapters to it to fully satisfy D5/CAGH-CUI-01-c.
- [x] 4.2 RED+GREEN: `Infrastructure/Ai/Providers/ComfyUiImageModelClient.cs` — read `request.BackgroundOnly`, compose `$"{BackgroundOnlyDirective}\n\n{request.Prompt}"` before `BuildWorkflowGraph` (D5, adapted — local const, not yet the shared `ImagePromptDirectives.BackgroundOnly` per 4.1's deferral); tests: `true` injects directive, `false`/unset leaves prompt identical to pre-change (regression). CAGH-CUI-01-a/b.
- [x] 4.3 RED+GREEN: same file `DefaultNegativePrompt` — append anatomy-defect terms in place, `extra limbs, extra fingers, missing fingers, fused fingers, bad hands, malformed hands, deformed face, duplicate face, cloned face, two heads, extra person, mutated, disfigured, bad anatomy` (D6); tests: every pre-existing text-suppression term retained (regression), every new anatomy term present. CAGH-CUI-02-a/b.

## WU-5: Detailer Feasibility Report (Docs, ~200 lines — investigation only, no code)

- [x] 5.1 Write `openspec/changes/client-aesthetic-generation-hardening/detailer-feasibility-report.md` answering D7(a)-(f): node family (`FaceDetailer`/`UltralyticsDetectorProvider` vs `ADetailer`), custom-node package + bbox/segm model downloads, preflight-detectability via `GET /object_info`, failure mode (`POST /prompt` 400 → whole workflow fails), graceful-degrade graph viability, and a Recommend / Recommend-with-preflight / Do-not-ship verdict. Settled decision 1; no workflow-graph or production code change. **Note**: delivered filename is `detailer-feasibility-report.md` (without the `comfyui-` prefix used in design.md's File Changes table) — content and scope match D7/task 5.1 exactly; only the filename differs from the design doc's table.

## WU-6a: BrandMarkDetection MarkType + Text (Application/Infrastructure, ~460 lines)

- [x] 6a.1 RED+GREEN: `Application/.../Shared/BrandMarkDetection.cs` — add nullable `MarkType` (D8); case-insensitive parse to `text`/`icon`, else/null → `unknown`. Tests: recognised value parses (CAGH-PA-01-a), unrecognised → `unknown`, no throw (CAGH-PA-01-b), pre-change blob with no `markType` key deserializes `MarkType=null`, no throw (CAGH-PA-01-c).
- [x] 6a.2 RED+GREEN: same file — add nullable `Text`, meaningful only when `MarkType="text"` (D8). Tests: text mark carries literal (CAGH-PA-02-a), icon mark → `Text=null` (CAGH-PA-02-b).
- [x] 6a.3 RED+GREEN: `Infrastructure/.../MultimodalAestheticStyleExtractor.cs` — extend prompt/schema for `markType`/`text`; bump `AestheticFreshness.CurrentSchemaVersion`/`PromptVersion` to `aesthetic-extractor/v5` (D10). Test covers defensive parse of malformed `markType`/`text` tokens → null/unknown, never throw.
- [x] 6a.4 RED+GREEN: `AestheticProfilePersistence.cs` — add JSON keys for `MarkType`/`Text`. Tests: round-trip preserves both fields (FFS-PA-02-a), blob with only pre-hardening shape (`present`/`positionHint`/`colorHex`) deserializes `MarkType`/`Text` as null, pre-existing fields intact, no throw (FFS-PA-02-c).

## WU-6b: SourceImagePath + BoundingBox, Validation, Reliability Suite (Infrastructure, ~400 lines)

- [x] 6b.1 RED+GREEN: `Application/.../Shared/BrandMarkDetection.cs` — add nullable `SourceImagePath` and `BrandMarkBoundingBox(X, Y, Width, Height)` record (D8/D9).
- [x] 6b.2 RED+GREEN: `Infrastructure/.../MultimodalAestheticStyleExtractor.cs` — widen `loadedPairs` from `(AiImageInput, Caption)` to `(AiImageInput, Caption, StoragePath)` (D9); resolve the model's `Image {i}` **post-skip** `loadedPairs` index to `SourceImagePath`. Tests: valid index resolves to `StoragePath` (CAGH-PA-03-a); out-of-range/unresolvable index → null, no throw, rest of `BrandMarkDetection` unaffected (CAGH-PA-03-b); a failing `OpenAsync` on an earlier image correctly shifts subsequent index resolution (D9 post-skip regression).
- [x] 6b.3 RED+GREEN: same file — validate/normalise `BoundingBox`: X/Y/Width/Height finite in `[0,1]`, `X+Width≤1.0`, `Y+Height≤1.0`, normalised area ≥0.0009 (3%×3%); bump `CurrentSchemaVersion`/`PromptVersion` to `aesthetic-extractor/v6` (D10). Tests: valid box retained (CAGH-PA-04-a), below-minimum-area rejected → null (CAGH-PA-04-b), malformed/out-of-range rejected → null (CAGH-PA-04-c). **Note**: the design's `crop ≥24×24px after scaling` guard is validated later, at actual crop time, by WU-7b's `SkiaBrandMarkAssetExtractor` (task 7b.2's "sub-24×24 crop" failure mode) — it needs the source image's real pixel dimensions, which this extractor-side normalised-geometry check does not have/decode.
- [x] 6b.4 **Explicit** defensive-parsing suite: `Tests/Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractorTests.cs` — 11 canned-JSON cases, each MUST yield null and MUST NOT throw: missing `boundingBox`; non-numeric values; negative coords; `>1.0` values; zero-area; inverted (`Width<0`); `X+Width>1.0`; below-minimum-area; box present but `markType:"text"`; box present but `present:false`; source index out of range. Delivered as `DefensiveParsing_01`..`_11` in `MultimodalAestheticStyleExtractorTests.cs`. Non-numeric-value defence required widening `BoundingBoxDto`'s raw fields from `double?` to `JsonElement?` (a non-numeric JSON token deserializes cleanly as a non-Number-kind element instead of throwing `JsonException` at the `System.Text.Json` layer, before `BrandMarkDetection.TryValidateBoundingBox` ever runs).
- [x] 6b.5 **Explicit** fixture IoU measurement (non-gating): add 3-5 checked-in reference images with hand-annotated expected regions under `Tests/Infrastructure/Social/ContentGeneration/Fixtures/`; measure extractor bounding-box output against expected region with IoU (informal floor ≥0.5). This is measurement, not a pass/fail gate — deliverable is a **written reliability verdict** recorded below in [Bounding-Box Reliability Verdict](#bounding-box-reliability-verdict-wu-6b). If it fails, the WU-7b ADR (task 7b.5) records the decision to cut WU-7b/WU-7c. CAGH-PA-04-d, CAGH-PA-05. **Not executable in this offline apply session** — see the verdict section below for why and what an owner-run follow-up needs to do.

## WU-7a: Text-Mark Rendering (Application, ~340 lines — depends on WU-6a only)

- [x] 7a.1 RED+GREEN: `ContentGenerationJobProcessor.cs::BuildBrandMarkSlot` — when `MarkType="text"`, render extracted `Text` through the client's resolved caption typography (family/weight/size). Tests: literal text renders with caption typography (CAGH-CG-04-a), `Text=null` renders no text-mark content, no throw (CAGH-CG-04-b).
- [x] 7a.2 RED+GREEN: same method — `MarkType="unknown"` (including legacy/unrecognised) renders the pre-existing `PositionHint`+`ColorHex` chip identically to pre-change; `Present=false` renders nothing. CAGH-CG-07-a; FFS-CG-02 regression scenarios FFS-CG-02-a/b/c.

## WU-7b: Icon-Mark Asset Pipeline + ADR (Application/Infrastructure, ~640 lines — largest unit, matches proposal)

- [x] 7b.0 **Check current `git status` of `Incoders.Hive.ApiService/Program.cs` before writing the new DI registration** — the file is already dirty in the working tree from unrelated in-flight changes (design.md flag). Diff only the new `IBrandMarkAssetExtractor` line(s); do not revert or clobber pre-existing changes. **Note**: `Program.cs` was clean (no diff) by the time this batch resumed — DI registration lives in `InfrastructureContentGenerationExtensions.AddB1bRenderingAndDesigner`, reached from `Program.cs` via `DependencyInjection.cs`, so no `Program.cs` edit was needed at all.
- [x] 7b.1 RED+GREEN: `Application/Abstractions/Social/ContentGeneration/IBrandMarkAssetExtractor.cs` — create port: `Task<Result<StoredAssetRef>> ExtractAsync(StoredAssetKey targetKey, string sourceImagePath, BrandMarkBoundingBox box, CancellationToken ct)`; contract: never throws, `Failure` on any error.
- [x] 7b.2 RED+GREEN: `Infrastructure/Social/ContentGeneration/SkiaBrandMarkAssetExtractor.cs` — `OpenAsync(sourceImagePath)` → SkiaSharp crop(`BoundingBox`) → `IGeneratedAssetStorage.PutAssetAsync(RelativePath="brand-mark.png", upsert)`. New tests `Tests/Infrastructure/Social/ContentGeneration/SkiaBrandMarkAssetExtractorTests.cs` with in-memory `IGeneratedAssetStorage` fake + synthesised `SKBitmap`s: valid box → correct pixel rect; every failure mode (open, decode, invalid box, sub-24×24 crop, storage) → `Failure`, never throws.
- [x] 7b.3 GREEN: `Incoders.Hive.ApiService/Program.cs` — register `IBrandMarkAssetExtractor` → `SkiaBrandMarkAssetExtractor` in DI, honouring the 7b.0 git-status check. **Note**: delivered as `services.AddScoped<IBrandMarkAssetExtractor, SkiaBrandMarkAssetExtractor>()` in `InfrastructureContentGenerationExtensions.cs` next to the existing `IImageGuardrailValidator` registration (same DI seam `Program.cs` already reaches through `DependencyInjection.cs` for every other content-generation port) rather than a literal new line in `Program.cs` itself.
- [x] 7b.4 RED+GREEN: `ContentGenerationJobProcessor.cs::BuildBrandMarkSlot` (icon branch, wiring) — invoke `ExtractAsync` when `MarkType="icon"` and `BoundingBox` present; on `Failure`, log a degrade entry and fall through to the chip path. CAGH-CG-06-a (wiring half; full render behaviour completes in WU-7c). Tests (`ContentGenerationJobProcessorVisualTests.cs`): valid box+source invokes the extractor and still renders the chip this batch (no `<img>` yet — WU-7c's scope); extraction failure logs a warning and degrades to chip without throwing; missing bounding box/source never invokes the extractor at all.
- [x] 7b.5 Write ADR `docs/architecture/adr/{date}-brand-mark-asset-port.md` (D13): new `IBrandMarkAssetExtractor` port + SkiaSharp adapter; crop-only, no matting, no vision API, no logo regeneration; degrade contract; **MUST cite `DegenerateImageGuardrailValidator` (F4) as the existing SkiaSharp-behind-a-port precedent** rather than claiming a novel category; record the WU-6b IoU reliability verdict (task 6b.5) and, if it failed, the decision to cut WU-7b/WU-7c. Delivered as `docs/architecture/adr/0014-brand-mark-asset-port.md`.

## WU-7c: Icon-Mark Render + Ref Resolution (Application/Infrastructure, ~560 lines — depends on WU-7a and WU-7b)

- [x] 7c.1 RED+GREEN: `ContentGenerationJobProcessor.cs::BuildBrandMarkSlot` — on successful crop from 7b.4, emit `<img>` referencing the stored crop asset, replacing the empty coloured `<div>`. CAGH-CG-05-a. Delivered as a new optional `extractedIconAssetPath` parameter (icon branch renders `<img class="slot" data-slot="brand-mark" ... src="{path}" style="...width:112px;height:36px;object-fit:cover;border-radius:8px;">`, mirroring the enforced-background `<img>`'s `object-fit:cover` pattern, sized to the chip's own 112x36 footprint, not the full canvas).
- [x] 7c.2 RED+GREEN: same file — generalise render-time inlining (D11, F3): replace single `renderBackgroundStoragePath` with `HashSet<string> renderInlineRefs`, loop `InlineBackgroundUrlAsync` over it, add the brand-mark crop path when the icon slot is emitted; test asserts the brand-mark path is present in `renderInlineRefs` and gets inlined for the rasterized Chromium pass. Test: `IconMarkType_ExtractionSucceeds_CropPathIsInlinedForRenderPass_D11`.
- [x] 7c.3 RED+GREEN: `Infrastructure/Rendering/PptxExportAdapter.cs` — resolve the brand-mark ref as a second picture (reuse `ResolvePictureBytesAsync` unchanged), placed from `data-slot="brand-mark"` captured geometry (D12); unresolvable → omit the picture, export the slide (BGREF-04 semantics). Tests: PPTX resolves a second picture for a stored crop (CAGH-SBR-02-a), omits it cleanly on resolution failure with no leaked storage-path string, and both a background ref + brand-mark ref resolve independently (2 pictures). **Note**: fixed a latent `SlotRegex` bug surfaced by introducing the first SELF-CLOSING `<img data-slot="...">` element — the old regex had no tag-name anchor and would greedily match up to the NEXT unrelated closing tag; replaced with a tag-name-backreferenced regex (`\k<tag>`) that also treats `/>` as an empty-inner match. The brand-mark slot is also skipped in the text-box loop when rendered as a picture, to avoid a duplicate empty text box.
- [x] 7c.4 Confirm `StudioVisualMarkupResolver.cs` needs **no change** (F2 — resolver already inlines every `src="..."`, not only `BackgroundImageRef`); add/keep a regression test proving the brand-mark ref resolves in the Studio preview unmodified (CAGH-SBR-01-a) and a no-ref slide is unaffected (CAGH-SBR-01-b). **Verified accurate on current `dev` HEAD** — no resolver change needed; two new dedicated tests added confirming the generic `src="..."` regex already covers the brand-mark `<img>`.
- [x] 7c.5 RED+GREEN: `BuildBrandMarkSlot` — full degrade path: missing/invalid/below-minimum-area bounding box, or crop failed to store, renders `PositionHint`+`ColorHex` chip unchanged, no throw, degrade log entry emitted. CAGH-CG-06-a (completes 7b.4's wiring). Covered by WU-7b's pre-existing `IconMarkType_ExtractionFails_DegradesToChip_NeverThrows_CAGH_CG_06_a` and `IconMarkType_NoBoundingBox_ExtractorNeverCalled_DegradesToChip_CAGH_CG_06_a` — both re-verified green against the new `BuildBrandMarkSlot` signature (extractedIconAssetPath null → chip, unchanged).

## WU-8: Spec/Doc Sync + PRD Amendment (Docs, ~420 lines, last)

- [ ] 8.1 Merge the four ADDED/MODIFIED requirement sets from the spec-delta files into `openspec/specs/content-generation/spec.md`, `comfyui-image-provider/spec.md`, `profile-analysis/spec.md`, `content-studio-background-rendering/spec.md`.
- [ ] 8.2 Write the PRD §11 amendment (Profile Analysis / Mundo 2): brand-mark extraction now derives and stores a durable asset from analysed reference imagery, not just categorical analysis fields.
- [ ] 8.3 Confirm ADR `docs/architecture/adr/*-brand-mark-asset-port.md` (task 7b.5) is merged; cross-reference from the project's ADR index if one exists.
- [ ] 8.4 Sync any `docs/` pipeline description referencing typography/colour/brand-mark behaviour to match the new consuming call sites.
- [ ] 8.5 Run `pwsh -File scripts/gates/Run-Gates.ps1`; confirm all MUST gates pass before closing the change.

---

## Bounding-Box Reliability Verdict (WU-6b)

**Verdict: NOT MEASURABLE IN THIS SESSION.**

This `sdd-apply` batch ran offline, with no live vision-model (multimodal AI) access and no
owner-provided reference images or hand-annotated ground-truth bounding boxes. Task 6b.5 calls for:
(a) 3-5 checked-in reference images with hand-annotated expected icon regions, and (b) running the
*real* `MultimodalAestheticStyleExtractor` against a *real* configured vision route to see what
bounding box it actually returns for each image, then computing IoU between the model's output and
the hand-annotated ground truth.

Neither ingredient exists in this environment:

- No real client reference images with a known, hand-annotated icon-mark region were supplied or
  could be sourced without fabricating them (using stock/placeholder images with invented "expected"
  boxes would not measure the vision model's real-world reliability — it would just measure whether
  a made-up number matches another made-up number).
- No live call to a configured multimodal route (OpenAI/Anthropic vision) was made or is available
  from this offline apply context — `CandidateResolver()`/`CaptureGateway()` in the test suite are
  in-memory fakes that return canned JSON; they intentionally do NOT exercise the real vision model's
  actual geometric-localisation behaviour, which is precisely the thing being measured here.

Fabricating an IoU score (e.g. "measured mean IoU 0.62 across 4 fixtures") would be worse than no
number at all — it would misrepresent an untested claim as an empirical result and could wrongly
green-light WU-7b/WU-7c (or wrongly cut them) on invented evidence. Per the explicit instruction for
this batch, no such number is recorded.

**What this batch DID deliver toward WU-6b's reliability goal**: the defensive-parsing suite
(task 6b.4, 11 cases) proves the *code path* never throws and never fabricates a value on
malformed/adversarial model output — this is a correctness guarantee, not a reliability measurement.
It is a necessary precondition for trusting any future IoU measurement (a crash or a silently-wrong
value would corrupt the measurement itself), but it says nothing about whether the vision model's
*correctly-shaped* bounding boxes are geometrically accurate.

**Required owner-run follow-up before WU-7b/WU-7c start** (per task 6b.5's own gating language):

1. Pick 3-5 real client reference images (from an org with a genuinely recurring icon mark) already
   archived via `ProfilePostImageRef`/`IGeneratedAssetStorage`.
2. Hand-annotate the true icon bounding box on each (any simple image-annotation tool; record as
   normalised 0..1 `{x,y,width,height}`), commit them under
   `Tests/Infrastructure/Social/ContentGeneration/Fixtures/` alongside the images.
3. Run `MultimodalAestheticStyleExtractor.ExtractAsync` against a real configured `ai-aesthetic`
   route (not the test fakes) for each fixture; capture the returned `BoundingBox`.
4. Compute IoU between each returned box and its ground truth; record per-fixture IoU and the mean
   against the informal ≥0.5 floor.
5. Record the Ship / Cut-WU-7b-7c decision here, and carry it into the WU-7b ADR (task 7b.5) either
   way — a Ship verdict lets WU-7b/WU-7c proceed as designed; a Cut verdict means icon marks ship the
   WU-6a `PositionHint`+`ColorHex` chip treatment only (proposal risk table, "High" likelihood risk),
   and WU-7b/WU-7c tasks above are skipped.

Until that owner-run measurement lands, WU-7b/WU-7c MUST NOT be started from this session's output
alone — the code in this batch (defensive parsing, D9 index resolution) is a correct, tested
*plumbing* layer regardless of the verdict, but the reliability question task 6b.5 exists to answer
remains open.

## Key Learnings

1. WU-3's colour resolution has a hard sequencing dependency on extracting `WcagContrast` out of `ColorPairingDerivation` first.
2. WU-6b's bounding-box reliability measurement is a recorded verdict, not a pass/fail gate, and directly gates whether WU-7b/WU-7c proceed.
3. `StudioVisualMarkupResolver` needed no code change because it already inlines every `src` attribute, not only the background ref.
4. The DI registration for the new brand-mark port must be sequenced after checking `Program.cs`'s pre-existing uncommitted changes to avoid collision.
5. WU-7b remains the largest single work unit at roughly 640 changed lines, matching the proposal's estimate after full task-level decomposition.
