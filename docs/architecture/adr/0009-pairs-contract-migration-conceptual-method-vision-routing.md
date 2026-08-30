# ADR 0009 - Pairs Contract Migration, Conceptual Method Vocabulary, and Vision-Model Routing

## Status
Accepted

## Context

The `client-aesthetic-conceptual-detection` change upgrades the aesthetic-analysis surface
from capturing what a client's posts look like (visual surface: palette, descriptors,
background pattern) to capturing the client's **conceptual method** — how they turn a
message into an image. Two client exemplars drive the design: Atlas
(`@atlas.editorial`), whose method is metaphorical-editorial (abstract psychology
messages → mono-color staged still-life + photography), and Comet Comedy, whose
method is literal-illustration (tender watercolor characters with warm cream palette).

The change introduces four architectural decisions that require an ADR under
`require_adr_for_architecture_changes`:

1. **Port-signature migration** — `IClientAestheticStyleExtractor.ExtractAsync` previously
   accepted loose parameters `(IReadOnlyList<ProfilePostImageRef>, IReadOnlyList<string>
   captions)`; the two-parameter block made the metaphor-learning signal structurally
   impossible (the vision model saw images and captions as unrelated blocks with no
   pairing signal).

2. **Closed enum for `ArtDirectionMethod`** — method detection returns a value from a fixed
   vocabulary rather than free text; the `Unknown` fallback semantics and the
   `TreatWarningsAsErrors`-safe parse path are recorded here.

3. **`ImageTypeRatio` as a corpus statistic** — the ratio of photographic vs. flat-color vs.
   decorator slides is derived from aggregate counts across the entire attached feed sample,
   not inferred from a single carousel, and is kept separate from `BackgroundPattern` (which
   carries interleave ORDER, not frequency magnitude).

4. **`ai-aesthetic` vision-model routing** — the `ai-aesthetic` / `extract-aesthetic`
   workflow step is pointed at a real vision-capable model; `claude -p` is excluded.

This change builds directly on the `client-aligned-carousel-generation` surface
(`BackgroundPattern`, `ColorPairing`, `AestheticProfilePersistence`,
`aesthetic_profile_json` JSON-blob seam) and the Phase 2 extractor
(`MultimodalAestheticStyleExtractor`). No schema migration is introduced.

## Decision

### 1. Port-signature migration — `(refs, captions)` → `IReadOnlyList<ProfilePostImageCaptionPair>`

`IClientAestheticStyleExtractor.ExtractAsync` is migrated from:

```csharp
Task<AestheticExtraction> ExtractAsync(
    Guid organizationId,
    IReadOnlyList<ProfilePostImageRef> referenceImages,
    IReadOnlyList<string> captions,
    CancellationToken ct);
```

to:

```csharp
Task<AestheticExtraction> ExtractAsync(
    Guid organizationId,
    IReadOnlyList<ProfilePostImageCaptionPair> referenceImages,
    CancellationToken ct);
```

`ProfilePostImageCaptionPair` is a new pure Domain record:

```csharp
public sealed record ProfilePostImageCaptionPair(ProfilePostImageRef Image, string? Caption);
```

The caller (`ProfileAnalyzerOrchestrator`) builds pairs by **key-joining** each archived
`ProfilePostImageRef` to its parent post caption on `SourcePostId` (the `string` field
already present on `ProfilePostImageRef`):

```csharp
var pairs = visionRefs
    .Select(r => new ProfilePostImageCaptionPair(
        r,
        postsWithImages.FirstOrDefault(p => p.PostId == r.SourcePostId)?.Caption))
    .ToList();
```

Missing caption → `Caption = null` (image-only pair). Extra captions with no matching image
are dropped. The extraction never throws on absent captions.

**Single call site.** `ProfileAnalyzerOrchestrator` is the only production caller. The
migration is complete and fully tested in this change (WU-3 + WU-4).

**Rationale.** A key-join on `SourcePostId` is misalignment-proof: the caller already knows
the post↔image↔caption association, so aligning by identity rather than position eliminates
any count-mismatch risk that existed with the prior positional zip. Replacing (not
overloading) the signature removes the loose-block path whose existence prevented metaphor
learning — the vision model could not learn the message→image mapping because it saw images
and captions as detached, unrelated blocks.

**Rejected alternatives:**
- *Positional zip* — breaks silently on count mismatch; the risk was documented when the
  loose block was introduced.
- *Keep the loose block and add pairs as a second overload* — creates dual paths and dilutes
  the detection signal; no scenario justifies maintaining both paths simultaneously.
- *Fail when a caption is missing* — violates the resilience contract; posts may legitimately
  have no caption.

---

### 2. Closed `ArtDirectionMethod` enum and `ConceptualMethod` record

```csharp
public enum ArtDirectionMethod
{
    Unknown,                 // cold-start / undetected → exact parity with pre-change behavior
    MetaphoricalEditorial,   // Atlas: message → visual metaphor; mono-color still-life + staged photography
    LiteralIllustration,     // Comet: literal tender illustration of the subject
    SymbolicStillLife,
    DocumentaryPhotography,
    GraphicTypographic,
    Mixed,
}

public sealed record ConceptualMethod(
    ArtDirectionMethod Method,
    string Rationale,
    IReadOnlyList<string> Cues);
```

`ArtDirectionMethod.Unknown` is the zero-value and the cold-start / undetected sentinel.
When the vision model returns a method token that is not one of the six named members, the
enum parser resolves to `Unknown`. This is the `TreatWarningsAsErrors=true`-safe path: no
`!` nullable-suppression operator is needed anywhere in this change; flow analysis handles
the null-check on the optional `ConceptualMethod` record. A null `ConceptualMethod` record
OR `Method == Unknown` both trigger the cold-start parity path in `ImagePromptWriter` (the
pre-change emulate-style instruction, verbatim; byte-identical to the prior output).

**Rationale.** The closed enum makes detection **deterministic and routable at ideation
time**: the instruction tells the vision model to return EXACTLY ONE of the listed tokens,
so `ImagePromptWriter` can branch on a specific enum member rather than parsing free text.
`Rationale` + `Cues` preserve human-readable nuance without introducing run-to-run drift.
Atlas → `MetaphoricalEditorial`, Comet → `LiteralIllustration` are exact, testable
mappings confirmed by the exemplar tests in WU-1, WU-3, and WU-5.

**Rejected alternatives:**
- *Pure free-text* — no deterministic routing; fuzzy across model runs; cannot gate
  `ImagePromptWriter` ideation without fragile text parsing.
- *Bare enum without Rationale/Cues* — loses the nuance the ideation branch needs to
  constrain concept invention to the client's observable cues.
- *A boolean metaphor-vs-literal flag* — too coarse for more than two clients; cannot
  accommodate `SymbolicStillLife`, `DocumentaryPhotography`, `GraphicTypographic`, or
  `Mixed` without additional flags.

---

### 3. `ImageTypeRatio` — corpus-grounded; separate from `BackgroundPattern`

```csharp
public sealed record ImageTypeRatio(
    double Photo, double FlatColor, double Decorator, int SampleSize);
```

Proportions sum to approximately 1.0. `SampleSize = photo + flat + decorator` (aggregate
counts across ALL posts attached to the extraction call, not a single carousel).
A null `ImageTypeRatio` means an insufficient sample; the system degrades silently.

`ImageTypeRatio` is **kept separate from `BackgroundPattern`**. `BackgroundPattern`
captures the creative-slide INTERLEAVE ORDER — which slide positions within a carousel are
creative. `ImageTypeRatio` captures the raw FREQUENCY MAGNITUDE across the feed. The two
signals answer different questions: `BackgroundPattern` decides which positions are creative;
the ratio grounds that decision with feed-level statistics. Photo-vs-flat assignment happens
upstream in `CreativeSlideIndexResolver` at job-planning time, not in `ImagePromptWriter`,
keeping the writer single-responsibility.

**Rationale.** The prior change's `TypicalSlideCount = pattern.Count` was a single-carousel
proxy for a feed-level statistic. `SampleSize` is the confidence / grounding signal that
exposes how many slide images the model actually observed. Separating ORDER from MAGNITUDE
preserves the correctness of `BackgroundPattern` while adding a new, independent
feed-frequency signal.

**Rejected alternatives:**
- *Reuse `slideMixPattern` as the ratio* — one carousel ≠ a corpus statistic; conflates
  two different questions about creative-slide distribution.
- *A scalar photo-fraction only* — loses decorator count and `SampleSize` (the confidence
  signal that distinguishes a reliable ratio from a weak one).
- *Per-index distributions* — over-specified; won't generalize across clients with different
  carousel lengths.

---

### 4. `ai-aesthetic` routes to a real vision model; `claude -p` excluded

The `ai-aesthetic` / `extract-aesthetic` workflow step is configured to resolve via
`IWorkflowConfigurationResolver` to a **real, vision-capable model**. The configured key
in this change is `openai` / `gpt-4o` (documented in a code comment at
`MultimodalAestheticStyleExtractor.ResolveStepAsync`, WU-6). The resolver is config-driven
and org-scoped; any org-configured vision-capable provider (GPT-4o-class, Claude vision via
API) is valid.

`claude -p` (the Claude Code CLI agent) is **explicitly excluded** and its exclusion is
enforced by a test (`AiAestheticRoutingTests`, WU-6) that asserts no test-environment route
for `ai-aesthetic` resolves to the `claude-p` provider alias.

**Rationale.** `claude -p` is a conversational CLI agent, not a multimodal API endpoint; it
converses interactively and does not honor structured JSON response contracts. Aesthetic
extraction requires image input and a structured JSON output (`AestheticExtractionDto`),
which the CLI agent cannot satisfy. Profile analysis is infrequent and the result is cached
per client, so a paid quality vision model is acceptable in aggregate cost. The exclusion is
design-enforced (no route for this step can reference `claude -p`) and test-verified.

**Rejected alternatives:**
- *`claude -p` for extraction* — refuses image input; ignores JSON contracts; documented
  failure mode that motivated this routing decision.
- *A cheap text-only model* — cannot process image inputs; cannot observe the posts.
- *Per-generation vision analysis* — defeats the infrequent-cache economics; extraction
  runs once per client per refresh cycle, generation runs per job.

---

### 5. Architecture-gate compliance

All new types follow the hexagonal boundary:

| Concern | Layer | Type(s) |
|---------|-------|---------|
| Pairing input VO | Domain | `ProfilePostImageCaptionPair` (composes `ProfilePostImageRef`) |
| Conceptual method + ratio VOs | Application | `ArtDirectionMethod`, `ConceptualMethod`, `ImageTypeRatio`; additive nullable fields on `AestheticExtraction` / `ClientAestheticProfile` |
| Ideation prompt-building | Application | `ImagePromptWriter.BuildInput` (method-gated branch) |
| Persistence read/write of new JSON keys | Application | `AestheticProfilePersistence` (additive nullable keys in `aesthetic_profile_json`) |
| Vision extraction, instruction, pair-tagging, Map | Infrastructure | `MultimodalAestheticStyleExtractor` ONLY |
| Profile read projection, pair assembly, route config | Infrastructure | `EfClientAestheticProfileReader`, `ProfileAnalyzerOrchestrator`, `ai-aesthetic` config |

No provider SDK type, EF Core type, `Npgsql` type, or ASP.NET type appears in any
Application or Domain signature. `TreatWarningsAsErrors=true` is respected throughout: all
new fields are nullable BY DESIGN; enum parse failure resolves to `Unknown`; no `!`
nullable-suppression operators are used anywhere in this change.

**No schema migration.** All new data lives as additive nullable JSON-blob keys inside the
existing `aesthetic_profile_json` column (`conceptualMethod { method, rationale, cues[] }`
and `imageTypeRatio { photo, flat, decorator, sampleSize }`). Old blobs missing the new
keys deserialize with `null` for both new fields (parity). `PromptVersion` is bumped to
`aesthetic-extractor/v2` to distinguish new analyses from old. No EF Core migration is
added in this change.

## Consequences

- `ProfileAnalyzerOrchestrator` and the test suite are the only production call sites that
  needed updating for the signature migration. The migration is complete; no legacy path
  remains.
- `ImagePromptWriter` branches on `ArtDirectionMethod` at ideation time. Adding a new
  method vocabulary requires only a new enum member and a branch in the writer; no other
  Application change is needed.
- Cold-start clients (null `ConceptualMethod`, null `ImageTypeRatio`) receive EXACTLY the
  pre-change prompt output. Parity is enforced by a dedicated byte-identical test
  (`ImagePromptWriterColdStartParityTests`, WU-5).
- The `ai-aesthetic` exclusion of `claude -p` is test-guarded. Routing tests will fail if
  the exclusion is accidentally removed from the test-environment configuration.
- `SampleSize` on `ImageTypeRatio` is the confidence signal for the ratio. A future
  operator UI can surface this value to explain why a ratio is weak (small sample) vs.
  reliable (large sample).
- The `aesthetic_profile_json` blob gains two new optional keys. A rollback requires only
  removing the serialization code; stored blobs with new keys remain readable by rolled-back
  code because unknown keys are ignored at deserialization.

## Related

- Design: `openspec/changes/client-aesthetic-conceptual-detection/design.md` (OQ-1, OQ-2, OQ-3, OQ-6)
- Spec: `openspec/changes/client-aesthetic-conceptual-detection/specs/` (CACD-PA-01..07, CACD-CG-01..03, CACD-IMG-01..02)
- Builds on: ADR 0006 (`client-aesthetic-ensure-fresh`), ADR 0007 (Image Generation Gateway Port)
- Architecture gate: `pwsh -File scripts/gates/Run-Gates.ps1`
- Test suite: `Incoders.Hive.Tests/Incoders.Hive.Tests.csproj` (WU-1..WU-6 commit range: 79288bc..35ebb21)
