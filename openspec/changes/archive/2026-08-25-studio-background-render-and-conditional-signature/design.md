# Design: Studio background rendering (Slice 2, #58) + conditional @handle signature (#70)

Two independent slices. A is a read/expose path (no new deliverable); B is a trigger-gating change to a deterministic composer. Refs: #58, #70.

## Technical Approach

- **A** — the background PNG already exists per slide as `CompositionSpecEnvelope.BackgroundImageRef` (storage path, or legacy `data:` URI), materialized into the studio item via `MaterializeRunCommand` (`ContentDraft(text, result.CompositionSpecJson, assets, …)` — the envelopes JSON lands in `item.Draft.VisualSpecMarkup`, `MaterializeRunCommand.cs:81-85`). Expose it through a **dedicated, slide-scoped studio endpoint** that resolves `BackgroundImageRef` on demand — mirroring `PptxExportAdapter.ResolvePictureBytesAsync` (`PptxExportAdapter.cs:106-114`: `data:` decoded inline, storage path opened via `IGeneratedAssetStorage`). It never touches `content_generation_format_assets` nor `item.Draft.Assets`, so ZIP/listing/Canva/delete are untouched by construction.
- **B** — change the *trigger*, not the resolver. `AttributionPolicy.ResolveSignature` (`AttributionPolicy.cs:20`) still decides *what* the text is; a new `AttributionPolicy.ShouldSign(brand)` predicate decides *whether* to populate the slot. Both `CarouselVisualComposer` (`:150-157`) and `StoryVisualComposer` (`:34-38`) route through it. Signal source (first cut): an explicit nullable client field, default off.

## Architecture Decisions

### Decision A — Background exposure mechanism

| Option | Tradeoff | Verdict |
|---|---|---|
| **Dedicated slide-scoped endpoint** `GET /items/{itemId}/slides/{n}/background` — parses `item.Draft` composition envelopes, resolves `envelope[n].BackgroundImageRef` via a new `GetContentItemSlideBackgroundQuery` (mirrors `GetContentItemAssetQuery` + PPTX data-URI branch) | New endpoint + query + FE helper. Zero draft mutation, zero index arithmetic, structurally matches N-envelopes-per-item, provably outside the `FormatAsset` index space | **Chosen** |
| Synthesized studio-only `DraftAsset` at materialize, served via existing `/assets/{assetIndex}` | Reuses the asset endpoint, but MUTATES persisted `item.Draft.Assets` (N backgrounds appended → slide→index arithmetic, idempotency risk on re-materialize, pollutes the list `EditVisualCommand` round-trips) | Rejected |

**Rationale**: a carousel item is ONE `ContentItem` carrying N slide envelopes, each with its own `BackgroundImageRef`. Interleaving N synthesized assets into `Draft.Assets` is exactly the "asset-index ordering confuses the frontend" risk. A slide-scoped endpoint addresses `(itemId, slideIndex)` directly, needs no draft write, and cannot leak into any deliverable enumeration. Back-compat: the resolver branches on `data:` prefix exactly like PPTX; a blank/failed ref returns 404 and the layer is simply absent (graceful, BGREF-04 parity).

### Decision B — Signing signal + field

| Aspect | Choice | Rationale |
|---|---|---|
| Signal source (first cut) | Explicit client field `SignsOwnSlides` (`bool?`) on the `Client` entity; profile-analysis auto-detection is an explicit **follow-up** | Smallest correct cut. Mirrors the proven `BackgroundSourcingPolicy` additive-nullable-client-field pattern (entity + `smallint`/`bool` column + ABM Create/Update/Response + snapshot loader). Detection is noisy (proposal risk) and needs its own analysis-pipeline work. |
| Storage | `social.clients.signs_own_slides boolean NULL`; `ClientProfileSnapshot.SignsOwnSlides` loaded in `EfClientBrandSnapshotLoader` (`:107-114`) | Same load site as `Handle`/`BackgroundSourcingPolicy`. |
| Default / migration | Column added NULL, **no backfill**. `null` ⇒ not signing | Maintainer's rule is "sign IFF client signs". Existing handled clients flip **off** — deliberate and documented (see Migration). |
| Precedence (future both) | Tri-state `bool?`: `true`=sign, `false`=never, `null`=defer to detected signal | Explicit client opt-out always wins over detection; keeps the field forward-compatible when detection lands. |
| Gate placement | New `AttributionPolicy.ShouldSign(BrandSnapshot)`; both composers replace their `Handle is not null` guard with it | Centralizes the decision so Carousel/Story cannot drift; `ResolveSignature`/prohibited-token/brand-notes logic is untouched. |

`ShouldSign` = `Handle` present (still **necessary**) AND (`SignsOwnSlides == true` OR (`SignsOwnSlides == null` AND detectedSignsSignal)). First cut: detection absent ⇒ predicate collapses to `Handle present AND SignsOwnSlides == true`.

## Data Flow

**A** (render):
```
content-studio-panel SlideThumbnail(slideIndex)
   └─ GET /items/{itemId}/slides/{n}/background
        └─ GetContentItemSlideBackgroundQuery → parse item.Draft envelopes
             → envelope[n].BackgroundImageRef
               ├─ "data:…"      → decode inline → stream
               └─ storage path  → IGeneratedAssetStorage.OpenAsync → stream
   (rendered BEHIND the sanitized markup/text layer)
```
`content_generation_format_assets` / `Draft.Assets` / ZIP / Canva / delete: **not read, not written**.

**B** (sign):
```
Client.SignsOwnSlides ─(EfClientBrandSnapshotLoader)→ ClientProfileSnapshot.SignsOwnSlides
   → BrandSnapshot.Client → AttributionPolicy.ShouldSign(brand)
        false → @handle / signature slot OMITTED (as today when Handle null)
        true  → ResolveSignature(brand,…) → slot populated  [Carousel + Story identical]
```

## Affected Files

| File | Action | Purpose |
|---|---|---|
| **A** `.../ContentStudio/Queries/GetContentItemSlideBackgroundQuery.cs` | Create | Parse item envelopes, resolve `BackgroundImageRef` (data-URI + storage), stream bytes |
| **A** `ApiService/Endpoints/Social/MapContentStudioEndpoints.cs` | Modify | Map `GET /items/{itemId}/slides/{n:int}/background` |
| **A** `apps/web/lib/api/content-generation.ts` (+ `types.ts`) | Modify | `fetchSlideBackground(itemId, slideIndex, token)` helper |
| **A** `apps/web/components/social/content-studio-panel.tsx` (`SlideThumbnail` ~1680, `AssetFallbackImage` ~1740) | Modify | Render AI background layer behind markup/text per slide |
| **A** `.../Pipeline/CompositionSpecEnvelope.cs` | Read | Source of `BackgroundImageRef` + `CompositionSpecJson.ParseActiveList` |
| **B** `Domain/Social/Clients/Client.cs` | Modify | `SignsOwnSlides bool?` + `SetSignsOwnSlides(bool?, nowUtc)` |
| **B** `Infrastructure/Persistence/Postgres/Entities/SocialClientData.cs` (+ EF config + snapshot) | Modify | `bool? SignsOwnSlides` column mapping |
| **B** `.../Migrations/*_Add_SignsOwnSlides.cs` | Create | `signs_own_slides boolean NULL` on `social.clients`, no backfill |
| **B** `Domain/Social/ContentGeneration/ClientProfileSnapshot.cs` | Modify | Add `bool? SignsOwnSlides = null` (additive) |
| **B** `Infrastructure/.../EfClientBrandSnapshotLoader.cs` (`:107-114`) | Modify | Load `clientRow.SignsOwnSlides` into snapshot |
| **B** `.../Shared/AttributionPolicy.cs` | Modify | Add `ShouldSign(BrandSnapshot)` |
| **B** `.../Visual/Composers/CarouselVisualComposer.cs` (`:150-157`) | Modify | Guard `@handle` on `ShouldSign` |
| **B** `.../Visual/Composers/StoryVisualComposer.cs` (`:34-38`) | Modify | Guard `signature` on `ShouldSign` |
| **B** `.../Clients/{Create,Update}/*` + `SocialClientResponse.cs` + `MapSocialClientsEndpoints.cs` | Modify | ABM read/write of the field (mirror `BackgroundSourcingPolicy`) |
| **B** `apps/web` client ABM form + client types | Modify | Toggle "Client signs their slides" (default off) |
| Tests (`CarouselVisualComposerHandleSlotTests`, story composer, snapshot loader, new slide-background query test) | Modify/Add | Gated on/off signing; background resolves w/o a `FormatAssetRow` |

## ZIP Non-Regression Guard (A)

The background is served ONLY by `GetContentItemSlideBackgroundQuery`, which reads envelopes and `IGeneratedAssetStorage`. It writes NO `content_generation_format_assets` row and adds NO `ContentAssetRef` to `Draft.Assets`. Test: after rendering backgrounds, assert package-assembly (ZIP), listing, Canva export, and delete/regenerate deliverable counts are byte-for-byte unchanged vs. baseline.

## Failure / Edge Cases

| Case | Behavior |
|---|---|
| `BackgroundImageRef` null/blank | 404 → FE renders no background layer (graceful) |
| Legacy `data:` URI ref | Decoded inline (PPTX parity); FE may also short-circuit `data:` directly |
| Storage open fails | Query failure → 404, layer absent, thumbnail still renders markup |
| `slideIndex` out of range / tombstoned | 404 (`ParseActiveList` skips tombstones) |
| Client has `Handle` but `SignsOwnSlides != true` | `@handle`/`signature` slot omitted (Carousel + Story consistent) |
| `SignsOwnSlides == true` but `Handle` null | Slot omitted — `Handle` remains necessary |
| Existing signed clients post-migration | `null` ⇒ unsigned; documented intentional flip |

## Migration / Rollout

- Additive `signs_own_slides boolean NULL`, **no backfill** — mirrors `Add_BackgroundSourcingPolicy`. Down = drop column.
- Behavior change is deliberate per maintainer "sign if and only if": previously auto-signed handled clients render unsigned until they opt in via ABM. Document in release notes.
- A is purely additive (new endpoint + FE layer); no migration, no flag needed.
- `dotnet test` is SAC-blocked locally — verify via CI dispatch (project memory).

## Residual Risks

- **B silent flip**: opt-out-by-default silences currently-signed clients until they re-enable. Accepted and documented; the ABM toggle is the remediation path.
- **B detection deferred**: `null` tri-state reserves precedence, but until detection ships the only "on" path is the explicit field. Follow-up must fill the `SignsOwnSlides == null AND detected` branch.
- **A envelope source**: relies on `item.Draft` still carrying the composition envelopes at query time; if `EditVisual` overwrites `VisualSpecMarkup` with edited markup only, background resolution could regress — verify the envelope JSON survives edits or resolve from the source `composition_spec_json` instead.

## Open Questions

- [ ] Confirm `item.Draft.VisualSpecMarkup` retains the full `composition_spec_json` after a visual edit, or resolve backgrounds from the immutable format-result spec.
- [ ] ABM UI copy/placement for the "Client signs their slides" toggle (product wording).
