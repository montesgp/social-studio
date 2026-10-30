# Proposal: Studio background rendering (Slice 2) + conditional @handle signature

Tracking issues: #58, #70

This change bundles two independent capabilities the maintainer requested together. They ship as two clearly-separated slices and can be planned/implemented without coupling.

- **Capability A (#58)** — Content Studio renders each slide's AI background by URL/reference (Slice 2 of the background-asset work).
- **Capability B (#70)** — Sign slides with the client's `@handle` only when the client actually signs, not merely because an own-account handle exists.

## Intent

### Capability A — render the slide background by URL (#58)

AI background images are already generated and persisted as storage files (`.../feed-carousel/slide_N_bg.png`) and referenced per slide via `CompositionSpecEnvelope.BackgroundImageRef` (a storage **path**, shipped in #56 / Slice 1). But the background is **not exposed as an addressable asset** and the frontend **never fetches or renders it by URL**. The `content_generation_format_assets` rows are the composed slides (`slide_N.png`, asset_index 1..N) served through `AssetFallbackImage` by asset index (`apps/web/components/social/content-studio-panel.tsx:1740`); `BackgroundImageRef` is not among them. Result: the studio/package preview shows the slide layout and text but the AI photo background is **missing**.

Success looks like: every slide in the studio/package preview renders its AI background by URL/reference, resolved from `BackgroundImageRef`, without introducing an extra deliverable into the ZIP/export/listing surfaces.

### Capability B — conditional signature (#70)

`CarouselVisualComposer.cs:150-157` populates the `@handle` copy slot whenever `brand.Client?.Handle is not null` (via `AttributionPolicy.ResolveSignature`, `.../Shared/AttributionPolicy.cs:20`, CAC-CG-03). An own-account handle is **also required for aesthetic profile analysis**, so every analyzed client automatically gets signed — including clients who do **not** sign their real slides. The AI designer prompt already avoids adding a handle unless directed (`AiVisualDesigner.cs:401-403`); it is the **deterministic composer** that forces the signature.

Success looks like: the `@handle` slot is populated **only** when the client actually signs — detected by profile analysis, and/or explicitly opted-in — never merely because an own-account handle is registered. Story and Carousel composers stay consistent through `AttributionPolicy`.

## Scope

### In Scope

**Capability A — content-studio background rendering**
- Expose each slide's background (from `BackgroundImageRef`) so the studio/package preview can render it by URL/reference.
- Render the AI background behind the slide layout/text in the studio and package preview.
- Support both `BackgroundImageRef` forms carried today: a storage path (new, Slice 1) and — for back-compat — a legacy `data:` URI, resolved the same way PPTX export already handles both.

**Capability B — content-generation attribution/signature gating**
- Gate the `@handle` signature behind an explicit "client signs their slides" condition: (1) a profile-analysis detected signal that the client signs, and/or (2) an explicit client opt-in field/preference.
- Apply the gate in the deterministic composers (`CarouselVisualComposer`, `StoryVisualComposer`) via `AttributionPolicy`, keeping both composers consistent.
- When the gate is not satisfied, omit the `@handle` slot entirely (the slide already renders correctly without it when `Handle` is null).
- Provide a default/migration decision for existing clients that currently get signed, chosen to preserve intent (see Risks).

### Out of Scope (Non-goals)

- **A:** Registering the background as a `content_generation_format_assets` row (`FormatAssetRow`) — explicitly forbidden (see Risks). No ZIP/export/listing behavior change; the background must not become an extra deliverable.
- **A:** Re-generating or re-uploading backgrounds; changing the generation pipeline or `BackgroundImageRef` production (that was Slice 1). Slice 2 only exposes and renders what exists.
- **A:** PPTX visual redesign or changing how PPTX export consumes the background.
- **B:** Changing own-account (ABM) configuration flows, or the aesthetic-analysis pipeline beyond adding/reading the "client signs" signal.
- **B:** Changing the AI-designer prompt signing behavior (already correct).
- **B:** Redefining `AttributionPolicy`'s prohibited-token / source-origin guardrails — only the *trigger condition* for populating the slot changes.
- Any change to non-carousel/non-story formats.

## Capabilities

### Modified Capabilities

- `content-studio-background-rendering` (A): the studio/package preview resolves and renders each slide's AI background from `BackgroundImageRef` by URL/reference. Previously the background existed only as a storage path with no addressable exposure and no frontend fetch.
- `content-generation-attribution` (B): the deterministic `@handle` signature is gated on an explicit "client signs" condition (detected signal and/or opt-in) rather than on mere presence of an own-account handle. Story and Carousel remain consistent through `AttributionPolicy`.

## Approach

### Capability A

The background PNG already exists in storage and is referenced per slide by `BackgroundImageRef`. Two candidate mechanisms exist; the choice is a **design decision**:

1. **Studio-only synthesized `DraftAsset` at materialize** — when materializing the run, synthesize a studio-scoped background asset from `BackgroundImageRef` so the existing `/items/{itemId}/assets/{assetIndex}` path (which reads `item.Draft.Assets`) can serve it and the frontend renders it as a background layer. Because it lives only in the studio draft, it never enters `content_generation_format_assets`, so ZIP/export/listing are untouched.
2. **Dedicated background endpoint** — add a studio-only endpoint that resolves and streams the background for a given item/slide directly from `BackgroundImageRef`, and have the frontend render it as the background layer.

Either way, the background is served through the port (`IGeneratedAssetStorage` / existing asset resolution) with no storage-type leak into Domain/Application, resolving both a storage path and a legacy `data:` URI. The frontend renders the AI background **behind** the composed markup/text layer in the studio thumbnail and package preview.

### Capability B

Change the *trigger*, not the resolver. Today `CarouselVisualComposer` writes `copySlots["@handle"]` iff `brand.Client?.Handle is not null`. Replace that guard with an explicit "client signs" predicate sourced from:

1. an **explicit client field/preference** (opt-in to signing), and/or
2. a **profile-analysis detected signal** that the client signs their own slides (a new signal on the aesthetic/profile snapshot).

The choice of *client field vs profile-analysis signal vs both* is an **open design decision**. Presence of a `Handle` remains **necessary** (there is nothing to sign with otherwise) but is no longer **sufficient**. `AttributionPolicy` continues to resolve *what* the signature text is; the composers decide *whether* to populate the slot, applied identically in `StoryVisualComposer` and `CarouselVisualComposer` so behavior stays consistent. When the gate is off, the slot is omitted exactly as it is today when `Handle` is null.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| **A** `apps/web/components/social/content-studio-panel.tsx` (~1700-1779, `AssetFallbackImage`) | Modified | Render each slide's AI background by URL/reference behind the layout/text layer |
| **A** `apps/web/lib/api/content-generation.ts` (+ `types.ts`) | Modified (likely) | Fetch/resolve the background reference for a slide |
| **A** `SocialStudio.Application/.../ContentStudio/Materialize/MaterializeRunCommand.cs` | Modified (option 1) | Synthesize a studio-only `DraftAsset` from `BackgroundImageRef` |
| **A** `SocialStudio.ApiService/Endpoints/Social/MapContentStudioEndpoints.cs` (+ asset query) | Modified (option 1 or 2) | Serve the background via existing asset index OR a dedicated background endpoint |
| **A** `SocialStudio.Application/.../Pipeline/CompositionSpecEnvelope.cs` | Read/verify | Source of `BackgroundImageRef` (path or legacy data-URI) |
| **B** `SocialStudio.Application/.../Visual/Composers/CarouselVisualComposer.cs` (140-157) | Modified | Replace `Handle is not null` guard with the "client signs" predicate |
| **B** `SocialStudio.Application/.../Visual/Composers/StoryVisualComposer.cs` | Modified | Apply the same gate for consistency |
| **B** `SocialStudio.Application/.../Shared/AttributionPolicy.cs` (20) | Modified (possible) | Expose/centralize the "should sign" decision so both composers stay consistent |
| **B** Client/aesthetic snapshot model (`BrandSnapshot.Client` / profile-analysis signal) | Modified | Carry the opt-in field and/or the detected "client signs" signal |
| **B** Aesthetic profile-analysis pipeline | Modified (if signal chosen) | Detect and persist the "client signs their slides" signal |
| Tests (`SurfaceScopedCarouselVisualComposerTests`, studio/asset tests, strict TDD) | Modified | Cover: gated on/off signing; background renders without a `FormatAssetRow` |

## Risks

| Risk | Capability | Likelihood | Mitigation |
|------|------------|------------|------------|
| **HARD CONSTRAINT:** registering the background as a `content_generation_format_assets` row would double ZIP deliverables — the table is enumerated by package-assembly (ZIP), listing, Canva export, and delete/regenerate handlers **without a kind filter** | A | High | MUST NOT add a `FormatAssetRow`. Use a studio-only synthesized `DraftAsset` at materialize OR a dedicated background endpoint; assert in tests that ZIP/export/listing deliverable counts are unchanged |
| Changing signing behavior for existing clients with a registered handle — they are currently signed and would suddenly stop (or vice-versa) | B | High | Choose a default/migration that **preserves intent**: decide whether existing handled clients default to opted-in or opted-out, document it, and back it with a migration/default so the change is deliberate, not silent |
| Asset-index / ordering contract confuses the frontend if background is interleaved with slide assets | A | Med | Keep the background out of the `FormatAsset` index space; expose it via a distinct, deterministic reference (synthesized DraftAsset index or dedicated endpoint) |
| Legacy `BackgroundImageRef` as `data:` URI vs storage path | A | Med | Resolve both forms, mirroring the existing PPTX back-compat resolver |
| Story/Carousel drift if the gate is applied in only one composer | B | Med | Centralize the "should sign" decision in `AttributionPolicy` and apply in both composers; cover both in tests |
| Profile-analysis "client signs" signal may be noisy / false-negative | B | Med | Combine with an explicit opt-in field; let opt-in override the detected signal (final rule is a design decision) |
| Cannot run `dotnet test` locally (SAC / WDAC) | Both | High | Verify via CI dispatch, not local runs |

## Success Criteria

**Capability A (#58)**
- [ ] Each slide in the studio/package preview renders its AI background by URL/reference resolved from `BackgroundImageRef`.
- [ ] The background is served through the port for both a storage path and a legacy `data:` URI.
- [ ] **No** `content_generation_format_assets` row is added for the background; ZIP, listing, Canva export, and delete/regenerate deliverable counts are unchanged.

**Capability B (#70)**
- [ ] The `@handle` slot is populated **only** when the client signs — via profile-analysis detected signal and/or explicit opt-in — never merely because an own-account handle exists.
- [ ] A client with a registered handle who does **not** sign renders slides with the `@handle` slot omitted.
- [ ] `StoryVisualComposer` and `CarouselVisualComposer` produce consistent signing decisions through `AttributionPolicy`.
- [ ] Existing signed clients get a documented, intent-preserving default/migration (no silent behavior flip).

## Open Design Decisions

**Capability A**
1. Mechanism: studio-only synthesized `DraftAsset` at materialize **vs** a dedicated background endpoint.
2. How the frontend addresses the background per slide (asset index vs slide-scoped reference) given the background must stay out of the `FormatAsset` index space.

**Capability B**
3. Trigger source: explicit client opt-in field **vs** profile-analysis detected signal **vs** both (and precedence if both).
4. Where the "client signs" signal lives on the model (client entity/preference vs aesthetic/profile snapshot) and how it reaches `BrandSnapshot`.
5. Default/migration for existing handled clients (opt-in vs opt-out by default) that preserves current intent.
