# Design: Background-only PNG as a storage-path asset reference

Replace the base64 data-URI in `CompositionSpecEnvelope.BackgroundImageRef` with a lightweight
storage path, upload the PNG through the existing `IGeneratedAssetStorage` port, and make PPTX export
resolve BOTH forms (legacy data-URI + new path). Forward-only, additive JSON, no EF migration, no
backfill, **no new `content_generation_format_assets` row**.

> Revision note (blast-radius correction): the first draft registered the background as a new
> `FormatAssetRow`. That was WRONG for Slice 1 — `content_generation_format_assets` is read/mutated by
> many consumers beyond `MaterializeRunCommand` (assembly ZIP, package listing counts, Canva export,
> delete/regenerate). A background row would double ZIP deliverables and corrupt counts. Slice 1 now
> stops the DB bloat ONLY; addressability/serving moves to Slice 2 with an explicit constraint. See
> "Consumer blast-radius" and "Slice 2".

## Quick path (Slice 1 — stop the bloat)

1. Processor decodes the captured background data-URI to raw bytes, uploads them via
   `IGeneratedAssetStorage.PutAssetAsync` (same `StoredAssetKey` scope, `*_bg.png` relativePath), and
   sets `BackgroundImageRef` to the returned storage path instead of the base64 string.
2. `PptxExportAdapter` gains an `IGeneratedAssetStorage` dependency and resolves `BackgroundImageRef`
   as data-URI (inline decode, legacy) OR storage path (open via port).
3. `CompositionSpecEnvelope` doc-only update for the new field semantics.
4. NOTHING else changes: no new `FormatAssetRow`, no `MaterializeRunCommand` change, no endpoint
   change. New generations simply stop writing base64 into two tables.

## Consumer blast-radius (why Slice 1 must not touch `content_generation_format_assets`)

`content_generation_format_assets` is read/mutated by many consumers, not just materialize. Adding a
background row there escapes Slice 1's scope:

| Consumer (real path) | Uses rows for | Effect of an extra bg row |
|----------------------|---------------|---------------------------|
| `Assemble/AssembleGeneratedPackageQuery.cs` (`ListByAnalysisClientAsync`; `WritePackageEntriesAsync(archive, selectedRows, …)`) | writes EVERY selected row as a ZIP entry, filtered only by client/format (not asset kind) | carousel ZIP would contain BOTH `slide_N.png` AND `slide_N_bg.png` → doubled deliverables |
| `PackageListing/ListAnalysisPackagesQuery.cs` | asset counts | inflated counts |
| `Export/ExportPackageToCanvaCommand.cs`, `Export/ExportFormatToCanvaCommandHandler.cs` | enumerate assets to push to Canva | duplicate/unwanted uploads |
| `Regenerate/DeleteFormatAssetCommand.cs`, `Regenerate/DeleteAssetCommand.cs`, `Regenerate/RegenerateFormatAssetCommand.cs` | enumerate/mutate rows by index | index/kind confusion |
| `ContentStudio/Materialize/MaterializeRunCommand.cs` | builds `item.Draft.Assets` | would serve bg but see above cost |

Filtering backgrounds out of all of those is a multi-consumer change far beyond "stop the bloat".
Therefore Slice 1 does not create any row; Slice 2 owns addressability with a no-row constraint.

## Technical approach (Slice 1)

The background bytes today arrive as a data-URI string
(`renderedWithGeo.Value.BackgroundImageDataUri`, captured at `ContentGenerationJobProcessor.cs:786`).
To upload raw bytes we decode that data-URI. There is no reusable Application-layer data-URI decoder
today (the only decoder is `PptxExportAdapter.TryDecodeDataUri`, which is Infrastructure and private),
so we add a small internal static decoder in the processor rather than reaching into Infrastructure.

Upload uses the primitives already in the loop:
- `StoredAssetKey(job.OrganizationId, effectiveClientId.Value, job.AnalysisId, relativePath)` — the
  SAME scope as the full slide (`cs:825-829`).
- `PutAssetAsync(key, new StoredAssetContent(bytes, "image/png"), ct)` → `storedRef.StoragePath`.
- Set the envelope's `BackgroundImageRef` (built at `cs:877-883`) to `storedRef.StoragePath`.

All storage access stays behind the port — no Supabase/EF/ASP.NET leak into Domain or Application.
`ContentAssetRef` (Domain) is untouched.

## Decision A — Storage path / naming convention (kept from Q3)

**Decision.** Reuse the SAME `StoredAssetKey` scope as the full slide; only the `relativePath`
differs:

| Format | Full-slide relativePath (existing) | Background relativePath (new) |
|--------|------------------------------------|-------------------------------|
| Carousel (multi-asset, `specs.Count > 1`) | `{formatDef.StorageFolder}/slide_{N}.png` | `{formatDef.StorageFolder}/slide_{N}_bg.png` |
| Single-asset | `{asset.Filename}` (e.g. `story-single.png`) | `{filenameWithoutExt}_bg.png` (e.g. `story-single_bg.png`) |

`N` = `specIndex + 1` (1-based), matching the existing convention (`cs:813`). Uploaded as
`StoredAssetContent(decodedBytes, "image/png")`.

**Rationale.** Same org/client/analysis scope means the background lands in the same container/folder
as its slide, so existing lifecycle (delete/cleanup by analysis or format result) already covers it.
The `_bg` suffix keeps the pairing obvious in storage. Crucially, this writes ONLY to the storage
bucket — it does NOT add a DB row, so none of the blast-radius consumers are affected.

**Alternatives rejected.** Separate `backgrounds/` folder or hash-addressed name — fragment cleanup /
out of scope. No benefit for Slice 1.

## Decision B — Storage-not-wired fallback (kept)

**Decision.** When `_assetStorage is null` (legacy no-storage path, `cs:861`), keep the CURRENT
behavior: leave the data-URI in `BackgroundImageRef` (no upload possible). Only when storage IS wired
do we upload + swap to a path.

**Rationale.** Preserves back-compat for environments without storage and keeps the change strictly
additive.

## Decision C — PPTX back-compat resolver (kept from original)

`PptxExportAdapter` is Infrastructure and today has NO constructor and NO port (it is DI-registered as
scoped but stateless; `AddSlide`, `TryDecodeDataUri`, `DataUriRegex` are static). It may legitimately
take `IGeneratedAssetStorage` directly (adapter layer). Resolution at the `pictureRef` site
(`PptxExportAdapter.cs:167-179`):

```
resolve(pictureRef):
  if pictureRef is null/blank        -> no picture
  if pictureRef starts with "data:"  -> TryDecodeDataUri(pictureRef)   // legacy inline
  else                               -> OpenAsync(pictureRef) -> read all bytes   // new storage path
```

Do the resolution ONCE per envelope BEFORE the OpenXML `AddSlide` loop (build a `byte[]?` per slide),
so `AddSlide` stays synchronous and OpenXML stays off the async path. On `OpenAsync` failure, treat as
"no background" (same graceful skip as a failed decode today) and log — never throw the whole export.
`FlattenedImageRef` continues to be a data-URI (unchanged).

## Component-by-component change map (Slice 1)

| # | File (real path) | Layer | Change |
|---|------------------|-------|--------|
| 1 | `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` | Application | After capturing `capturedBackgroundDataUri` (~cs:786): decode data-URI → bytes; build `*_bg.png` relativePath; `PutAssetAsync` via `_assetStorage`; set the envelope's `BackgroundImageRef` (cs:877-883) to `storedRef.StoragePath` instead of the data-URI. Add internal static data-URI decoder. **No `FormatAssetRow` added.** Storage-not-wired (cs:861) → keep data-URI. Null background → ref stays null. |
| 2 | `Incoders.Hive.Application/Features/Social/ContentGeneration/Pipeline/CompositionSpecEnvelope.cs` | Application | Doc-only: update `BackgroundImageRef` XML doc — now a storage path for new rows; legacy rows may still hold a data-URI; consumers accept both. No record shape change. |
| 3 | `Incoders.Hive.Infrastructure/Rendering/PptxExportAdapter.cs` | Infrastructure | Add `IGeneratedAssetStorage` constructor dependency. Pre-resolve each envelope's picture ref to `byte[]?` before the OpenXML build loop (data-URI → `TryDecodeDataUri`; else `await _storage.OpenAsync(ref)` → read stream). `AddSlide` accepts `byte[]? backgroundBytes` (stays synchronous). `OpenAsync` failure → graceful skip. |
| 4 | `Incoders.Hive.Infrastructure/Social/ContentGeneration/InfrastructureContentGenerationExtensions.cs:114` | Infrastructure | `AddScoped<IPptxExportAdapter, PptxExportAdapter>()` — confirm `IGeneratedAssetStorage` is registered in the same container so the new ctor param resolves. |

**Explicitly NOT changed in Slice 1:** `MaterializeRunCommand.cs`, `GetContentItemAssetQuery.cs`,
`MapContentStudioEndpoints.cs`, and `content_generation_format_assets` (schema + rows).

## Slice 2 (frontend + serving) — deferred, with a hard constraint

Slice 1 stops the bloat but does NOT make the background addressable by the frontend. Slice 2 owns
that, and its own design must resolve it. Record now:

**CONSTRAINT (non-negotiable): Slice 2 MUST NOT append rows to `content_generation_format_assets`.**
See "Consumer blast-radius" — the assembly ZIP, listing counts, Canva export, and delete/regenerate
consumers all enumerate that table without a kind filter, so an extra row corrupts every one of them.

**Two candidate mechanisms (decision deferred to Slice 2's design):**
1. **Studio-only `DraftAsset` synthesized at materialize time** from the envelope's
   `BackgroundImageRef`. `MaterializeRunCommand` would append a `ContentAssetRef("background", path,
   mime)` to `item.Draft.Assets` derived from the parsed envelope — WITHOUT touching
   `content_generation_format_assets`. This keeps the addressable asset scoped to Content Studio and
   away from the assembly/export/delete consumers. Trade-off: materialize must parse the envelope and
   define a deterministic positional index for backgrounds relative to full slides.
2. **Dedicated background endpoint** (e.g. `/items/{itemId}/background/{slideIndex}`) that resolves
   the envelope's `BackgroundImageRef` and streams via `IGeneratedAssetStorage.OpenAsync`. Keeps
   `item.Draft.Assets` untouched entirely. Trade-off: a new endpoint + a new frontend fetch path.

Do not resolve between these now. Slice 2 must also fix the positional/URL contract for the frontend
(the original draft's `fullSlideCount + slideOrdinal` idea belongs to whichever mechanism Slice 2
picks, not to Slice 1).

## Back-compat strategy (summary)

- **Legacy envelopes** (data-URI in `BackgroundImageRef`): PPTX resolver detects `data:` and
  inline-decodes exactly as today. No behavior change.
- **New envelopes** (storage path): PPTX resolver opens via the port.
- **No backfill**: existing rows keep working through the same dual-path resolver; they never shrink
  until regenerated. Forward-only.
- **Additive JSON**: `CompositionSpecEnvelope` record shape is unchanged; only the semantic content of
  an existing nullable field changes. Old and new rows deserialize identically.

## Test strategy (strict TDD — CI dispatch only; `dotnet test` blocked locally by SAC/WDAC)

Author each test RED first, then implement to GREEN. Verification runs via CI dispatch — the design
must not assume a local test run.

| Component | Test file (existing or new) | RED cases |
|-----------|-----------------------------|-----------|
| Processor upload + ref swap | `Incoders.Hive.Tests/Application/Social/ContentGeneration/Pipeline/` (new, e.g. `BackgroundAssetUploadTests.cs`) | (a) carousel: background uploaded to `{folder}/slide_{N}_bg.png`, envelope `BackgroundImageRef` == returned path (not `data:`); (b) single: `*_bg.png`; (c) storage-not-wired → data-URI retained; (d) null background → ref stays null; (e) NO extra `FormatAssetRow` is produced for the background (assert row count unchanged). |
| Data-URI decoder | same suite | decodes valid `data:image/png;base64,…` to bytes; returns null/no-upload on malformed input. |
| PPTX back-compat resolver | `Incoders.Hive.Tests/Infrastructure/Rendering/PptxExportAdapterTests.cs` | (a) `BackgroundImageRef` = data-URI → picture embedded (legacy, must still pass); (b) `BackgroundImageRef` = storage path → adapter opens via stub `IGeneratedAssetStorage`, picture embedded; (c) `OpenAsync` failure → export succeeds without the background (graceful). |
| Contract stability | `Incoders.Hive.Tests/Application/IPptxExportAdapterContractTests.cs` | constructor with the new port dependency still satisfies the contract. |

**Dropped from Slice 1** (moved to Slice 2): materialize-role tests and endpoint-serve tests — no
materialize or endpoint change in Slice 1.

## Residual risks

- **PPTX adapter refactor blast radius (Med).** Adding a ctor dependency touches DI registration and
  every direct instantiation in tests (several `PptxExportAdapter` sites). Mitigate: provide the port
  via constructor and update instantiations; the pre-resolve-to-bytes design keeps `AddSlide`
  synchronous so the OpenXML core is untouched.
- **Storage-not-wired path keeps bloat (Low, accepted).** Environments without storage still store the
  data-URI. Acceptable: non-production/legacy paths; change is additive.
- **Background not yet servable to the frontend (by design).** Slice 1 intentionally defers this;
  Slice 2 must honor the no-`FormatAssetRow` constraint.
- **Cannot run `dotnet test` locally (High, process).** All verification via CI dispatch; no local
  green gate. Design keeps tests deterministic and storage-stubbed to be CI-friendly.

## Next step

Proceed to `sdd-tasks` once the spec is ready. Slice 1 tasks sequence: RED tests → processor
decode/upload/ref-swap + internal decoder → PPTX resolver + DI ctor → CI dispatch. Addressability is
Slice 2.
