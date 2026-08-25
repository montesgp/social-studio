# Design — client-aesthetic-fidelity-fixes (Faithful Degrade + 3-Role Typography + Per-Slide Photo Transparency)

Technical HOW for `proposal.md` (revision 2). The proposal settled *what* ships and *why*; this document
fixes the branch structure, the exact contract shapes, the render mechanism, the merge rule, and the
test seams. It does **not** enumerate task steps — that is `sdd-tasks`.

## Status

Design — architecture decisions resolved. Every new contract member is additive-nullable or
init-defaulted, so **no EF migration and no `ContentItemDto` change**. Dependency rule unchanged:
Domain ← Application ← Infrastructure/Api. `require_adr_for_architecture_changes` stays untriggered
(no new port, provider category, credential type or bounded area; the render mechanism in D5 reuses the
existing inline-style markup builders rather than introducing a render abstraction).

---

## 0. The decisions (read this first)

| # | Question | Decision | One-line rationale |
|---|---|---|---|
| **D1** | Degrade-trigger detection | A pure static `ClientAestheticDegradeDecision` (Application/`Shared`) exposing `IsEligible`, `TriggerA`, `TriggerB` + a `SlideDegradeTrigger` enum; the processor branches on those calls only | The proposal's own risk mitigation demands a directly-testable predicate; the processor keeps zero degrade logic inline |
| **D2** | Envelope degrade marker | `string? DegradeReason = null` as the trailing positional member of `CompositionSpecEnvelope`, annotated `[property: JsonIgnore(Condition = WhenWritingNull)]`, tokens `photo-image-unavailable` / `designer-unusable` | A string token survives `JsonSerializerDefaults.Web` round-trip and reads directly in TS; `WhenWritingNull` is what keeps a non-degraded envelope **byte-identical** to today |
| **D3** | Per-slide transparency shape | `SlideBackgroundTransparency(bool Blended, string? Descriptor)`, carried as `IReadOnlyList<SlideBackgroundTransparency?>?` index-aligned with `BackgroundPattern`; nullable entry = unmeasured slide | Mirrors the proposal's "boolean + qualitative descriptor" verbatim; a nullable **entry** expresses per-slide unmeasured without a sentinel |
| **D4** | Third typography role | `CompositionTypography` gains `CaptionFamily`/`CaptionSize` (nullable), `CaptionWeight` (default `800`), `CaptionItalic`, `RequestedCaptionFamily`, plus `EffectiveCaptionFamily => CaptionFamily ?? BodyFamily` | Nullable + a computed fallback means one-line consumer changes and provable cold-start parity |
| **D5** | Render mechanism | **Flat `opacity` on the `<img>` composited over a solid palette-derived backdrop**, single constant `CompositionDefaults.BlendedPhotoOpacity = 0.72`; the descriptor is never parsed at render time | The only mechanism that is deterministic, direction-free, and stacking-context-independent under Chromium screenshotting |
| **D6** | Studio consumption | One optional `degradeReason?: string \| null` on the `Envelope` interface + a small `SlideDegradeBadge` presentational component used by the viewer and the slide strip | Additive to a `[key: string]: unknown` interface; `serializeEnvelopes` is untouched so the autosave round-trip is preserved by construction |
| **D7** | `AllowedFonts` merge | `client classes (heading, body, caption — resolved, deduped, blanks dropped) ++ brand.Typography`, case-insensitive distinct, client-first ordering; **supplement, never replace** | A degenerate client set (0 or 1 family) can then never starve the designer, and a null context produces a byte-identical array |

---

## 1. D1 — Degrade-trigger branch structure

New pure type, `Incoders.Hive.Application/Features/Social/ContentGeneration/Shared/ClientAestheticDegradeDecision.cs`:

```csharp
public enum SlideDegradeTrigger { None = 0, PhotoImageUnavailable, DesignerUnusable }

public static class ClientAestheticDegradeDecision
{
    // Shared precondition for BOTH triggers. ColorPairings is required because the faithful
    // treatment IS the pairings-backed flat treatment — without it there is nothing faithful to
    // degrade to, so the legacy path must stand (cold-start parity).
    public static bool IsEligible(
        BackgroundSourcingPolicy? policy, bool isCarouselLayout, bool hasColorPairings);

    // A: the pattern calls this slide Photo, but no generated image reached it.
    public static SlideDegradeTrigger TriggerA(
        bool eligible, SlideBackgroundKind? mappedKind, bool hasGeneratedImage);

    // B: the designer produced nothing usable (error OR guardrail rejection) and no enforced
    // background exists.
    public static SlideDegradeTrigger TriggerB(
        bool eligible, bool hasEnforcedBackground, bool hasUsableAiMarkup);
}
```

Branch order inside `ContentGenerationJobProcessor.cs` (~L807-954):

```
if (ClientAesthetic && mappedFlatKind == FlatColor && !hasGeneratedImage && ColorPairings)   // UNCHANGED, FIRST
    -> BuildFlatColorMarkup                                                     (pre-existing path)
else
    eligible = ClientAestheticDegradeDecision.IsEligible(preLoopPolicy, isCarouselLayout, hasPairings)
    if (TriggerA(eligible, mappedFlatKind, slidesWithGeneratedImage.Contains(specIndex + 1)) != None)
        -> RenderFaithfulFlat(...)    // NO BackgroundSelection resolve, NO _designer.ComposeAsync
           degradeReason = "photo-image-unavailable"
    else
        ... existing selection + ComposeAsync + TypographyNormalizer + _guardrails (UNCHANGED) ...
        if      (enforcedBackground is not null) -> BuildTemplateBackedMarkup(...)
        else if (aiMarkup is not null)           -> aiMarkup
        else if (TriggerB(eligible, false, false) != None)
                                                 -> RenderFaithfulFlat(...)
                                                    degradeReason = "designer-unusable"
        else                                     -> BuildTemplateBackedMarkup(spec, null, ...)  // SCRIM CARD SURVIVES
```

- **Priority is structural, not conditional**: the `FlatColor` short-circuit is the `if`; both triggers
  live only in its `else`. A FlatColor slide can never reach either trigger.
- **Trigger A and B are disjoint by construction**: A requires `mappedKind == Photo`; B is only reachable
  after A declined and the designer chain ran. A `Decorator` slide skips A and remains eligible for B.
- **Trigger B is scoped by `IsEligible`**, which is the single place `BackgroundSourcingPolicy.ClientAesthetic`
  is checked. Every other policy falls to the final `else` and keeps the `rgba(0,0,0,0.52)` scrim card
  byte-for-byte.
- `RenderFaithfulFlat` is a small private local helper wrapping the existing
  `ResolvePalette` + `BuildFlatColorMarkup` + `slideTextColorHex` block so it is written once, not three
  times. Its output is identical to the short-circuit's — the degrade *is* the proven flat path.

## 2. D2 — Envelope marker

```csharp
public sealed record CompositionSpecEnvelope(
    int SchemaVersion, string Markup, CanvasDimensions Canvas,
    IReadOnlyList<SlotGeometryEntry> SlotGeometry,
    string? BackgroundImageRef = null, string? FlattenedImageRef = null, bool Deleted = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DegradeReason = null);
```

The processor declares `string? capturedDegradeReason = null` beside `capturedMarkup`, assigns it in the
two degrade branches, and passes it at the single construction site (~L1153). `SchemaVersion` stays `1`:
the member is additive-optional, so a consumer of v1 is unaffected. `ParseList`/`ParseActiveList` need no
change. Tokens are `const string` on `SlideDegradeTrigger`'s companion so backend, tests and the TS badge
agree on one spelling.

## 3. D3 — Per-slide transparency, six layers

| # | Layer | File | Shape |
|---|---|---|---|
| 1 | Extractor prompt + DTO | `Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs` | `slideBackgroundTransparency: [{ blended: bool?, descriptor: string? }]`, emitted next to `slideMixPattern`; defensive `Map()` exactly like `imageTypeCounts` |
| 2 | Extraction contract | `Abstractions/.../IClientAestheticStyleExtractor.cs` | `IReadOnlyList<SlideBackgroundTransparency?>? BackgroundTransparency = null` (trailing) |
| 3 | Persistence | `Shared/AestheticProfilePersistence.cs` | `StoredSlideTransparency(bool? Blended, string? Descriptor)` under key `backgroundTransparency`; `Read` tuple gains a trailing element |
| 4 | Profile | `Shared/ClientAestheticProfile.cs` | trailing nullable, same list type |
| 5 | Design context | `Shared/ClientAestheticDesignContext.cs` | trailing nullable, built at processor L469-481 |
| 6 | Render | processor `BuildTemplateBackedMarkup` | see D5 |

**Reconciliation** — new pure `Shared/BackgroundTransparencyReconciler.Align(pattern, transparency)`,
modelled on `BackgroundPatternReconciler`'s contract (tolerant, never throws, returns input unchanged when
there is nothing to reconcile): returns `null` when either input is null/empty; otherwise pads with `null`
entries to `pattern.Count` and truncates surplus entries. It runs at the extractor `Map()` site — the same
place `BackgroundPatternReconciler` runs — so the persisted array is canonically aligned once.

**Slide lookup** is *not* raw indexing. `BackgroundPattern` is already mapped onto the actual slide count by
the proportional formula in `CreativeSlideIndexResolver.ResolveKindForSlide`; the transparency signal gets a
sibling `ResolveTransparencyForSlide(transparency, totalSlides, slideNumber)` using the identical formula, so
the two signals can never drift for a carousel whose length differs from the analyzed corpus. Out-of-range or
null ⇒ `null` ⇒ today's `object-fit:cover`.

## 4. D4 — Caption/overline role

```csharp
// CompositionTypography — additive init members, every positional site keeps compiling
public string? CaptionFamily { get; init; }         // null -> falls back to BodyFamily
public int?    CaptionSize   { get; init; }         // null -> the builder's existing hardcoded size
public int     CaptionWeight { get; init; } = 800;  // today's cta weight
public bool    CaptionItalic { get; init; }
public string? RequestedCaptionFamily { get; init; }
public string  EffectiveCaptionFamily => CaptionFamily ?? BodyFamily;
```

- `TypographyRoles` gains `Caption = "caption"` and `Overline = "overline"`. `ResolveTypography` (4-arg,
  L159-199) resolves `FindRole(observed, Caption) ?? FindRole(observed, Overline) ?? FindRole(observed, Accent)`
  — `Accent` already exists and is the closest legacy synonym — then `FontFamilyResolution.Resolve` exactly as
  heading/body do, with `SizeFor` for `CaptionSize` and `RequestedCaptionFamily` set only when
  `!IsExact`. The existing early-outs (`observed` empty, or heading **and** body both absent) are unchanged:
  caption alone never activates the profile-aware branch.
- **Consumers**: `BuildCarouselTemplateSlots` and `BuildFlatCarouselSlots` use
  `EffectiveCaptionFamily`/`CaptionWeight`/`CaptionSize` for the `cta` slot; `BuildStoryTemplateSlots` for
  `cta` + `signature`. With a null caption these render exactly today's bytes.
- **AI path**: `TypographyNormalizer`'s effective-family set gains the caption family, so a generic-only
  declaration in a caption-scale element resolves to the client's caption class instead of body. Null caption
  ⇒ set unchanged ⇒ current behaviour.

## 5. D5 — Render-time transparency treatment

Chosen mechanism, applied only when the resolved signal for this slide is `Blended: true`:

```html
<div data-layer="background" style="...;background:{paletteBackgroundHex};">
  <img ... style="position:absolute;inset:0;width:100%;height:100%;object-fit:cover;opacity:0.72;" />
```

- One constant, `CompositionDefaults.BlendedPhotoOpacity = 0.72`. The reduced alpha composites against the
  **client's own** palette background (from `ResolvePalette`), not the `#000000` root — otherwise "blended"
  would read as "dimmed".
- The **descriptor is advisory metadata only**: it is never parsed at render time. It travels to
  `ClientAestheticDesignContext` for the designer prompt and for diagnostics/reporting. Rejected
  alternatives: *gradient fade* (needs a direction the signal does not carry, so it would be invented) and
  *`mix-blend-mode`* (result depends on the stacking context and blends unpredictably with the accent divider
  and text-shadows under headless Chromium).
- `BuildTemplateBackedMarkup` takes one new optional parameter `SlideBackgroundTransparency? transparency = null`;
  the **caller** resolves it per `specIndex` (the builder stays a pure function of its inputs, as today).
  This covers the enforced-background case, which is where a real generated photo actually renders.
- The AI path emits its own markup and is not rewritten post-hoc; the signal reaches it through the design
  context prompt only. This is stated as a known asymmetry, not a gap to be closed by markup surgery.

## 6. D6 — Content Studio badge

`Envelope` gains one member: `degradeReason?: string | null;`. A `SlideDegradeBadge` presentational
component (props: `reason: string`) renders a subtle informational chip — styled after
`InferredFieldsDisclosure`, never an error state — in the carousel/story viewer corner, and a small dot on
the corresponding slide-strip thumbnail. No API, DTO or endpoint change; `parseEnvelopes` and
`serializeEnvelopes` are untouched, so the key survives edit + autosave because the whole object is
`JSON.stringify`-ed.

## 7. D7 — `AllowedFonts` merge rule

In `AiVisualDesigner.BuildInputJson` (L286):

```
clientClasses = aesthetic?.Typography              // heading, body, caption order
                 .Select(t => FontFamilyResolution.Resolve(t.FontFamily).Renderable)
                 .Where(non-blank)
AllowedFonts = clientClasses.Concat(brand.Typography)
                 .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
```

Client-first ordering (earlier entries read as preferred by the model); the brand kit is **appended, never
removed**, which is what makes a 0- or 1-family client set safe. A null design context or an empty client set
yields an array equal to today's `brand.Typography.ToArray()`. Resolution happens through the Application
static `FontFamilyResolution`, the same way the processor already uses `FontCatalog` — no new port.

## 8. Data flow

```
extractor prompt ──► AestheticExtractionDto ──► Map() ──► AestheticExtraction
                                                  │ (BackgroundTransparencyReconciler.Align)
                                                  ▼
                                    AestheticProfilePersistence (aesthetic_profile_json)
                                                  ▼
                       EfClientAestheticProfileReader ──► ClientAestheticProfile
                                                  ▼
                                    ClientAestheticDesignContext (processor L469-481)
                                                  ▼
       ┌──────────── ContentGenerationJobProcessor render loop (~L807-954) ────────────┐
       │  FlatColor short-circuit  │  TriggerA ──► RenderFaithfulFlat                  │
       │       (untouched)         │  designer ──► TriggerB ──► RenderFaithfulFlat     │
       │                           │           └─► BuildTemplateBackedMarkup(+opacity) │
       └───────────────────────────┴──────────────────┬───────────────────────────────┘
                                                      ▼
                    CompositionSpecEnvelope{ DegradeReason } ──► composition_spec_json
                                                      ▼
                       ContentItem.visualSpecMarkup ──► parseEnvelopes ──► SlideDegradeBadge
```

## 9. File changes

| File | Action | What |
|---|---|---|
| `Application/.../Shared/ClientAestheticDegradeDecision.cs` | Create | Pure trigger predicates + `SlideDegradeTrigger` + reason tokens (D1) |
| `Application/.../Shared/BackgroundTransparencyReconciler.cs` | Create | Tolerant index alignment against `BackgroundPattern` (D3) |
| `Application/.../Shared/SlideBackgroundTransparency.cs` | Create | `(bool Blended, string? Descriptor)` (D3) |
| `Application/.../Pipeline/ContentGenerationJobProcessor.cs` | Modify | Trigger branches + `RenderFaithfulFlat` + `capturedDegradeReason` + per-slide opacity + caption slots (D1/D2/D4/D5) |
| `Application/.../Pipeline/CompositionSpecEnvelope.cs` | Modify | Trailing `DegradeReason` with `WhenWritingNull` (D2) |
| `Application/.../Visual/CompositionDefaults.cs` | Modify | Caption resolution in the 4-arg `ResolveTypography`; `BlendedPhotoOpacity` (D4/D5) |
| `Application/.../Shared/AestheticTypography.cs` | Modify | `TypographyRoles.Caption` / `.Overline` (D4) |
| `Application/.../Shared/AestheticProfilePersistence.cs` | Modify | `StoredSlideTransparency` + read/write round-trip (D3) |
| `Application/.../Shared/ClientAestheticProfile.cs`, `ClientAestheticDesignContext.cs` | Modify | Trailing nullable `BackgroundTransparency`; context also gains `Typography` (D3/D7) |
| `Application/Abstractions/.../IClientAestheticStyleExtractor.cs` | Modify | `AestheticExtraction.BackgroundTransparency` (D3) |
| `Application/.../Background/CreativeSlideIndexResolver.cs` | Modify | `ResolveTransparencyForSlide` sibling (D3) |
| `Domain/Social/ContentGeneration/VisualCompositionSpec.cs` | Modify | Caption members on `CompositionTypography` (D4) |
| `Infrastructure/.../MultimodalAestheticStyleExtractor.cs` | Modify | Typography (3 roles) + transparency prompt/DTO/`Map()` (D3/D4) |
| `Infrastructure/.../AiVisualDesigner.cs` | Modify | `AllowedFonts` merge (D7) |
| `Infrastructure/.../TypographyNormalizer` call site | Modify | Caption family in the effective set (D4) |
| `apps/web/components/social/content-studio-panel.tsx` | Modify | `Envelope.degradeReason` + `SlideDegradeBadge` (D6) |
| `openspec/changes/.../comfyui-generation-time-report.md` | Create | WU-0 findings, documentation only |

## 10. Testing Strategy

Both degrade triggers are only observable under **forced failure**. The good news from reading the suite:
the exact fixtures already exist and are extended rather than invented.

| Layer | What | Approach / seam |
|---|---|---|
| Unit | `ClientAestheticDegradeDecision` truth table | New `Application/Social/ContentGeneration/Shared/ClientAestheticDegradeDecisionTests.cs`. Pure function, no processor: policy × layout × mappedKind × hasGeneratedImage × hasPairings, including the non-ClientAesthetic row that MUST return `None` |
| Unit | `BackgroundTransparencyReconciler` + `ResolveTransparencyForSlide` | Shorter, longer, empty, null arrays — asserts pad-with-null / truncate and **never throws**, alongside the existing `BackgroundPatternReconcilerTests` |
| Unit | `ResolveTypography` caption | Extend the existing `CompositionDefaults` typography tests: caption present / absent / overline alias / accent alias / caption-only (must still early-out) |
| Unit | Envelope serialization | `CompositionSpecJsonTests` — a non-degraded envelope's JSON contains **no** `degradeReason` key (byte-identity), a degraded one round-trips the token, and a legacy blob parses with `DegradeReason == null` |
| Integration (trigger A) | Photo slide, image generation failed → flat treatment, designer never called | Reuse `ProcessorImageBackgroundResilienceTests`' `FixedResultImageService` with an **empty**/partial map (already present) plus `ContentGenerationJobProcessorVisualTests`' `FC_NeverCalledDesigner` to assert zero `ComposeAsync` calls |
| Integration (trigger B) | ClientAesthetic slide, designer fails → flat treatment, **no** `rgba(0,0,0,0.52)` in markup | Reuse `ActiveFailingDesigner` (already in `ContentGenerationJobProcessorVisualTests`) and `FailingGuardrailValidator` for the guardrail-rejection arm — both arms MUST reach trigger B |
| Integration (no collateral) | Non-ClientAesthetic job, designer fails → scrim card still present | Same `ActiveFailingDesigner`, policy set to anything other than `ClientAesthetic`; assert the scrim substring IS in the markup |
| Integration (no shadowing) | FlatColor slide takes the pre-existing short-circuit, never reaches a trigger | Extend the `FC_*` fixtures in `ContentGenerationJobProcessorVisualTests` |
| Integration | Envelope marker written on exactly the degraded slides | `CompositionSpecJsonPersistenceTests` pattern (`StubMarkupDesigner` / `AlwaysFailingGuardrails` are already there) |
| Integration | Per-slide render: signal on slide 2 only | `BuildTemplateBackedMarkupTests` — slide 2 carries `opacity:0.72`, every other slide keeps unchanged `object-fit:cover` with no opacity token |
| Integration | Export cleanliness | Assert PPTX/ZIP export output contains no `degradeReason` text and is unchanged for a non-degraded slide |
| Frontend | Badge + autosave round-trip | Vitest on `content-studio-panel`: badge renders for a marked envelope, absent otherwise, and `serializeEnvelopes(parseEnvelopes(x))` preserves the unknown key |

Strict TDD: every row above is written RED first, per work unit (WU-1a/1b/2a/2b/3a/3b/4).

## 11. Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary. The change touches markup generation, JSON serialization of an existing
column, and one React component.

## 12. Migration / Rollout

**No migration required.** Every new field is additive-nullable or init-defaulted inside existing
JSON blobs (`aesthetic_profile_json`, `composition_spec_json`) or existing records. Existing rows read back
as null/false and take today's code path; profiles converge on the next analysis refresh. Rollout follows the
proposal's Delivery table (WU-0, WU-1a→1b, WU-2a→2b, WU-3a→3b, WU-4) as direct sequential commits to `dev`.

## 13. Open Questions

- [ ] `BlendedPhotoOpacity = 0.72` is a design-chosen constant, not a measured one. It is deliberately a
      single named constant so a visual review of the Lucia Fernandez reference job can retune it without a
      contract change.
- [ ] Whether the caption role should also drive a *new* dedicated overline slot rather than restyling the
      existing `cta`/`signature` slots. Current decision: restyle only — adding a slot changes the copy
      contract, which is out of this change's scope.
