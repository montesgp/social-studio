# Tasks: content-aesthetic-generation-phase-2

**Branch:** TBD by human (suggest a fresh `feat/content-aesthetic-phase-2` off the current branch).
**TDD mode:** STRICT — `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj`. Every behavior task: RED → GREEN.

## Implemented so far (autonomous run 2026-06-22 — full suite green: 1798 passed, 0 failed)

- ✅ Optional workflow step `extract-aesthetic` / `ai-aesthetic` in `WorkflowConfigurationCatalog` (degrades to Phase 1 when unconfigured).
- ✅ WU-1 Domain: `ProfilePost.ImageUrls`/`Caption`, `ProfilePostImageRef` (+ tests).
- ✅ WU-2 Application VOs: `AestheticPaletteEntry`, enriched `ClientAestheticProfile` (palette optional), `ClientAestheticDesignContext` (+ tests).
- ✅ WU-3 Ports: `IClientPostMediaArchiver`, `IClientAestheticStyleExtractor` (+ `AestheticExtraction`), `IVisualDesigner` optional aesthetic context (all impls + test stubs updated).
- ✅ WU-9 (partial) + WU-10 (text-first): `ContentGenerationJobProcessor` builds `ClientAestheticDesignContext` and threads it to the designer; `AiVisualDesigner` composes creatives-first, leads with the feed palette, and when the client has NO palette derives colour from creatives WITHOUT fabricating one (+ tests).
- ✅ **AI gateway VISION extension (dependency CLEARED):** `AiCompletionRequest`/`AiJsonCompletionRequest` gained optional `Images` (`AiImageInput`: media type + base64 or URL); `AiCompletionGateway` forwards them on the primary call; `ClaudeModelClient` emits Anthropic vision content blocks (base64/url image + text) when present, plain string otherwise (+ tests). Architecture confirmed: vision runs ONCE at extraction → distilled to text profile; the designer stays text-only. Base64 downscaled thumbnails ARE the lightweight transport.

- ✅ WU-5 `SupabasePostMediaArchiver`: downloads Apify image URLs → re-hosts via `IGeneratedAssetStorage` under `aesthetic/{postId}-{i}` → returns `ProfilePostImageRef[]`; best-effort per image (+ tests). (Thumbnail downscale deferred — stored as-downloaded.)
- ✅ WU-7 `MultimodalAestheticStyleExtractor`: resolves the optional `extract-aesthetic` route; reads image bytes from storage → base64 → gateway vision call → maps to `AestheticExtraction`; empty palette is a VALID result; no route / no images → `Empty` (degrade) (+ tests).

- ✅ WU-8 persistence (read side): single additive nullable jsonb column `aesthetic_profile_json` on `social.profile_analyses` (chosen over a separate table — lower risk); EF entity + DbContext mapping; migration `20260622143516_Add_AestheticProfileJson` generated (snapshot updated); `AestheticProfilePersistence` shared serialize/deserialize contract; `EfClientAestheticProfileReader` surfaces the enriched fields (palette/descriptors/motifs/voice/refs) when present, null otherwise (+ tests). ⚠️ Migration NOT applied to the remote DB — run `dotnet ef database update` (or deploy) to apply the additive column.

- ✅ WU-6 analyzer enrichment (WRITE side) + DI: `ProfileAnalyzerOrchestrator.TryEnrichClientAestheticAsync` runs AFTER the analysis is Analyzed (best-effort, never rolls back). Gated on: adapters wired + client own-account analysis + the optional `extract-aesthetic` route configured (so non-ClientAesthetic orgs pay nothing). Archives post images → extracts multimodal aesthetic → persists via new `IProfileAnalysisRepository.SetAestheticProfileJsonAsync` (default no-op interface method so test fakes compile; EF adapter overrides). `AestheticProfilePersistence` moved to Application (shared by writer + reader). Archiver + extractor registered in DI. (+ repo write tests; full suite green 1811.)

- ✅ WU-4 Apify media/caption mapping: `InstagramProfileApifyScrapeAdapter.MapSinglePost` now captures cover (`displayUrl`) + carousel children (`childPosts`/`images`/`sidecar`/`children`) + `caption` into `ProfilePost.ImageUrls`/`Caption`, tolerant of absence (metadata-only actor → null) (+ tests).
- ✅ WU-9 remainder: `AestheticGuidance` now includes caption-voice + style descriptors (additive to concision); the resilience backstop (`BuildTemplateBackedMarkup`) derives gradient colour from the client's feed palette when present (no-grey).

**ALL CODE COMPLETE end-to-end. Full suite green (1817 tests).** Chain: scrape (captures images+caption) → orchestrator enrich (archive→multimodal extract→persist) → reader surfaces enriched profile → job processor builds ClientAestheticDesignContext → AiVisualDesigner creatives-first (vision-capable gateway).

**Remaining = config + ops (no more feature code):**
- Apply the EF migration to the DB: `dotnet ef database update --project Incoders.Hive.Infrastructure --startup-project Incoders.Hive.Infrastructure`.
- Configure the org: Apify scrape-profile actor that returns post media (`apify/instagram-scraper`, resultsType posts) + the optional `extract-aesthetic` route on a multimodal Claude model.
- WU-12 ADR + docs sync + spec-merge; run `pwsh -File scripts/gates/Run-Gates.ps1`.
- Optional later: no-AI palette via server-side colour quantization; thumbnail downscaling in the archiver.

---

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1,800–2,400 |
| 400-line budget risk | **High** |
| Chained PRs recommended | **Yes** — natural split: (A) capture+persist, (B) multimodal extraction, (C) designer vision + no-grey |
| Decision needed before apply | **Yes** — OQ-1 actor spike must pass before WU-1+ |
| Chain strategy | TBD with owner (`stacked-to-main` vs `feature-branch-chain`) |

### Suggested Work Units

| WU | Goal | Depends on | PR slice |
|----|------|-----------|----------|
| WU-0 | **SPIKE** — confirm Apify actor returns `displayUrl`/`displayResources` + `caption` + children | — | gate, no PR |
| WU-1 | Domain — `ProfilePost.ImageUrls`/`Caption`, `ProfilePostImageRef` | WU-0 | A |
| WU-2 | Application VOs — `AestheticPaletteEntry`, extended `ClientAestheticProfile`, `ClientAestheticDesignContext` | WU-1 | A |
| WU-3 | Application ports — `IClientPostMediaArchiver`, `IClientAestheticStyleExtractor`, `IVisualDesigner` aesthetic ctx | WU-2 | A/B |
| WU-4 | Infra — Apify media/caption mapping (per WU-0) | WU-1 | A |
| WU-5 | Infra — `SupabasePostMediaArchiver` (download→thumbnail→PutAsset) | WU-3 | A |
| WU-6 | Application + Infra — analyzer archives images + runs multimodal extractor + persists enriched payload | WU-3,4,5 | B |
| WU-7 | Infra — `MultimodalAestheticStyleExtractor` (vision via `IAiCompletionGateway`; palette optional) | WU-3 | B |
| WU-8 | EF — enriched payload columns + `profile_analysis_images` table + migration + round-trip | WU-2,6 | B |
| WU-9 | Application — generation: build `ClientAestheticDesignContext`; no-grey colour source; reference cap; caption-voice | WU-2,3 | C |
| WU-10 | Infra — `AiVisualDesigner` vision path + creatives-first + no-grey prompt | WU-3,9 | C |
| WU-11 | Degrade paths — non-multimodal routes, partial failures, empty-palette-is-success | WU-6,9,10 | C |
| WU-12 | ADR + docs sync + spec merge + `state.yaml` + final gates | all | C |

---

## WU-0 — SPIKE: Actor capability (BLOCKING — must pass before any build)

*Refs: CAG2-PA-05; design §0 OQ-1.*

- [ ] 0.1 Run `apify/instagram-profile-scraper` against a representative profile; record whether each post carries `displayUrl`, carousel `displayResources`/children URLs, and `caption`.
- [ ] 0.2 Run the backup `get-leads/all-in-one-instagram-scraper`; record the same fields.
- [ ] 0.3 Decide the actor (and any input config) that reliably returns media + captions; record the decision + sample field shape in this file and the ADR.
- [ ] 0.4 If NEITHER returns media/captions, STOP and escalate to owner (switch actor or descope Phase 2). Do not proceed to WU-1.

---

## WU-1 — Domain: post media capture fields (foundation)

*Refs: CAG2-PA-01; design §2.1.*

- [ ] 1.1 **[RED]** `ProfilePostMediaFieldsTests.cs` — assert `ProfilePost` exposes `ImageUrls IReadOnlyList<string>?` and `Caption string?`; null-safe construction; back-compat `Restore`.
- [ ] 1.2 **[GREEN]** Modify `ProfilePost.cs` — add the two additive nullable fields; update `Create`/`Restore` with null defaults.
- [ ] 1.3 **[RED]** `ProfilePostImageRefTests.cs` — assert value type holds storage path + content hash + source post id + asset index; equality by value.
- [ ] 1.4 **[GREEN]** Create `ProfilePostImageRef.cs` (pure Domain value type).
- [ ] 1.5 `dotnet test` — WU-1 green; no regression.

---

## WU-2 — Application: aesthetic value objects (palette optional, forward-compat)

*Refs: CAG2-PA-03, CAG2-PA-04, CAG2-CG-01; design §2.2.*

- [ ] 2.1 **[RED]** `AestheticPaletteEntryTests.cs` — hex + role + rank; construction valid.
- [ ] 2.2 **[GREEN]** Create `AestheticPaletteEntry.cs`.
- [ ] 2.3 **[RED]** `ClientAestheticProfileEnrichedTests.cs` — assert new OPTIONAL fields (`Palette`, `StyleDescriptors`, `Motifs`, `CaptionVoice`, `ReferenceImages`) default empty/null; assert a Phase-1-shaped construction still compiles/behaves (no breaking change); assert empty palette is a valid state.
- [ ] 2.4 **[GREEN]** Extend `ClientAestheticProfile.cs` with the optional fields (trailing nullable params).
- [ ] 2.5 **[RED]** `ClientAestheticDesignContextTests.cs` — palette? + descriptors + motifs + reference refs + variationSeed; null palette valid.
- [ ] 2.6 **[GREEN]** Create `ClientAestheticDesignContext.cs`.
- [ ] 2.7 `dotnet test` — WU-2 green.

---

## WU-3 — Application: ports (archiver, extractor, designer aesthetic ctx)

*Refs: CAG2-PA-02, CAG2-PA-03, CAG2-CG-01, CAG2-CG-06; design §2.2.*

- [ ] 3.1 **[RED]** `IClientPostMediaArchiverContractTests.cs` — reflect interface; assert no provider/EF/HttpClient types; signature `ArchiveAsync(orgId, clientId, analysisId, imageUrls, ct) → IReadOnlyList<ProfilePostImageRef>`.
- [ ] 3.2 **[GREEN]** Create `IClientPostMediaArchiver.cs`.
- [ ] 3.3 **[RED]** `IClientAestheticStyleExtractorContractTests.cs` — assert `ExtractAsync(images, captions, ct) → AestheticExtraction` (palette optional); no provider types.
- [ ] 3.4 **[GREEN]** Create `IClientAestheticStyleExtractor.cs` + `AestheticExtraction` shape.
- [ ] 3.5 **[RED]** `IVisualDesignerAestheticContextTests.cs` — assert `ComposeAsync` accepts an optional `ClientAestheticDesignContext?` (null = pre-change behavior); no provider types added to the port.
- [ ] 3.6 **[GREEN]** Modify `IVisualDesigner.cs` — add the optional trailing parameter.
- [ ] 3.7 `dotnet test` — WU-3 green; existing designer callers compile (null arg).

---

## WU-4 — Infrastructure: Apify media + caption mapping (per WU-0)

*Refs: CAG2-PA-01; design §2.3.*

- [ ] 4.1 **[RED]** Mapping test against a captured sample (from WU-0): sidecar → N child URLs; single → 1 URL + caption; missing field → empty/null, no throw.
- [ ] 4.2 **[GREEN]** Modify the confirmed scrape adapter to map `displayUrl`/`displayResources`/`caption`/children into `ProfilePost`.
- [ ] 4.3 `dotnet test` — WU-4 green.

---

## WU-5 — Infrastructure: durable post-media archiver

*Refs: CAG2-PA-02; design §2.3, §0 OQ-3.*

- [ ] 5.1 **[RED]** `SupabasePostMediaArchiverTests.cs` — given image URLs, downloads bytes, downscales to thumbnail, calls `PutAsset` under `(org, client, analysis)` path, returns refs with content hash; partial download failure skips that image, others persist.
- [ ] 5.2 **[GREEN]** Implement `SupabasePostMediaArchiver` (reuse `IGeneratedAssetStorage`/storage adapter; downscale; cap per OQ-2/OQ-4).
- [ ] 5.3 `dotnet test` — WU-5 green.

---

## WU-6 — Application+Infra: analyzer archives + extracts + persists enriched payload

*Refs: CAG2-PA-02, CAG2-PA-03, CAG2-PA-04; design §3.1, §4.*

- [ ] 6.1 **[RED]** `ProfileAnalyzerEnrichmentTests.cs` — after scrape: archiver called, extractor called when multimodal route present, enriched payload persisted; **empty palette persists as success**; non-multimodal route → extractor skipped, Phase 1 signal retained; partial archive failure → analysis still completes.
- [ ] 6.2 **[GREEN]** Modify `ProfileAnalyzerOrchestrator` — insert archive + extract steps (best-effort, non-fatal); persist enriched payload + refs via repository.
- [ ] 6.3 `dotnet test` — WU-6 green.

---

## WU-7 — Infrastructure: multimodal style extractor

*Refs: CAG2-PA-03, CAG2-PA-06; design §2.3.*

- [ ] 7.1 **[RED]** `MultimodalAestheticStyleExtractorTests.cs` — builds a vision request (images + captions); maps response into `AestheticExtraction`; palette MAY be empty (valid); bumps prompt-version key.
- [ ] 7.2 **[GREEN]** Implement `MultimodalAestheticStyleExtractor` via the multimodal `IAiCompletionGateway` path; add the prompt-version constant.
- [ ] 7.3 `dotnet test` — WU-7 green.

---

## WU-8 — EF: persist enriched payload + image refs (+ migration)

*Refs: CAG2-PA-02, CAG2-PA-04; design §5.*

- [ ] 8.1 **[RED]** `EfProfileAnalysisEnrichmentRoundTripTests.cs` — persist + read enriched payload (palette/descriptors/motifs/voice) and `ProfilePostImageRef[]`; null payload round-trips as Phase 1 shape.
- [ ] 8.2 **[GREEN]** Add entity fields + `profile_analysis_images` entity; map in ToData/Update/ToDomain (all three — predecessor bug guard); repository persist/read.
- [ ] 8.3 **[GREEN]** Create additive EF migration (nullable payload columns + new table).
- [ ] 8.4 `dotnet test` — WU-8 green.

---

## WU-9 — Application: generation wiring (design context, no-grey, caption-voice)

*Refs: CAG2-CG-01..05; design §3.2, §4.*

- [ ] 9.1 **[RED]** `ContentGenerationDesignContextTests.cs` — for ClientAesthetic with enriched profile: builds `ClientAestheticDesignContext` (reference cap 6, variationSeed=job.Id); empty palette does not block; no enriched profile → Phase 1 path.
- [ ] 9.2 **[GREEN]** Modify `ContentGenerationJobProcessor`/`ContentPackageOrchestrator` to build + pass the design context.
- [ ] 9.3 **[RED]** `NoGreyColourSourceTests.cs` — ClientAesthetic + no manual + enriched profile → colour derives from profile/creatives, NOT the grey default; non-ClientAesthetic unchanged.
- [ ] 9.4 **[GREEN]** Modify the colour-source path (designer input + `BuildTemplateBackedMarkup` backstop) to source from profile/creatives for ClientAesthetic; never fabricate a palette.
- [ ] 9.5 **[RED]** `CaptionVoiceAdditiveTests.cs` — caption-voice present in copy prompt alongside concision; absent → no section, no error.
- [ ] 9.6 **[GREEN]** Thread caption-voice into copy generators (additive to `ConcisionConstraint` + Phase 1 `VoiceTone`).
- [ ] 9.7 `dotnet test` — WU-9 green.

---

## WU-10 — Infrastructure: designer vision path + creatives-first + no-grey

*Refs: CAG2-CG-01, CAG2-CG-02, CAG2-CG-03, CAG2-CG-04; design §2.3.*

- [ ] 10.1 **[RED]** `AiVisualDesignerVisionTests.cs` — given a design context with reference images + multimodal route: includes vision blocks (cap 6); empty palette → no fabricated palette; variation directive + seed present; non-multimodal route → no vision, descriptor-only, notice logged.
- [ ] 10.2 **[GREEN]** Modify `AiVisualDesigner` — vision path, creatives-first prompt, no-grey, variety directive; reference cap truncation.
- [ ] 10.3 `dotnet test` — WU-10 green.

---

## WU-11 — Degrade paths (cross-cutting hardening)

*Refs: CAG2-PA-03, CAG2-CG-06; design §4.*

- [ ] 11.1 **[RED/GREEN]** Cover: non-multimodal analyze route (extraction skipped); non-multimodal generate route (descriptor-only design); partial image failures; designer total failure → backstop colour from profile not grey.
- [ ] 11.2 `dotnet test` — full suite green.

---

## WU-12 — ADR, docs, spec merge, gates

*Refs: project gates contract.*

- [ ] 12.1 Author ADR under `docs/architecture/adr/` (capture pipeline + durable media + multimodal extraction + designer vision path).
- [ ] 12.2 Sync `docs/` + merge spec deltas into `openspec/specs/{profile-analysis,content-generation,background-sourcing-policy}/spec.md`.
- [ ] 12.3 Update `state.yaml` to `apply-complete`.
- [ ] 12.4 **[deferred to human]** Run `pwsh -File scripts/gates/Run-Gates.ps1`; report summary. Do NOT mark complete if any MUST gate fails.

---

## Definition of Done

- [ ] WU-0 spike passed (or owner re-scoped); a real client (e.g., Atlas) generates content that visibly reflects their feed (manual review), not generic greys.
- [ ] Empty/absent palette is handled as a valid state end-to-end; no fabricated palette is ever injected.
- [ ] Non-blocking contract preserved; all degrade paths covered by tests.
- [ ] Architecture gate green (no provider types in Domain/Application); all MUST gates pass.
