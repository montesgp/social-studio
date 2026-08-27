# Proposal: Content Layout and Image Fidelity

## Change ID
`content-layout-and-image-fidelity`

## Status
Proposal — revision 2. Builds on `exploration.md` (verified codebase map with file/line citations,
5 items). Fourth change in the ClientAesthetic line, after `client-aesthetic-fidelity-fixes`
(archived 2026-08-24), `client-aesthetic-generation-hardening` (archived 2026-08-25) and
`client-aesthetic-visual-sophistication` (archived 2026-08-26).

**All three open decisions are now settled by the owner** (see `## Settled decisions`). No decision
blocks the spec or design phases. The layout track is unblocked.

## Settled decisions

| # | Decision | Owner ruling | Consequence |
|---|---|---|---|
| D1 | Item 3's fix mechanism | **Two-pass measure-then-render (mechanism (a))**. CSS document flow is rejected. | Absolute positioning is **kept**. Content Studio's free-drag editing model and `PptxExportAdapter` stay unchanged. The render-latency cost is accepted. The convergence problem must be **designed for explicitly** with a bounded iteration cap — not hand-waved. |
| D2 | Item 2's scope | **Investigation-only feasibility report.** No OCR production code in this change. | Follows the `IBrandMarkAssetExtractor` / ComfyUI-detailer-node investigate-then-gate precedent. The OCR track collapses from ~1,120 lines across two units to ~220 in one report unit. Real implementation is a gated follow-up carrying its own ADR. |
| D3 | Item 5's v1 scope | **Full alignment-guide scope.** Snap and guide against canvas centre **and** every sibling slot's edges and centres. | The fuller, more Figma-like v1. Re-forecast puts it **over the 800-line budget**, so it splits into two work units per this project's established WU-Na/WU-Nb pattern. |

## Intent

**Business problem.** The two prior changes worked. A real generation for Lucia Fernandez (a carousel
about *ronquidos*/snoring) confirmed the pipeline now renders the client's real signature and the
client's real measured typography — the exact defects those changes shipped to close. What that same
generation exposed is that the fidelity gains landed on a rendering substrate that was never built to
receive them, plus two image-level defects that make an otherwise on-brand slide unpublishable.

The owner's bar has not moved: a generated piece must be publishable as-is. A slide whose headline
overlaps its subtitle is not "mostly right"; it is a manual redo, which is the exact cost the whole
Content Generation capability exists to remove. Worse, the overlap is *caused by* the prior change
working correctly — a 149px headline derived from the client's real `SizeRatio` colliding with a
subtitle pinned at a hardcoded `top:575px`. Shipping more fidelity onto this substrate makes the
substrate fail more often, not less.

**Current-state gap (verified, not assumed).**

| # | Gap | Nature |
|---|---|---|
| 1 | **Image content is off-topic** | `ImagePromptWriter`'s cold-start branch (the common case, fires whenever `ConceptualMethod` is null/`Unknown`) instructs the writer-model on *style and mood only*. `SlideCopySummary` is passed as a raw JSON field the instruction text never tells the model to use. A snoring slide gets "warm cream-toned clinical still life". |
| 2 | **Images carry baked-in, non-editable text** | The only defense is `ComfyUiImageModelClient.DefaultNegativePrompt` — a soft steering string. `DegenerateImageGuardrailValidator`'s own doc comment states it does **not** implement the text/logo detection its port promises. There is no OCR/ML pass anywhere in the codebase. |
| 3 | **Slide text overlaps** | Every text slot's `top` in `ContentGenerationJobProcessor` is a fixed pixel literal. **No slot's position anywhere in that file is ever computed from another slot's measured height.** `VisualGuardrailValidator.ValidateOverflow` is single-slot char-count only — and is **never invoked on the template-backed path at all** (`_guardrails.Validate` runs once, against AI-designer markup, *before* the enforced-background priority resolution picks template markup over it). |
| 4 | **Content Studio boxes look far bigger than the text** | **Not a separate bug.** `SlotGeometryEntry` is `getBoundingClientRect()` on the same CSS box, so it returns the fixed `min-height` literal (260px, 310px), not the glyph ink. Fixing gap 3 fixes this. |
| 5 | **No alignment guides in Content Studio** | Confirmed absent — no snap/guide mechanism exists. New feature, not a defect. Independent of 1–4. |

**Outcome.** A generated slide's text block reflows so that a legitimately-large headline pushes the
subtitle down instead of colliding with it; its background image depicts the slide's actual subject;
its Content Studio editing boxes track the real text; the codebase carries a decision-grade answer on
whether baked-in-text detection is worth building; and the operator can align elements against real
guides — canvas centre and neighbouring slots — instead of by eye.

## Scope

### In Scope

1. **Topical fidelity in the cold-start image prompt (item 1).** The cold-start branch of
   `ImagePromptWriter.BuildInput` gains an explicit topical-fidelity instruction, mirroring the
   wording the method-gated branch already carries ("the concept MUST depict the slide message"),
   so `SlideCopySummary` becomes an instructed input rather than an unread data field. Style,
   palette and motif guidance is retained, not replaced.
2. **Cross-slot overlap detection (item 3).** `VisualGuardrailValidator` gains a cross-slot geometry
   check that can detect two text slots whose vertical extents intersect — today no such check exists
   at any level.
3. **Guardrail coverage of the template-backed path (item 3).** `BuildTemplateBackedMarkup`'s output
   is submitted to guardrail validation. This is the enforced-background (ComfyUI-image) path that
   currently bypasses validation entirely.
4. **Text-block reflow via bounded two-pass measure-then-render (item 3, D1).** Stacked text slots on
   the template-backed carousel and story builders stop using hardcoded absolute `top` literals and
   instead derive position from the preceding slot's **Chromium-measured** extent, read through the
   existing `SlotGeometryScript` mechanism. Markup stays absolutely positioned. The recompute loop
   carries an explicit, tested **convergence cap** (see `## Approach`).
5. **Copy-length-aware typography fit (item 3).** `CompositionDefaults` gains a bounded auto-fit so a
   legitimate `SizeRatio` applied to an unusually long copy string is reduced within stated limits
   rather than applied rigidly. `MaxSizeRatio = 0.30` guards a bad *measurement*; nothing guards a
   long *string*. Auto-fit is a floor-bounded reduction, never a growth.
6. **Slot geometry tightening for Content Studio (item 4).** Once item 4's root cause is removed, the
   captured `SlotGeometryEntry` is asserted to track the real text box. If residual slack remains
   after reflow, the capture script tightens to the content box rather than the padded CSS box.
7. **Baked-in-text feasibility report (item 2, D2).** A written investigation deliverable only:
   candidate detection runtimes, measured accuracy on real generated slides, per-image latency on the
   ComfyUI stage, behaviour when the dependency is absent, and a go/no-go recommendation. **No
   production code, no port, no adapter, no runtime dependency** ships in this change.
8. **Content Studio alignment guides — full v1 scope (item 5, D3).** A snapping/guide mechanism
   plugged into the existing `onPointerMove`/`onPointerDown` drag-resize handlers, snapping against
   **canvas centre (x and y) and every sibling slot's left / centre-x / right and top / centre-y /
   bottom edges**, with visible guide rendering while a snap is active.
9. **Spec deltas and documentation sync** for the affected capabilities, plus the one ADR required by
   D1.

### Out of Scope — explicit non-goals

| Deferred | Why now is wrong |
|---|---|
| **CSS document-flow layout** | Rejected by D1. Converting stacked slots to normal flow would change the markup contract that Content Studio's drag model, `PptxExportAdapter` and slot-geometry capture all assume, trading a rendering bug for an editing regression. Not revisited in this change. |
| **Any OCR / text-detection production code** | D2. The report is the deliverable. A gated follow-up carries its own ADR, its own runtime-dependency selection, and its own accuracy posture. This change pre-approves nothing. |
| **Reflowing the deterministic flat path (`BuildFlatColorMarkup` / `BuildFlatCarouselSlots`)** | That path was restructured *twice* in the last two weeks (archetype geometry presets, divider primitive, ornament slot) and carries a byte-identity golden. The observed defect is on the template-backed photo path. Widening reflow to the flat path in the same change multiplies blast radius across work that has not yet been observed in the wild. Revisit once the two-pass mechanism is proven on one path. |
| **Regenerating or inpainting an image that fails a check** | A failing image is discarded and retried through the existing bounded-retry mechanism (`CAC-IMG-03`), or degrades to the flat path. Repairing a bad generation is a separate capability with its own model dependency. |
| **Automated image-quality or aesthetic scoring** | This repository still has no way to score generated-image quality. Item 1's effectiveness remains owner-verified by real generation, as stated in the prior two changes. |
| **Guides beyond snapping** (distribute, equal-spacing, multi-select alignment, measurement badges) | D3 settles v1 at canvas-centre plus sibling edges/centres. Distribution and measurement badges are separable and independently valuable later changes. |
| **Persisting operator alignment preferences (snap on/off, snap tolerance) to the backend** | Local UI state is sufficient for v1; a persisted per-user setting is a `user-preferences` change. |
| **Changing the enforced-background precedence rule** | Untouched, exactly as shipped by `client-aesthetic-fidelity-fixes`. This change adds validation *to* that path; it does not re-order it. |
| **Replacing the char-count overflow heuristic in `ValidateOverflow`** | The single-slot heuristic stays as-is. This change *adds* a cross-slot check beside it; rewriting the existing one is unrelated scope. |
| **Any change to the aesthetic extraction schema** | The binding schema-fatigue norm (ADR from the prior change) applies. None of these five items needs a new extraction field, and none may add one here. |

## Capabilities

### New Capabilities
None. D2 removes the only candidate for a new port; the feasibility report introduces no behaviour.

### Modified Capabilities (additive deltas)

- `image-generation`: the cold-start image-prompt branch MUST instruct the writer-model to depict the
  slide's subject matter from `SlideCopySummary`, while retaining the client's aesthetic guidance
  (this amends `CACD-IMG-02`, which currently specifies the cold-start branch as the *unchanged*
  descriptor prompt). No guardrail contract change — D2 keeps text detection out of this change.
- `content-generation`: the template-backed markup builders MUST position stacked text slots from the
  preceding slot's **measured** extent rather than from fixed pixel literals; the measure/recompute
  loop MUST terminate within a bounded number of render passes and MUST have a defined behaviour when
  it does not converge; typography resolution MUST bound heading size against copy length within a
  stated floor; guardrail validation MUST run on the template-backed path's output; and a cross-slot
  vertical-overlap violation MUST be detectable.
- `content-studio`: captured slot geometry MUST track the rendered text box rather than a fixed
  `min-height` literal; and the canvas MUST offer alignment guides during drag/resize against canvas
  centre and against every sibling slot's edges and centres.
- `comfyui-image-provider`: **untouched.** D2 keeps the negative prompt as the sole soft defense for
  this change; the report documents that posture rather than altering it.

## Approach

Four tracks. Track A is cheap and independent. Track B is the architectural core and its mechanism is
now settled. Track C is a document. Track D is frontend-only, has **no** backend dependency, and —
because D1 kept absolute positioning — is now fully parallelisable with every other track.

### Track B — two-pass measure-then-render, with a bounded convergence cap (D1)

Mechanism (a) is chosen. The markup contract is preserved; the cost is paid at render time.

**The loop, stated precisely** (this is the part D1 requires be designed for, not hand-waved):

| Step | Behaviour |
|---|---|
| Pass 1 | Render the template-backed markup with the current literal-derived `top` values. Read every text slot's real extent via the existing `SlotGeometryScript`. |
| Fast path | If no two slots intersect **and** no recomputed `top` differs from the rendered `top` by more than a stated epsilon, **stop at one render**. Short-copy slides — the common case — never pay a second pass. |
| Pass 2 | Otherwise recompute subsequent `top` offsets from measured extents and re-render. Re-measure. |
| Pass 3 | If the recompute changed a slot's own wrapping and overlap survives, recompute **once more** and re-render. |
| Cap | **Hard cap of 3 renders per slide** (1 initial + at most 2 re-renders). The loop can never iterate further, and can never be entered from the fast path. |
| Non-convergence | If overlap still survives at the cap, the pipeline accepts the **best-effort pass** (the measured layout with the smallest total intersection area), emits a structured non-convergence diagnostic, and reports a cross-slot violation so the slide routes through the existing bounded-retry / CSS-fallback contract (`CAC-IMG-03`) rather than shipping silently broken. |

**Latency posture (the accepted cost).** Worst case triples the render stage; typical case for a
converging long-headline slide doubles it; the fast path leaves it unchanged. The re-render is scoped
to the **template-backed path only** — never to the deterministic flat path, never to AI-designer
markup. Render remains materially cheaper than the ComfyUI stage it sits beside, which is why the
owner accepted the trade. The cap, the fast path and the path-scoping are the three mitigations, and
each is a testable assertion, not an intention.

**What did not change because of D1.** `StudioVisualMarkupResolver`, `PptxExportAdapter`,
`content-studio-panel.tsx`'s drag model and `SlotGeometryEntry`'s shape all continue to consume
absolutely-positioned markup. Item 4 therefore still needs its own fix (WU-6) — mechanism (a) does
not fix it for free.

### Track D — full-scope alignment guides (D3)

Snap candidates are computed per pointer move from: canvas centre-x and centre-y, plus, for every
sibling slot, its left / centre-x / right and top / centre-y / bottom. A candidate within tolerance
wins; the winning axis renders a guide line for the duration of the snap. Because D1 preserved
absolute positioning, snapping operates directly on the same `left`/`top` values the drag handlers
already write — no reconciliation with document flow is needed, and Track D has no ordering
dependency on Track B.

### File / layer map

| Piece | Track | Layer | Path |
|---|---|---|---|
| Cold-start topical-fidelity instruction | A | Application | `Features/Social/ContentGeneration/Background/ImagePromptWriter.cs` (~L172-181) |
| Cross-slot vertical-overlap check | B | Application | `Features/Social/ContentGeneration/Visual/VisualGuardrailValidator.cs` (~L552-610) |
| Guardrail invocation on the template-backed path | B | Application | `Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` (~L957, ~L977-1035) |
| Reflow of stacked text slots from measured extents | B | Application | same file — `BuildTemplateBackedMarkup` (~L2049), `BuildCarouselTemplateSlots` (~L2297-2304), `BuildStoryTemplateSlots` (~L2241) |
| Measurement / re-render loop + convergence cap | B | Infrastructure | `Rendering/HtmlChromiumContentRenderer.cs` (`SlotGeometryScript` ~L51-66) |
| Copy-length-aware typography fit | B | Application | `Features/Social/ContentGeneration/Visual/CompositionDefaults.cs` (`ResolveTypography`/`SizeFor`/`SizeForCaption`) |
| Slot geometry capture tightening | B | Application / Infrastructure | `Features/Social/ContentGeneration/Pipeline/CompositionSpecEnvelope.cs` (`SlotGeometryEntry` ~L169-183), `Rendering/HtmlChromiumContentRenderer.cs` |
| Feasibility report | C | Docs | `openspec/changes/content-layout-and-image-fidelity/ocr-text-detection-feasibility-report.md` |
| Snap-candidate engine (canvas centre + all sibling edges/centres) | D | Web | new module beside `apps/web/components/social/content-studio-panel.tsx` |
| Guide rendering + pointer-handler integration | D | Web | `apps/web/components/social/content-studio-panel.tsx` (`onPointerDown`/`onPointerMove` ~L2843, L2873, and per-element handlers ~L3241/3275/3288/3531/3560/3572) |

### Reused vs. new

| Reused unchanged | New |
|---|---|
| The method-gated branch's topical-fidelity wording (`ImagePromptWriter` ~L150-169) | Same instruction applied to the cold-start branch |
| `VisualGuardrailValidator`'s existing single-slot `data-max-lines` heuristic | A cross-slot geometry check beside it |
| The bounded-retry + CSS-fallback contract (`CAC-IMG-03`) | Reused as the non-convergence disposition |
| `SlotGeometryScript` / `getBoundingClientRect()` capture mechanism | Reused as the measurement source for the second pass, and tightened for item 4 |
| The `IBrandMarkAssetExtractor` investigation-then-gate pattern | Applied to the OCR track (report only, D2) |
| Existing `onPointerDown`/`onPointerMove` drag-resize handlers | Snap-candidate computation and guide rendering inside the same render pass |
| **Absolute positioning, the Studio drag model, `PptxExportAdapter`, `StudioVisualMarkupResolver`** | **Untouched — this is what D1 bought** |
| The enforced-background precedence rule, the flat path, the archetype presets, the divider, the ornament slot | **Untouched** |

### Ordering rationale

Detection before correction. Track B ships its cross-slot check and its guardrail wiring *first*, so
the overlap is observable and regression-testable before any positioning code moves. Track A, Track C
and Track D are independent of Track B and of each other, and are freely reorderable — D1 removed the
only ordering constraint Track D previously had.

## ADR determination

`openspec/config.yaml` sets `require_adr_for_architecture_changes: true`.

**Exactly one ADR is required, and it is now unconditional:**

- **D1 (two-pass measure-then-render)** — **ADR required.** Introducing a
  measure→recompute→re-render loop on the pipeline's render stage, with an accepted latency cost, a
  hard iteration cap and a defined non-convergence disposition, is a performance/architecture posture
  that must be recorded.
- **D2 (investigation-only)** — no ADR. The report itself is the artifact; any follow-up that ships
  real OCR carries its own ADR. This change does not pre-approve it.
- **D3 (full-scope guides)** — no ADR. Frontend interaction affordance; no bounded area, port,
  provider category or credential type.
- **Tracks A, and Track B's detection half** — no ADR.

## PRD Traceability

`openspec/config.yaml` sets `require_prd_traceability: true`.

| PRD anchor | This change | Honesty note |
|---|---|---|
| §1 Product Summary / §3 Goals — content the client can publish as-is | Directly: an overlapping headline or an off-topic image is an unpublishable asset regardless of how well the aesthetic matched | — |
| §9 Acceptance Criteria — fidelity of generated assets | Adds layout correctness and image-content correctness to the colour/type/composition fidelity already shipped | Baked-in-text removal is **not** delivered here (D2); only the decision-grade answer is |
| §10 Content Generation — Format Taxonomy | Carousel and story template-backed paths only; no taxonomy change | — |
| §12 Content Studio (operator editing) | Slot geometry accuracy and full-scope alignment guides are operator-editing affordances | Alignment guides are a **new operator capability**, not a fix. Spec phase confirms whether §12 needs an amendment — provisionally expected **yes** for item 5, **no** for item 4. |

No `profile-analysis` / §11 involvement: nothing here touches extraction.

## Delivery forecast — direct sequential commits to `dev`

Delivery strategy `single-pr`: direct sequential commits to `dev`, no PR chain, no stacked PRs,
tracked via one GitHub issue. Budget **800 changed lines per work unit**; estimates are
`additions + deletions` with Strict TDD included (tests ≈ 1:1). Re-forecast reflects D1, D2 and D3.

| # | Work unit | Track | Est. | Over budget? |
|---|---|---|---|---|
| WU-1 | Cold-start topical-fidelity instruction + prompt-version bump + assertions that the instruction is present and aesthetic guidance survives | A | ~240 | No |
| WU-2 | Cross-slot vertical-overlap check in `VisualGuardrailValidator` + fixtures reproducing the real 149px-headline overlap | B | ~480 | No |
| WU-3 | Guardrail invocation on `BuildTemplateBackedMarkup`'s output + retry/degrade disposition wiring | B | ~420 | No |
| WU-4 | Two-pass reflow: measurement pass, `top` recompute from measured extents, fast-path short-circuit, **3-render hard cap**, best-effort + non-convergence diagnostic, latency assertions, ADR | B | ~700 | No (tight — split candidate) |
| WU-5 | Copy-length-aware typography fit with a stated floor + long-copy regression fixtures | B | ~420 | No |
| WU-6 | Slot geometry tightening for Content Studio + assertion that the captured box tracks rendered text | B | ~360 | No |
| WU-7 | OCR / baked-in-text feasibility report — **document only, no production code** (D2) | C | ~220 | No |
| WU-8a | **Snap-candidate engine**: pure module computing canvas-centre and per-sibling left/centre-x/right + top/centre-y/bottom candidates, tolerance resolution, winner selection + unit tests | D | ~440 | No |
| WU-8b | **Guide rendering + pointer integration**: wire the engine into `onPointerDown`/`onPointerMove`, render active guide lines, bypass modifier, interaction tests | D | ~400 | No |
| WU-9 | Spec deltas + doc sync + PRD §12 amendment if required | — | ~420 | No |

**Total: ~4,100 changed lines across 10 commits.** No mutually-exclusive branches remain — the
settled decisions collapsed the previous four-way branch matrix to a single plan.

### Re-forecast against the three decisions — plain statement

- **D2 shrinks Track C by ~900 lines.** The previous implement branch (WU-8r ~640 + WU-9r ~480 =
  ~1,120) is replaced by a single ~220-line report unit. No unit is near budget on this track.
- **D1 removes ~1,300 lines of mechanism-(b) work** (old WU-4b ~600 + WU-5b ~700) and keeps the old
  WU-4a. That unit grows from ~640 to **~700** because the single-recompute cap became a real bounded
  loop with a fast path, a best-effort selection rule, a non-convergence diagnostic and latency
  assertions. **~700 is under budget but leaves under 100 lines of headroom** — it is a split
  candidate at the tasks phase (natural seam: Infrastructure measure/re-render loop vs. Application
  builder integration). It is **not** mandated as a split now, because the loop and its consumer are
  hard to test independently and a forced split would ship a renderer capability nothing calls.
- **D3 grows Track D past budget and therefore forces a split.** The lighter canvas-centre-only v1 was
  ~460 and the previously-sketched fuller variant was ~720. Full scope as ruled — canvas centre **and**
  six alignment axes per sibling slot, plus guide rendering and Strict TDD 1:1 coverage on a
  shadow-DOM canvas component — re-forecasts to **~840, over the 800-line budget**. It is split into
  **WU-8a / WU-8b** following this project's established WU-Na/WU-Nb pattern, at the cleanest seam:
  a pure, fully unit-testable candidate engine, then its integration and visual rendering.

`Decision needed before apply: No` — D1, D2 and D3 are settled; no work unit is conditional.
`Chained PRs recommended: No`
`800-line budget risk: Medium` — one unit (WU-4 at ~700) sits within 100 lines of budget and is a
tasks-phase split candidate; the one unit that exceeded budget (Track D at ~840) is already split.

**Ordering constraints**: WU-2 → WU-3 (a check must exist before it can be wired in). WU-2/WU-3 →
WU-4 (detection before correction). WU-5 after WU-4, so auto-fit is tuned against real reflowed
geometry rather than against the broken baseline. WU-6 after WU-4 (its residual-slack question cannot
be answered before). WU-8a → WU-8b. WU-1, WU-7 and the WU-8 pair are independent of Track B and
reorderable. WU-9 last.

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| **The two-pass loop does not converge**: pushing a slot down can change its own wrapping, so one recompute may not settle. | Medium | **Designed for, not hand-waved (D1's explicit requirement).** Hard cap of 3 renders per slide; the loop is structurally incapable of iterating further. On non-convergence the pipeline takes the best-effort pass (smallest total intersection), emits a structured diagnostic, and reports a cross-slot violation so the slide degrades through the existing bounded-retry / CSS-fallback contract instead of shipping broken. Cap, best-effort selection and diagnostic are each a blocking test assertion. |
| **Two-pass rendering increases cost on the pipeline's slowest-adjacent stage.** | High (accepted by D1) | Three scoped mitigations, each asserted: (1) a fast path that renders **once** when the first measurement shows no overlap and no material `top` delta — the common short-copy case; (2) the loop is scoped to the **template-backed path only**, never the flat path and never AI-designer markup; (3) the hard 3-render cap bounds worst case at 3×, not unbounded. Recorded in the D1 ADR with the measured render-stage cost before and after. |
| **Track B touches the same rendering path three other changes modified this quarter** (typography sizing, text colour, brand mark, divider/archetype/ornament), including a byte-identity golden. | **High** | Reflow is scoped to the *template-backed* builders only; the flat path with the golden is an explicit non-goal. Every pre-existing test on both paths must pass unchanged, as a blocking criterion. |
| **Item 1's fix is cheap but unverifiable offline** — whether the writer-model actually produces topically-relevant prompts more often cannot be asserted in-repo. | High (accepted) | Same "can't measure offline" posture as the prior two changes. What *is* assertable: the instruction is present in the assembled prompt, `SlideCopySummary` is referenced by the instruction text, and aesthetic guidance is not displaced. Effectiveness is owner-verified by real generation. |
| **Baked-in text keeps shipping for the whole life of this change** — D2 delivers a report, not a fix. | High (accepted, explicit) | This is the conscious cost of the investigate-then-gate precedent, which this project applied twice this quarter and was right both times. The negative prompt remains the only defense until the gated follow-up. The report MUST state measured accuracy and per-image latency, so the follow-up decision is evidence-based rather than another round of estimation. |
| **Full-scope guides degrade drag performance** on a shadow-DOM canvas with many slots — six candidate axes per sibling, recomputed per pointer move. | Medium *(raised by D3)* | The WU-8a/WU-8b split exists partly for this: the candidate engine is a pure module with its own performance assertion at a stated slot count, measured **before** it is wired into the pointer handler. Candidates are computed once per drag start where geometry is static, not per move, wherever the slot set is unchanged. |
| **Auto-fit silently shrinks text below legibility**, undoing the measured-typography fidelity the prior change just shipped. | Medium | Auto-fit is floor-bounded and reduction-only; the floor is a spec-phase requirement, asserted by test. A profile whose copy fits at its measured ratio must render **byte-identically** to today. |
| **Item 4 is not fully fixed by item 3.** D1 explicitly does *not* fix item 4 for free (only the rejected CSS-flow option would have), so residual padding/line-height slack is a live possibility. | Medium *(raised by D1)* | WU-6 exists precisely for that residual and is sequenced after WU-4 so the question is answered with evidence rather than predicted. |
| Guardrail validation newly running on the template-backed path rejects slides that previously shipped | Medium | The cross-slot check ships (WU-2) before it is wired in (WU-3), so its false-positive rate is measurable against existing fixtures first. Rejection routes to the existing bounded-retry/CSS-fallback contract, never to a failed job. |
| **The owner's publishable-as-is bar is still not met after all tracks ship** | Medium (stated honestly) | This change closes layout correctness and image-topic correctness. It does **not** close baked-in text (D2 defers it) and does not claim to close every remaining fidelity gap; the prior change's Approach 2 (constrained AI design pass) remains the un-prejudiced next lever. |
| Verified against one client / one topic (Lucia Fernandez, *ronquidos*) | Medium | Success criteria require a second client with a materially different `SizeRatio` and a materially longer/shorter headline, since the defect is a function of exactly those two variables. |

## Rollback Plan

- **WU-1** — prompt text only. Revert restores the previous instruction string and the previous
  prompt-version key. No persisted data affected.
- **WU-2** — adds a check that nothing calls yet. Inert; reverts freely.
- **WU-3** — reverting restores the pre-change behaviour exactly (the template-backed path is
  unvalidated today), so a partial revert leaves a consistent pipeline.
- **WU-4** — **self-contained within the render pipeline, and this is the direct benefit of D1.**
  Reverting removes the measurement/re-render loop and restores the fixed `top` literals. Rendering
  returns to today's cost and today's overlap. Because the markup shape never changed, no downstream
  consumer — Studio, export, geometry capture — is affected by the revert. There is **no** multi-unit
  atomic-revert constraint anywhere in this change; every unit reverts independently.
- **WU-5** — reverting restores rigid size application. The floor-bounded auto-fit has no persisted
  side effect.
- **WU-6** — geometry capture only; reverting restores the padded CSS box. Studio degrades to today's
  loose boxes, never to broken ones.
- **WU-7** — a document. Nothing to roll back, and nothing in production depends on it.
- **WU-8a** — a pure module nothing calls until WU-8b. Inert; reverts freely.
- **WU-8b** — frontend-only, local UI state, no persisted preference. Reverting restores free
  unsnapped dragging, which is today's behaviour. WU-8a may be left in place or reverted after.
- **WU-9** — documentation and spec deltas only.
- No database migration exists in this change.

## Dependencies

- Existing `VisualGuardrailValidator`, the bounded-retry + CSS-fallback contract (`CAC-IMG-03`),
  `SlotGeometryScript` and `IGeneratedAssetStorage` — reused read-only.
- A running ComfyUI instance for owner-side verification of item 1 and for gathering the real
  generated images the WU-7 report measures against.
- **Owner-side prerequisite**: at least two real client profiles whose measured `SizeRatio` and
  typical headline length differ materially — the overlap defect is a function of exactly those two
  variables, so a second client with a similar profile verifies nothing.
- **No new runtime dependency, migration, credential or provider category.** D2 removed the only
  candidate (a text-detection runtime); selecting one is the follow-up change's job, not this one's.

## Success Criteria

- [ ] The cold-start image-prompt branch contains an explicit instruction to depict the slide's
      subject matter, referencing `SlideCopySummary` — asserted on the assembled prompt.
- [ ] The cold-start branch still carries the client's style descriptors, motifs, background
      descriptors, voice tone and palette; the topical instruction **adds to** aesthetic guidance and
      does not displace it — asserted, not assumed.
- [ ] A cross-slot vertical-overlap check exists and **reproduces the real defect**: a 149px headline
      wrapping to 2–3 lines at `top:255px` against a subtitle at `top:575px` is reported as a
      violation.
- [ ] Guardrail validation runs on `BuildTemplateBackedMarkup`'s output — asserted by test, since the
      current bypass is caused by call ordering that a future refactor could silently reintroduce.
- [ ] A long headline at the client's real measured `SizeRatio` renders **without** intersecting the
      subtitle slot, on both the carousel and the story template builders.
- [ ] **The render loop never exceeds 3 renders per slide** — asserted directly against a
      deliberately non-converging fixture, not inferred.
- [ ] **A slide whose first measurement shows no overlap renders exactly once** — the fast path is
      asserted, so the common case pays no extra latency.
- [ ] **A non-converging slide produces the best-effort layout plus a structured non-convergence
      diagnostic, and routes through the bounded-retry / CSS-fallback contract** — never a silently
      broken slide, never a failed job, never a hang.
- [ ] The generated markup remains **absolutely positioned**, and Content Studio drag/resize,
      `PptxExportAdapter` output and `StudioVisualMarkupResolver` behaviour are unchanged — the D1
      guarantee, asserted rather than assumed.
- [ ] A profile and copy string that already fit today render **byte-identically** to the pre-change
      output — the auto-fit and reflow identity guarantee.
- [ ] Every pre-existing test on the template-backed path **and** on the deterministic flat path
      (including the prior change's byte-identity golden) passes unchanged.
- [ ] Auto-fit never increases a resolved size, and never reduces below the specified floor —
      asserted at the boundary.
- [ ] Captured `SlotGeometryEntry` for a short text string is measurably tighter than the pre-change
      `min-height`-driven box, and Content Studio renders editing boxes that track the text.
- [ ] Alignment guides snap and render against **canvas centre-x/centre-y and every sibling slot's
      left / centre-x / right and top / centre-y / bottom**, and snapping is distinguishable from free
      movement by test.
- [ ] The snap-candidate engine meets a stated per-move computation budget at a stated slot count,
      measured on the pure module before integration.
- [ ] The feasibility report exists and states measured detection accuracy, per-image latency, the
      runtime dependency required, behaviour when it is absent, and an explicit go/no-go
      recommendation — and **no** OCR production code, port or adapter ships in this change.
- [ ] The D1 ADR (two-pass measure-then-render, latency posture, iteration cap, non-convergence
      disposition) is merged.
- [ ] The owner reviews one real generation for each of two clients with materially different
      `SizeRatio` and headline length, and records accept/reject in writing.
- [ ] Spec deltas merged for the affected capabilities; PRD §12 amendment merged if the spec phase
      confirms it is required.
- [ ] All MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`); Strict TDD followed;
      architecture gate green (no provider or EF type crossing into Domain/Application).

## Proposal question round

**All three previously-open decisions are closed** by the owner's answers and are recorded in
`## Settled decisions`. Nothing in this proposal is now blocking.

One low-stakes item remains genuinely open, and it does **not** block spec or design:

1. **Snap tolerance and the bypass affordance for the new guides.** Full scope (D3) makes snapping
   more assertive — with canvas centre plus six axes per sibling, a large tolerance can make the
   canvas feel sticky, and a small one can make snapping feel unreliable. There is also a question of
   whether the operator can temporarily suppress snapping while dragging.
   *Assumption if unanswered: a modest pixel tolerance tuned during WU-8b against a real multi-slot
   slide, plus a held-modifier bypass (the conventional editor affordance). Both are local UI state,
   both are trivially adjustable after the fact, and neither changes any work unit's shape or size.*

You may answer this, skip it, correct the framing, or ask for another question round.
