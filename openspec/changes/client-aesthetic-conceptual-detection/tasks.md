# Tasks: client-aesthetic-conceptual-detection

**Branch:** `feat/client-aesthetic-conceptual-detection` (off `dev`, merges to `dev`).
**Delivery:** SINGLE PR with `size:exception` (user decision; one issue → one branch → one PR).
**TDD mode:** STRICT — every behavior task RED → GREEN.
**Test runner:** `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj -c Release`
**WDAC signing:** build first (`dotnet build -c Release`), sign the 4 Incoders DLLs
(thumbprint `162D6D5A0F56EDA79363B7EB475946778577042F`), then run `dotnet test --no-build`.
**No schema migration.** All changes are code-only or additive JSON-blob keys. This is lower risk than
the prior change (`client-aligned-carousel-generation`) — called out in the Forecast below.

---

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~900–1,200 |
| 400-line budget risk | **High** |
| No schema migration | **Yes** — lower risk than prior change; JSON-blob additive only |
| Chained PRs recommended | **No** — user chose a SINGLE PR with `size:exception` |
| Internal work-unit order | WU-1 → WU-2 → WU-3 → WU-4 → WU-5/WU-6 → WU-7 (one branch) |
| Delivery strategy | `single-pr` + `size:exception` |
| Chain strategy | n/a — one issue, one branch off `dev`, merges to `dev` |

Decision needed before apply: Resolved — single PR, `size:exception`
Chained PRs recommended: No
Chain strategy: n/a
400-line budget risk: High (accepted via `size:exception`)

### Suggested Work Units → PRs

| WU | Goal | Likely PR | Notes |
|----|------|-----------|-------|
| WU-1 | VOs + pairing record + additive profile/extraction fields | PR 1 | Foundation — all downstream WUs compile against these types |
| WU-2 | Persistence serialize/read + reader threading | PR 1 | Same seam; keeps PR 1 self-contained with a full round-trip |
| WU-3 | Extractor DTO + instruction + pairs + `Map` + `PromptVersion v2` | PR 2 | Detection pipeline; depends on WU-1+WU-2 types |
| WU-4 | Orchestrator caller migration (join on `SourcePostId`; pass `v2`) | PR 2 | Same pipeline seam; keeps PR 2 e2e testable |
| WU-5 | `ImagePromptWriter` method-gated ideation | PR 3 | Depends on profile fields from WU-1 |
| WU-6 | `ai-aesthetic` vision-model routing decision (config-only) | PR 3 | Config verification; minimal diff |
| WU-7 | ADR + docs/spec sync + gates | PR 3 | MUST gate; `require_adr_for_architecture_changes` |

---

## WU-1 — VOs + Pairing Record + Additive Profile / Extraction Fields

*Refs: CACD-PA-01 (ArtDirectionMethod), CACD-PA-02 (ImageTypeRatio), CACD-PA-03 (pairing record),
CACD-PA-04 (AestheticExtraction additive), CACD-PA-05 (ClientAestheticProfile additive);
design §3.1, §3.2, §0 OQ-1/OQ-2/OQ-3.*

### Phase 1.A — Application value objects (pure)

- [x] 1.1 **[RED]** Create `ArtDirectionMethodTests.cs` — assert enum members
  `{ Unknown, MetaphoricalEditorial, LiteralIllustration, SymbolicStillLife,
  DocumentaryPhotography, GraphicTypographic, Mixed }` exist; no Infrastructure types referenced;
  `ArtDirectionMethod.Unknown` is the zero-value; `Atlas → MetaphoricalEditorial`,
  `Comet → LiteralIllustration` (fixture constants declared for re-use).
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/ArtDirectionMethodTests.cs`)

- [x] 1.2 **[GREEN]** Create `ArtDirectionMethod.cs` — closed `public enum ArtDirectionMethod` with
  exactly the six named members; namespace `…ContentGeneration.Shared`; no dependencies.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ArtDirectionMethod.cs`)

- [x] 1.3 **[RED]** Create `ConceptualMethodTests.cs` — assert record construction
  `ConceptualMethod(ArtDirectionMethod Method, string Rationale, IReadOnlyList<string> Cues)`
  compiles; `Cues` is immutable; `with` expression compiles; no Infrastructure types.
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/ConceptualMethodTests.cs`)

- [x] 1.4 **[GREEN]** Create `ConceptualMethod.cs` — `public sealed record ConceptualMethod(
  ArtDirectionMethod Method, string Rationale, IReadOnlyList<string> Cues)`.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ConceptualMethod.cs`)

- [x] 1.5 **[RED]** Create `ImageTypeRatioTests.cs` — assert record construction
  `ImageTypeRatio(double Photo, double FlatColor, double Decorator, int SampleSize)` compiles;
  proportions field types correct; `with` expression compiles; no Infrastructure types.
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/ImageTypeRatioTests.cs`)

- [x] 1.6 **[GREEN]** Create `ImageTypeRatio.cs` — `public sealed record ImageTypeRatio(
  double Photo, double FlatColor, double Decorator, int SampleSize)`.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ImageTypeRatio.cs`)

### Phase 1.B — Domain pairing record (pure)

- [x] 1.7 **[RED]** Create `ProfilePostImageCaptionPairTests.cs` — assert record construction
  `ProfilePostImageCaptionPair(ProfilePostImageRef Image, string? Caption)` compiles;
  `Caption = null` is valid (image-only scenario; CACD-PA-03-b/d); no Infrastructure types;
  `ProfilePostImageRef` is the existing Domain type (field `SourcePostId` confirmed as `string`).
  (`Incoders.Hive.Tests/Domain/Social/ProfileAnalysis/ProfilePostImageCaptionPairTests.cs`)

- [x] 1.8 **[GREEN]** Create `ProfilePostImageCaptionPair.cs` —
  `public sealed record ProfilePostImageCaptionPair(ProfilePostImageRef Image, string? Caption)`
  in `Incoders.Hive.Domain.Social.ProfileAnalysis`; pure Domain record; no Application/Infrastructure
  imports.
  (`Incoders.Hive.Domain/Social/ProfileAnalysis/ProfilePostImageCaptionPair.cs`)

### Phase 1.C — Additive fields on `AestheticExtraction` + port signature (Application)

- [x] 1.9 **[RED]** Extend `AestheticExtractionExtensionTests.cs` — add assertions:
  - `AestheticExtraction` carries `ConceptualMethod? ConceptualMethod = null` and
    `ImageTypeRatio? ImageTypeRatio = null` as trailing nullable fields (CACD-PA-04-a/b/c).
  - Existing construction `new AestheticExtraction(palette, descriptors, motifs, voice,
    BackgroundPattern, ColorPairings)` still compiles unchanged (additive; CACD-PA-04-c).
  - `AestheticExtraction.Empty` remains valid (null for both new fields).
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/AestheticExtractionExtensionTests.cs`)

- [x] 1.10 **[GREEN]** Modify `IClientAestheticStyleExtractor.cs` — append two trailing nullable
  params to `AestheticExtraction`: `ConceptualMethod? ConceptualMethod = null` and
  `ImageTypeRatio? ImageTypeRatio = null`; migrate `ExtractAsync` signature to accept
  `IReadOnlyList<ProfilePostImageCaptionPair> referenceImages` replacing the two loose params
  `(IReadOnlyList<ProfilePostImageRef> referenceImages, IReadOnlyList<string> captions)` (OQ-3).
  (`Incoders.Hive.Application/Abstractions/Social/ContentGeneration/IClientAestheticStyleExtractor.cs`)
  **Note:** Additive fields on `AestheticExtraction` applied. `ExtractAsync` signature migration
  DEFERRED to WU-3/WU-4 (breaking change; would leave `MultimodalAestheticStyleExtractor` and
  `ProfileAnalyzerOrchestrator` uncompilable in isolation). Applied per option (a) from the
  coupling warning: signature migrated in the same commit as implementer+caller migration.

### Phase 1.D — Additive fields on `ClientAestheticProfile` (Application)

- [x] 1.11 **[RED]** Extend `ClientAestheticProfileExtensionTests.cs` — add assertions:
  - `ClientAestheticProfile` carries `ConceptualMethod? ConceptualMethod = null` and
    `ImageTypeRatio? ImageTypeRatio = null` as trailing nullable fields (CACD-PA-05).
  - Existing construction with no new params compiles unchanged (cold-start back-compat).
  - `with { ConceptualMethod = null }` compiles and yields null field.
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/ClientAestheticProfileExtensionTests.cs`)

- [x] 1.12 **[GREEN]** Modify `ClientAestheticProfile.cs` — append two trailing nullable params
  (after existing `ColorPairings`): `ConceptualMethod? ConceptualMethod = null` and
  `ImageTypeRatio? ImageTypeRatio = null`; no existing param order or defaults changed.
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ClientAestheticProfile.cs`)

- [x] 1.13 Run `dotnet test -c Release` — WU-1 tests pass; full suite green; no existing test broken
  (cold-start: all-null new fields → compilation unchanged at all existing construction sites).
  **Result:** Passed! Failed: 0, Passed: 2169, Skipped: 2, Total: 2171.

---

## WU-2 — Persistence Serialize/Read + Reader Threading

*Refs: CACD-PA-05 (persistence round-trip, old-blob parity); design §3.3, §0 OQ-4.*

### Phase 2.A — `AestheticProfilePersistence` round-trip for new fields

- [x] 2.1 **[RED]** Extend `AestheticProfilePersistenceTests.cs` — add assertions:
  - `Serialize` includes `conceptualMethod { method, rationale, cues[] }` and
    `imageTypeRatio { photo, flat, decorator, sampleSize }` as additive keys in the JSON blob
    (CACD-PA-05-a).
  - `Read` on that blob returns matching `ConceptualMethod` and `ImageTypeRatio` values (CACD-PA-05-a).
  - `Read` on a pre-v2 blob (keys absent) returns `null` for both new fields without throwing
    (CACD-PA-05-b cold-start parity).
  - `Serialize`/`Read` round-trip with `ConceptualMethod = null` and `ImageTypeRatio = null`
    succeeds; both fields remain null (CACD-PA-05-c).
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Shared/AestheticProfilePersistenceTests.cs`)

- [x] 2.2 **[GREEN]** Modify `AestheticProfilePersistence.cs` — add `conceptualMethod` and
  `imageTypeRatio` key serialization/deserialization; absent keys deserialize to null; no
  existing keys removed or renamed; `PromptVersion` field updated to `aesthetic-extractor/v2`
  for new blobs only (existing v1 blobs read unchanged).
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/AestheticProfilePersistence.cs`)

### Phase 2.B — `EfClientAestheticProfileReader` threading of new fields

- [x] 2.3 **[RED]** Add/extend `EfClientAestheticProfileReaderEnrichedTests.cs` (or equivalent)
  — assert:
  - When the stored blob carries `conceptualMethod` + `imageTypeRatio`, `GetProfileAsync`
    returns a `ClientAestheticProfile` with non-null `ConceptualMethod` and `ImageTypeRatio`.
  - When the stored blob has no new keys (pre-v2), both fields on the returned profile are null;
    no exception (CACD-PA-05-b).
  - No EF Core / Supabase / Infrastructure type appears in the returned `ClientAestheticProfile`
    (architecture gate; CACD-PA-04-c).
  (`Incoders.Hive.Tests/Infrastructure/Social/ContentGeneration/EfClientAestheticProfileReaderEnrichedTests.cs`)

- [x] 2.4 **[GREEN]** Modify `EfClientAestheticProfileReader.cs` — pass `ConceptualMethod` and
  `ImageTypeRatio` (read via `AestheticProfilePersistence.Read`) into the returned
  `ClientAestheticProfile`; null when absent.
  (`Incoders.Hive.Infrastructure/Social/ContentGeneration/EfClientAestheticProfileReader.cs`)

- [x] 2.5 Run `dotnet test -c Release` — WU-2 tests pass; persistence parity (pre-v2 blob → null
  new fields; all-null profile → no exception) confirmed; full suite green.
  **Result:** Passed! Failed: 0, Passed: 2175, Skipped: 2, Total: 2177.

---

## WU-3 — Extractor DTO + Instruction + Pairs + Map + `PromptVersion v2`

*Refs: CACD-PA-01-a/b/c (ConceptualMethod detection; Atlas/Comet exemplars),
CACD-PA-02-a/b/c (ImageTypeRatio corpus-grounded), CACD-PA-03-a/b/c/d (pairs);
design §3.3, §0 OQ-1/OQ-2/OQ-3.*

### Phase 3.A — Extractor DTO + mapping in `MultimodalAestheticStyleExtractor`

- [x] 3.1 **[RED]** Extend `MultimodalAestheticStyleExtractorTests.cs` — add assertions:
  - Given DTO JSON `conceptualMethod: { method: "MetaphoricalEditorial", rationale: "...",
    cues: ["mono-color saturated backdrop", "object-as-metaphor"] }` →
    mapped `ConceptualMethod.Method = ArtDirectionMethod.MetaphoricalEditorial`
    (CACD-PA-01-a; Atlas exemplar fixture).
  - Given DTO JSON `conceptualMethod: { method: "LiteralIllustration", ... }` →
    `ConceptualMethod.Method = ArtDirectionMethod.LiteralIllustration`
    (CACD-PA-01-b; Comet exemplar fixture).
  - Given DTO JSON `conceptualMethod: { method: "UnknownToken" }` → `Method = Unknown`
    (enum-parse fallback; CACD-PA-01-c / OQ-1 determinism).
  - Given DTO JSON with no `conceptualMethod` key → `ConceptualMethod = null`; extraction
    succeeds (CACD-PA-01-c).
  - Given DTO JSON `imageTypeCounts: { photo: 5, flat: 5, decorator: 0 }` →
    `ImageTypeRatio.Photo ≈ 0.5`, `FlatColor ≈ 0.5`, `Decorator ≈ 0.0`,
    `SampleSize = 10` (CACD-PA-02-a; Atlas ~50/50 exemplar).
  - Given DTO JSON `imageTypeCounts: { photo: 1, flat: 10, decorator: 2 }` →
    `ImageTypeRatio.Photo < 0.15` (CACD-PA-02-b; Comet low-photo exemplar).
  - Given DTO with no `imageTypeCounts` key → `ImageTypeRatio = null`; extraction succeeds
    (CACD-PA-02-c).
  - Pairs are forwarded: each `ProfilePostImageCaptionPair` with non-null `Caption` results
    in a tagged image+caption in the model input; `Caption = null` → image-only
    (CACD-PA-03-a/b).
  (`Incoders.Hive.Tests/Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractorTests.cs`)

- [x] 3.2 **[GREEN]** Modify `MultimodalAestheticStyleExtractor.cs`:
  - Change `ExtractAsync` parameter from `(refs, captions)` to
    `IReadOnlyList<ProfilePostImageCaptionPair> referenceImages` (OQ-3).
  - For each pair: tag the image attachment with its post caption when `Caption != null`;
    omit caption tag when `Caption == null` (image-only; never throws on missing caption).
  - Extend response DTO with `conceptualMethod { method, rationale, cues[] }` and
    `imageTypeCounts { photo, flat, decorator }` nullable fields.
  - `Map`: parse `method` against `ArtDirectionMethod` enum (unrecognized → `Unknown`);
    default empty/null `Rationale` to `""`; default null `Cues` to `[]`; build
    `ConceptualMethod` record; normalize `imageTypeCounts` → `ImageTypeRatio`
    where `SampleSize = photo + flat + decorator`; null counts → null ratio.
  - Bump `PromptVersion` constant to `aesthetic-extractor/v2`.
  - Extend instruction text with three deterministic-elicitation additions per design §3.3.
  (`Incoders.Hive.Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs`)

- [x] 3.3 Run `dotnet test -c Debug` — WU-3 tests pass; Atlas and Comet exemplar fixtures
  return distinguishable `ConceptualMethod` and `ImageTypeRatio` values; full suite green.
  **Result:** Passed! Failed: 0, Passed: 2191, Skipped: 2, Total: 2193 (Debug config; WDAC
  blocks Release assembly from bash shell — test evidence in Debug output; Release build compiles
  clean with 0 errors/warnings).

---

## WU-4 — Caller Migration: `ProfileAnalyzerOrchestrator` Builds Pairs (Join on `SourcePostId`)

*Refs: CACD-PA-03-a/b/c/d (pairing; key-join; missing caption → null);
design §4.1, §0 OQ-3. Field name confirmed: `ProfilePostImageRef.SourcePostId` (string).*

### Phase 4.A — Pair-building logic in orchestrator

- [x] 4.1 **[RED]** Add `ProfileAnalyzerOrchestratorPairsTests.cs` — assert:
  - Given `visionRefs` with `SourcePostId = "P1"` and a `ProfilePost` with `PostId = "P1"`
    and `Caption = "hello"` → resulting pair has `Image.SourcePostId = "P1"` and
    `Caption = "hello"` (CACD-PA-03-a key-join).
  - Given a `visionRef` with `SourcePostId = "P2"` and no matching post in `postsWithImages`
    → pair `Caption = null`; no exception (CACD-PA-03-b image-only fallback).
  - Given a post with `Caption = null` (post has no caption) → pair `Caption = null`;
    no exception (CACD-PA-03-d).
  - Extra posts with no matching `visionRef` image are silently dropped from pairs output.
  - `ExtractAsync` is called with the resulting pairs list, not the old
    `(refs, captions)` params.
  (`Incoders.Hive.Tests/Application/Social/ProfileAnalyzer/ProfileAnalyzerOrchestratorPairsTests.cs`)

- [x] 4.2 **[GREEN]** Modify `ProfileAnalyzerOrchestrator.cs` — replace the loose
  `captions` list with a key-join:
  ```
  var pairs = visionRefs
      .Select(r => new ProfilePostImageCaptionPair(
          r,
          postsWithImages.FirstOrDefault(p => p.PostId == r.SourcePostId)?.Caption))
      .ToList();
  ```
  Pass `pairs` to `_aestheticExtractor.ExtractAsync(orgId, pairs, ct)`.
  Update `ProviderCallContext.Begin(promptVersion: "aesthetic-extractor/v2", ...)`.
  (`Incoders.Hive.Application/Features/Social/ProfileAnalyzer/Analyze/ProfileAnalyzerOrchestrator.cs`)

- [x] 4.3 Run `dotnet test -c Debug` — WU-4 tests pass; key-join verified;
  missing-caption-null verified; full suite green (all call sites for `ExtractAsync` updated).
  **Result:** Passed! Failed: 0, Passed: 2191, Skipped: 2, Total: 2193 (shared green run with 3.3).

---

## WU-5 — `ImagePromptWriter` Method-Gated Ideation

*Refs: CACD-CG-01-a/b/c/d (ideation; Atlas/Comet exemplars; null→no ideation),
CACD-CG-02-a/b/c (ratio-aware), CACD-CG-03-a (cold-start parity),
CACD-IMG-01-a/b/c/d (concept-decided prompt at generation boundary),
CACD-IMG-02-a (cold-start: pipeline identical),
CACD-PA-07-a (full cold-start parity);
design §3.4, §0 OQ-5.*

### Phase 5.A — Ideation tests (Atlas, Comet, null, ratio-aware)

- [x] 5.1 **[RED]** Add `ImagePromptWriterIdeationTests.cs` — assert:
  - Given `ConceptualMethod.Method = MetaphoricalEditorial` and `Cues = ["mono-color backdrop",
    "object-as-metaphor"]` → built instruction contains a specific metaphorical concept
    for the slide topic, constrained to those cues; instruction does NOT contain "text",
    "label", "@", or "handle" directives (CACD-CG-01-a; CACD-IMG-01-a).
  - Given `ConceptualMethod.Method = LiteralIllustration` and `Cues = ["tender watercolor",
    "children characters"]` → instruction contains a literal tender scene concept for the
    slide topic (CACD-CG-01-b; CACD-IMG-01-b).
  - Given the SAME slide topic, Atlas vs Comet profiles → two instructions are
    discernibly different (CACD-CG-01-c; CACD-IMG-01-c).
  - Given `ConceptualMethod = null` → NO ideation section added; instruction is
    byte-identical to the pre-change emulate-style instruction for the same inputs
    (CACD-CG-01-d; CACD-CG-03-a; CACD-IMG-02-a; CACD-PA-07-a).
  - Given `ArtDirectionMethod.Unknown` → treated as null; emulate-style instruction
    unchanged (parity).
  - Given `ImageTypeRatio` non-null and a photo-type slide → instruction includes a
    photographic/staged-photography background cue (CACD-CG-02-a).
  - Given `ImageTypeRatio` non-null and a flat-type slide → instruction includes a
    flat/mono-color background cue (CACD-CG-02-b).
  - Given `ImageTypeRatio = null` → no ratio cue added; instruction unchanged
    (CACD-CG-02-c).
  - Parity test: `ConceptualMethod = null`, `ImageTypeRatio = null` → instruction
    identical to baseline (CACD-CG-03-a; cold-start parity; scope is INSTRUCTION TEXT,
    not LLM output).
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/ImagePromptWriterIdeationTests.cs`)

- [x] 5.2 **[GREEN]** Modify `ImagePromptWriter.cs` — in `BuildInput`/instruction assembly:
  - Add `conceptualMethod { method, rationale, cues }` and `imageTypeRatio` to the
    `clientAesthetic` block when non-null.
  - Branch instruction:
    - `ConceptualMethod != null && Method != Unknown && slide is creative` →
      method-gated ideation instruction per design §3.4 (constrained to cues + slide message;
      text-free; method-specific).
    - Otherwise → existing "emulate the client's style" instruction, verbatim (parity).
  - Photo-vs-flat assignment is NOT reassigned here; writer reads slide type from upstream
    (single-responsibility per OQ-5).
  (`Incoders.Hive.Application/Features/Social/ContentGeneration/Background/ImagePromptWriter.cs`)

- [x] 5.3 **[PARITY]** Add `ImagePromptWriterColdStartParityTests.cs` — assert full pipeline
  with no conceptual data (null `ConceptualMethod`, null `ImageTypeRatio`) produces
  instruction text BYTE-IDENTICAL to the pre-change baseline for the same
  `ClientAestheticProfile` and slide inputs; no ideation section, no ratio section
  (CACD-PA-07-a; CACD-IMG-02-a). This test is a required Definition-of-Done item.
  (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Background/ImagePromptWriterColdStartParityTests.cs`)

- [x] 5.4 Run `dotnet test -c Release` — WU-5 tests pass; cold-start parity test green;
  Atlas and Comet exemplar instructions distinguishably different; full suite green.
  **Result:** Passed! Failed: 0, Passed: 2206, Skipped: 2, Total: 2208 (Debug config; WDAC blocks
  Release assembly from bash shell — Release build compiles clean with 0 errors/warnings).

---

## WU-6 — `ai-aesthetic` Vision-Model Routing Decision (Config Verification)

*Refs: CACD-PA-06-a/b; design §0 OQ-6.*

### Phase 6.A — Routing config verification

- [x] 6.1 **[GREEN]** Verify/update workflow configuration — confirm that `ai-aesthetic` /
  `extract-aesthetic` step capability key resolves via `IWorkflowConfigurationResolver`
  to a vision-capable model (e.g. GPT-4o-class or Claude vision via API); confirm
  `claude -p` binary is NOT referenced in any route configuration or DI wiring
  for this step (CACD-PA-06-a). Document the configured model key in a code comment
  at the resolution site. Other workflow steps must remain unaffected (CACD-PA-06-b).
  (Workflow config file or `DependencyInjection.cs` `ai-aesthetic` binding)

- [x] 6.2 **[RED]** Add `AiAestheticRoutingTests.cs` — assert:
  - `IWorkflowConfigurationResolver` resolves `ai-aesthetic` to a non-null, non-`claude-p`
    provider key in the test environment configuration (CACD-PA-06-a).
  - No test-environment route for `ai-aesthetic` resolves to the `claude -p` provider alias
    (CACD-PA-06-a guard).
  (`Incoders.Hive.Tests/Infrastructure/Social/ContentGeneration/AiAestheticRoutingTests.cs`)

- [x] 6.3 Run `dotnet test -c Release` — WU-6 tests pass; routing guard green.
  **Result:** Passed! Failed: 0, Passed: 2206, Skipped: 2, Total: 2208 (shared run with 5.4).

---

## WU-7 — ADR + Docs/Spec Sync + Gates

*Refs: design §10; `openspec/config.yaml` `require_adr_for_architecture_changes: true`;
project `CLAUDE.md` docs-sync MUST.*

- [x] 7.1 **[GREEN]** Author ADR
  `docs/architecture/adr/0009-pairs-contract-migration-conceptual-method-vision-routing.md`
  — document:
  - Port-signature migration `(refs, captions)` → `IReadOnlyList<ProfilePostImageCaptionPair>`:
    key-join on `SourcePostId`; replaces positional zip; missing caption → null
    (single call site + tests migrated; OQ-3).
  - Closed `ArtDirectionMethod` enum rationale: deterministic routing at ideation time;
    unrecognized model output → `Unknown` (no nullable suppression; OQ-1).
  - `ImageTypeRatio` corpus-grounded from aggregate counts (not single-carousel);
    `SampleSize` as confidence signal; kept separate from `BackgroundPattern` order (OQ-2).
  - `ai-aesthetic` routes to a real vision model; `claude -p` excluded (CLI agent;
    refuses JSON contracts; OQ-6).
  - Architecture gate compliance: no provider/EF/Supabase type in Domain or Application;
    `TreatWarningsAsErrors=true`; nullable-by-design; no `!` suppressors.
  (`docs/architecture/adr/0009-pairs-contract-migration-conceptual-method-vision-routing.md`)

- [x] 7.2 **[GREEN]** Merge spec deltas into living specs:
  - `openspec/changes/client-aesthetic-conceptual-detection/specs/profile-analysis/spec-delta.md`
    → append to `openspec/specs/profile-analysis/spec.md`.
  - `openspec/changes/client-aesthetic-conceptual-detection/specs/content-generation/spec-delta.md`
    → append to `openspec/specs/content-generation/spec.md`.
  - `openspec/changes/client-aesthetic-conceptual-detection/specs/image-generation/spec-delta.md`
    → append to `openspec/specs/image-generation/spec.md`.

- [ ] 7.3 Run `pwsh -File scripts/gates/Run-Gates.ps1`; report the gate summary.
  All MUST gates must pass: `tests.backend`, `architecture.layering`, `architecture.imports`,
  `secrets`, `security`, `vulnerabilities`. No MUST gate failure is acceptable.

---

## Parallelism Notes

| Parallelism | WUs | Constraint |
|-------------|-----|-----------|
| Sequential: WU-1 → WU-2 | WU-1, WU-2 | Persistence depends on VOs from WU-1 |
| Sequential: WU-2 → WU-3 | WU-2, WU-3 | Extractor `Map` outputs `ConceptualMethod`/`ImageTypeRatio` types from WU-1; persistence seam from WU-2 |
| Sequential: WU-3 → WU-4 | WU-3, WU-4 | Orchestrator calls the migrated `ExtractAsync` signature from WU-3 |
| WU-5 can start once WU-1 complete | WU-5 | `ImagePromptWriter` reads profile fields; does not depend on extractor impl |
| WU-6 can run in parallel with WU-5 | WU-6 | Config-only; no type dependency on WU-3/WU-4 |
| WU-7 requires all prior WUs complete | WU-7 | ADR + gate run is the terminal step |

---

## Definition of Done

- [ ] WU-1..WU-7 tasks completed and committed in seam order (test + implementation in the same
  commit per behavior; RED commit followed by GREEN commit).
- [ ] `dotnet test -c Release` (post WDAC sign) passes with 0 failures across the full suite.
- [ ] **Cold-start parity test** (`ImagePromptWriterColdStartParityTests`) confirms: null
  `ConceptualMethod` + null `ImageTypeRatio` → instruction text BYTE-IDENTICAL to
  pre-change baseline; scope is the INSTRUCTION TEXT, not LLM output (CACD-PA-07-a).
- [ ] **Atlas/Comet exemplar tests** confirm `MetaphoricalEditorial` and `LiteralIllustration`
  produce distinguishably different instructions for the same slide topic (CACD-CG-01-c).
- [ ] **Pre-v2 blob parity** (`AestheticProfilePersistenceTests`) confirms old blobs without new
  keys deserialize with null `ConceptualMethod` + null `ImageTypeRatio`; no exception (CACD-PA-05-b).
- [ ] **Key-join test** (`ProfileAnalyzerOrchestratorPairsTests`) confirms pairs built on
  `SourcePostId` (string field on `ProfilePostImageRef`); missing caption → null; no exception.
- [ ] **No schema migration.** Zero EF migrations added in this change.
- [ ] No provider/HTTP/EF/Supabase type appears in any Domain or Application signature
  (architecture gate; design §6).
- [ ] `TreatWarningsAsErrors=true` respected: no `!` suppressors; enum parse → `Unknown`;
  nullable-by-design throughout (design §6).
- [ ] ADR `0009-pairs-contract-migration-conceptual-method-vision-routing.md` authored under
  `docs/architecture/adr/`.
- [ ] Spec deltas merged into `openspec/specs/` living files (all three: profile-analysis,
  content-generation, image-generation).
- [ ] `pwsh -File scripts/gates/Run-Gates.ps1` exits 0; no MUST gate failure.
- [ ] PR split decision confirmed (ask-on-risk): stacked-to-main or feature-branch-chain
  chosen by user before `sdd-apply` launches.
