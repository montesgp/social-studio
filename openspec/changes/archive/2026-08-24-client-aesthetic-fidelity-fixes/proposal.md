# Proposal: ClientAesthetic (CASE C) Fidelity Fixes

## Change ID
`client-aesthetic-fidelity-fixes`

## Status
Proposal — **revision 2**. Builds on `exploration.md` (verified root causes with file/line evidence). The
owner answered the first proposal question round and expanded scope on all four questions; those answers are
now binding and appear as settled decisions 3–6. Specs and design next.

*Resolved in this revision*: the former `## Proposal question round` section is gone. Its four assumptions
were all corrected (expanded, not confirmed) and are now **Settled decisions 3, 4, 5 and 6**. The
consequences are propagated through Scope, Capabilities, Approach, Delivery, Risks, Rollback and Success
Criteria below, plus a new `## Review Workload Forecast`.

## Intent

**Business problem.** For `BackgroundSourcingPolicy.ClientAesthetic` clients the promise is that generated
carousels are indistinguishable from what the client already posts. The reference incident — client
*Lucia Fernandez* (`client_id 0186cd86-…`, analysis `3f0e272f-…`, job `894ccda7-…`) — produced slides the
client would not publish: an `rgba(0,0,0,0.5)` dark card overlay the client never uses, and typography
unrelated to their feed. The operator's only recourse today is to discard the package and regenerate, which
costs another full ComfyUI cycle and still gives no guarantee.

**Current-state gap (verified, not assumed).**

1. When a Photo-classified slide's AI image generation fails, the slide silently falls through to the
   generic `Creative` background tier of `AiVisualDesigner`. That tier is the only one with **no**
   `NoScrimDirective`, and `AllowsDecoration` defaults to `true` whenever `ImageTypeRatio` is unmeasured — so
   the model is actively told to add gradients, blobs and glows. Failure degrades into *invention*, not into
   the client's own visual language.
2. When the AI designer itself fails outright (`background is null`), the pipeline paints an unconditional
   `rgba(0,0,0,0.52)` scrim card. Same class of defect: an invented treatment tied to no client signal.
3. Both degrade paths are **silent**. The operator opens Content Studio and sees a slide that looks
   deliberate; nothing tells them it is a fallback, so they cannot decide to regenerate.
4. The whole typography chain downstream of extraction is already correct and profile-aware
   (`CarouselVisualComposer` already calls the 4-arg `CompositionDefaults.ResolveTypography`), but
   `MultimodalAestheticStyleExtractor` never extracts typography at all — the chain is fed nothing.
   Separately, `AiVisualDesigner.AllowedFonts` is sourced from the org Brand Kit, so the model invents fonts
   the guardrail later rejects, discarding the whole design.
5. No signal anywhere in the extraction schema expresses "this client blends or fades their photo
   backgrounds". The single real `<img>` path renders `object-fit:cover` with no alpha or blend treatment.

**Outcome.** A ClientAesthetic carousel degrades *faithfully* instead of creatively, says so when it does,
renders the client's own type classes across three roles, and reproduces their per-slide photo-transparency
habit when they have one — with unchanged behavior for every client that has no such signal.

## Settled decisions (do not re-open)

1. **Degrade uses a deterministic template, not a better prompt.** A degraded slide is routed through the
   same faithful ColorPairings-based flat treatment already proven correct for genuinely flat slides
   (`BuildFlatColorMarkup` / `CompositionDefaults.ResolvePalette`). The AI designer is **not** re-called for
   that slide. The constrained-Creative-prompt alternative was considered and explicitly rejected: a
   non-deterministic fix for a fidelity guarantee is not a fix. Accepted tradeoff: a failed photo slide looks
   flat rather than photographic.
2. **The transparency signal is boolean + qualitative descriptor, never a numeric alpha.** A precise opacity
   ratio is not something a vision model can reliably measure off a feed, and a confidently-wrong number is
   worse than an honest qualitative one.
3. **Degradation MUST be visible in Content Studio.** A degraded slide is not silent. The operator sees a
   per-slide marker and can choose to regenerate. *(Owner answer to Q1 — expanded from the assumed "silent
   is fine".)*
4. **The transparency signal is per-slide, not per-client.** It is captured as a parallel per-slide
   structure alongside `BackgroundPattern` (already `IReadOnlyList<SlideBackgroundKind>`), not as a scalar on
   the profile. *(Owner answer to Q2 — expanded from the assumed per-client scalar.)*
5. **Typography extracts three roles: heading, body and caption/overline.** *(Owner answer to Q3 — expanded
   from the assumed heading+body.)* **Verified**: `CompositionTypography`
   (`Incoders.Hive.Domain/Social/ContentGeneration/VisualCompositionSpec.cs` L74-99) carries **only**
   heading and body — `HeadingFamily/HeadingSize/BodyFamily/BodySize` positional plus
   `HeadingWeight/BodyWeight/BodyItalic/RequestedHeadingFamily/RequestedBodyFamily` as init members. There is
   **no** slack for a third role; new init-default members are required. This is a Domain-layer edit.
6. **Deterministic degrade covers AI-designer failure too, not only image-generation failure.** Any failure
   of the AI Creative-tier designer path on a ClientAesthetic carousel slide routes to the same deterministic
   palette-backed flat treatment, replacing the `rgba(0,0,0,0.52)` scrim card. *(Owner answer to Q4 —
   expanded from the assumed Photo-only trigger.)* The existing `mappedFlatKind == FlatColor` short-circuit
   is **not** touched: it already degrades correctly (exploration item 2) and continues to run first. The
   expansion adds a *second* trigger downstream of it, for slides the pattern does not already call
   FlatColor.

## Scope

### In Scope

1. **Faithful degrade, both triggers** — in `ContentGenerationJobProcessor`, route to the deterministic
   palette-backed flat treatment when (a) a Photo-classified slide's image generation failed, or (b) the AI
   designer returned no usable background for a ClientAesthetic carousel slide (replacing the scrim card).
   The pre-existing `mappedFlatKind == FlatColor` short-circuit stays first and unchanged.
2. **Degrade visibility signal (backend half)** — an additive-nullable degrade marker on the per-slide
   `CompositionSpecEnvelope`, written whenever either trigger fires, so `visual_spec_markup` carries it
   verbatim to Content Studio with no DB migration and no Content Studio API DTO change.
3. **Degrade visibility (frontend half)** — `apps/web` surfaces the marker as a per-slide badge in the
   Content Studio carousel/story viewer and slide strip, so the operator can regenerate deliberately.
4. **Typography extraction + designer wiring, three roles** — heading, body and caption/overline
   (family class, weight, italic, size ratio) through the extractor prompt, `AestheticExtractionDto` and
   `Map()`; new caption members on `CompositionTypography`; caption resolution in
   `CompositionDefaults.ResolveTypography`; caption consumption in both markup builders; `Typography` on
   `ClientAestheticDesignContext`; `AiVisualDesigner.AllowedFonts` sourced from client-derived classes
   resolved through `FontFamilyResolution` instead of the org Brand Kit.
5. **Per-slide photo-transparency signal, end to end** — a per-slide array parallel to `BackgroundPattern`
   through extractor prompt + DTO + `Map()` → `AestheticExtraction` → `AestheticProfilePersistence` →
   `ClientAestheticProfile` → `ClientAestheticDesignContext` → per-`specIndex` conditional opacity/blend on
   `BuildTemplateBackedMarkup`'s `<img>` (which already resolves per `specIndex`) and on the AI path's own
   real-photo background.
6. **ComfyUI generation-time findings report** — an investigative, documentation-only deliverable recording
   the verified conclusion: the two 10-minute failures match `ComfyUiSettings.DefaultTimeout = 600000ms`
   exactly, `DefaultSteps = 20` / `DefaultCfg = 7.0` are standard, and the ~9m35s single-image time is
   attributable to the `--directml --lowvram --cpu-vae` backend configuration, not to a code defect. **No
   code change and no infrastructure recommendation** — findings for the record.

### Out of Scope — explicit non-goals

| Deferred | Why now is wrong |
|---|---|
| Brand-mark placeholder rendering (FFS-CG-02 chip instead of the real client signature text) | Known adjacent gap in the same feature, but a separate defect with its own extraction/render question |
| Profile-aware typography for non-carousel composers (`StoryVisualComposer`, `FeedPostVisualComposer`, …) | They still use the 2-arg profile-blind overload; widening beyond carousel/ClientAesthetic multiplies the blast radius of an already-load-bearing fix |
| A one-click "regenerate this slide" action next to the degrade badge | Decision 3 requires *visibility*, not a new regeneration entry point; per-slide regeneration is its own pipeline change |
| Persisting the degrade marker as a queryable column / analytics on degrade rate | The envelope carries it for the operator today; a reporting surface is later work with its own schema question |
| Post-hoc CSS colour clamping of AI-generated backgrounds (exploration's item-1 option 3) | Superseded by decision 1; would add first-of-its-kind colour-extraction-and-recolour logic |
| Any ComfyUI code, settings, timeout, or hardware/execution-mode change | Item 6 concluded there is no code defect; changing settings without evidence would be speculative |
| FlatColor slides (exploration item 2) | Already verified correct; untouched, and it is the reference implementation both degrade triggers reuse |

## Capabilities

### New Capabilities
None.

### Modified Capabilities (additive deltas)
- `profile-analysis`: multimodal aesthetic extraction gains per-role typography for **three** roles
  (heading, body, caption/overline) and a **per-slide** photo-transparency signal (boolean + qualitative
  descriptor per slide); both additive-nullable, absent ⇒ current behavior.
- `content-generation`: a ClientAesthetic carousel slide MUST degrade to the deterministic palette-backed
  flat treatment when its photo generation fails **or** when the AI designer returns no usable background;
  the degrade MUST be recorded on that slide's composition envelope; the design context MUST carry
  client-derived typography and the per-slide transparency signal, and the photo layer MUST honour that
  signal per slide at render time.
- `content-studio`: the Studio slide viewer MUST surface, per slide, that a slide was produced by the
  deterministic degrade path.

## Approach

Follow the additive-trailing-nullable pattern already established by `Typography`, `BrandMark`,
`ImageTypeRatio` and `CompositionSpecEnvelope.Deleted`: null/false means "unmeasured", and unmeasured means
today's behavior. Nothing regresses for a client whose profile predates this change; the fix converges on the
next analysis refresh.

**Why the envelope carries the degrade marker.** `CompositionSpecEnvelope`
(`.../Pipeline/CompositionSpecEnvelope.cs` L119-130) already serialises one envelope per carousel slide into
`composition_spec_json`, is passed through materialization verbatim as `ContentItem.visualSpecMarkup`, and is
parsed on the frontend by `parseEnvelopes` into an `Envelope` interface that declares
`[key: string]: unknown` (`content-studio-panel.tsx` L233-239, L283-298). A trailing optional member on the
record therefore reaches the operator with **no DB migration, no `ContentItemDto` change, no Content Studio
endpoint change**, and survives the Studio edit/autosave round-trip because unknown keys are preserved by
`JSON.stringify`. This is the lowest-friction route by a wide margin; the alternative (a new column plus DTO
plumbing through materialization) was rejected as disproportionate.

| Piece | Layer | Path |
|---|---|---|
| Degrade trigger A (Photo + generation failed) and trigger B (AI designer returned nothing) | Application | `Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` (~L807-954; scrim-card site removed) |
| Degrade marker on the per-slide envelope | Application | `Features/Social/ContentGeneration/Pipeline/CompositionSpecEnvelope.cs` (trailing optional member, mirrors `Deleted`) |
| Per-slide conditional opacity/blend on the photo `<img>` | Application | same processor, `BuildTemplateBackedMarkup` (~L1891-1989), resolved per `specIndex` |
| Typography (3 roles) + per-slide transparency extraction (prompt, DTO, `Map()`) | Infrastructure | `Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs` |
| Caption/overline role members | Domain | `Social/ContentGeneration/VisualCompositionSpec.cs` (`CompositionTypography`, init-defaulted) |
| Caption resolution | Application | `Visual/CompositionDefaults.cs` (`ResolveTypography` 4-arg, L159-199) |
| Per-slide transparency persisted round-trip | Application | `Shared/AestheticProfilePersistence.cs`, `Shared/ClientAestheticProfile.cs` (parallel to `BackgroundPattern`) |
| `Typography` + per-slide transparency on the design context | Application | `Shared/ClientAestheticDesignContext.cs` (built at `ContentGenerationJobProcessor.cs` L469-481) |
| `AllowedFonts` from client-derived classes | Infrastructure | `Social/ContentGeneration/AiVisualDesigner.cs` (L286) |
| Degrade badge (per slide) | Web | `apps/web/components/social/content-studio-panel.tsx` (`Envelope` interface + carousel/story viewer + slide strip) |
| Findings report | Docs | `openspec/changes/client-aesthetic-fidelity-fixes/comfyui-generation-time-report.md` |

Reused unchanged: `CompositionDefaults.ResolvePalette`, `FontFamilyResolution`, `FontCatalog`,
`TypographyNormalizer`, `ColorPairings`, `BackgroundPatternReconciler`, `CompositionSpecJson`
serialize/parse, and the whole ComfyUI/integration-policy stack.

## ADR

Not required. This change introduces no new bounded area, port, provider category or credential type; it adds
nullable/init-defaulted fields to existing contracts and changes branch selection inside an existing
pipeline. The `CompositionTypography` caption members are additive to an existing Domain record, not a new
Domain concept. `require_adr_for_architecture_changes` is not triggered. If design surfaces a new render-time
abstraction for the per-slide transparency treatment, revisit before apply.

## PRD Traceability

| PRD anchor | This change |
|---|---|
| §3 Goals — content the client can publish as-is | Directly: a failed slide must still be publishable, not visibly AI-invented |
| §11 Profile Analysis — MVP acceptance criteria | Extends the extracted aesthetic with 3-role typography and a per-slide photo-transparency signal |
| §10 Content Generation — Format Taxonomy | Scoped to carousel; no taxonomy change |
| §12 Content Studio — operator review loop | Adds a per-slide degrade indicator to the review surface |

No PRD edit required — this is a fidelity defect against goals the PRD already states, plus one operator
transparency affordance inside an existing review surface.

## Delivery

Direct sequential commits to `dev` under one tracking GitHub issue. **No PR chain, no stacked PRs.** Review
budget: 800 changed lines per commit, Strict TDD (tests ≈ 1:1). Backend (.NET) and frontend (`apps/web`) are
separate commits — different codebases, different review passes.

| # | Work unit | Stack | Est. lines | Over 800? |
|---|---|---|---|---|
| WU-0 | ComfyUI findings report (was WU-4; independent, can land first) | Docs | ~120 | No |
| WU-1a | Trigger A + trigger B degrade routing and scrim-card removal | .NET | ~470 | No |
| WU-1b | Envelope degrade marker + serialization/round-trip tests | .NET | ~250 | No |
| WU-2a | Typography extraction half: prompt + DTO + `Map()` | .NET | ~300 | No |
| WU-2b | Typography render half: `CompositionTypography` caption members + `ResolveTypography` + both markup builders + `ClientAestheticDesignContext.Typography` + `AllowedFonts` | .NET | ~400 | No |
| WU-3a | Per-slide transparency: extractor prompt + DTO + `Map()` + `AestheticExtraction` + `AestheticProfilePersistence` + `ClientAestheticProfile` round-trip | .NET | ~480 | No |
| WU-3b | Per-slide transparency: `ClientAestheticDesignContext` + per-`specIndex` render treatment on `BuildTemplateBackedMarkup` and the AI-path background | .NET | ~420 | No |
| WU-4 | Content Studio per-slide degrade badge (`Envelope` type, carousel + story viewers, slide strip, tests) | Web | ~220 | No |

Owner decision (this revision): WU-1 and WU-2 are pre-split into `a`/`b` commits each, both landing
sequentially on `dev` — chosen over accepting them as single ~700-720-line commits, for review comfort
despite being within the 800-line budget.

Ordering: WU-0 independent. WU-1a before WU-1b (the marker is written where trigger A/B resolve). WU-1a/1b
first among the code units (WU-4 consumes the envelope marker from WU-1b, and WU-3b's render treatment
touches the same markup builders WU-1a changes). WU-2a before WU-2b. WU-2 (a+b) is independent of WU-1/WU-3.
WU-3b depends on WU-3a. WU-4 depends on WU-1b.

Note the split relative to revision 1: WU-3 was a single ~650-line unit under the per-client scalar
assumption. Decision 4 turns it into a per-slide array across six layers plus per-slide render resolution,
which projects to ~900 lines as one commit — so it is pre-split into WU-3a/WU-3b, each comfortably inside
budget with a clear start, finish, verification and rollback. WU-1 and WU-2 are split for the same reason,
per the owner decision above.

## Review Workload Forecast

**Chained PRs recommended: No** (delivery strategy is `single-pr` — direct sequential commits to `dev`).
**400-line budget risk: High** — every code work unit exceeds the 400-line default; the session budget is
800, against which the risk is **Medium**.
**Estimated changed lines: ~2660 total** across 6 work units (~2540 code + ~120 docs).
**Decision needed before apply: Resolved.** Owner chose to pre-split WU-1 and WU-2 (see Delivery table
above) rather than accept them as single ~700-720-line commits. Every work unit is now comfortably inside
the 800-line budget with headroom.

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Both degrade triggers only fire on *failure*, so they are invisible in a happy-path run | High | Test via mocked/forced failure fixtures (ComfyUI failure for trigger A, designer-returns-null for trigger B), not by waiting for a real timeout; make each degrade decision a pure, directly-testable function |
| Removing the `rgba(0,0,0,0.52)` scrim card changes a currently-shipping resilience path for *all* jobs, not only ClientAesthetic | Medium | Scope trigger B explicitly to ClientAesthetic carousel slides; leave the scrim card in place for every other policy and assert that with a test |
| Trigger B could shadow or double-fire with the existing `mappedFlatKind == FlatColor` short-circuit | Medium | The short-circuit runs first and is untouched; add a regression test asserting a FlatColor slide still takes the pre-existing path and never reaches trigger B |
| The degrade marker leaks into operator-visible exports (PPTX, package ZIP) as stray JSON | Low | The marker is an envelope member, not markup; assert exports are byte-unchanged for a non-degraded slide and contain no marker text for a degraded one |
| Studio edit/autosave drops the unknown envelope key on save | Low | `Envelope` declares `[key: string]: unknown` and `serializeEnvelopes` stringifies the whole object — verified; add a frontend round-trip test to lock it |
| The extractor prompt already returns a large multi-part JSON; three typography roles plus a per-slide transparency array raise model non-compliance risk materially more than revision 1 assumed | **High** (was Medium) | Defensive parsing exactly as existing `Map()` fields do; every new field nullable; a per-slide array shorter than `BackgroundPattern` pads with null rather than erroring; a missing field is never an error |
| The per-slide transparency array can disagree in length with `BackgroundPattern` | Medium | Define length reconciliation explicitly in the spec (index-aligned, missing index ⇒ null ⇒ current `object-fit:cover`); mirror `BackgroundPatternReconciler`'s tolerance, do not throw |
| Caption/overline has no consumer today, so extracting it could ship a field nothing renders | Medium | WU-2 includes the render half (`CompositionTypography` caption members + both markup builders); the role is not merged extraction-only |
| `AllowedFonts` change could starve the designer if a client's classes resolve to a single family | Low | Client classes supplement rather than erase the brand-kit list when the client set is empty or degenerate |
| A visible degrade badge could read as "broken output" and increase operator discard rate | Low | Informational, collapsed/subtle styling in the spirit of the existing `InferredFieldsDisclosure`, not an error state |

## Rollback Plan

- **Fully additive at the schema level.** New extraction fields are nullable and new envelope/typography
  members are init-defaulted; existing persisted profiles and existing `composition_spec_json` rows read back
  as null/false and take the current code path. **No DB migration in this change**, so no data rollback
  exists to perform.
- WU-1a reverts on its own: trigger A disappears and trigger B restores the previous `rgba(0,0,0,0.52)`
  scrim card. Because the untouched `mappedFlatKind == FlatColor` short-circuit is upstream of both, reverting
  cannot regress the already-correct flat path. WU-1b (the envelope marker) is independently revertible —
  removing it just stops the marker from being written; WU-1a's routing is unaffected.
- WU-4 (frontend) is revertible **independently and in either order** relative to WU-1a/1b: an absent marker
  renders no badge, and a present marker with no frontend reader is simply ignored by `parseEnvelopes`.
- WU-2b's `AllowedFonts` change is the one behavior-visible modification to a *succeeding* path; it is
  isolated to one assignment in `AiVisualDesigner.BuildInputJson` and revertible independently of WU-2a
  (extraction). The `CompositionTypography` caption members are init-defaulted, so reverting WU-2a alone
  leaves every construction site compiling and rendering as today.
- WU-3a and WU-3b revert independently: the render treatment (3b) is conditional on a signal that is null for
  every existing client, and the schema (3a) is inert without a consumer.
- WU-0 is documentation and carries no runtime risk.

## Success Criteria

- [ ] A Photo-classified slide whose image generation fails renders through the deterministic palette-backed
      flat treatment, with no `AiVisualDesigner` call for that slide (asserted with a forced-failure fixture).
- [ ] A ClientAesthetic carousel slide whose AI designer returns no usable background renders through the
      same deterministic flat treatment, and **no** `rgba(0,0,0,0.52)` scrim card appears anywhere in its
      markup.
- [ ] A non-ClientAesthetic job whose designer fails still receives the legacy scrim card (no collateral
      behavior change).
- [ ] A slide the client's `BackgroundPattern` already calls `FlatColor` takes the pre-existing short-circuit
      and never reaches either new trigger.
- [ ] No dark scrim, card overlay, gradient, blob or glow appears on a degraded slide; every colour in the
      output resolves to the client's extracted `ColorPairings` / palette.
- [ ] Every degraded slide carries the degrade marker on its composition envelope, and a non-degraded slide's
      envelope is byte-identical to today's.
- [ ] Content Studio shows a per-slide degrade indicator for exactly those slides, and the marker survives a
      Studio edit + autosave round-trip.
- [ ] The extractor returns heading, body **and** caption/overline typography for a client whose feed shows
      distinguishable type, and all three reach `spec.Typography` and the rendered markup.
- [ ] `AiVisualDesigner.AllowedFonts` reflects client-derived font classes for a ClientAesthetic job, and a
      job whose client has no typography signal behaves exactly as before.
- [ ] The per-slide transparency signal round-trips extractor → persistence → profile → design context, is
      applied per `specIndex`, and a client with a signal on slide 2 only renders the treatment on slide 2
      while every other slide keeps unchanged `object-fit:cover`.
- [ ] A transparency array shorter or longer than `BackgroundPattern` is reconciled without throwing.
- [ ] Reprocessing the Lucia Fernandez reference job produces output free of the reported defects.
- [ ] The ComfyUI findings report is committed with the change and states explicitly that no code fix is
      recommended.
- [ ] All MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`); Strict TDD followed; architecture gate
      green (no provider or EF type crossing into Domain/Application).
