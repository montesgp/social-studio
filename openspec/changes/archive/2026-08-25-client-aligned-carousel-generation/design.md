# Design — client-aligned-carousel-generation (Structured Style Capture + Background-Only Conditioned Generation)

Hexagonal design that makes a `BackgroundSourcingPolicy.ClientAesthetic` feed-carousel match how the client
ACTUALLY publishes: the captured photo-vs-flat structure + interleave drive which slides are creative, a
captured `{backgroundHex,textHex}` pairing set drives per-slide colour, the client's real archived creatives
condition the generated backgrounds (realising the reserved `ImageGenerationRequest.Init` slot), the provider
generates ONLY the background while we composite editable text/`@handle`/signature, and the previously-silent
per-slide image-gen degradation becomes observable. This change does NOT build new ports from scratch — the
`content-image-generation` slice already shipped the byte-only image gateway, `CreativeSlideIndices`,
`ClientAestheticImageBackgroundService`, and the reserved `ImageInitReference` slot. This design EXTENDS those
seams additively and closes the two confirmed root causes (capture not recorded/applied; image-gen silently
degrading and never conditioned).

It resolves the proposal's seven open questions (OQ-1..OQ-7), maps the component changes per layer, fixes the
new contracts, the flow, the degrade matrix, the data-model + migration verdict, a Strict-TDD-friendly
test-first ordering, and a single-PR sliced implementation order. It does NOT enumerate task steps — that is
the `sdd-tasks` phase.

## Status
Design — architecture decisions resolved. Implements the technical HOW for `proposal.md`. All new aesthetic
capture lives as additive nullable fields serialised into the EXISTING `aesthetic_profile_json` column (NO
schema migration for the aesthetic fields). The ONE schema migration is for observability: relaxing
`integration_execution_attempts.analysis_id` to nullable + adding a correlation column so content-generation
image-gen attempts persist. New Application value objects + an extended image-conditioning contract + a gateway
capability-describe method → an ADR under `docs/architecture/adr/` is authored at apply time
(`require_adr_for_architecture_changes`). Targets branch `feat/claude-p-provider` as a single PR.

## Scope reminder (Non-Goals are hard boundaries)
Ships: structured package-structure capture (photo-vs-flat background-kind sequence + typical slide count),
`{backgroundHex,textHex}` colour-pairing capture + application, enforced slide count over the hardcoded
`~3-5`, real-creative conditioning of generated backgrounds via the realised `Init` slot with provider
capability detection + descriptor fallback, the background-only generation contract, the carousel
`@handle`/signature composition slot + `ClientOwnAccount.Handle` load, and observable per-slide degradation
telemetry. Does NOT: build a new MediaGeneration provider adapter (reuses the existing provider-agnostic
gateway + whatever providers are wired), does NOT hardcode any model/provider (routing stays config-driven),
does NOT touch Story formats, does NOT apply typography Weight/Role (GAP 6 deferred), and does NOT change
job-completion semantics (degradation stays non-fatal — just visible).

---

## 0. The decisions (read this first)

| # | Open question | Decision | One-line rationale |
|---|---------------|----------|--------------------|
| **OQ-1** | Structure representation | **Capture an ordered per-slide background-kind SEQUENCE** `BackgroundPattern: IReadOnlyList<SlideBackgroundKind>?` (`SlideBackgroundKind { Photo, FlatColor, Decorator }`) plus `TypicalSlideCount: int?`, both additive-nullable on `ClientAestheticProfile`. The photo-vs-flat RATIO is DERIVED from the pattern (count `Photo` / total). `CreativeSlideIndexResolver` gains a pattern-aware overload that PROPORTIONALLY maps the captured pattern onto the actual slide count (tile/scale) to yield the creative (`Photo`) indices. | A sequence captures BOTH ratio and interleave in one shape and is the exact thing the multimodal extractor already describes ("slide-mix pattern"); proportional mapping handles a variable slide count without a fixed-length assumption. |
| **OQ-2** | Colour-pairing derivation | **Capture a RANKED ordered set** `ColorPairings: IReadOnlyList<ColorPairing>?` where `ColorPairing(string BackgroundHex, string TextHex, int Rank)`. Derivation joins brand-manual `ColorRole` + extracted `palette.Role`: backgrounds = entries with role in {Primary, Secondary, Accent} ordered by (role ordinal, rank); each background is paired with the highest-WCAG-contrast text candidate (Neutral-role entries first, else computed black/white by luminance, contrast target ≥ 4.5:1). The "client criterion that varies" is encoded as THIS ordered set + a rotation strategy: the composer selects `pairings[(slideIndex-1) % pairings.Count]` instead of array-position palette. `CompositionDefaults.ResolvePalette()` gains a `(brand, profile, slideIndex)` overload that returns the rotated pairing when present. | A ranked rotating set reproduces a per-client combination logic deterministically; WCAG contrast guarantees legible text; rotation keys colour to slide POSITION-IN-PATTERN, not raw array index. |
| **OQ-3** | Slide-count enforcement | **Enforce the captured count as the DEFAULT; an explicit user-requested length WINS (logged).** Effective count = explicit user request, else `TypicalSlideCount` clamped to a sanity range `[3..10]`, else the current default. The hardcoded `~3-5` string in `CarouselGenerator` is REPLACED by the resolved effective count. | Reproduces the client's typical structure by default while never overriding explicit user intent; the clamp prevents a pathological captured value from producing a 1- or 30-slide carousel. |
| **OQ-4** | Reference-image selection + capability detection | **Pass up to `MaxConditioningImages = 3` of the client's curated archived creatives per creative slide as `ImageInitReference` HANDLES (storage-path `Url`, NOT bytes).** Capability flag lives on the ADAPTER (`IImageModelClient.Capabilities.SupportsImageConditioning` + `MaxConditioningImages`), advertised through the gateway via a new `DescribeAsync` method. The background service queries capability ONCE per job; conditions only when supported, else passes a descriptor prompt. The supporting adapter resolves bytes from storage. | Keeps the Application layer free of byte-hauling (passes Urls), bounds vision-token cost (cap 3, reuses the curated set), and detects capability declaratively without a DB flag or migration — provider-agnostic. |
| **OQ-5** | Cold-start fallback | **Progressive capture; all captured fields stay nullable and degrade to current behaviour when absent.** No `BackgroundPattern` → `CreativeSlideIndexResolver` keeps `{first,last}`; no `ColorPairings` → positional palette; no `TypicalSlideCount` → current default; no `SupportsImageConditioning` or no reference images → descriptor-prompt background. | Capture is incremental and never blocks generation; a brand-new client produces today's output, improving as the extractor fills fields. |
| **OQ-6** | Telemetry scope | **Make per-slide image-gen degradation observable by persisting `integration_execution_attempts` rows for the content-generation image path; KEEP job `Completed` and DO NOT set a fatal `error_code`.** Root cause: `ImageGenerationGateway` passes `AnalysisId: null`, and `IntegrationExecutionPolicy` SKIPS persistence when `AnalysisId is null`. Fix: thread a nullable `CorrelationId`/`CorrelationKind`; persist when either `AnalysisId` OR `CorrelationId` is present; write one `Failed`/`Skipped` row per degraded slide with `DiagnosticsJson = { slideIndex, reason }`. | Surfaces the currently-invisible silent failure without changing the resilience contract; correlation decouples image-gen telemetry from the single-analysis FK. |
| **OQ-7** | Carousel-first vs single parity | **Carousel-first, confirmed.** Feed single-image posts share the `ClientAesthetic` surface but a different composer; capture (structure/pairing) is reusable, application to `FeedPostVisualComposer` is a documented follow-up. | The reported defect is carousel-shaped; shipping carousel first delivers the fix while keeping the captured fields format-agnostic for the single-post follow-up. |

Each decision is expanded in [Decisions in depth](#9-decisions-in-depth).

---

## 1. Architecture approach

Dependency rule, unchanged: **Domain ← Application ← Infrastructure / Api**. No Apify/Supabase/OpenAI/Claude/
`HttpClient`/EF/ASP.NET types in Domain or Application. Every provider concern stays behind the EXISTING
`IImageGenerationGateway` / `IImageModelClient` ports and the AI/storage ports. This change is overwhelmingly
ADDITIVE on top of `content-image-generation` and `content-aesthetic-generation-phase-2`.

- **Domain** (`Domain/Social/ContentGeneration/`): nearly unchanged. ONE additive nullable field —
  `ClientProfileSnapshot.Handle` (`string?`) — so the carousel composer can render the client's `@handle`.
  `BackgroundSelection`, `CarouselBackgroundPlan`, `WeightedColor`, `ColorRole` are reused as-is.
- **Application** gains the capture vocabulary and the apply logic:
  - New pure value objects in `…/ContentGeneration/Shared/`: `SlideBackgroundKind` (enum) and `ColorPairing`
    (record).
  - `ClientAestheticProfile` extended with additive-nullable `BackgroundPattern`, `TypicalSlideCount`, and
    `ColorPairings` (trailing optional params; every existing construction site / `with` compiles unchanged).
  - `CreativeSlideIndexResolver` gains a pattern-aware overload (proportional mapping → creative indices).
  - `CompositionDefaults.ResolvePalette` gains a slide-index/pairing-aware overload (rotating pairings).
  - `CarouselVisualComposer` gains an `@handle`/signature copy slot (parity with `StoryVisualComposer` via
    `AttributionPolicy.ResolveSignature`) and threads slide index into palette resolution.
  - `CarouselGenerator` consumes the effective slide count (OQ-3) instead of the hardcoded `~3-5`.
  - `AestheticExtraction` + the extractor DTO gain the slide-mix-pattern + pairing signals (the multimodal
    prompt already asks for them — there was no field to store them).
  - `AestheticProfilePersistence` (read/write of `aesthetic_profile_json`) serialises the new fields.
  - `ClientAestheticImageBackgroundService` queries capability, builds `ImageInitReference` handles for
    creative slides, enforces background-only, validates via `VisualGuardrailValidator` with bounded retry,
    and emits per-slide degradation telemetry.
  - `IImageGenerationGateway` gains a capability `DescribeAsync`; `ImageGenerationRequest` gains an additive
    `ReferenceImages` conditioning slot; `IntegrationExecutionRequest`/attempt record gain `CorrelationId`.
- **Infrastructure** carries only wiring/adapters:
  - The supporting `IImageModelClient` adapter advertises `Capabilities.SupportsImageConditioning` and, when
    true, resolves reference-image bytes from `IGeneratedAssetStorage` and forwards them as provider image
    input; non-supporting adapters return `SupportsImageConditioning = false` and ignore the references.
  - The brand-snapshot loader populates `ClientProfileSnapshot.Handle` from `ClientOwnAccount.Handle`.
  - `IntegrationExecutionPolicy` persists attempts on correlation; the EF entity + repository + ONE additive
    migration relax `analysis_id` to nullable and add `correlation_id`/`correlation_kind`.
- **Api / Web**: none required. The Content Studio already composites editable text over a forced background
  (verified in `content-image-generation`); `@handle`/signature ride the existing copy-slot mechanism.

Generation runs INSIDE the existing async `ContentGenerationJobProcessor` pre-loop (verified seam used by
`content-image-generation`). No new worker, no new job state. Completion NEVER awaits a successful image — any
per-slide failure degrades to the CSS/descriptor path AND now emits a visible telemetry row.

---

## 2. Component map — ports, adapters, layers

### 2.1 Domain (pure, additive)
| Element | Path | Change |
|---------|------|--------|
| `ClientProfileSnapshot.Handle` (`string?`) | `Domain/Social/ContentGeneration/…ClientProfileSnapshot.cs` | **Modified** — additive nullable `@handle`; null = no own account loaded |
| `BackgroundSelection`, `CarouselBackgroundPlan`, `WeightedColor`, `ColorRole` | `Domain/Social/…` | **Reused (no change)** |

### 2.2 Application (value objects, ports, use cases)
| Element | Kind | Path | Change |
|---------|------|------|--------|
| `SlideBackgroundKind` | enum | `…/ContentGeneration/Shared/SlideBackgroundKind.cs` | **New** — `{ Photo, FlatColor, Decorator }` |
| `ColorPairing` | record | `…/ContentGeneration/Shared/ColorPairing.cs` | **New** — `(string BackgroundHex, string TextHex, int Rank)` |
| `ClientAestheticProfile` (+ `BackgroundPattern`, `TypicalSlideCount`, `ColorPairings`) | record | `…/Shared/ClientAestheticProfile.cs` | **Modified** — three additive trailing nullable params |
| `CreativeSlideIndexResolver.ResolveFromPattern(pattern, slideCount)` | static | `…/Background/CreativeSlideIndexResolver.cs` | **Modified** — pattern-aware overload; existing `Resolve` retained for null-pattern default |
| `CompositionDefaults.ResolvePalette(brand, profile, slideIndex, inferred)` | static | `…/Visual/CompositionDefaults.cs` | **Modified** — pairing/slide-index overload; legacy overload retained |
| `CarouselVisualComposer` | composer | `…/Visual/Composers/CarouselVisualComposer.cs` | **Modified** — `@handle`/signature copy slot; pass `slideIndex` to palette resolution |
| `CarouselGenerator` | use case | `…/Formats/Carousel/CarouselGenerator.cs` | **Modified** — emit effective slide count (OQ-3) not `~3-5` |
| `AestheticExtraction` (+ `BackgroundPattern`, `ColorPairings`) | record | `…/Abstractions/Social/ContentGeneration/IClientAestheticStyleExtractor.cs` | **Modified** — additive signals |
| `AestheticProfilePersistence` (read/write) | static | `…/Shared/AestheticProfilePersistence.cs` | **Modified** — serialise/deserialise new fields into `aesthetic_profile_json` |
| `IImageGenerationGateway.DescribeAsync` + `ImageModelCapabilities` | port + record | `…/Abstractions/Ai/IImageGenerationGateway.cs` | **Modified/New** — capability query; `ImageModelClient.Capabilities` |
| `ImageGenerationRequest.ReferenceImages` (`IReadOnlyList<ImageInitReference>?`) | record field | same file | **Modified** — additive trailing conditioning slot (realises OQ-7); `Init` kept for back-compat |
| `ClientAestheticImageBackgroundService` | use case | `…/Background/ClientAestheticImageBackgroundService.cs` | **Modified** — capability-gated conditioning, guardrail retry, per-slide telemetry |
| `IIntegrationExecutionPolicy` request + attempt record (`CorrelationId`, `CorrelationKind`) | records | `…/Abstractions/Integrations/IIntegrationExecutionAttemptRepository.cs` | **Modified** — additive nullable correlation |

### 2.3 Infrastructure (adapters, wiring only)
| Element | Path | Change |
|---------|------|--------|
| Supporting `IImageModelClient` adapter — advertise `SupportsImageConditioning`, resolve reference bytes, forward image input, enforce background-only system directive | `Infrastructure/Ai/…ImageModelClient.cs` | **Modified** — capability + conditioning path |
| `MultimodalAestheticStyleExtractor` — map `backgroundPattern` + `colorPairings` from the AI JSON | `Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs` | **Modified** — DTO + `Map` extended |
| Brand-snapshot loader — load `ClientOwnAccount.Handle` → `ClientProfileSnapshot.Handle` | `Infrastructure/Social/ContentGeneration/…SnapshotLoader.cs` | **Modified** — additive load |
| `IntegrationExecutionPolicy` — persist on `CorrelationId` when `AnalysisId` is null | `Infrastructure/Integrations/IntegrationExecutionPolicy.cs` | **Modified** — relax persistence guard |
| `ImageGenerationGateway` — pass `CorrelationId`/`CorrelationKind`; ensure execution policy is wired on the content-generation path | `Infrastructure/Ai/ImageGenerationGateway.cs` | **Modified** — correlation threading + `DescribeAsync` impl |
| `IntegrationExecutionAttemptData` + repository + EF config | `Infrastructure/Persistence/Postgres/…` | **Modified** — nullable `AnalysisId`, add `correlation_id`/`correlation_kind` |
| EF migration (telemetry) | `Infrastructure/Persistence/Postgres/Migrations/` | **New (additive)** — nullable `analysis_id` + correlation columns |
| DI registration | `Infrastructure/DependencyInjection/DependencyInjection.cs` | **Modified** — wire execution policy into the image gateway on the content-generation path |

> **Naming check (verified):** the aesthetic enriched payload persists in the `aesthetic_profile_json` column
> via `AestheticProfilePersistence.Read/Write` (verified `EfClientAestheticProfileReader.cs`), NOT in dedicated
> columns. New capture fields therefore need NO schema migration.

---

## 3. The new / extended contracts

### 3.1 Structure capture (Application VOs — pure)
```csharp
public enum SlideBackgroundKind { Photo, FlatColor, Decorator }

public sealed record ColorPairing(string BackgroundHex, string TextHex, int Rank);
```
`ClientAestheticProfile` extension (additive trailing — all nullable, default null):
```csharp
public sealed record ClientAestheticProfile(
    // …existing Phase 1 + Phase 2 + content-image-generation params…
    IReadOnlyList<int>? CreativeSlideIndices = null,
    // client-aligned-carousel-generation (additive):
    IReadOnlyList<SlideBackgroundKind>? BackgroundPattern = null, // OQ-1 ordered slide-mix template
    int? TypicalSlideCount = null,                                // OQ-3 enforced default count
    IReadOnlyList<ColorPairing>? ColorPairings = null);          // OQ-2 ranked rotating pairings
```
- Photo-vs-flat RATIO is derived (`BackgroundPattern.Count(k => k == Photo) / BackgroundPattern.Count`), not a
  separate stored field — one source of truth.
- All three persist as additive keys inside `aesthetic_profile_json`; absent keys deserialise to null.

### 3.2 `CreativeSlideIndexResolver` — pattern-aware mapping (OQ-1)
```csharp
// New overload. Maps a captured pattern onto the actual slide count by proportional scaling,
// then returns the 1-based indices whose mapped kind is Photo, deduped + capped to MaxCreativeSlidesPerPackage.
public static IReadOnlyList<int> ResolveFromPattern(
    IReadOnlyList<SlideBackgroundKind>? pattern, int slideCount);
```
- `pattern` null/empty → delegates to the existing `Resolve(null, slideCount)` (`{first,last}` default).
- When `pattern.Count == slideCount` → 1:1 (creative = `Photo` positions).
- When counts differ → proportional map: slide `i` (1-based) reads `pattern[floor((i-1) * pattern.Count / slideCount)]`.
- Cap `MaxCreativeSlidesPerPackage` (2) is preserved — the conditioning budget is unchanged.

### 3.3 Colour pairing (OQ-2)
```csharp
// CompositionDefaults — new pairing/slide-index aware overload (legacy ResolvePalette retained for null).
internal static CompositionPalette ResolvePalette(
    BrandSnapshot brand, ClientAestheticProfile? profile, int slideIndex, InferenceAccumulator inferred);
```
- When `profile?.ColorPairings` is non-empty → `var p = pairings[(slideIndex-1) % pairings.Count]` and return a
  palette whose `Background = p.BackgroundHex`, `Text = p.TextHex` (other roles from brand or defaults).
- When absent → existing positional `ResolvePalette(brand, inferred)` behaviour (rollback-safe).
- Pairing DERIVATION (in the extractor/persistence mapping, NOT at compose time): join `ColorRole`
  (brand-manual) + `palette.Role` (extracted), rank backgrounds by (role ordinal, rank), pick the
  highest-contrast text per background (WCAG ≥ 4.5:1; Neutral candidates first, else luminance black/white).

### 3.4 Reference-image conditioning + capability detection (OQ-4)
```csharp
public interface IImageModelClient
{
    Task<Result<ImageGenerationResult>> GenerateAsync(ImageGenerationRequest request, CancellationToken ct = default);
    ImageModelCapabilities Capabilities { get; } // declarative, advertised per adapter
}

public sealed record ImageModelCapabilities(bool SupportsImageConditioning, int MaxConditioningImages);

public interface IImageGenerationGateway
{
    Task<Result<ImageGenerationResult>> GenerateAsync(ImageGenerationRequest request, CancellationToken ct = default);
    // Resolves route+credential+client and returns the advertised capability WITHOUT calling the provider.
    Task<Result<ImageModelCapabilities>> DescribeAsync(Guid organizationId, CancellationToken ct = default);
}

public sealed record ImageGenerationRequest(
    // …existing params… ImageInitReference? Init = null, …,
    IReadOnlyList<ImageInitReference>? ReferenceImages = null); // OQ-7 realised; handles (Url), not bytes
```
- The background service calls `DescribeAsync` ONCE per job. If `SupportsImageConditioning`, it maps up to
  `min(MaxConditioningImages, 3)` of `profile.ReferenceImages` (`ProfilePostImageRef.StoragePath`) into
  `ImageInitReference(Bytes: null, Url: storagePath, Mime)` and sets `ReferenceImages`. Else it leaves them null.
- The supporting adapter resolves bytes from `IGeneratedAssetStorage` for `Url` references (Infrastructure only).
- **Background-only contract** is enforced at the adapter via a fixed system directive ("generate ONLY the
  background; never render text, logos, `@handles`, or signatures") AND validated by `VisualGuardrailValidator`
  with a bounded retry (reuse the existing validator + retry budget); a guardrail miss after retries degrades
  that slide to CSS/descriptor.

### 3.5 Observability correlation (OQ-6)
```csharp
public sealed record IntegrationExecutionAttemptRecord(
    Guid Id, Guid? AnalysisId, /* now nullable */
    string CapabilityKey, string ProviderKey, IntegrationExecutionAttemptStatus Status,
    int AttemptNumber, string? FailureCode, int RetryCount, int ElapsedMs, string? DiagnosticsJson,
    DateTime StartedAtUtc, DateTime? CompletedAtUtc,
    Guid? CorrelationId = null, string? CorrelationKind = null); // additive

// IntegrationExecutionRequest gains: Guid? CorrelationId, string? CorrelationKind
// IntegrationExecutionPolicy.PersistAttemptAsync persists when (AnalysisId is not null) OR (CorrelationId is not null).
```
- The image background service supplies `CorrelationId = FormatResultId`, `CorrelationKind =
  "content-generation/generate-image"`, and per-slide `DiagnosticsJson = {"slideIndex":N,"reason":"…"}` so a
  degraded slide produces a `Failed`/`Skipped` row. **Job stays `Completed`; no fatal `error_code`.**

---

## 4. Flow

### 4.1 Capture (async profile-analysis pipeline — produces the structure substrate)
```
extract style ─► multimodal(images + captions)
                 └─► AestheticExtraction { palette?, descriptors, motifs, voice,
                                           backgroundPattern? (slide-mix sequence), colorPairings? }   [OQ-1/OQ-2]
derive pairings ─► join brand ColorRole + extracted palette.Role → ranked {bgHex,textHex} (WCAG-validated)
persist ─► AestheticProfilePersistence.Write → aesthetic_profile_json (additive keys; no migration)
```

### 4.2 Apply (ClientAesthetic carousel — consumes the substrate)
```
pre-loop ─► profile = reader.GetProfile(org, client)   [enriched: pattern?, typicalCount?, pairings?, refs?]
count   ─► effective = userRequested ?? clamp(profile.TypicalSlideCount, 3..10) ?? default   [OQ-3]
indices ─► creative = CreativeSlideIndexResolver.ResolveFromPattern(profile.BackgroundPattern, effective)  [OQ-1]
caps    ─► caps = gateway.DescribeAsync(org)   [OQ-4 capability once per job]
images  ─► imageBackgroundService.BuildCreativeBackgroundsAsync(req with creative indices):
            per creative slide (Task.WhenAll, cap 2):
              promptWriter.WriteAsync(...)                                  [org TEXT route]
              refs = caps.SupportsImageConditioning ? map(profile.ReferenceImages, cap 3) : null   [OQ-4]
              gateway.GenerateAsync(req{ background-only, ReferenceImages = refs })  [org IMAGE route]
              VisualGuardrailValidator.Validate(bytes) + bounded retry            [background-only enforce]
              storage.PutAsset(...) → ForcedDefault(url)
              on any failure → omit slide (CSS/descriptor) + emit Failed/Skipped attempt row   [OQ-6]
plan    ─► overlay ForcedDefault onto creative slots; flat slots keep ClientAesthetic
compose ─► CarouselVisualComposer.ComposeMany(payload, brand, plan):
            per slide: palette = ResolvePalette(brand, profile, slideIndex)     [OQ-2 rotating pairing]
                       copySlots += { handle, signature }                        [GAP 5 parity]
render  ─► existing per-slide envelope (editable text/handle/signature over the background)
```

### 4.3 Two-step routing (no provider hardcoding — unchanged seam)
TEXT route writes the per-slide prompt; IMAGE route renders it; both resolve per organisation via
`IWorkflowConfigurationResolver` (verified). Absent an IMAGE route → every creative slide degrades AND emits a
telemetry row. Capability (image conditioning) is a property of the resolved IMAGE adapter, not a config flag.

---

## 5. Degrade matrix (resilience — non-negotiable)

| Condition | Behaviour |
|-----------|-----------|
| `BackgroundPattern` null/empty | `CreativeSlideIndexResolver` keeps `{first,last}` default (OQ-1/OQ-5) |
| `TypicalSlideCount` null | Effective count = current default; explicit user request still wins (OQ-3) |
| `ColorPairings` null/empty | `ResolvePalette` keeps positional palette (OQ-2/OQ-5) |
| Provider `SupportsImageConditioning == false` | Descriptor-prompt background; `ReferenceImages` left null (OQ-4) |
| No reference images for the client | Descriptor-prompt background; conditioning skipped (OQ-5) |
| Guardrail validation fails after bounded retry | Slide degrades to CSS/descriptor; `Failed` attempt row emitted (OQ-6) |
| Prompt-write / image-gen / storage fails | Slide degrades; `Failed`/`Skipped` attempt row with `DiagnosticsJson`; job stays `Completed` (OQ-6) |
| No IMAGE route configured | All creative slides degrade; one row per slide; package completes |
| Policy ≠ `ClientAesthetic` | Entire capture/apply path skipped; existing behaviour unchanged |
| No `ClientOwnAccount.Handle` | `@handle`/signature slot omitted; composition otherwise unchanged (GAP 5) |

Image generation is NEVER on the completion critical path. Every failure is a per-slide degrade that is now
VISIBLE via an execution-attempt row — exactly the proposal's "keep silent-degradation behaviour but make it
visible" requirement.

---

## 6. Strict-hexagonal boundary mapping (which type lives where)

| Concern | Layer | Type(s) |
|---------|-------|---------|
| Background plan / selection | **Domain** | `BackgroundSelection`, `CarouselBackgroundPlan`, `WeightedColor`, `ColorRole` (reused) |
| Client `@handle` snapshot | **Domain** | `ClientProfileSnapshot.Handle` (additive nullable) |
| Structure + pairing capture VOs | **Application** | `SlideBackgroundKind`, `ColorPairing`, extended `ClientAestheticProfile`, `AestheticExtraction` |
| Resolver / palette / composer / generator apply logic | **Application** | `CreativeSlideIndexResolver`, `CompositionDefaults`, `CarouselVisualComposer`, `CarouselGenerator` |
| Image conditioning contract + capability query | **Application** | `ImageGenerationRequest.ReferenceImages`, `IImageGenerationGateway.DescribeAsync`, `ImageModelCapabilities` |
| Telemetry correlation contract | **Application** | `IntegrationExecutionAttemptRecord` / `IntegrationExecutionRequest` correlation fields |
| Provider SDK (image input, background-only directive, byte resolution) | **Infrastructure** | the `IImageModelClient` adapter ONLY |
| Multimodal extraction mapping, snapshot load, attempt persistence, migration | **Infrastructure** | extractor, snapshot loader, `IntegrationExecutionPolicy`, EF entity/repo/migration |

Architecture-gate contract: no provider/HTTP/EF/Supabase type appears in any Application/Domain signature.
`ImageInitReference` carries `byte[]?`/`string? Url` only; bytes are resolved INSIDE the adapter. A port
contract test (`Port_HasNoInfrastructureTypes`, following the existing `IImageGenerationGatewayContractTests`
pattern) pins this for the extended gateway + capability records.

---

## 7. Data model + migration verdict

- **Aesthetic capture fields (`BackgroundPattern`, `TypicalSlideCount`, `ColorPairings`)** — serialise into the
  EXISTING `aesthetic_profile_json` column via `AestheticProfilePersistence.Read/Write` (verified read-side
  projection). **NO schema migration.** Additive JSON keys are forward/backward compatible: an old row missing
  the keys deserialises to null and degrades to current behaviour; bump the extractor prompt-version so new
  analyses populate them.
- **`ClientProfileSnapshot.Handle`** — a frozen snapshot field built at job start from `ClientOwnAccount`
  (already persisted). **NO migration** — the source column already exists; only the snapshot load changes.
- **Telemetry — `integration_execution_attempts`** — the ONE schema migration: make `analysis_id` NULLABLE,
  relax its FK to single-content-analyses to optional, and add nullable `correlation_id` (uuid) +
  `correlation_kind` (text). Additive + reversible (drop columns / re-tighten on rollback; existing analysis-
  scoped rows are unaffected).

**Migration verdict: ONE additive, reversible migration (telemetry only).** All capture/apply changes are
code-only or JSON-blob-additive.

---

## 8. Test-first ordering (Strict TDD — `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj -c Release`)

1. **Structure VOs** — `SlideBackgroundKind`, `ColorPairing`; extended `ClientAestheticProfile` (additive
   nullable; existing construction sites + `with` compile unchanged). (RED→GREEN.)
2. **`CreativeSlideIndexResolver.ResolveFromPattern`** — null/empty → `{first,last}`; equal-length 1:1; unequal
   proportional map; `Photo` selection; dedupe + cap 2. (OQ-1/OQ-5.)
3. **Pairing derivation** — join `ColorRole` + `palette.Role`; rank ordering; WCAG ≥ 4.5:1 text choice;
   luminance fallback; empty palette → no pairings. (OQ-2.)
4. **`CompositionDefaults.ResolvePalette` (pairing overload)** — rotation `(slideIndex-1) % count`; null
   pairings → legacy positional palette (parity). (OQ-2/OQ-5.)
5. **Slide-count enforcement** — user request wins; captured count clamped `[3..10]`; absent → default;
   `CarouselGenerator` prompt uses the effective count, not `~3-5`. (OQ-3.)
6. **`@handle`/signature slot** — `CarouselVisualComposer` adds `handle`/`signature` copy slots via
   `AttributionPolicy.ResolveSignature`; absent `Handle` → slot omitted (parity). (GAP 5.)
7. **Capability + conditioning** — `DescribeAsync` returns adapter capability without provider call; background
   service conditions only when `SupportsImageConditioning`, caps references to 3, passes Url handles;
   non-supporting → null references (descriptor). (OQ-4.)
8. **Background-only + guardrail** — adapter system directive; `VisualGuardrailValidator` pass/fail with bounded
   retry; post-retry miss → slide degrades. (direction #1.)
9. **Telemetry** — `IntegrationExecutionPolicy` persists on `CorrelationId` when `AnalysisId` is null; one
   `Failed`/`Skipped` row per degraded slide with `DiagnosticsJson`; job stays `Completed`. (OQ-6.)
10. **Extractor mapping** — `MultimodalAestheticStyleExtractor` maps `backgroundPattern` + `colorPairings`;
    `AestheticProfilePersistence` round-trips them through `aesthetic_profile_json`.
11. **EF migration** — `integration_execution_attempts` nullable `analysis_id` + correlation columns round-trip;
    existing analysis-scoped rows unaffected.
12. **Processor wiring** — ClientAesthetic carousel uses pattern indices, rotating pairings, conditioned
    backgrounds, handle/signature; cold-start (all null) reproduces current output (parity); image failure →
    CSS + telemetry row (resilience). Reuse the existing processor visual test harness.
13. **Gates + ADR + docs/spec sync** — `pwsh -File scripts/gates/Run-Gates.ps1`.

---

## 9. Decisions in depth

### OQ-1 — Ordered slide-mix sequence + derived ratio + proportional mapping
**Decision.** Capture `BackgroundPattern: IReadOnlyList<SlideBackgroundKind>?` (`Photo`/`FlatColor`/`Decorator`)
and `TypicalSlideCount: int?`. Ratio is derived from the pattern. `CreativeSlideIndexResolver.ResolveFromPattern`
maps the pattern proportionally onto the actual slide count and returns the `Photo` indices (deduped, capped).
**Rationale.** A sequence encodes BOTH the photo-vs-flat ratio AND the interleave in ONE shape, matches exactly
what the multimodal extractor already describes (the "slide-mix pattern" it detects but cannot store), and a
proportional map cleanly handles a slide count that differs from the captured pattern length.
**Rejected.** Per-index boolean flags (don't generalise to a different slide count). A ratio-only scalar (loses
the interleave — the exact defect: all-flat output). A fixed-length pattern equal to `TypicalSlideCount` (breaks
when the user requests a different length).

### OQ-2 — Ranked rotating `{backgroundHex,textHex}` pairings, WCAG-validated
**Decision.** Capture `ColorPairings: IReadOnlyList<ColorPairing>?` derived by joining brand-manual `ColorRole`
and extracted `palette.Role`; the composer rotates `pairings[(slideIndex-1) % count]`. Text per background is the
highest-WCAG-contrast candidate (Neutral-role first, else luminance black/white, target ≥ 4.5:1).
**Rationale.** A ranked rotating SET reproduces a per-client combination LOGIC that varies but is deterministic;
WCAG contrast guarantees legible composited text; keying colour to slide position-in-rotation (not raw array
index) is what "the client's criterion, not array position" means.
**Rejected.** Single fixed palette (the current positional bug). Random pairing (non-reproducible). Per-slide
explicit colour capture (over-specified; doesn't generalise across slide counts).

### OQ-3 — Enforce captured count as default; explicit user request wins; sanity clamp
**Decision.** Effective slide count = explicit user request, else `TypicalSlideCount` clamped to `[3..10]`, else
current default. Replace the hardcoded `~3-5` in `CarouselGenerator` with the effective count.
**Rationale.** Reproduces the client's typical structure by default (the proposal's direction #3) while never
overriding explicit user intent; the clamp guards against a pathological captured value.
**Rejected.** Hard-enforce captured count over the user (violates explicit intent). Keep `~3-5` advisory (the
current GAP 4 — captured count never wins). Unclamped enforcement (a bad capture yields a 1- or 30-slide
carousel).

### OQ-4 — Adapter-advertised capability; Url handles; cap 3 per slide
**Decision.** Capability flag (`SupportsImageConditioning`, `MaxConditioningImages`) lives on the
`IImageModelClient` adapter, surfaced through `IImageGenerationGateway.DescribeAsync`. The background service
queries once per job and passes up to 3 reference creatives as `ImageInitReference` Url HANDLES; the supporting
adapter resolves bytes from storage; non-supporting adapters ignore them and the slide uses a descriptor prompt.
**Rationale.** Declarative capability needs no DB column or migration and stays provider-agnostic; Url handles
keep the Application layer free of byte-hauling; cap 3 reusing the curated reference set bounds vision-token cost.
**Rejected.** A DB/catalog `supports_image_input` flag (migration + drift from real adapter ability). Always
attaching encoded bytes in Application (wasted cost when unsupported; byte-hauling in Application). Unbounded
reference count (open-ended vision cost).

### OQ-5 — Progressive capture; nullable everything; degrade to current behaviour
**Decision.** Every captured field is nullable; each absent field independently degrades to today's behaviour
(`{first,last}` indices, positional palette, default count, descriptor background).
**Rationale.** Capture is incremental — a brand-new client must still generate a package; quality improves as the
extractor fills fields. Matches the proposal's rollback plan and cold-start risk mitigation.
**Rejected.** Block generation until structure is captured (breaks new clients). A synthetic heuristic when
capture is absent (invents a structure the client never used — the opposite of the goal).

### OQ-6 — Observable degradation via correlated attempt rows; job stays Completed
**Decision.** Thread `CorrelationId`/`CorrelationKind`; persist `integration_execution_attempts` when `AnalysisId`
OR `CorrelationId` is present; one `Failed`/`Skipped` row per degraded slide with `DiagnosticsJson`. Keep the job
`Completed`; do NOT set a fatal `error_code`.
**Rationale.** The empty-telemetry root cause is precisely the `AnalysisId is null` skip in
`IntegrationExecutionPolicy`; correlation decouples image-gen telemetry from the single-analysis FK and makes the
silent failure visible WITHOUT changing the resilience contract.
**Rejected.** Stuff `JobId` into `AnalysisId` (violates the single-analysis FK). Set a fatal `error_code` on
degradation (changes completion semantics — explicitly out of scope). A brand-new telemetry table (duplicates the
existing attempt store for no benefit).

### OQ-7 — Carousel-first; single-post parity is a follow-up
**Decision.** Ship carousel; keep captured fields format-agnostic; apply to `FeedPostVisualComposer` as a
documented follow-up.
**Rationale.** The reported defect is carousel-shaped; the capture surface (structure/pairing) is reusable, so the
single-post follow-up is a small composer change, not a re-capture.
**Rejected.** Cover single + carousel now (widens the slice past the reported defect for no extra capture value).

---

## 10. Architecture-gate compliance
- **No provider types in Domain/Application.** All new VOs/contracts are pure; the OpenAI/Claude SDK, byte
  resolution, and the background-only directive live ONLY in the `IImageModelClient` adapter. `ImageInitReference`
  carries `byte[]?`/`Url` (Application-safe); bytes are resolved inside Infrastructure. Verified against
  `architecture-boundaries.rules.md` + `ai-agent.gate.md`.
- **No hardcoded provider/model.** TEXT + IMAGE routes resolve per org via `IWorkflowConfigurationResolver`;
  capability is read from the resolved adapter, never pinned.
- **Additive + reversible.** Capture fields are nullable JSON-blob keys (no migration); the one telemetry
  migration is additive + reversible.
- **Docs-sync (MUST):** `openspec/specs/*` deltas for `content-generation`, `background-sourcing-policy`,
  `image-generation`, and `integrations` authored in the spec phase.
- **ADR (MUST):** the extended image-conditioning contract + capability-describe + telemetry correlation →
  an ADR under `docs/architecture/adr/` at apply time.
- Run `pwsh -File scripts/gates/Run-Gates.ps1` before completion; do not mark complete on any MUST failure.

## 11. Risks and mitigations (design-level)

| Risk | Severity | Mitigation |
|------|----------|------------|
| Configured IMAGE provider still fails after wiring (silent degradation persists) | **High** | Telemetry FIRST (OQ-6) so the failure is visible; verify end-to-end against a real provider before relying on it |
| Provider does not support image conditioning | **Medium** | Adapter-advertised capability (OQ-4); descriptor-prompt fallback; provider-agnostic — no hardcoding |
| Pairing derivation ambiguous / low-contrast | **Medium** | WCAG ≥ 4.5:1 selection with luminance black/white fallback; empty palette → no pairings → positional palette |
| Proportional pattern mapping off-by-one on odd counts | **Medium** | `floor((i-1)*len/count)` mapping pinned by tests across equal/unequal counts; cap 2 bounds blast radius |
| Captured slide count breaks packages relying on `~3-5` | **Medium** | Enforce only when captured; user request wins; sanity clamp `[3..10]`; parity test for null capture |
| Telemetry FK relaxation regresses analysis-scoped rows | **Medium** | Additive nullable migration; existing rows keep `analysis_id`; correlation is a NEW optional path; round-trip test |
| Provider SDK type leaks into Application via conditioning | **Medium** | Url-handle references; bytes resolved in adapter; `Port_HasNoInfrastructureTypes` contract test |
| Cold-start client with no capture | **Low** | Every field nullable → degrades to current output (OQ-5) |

## 12. Spec impact noticed (for `sdd-spec` / verify)
- **content-generation (modified):** `CreativeSlideIndexResolver` consumes `BackgroundPattern` (proportional
  mapping) instead of `{first,last}`; `CompositionDefaults.ResolvePalette` applies rotating `ColorPairings`
  instead of positional palette; `CarouselGenerator` enforces the effective slide count; `CarouselVisualComposer`
  gains the `@handle`/signature slot.
- **background-sourcing-policy (modified):** the `ClientAesthetic` capture surface gains `BackgroundPattern`,
  `TypicalSlideCount`, and `ColorPairings`; the extractor populates them.
- **image-generation (modified):** `ImageGenerationRequest.ReferenceImages` realises OQ-7 conditioning;
  `IImageGenerationGateway.DescribeAsync` + `ImageModelCapabilities` advertise image-input support; background-only
  generation contract + guardrail-validated bounded retry.
- **integrations / observability (modified):** `integration_execution_attempts` gains nullable `analysis_id` +
  `correlation_id`/`correlation_kind`; image-gen degradation persists per-slide rows; routing stays config-driven.

## 13. Sliced implementation order (single PR on `feat/claude-p-provider`) + next step
Seam order, capture → apply structure → apply colour → image conditioning → composition slots → observability:
1. **Capture VOs + profile fields** — `SlideBackgroundKind`, `ColorPairing`, extended `ClientAestheticProfile`,
   `AestheticExtraction`; `AestheticProfilePersistence` round-trip; extractor DTO mapping (+ pairing derivation).
2. **Apply structure** — `CreativeSlideIndexResolver.ResolveFromPattern`; `CarouselGenerator` effective slide
   count (OQ-3).
3. **Apply colour** — `CompositionDefaults.ResolvePalette` pairing/rotation overload; thread `slideIndex` from
   `CarouselVisualComposer`.
4. **Image conditioning** — `ImageGenerationRequest.ReferenceImages`, `IImageGenerationGateway.DescribeAsync` +
   `ImageModelCapabilities`, adapter capability + byte resolution + background-only directive + guardrail retry;
   background service capability-gated conditioning.
5. **Composition slots** — `ClientProfileSnapshot.Handle` (+ snapshot loader), `@handle`/signature copy slot.
6. **Observability** — telemetry correlation contract + policy persistence relax + EF migration; per-slide
   degradation rows from the background service; wire the execution policy into the image gateway.
7. **ADR + docs/spec sync + gates.**

`sdd-tasks` owns the checklist; this design owns the HOW. Next: `sdd-tasks` (after the spec is also ready).
