# Design: Client-Aesthetic Visual Sophistication

## Technical Approach

Two additive tracks over the deterministic flat carousel path, no new capability and no AI call.
**A1**: one nested value object (`DecorationMotifs`) rides the existing extraction → persistence →
`ClientAestheticDesignContext` chain and drives two new deterministic render primitives (a divider
rule, an archetype geometry preset) inside `BuildFlatCarouselSlots`. **A3**: the existing
bounding-box crop port renders one ornament `<img>`, with an operator-visible degrade token when it
cannot. `DecorationMotifs = null` must produce byte-identical markup — enforced by a golden captured
*before* any render edit.

## Architecture Decisions

### D1 — `CompositionArchetype` is an open normalised string, preserved verbatim

| Option | Tradeoff | Decision |
|---|---|---|
| Closed C# enum | Compiler validity; every new client niche = schema + migration | Rejected (settled decision 5) |
| `string?` collapsing unrecognised → `"unknown"` (`NormalizeMarkType` verbatim) | Reuses precedent exactly; **discards** the novel token, so a later preset needs re-extraction | Rejected |
| **`string?`, `Normalize` = trim + lowercase only, unrecognised preserved** | No compile-time validity; a drifted token silently renders `left-aligned` | **Chosen** |

`Normalize` is the *only* mutation the token ever undergoes, and it happens once in the extractor's
`Map()`. Persistence writes the string, reads the string. The renderer never mutates it — it calls a
read-only `FlatGeometryPresets.Resolve(token)` whose `_ =>` arm returns the `left-aligned` preset.
Recognition therefore lives entirely in renderer code, so teaching the renderer `"diagonal-split"`
later is one `const string` plus one preset — no schema bump, no data migration, no re-extraction.
`DividerRuleStyle` stays a closed enum (its value set is bounded by what the CSS primitive draws,
not by clients); unrecognised token ⇒ `null` ⇒ no divider.

### D2 — Divider is one `data-slot` element with a non-slot child glyph

The glyph cannot be a second `data-slot="divider"`: `SlotGeometry` capture and
`PptxExportAdapter`'s `data-slot` regex (`PptxExportAdapter.cs` L600) would emit two entries for one
slot. The rule is the outer `data-slot="divider"` div; the diamond/dot is a nested plain `<div>`
with no `data-slot`. Built as a raw inline string (the `Slot()` helper HTML-escapes its text and
cannot carry a child) — the same technique `BuildBrandMarkSlot`'s `<img>` already uses.

### D3 — Ornament reuses the existing crop port, renamed

`IBrandMarkAssetExtractor.ExtractAsync(StoredAssetKey, string sourceImagePath, BrandMarkBoundingBox, ct)`
is already element-agnostic; only its name and doc comment are brand-mark-specific.

| Option | Tradeoff | Decision |
|---|---|---|
| Call it as-is for ornaments | Zero code; the name lies at a boundary two element types now cross | Rejected |
| **Rename to `IAestheticAssetCropExtractor` (+ `SkiaAestheticAssetCropExtractor`)** | Mechanical rename: port, adapter, DI registration, processor ctor param, one test class. Internal port, no external consumer | **Chosen** |
| New sibling port + second adapter | Two registrations and two adapters for one behaviour | Rejected |

This is the exact question the proposal's ornament ADR must record.

### D4 — Degrade precedence is a pure function, not inline ordering

`ClientAestheticDegradeDecision` gains
`ResolveDegradeReason(string? wholeSlideToken, bool ornamentUnavailable)` returning
`wholeSlideToken ?? (ornamentUnavailable ? OrnamentUnavailable : null)`. The processor calls it once
at envelope construction (`ContentGenerationJobProcessor.cs` L1315) instead of relying on statement
order. Rejected alternative: `capturedDegradeReason ??= …` at the failure site — correct today only
because the ornament block (L1057-1123) happens to run after both trigger sites (L891, L1002), and
untestable without the whole processor. The failure-site log is emitted **unconditionally**, before
and independently of that resolution, so the collision case loses no diagnostic information.

### D5 — Ornament is gated on the deterministic flat path

Ornament extraction, its `<img>` and its token only apply when this slide's markup came from
`RenderFaithfulFlat`. A new local `bool renderedDeterministicFlat`, set at each `RenderFaithfulFlat`
call site, gates all three. Prevents an `ornament-unavailable` badge on an AI-designed slide that
never had an ornament slot.

### D6 — Badge label map defaults unknown tokens to today's string

`getDegradeLabel` returns the generic `Fallback design` pair for any token not in the map, so
reverting the map leaves a persisted `ornament-unavailable` degraded, never broken, and the shipped
`CAFF-CS-01` badge is unregressed.

## Interfaces / Contracts

```csharp
// Application/Features/Social/ContentGeneration/Shared/DecorationMotifs.cs (new)
public enum DividerRuleStyle { Line = 1, LineWithDiamond = 2, LineWithDot = 3 }

public sealed record DecorativeOrnamentDetection(
    string? SourceImagePath = null,
    BrandMarkBoundingBox? BoundingBox = null);   // reused verbatim

public sealed record DecorationMotifs(
    DividerRuleStyle? DividerRule = null,        // null = unmeasured/absent (no companion bool)
    string? CompositionArchetype = null,         // OPEN, verbatim-preserved
    DecorativeOrnamentDetection? Ornament = null);

public static class CompositionArchetypes
{
    public const string LeftAligned = "left-aligned";  // identity preset
    public const string Centered    = "centered";
    public const string LowerThird  = "lower-third";
    public static string? Normalize(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToLowerInvariant();  // NEVER collapses
    public static bool IsRecognised(string? raw) =>
        Normalize(raw) is LeftAligned or Centered or LowerThird;
    public static DividerRuleStyle? TryParseDividerRule(string? raw); // unrecognised => null
}

// Shared/ClientAestheticDegradeDecision.cs
public const string OrnamentUnavailable = "ornament-unavailable";
public static string? ResolveDegradeReason(string? wholeSlideToken, bool ornamentUnavailable);
```

Extractor DTO (`MultimodalAestheticStyleExtractor.cs`), mirroring `BrandMarkDto`/`imageTypeCounts`:
`decorationMotifs { dividerRule, compositionArchetype, ornament { sourceImageIndex, boundingBox } }`.
`sourceImageIndex` → `StoragePath` via the existing post-skip `loadedPairs` resolution (D9 precedent).
`PromptVersion` bumps (v7 for A1, v8 for the ornament) and must stay equal to
`AestheticFreshness.CurrentSchemaVersion`.

### Geometry presets (canvas 1080×1350)

| Slot | `left-aligned` (identity — today's literals) | `centered` | `lower-third` |
|---|---|---|---|
| accent | `left:104;top:180` | `left:456;top:180` | `left:104;top:620` |
| title | `left:104;top:235;w:872;align:left` | same box, `align:center` | `left:104;top:675;w:872` |
| divider | `left:104;top:492` | `left:450;top:492` | `left:104;top:930` |
| subtitle | `left:108;top:540;w:815;align:left` | `left:132;top:540;w:816;align:center` | `left:108;top:960;w:815` |
| cta | `left:108;bottom:220;w:760` | `left:160;bottom:220;w:760;align:center` | `left:108;bottom:120;w:760` |
| ornament | `right:96;top:64;160×140` | `left:460;top:40` | `right:96;top:64` |

`left-aligned` reproduces the current literals exactly. Non-identity pixel values are design
proposals; apply-time asserts no preset overlaps the title block or the brand mark's `bottom:56`
anchor. Ornament is anchored deterministically, never at its source position (`BuildBrandMarkSlot`
precedent), `object-fit:contain`.

## Data Flow

    vision JSON ──Map()/Normalize──→ AestheticExtraction.DecorationMotifs
         │                                      │
         │                          AestheticProfilePersistence (JSON blob, verbatim token)
         │                                      ↓
         │                          ClientAestheticProfile → ClientAestheticDesignContext
         ↓                                      ↓
    sourceImageIndex→StoragePath    BuildFlatColorMarkup → BuildFlatCarouselSlots
                                       ├─ FlatGeometryPresets.Resolve(token)  (unrecognised ⇒ identity)
                                       ├─ divider element (accent ?? text colour)
                                       └─ ornament <img> ← IAestheticAssetCropExtractor
                                                             │ failure ⇒ log (always)
                                                             ↓  + ornamentUnavailable = true
                              ResolveDegradeReason(wholeSlideToken, ornamentUnavailable) → envelope

`StudioVisualMarkupResolver` needs **no change**: it already inlines every `src="…"` in the markup
(L83). `PptxExportAdapter` **does** need one — its brand-mark picture path is scoped by a
`data-slot="brand-mark"`-specific regex (L153), so the ornament needs a sibling matcher plus
skip-in-text-loop handling (L315-350).

## File Changes

| File | Action | Description |
|---|---|---|
| `Application/…/Shared/DecorationMotifs.cs` | Create | Record, enum, `CompositionArchetypes`, `DecorativeOrnamentDetection` |
| `Application/…/Shared/FlatGeometryPresets.cs` | Create | Token → preset lookup, identity `_ =>` arm |
| `Application/Abstractions/…/IClientAestheticStyleExtractor.cs` | Modify | 13th trailing-nullable field on `AestheticExtraction` |
| `Application/…/Shared/AestheticProfilePersistence.cs` | Modify | 15th tuple element on `Read` (+ every call site), DTO member |
| `Application/…/Shared/ClientAestheticProfile.cs`, `ClientAestheticDesignContext.cs` | Modify | Trailing-nullable passthrough |
| `Application/…/Pipeline/ContentGenerationJobProcessor.cs` | Modify | Divider, presets, ornament crop + `<img>`, `renderedDeterministicFlat`, log, `ResolveDegradeReason` |
| `Application/…/Shared/ClientAestheticDegradeDecision.cs` | Modify | `OrnamentUnavailable` token + precedence function |
| `Application/Abstractions/…/IBrandMarkAssetExtractor.cs` | Rename | → `IAestheticAssetCropExtractor` |
| `Infrastructure/…/SkiaBrandMarkAssetExtractor.cs` + DI | Rename | → `SkiaAestheticAssetCropExtractor` |
| `Infrastructure/…/MultimodalAestheticStyleExtractor.cs` | Modify | Prompt section, DTO, `Map()`, index→path, version bump |
| `Infrastructure/Rendering/PptxExportAdapter.cs` | Modify | Ornament `<img>` matcher + slot skip |
| `apps/web/lib/studio/degrade-badge.ts` | Modify | `getDegradeLabel` token→label map |
| `apps/web/components/social/content-studio-panel.tsx` | Modify | `SlideDegradeBadge` takes `reason`; 3 call sites pass it |
| `docs/architecture/adr/*` | Create | Ornament crop-port ADR; schema-fatigue-norm ADR |

## Testing Strategy

| Layer | What | Where |
|---|---|---|
| Unit | `Normalize` preserves an invented token verbatim; `IsRecognised` false; `TryParseDividerRule` unrecognised ⇒ null | `Tests/Application/…/Shared/DecorationMotifsTests.cs` (new, sibling of `BrandMarkDetectionTests.cs`) |
| Unit | Verbatim round-trip: `"diagonal-split"` in ⇒ same string out (never `"unknown"`); pre-change blob ⇒ null, no throw | extend `AestheticProfilePersistenceTests.cs`, `AestheticExtractionExtensionTests.cs`, `ClientAestheticProfileExtensionTests.cs` |
| Unit | DTO/`Map()` defensive parse; version bump | extend `MultimodalAestheticStyleExtractorTests.cs`, `PromptVersionBumpAestheticTests.cs` |
| Unit | **Byte-identical cold start**: `Assert.Equal` on the full markup string vs a golden captured from the pre-change build, `DecorationMotifs = null` | new `Tests/Application/…/Pipeline/FlatCarouselMarkupGoldenTests.cs` + `Goldens/flat-carousel-cold-start.html`, committed in WU-4 **before** any render edit |
| Unit | Divider present/absent, accent-vs-text colour, one `data-slot="divider"` only; two profiles differing only in archetype ⇒ different geometry; invented token ⇒ identity markup; no preset overlap | same golden test class |
| Unit | Precedence: whole-slide token + ornament failure ⇒ whole-slide token; ornament alone ⇒ `ornament-unavailable`; neither ⇒ null | extend `ClientAestheticDegradeDecisionTests.cs` |
| Integration | Processor: a slide with BOTH degrades ⇒ envelope carries only the whole-slide token **and** the ornament log entry was still written (assert on the `ILogger` spy); no AI model invoked on any flat slide | extend `ProcessorAestheticWiringTests.cs` |
| Integration | Crop adapter over the renamed port; ornament ref resolves in Studio preview and PPTX; marker never leaks into export | extend `SkiaBrandMarkAssetExtractorTests.cs` (renamed), `PptxExportAdapterTests.cs` |
| Frontend | Each token → its own label; unknown token ⇒ `Fallback design` | extend `apps/web/lib/studio/degrade-badge.test.ts` |

Goldens are excluded from the authored review-line count but remain in snapshot identity.

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary. Image bytes are decoded by the already-registered SkiaSharp adapter
from paths the pipeline itself archived; no new external input surface.

## Migration / Rollout

No migration. The profile is a JSON blob and every field is additive-nullable; pre-change blobs read
`DecorationMotifs = null`. The port rename is compile-time only. WU-7/WU-8 stay gated on WU-6's
recorded bounding-box measurement; if it fails, both are cut, the ornament ADR is dropped, and the
schema-fatigue ADR ships alone.

**Schema-fatigue norm artifact (WU-9)**: `docs/architecture/adr/` ADR (the norm's own permitted
escape hatch) + a normative statement in the `profile-analysis` spec + a one-line XML-doc pointer on
both `AestheticExtraction` and `ClientAestheticProfile` naming the ADR, so the constraint is visible
at the point of temptation. `.claude/CLAUDE.md` is deliberately **not** used — it is not read by the
gates and would duplicate the ADR.

## Open Questions

- [ ] Proposal question 1 (recognised v1 archetypes) is unanswered; this design assumes the stated
      default — `left-aligned` / `centered` / `lower-third`. **Design assumption, not a re-litigation.**
- [ ] Non-identity preset pixel values above are design proposals; apply-time overlap assertions are
      the binding constraint, not the exact numbers.
- [ ] Divider contrast guard: design proposes falling back to text colour when the accent hex equals
      the background hex. Spec phase owns the full contrast rule.
