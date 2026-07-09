# Proposal: Store background-only PNG as asset reference (not base64) in composition spec

## Intent

`CompositionSpecEnvelope.BackgroundImageRef` currently holds a full base64 `data:image/png;base64,...` (the background-only PNG) per slide. It is serialized into `content_generation_format_results.composition_spec_json` and copied verbatim into `content_studio_items.draft_visual_spec_markup`, bloating BOTH tables. The field was always intended to be a storage reference (`asset-hash://...`); the data-URI is a stopgap. Replace it with a lightweight storage path so new generations stop bloating the DB. Forward-only — no backfill.

## Scope

### In Scope (Slice 1 — stop the bloat)
- Upload the background-only PNG to storage via the existing `IGeneratedAssetStorage` port (mirroring the full-slide asset upload in `ContentGenerationJobProcessor`), producing a `slide_N_bg.png`-style path.
- Set `CompositionSpecEnvelope.BackgroundImageRef` to that storage path instead of the base64 data-URI.
- Expose the background PNG through the existing `/items/{itemId}/assets/{assetIndex}` endpoint (its own addressable asset index).
- Keep PPTX export working with back-compat: `PptxExportAdapter` must accept BOTH a data-URI AND a storage path in `BackgroundImageRef`.

### Out of Scope (Non-goals)
- Backfill/migration of existing base64 rows — existing rows regenerate over time.
- Frontend fetching the background by URL — **Slice 2** follow-up.
- Any PPTX visual redesign.
- EF migration — `composition_spec_json` is an additive JSON blob.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- None (implementation-level change; envelope JSON is additive, no spec-level behavior change). If spec phase disagrees, the closest existing capability is the content-studio/content-generation composition envelope contract.

## Approach

At generation time the processor already captures the background PNG (`capturedBackgroundDataUri`). Instead of embedding it into the envelope, upload it through `IGeneratedAssetStorage` and assign `BackgroundImageRef` the returned storage path. Register the background as an addressable asset so `GetContentItemAssetQuery` (indexed by position in `item.Draft.Assets`) can serve it. PPTX export resolves `BackgroundImageRef` by detecting `data:` prefix (inline decode, legacy) vs storage path (open via port). No Domain/Application leak: all storage access stays behind the port.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` | Modified | Upload bg PNG via port; set `BackgroundImageRef` = path |
| `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/CompositionSpecEnvelope.cs` | Modified | Field now carries a storage ref (doc/semantics) |
| `Incoders.Hive.Application/Features/Social/ContentStudio/Materialize/MaterializeRunCommand.cs` | Verify | Copies envelope verbatim — confirm bg asset materializes as DraftAsset |
| `Incoders.Hive.Infrastructure/Rendering/PptxExportAdapter.cs` | Modified | Back-compat resolver: data-URI OR storage path |
| `Incoders.Hive.ApiService/Endpoints/Social/MapContentStudioEndpoints.cs` + `GetContentItemAssetQuery.cs` | Verify/Modified | Serve bg via existing asset endpoint/index |
| `Incoders.Hive.Tests/**` (Pptx, Processor bg, Storage) | Modified | Cover both data-URI and path paths (strict TDD) |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| PPTX export regression | Med | Back-compat resolver + tests for both data-URI and path |
| Asset-index contract change confuses frontend | Med | Slice 1 keeps envelope readable; frontend URL-fetch deferred to Slice 2 |
| Back-compat gap (legacy rows) | Med | Consumers accept both forms; forward-only, no row rewrite |
| Cannot run `dotnet test` locally (SAC / WDAC) | High | Verify via CI dispatch, not local runs |

## Rollback Plan

Revert the processor/adapter changes. Since it is forward-only and additive, legacy base64 envelopes keep working through the same back-compat resolver; no data rewrite to undo.

## Dependencies

- Existing `IGeneratedAssetStorage` port and `/items/{itemId}/assets/{assetIndex}` endpoint.

## Success Criteria

- [ ] New generations store a storage path (not base64) in `BackgroundImageRef`.
- [ ] `composition_spec_json` and `draft_visual_spec_markup` no longer grow by the background PNG size.
- [ ] PPTX export renders correctly for both legacy (data-URI) and new (path) envelopes.
- [ ] Background PNG is servable via the existing asset endpoint.

## Open Questions for Design

1. Exact asset-index numbering: is the background appended after the full-slide asset per slide, or interleaved? Frontend contract (Slice 2) depends on deterministic ordering.
2. Is the background registered as a `ContentGenerationFormatAsset` row, a studio `DraftAsset`, or both? `GetContentItemAssetQuery` reads `item.Draft.Assets`, so materialization must add it there.
3. Storage path/naming convention for the bg asset (e.g. `slide_N_bg.png`) and whether it reuses the full-slide asset's storage scope/container.

## Proposal question round

Product/scope decisions were pre-locked by the orchestrator (forward-only, two slices, no migration, back-compat mandatory). No open product questions block this proposal; the remaining unknowns are design-level (see Open Questions).
