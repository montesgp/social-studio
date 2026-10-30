# Proposal: Single-Analyzer Visual Quality (Fase B / B1) — Opus-as-Designer Composition

## Change ID
`single-analyzer-visual-quality`

## Summary
Make the single-analyzer's generated carousels and flyers look **professional** instead of "made in Paint". Today's output is mechanically drawn by a raster renderer with no design-intelligence stage. This change introduces an **Opus-as-designer** composition stage: Opus 4.8 emits a RICH, brand-bound composition (HTML/CSS or SVG) that a renderer materializes into the final asset, with guardrails that enforce the client brand manual and prevent malformed output.

This is **Fase B, sub-phase B1** — item **#6 only** (visual quality). It is delivered as **two chained slices**:
- **B1a** — Domain/Application: the 3-tier background model, tier-aware selection, the Opus-designer composition spec, and guardrails (LLM stubbed/mocked in tests).
- **B1b** — Infrastructure: the renderer adapter behind the existing `IContentRenderer` port, with richer typography/layout. The rendering **ENGINE** (headless Chromium via Playwright .NET vs. an upgraded SkiaSharp renderer) is **DEFERRED to the design phase**, which must run a real comparison spike with visual evidence before choosing.

Canva connector / "Export to Canva" / native-editable formats (the review items #1, #4, #5) are **explicitly OUT of scope** here; they are reserved for sub-phases **B2/B3**.

## Problem
The single-analyzer produces carousel slides and flyers that look amateur ("today looks like Paint"). The verified root cause:

- `SocialStudio.Infrastructure/Rendering/LocalSvgContentRenderer.cs` is a **raster SkiaSharp renderer** (despite the "Svg" name it draws primitives directly), with **hardcoded mechanical layout** (seeded gradients/circles, fixed panel rectangles, naive word-wrap) and **Arial 28/16 typography** from `CompositionDefaults`.
- There is **NO design-intelligence stage** anywhere in the pipeline. Composers under `ContentGeneration/Visual/Composers/*` only map payload → slots and emit a POOR `VisualCompositionSpec`; the renderer then mechanically rasterizes that poor spec. The quality ceiling is set by hardcoded heuristics, not by any designer.

When a human asks Opus directly for a flyer, the result looks great because **Opus itself is acting as the designer**. The fix is to put Opus into the pipeline as the design brain, emitting a rich, varied, brand-bound composition that the renderer faithfully materializes.

Separately, the brand manual cannot express the user's three intended background behaviors. `BackgroundEntry(Name, Url, UseCase?)` has no per-format default flag and no tier classification, so the composers cannot distinguish "always use THIS image" from "use this as a style reference" from "generate freely".

## Goals
- Generated carousels/flyers look **professional and modern**, brand-bound to the client manual, and **always varied** in the creative tier.
- Introduce an **Opus-as-designer composition stage** that emits a rich composition, sitting in front of the renderer.
- Make background resolution **3-tier aware** (Forced default → compose on that image; Reference → style guide; Creative → pure generation).
- Enforce brand binding: palette, typography, and voice from the manual, with guardrails against text overflow and malformed design output (schema/spec validation).
- Keep the rendering **engine decision deferred to design** (Playwright/Chromium vs. upgraded SkiaSharp), backed by a real visual comparison spike — this proposal does NOT fix the engine.
- Stay within strict hexagonal boundaries (Domain ← Application ← Infrastructure/Api); provider/persistence types never leak into Domain/Application. Pass all MUST gates.

## Out of Scope
- **Canva connector (#1).** No OAuth seeder changes, no token store, no `IIntegrationResolver` Canva path. → sub-phase **B2**.
- **Native-editable formats / "Export to Canva" (#4, #5).** No OpenXML PPTX generator, no Canva Design Import, no Export button. → sub-phase **B3**.
- **The rendering ENGINE choice.** This proposal requires that B1b expose a renderer adapter behind `IContentRenderer`, but it does NOT pick Chromium vs. SkiaSharp. That decision is made in the **design phase** via a comparison spike with visual evidence.
- **Analysis pipeline / scrape contracts / Mundo 1 vs Mundo 2.** Untouched. This change is confined to visual composition + rendering.

## Scope Boundaries
- **3-tier background model is IN scope for B1** because it shapes WHAT the designer produces (forced image vs. reference vs. free generation). It is delivered in B1a.
- The Opus-designer stage is an **Application port** (an `IVisualDesigner`-style abstraction); its LLM implementation lives in Infrastructure and is **stubbed/mocked** in B1a tests, so B1a is fully testable without a live model.
- The renderer adapter (B1b) plugs into the **existing `IContentRenderer` port** (which already documents "Future adapters: Canva, Stability, Imagen") — no new port shape is invented; we add an adapter and select it via config.
- Persistence: extending `BackgroundEntry` touches the `ClientBrandManualData` JSON column; the migration is **additive and backward-compatible** (legacy entries default to the Creative tier with no format binding).

## Approach

### 1. 3-tier background model (B1a — Domain/Application)
Extend the `BackgroundEntry` domain record:
- Add `Tier` enum: `ForcedDefault | Reference | Creative`.
- Add optional `FormatKey` (which format a forced default binds to, e.g. carousel vs. flyer).
- Add `IsFormatDefault` flag, with the invariant **≤ 1 default per `FormatKey`** enforced at the aggregate level.
- Mirror these on the read/snapshot side (`BrandBackgroundTemplateSnapshot` / DTOs) and on `BackgroundEntryDto`.

Make `SelectBackgroundTemplate` (in the Flyer/Carousel composers) **tier-aware**:
1. **Tier-1 Forced** default for this `FormatKey` → compose on that exact image, **no generation**.
2. **Tier-2 Reference** → pass the image to the Opus designer as a multimodal **style guide** (Opus "sees" it and composes in its style).
3. **Tier-3 Creative** → pure creative generation, brand-bound, **always varied**.

### 2. Opus-as-designer composition stage (B1a — Application)
Introduce an Application-layer designer port (e.g. `IVisualDesigner`) that takes the resolved brand manual + payload + tier context and returns a **rich composition** (HTML/CSS or SVG markup) bound to the brand. The composer pipeline becomes: `payload → tier resolution → IVisualDesigner.ComposeAsync → rich composition → IContentRenderer.RenderAsync`. In B1a the designer is exercised with a **stubbed/mocked LLM** so behavior and guardrails are testable deterministically.

### 3. Renderer-adapter seam (B1b — Infrastructure)
Add a new `IContentRenderer` adapter that materializes the rich composition emitted by the designer, with richer typography/layout than the current Arial-28/16 heuristics. The concrete **engine** (Playwright .NET headless Chromium for full-CSS fidelity vs. an upgraded SkiaSharp/SVG renderer for determinism and low ops weight) is chosen in **design** after a spike that renders the SAME composition through both candidates and compares quality, determinism, and ops cost on visual evidence. The current `LocalSvgContentRenderer` is retained as a deterministic fallback selectable via config.

### 4. Guardrails (B1a — Application)
The designer output must be constrained before it reaches the renderer:
- **Brand palette enforcement** — only the manual's colors are allowed; off-palette colors are rejected/remapped.
- **Typography enforcement** — only the manual's fonts; no Arial fallback unless the manual specifies it.
- **Text-overflow prevention** — measure/fit copy so it does not clip or overrun.
- **Schema/spec validation** — the emitted markup is validated against a known schema/spec before rendering; invalid output fails the `Result` instead of producing a broken asset.

## Sub-Split (Chained Slices)
`delivery_strategy = ask-on-risk`. The exploration flags **400-line risk = HIGH** for B1, so it is split into two dependent slices.

| Slice | Layer | Contents | Depends on |
|---|---|---|---|
| **B1a** | Domain + Application | 3-tier `BackgroundEntry` extension + tier-aware selection + `IVisualDesigner` composition port + guardrails (LLM stubbed/mocked in tests) | — |
| **B1b** | Infrastructure | Renderer adapter behind `IContentRenderer` (engine chosen in design's spike) + richer typography/layout + real `IVisualDesigner` LLM implementation | **B1a's spec contract** |

**Dependency**: B1b consumes the composition contract and designer/renderer ports defined and frozen in B1a. B1a must merge (or at least stabilize its public contract) before B1b begins.

## Acceptance Criteria

### B1a (Domain + Application)
- [x] `BackgroundEntry` exposes `Tier { ForcedDefault, Reference, Creative }`, optional `FormatKey`, and `IsFormatDefault`.
- [x] The aggregate rejects a second `IsFormatDefault = true` for the same `FormatKey` (invariant: ≤ 1 default per format).
- [x] Legacy `BackgroundEntry` data restores cleanly, defaulting to `Creative` tier with no format binding (backward-compatible migration).
- [x] `SelectBackgroundTemplate` returns the Tier-1 forced image when one exists for the format and performs NO generation in that case.
- [x] Tier-2 reference images are surfaced to the designer as style input; Tier-3 triggers creative generation.
- [x] An `IVisualDesigner` port exists in Application; the composer pipeline calls it before the renderer.
- [x] Guardrails: off-palette colors and non-manual fonts are rejected/remapped; overflowing copy is detected; invalid designer markup yields a failed `Result` (not a broken asset) — all verified with a **stubbed/mocked LLM**.
- [x] All B1a tests pass under strict TDD (`dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj`); no provider/persistence types leak into Domain/Application.

### B1b (Infrastructure)
- [x] A new `IContentRenderer` adapter renders the rich composition emitted by the designer, honoring brand typography (NOT hardcoded Arial 28/16).
- [x] The adapter is selectable via config; `LocalSvgContentRenderer` remains as a deterministic fallback.
- [x] The real `IVisualDesigner` LLM implementation lives in Infrastructure and satisfies the B1a contract.
- [x] Renderer adapter has an `IContentRenderer` contract test (success/failure `Result`).
- [x] **Design-spike acceptance (engine choice)**: the design phase rendered the same composition through both candidate engines (Playwright/Chromium and upgraded SkiaSharp) and recorded a decision with visual evidence and ops-cost notes; B1b implements the chosen engine. This criterion is satisfied by the design artifact, not by B1 code alone.

## Affected Capabilities / Specs

| Spec (`openspec/specs/`) | Effect | Why |
|---|---|---|
| `content-generation/spec.md` | Touched (delta) | The Opus-designer composition stage, 3-tier background resolution, and guardrails all belong to the content-generation / visual-composition capability. Add the new acceptance criteria here. |
| `integrations/spec.md` | Untouched | Canva (#1/#4/#5) is out of scope; no integration behavior changes in B1. |

## Docs-Sync Note
- `openspec/specs/content-generation/spec.md` — record the 3-tier background contract, the `IVisualDesigner` composition stage, and the guardrail acceptance criteria.
- `docs/` — note the new renderer-adapter seam and the deferred engine decision; the design artifact records the spike result.
- No PRD restructure — this is a quality/fidelity improvement of the existing content-generation/packaging output.

## PRD Traceability
- **PRD `docs/PRD.md` — content-generation capability** (downloadable, professional content package output): item #6 directly improves the visual quality of that output so carousels/flyers are presentation-ready.
- **Capability**: `content-generation` (visual composition + rendering).
- **SDD `docs/SDD.md` — Content Generation bounded context**: the designer stage and renderer adapter stay within the existing `IContentRenderer` / `VisualCompositionSpec` seam; the 3-tier model is an additive `ClientBrandManual` aggregate change.

## Risks
- **LLM determinism & guardrails.** An LLM emitting design directly maximizes quality and variety but is non-deterministic. Tests must run against a stubbed/mocked designer (B1a); production needs strict guardrails (palette/font enforcement, overflow prevention, schema validation) or output may go off-brand or break. This is the central B1 risk.
- **Renderer ops weight (if Chromium).** If the design spike selects Playwright/headless Chromium, deployment gains ~300MB plus font installation, sandbox flags, and rendering-queue concerns. The fallback (upgraded SkiaSharp) avoids this but has a lower fidelity ceiling. The engine decision is deliberately deferred to design WITH a spike so this tradeoff is made on evidence, not assertion.
- **3-tier migration of `ClientBrandManualData` JSON.** Extending `BackgroundEntry` changes the persisted JSON shape. The migration must be additive and backward-compatible (legacy entries → Creative tier, no format binding); a non-defensive deserializer could break existing brand manuals.
- **Prompt/token cost per generation.** Opus-as-designer is "free" only relative to Canva — every carousel/flyer generation consumes Claude tokens (and multimodal tokens for Tier-2 reference images). Confirmed acceptable in principle (obs #566 open decision e), but cost-per-generation should be monitored.
- **Contract freeze between slices.** B1b depends on B1a's composition/designer/renderer contract. If that contract is not stabilized before B1b starts, the two slices will churn against each other.

## Review Workload Note
Estimated **400-line risk = HIGH** for B1 as a whole (per exploration), hence the **two chained slices** B1a → B1b. Each slice targets ≤ 400 changed lines. `delivery_strategy = ask-on-risk` → confirm chained PRs at tasks/apply time.

## Traceability
- Exploration (Fase B): engram `sdd/single-analyzer-fase-b/explore` (obs #565); critical findings (obs #566).
- Agreed direction: 3-tier background model (obs #553), visual-quality decision criterion (obs #554).
- Capability spec: `openspec/specs/content-generation/spec.md`.
- Sibling sub-phases (OUT of scope here): B2 (Canva connector #1), B3 (editable export + Export-to-Canva #4/#5).
