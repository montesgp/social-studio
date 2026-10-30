# Design: single-analyzer-visual-quality — Opus-as-Designer + 3-Tier Background (Fase B / B1)

## Overview

B1 fixes the "looks like Paint" problem (#6) by inserting an **LLM design-brain stage** into the
content-generation pipeline and giving the brand manual a **3-tier background model**. Opus 4.8 stops
being asked indirectly — it becomes the explicit DESIGNER, emitting brand-bound HTML/CSS that a
headless-Chromium renderer materializes at crisp 2x.

The work splits into two chained slices behind a frozen contract:

- **B1a — Domain/Application (engine-agnostic).** 3-tier `BackgroundEntry` extension, the
  `IVisualDesigner` Application port, the `BackgroundSelection` discriminated result, and the four
  guardrails (palette, typography, overflow, schema). Everything testable with a STUBBED designer —
  no real LLM, no browser.
- **B1b — Infrastructure.** `HtmlChromiumContentRenderer : IContentRenderer` (Playwright .NET),
  the real Opus `IVisualDesigner` implementation, font/browser ops. SkiaSharp stays as a
  config-selectable fallback. B1b DEPENDS on B1a's frozen contract.

**Decided up front (do not re-open):** rendering engine = **Engine A, HTML/CSS via headless Chromium
(Playwright .NET)**. The spike (#569) showed quality TIES SkiaSharp on fixed templates, but Engine A
is the only engine that lets Opus author over VARIABLE content with full CSS expressive range.
SkiaSharp remains a fallback adapter behind the existing `IContentRenderer` port.

This doc is the HOW (architecture/seams). The spec (`specs/content-generation/spec-delta.md`,
engram #568) is the WHAT. Authoritative inputs: proposal #567, spike #569, editability model #570,
3-tier model #553, exploration #565.

## Architectural Boundaries

Strict hexagonal — dependencies point inward (Domain ← Application ← Infrastructure/Api). No
EF/Npgsql/Supabase/AspNetCore/Playwright/SkiaSharp type may appear in Domain or Application.
`TreatWarningsAsErrors` is on for Domain/Application/Infrastructure, so any non-exhaustive `switch`
over a new enum is a build break — every dispatch site must be exhaustive.

| Layer | Change in this change (slice) |
|---|---|
| Domain (B1a) | Extend `BackgroundEntry` with `Tier`/`FormatKey`/`IsFormatDefault`; add `BackgroundTier` enum; add `BackgroundSelection` discriminated result; mirror three fields on `BrandBackgroundTemplateSnapshot`; add `ClientBrandManual` invariant (≤1 forced default per FormatKey); add `VisualDesignOutput` record + `VisualGuardrailErrors`. |
| Application (B1a) | Define `IVisualDesigner` port; add `IVisualGuardrailValidator` (palette/typography/overflow/schema) + concrete validator; make `SelectBackgroundTemplate` tier-aware returning `BackgroundSelection`; wire designer + guardrails into the compose→render seam of `ContentGenerationJobProcessor`. |
| Infrastructure (B1b) | Add `HtmlChromiumContentRenderer : IContentRenderer` (Playwright, pooled browser); add `OpusVisualDesigner : IVisualDesigner`; config-select renderer (Chromium default / SkiaSharp fallback); font-install + browser-launch ops. |
| Persistence (B1a) | `ClientBrandManualData.BackgroundTemplates` JSON: additive — legacy entries with no `Tier` deserialize as `Creative`. No migration, no column change. |
| Api / Frontend | No contract change in B1. (Brand-manual editor UI for marking tier/default is a SEPARATE follow-up, out of B1 scope.) |
| Deploy / Ops (B1b) | Playwright .NET package + `playwright install chromium` + font package in the Linux container image. Flagged as a deploy task, not code. |

## Architecture Decisions

### Decision 1 — `BackgroundTier` enum + `BackgroundEntry` extension (Domain), additive & backward-compatible

**Context.** `BackgroundEntry(Name, Url, UseCase?)` is a sealed record with `Create`/`Restore`
factories (`Domain/Social/ClientBrandManuals/BackgroundEntry.cs`). It is persisted as JSON inside
`ClientBrandManualData.BackgroundTemplates`. BG-01 requires three new fields; BG-01's legacy scenario
requires entries with no `Tier` to deserialize as `Creative`.

**Choice.** Add a Domain enum and extend the record positionally with DEFAULTED trailing parameters so
existing `Create(name, url, useCase)` and `Restore(name, url, useCase)` callers keep compiling, and so
System.Text.Json fills missing JSON properties with their defaults.

```csharp
// Domain/Social/ClientBrandManuals/BackgroundTier.cs
public enum BackgroundTier
{
    Creative = 0,      // default — legacy entries land here (BG-01 backward-compat)
    Reference = 1,
    ForcedDefault = 2,
}
```

```csharp
// Domain/Social/ClientBrandManuals/BackgroundEntry.cs — extended record
public sealed record BackgroundEntry(
    string Name,
    string Url,
    string? UseCase,
    BackgroundTier Tier = BackgroundTier.Creative,   // NEW, defaults to Creative
    string? FormatKey = null,                          // NEW
    bool IsFormatDefault = false)                      // NEW
```

- `Create` and `Restore` gain three OPTIONAL parameters with the same defaults. New `Create`
  overload validates: when `IsFormatDefault == true`, `FormatKey` MUST be non-empty AND
  `Tier` MUST be `ForcedDefault` (a forced default without a format key is meaningless — BG-02
  null-FormatKey scenario; entry-level guard complements the aggregate-level invariant).
- `FormatKey` is normalized (trim, null-if-empty) exactly like `UseCase`.
- **Why `Creative = 0`:** System.Text.Json deserializes a missing enum property to the zero value.
  Putting `Creative` at 0 makes legacy JSON round-trip to `Creative` with NO custom converter and NO
  migration — satisfying BG-01's legacy scenario by construction.

**Rejected — separate `BackgroundTierInfo` side record keyed by Name.** Rejected: splits one concept
across two collections, breaks the single-source-of-truth, and complicates the JSON shape. The fields
belong ON the entry.

**Rejected — non-defaulted positional params.** Rejected: breaks every existing `Create`/`Restore`
call site and the snapshot mapper; defaults keep the change additive.

### Decision 2 — `ClientBrandManual` invariant: ≤1 `IsFormatDefault` per non-null `FormatKey` (BG-02)

**Context.** `ClientBrandManual.Create`/`Update` accept `IReadOnlyList<BackgroundEntry>` and run
`HasSomeSignal`. There is no per-format-default invariant today. BG-02 requires a FAILED `Result`
(not exception, not silent overwrite) when a second forced default for the same format is supplied.

**Choice.** Add a private static guard invoked inside BOTH `Create` and `Update` AFTER the per-entry
lists are materialized, BEFORE success is returned:

```csharp
private static Result ValidateForcedDefaultUniqueness(IReadOnlyList<BackgroundEntry> backgrounds)
{
    // null/empty FormatKey forced-defaults are already rejected at entry level (Decision 1),
    // but re-guard here for the aggregate-as-authority case.
    var dupFormat = backgrounds
        .Where(b => b.IsFormatDefault && !string.IsNullOrWhiteSpace(b.FormatKey))
        .GroupBy(b => b.FormatKey!, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault(g => g.Count() > 1);

    if (dupFormat is not null)
        return Result.Failure(ClientBrandManualErrors.DuplicateForcedDefault(dupFormat.Key));

    var orphanForcedDefault = backgrounds
        .FirstOrDefault(b => b.IsFormatDefault && string.IsNullOrWhiteSpace(b.FormatKey));
    if (orphanForcedDefault is not null)
        return Result.Failure(ClientBrandManualErrors.ForcedDefaultWithoutFormat(orphanForcedDefault.Name));

    return Result.Success();
}
```

- New errors in `ClientBrandManualErrors`: `DuplicateForcedDefault(formatKey)` and
  `ForcedDefaultWithoutFormat(name)` (code `client-brand-manual.duplicate-forced-default` /
  `...forced-default-without-format`).
- `Restore` does NOT run the guard (data trusted from persistence — matches existing `Restore`
  semantics that bypass validation).
- The existing entry-level guard (Decision 1) covers the null-FormatKey scenario at `Create`;
  the aggregate guard is the authority when entries arrive pre-built.

**Rejected — DB unique index.** Rejected for B1: the entries live in a JSON column, not a relational
table; a partial unique index is not expressible. The invariant is a DOMAIN rule and belongs in the
aggregate, which is also where the failing `Result` surfaces to the caller.

### Decision 3 — `BackgroundSelection` discriminated result (Domain) + tier-aware selection (BG-03)

**Context.** `FlyerVisualComposer.SelectBackgroundTemplate` / `CarouselVisualComposer` currently pick
the first `UseCase`-matching template or `templates[0]`. BG-03 requires a strict tier-priority
dispatch returning a DISCRIMINATED result the caller never has to re-inspect. The pipeline must also
feed this selection to `IVisualDesigner` (Tier-1 = compose ON the image; Tier-2 = pass references as
multimodal style guides; Tier-3 = free creative).

**Choice.** Model `BackgroundSelection` as an abstract record with three sealed cases (records as a
closed discriminated union — C# idiom, exhaustive `switch` enforced by `TreatWarningsAsErrors`):

```csharp
// Domain/Social/ContentGeneration/BackgroundSelection.cs
public abstract record BackgroundSelection
{
    private BackgroundSelection() { }   // closed hierarchy

    public sealed record ForcedDefault(string Url, string Name) : BackgroundSelection;
    public sealed record References(IReadOnlyList<BrandBackgroundTemplateSnapshot> Entries) : BackgroundSelection;
    public sealed record Creative : BackgroundSelection;
}
```

Tier-aware selection (a static resolver on the snapshot side, callable from composers AND the pipeline):

```csharp
// Domain/Social/ContentGeneration/BackgroundSelector.cs
public static class BackgroundSelector
{
    public static BackgroundSelection Select(
        IReadOnlyList<BrandBackgroundTemplateSnapshot> templates,
        string formatKey)
    {
        // Tier-1: ForcedDefault matching this formatKey (IsFormatDefault=true).
        var forced = templates.FirstOrDefault(t =>
            t.Tier == BackgroundTier.ForcedDefault
            && t.IsFormatDefault
            && string.Equals(t.FormatKey, formatKey, StringComparison.OrdinalIgnoreCase));
        if (forced is not null)
            return new BackgroundSelection.ForcedDefault(forced.Url, forced.Name);

        // Tier-2: all Reference entries (regardless of FormatKey match — they are style guides).
        var references = templates.Where(t => t.Tier == BackgroundTier.Reference).ToArray();
        if (references.Length > 0)
            return new BackgroundSelection.References(references);

        // Tier-3: free creative generation, brand-bound.
        return new BackgroundSelection.Creative();
    }
}
```

- `BrandBackgroundTemplateSnapshot` is extended to mirror `Tier`/`FormatKey`/`IsFormatDefault`
  (BG-01 snapshot scenario) — see Decision 4.
- Composers and the pipeline call `BackgroundSelector.Select(...)` instead of the old
  first-match logic. The legacy `UseCase` field stays for backward display but no longer drives
  background selection.
- Tier-1 returns IMMEDIATELY with no generation flag — the pipeline passes `ForcedDefault.Url`
  straight into the designer's background slot; the designer composes text/decoration ON that image
  and generates NO background.

**Rejected — enum + nullable payload tuple.** Rejected: forces every caller to re-check the enum and
risk null-deref on the wrong arm. A closed record union makes illegal states unrepresentable and gives
exhaustive `switch`.

### Decision 4 — `BrandBackgroundTemplateSnapshot` mirrors the three fields; loader maps them

**Context.** `BrandBackgroundTemplateSnapshot(Name, Url, UseCase?)` is the Application-facing snapshot;
`EfClientBrandSnapshotLoader` builds it from `brandRow.BackgroundTemplates`. The Application layer must
operate on the snapshot WITHOUT referencing Domain entry types (it already does), so the snapshot must
carry the tier fields.

**Choice.** Extend the snapshot record additively (defaults keep existing constructions compiling) and
map the new fields in the loader:

```csharp
// Domain/Social/ContentGeneration/BrandBackgroundTemplateSnapshot.cs
public sealed record BrandBackgroundTemplateSnapshot(
    string Name,
    string Url,
    string? UseCase,
    BackgroundTier Tier = BackgroundTier.Creative,
    string? FormatKey = null,
    bool IsFormatDefault = false);
```

```csharp
// EfClientBrandSnapshotLoader — mapping (replace the 3-arg ctor call)
.Select(background => new BrandBackgroundTemplateSnapshot(
    background.Name, background.Url, background.UseCase,
    background.Tier, background.FormatKey, background.IsFormatDefault))
```

- `BackgroundTier` lives in `Domain.Social.ClientBrandManuals`; the snapshot lives in
  `Domain.Social.ContentGeneration`. Both are Domain — no boundary violation. Add the `using`.
- `BrandSnapshotTests` and the loader test gain tier round-trip assertions.

### Decision 5 — `IVisualDesigner` Application port + `VisualDesignOutput` (VD-01)

**Context.** Today there is NO design-intelligence stage; composers map payload→slots and the renderer
draws mechanically. The fix is an Application port that Opus implements in Infrastructure. VD-01 fixes
the exact signature; the port must be Infrastructure-type-free and live in Application.

**Choice.** Define the port and its output record exactly as the spec freezes them:

```csharp
// Application/Abstractions/Social/ContentGeneration/IVisualDesigner.cs
public interface IVisualDesigner
{
    Task<Result<VisualDesignOutput>> ComposeAsync(
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken);
}
```

```csharp
// Domain/Social/ContentGeneration/VisualDesignOutput.cs  (Domain — referenced by the port return type)
public sealed record VisualDesignOutput(
    string Markup,             // HTML document (constrained template — Decision 8)
    string PromptVersionKey,   // identifies the prompt template version
    bool IsValid)              // set by guardrail validation (false until validated)
{
    public VisualDesignOutput WithValidated() => this with { IsValid = true };
}
```

- `Markup` is **HTML/CSS** (the decided engine). The port stays engine-agnostic at the TYPE level
  (it is just a string) so SkiaSharp-fallback could in principle receive a different markup flavor,
  but B1's contract and validator assume the constrained HTML schema of Decision 8.
- `PromptVersionKey` is a deterministic string derived from the prompt template version — see
  Decision 9. VD-01 scenario requires it non-empty even for the stubbed designer.
- The real Opus call lives in `OpusVisualDesigner` (Infrastructure, B1b). Application depends ONLY on
  the port. All B1a tests inject a stub `IVisualDesigner` returning fixed `VisualDesignOutput`.
- `multimodal reference images for Tier-2` are passed THROUGH `backgroundSelection`
  (`BackgroundSelection.References` carries the entry URLs); the Infrastructure implementation fetches
  and attaches them as vision input. No Infrastructure type leaks into the port.

**Rejected — returning a `VisualCompositionSpec` from the designer.** Rejected: the spec is a
slot/typography DTO, too poor to carry an authored HTML composition. The designer's value is the rich
markup; a new output type is correct.

### Decision 6 — Guardrail pipeline: palette, typography, overflow, schema (VD-02/03/04)

**Context.** The designer is an LLM — non-deterministic. VD-02/03/04 require four checks BEFORE the
renderer is called, each failing the `Result` and NEVER forwarding a broken asset. Critically, the
spec text says `Typography.AllowedFontFamilies`, but the real `BrandManualSnapshot.Typography` is
`IReadOnlyList<string>` (font-family names) and `ColorPalette` is `IReadOnlyList<string>` (hex). The
design RESOLVES this: the allow-list IS `BrandManualSnapshot.Typography` (the list of family names);
the palette allow-list IS `BrandManualSnapshot.ColorPalette` (the list of hex strings). No new
snapshot shape is introduced.

**Choice.** One Application validator orchestrating four ordered steps, each returning a typed error:

```csharp
// Application/Abstractions/Social/ContentGeneration/IVisualGuardrailValidator.cs
public interface IVisualGuardrailValidator
{
    Result<VisualDesignOutput> Validate(
        VisualDesignOutput output,
        BrandManualSnapshot brand,
        VisualCompositionSpec spec);
}
```

Order (fail fast, cheapest first) and contracts:

| Step | Rule | On violation |
|---|---|---|
| **1. Schema (VD-04)** | Markup parses as a well-formed HTML document and conforms to the constrained template (Decision 8): single root, only allowed elements/classes, `data-slot` on text nodes. | `VisualGuardrailErrors.InvalidMarkup` |
| **2. Palette (VD-02)** | Every color token in the markup/inline style is a member of `brand.ColorPalette` (case-insensitive hex compare) OR `transparent`/`none`/`inherit`/`currentColor`. | `VisualGuardrailErrors.OffPalette(color)` |
| **3. Typography (VD-02)** | Every `font-family` referenced is a member of `brand.Typography` (the allow-list of family names). `Arial` not in the manual ⇒ violation. | `VisualGuardrailErrors.FontNotAllowed(family)` |
| **4. Overflow (VD-03)** | Declared text-block heights/lengths do not exceed slot constraints derived from `spec.Dimensions`. **B1a = STATIC heuristic** (declared height vs. slot height in the markup's data attributes); **B1b enriches** with real Chromium `boundingBox` measurement (Decision 7). | `VisualGuardrailErrors.TextOverflow(slot)` |

- All four run in the Application pipeline; first failure short-circuits to a failed `Result` and the
  renderer is NEVER called (asserted in VD-02/03/04 scenarios).
- Color/font extraction in B1a is a deterministic markup scan (regex/HTML parse over `style`,
  `color`, `background`, `font-family`) — NO browser, NO LLM. This is what makes every guardrail
  scenario testable with a stub designer.
- New typed errors live in `Domain` (`VisualGuardrailErrors`) so both Application and tests reference
  them without an Infrastructure dependency.

**Rejected — guardrails inside `OpusVisualDesigner` (Infrastructure).** Rejected: the validation is a
business rule (brand compliance), must run regardless of which designer/engine is used, and must be
unit-testable without a browser. It belongs in Application.

### Decision 7 — Overflow detection: static in B1a, real Chromium measurement in B1b (VD-03)

**Context.** The spike confirmed Chromium can MEASURE real layout via `boundingBox`. The spec's VD-03
must be satisfiable in B1a (engine-agnostic, stub designer) yet take advantage of real measurement in
B1b.

**Choice — two-layer overflow contract.**

- **B1a (static, in `IVisualGuardrailValidator`):** the constrained template (Decision 8) requires
  each text slot to carry `data-slot`, `data-max-lines` (or `data-max-height`) and the rendered font
  size. The validator computes an UPPER-BOUND estimate (char-count × average glyph advance ÷ slot
  width → line count vs. `data-max-lines`) and fails when the declared/estimated height exceeds the
  slot. Deterministic, browser-free, unit-testable. This SATISFIES VD-03's two scenarios.
- **B1b (precise, in `HtmlChromiumContentRenderer`):** after the page is laid out, the renderer reads
  each `[data-slot]` element's `boundingBox()` and compares it against the slot constraint. **On
  overflow the renderer returns a failed `Result`** (`RenderErrors.RenderFailed("overflow:{slot}")`)
  — deterministic CLAMP is NOT used in B1 (reprompt-with-feedback is a B-later enhancement; B1's
  contract is FAIL, consistent with "no broken asset"). The static B1a check catches most cases
  before render; the B1b measurement is the authoritative backstop.

**Why FAIL not clamp in B1:** clamping silently alters the designer's intent and can still look broken;
reprompt-with-feedback needs a designer round-trip loop that is out of B1's line budget. Failing with a
structured error keeps the contract honest and lets a later phase add the reprompt loop without
changing the guardrail's failure semantics.

### Decision 8 — Constrained HTML template schema (VD-04 markup contract) + brand-token CSS variables

**Context.** "Markup schema = HTML/CSS." VD-04 requires schema validation of the designer's output
BEFORE render. The output must also stay LAYERED for forward-compat (Decision 10). We need a concrete,
validatable structure — not free-form HTML.

**Choice — a constrained HTML document contract:**

```html
<!-- the designer MUST emit exactly this skeleton; classes/attributes are the validatable schema -->
<div class="composition" data-format="{formatKey}"
     style="width:{w}px;height:{h}px;
            --brand-primary:{c1};--brand-secondary:{c2};--brand-accent:{c3};
            --brand-text:{c4};--brand-bg:{c5}">
  <div class="layer-background" data-layer="background" style="...">...</div>   <!-- decoration / Tier-1 image -->
  <div class="layer-content" data-layer="content">
    <h1 class="slot" data-slot="headline" data-max-lines="3" style="...">...</h1>
    <p  class="slot" data-slot="subtitle" data-max-lines="2" style="...">...</p>
    <div class="slot" data-slot="cta" data-max-lines="1" style="...">...</div>
    <!-- logo as an <img class="slot" data-slot="logo"> layer -->
  </div>
</div>
```

Schema rules enforced by the validator (VD-04):

1. Parses as a single well-formed HTML fragment with ONE root `div.composition`.
2. Allowed elements only: `div, h1, h2, p, span, img, ul, li`. Disallowed tags ⇒ schema failure
   (blocks `<script>`, `<style>` injection, arbitrary tags).
3. Brand tokens are injected by the PIPELINE as CSS custom properties on the root from
   `BrandManualSnapshot.ColorPalette`/`Typography`; the designer references them via `var(--brand-*)`.
   This makes the palette guardrail (Decision 6 step 2) tractable and keeps colors on-brand by
   construction.
4. Every text node lives in a `.slot[data-slot]` element carrying `data-max-lines`. Two layers
   (`data-layer="background"` and `data-layer="content"`) are MANDATORY — this is the forward-compat
   seam (Decision 10).

- The validator implements (1)-(4) over an HTML parse (e.g. a lightweight HTML reader in Application;
  no browser). Failure ⇒ `VisualGuardrailErrors.InvalidMarkup` (VD-04 invalid-markup scenario).
- The Infrastructure renderer wraps this fragment in a minimal full HTML page with `@font-face`
  declarations (Decision 9) before handing it to Chromium.

**Rejected — accept arbitrary HTML and sanitize.** Rejected: unbounded surface, hard to validate,
hostile to the layered forward-compat requirement. A closed template is validatable AND structurable.

### Decision 9 — Font allow-list enforcement + `PromptVersionKey` (VD-02 fonts, VD-04 key)

**Context.** `CompositionTypography(HeadingFamily, HeadingSize, BodyFamily, BodySize)` is the spec's
typography DTO; `BrandManualSnapshot.Typography` is the `IReadOnlyList<string>` allow-list of family
names. VD-02 forbids Arial (or any non-manual font). The Chromium renderer must actually LOAD only
allowed fonts.

**Choice.**
- **Enforcement (Application, B1a):** Decision 6 step 3 rejects any `font-family` not in
  `brand.Typography`. When the manual lists no typography, the pipeline falls back to a SAFE bundled
  brand-neutral family that IS added to the effective allow-list (so generation never hard-fails for a
  manual with empty typography) — this fallback family is a constant, documented, and never `Arial`
  unless the manual explicitly lists it.
- **Injection (Infrastructure, B1b):** `OpusVisualDesigner`/`HtmlChromiumContentRenderer` declare ONLY
  allow-listed families via `@font-face` pointing at FONTS INSTALLED IN THE CONTAINER (Decision 11).
  Disallowed families are unreachable because no `@font-face`/web-font is emitted for them — a
  disallowed `font-family` simply has no font to bind, and the guardrail already rejected it upstream.
- **`PromptVersionKey` (B1a):** a deterministic constant per prompt-template version, e.g.
  `"visual-designer/v1"`. Defined as a `const` in the designer-prompt module so the SAME template
  always yields the SAME key (VD-04 determinism scenario). The stub designer in tests returns the
  same constant. The key is threaded through `VisualDesignOutput` → preserved into the render call /
  audit (it can be persisted alongside the existing `PromptVersion` on the format result).

### Decision 10 — Forward-compatibility for B3 (Canva-editable): layered, structured output (NOT flat-only)

**Context.** Editability model #570: a flat PNG is NOT editable in Canva. A future B3 will build an
editable PPTX (text boxes + background image) via Canva Design Import. B1 must NOT implement B3, but
its output MUST be structured so B3 can later extract text + a text-less background. This is an
EXPLICIT forward-compat requirement.

**Choice — two guarantees baked into B1's output, no PPTX/export code now:**

1. **Identifiable text (already in Decision 8):** every text element carries `data-slot` + its
   computed style. A future B3 can enumerate `[data-slot]` elements and read text + bounding box +
   font/size/color to build editable PPTX text boxes.
2. **Separable background layer:** the template MANDATES `data-layer="background"` and
   `data-layer="content"`. The B1b renderer's contract (documented, NOT a render mode shipped now)
   is structured so a FUTURE renderer call can hide `.layer-content` and rasterize ONLY
   `.layer-background` to produce a text-less background image — without re-authoring the design.

- B1 ships ONE render output: the full composited PNG (download + preview), exactly as today's
  pipeline expects. The layered structure is INTERNAL to the markup; no second asset, no PPTX, no
  Canva call in B1.
- This decision is recorded so B3 inherits a structured artifact instead of an opaque flat PNG.

**Rejected — emit a separate background PNG now.** Rejected: that is B3 work (doubles render cost,
adds a storage path, no consumer yet). B1 only GUARANTEES the structure exists.

### Decision 11 — `HtmlChromiumContentRenderer` (B1b): pooled browser, container fonts, 2x scale, queue

**Context.** Spike #569: Playwright .NET official C# API; Chromium ~300MB; `deviceScaleFactor:2`
yields crisp 2x at no code cost; Google-Fonts `@import` needs network at render. Production is Linux;
`Infrastructure.csproj` ships only `SkiaSharp.NativeAssets.Linux.NoDependencies` (fine). Browser-
per-render is too heavy.

**Choice — Infrastructure adapter design:**

- **`HtmlChromiumContentRenderer : IContentRenderer`** in `Infrastructure/Rendering/`. Implements the
  EXISTING port (`RenderAsync(VisualCompositionSpec, RenderOptions, CancellationToken)`). It does NOT
  call the designer — the Application pipeline calls the designer first, validates, and passes the
  validated HTML to the renderer (the HTML travels via the spec/options seam — Decision 12).
- **Browser lifecycle = pooled singleton.** A `ChromiumBrowserPool` (singleton, `IAsyncDisposable`)
  owns ONE launched `IBrowser`; each render acquires a fresh `IBrowserContext`/`IPage`, renders, and
  disposes the context (cheap) — never the browser. Launch is lazy + guarded by a `SemaphoreSlim`.
- **Concurrency/queue.** A bounded `SemaphoreSlim(maxConcurrentRenders)` (config, default e.g. 2-4)
  throttles simultaneous pages to bound memory; renders queue rather than spawn unbounded pages.
- **Timeouts.** Per-render navigation/`waitForLoadState` + screenshot timeout (config, default ~15s).
  Timeout ⇒ failed `Result` (`RenderErrors.RenderFailed("timeout")`), never a hang.
- **2x crispness.** `BrowserNewContextOptions { DeviceScaleFactor = 2, ViewportSize = {w,h} }`.
- **Fonts = installed in the container, NOT remote @import.** Fonts are baked into the image
  (Decision toward determinism + no render-time network). `@font-face` points at local font files;
  the brand allow-list maps to installed families. (Remote Google-Fonts `@import` is explicitly
  avoided in prod to remove a network dependency + nondeterminism.)
- **Config-selectable engine.** `RenderingOptions.Engine` (`chromium` | `skia`) in configuration.
  DI registers `HtmlChromiumContentRenderer` as `IContentRenderer` when `chromium` (default),
  `LocalSvgContentRenderer` when `skia` (fallback). The existing `LocalSvgContentRenderer` is RETAINED
  unchanged as the fallback adapter.
- **Packages (B1b csproj):** add `Microsoft.Playwright`. Browser binaries are installed at deploy via
  `playwright install chromium` (ops task — Decision 13). SkiaSharp packages stay.

### Decision 12 — How validated HTML reaches the renderer without breaking the `IContentRenderer` signature

**Context.** The port is `RenderAsync(VisualCompositionSpec, RenderOptions, CancellationToken)`. The
designer's validated HTML is a NEW artifact that must reach the Chromium renderer, but we should not
break the frozen port shape (SkiaSharp fallback and existing text/markdown paths use the same port).

**Choice.** Carry the validated composition markup as an ADDITIVE, optional field on the spec seam:

- Add `string? DesignerMarkup` (init-only, default `null`) and a `CompositionOutputKind.Html` arm to
  `VisualCompositionSpec`/`CompositionOutputKind` (mirrors how Fase A added `Markdown`). When the
  pipeline runs the designer stage, it produces a NEW spec `with { OutputKind = Html, DesignerMarkup =
  validatedHtml }` and passes THAT to `RenderAsync`.
- `HtmlChromiumContentRenderer` dispatches on `OutputKind == Html` → renders `DesignerMarkup` to PNG.
  `LocalSvgContentRenderer` keeps its existing `Raster`/`Text`/`Markdown` arms; if it is the selected
  fallback engine and receives `Html`, it either (a) renders a degraded raster from the spec slots
  (documented fallback) or (b) returns `RenderErrors.InvalidSpec` — chosen at B1b based on whether the
  fallback must support designer output. **Default: SkiaSharp fallback renders the legacy slot-based
  raster (ignores `DesignerMarkup`)**, preserving today's behavior so the fallback never hard-fails.
- This keeps the port signature FROZEN (additive enum + additive nullable field), consistent with the
  Fase-A precedent, and avoids a parallel renderer interface.

**Rejected — a second port `IHtmlContentRenderer`.** Rejected: forks the pipeline's render call,
duplicates storage wiring, and breaks the single-`IContentRenderer` injection in
`ContentGenerationJobProcessor`. The additive-spec approach matches the existing extension pattern.

### Decision 13 — Pipeline wiring: insert designer + guardrails between compose and render

**Context.** `ContentGenerationJobProcessor` step 4 does `ComposeManyAsync` → `RenderAsync` per spec.
The designer stage and guardrails slot BETWEEN these two calls. The pipeline already holds `brand`
(BrandSnapshot) and each `spec`.

**Choice.** New collaborators injected into `ContentGenerationJobProcessor` (both Application
abstractions): `IVisualDesigner _designer` and `IVisualGuardrailValidator _guardrails`. Per spec, for
RASTER formats that should be DESIGNED (flyer, carousel slides):

```
for each spec in specs:
    if spec is a designed raster format:
        selection = BackgroundSelector.Select(brand.Brand.BackgroundTemplates, formatKey)
        designed  = await _designer.ComposeAsync(spec, brand.Brand, selection, ct)   // Opus (or stub)
        if designed.IsFailure -> record render failure, continue                      // no asset
        guarded = _guardrails.Validate(designed.Value, brand.Brand, spec)             // VD-02/03/04
        if guarded.IsFailure -> record render failure, continue                       // renderer NOT called
        renderSpec = spec with { OutputKind = Html, DesignerMarkup = guarded.Value.Markup }
        rendered = await _renderer.RenderAsync(renderSpec, opts, ct)
    else:
        rendered = await _renderer.RenderAsync(spec, opts, ct)   // text/markdown unchanged
```

- Text/markdown formats (hook, caption, cta, reel-script, content-idea) BYPASS the designer — they
  keep their existing `Text`/`Markdown` path untouched.
- Both designer-failure and guardrail-failure produce the SAME outcome as a render failure today
  (`PersistRenderFailureAsync`, no asset row, format marked failed) — the renderer is NOT called,
  satisfying VD-02/03/04's "renderer not called" assertions.
- The injected designer/guardrail validator are NEW constructor params; to keep the processor's many
  existing call sites compiling, add them as required params with test doubles supplied in tests
  (matching the existing pattern of explicit constructor wiring).
- DI: register `IVisualDesigner → OpusVisualDesigner` and `IVisualGuardrailValidator →
  VisualGuardrailValidator` in `AddRenderingAndPackaging` (Infrastructure). B1a can register a
  no-op/stub designer if B1b is not yet merged (chained-PR safety), but the REAL Opus designer ships
  in B1b.

## Sub-Split + Ordering

### B1a — Domain/Application (engine-agnostic, frozen contract). No deps.

Delivers: `BackgroundTier`, extended `BackgroundEntry` + entry-level guard, `ClientBrandManual`
forced-default invariant, `BackgroundSelection` + `BackgroundSelector`, extended
`BrandBackgroundTemplateSnapshot` + loader mapping, `IVisualDesigner` port, `VisualDesignOutput`,
`IVisualGuardrailValidator` + `VisualGuardrailValidator` (palette/typography/overflow-static/schema),
`VisualGuardrailErrors`, `CompositionOutputKind.Html` + `VisualCompositionSpec.DesignerMarkup`, and
the pipeline wiring against a STUBBED designer. Fully unit-tested, no browser, no LLM.

### B1b — Infrastructure. DEPENDS on B1a frozen contract.

Delivers: `OpusVisualDesigner : IVisualDesigner` (real Opus call, multimodal Tier-2),
`HtmlChromiumContentRenderer : IContentRenderer` (Playwright pooled browser, 2x, queue, timeouts,
container fonts, `boundingBox` overflow backstop), config-selectable engine + DI, `Microsoft.Playwright`
package. SkiaSharp fallback retained.

**Contract freeze:** B1b must NOT change any B1a type. If B1b discovers a contract gap, it returns to
B1a as an explicit amendment (the proposal flags contract-freeze churn as a risk).

## File Changes

### B1a (Domain / Application / Persistence-mapping)

| File | Action | Description |
|---|---|---|
| `Domain/Social/ClientBrandManuals/BackgroundTier.cs` | Create | enum `{ Creative=0, Reference=1, ForcedDefault=2 }` |
| `Domain/Social/ClientBrandManuals/BackgroundEntry.cs` | Modify | +`Tier`/`FormatKey`/`IsFormatDefault` (defaulted); `Create` validates forced-default needs FormatKey + ForcedDefault tier; `Restore` overload |
| `Domain/Social/ClientBrandManuals/ClientBrandManual.cs` | Modify | `ValidateForcedDefaultUniqueness` in `Create`+`Update`; ≤1 forced default per FormatKey |
| `Domain/Social/ClientBrandManuals/ClientBrandManualErrors.cs` | Modify | `DuplicateForcedDefault`, `ForcedDefaultWithoutFormat` errors |
| `Domain/Social/ContentGeneration/BrandBackgroundTemplateSnapshot.cs` | Modify | mirror `Tier`/`FormatKey`/`IsFormatDefault` (defaulted) |
| `Domain/Social/ContentGeneration/BackgroundSelection.cs` | Create | closed record union: `ForcedDefault`/`References`/`Creative` |
| `Domain/Social/ContentGeneration/BackgroundSelector.cs` | Create | static tier-priority `Select(templates, formatKey)` |
| `Domain/Social/ContentGeneration/VisualDesignOutput.cs` | Create | `Markup`/`PromptVersionKey`/`IsValid` + `WithValidated()` |
| `Domain/Social/ContentGeneration/VisualGuardrailErrors.cs` | Create | `InvalidMarkup`/`OffPalette`/`FontNotAllowed`/`TextOverflow` |
| `Domain/Social/ContentGeneration/VisualCompositionSpec.cs` | Modify | +`CompositionOutputKind.Html`; +`string? DesignerMarkup { get; init; }` |
| `Application/Abstractions/Social/ContentGeneration/IVisualDesigner.cs` | Create | port: `ComposeAsync(spec, brand, selection, ct)` |
| `Application/Abstractions/Social/ContentGeneration/IVisualGuardrailValidator.cs` | Create | `Validate(output, brand, spec)` |
| `Application/Features/Social/ContentGeneration/Visual/VisualGuardrailValidator.cs` | Create | schema→palette→typography→overflow(static) |
| `Application/Features/Social/ContentGeneration/Visual/Composers/FlyerVisualComposer.cs` | Modify | use `BackgroundSelector.Select` (tier-aware) instead of first-match |
| `Application/Features/Social/ContentGeneration/Visual/Composers/CarouselVisualComposer.cs` | Modify | tier-aware background selection seam |
| `Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` | Modify | inject `IVisualDesigner`+`IVisualGuardrailValidator`; designer→guardrail→render for designed formats |
| `Infrastructure/Social/ContentGeneration/EfClientBrandSnapshotLoader.cs` | Modify | map `Tier`/`FormatKey`/`IsFormatDefault` into snapshot |
| `openspec/specs/content-generation/spec.md` | Modify | apply BG-01..VD-04 delta |

### B1b (Infrastructure)

| File | Action | Description |
|---|---|---|
| `Infrastructure/Rendering/HtmlChromiumContentRenderer.cs` | Create | Playwright `IContentRenderer`; renders `Html` output kind |
| `Infrastructure/Rendering/ChromiumBrowserPool.cs` | Create | singleton browser, per-render context, semaphore, timeouts |
| `Infrastructure/Social/ContentGeneration/OpusVisualDesigner.cs` | Create | real `IVisualDesigner`; constrained HTML; Tier-2 multimodal; `PromptVersionKey="visual-designer/v1"` |
| `Infrastructure/Rendering/RenderingOptions.cs` | Create | `Engine` (chromium/skia), concurrency, timeout, scale config |
| `Infrastructure/DependencyInjection/DependencyInjection.cs` | Modify | config-select `IContentRenderer`; register designer + guardrail validator |
| `Infrastructure/SocialStudio.Infrastructure.csproj` | Modify | add `Microsoft.Playwright`; keep SkiaSharp |
| Deploy (Dockerfile / ops) | Modify | `playwright install chromium` + font package in image |

## Data Flow

```
COMPOSE → DESIGN → GUARDRAIL → RENDER (designed raster formats: flyer, carousel slides)

ContentGenerationJobProcessor (step 4, per spec):
  specs = composer.ComposeManyAsync(formatKey, payload, brand)          [unchanged]
  for each spec:
    selection = BackgroundSelector.Select(brand.Brand.BackgroundTemplates, formatKey)
        Tier-1 ForcedDefault(url)  -> designer composes ON that image, no bg generation
        Tier-2 References(entries) -> designer gets entry URLs as multimodal style guides
        Tier-3 Creative            -> designer generates brand-bound background freely
    designed = IVisualDesigner.ComposeAsync(spec, brand.Brand, selection, ct)   [Opus | stub]
        fail -> PersistRenderFailure, no asset (renderer NOT called)
    guarded = IVisualGuardrailValidator.Validate(designed, brand.Brand, spec)
        schema | off-palette | font | overflow -> fail -> PersistRenderFailure (renderer NOT called)
    renderSpec = spec with { OutputKind = Html, DesignerMarkup = guarded.Markup }
    rendered = IContentRenderer.RenderAsync(renderSpec, opts, ct)
        chromium: HTML -> Chromium (2x, container fonts) -> boundingBox overflow backstop -> PNG
        skia fallback: ignores DesignerMarkup, renders legacy slot raster
  (text/markdown formats bypass designer/guardrails entirely — unchanged)

PERSIST: PromptVersionKey threaded into the format result alongside existing PromptVersion.
FORWARD-COMPAT (B3, NOT shipped): markup is layered (data-layer background/content) and
text is slotted (data-slot) so a future PPTX/Canva-import path can extract text + bg.
```

## Interfaces / Contracts (frozen for B1b)

```csharp
// Domain
public enum BackgroundTier { Creative = 0, Reference = 1, ForcedDefault = 2 }

public sealed record BackgroundEntry(
    string Name, string Url, string? UseCase,
    BackgroundTier Tier = BackgroundTier.Creative,
    string? FormatKey = null, bool IsFormatDefault = false);

public sealed record BrandBackgroundTemplateSnapshot(
    string Name, string Url, string? UseCase,
    BackgroundTier Tier = BackgroundTier.Creative,
    string? FormatKey = null, bool IsFormatDefault = false);

public abstract record BackgroundSelection
{
    public sealed record ForcedDefault(string Url, string Name) : BackgroundSelection;
    public sealed record References(IReadOnlyList<BrandBackgroundTemplateSnapshot> Entries) : BackgroundSelection;
    public sealed record Creative : BackgroundSelection;
}

public static class BackgroundSelector
{
    public static BackgroundSelection Select(
        IReadOnlyList<BrandBackgroundTemplateSnapshot> templates, string formatKey);
}

public sealed record VisualDesignOutput(string Markup, string PromptVersionKey, bool IsValid);

public enum CompositionOutputKind { Raster, Text, Markdown, Html }   // +Html
// VisualCompositionSpec gains:  public string? DesignerMarkup { get; init; } = null;

// Application
public interface IVisualDesigner
{
    Task<Result<VisualDesignOutput>> ComposeAsync(
        VisualCompositionSpec contentPayload, BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection, CancellationToken cancellationToken);
}

public interface IVisualGuardrailValidator
{
    Result<VisualDesignOutput> Validate(
        VisualDesignOutput output, BrandManualSnapshot brand, VisualCompositionSpec spec);
}
```

## Testing Strategy (Strict TDD — `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj`)

Test-first, Domain → Application → (Infra in B1b). Stubbed `IVisualDesigner` everywhere in B1a — no
real LLM. Order maps to spec scenarios BG-01/02/03, VD-01/02/03/04.

**B1a — Domain (BG-01, BG-02):**
1. `BackgroundEntryTests.Tier_DefaultsToCreative_WhenNotSpecified` (BG-01 legacy).
2. `BackgroundEntryTests.Create_ForcedDefault_RequiresFormatKey_And_ForcedDefaultTier` (BG-02 null-format).
3. `BackgroundEntry` JSON round-trip: legacy JSON (no `Tier`) → `Creative`, no exception (BG-01).
4. `BackgroundEntry` full round-trip: `ForcedDefault`/`"flyer"`/`true` persists+reloads (BG-01).
5. `ClientBrandManualTests.SecondForcedDefault_SameFormat_Fails` (BG-02).
6. `ClientBrandManualTests.ForcedDefaults_DifferentFormats_Succeed` (BG-02).
7. `ClientBrandManualTests.FirstForcedDefault_Succeeds` (BG-02).

**B1a — Domain (BG-01 snapshot, BG-03):**
8. `BrandSnapshotTests.BackgroundTemplateSnapshot_MirrorsTierFields` (BG-01 snapshot).
9. `BackgroundSelectorTests.Tier1_ForcedDefault_ReturnedWithoutGeneration` (BG-03).
10. `BackgroundSelectorTests.Tier2_ReferencesReturned_WhenNoForcedDefault` (BG-03).
11. `BackgroundSelectorTests.Tier3_Creative_WhenNoStructuredEntries` (BG-03).
12. `BackgroundSelectorTests.ForcedDefault_DifferentFormat_DoesNotMatch` (BG-03).

**B1a — Application (VD-01..VD-04, stubbed designer):**
13. `IVisualDesignerContractTests.Port_HasNoInfrastructureTypes` (reflection over Application asm) (VD-01).
14. `IVisualDesignerContractTests.ComposeAsync_Stub_ReturnsNonEmptyPromptVersionKey` (VD-01).
15. `VisualGuardrailValidatorTests.OffPaletteColor_Rejected_RendererNotCalled` (VD-02).
16. `VisualGuardrailValidatorTests.NonManualFont_Arial_Rejected` (VD-02).
17. `VisualGuardrailValidatorTests.OnPaletteAndFont_Passes` (VD-02).
18. `VisualGuardrailValidatorTests.TextWithinBounds_Passes` (VD-03 static).
19. `VisualGuardrailValidatorTests.TextOverflow_Rejected_BeforeRender` (VD-03 static).
20. `VisualGuardrailValidatorTests.InvalidMarkup_Rejected` (VD-04 schema).
21. `VisualGuardrailValidatorTests.ValidMarkup_PreservesPromptVersionKey` (VD-04).
22. `VisualGuardrailValidatorTests.StubDesigner_NoRealLlm_KeyNonEmpty` (VD-04 stub).
23. `ContentGenerationJobProcessor` pipeline: guardrail-fail ⇒ renderer NOT called, format marked failed.
24. `ContentGenerationJobProcessor` pipeline: Tier-1 forced default ⇒ designer receives `ForcedDefault.Url`, no generation flag.

**B1b — Infrastructure (engine, real designer):**
25. `HtmlChromiumContentRendererTests`: `Html` spec → PNG bytes, `image/png`, 2x dimensions (contract success).
26. `HtmlChromiumContentRendererTests`: timeout / launch failure → failed `Result` (contract failure).
27. `HtmlChromiumContentRendererTests`: `boundingBox` overflow backstop → failed `Result` (VD-03 precise).
28. `ChromiumBrowserPoolTests`: browser reused across renders; concurrency bounded by semaphore.
29. Engine config selection: `chromium` → Chromium adapter; `skia` → `LocalSvgContentRenderer` retained.

## Migration / Rollout

- **B1a:** additive only. No DB migration — `BackgroundTemplates` JSON gains optional fields; legacy
  entries deserialize as `Creative` (enum default 0). `VisualCompositionSpec` enum/field additions are
  backward-compatible (default `Raster`/`null`). Behavior change is gated by whether a designer is
  registered: with a stub/no-op designer the pipeline behaves as today for raster formats.
- **B1b:** introduces Chromium. Deploy task: `playwright install chromium` + container font package.
  Engine is config-selectable — ship `chromium` as default, keep `skia` fallback for rollback. SkiaSharp
  packages remain so a config flip reverts rendering with no redeploy of code.
- **Ops flag (deploy task, NOT code):** `Microsoft.Playwright` NuGet + browser-binary install + fonts
  must be present in the Linux image before `chromium` engine is enabled in production.

## Open Questions (resolve at apply)

- [ ] Confirm whether B1a registers a NO-OP stub `IVisualDesigner` in production DI (so B1a can ship
      before B1b) or whether B1a + B1b ship together — affects whether `ContentGenerationJobProcessor`
      runs the designer path before the real Opus designer exists.
- [ ] Confirm the bundled brand-neutral fallback font family name (Decision 9) and that it is installed
      in the container (B1b) — it must NEVER be Arial unless the manual lists it.
- [ ] Confirm `maxConcurrentRenders` and per-render timeout defaults for the Chromium pool against the
      target container's memory budget (B1b).
- [ ] Confirm which formats are "designed raster" (flyer + carousel slides assumed); verify no other
      raster format must route through the designer in B1.
