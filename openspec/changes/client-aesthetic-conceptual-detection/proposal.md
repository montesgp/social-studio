# Proposal: Client Aesthetic Conceptual Detection (Art-Direction Method + Concept Ideation)

## Change ID
`client-aesthetic-conceptual-detection`

## Status
Proposal — DRAFT for owner review. Builds on `client-aligned-carousel-generation` (which added `slideMixPattern`/`BackgroundPattern`/`TypicalSlideCount`/`ColorPairing`) and the Phase 2 aesthetic surface (`ClientAestheticProfile`, `MultimodalAestheticStyleExtractor`, archived reference images). Specs/design: TBD after approval.

## Intent / Problem

Incoders Hive promises carousel content "in each client's visual style". For metaphor-driven clients this promise is NOT kept today, and the output looks flat and soulless.

Root cause: the analyzer (`MultimodalAestheticStyleExtractor`) already SEES the client's posts, but its instruction + response schema (`AestheticExtractionDto`: palette, styleDescriptors, motifs, captionVoice, slideMixPattern) only capture the VISUAL SURFACE. It does NOT capture the client's CONCEPTUAL / ART-DIRECTION METHOD — HOW the client turns a message into an image (metaphor vs literal vs symbolic still-life; the image↔message relationship) — nor a robust image-type composition ratio. Two concrete gaps compound this:

- **Captions are passed as a loose block**, not paired to their images, so the model cannot learn the metaphor mapping (which message produced which image).
- **`ImagePromptWriter` does no conceptual ideation** — it only asks for "a text-free background in the client's style", so a new topic never gets a metaphor invented in the client's method.

Net effect: for a client like Atlas (@atlas.editorial) — psychology content where each image is a visual METAPHOR of the post (honey heart with ants, woman at a train window, head wrapped in FRAGILE tape; saturated mono-color backgrounds, conceptual still-life + staged photography, editorial-advertising treatment) — generation produces generic imagery indistinguishable from an illustration client like Comet Comedy (warm cream watercolor, hand-drawn children, pastel decorative icons). The same generic detection flattens two very different creative methods into one.

## Confirmed Product Direction (definition of done)

Arrive at the image generator with a fully DIGESTED, ENRICHED instruction — concept already decided in the client's method — instead of a thin surface description. Concretely:

1. **Detect the conceptual / art-direction METHOD**, not just the look — the image↔message creative relationship (e.g. Atlas = idea-driven visual metaphors via mono-color still-life + staged photography; Comet = literal tender watercolor illustration of children).
2. **Detect a robust image-type composition ratio** (e.g. Atlas ≈ 50% photo / 50% flat-color slides) that generation must mirror, robustifying the existing `slideMixPattern`/`BackgroundPattern` foundation.
3. **Learn from image↔caption PAIRS**, so the analyzer can infer the metaphor mapping (message → visual).
4. **Concept IDEATION at prompt-writing time** — `ImagePromptWriter` invents the visual metaphor in the client's detected method for the NEW topic, while respecting the detected photo/flat ratio.
5. **Use a real VISION model for the `ai-aesthetic` step.** Profile analysis is INFREQUENT (once per client, cached, refreshed every N days), so a quality paid vision model is cheap in aggregate; image GENERATION is frequent and routes to a free local provider (separate change). `claude -p` is unsuitable here — it is the Claude Code agent CLI: it converses/refuses and does not honor JSON contracts.

## Scope

### In Scope (digested-instruction slice)
- **Enrich detection (conceptual method + ratio).** Add an art-direction/conceptual-method signal and a robust image-type ratio/frequency to the multimodal extractor's instruction + `AestheticExtractionDto`, distinguishing metaphor/editorial from literal/illustration methods.
- **Image↔caption pairing input.** Feed the analyzer ordered image+caption PAIRS instead of a loose caption block, so it learns the message→image method.
- **Carry the new fields through the model.** Additive, nullable fields on `AestheticExtraction` → `ClientAestheticProfile` → `AestheticProfilePersistence` (same back-compat pattern the prior change used). Cold-start parity is mandatory.
- **Concept-ideation prompt-writing.** Upgrade `ImagePromptWriter` to ideate the visual metaphor in the detected method and to honor the detected photo/flat ratio (connecting to the existing `BackgroundPattern`/`slideMixPattern` foundation).
- **Real vision model on `ai-aesthetic`.** Ensure the `extract-aesthetic` / `ai-aesthetic` step routes to a genuine vision model; this is the ONLY routing change.

### Out of Scope (deferred / separate change)
- **The ComfyUI local image-generation provider/connector** — that is the SEPARATE `comfyui` provider change. THIS change stops at producing the digested, enriched instruction + profile; it does NOT implement the image backend.
- **Text-route provider wiring** beyond the `ai-aesthetic` analysis step. No other provider routing changes.
- **Persisting an editable human-curated art-direction field UI** — AI proposes the field here; human-refinement UI is a later refinement.
- **Story formats** — feed carousel/single only.

### Scope Boundaries
- Hexagonal: Domain ← Application ← Infrastructure/Api. No provider/EF/Supabase/ASP.NET types in Domain/Application. The extractor stays Infrastructure; the new profile fields are pure Application value objects.
- All new fields ADDITIVE + nullable. Cold-start (no conceptual data) MUST produce output identical to today (parity), per the architecture gate and `TreatWarningsAsErrors=true`.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities (additive deltas)
- `profile-analysis`: the multimodal aesthetic extraction gains a conceptual/art-direction method signal and a robust image-type composition ratio; input becomes ordered image↔caption pairs.
- `content-generation`: `ImagePromptWriter` performs concept ideation using the detected method and honors the detected photo/flat ratio.
- `image-generation`: the per-slide image prompt becomes a digested, concept-decided instruction (still text-free background; ratio-aware) rather than a thin style descriptor.

## Approach

Two coordinated tracks, both additive and back-compatible.

**Detection track (Infrastructure + Application model).** Extend the extractor's instruction to ask for (a) the client's conceptual/art-direction method — the image↔message relationship — and (b) a robust image-type ratio/frequency. Add the matching nullable fields to `AestheticExtractionDto`, map them onto `AestheticExtraction`, and carry them through `ClientAestheticProfile` + `AestheticProfilePersistence` as additive trailing-nullable fields. Replace the loose caption block with ordered image↔caption pairs so the model can learn the metaphor mapping. Robustify the existing `slideMixPattern` → `BackgroundPattern` ratio so it is statistically grounded, not a single-post guess.

**Ideation track (prompt-writing).** Upgrade `ImagePromptWriter.BuildInput` to pass the detected conceptual method and instruct the text model to INVENT the visual metaphor for the new topic in that method (Atlas → a fresh mono-color conceptual still-life/staged metaphor of the slide message; Comet → a tender literal watercolor scene), while keeping the background text-free and respecting the detected photo/flat ratio. Generation thus arrives pre-decided on concept.

**Routing.** Point the `ai-aesthetic` step at a real vision model only; leave all other routing untouched.

## Affected Areas (intent level)

| Area | Layer | Impact | Description |
|------|-------|--------|-------------|
| `MultimodalAestheticStyleExtractor` (instruction + `AestheticExtractionDto`) | Infrastructure | Modified | Add conceptual/art-direction + robust ratio fields; send image↔caption pairs |
| `AestheticExtraction` | Application | Modified | Additive nullable conceptual-method + ratio fields |
| `ClientAestheticProfile` | Application | Modified | Additive nullable conceptual-method + ratio fields (back-compat) |
| `AestheticProfilePersistence` / `EfClientAestheticProfileReader` | Infrastructure | Modified | Persist + read the new additive fields |
| `IClientAestheticStyleExtractor` caption→pair input contract | Application | Modified | Image↔caption pairs instead of loose captions |
| `ImagePromptWriter` (`BuildInput` + instruction) | Application | Modified | Concept ideation in detected method; ratio-aware |
| `ai-aesthetic` / `extract-aesthetic` step routing | Infrastructure/Config | Verified | Point to a real vision model; no other routing change |
| Persistence migration for new aesthetic fields | Infrastructure | New | Additive nullable columns |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Conceptual-method detection is fuzzy / inconsistent across runs | Medium | Anchor on real client exemplars (Atlas vs Comet) in spec/tests; keep field free-text + nullable; AI proposes, human can refine later |
| Image↔caption pairing degrades when captions are missing/misaligned | Medium | Best-effort pairing; fall back to image-only when a caption is absent; never fail the extraction |
| New fields ripple through extractor + persistence | Medium | Additive trailing-nullable only; non-destructive migration; same pattern as `client-aligned-carousel-generation` |
| Cold-start parity broken (clients without conceptual data change output) | Medium | Null conceptual data MUST reproduce today's behavior; cover with parity tests (Strict TDD) |
| Vision model on `ai-aesthetic` unavailable / mis-routed | Low | Keep existing degrade-to-empty path (`AestheticExtraction.Empty`); routing verified end-to-end before relying on it |
| Concept ideation over-creates and drifts from the client's method | Medium | Constrain ideation to the detected method + ratio; keep background text-free; validate against existing guardrails |
| Provider SDK types leaking into Domain/Application | Medium | Keep provider concerns behind the existing gateway port; architecture gate |

## Rollback Plan
- All new aesthetic fields are additive + nullable; dropping them via a non-destructive migration reverts to today's surface-only detection and behavior.
- Image↔caption pairing falls back to image-only when captions are absent; reverting to the loose-block input is isolated to the extractor.
- The `ImagePromptWriter` ideation upgrade is gated on the conceptual-method field existing; with no field present the current text-free-background instruction runs unchanged.
- The `ai-aesthetic` routing change is config-only and reversible; the existing degrade-to-`Empty` path is unaffected.

## Dependencies
- `client-aligned-carousel-generation` — provides the `BackgroundPattern`/`slideMixPattern`/`TypicalSlideCount`/`ColorPairing` foundation this change robustifies and connects.
- Phase 2 aesthetic surface (`ClientAestheticProfile`, `MultimodalAestheticStyleExtractor`, archived reference images + captions) — the capture surface to enrich.
- A real, config-selected VISION model on the `ai-aesthetic` step.
- The SEPARATE `comfyui` local image-generation provider change — consumes the digested instruction this change produces, but is NOT required for this change to ship.
- Persistence migration for the additive aesthetic fields.

## Success Criteria
- [ ] The aesthetic extraction captures a conceptual/art-direction method signal that distinguishes Atlas (metaphorical editorial still-life/photography) from Comet Comedy (literal tender watercolor illustration).
- [ ] The extraction captures a robust image-type composition ratio that reflects Atlas's ~50% photo / ~50% flat-color mix.
- [ ] The analyzer receives ordered image↔caption pairs and can infer the message→image method (validated in spec/tests against the real exemplars).
- [ ] `ImagePromptWriter` produces a concept-ideated, method-faithful, ratio-aware, text-free background prompt for a NEW topic.
- [ ] The `ai-aesthetic` step runs on a real vision model; `claude -p` is not used for analysis.
- [ ] Cold-start parity: clients with no conceptual data produce output identical to today.
- [ ] No provider types leak into Domain/Application; additive nullable fields only; all MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`); Strict TDD — detection mapping, pairing, ideation, and parity tested before implementation.

## Open Questions (for spec/design)
- **OQ-1 — Conceptual-method representation.** Free-text descriptor vs. a small structured shape (method-type enum + rationale); how the extractor is prompted to produce it deterministically.
- **OQ-2 — Ratio shape + robustness.** Exact representation of the image-type ratio (percentage vs. per-index distribution) and how it is statistically grounded across multiple posts rather than one carousel.
- **OQ-3 — Image↔caption pairing contract.** Exact shape of the paired input and behavior when captions are missing, fewer than images, or misaligned.
- **OQ-4 — Persistence shape.** Column/JSON shape for the new conceptual + ratio fields; migration plan mirroring the prior change.
- **OQ-5 — Ideation constraints.** How strongly to constrain `ImagePromptWriter` ideation to the detected method/ratio to avoid drift, and how it reconciles with per-slide photo-vs-flat assignment.
- **OQ-6 — Vision-model selection.** Which vision model/provider the `ai-aesthetic` step should target, and how refresh cadence (every N days) interacts with cost.
