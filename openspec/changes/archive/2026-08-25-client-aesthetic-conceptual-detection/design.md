# Design — client-aesthetic-conceptual-detection (Art-Direction Method + Concept Ideation)

Hexagonal design that upgrades the aesthetic surface from "what the client's posts LOOK like" to "the
client's CONCEPTUAL METHOD — how they turn a message into an image" so a metaphor-driven client (Atlas =
metaphorical-editorial mono-color still-life/staged photography) is no longer flattened into the same generic
output as a literal-illustration client (Comet = literal tender watercolor). It builds DIRECTLY on the
already-landed `client-aligned-carousel-generation` surface (`SlideBackgroundKind`, `BackgroundPattern`,
`ColorPairing`, `ColorPairingDerivation`, `TypicalSlideCount`, the `aesthetic_profile_json` persistence seam)
and the Phase 2 extractor (`MultimodalAestheticStyleExtractor`). It does NOT rebuild that foundation — it adds
two additive nullable signals (conceptual method + corpus-grounded image-type ratio), migrates the extractor
input to ordered image↔caption PAIRS, and teaches `ImagePromptWriter` to IDEATE a concept in the detected
method. Scope STOPS at the digested instruction; the ComfyUI image backend is a separate change but the
contract it will consume is fixed here.

## Status
Design — architecture decisions resolved. All new capture lives as additive nullable fields serialized into
the EXISTING `aesthetic_profile_json` column (NO schema migration). Builds on `feat/claude-p-provider`. An ADR
under `docs/architecture/adr/` is authored at apply time for the port-signature migration (`require_adr_for_
architecture_changes`).

## Scope reminder (Non-Goals are hard boundaries)
Ships: a structured `ConceptualMethod` (method-type + rationale + cues), a corpus-grounded `ImageTypeRatio`,
the image↔caption pairing input contract, additive persistence + read-through, and concept-ideation prompt
writing constrained to the detected method/ratio. Does NOT: implement the ComfyUI/local image provider, change
any routing except pointing `ai-aesthetic` at a real vision model, add a human-curation UI, or touch Story
formats.

---

## 0. The decisions (read this first)

| # | Open question | Decision | One-line rationale |
|---|---------------|----------|--------------------|
| **OQ-1** | Conceptual-method shape | **Small STRUCTURED record** `ConceptualMethod(ArtDirectionMethod Method, string Rationale, IReadOnlyList<string> Cues)` with a CLOSED enum `ArtDirectionMethod { Unknown, MetaphoricalEditorial, LiteralIllustration, SymbolicStillLife, DocumentaryPhotography, GraphicTypographic, Mixed }`. Additive-nullable on `AestheticExtraction` + `ClientAestheticProfile`. Null record OR `Unknown` = cold-start parity. | The closed enum makes detection deterministic (model picks one listed token) and routable at ideation time; `Rationale`+`Cues` keep the nuance free-text without drift. Atlas→`MetaphoricalEditorial`, Comet→`LiteralIllustration`. |
| **OQ-2** | Ratio shape + robustness | **Corpus-grounded record** `ImageTypeRatio(double Photo, double FlatColor, double Decorator, int SampleSize)`, proportions ~sum 1.0, `SampleSize` = total slides observed across ALL analyzed posts. Derived by NORMALIZING aggregate counts the model reports over the whole attached corpus, NOT one carousel. Reconciles with the existing `BackgroundPattern`: pattern = ORDER (which slides are creative), ratio = MAGNITUDE (how many, statistically). | Splits the two concerns the proposal conflates: interleave (already captured) vs. proportion (new, robust). `SampleSize` is the confidence/grounding signal that fixes the single-carousel `TypicalSlideCount = pattern.Count` guess. |
| **OQ-3** | Image↔caption pairing contract | **Replace** the `IReadOnlyList<string> captions` param with `IReadOnlyList<ProfilePostImageCaptionPair>` (new Domain record `(ProfilePostImageRef Image, string? Caption)`). Pairing is **key-based on `ProfilePostImageRef.SourcePostId`** (NOT positional zip): each archived slide image is tagged with its PARENT POST caption. Missing caption → `Caption = null` (image-only); never throws. Migrate the single production call site + tests. | The caller already knows post→image→caption association (`SourcePostId`); a key-join is misalignment-proof. Replacing (not overloading) the signature kills the loose-block path that prevents metaphor learning. |
| **OQ-4** | Persistence shape | **Additive nullable keys in `aesthetic_profile_json`**: `conceptualMethod { method, rationale, cues[] }` and `imageTypeRatio { photo, flat, decorator, sampleSize }`. Old blobs missing the keys → null on Read (parity). Bump `PromptVersion` to `aesthetic-extractor/v2`. NO column migration (mirrors the prior change). | Same JSON-blob additive pattern already proven for `BackgroundPattern`/`ColorPairings`; forward/backward compatible; reversible by dropping the keys. |
| **OQ-5** | Ideation constraints | **`ImagePromptWriter` ideates ONLY when `ConceptualMethod` is present and the slide is a CREATIVE (Photo) slide.** It INVENTS a concept for THIS slide's message (`SlideCopySummary`) in the detected method, constrained to the captured `Cues` (no new aesthetic), text-free, depicting the message. Photo-vs-flat assignment is decided UPSTREAM (`CreativeSlideIndexResolver` from `BackgroundPattern`, honoring the ratio at assignment time) — the writer never reassigns. Null method → current "emulate the style" instruction runs unchanged (parity). | Ideation gated on the structured method + cues + slide message bounds drift; the ratio is honored at index-resolution time, so the writer stays single-responsibility. |
| **OQ-6** | Vision-model selection | **Point the `ai-aesthetic` capability step at a config-selected REAL vision model** (paid multimodal, e.g. GPT-4o-class / Claude vision via API). `claude -p` is excluded (CLI agent, refuses, ignores JSON). Refresh cadence = once per client, cached, refreshed every N days (config). Decision only — no provider impl here; routing stays config-driven via `IWorkflowConfigurationResolver`. | Analysis is INFREQUENT, so a quality paid model is cheap in aggregate; generation (frequent) routes to the free local provider in the separate change. Existing degrade-to-`Empty` path is untouched. |

---

## 1. Architecture approach

Dependency rule, unchanged: **Domain ← Application ← Infrastructure/Api**. No provider/EF/Supabase/ASP.NET
types in Domain or Application. The extractor stays Infrastructure; the new method/ratio fields are pure
Application value objects; the pairing input record is a pure Domain value type (it composes the existing
Domain `ProfilePostImageRef`). The change is overwhelmingly ADDITIVE on top of `client-aligned-carousel-
generation`.

- **Domain** (`Domain/Social/ProfileAnalysis/`): ONE new pure record `ProfilePostImageCaptionPair`
  (`ProfilePostImageRef Image`, `string? Caption`). `ProfilePostImageRef` unchanged.
- **Application** gains the conceptual vocabulary and consumes it:
  - New pure VOs in `…/ContentGeneration/Shared/`: `ArtDirectionMethod` (enum), `ConceptualMethod` (record),
    `ImageTypeRatio` (record).
  - `AestheticExtraction` (in `IClientAestheticStyleExtractor.cs`) + `ClientAestheticProfile` extended with
    additive-trailing nullable `ConceptualMethod?` and `ImageTypeRatio?`.
  - `IClientAestheticStyleExtractor.ExtractAsync` signature MIGRATED to take pairs.
  - `AestheticProfilePersistence` serializes/reads the two new sub-objects.
  - `ImagePromptWriter.BuildInput` + instruction gain the method-gated ideation branch.
- **Infrastructure** carries adapters/wiring only:
  - `MultimodalAestheticStyleExtractor`: extend the DTO (`conceptualMethod`, `imageTypeCounts`), extend the
    instruction, consume pairs (tag each image with its caption), `Map` the new fields, bump `PromptVersion`.
  - `EfClientAestheticProfileReader`: thread the two new fields out of `AestheticProfilePersistence.Read`.
  - `ProfileAnalyzerOrchestrator`: build pairs by joining `visionRefs` to post captions on `SourcePostId`;
    pass the new `promptVersion`.
  - `ai-aesthetic` route config points at a real vision model (config-only).

No new worker, no new job state, no schema migration.

---

## 2. Component map

| Element | Kind | Path | Change |
|---------|------|------|--------|
| `ProfilePostImageCaptionPair` | record | `Domain/Social/ProfileAnalysis/ProfilePostImageCaptionPair.cs` | **New** — `(ProfilePostImageRef Image, string? Caption)` |
| `ArtDirectionMethod` | enum | `…/ContentGeneration/Shared/ArtDirectionMethod.cs` | **New** — closed method vocabulary |
| `ConceptualMethod` | record | `…/ContentGeneration/Shared/ConceptualMethod.cs` | **New** — `(ArtDirectionMethod Method, string Rationale, IReadOnlyList<string> Cues)` |
| `ImageTypeRatio` | record | `…/ContentGeneration/Shared/ImageTypeRatio.cs` | **New** — `(double Photo, double FlatColor, double Decorator, int SampleSize)` |
| `AestheticExtraction` (+ `ConceptualMethod?`, `ImageTypeRatio?`) | record | `…/Abstractions/Social/ContentGeneration/IClientAestheticStyleExtractor.cs` | **Modified** — additive trailing nullable |
| `IClientAestheticStyleExtractor.ExtractAsync` | port | same file | **Modified** — pairs replace loose captions |
| `ClientAestheticProfile` (+ `ConceptualMethod?`, `ImageTypeRatio?`) | record | `…/Shared/ClientAestheticProfile.cs` | **Modified** — additive trailing nullable |
| `AestheticProfilePersistence` | static | `…/Shared/AestheticProfilePersistence.cs` | **Modified** — serialize/read new sub-objects |
| `ImagePromptWriter` (`BuildInput` + instruction) | use case | `…/Background/ImagePromptWriter.cs` | **Modified** — method-gated ideation |
| `MultimodalAestheticStyleExtractor` | adapter | `Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs` | **Modified** — DTO + instruction + pairs + `Map` + `v2` |
| `EfClientAestheticProfileReader` | adapter | `Infrastructure/Social/ContentGeneration/EfClientAestheticProfileReader.cs` | **Modified** — thread 2 new fields |
| `ProfileAnalyzerOrchestrator` | use case | `…/ProfileAnalyzer/Analyze/ProfileAnalyzerOrchestrator.cs` | **Modified** — build pairs (join on `SourcePostId`); pass `v2` |
| `ai-aesthetic` step route | config | workflow config | **Verified** — real vision model |

---

## 3. The new / extended contracts

### 3.1 Conceptual method (Application VOs — pure)
```csharp
public enum ArtDirectionMethod
{
    Unknown,                 // cold-start / undetected → parity
    MetaphoricalEditorial,   // Atlas: message → visual metaphor; mono-color still-life + staged photography
    LiteralIllustration,     // Comet: literal tender illustration of the subject
    SymbolicStillLife,
    DocumentaryPhotography,
    GraphicTypographic,
    Mixed,
}

public sealed record ConceptualMethod(
    ArtDirectionMethod Method,
    string Rationale,                 // the image↔message relationship in this client's terms
    IReadOnlyList<string> Cues);      // observable cues: "mono-color saturated backdrop", "object-as-metaphor", "staged human subject"

public sealed record ImageTypeRatio(
    double Photo, double FlatColor, double Decorator, int SampleSize); // proportions ~sum 1.0; SampleSize = slides observed
```
Additive on the extraction + profile (all default `null`; existing construction sites / `with` compile
unchanged):
```csharp
public sealed record AestheticExtraction(
    /* …existing… */
    ConceptualMethod? ConceptualMethod = null,   // OQ-1
    ImageTypeRatio? ImageTypeRatio = null);       // OQ-2
```

### 3.2 Pairing input (Domain — pure) and port migration (OQ-3)
```csharp
public sealed record ProfilePostImageCaptionPair(ProfilePostImageRef Image, string? Caption);

Task<AestheticExtraction> ExtractAsync(
    Guid organizationId,
    IReadOnlyList<ProfilePostImageCaptionPair> referenceImages,   // was (refs, captions)
    CancellationToken ct);
```
- Caller builds pairs by joining each archived `ProfilePostImageRef` to its parent post caption on
  `SourcePostId`. Each slide image therefore carries the MESSAGE of the post it belonged to.
- Caption absent / post had none → `Caption = null` (image-only). Extra captions with no image are dropped.
  Never throws; the whole extraction never fails on a missing caption.

### 3.3 Extractor DTO + instruction (Infrastructure)
- DTO gains `conceptualMethod { method, rationale, cues[] }` and `imageTypeCounts { photo, flat, decorator }`
  (aggregate counts across ALL attached posts). `Map` normalizes counts → `ImageTypeRatio` with
  `SampleSize = photo+flat+decorator`; parses `method` against the closed enum (unrecognized → `Unknown`).
- Instruction additions (deterministic elicitation): (1) "Identify the client's ART-DIRECTION METHOD — how a
  MESSAGE becomes an IMAGE — and return `method` as EXACTLY ONE of: [enum tokens]"; (2) "Each image is tagged
  with the caption (message) of its post; infer the message→visual relationship"; (3) "Count, across ALL
  attached posts, how many slides are photographic vs. flat-color vs. decorator and return `imageTypeCounts`".
- `PromptVersion` → `aesthetic-extractor/v2`; the orchestrator's `ProviderCallContext.Begin(promptVersion:)`
  is updated to match.

### 3.4 Ideation in `ImagePromptWriter.BuildInput` (OQ-5)
- Add to the `clientAesthetic` block: `conceptualMethod { method, rationale, cues }` and `imageTypeRatio`.
- Branch the `instruction`:
  - **Method present (and this is a creative slide):** "INVENT a single visual CONCEPT for THIS slide's
    message using the client's method `{method}` and ONLY these cues `{cues}`. For MetaphoricalEditorial,
    design a fresh mono-color conceptual still-life / staged metaphor of the message; for LiteralIllustration,
    a literal tender scene. The concept MUST depict the slide message, stay text-free, and not introduce
    aesthetics outside the cues."
  - **Method null/`Unknown`:** the EXISTING "emulate the client's style" instruction, verbatim — exact parity.

---

## 4. Flow

### 4.1 Capture (profile-analysis pipeline)
```
archive ─► visionRefs: IReadOnlyList<ProfilePostImageRef>   (each carries SourcePostId)
pair    ─► pairs = visionRefs.join(postsWithImages on SourcePostId) → (image, post.Caption?)   [OQ-3]
extract ─► MultimodalAestheticStyleExtractor.ExtractAsync(org, pairs):
            instruction(image+caption tags) → AestheticExtractionDto {
              palette?, descriptors, motifs, voice, slideMixPattern?,           (existing)
              conceptualMethod?, imageTypeCounts? }                              [OQ-1/OQ-2]
map     ─► ConceptualMethod (enum+rationale+cues), ImageTypeRatio = normalize(counts)
persist ─► AestheticProfilePersistence.Serialize → aesthetic_profile_json (additive keys; no migration) [OQ-4]
```

### 4.2 Apply (ideation at prompt-writing time)
```
profile ─► reader.GetProfile → { …, ConceptualMethod?, ImageTypeRatio?, BackgroundPattern? }
indices ─► creative slides resolved upstream from BackgroundPattern (ratio honored at assignment)   [OQ-5]
per creative slide ─► ImagePromptWriter.WriteAsync:
            method present → ideate concept in method, constrained to cues, text-free, depicts message
            method null    → emulate-style instruction (parity)
```

---

## 5. Degrade matrix (resilience — non-negotiable)

| Condition | Behaviour |
|-----------|-----------|
| `ConceptualMethod` null / `Unknown` | `ImagePromptWriter` runs the current emulate-style instruction (exact parity, OQ-5) |
| `ImageTypeRatio` null | Photo-vs-flat assignment stays driven by `BackgroundPattern`/current defaults (OQ-2) |
| Caption missing for a post | Pair `Caption = null`; image-only; extraction proceeds (OQ-3) |
| Old `aesthetic_profile_json` (pre-v2) | New keys absent → null on Read → parity (OQ-4) |
| `ai-aesthetic` route unavailable / call fails | Existing degrade to `AestheticExtraction.Empty` (unchanged, OQ-6) |
| No images resolved | `AestheticExtraction.Empty` (unchanged) |

Cold-start parity is preserved EXACTLY: a client with no conceptual data yields a null `ConceptualMethod` →
the ideation branch is skipped → byte-identical prompt-writing to today. No new code path runs unless the
field is populated.

---

## 6. Strict-hexagonal boundary mapping

| Concern | Layer | Type(s) |
|---------|-------|---------|
| Pairing input VO | **Domain** | `ProfilePostImageCaptionPair` (composes `ProfilePostImageRef`) |
| Conceptual method + ratio VOs | **Application** | `ArtDirectionMethod`, `ConceptualMethod`, `ImageTypeRatio`, extended `AestheticExtraction` / `ClientAestheticProfile` |
| Ideation prompt-building | **Application** | `ImagePromptWriter` |
| Persistence read/write of JSON keys | **Application** | `AestheticProfilePersistence` |
| Multimodal extraction mapping, vision call, instruction | **Infrastructure** | `MultimodalAestheticStyleExtractor` |
| Profile read projection, pair assembly, route config | **Infrastructure** | `EfClientAestheticProfileReader`, `ProfileAnalyzerOrchestrator` |

No provider/HTTP/EF/Supabase type appears in any Application/Domain signature. `TreatWarningsAsErrors=true`
respected: all new fields are nullable BY DESIGN (no `!` suppressors); enum parse failure resolves to `Unknown`
(no nullable-suppression), and `Cues`/`Rationale` default to empty/`""` when the model omits them.

---

## 7. Data model + migration verdict
- `ConceptualMethod` + `ImageTypeRatio` serialize as additive keys inside the EXISTING `aesthetic_profile_json`
  via `AestheticProfilePersistence`. **NO schema migration.** Old rows missing the keys deserialize to null →
  parity; `PromptVersion → v2` so new analyses populate them.
- The port-signature migration (OQ-3) is a code-only contract change (single production call site + tests).

**Migration verdict: NO schema migration. All changes are code-only or JSON-blob-additive.**

## 8. Test-first ordering (Strict TDD — `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj -c Release`)
1. **VOs** — `ArtDirectionMethod`, `ConceptualMethod`, `ImageTypeRatio`; extended `AestheticExtraction` /
   `ClientAestheticProfile` (additive nullable; existing sites + `with` compile unchanged). (OQ-1/OQ-2.)
2. **Pairing record + key-join** — `ProfilePostImageCaptionPair`; orchestrator joins on `SourcePostId`;
   missing caption → null; extra captions dropped. (OQ-3.)
3. **Extractor mapping** — DTO `conceptualMethod`/`imageTypeCounts`; `Map` normalizes counts → ratio
   (`SampleSize` correct), enum parse + `Unknown` fallback; pairs tag captions. (OQ-1/OQ-2.)
4. **Persistence round-trip** — `Serialize`/`Read` new keys; old blob (keys absent) → null (parity). (OQ-4.)
5. **Reader projection** — `EfClientAestheticProfileReader` surfaces the two new fields; null when absent.
6. **Ideation** — `ImagePromptWriter` ideates in-method with cues when method present; emits emulate-style
   instruction (parity) when null/`Unknown`; never bakes text; Atlas vs Comet fixtures. (OQ-5.)
7. **Parity** — full pipeline with no conceptual data reproduces today's output exactly.
8. **Gates + ADR + docs/spec sync** — `pwsh -File scripts/gates/Run-Gates.ps1`.

## 9. Decisions in depth

### OQ-1 — Structured `ConceptualMethod` (closed enum + rationale + cues)
**Decision.** A record with a CLOSED `ArtDirectionMethod` enum plus free-text `Rationale` and `Cues[]`.
**Rationale.** The enum is the deterministic, routable anchor the ideation branch switches on; the free-text
preserves nuance the proposal demands without letting detection drift run-to-run. Atlas →
`MetaphoricalEditorial`, Comet → `LiteralIllustration` are exact, testable mappings.
**Rejected.** Pure free-text (no deterministic routing; fuzzy across runs — the documented risk). A bare enum
(loses the nuance/cues ideation needs). A boolean metaphor-vs-literal flag (too coarse for >2 clients).

### OQ-2 — Corpus-grounded `ImageTypeRatio` separate from the interleave pattern
**Decision.** `ImageTypeRatio(photo, flat, decorator, sampleSize)` normalized from aggregate counts across ALL
attached posts; `BackgroundPattern` keeps the ORDER, the ratio carries the robust MAGNITUDE.
**Rationale.** The proposal's "~50% photo / 50% flat across multiple posts" is a corpus statistic, not a
single-carousel sequence; `SampleSize` is the grounding/confidence signal and fixes the current single-post
`TypicalSlideCount = pattern.Count` guess.
**Rejected.** Reusing the single `slideMixPattern` as the ratio (one carousel ≠ a statistic). A scalar
photo-fraction (loses decorator + sample size). Per-index distributions (over-specified, won't generalize).

### OQ-3 — Key-based image↔caption pairs replacing the loose block
**Decision.** Replace `IReadOnlyList<string> captions` with `IReadOnlyList<ProfilePostImageCaptionPair>`;
pair by `SourcePostId`; missing caption → null; migrate the one call site + tests.
**Rationale.** A key-join on `SourcePostId` is misalignment-proof and reflects reality (one post caption,
many slides); replacing (not overloading) removes the loose path that blocks metaphor learning.
**Rejected.** Positional zip (breaks on count mismatch — the documented risk). Keeping the loose block +
adding pairs (dual paths, dilutes the signal). Failing when captions are missing (violates resilience).

### OQ-4 — Additive JSON keys, prompt-version bump, no migration
**Decision.** `conceptualMethod` + `imageTypeRatio` as additive nullable keys in `aesthetic_profile_json`;
`PromptVersion → v2`; old blobs → null.
**Rationale.** Proven pattern from the prior change; forward/backward compatible; reversible; cold-start safe.
**Rejected.** Dedicated columns + EF migration (unnecessary; the blob seam exists). Overwriting v1 blobs
(loses reversibility / parity).

### OQ-5 — Method-gated ideation, ratio honored upstream
**Decision.** Ideate ONLY when `ConceptualMethod` is present AND the slide is creative; constrain to the
detected method + captured cues + slide message + text-free. Photo-vs-flat assignment stays upstream.
**Rationale.** Gating on the structured method bounds drift; keeping assignment in `CreativeSlideIndexResolver`
keeps `ImagePromptWriter` single-responsibility and the ratio authoritative at the right seam.
**Rejected.** Always ideate (breaks parity for cold-start clients). Let the writer reassign photo-vs-flat
(two owners of the ratio; race with the resolver). Unconstrained ideation (the over-creation/drift risk).

### OQ-6 — Real vision model on `ai-aesthetic`; config-driven; refresh every N days
**Decision.** Point `ai-aesthetic` at a config-selected real vision model; exclude `claude -p`; refresh
cadence every N days. Decision only — no provider implementation here.
**Rationale.** Analysis is infrequent + cached, so a paid quality vision model is cheap in aggregate;
generation (frequent) routes to the free local provider in the separate change. Routing stays config-driven;
the degrade-to-`Empty` path is untouched.
**Rejected.** `claude -p` for analysis (CLI agent — refuses, ignores JSON contracts). A cheap text-only model
(no vision; cannot read the posts). Per-generation vision analysis (defeats the infrequent-cache economics).

---

## 10. Architecture-gate compliance
- **No provider types in Domain/Application.** New VOs are pure; the vision SDK + instruction live ONLY in
  `MultimodalAestheticStyleExtractor`. The pairing record composes the existing Domain `ProfilePostImageRef`.
- **No hardcoded provider/model.** `ai-aesthetic` resolves per org via `IWorkflowConfigurationResolver`.
- **Additive + reversible.** Nullable JSON-blob keys + a code-only port migration; no schema change.
- **`TreatWarningsAsErrors=true`.** Nullable-by-design fields; no `!` suppressors; enum parse → `Unknown`.
- **Docs-sync (MUST):** `openspec/specs/*` deltas for `profile-analysis` and `content-generation`.
- **ADR (MUST):** the port-signature migration (loose captions → pairs) → ADR at apply time.
- Run `pwsh -File scripts/gates/Run-Gates.ps1` before completion.

## 11. Spec impact noticed (for `sdd-spec` / verify)
- **profile-analysis (modified):** extraction gains `ConceptualMethod` + corpus-grounded `ImageTypeRatio`;
  input becomes image↔caption pairs; `ai-aesthetic` targets a real vision model.
- **content-generation (modified):** `ImagePromptWriter` performs method-gated concept ideation honoring the
  detected method/cues; per-slide photo-vs-flat assignment unchanged (upstream).

## 12. Sliced implementation order + next step
1. **VOs + pairing record** — `ArtDirectionMethod`, `ConceptualMethod`, `ImageTypeRatio`,
   `ProfilePostImageCaptionPair`; extend `AestheticExtraction` + `ClientAestheticProfile`.
2. **Persistence** — `AestheticProfilePersistence` serialize/read; `EfClientAestheticProfileReader` threading.
3. **Extractor** — DTO + instruction + pairs + `Map` + `PromptVersion v2`.
4. **Caller migration** — `ProfileAnalyzerOrchestrator` builds pairs (join on `SourcePostId`); pass `v2`.
5. **Ideation** — `ImagePromptWriter.BuildInput` + branched instruction.
6. **Routing decision** — `ai-aesthetic` config points at a real vision model.
7. **ADR + docs/spec sync + gates.**

`sdd-tasks` owns the checklist; this design owns the HOW. Next: `sdd-tasks` (after the spec is also ready).
