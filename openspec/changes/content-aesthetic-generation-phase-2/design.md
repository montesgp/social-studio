# Design — content-aesthetic-generation-phase-2 (CASE C, Phase 2: Real Visual Fidelity)

Hexagonal design that gives `BackgroundSourcingPolicy.ClientAesthetic` the real visual substrate it
needs to emulate a client: capture the client's actual post images + captions, persist them durably,
extract a **multi-signal** aesthetic profile (creatives-first; palette OPTIONAL) with a multimodal
model, and feed the client's real creatives into the AI visual designer — removing the grey-default
when no brand manual exists. Every provider concern (Apify, Supabase Storage, AI vision) stays behind
an Application port with an Infrastructure adapter; Domain and Application stay provider-free. This
document fixes decisions, the component map, the new flow, the degrade matrix, the migration verdict,
the test-first ordering, and risks. It does NOT enumerate task steps — that is `sdd-tasks`.

## Status
Design — architecture decisions resolved EXCEPT the actor-capability spike (OQ-1), which is the first,
blocking work item: nothing downstream can be built until we confirm which Apify actor returns
`displayUrl`/`displayResources` + `caption` + carousel children. New Application ports (post-media
archiver) + enriched profile shape + multimodal analyzer step + designer vision path + additive EF
migration(s) → an ADR under `docs/architecture/adr/` is authored at apply time.

## Scope reminder (Non-Goals are hard boundaries)
Ships real-creative capture + durable persistence + multimodal extraction + creatives-first designer
input + no-grey. Does NOT add an image-generation/diffusion model, does NOT pixel-reuse a client's
exact post as a template (that is `ClientDefinedSet`), does NOT target photographic feeds, does NOT
introduce a blocking/"waiting-for-analysis" job state (Phase 1's non-blocking contract is preserved),
and does NOT change `ClientDefinedSet`/`AiPalette` behavior.

---

## 0. The decisions (read this first)

| # | Open question | Decision | One-line rationale |
|---|---------------|----------|--------------------|
| **OQ-1** | Apify actor for media+captions | **SPIKE FIRST (blocking).** Confirm `apify/instagram-profile-scraper` and the already-configured backup `get-leads/all-in-one-instagram-scraper` for `displayUrl`/`displayResources` + `caption` + carousel children. Pick whichever returns them; record the decision. | Verified: the current persisted dataset is metadata-only. We must not build a capture mapping against an actor that returns nothing. |
| **OQ-2** | Sample size N posts × M images | **N = 18 posts** (matches Phase 1 `maxPostsPerProfile`); **M = cover + up to 2 carousel children** per post. | Reuses the Phase 1 window; enough visual signal without multiplying Apify/storage cost. Owner-tunable. |
| **OQ-3** | Stored image fidelity | **Downscaled thumbnails** (longest edge ≈ 1024px, JPEG). | Plenty for style/colour inference; a fraction of storage + vision-token cost vs full-res. Owner-tunable. |
| **OQ-4** | Reference images to the designer | **Curated set, cap 6, ONCE per job** (not per slide). | Bounds vision-token cost; one coherent reference set keeps a package on-style. Owner-tunable. |
| **OQ-5** | Primary signal | **Creatives-first.** Send the bounded reference image set as vision input + extracted descriptors as complement; palette only when marked. | Owner-locked: client identity often lives in backgrounds, not colour. |
| **OQ-6** | Variety mechanism | **Per-job variation seed** (reuse `job.Id` seed token) + explicit "vary composition, stay on-style" directive in the designer prompt. | Reproducible variety within coherence; no new infra. |
| **OQ-7** | Retention / privacy | **Keep only the latest analysis' durable images per client**; superseded on the next successful analysis; scoped under `(org, client, analysis)`. | Bounds storage + privacy surface; aligns with refresh-if-stale lifecycle. Owner-confirm. |
| **OQ-8** | Weak/absent palette | **Designer derives colour freely from the reference creatives**; a loose extracted palette is soft guidance only; **never** fabricate a default brand palette. | Owner-locked: palette can be inconsequential. |

---

## 1. Architecture approach

Dependency rule, unchanged: **Domain ← Application ← Infrastructure / Api.** No Apify/Supabase/AI/
`HttpClient` types in Domain or Application.

- **Domain** (`Domain/Social/ProfileAnalysis/`) gains additive nullable fields on `ProfilePost`:
  `ImageUrls` (`IReadOnlyList<string>?`, transient capture — cover + children) and `Caption`
  (`string?`). A new pure value type `ProfilePostImageRef` (durable storage path + content hash)
  models persisted images. All pure; no provider types.
- **Application** gains: the `ClientAestheticProfile` value object EXTENDED with optional
  `Palette` (role-ordered), `StyleDescriptors`, `Motifs`, `CaptionVoice`, and
  `ReferenceImages` (durable refs) — all nullable/optional, forward-compatible with Phase 1; a new
  `IClientPostMediaArchiver` port (download Apify image → re-host durable → return ref); a multimodal
  extraction step contract in the analyzer; and a `ClientAestheticDesignContext` carried into the
  visual-designer call (`IVisualDesigner.ComposeAsync` gains an optional aesthetic context).
- **Infrastructure** carries only wiring: Apify adapter maps `displayUrl`/`displayResources`/
  `caption`; `SupabasePostMediaArchiver` re-hosts thumbnails; `ProfileAnalyzerOrchestrator` runs the
  multimodal extraction via the existing multimodal `IAiCompletionGateway`; `AiVisualDesigner` gains a
  vision path (reference images as vision blocks) and a creatives-first / no-grey prompt; EF persists
  the enriched payload + image refs; one or two additive migrations.
- **Api / Web**: none required for Phase 2 (no new client ABM field). Retention policy is a server
  default, not a user setting in this slice.

The multimodal extraction runs INSIDE the existing async profile-analysis pipeline (no new worker).
Generation NEVER awaits analysis — Phase 1's fire-and-forget refresh-if-stale is unchanged.

---

## 2. Component map — ports, adapters, layers

### 2.1 Domain (pure, additive)
| Element | Path | Change |
|---------|------|--------|
| `ProfilePost.ImageUrls` (`IReadOnlyList<string>?`), `ProfilePost.Caption` (`string?`) | `Domain/Social/ProfileAnalysis/ProfilePost.cs` | **Modified** — append nullable transient capture fields; null when actor omits them |
| `ProfilePostImageRef` (storage path + content hash + sourcePostId + index) | `Domain/Social/ProfileAnalysis/ProfilePostImageRef.cs` | **New** — pure value type for a durably-hosted post image |

### 2.2 Application (ports + use cases)
| Element | Kind | Path | Change |
|---------|------|------|--------|
| `ClientAestheticProfile` (+ `Palette`, `StyleDescriptors`, `Motifs`, `CaptionVoice`, `ReferenceImages`) | value object | `…/ContentGeneration/Shared/ClientAestheticProfile.cs` | **Modified** — extend via optional/nullable fields (no breaking change) |
| `AestheticPaletteEntry` (hex + role + rank) | value object | `…/ContentGeneration/Shared/AestheticPaletteEntry.cs` | **New** — optional role-ordered palette signal |
| `IClientPostMediaArchiver` | Port | `Application/Abstractions/Social/ContentGeneration/IClientPostMediaArchiver.cs` | **New** — `ArchiveAsync(orgId, clientId, analysisId, imageUrls, ct) → IReadOnlyList<ProfilePostImageRef>` |
| `IClientAestheticStyleExtractor` | Port | `Application/Abstractions/Social/ContentGeneration/IClientAestheticStyleExtractor.cs` | **New** — multimodal extraction: `ExtractAsync(images, captions, ct) → AestheticExtraction` (palette optional) |
| `ProfileAnalyzerOrchestrator` | Use case | `…/ProfileAnalyzer/Analyze/ProfileAnalyzerOrchestrator.cs` | **Modified** — after scrape: archive images, run extractor (best-effort), persist enriched payload |
| `IProfileAnalysisRepository` (persist/read enriched payload + refs) | Port | `Application/Abstractions/Social/IProfileAnalysisRepository.cs` | **Modified** — additive persist/read for enriched payload + image refs |
| `ClientAestheticProfile` reader (`IClientAestheticProfileReader`) | Port | `Application/Abstractions/Social/ContentGeneration/IClientAestheticProfileReader.cs` | **Modified** — surface the enriched fields when present |
| `ClientAestheticDesignContext` (palette? + descriptors + motifs + reference refs + variationSeed) | value object | `…/ContentGeneration/Shared/ClientAestheticDesignContext.cs` | **New** — what the designer needs for creatives-first design |
| `IVisualDesigner.ComposeAsync(..., ClientAestheticDesignContext? aesthetic)` | Port | `Application/Abstractions/Social/ContentGeneration/IVisualDesigner.cs` | **Modified** — optional aesthetic context (null = pre-change behavior) |
| `ContentPackageOrchestrator` / `GenerationContext` | contracts | `…/Orchestration/*`, `…/Shared/GenerationContext.cs` | **Modified** — carry enriched profile / design context to the render loop |
| `ContentGenerationJobProcessor` | Use case | `…/Pipeline/ContentGenerationJobProcessor.cs` | **Modified** — build `ClientAestheticDesignContext`; no-grey colour source; pass to designer |
| Copy generators | Use case | `…/Formats/{Carousel,Story,FeedPost}Generator.cs` | **Modified** — additive caption-voice guidance |

### 2.3 Infrastructure (adapters, wiring only)
| Element | Path | Change |
|---------|------|--------|
| Apify scrape mapping — capture `displayUrl`/`displayResources`/`caption`/children | `Infrastructure/Social/.../InstagramProfile*ScrapeAdapter.cs` | **Modified** — per confirmed actor (OQ-1) |
| `ApifyProfileScrapeResolver` | `Infrastructure/Social/Providers/ApifyProfileScrapeResolver.cs` | **Possibly Modified** — actor/variant returning media |
| `SupabasePostMediaArchiver : IClientPostMediaArchiver` | `Infrastructure/Social/ContentGeneration/SupabasePostMediaArchiver.cs` | **New** — download → downscale thumbnail → `PutAsset` → ref |
| `MultimodalAestheticStyleExtractor : IClientAestheticStyleExtractor` | `Infrastructure/Social/ContentGeneration/MultimodalAestheticStyleExtractor.cs` | **New** — vision call via `IAiCompletionGateway`; palette optional |
| `AiVisualDesigner` — vision path + creatives-first + no-grey prompt | `Infrastructure/Social/ContentGeneration/AiVisualDesigner.cs` | **Modified** — reference images as vision blocks; never inject default palette |
| EF: enriched payload + image-ref persistence | `Infrastructure/Persistence/Postgres/...` | **Modified/New** — entity + repository mapping |
| EF migration(s) | `Infrastructure/Persistence/Postgres/Migrations/` | **New (additive)** — enriched payload columns on `profile_analyses` + `profile_analysis_images` table |
| DI registration | `Infrastructure/DependencyInjection/DependencyInjection.cs` | **Modified** — archiver, extractor wiring |

---

## 3. Flow

### 3.1 Profile analysis (async pipeline — produces the substrate)
```
scrape profile ──► map posts (image URLs + captions + children)   [OQ-1 actor]
              └──► persist raw dataset (now incl. media + captions)
archive images ──► download Apify image bytes ─► downscale thumb ─► PutAsset (Supabase) ─► ProfilePostImageRef[]   [best-effort]
extract style ──► multimodal(images + captions) ─► AestheticExtraction { palette?, descriptors, motifs, voice }   [if multimodal route; else skip]
persist ──► enriched payload + image refs on ProfileAiAnalysis (prompt-version bumped)
```

### 3.2 Generation (ClientAesthetic — consumes the substrate)
```
pre-loop ──► read ClientAestheticProfile (enriched: palette?, descriptors, motifs, voice, reference refs)
build ──► ClientAestheticDesignContext { palette?, descriptors, motifs, referenceImages(cap 6), variationSeed=job.Id }
design ──► IVisualDesigner.ComposeAsync(..., aesthetic)  ─► AiVisualDesigner vision path (creatives-first, no-grey)
copy ──► generators receive caption-voice (additive to ConcisionConstraint)
backstop ──► on designer failure, deterministic composition derives colour from profile/creatives (not fixed grey)
```

---

## 4. Degrade matrix

| Condition | Behavior |
|-----------|----------|
| No multimodal `analyze-profile` route | Skip extraction; keep Phase 1 text-descriptor signal; log notice |
| Image download fails (some) | Skip those images, keep the rest; analysis still completes |
| No enriched profile at generation | Fall back to Phase 1 descriptor path |
| Non-multimodal `generate-content` route | Descriptor-only designer (no vision); log notice; generation completes |
| Palette empty / client has none | Derive colour freely from creatives; never fabricate a palette |
| Designer fails entirely | Resilience backstop composition; colour from profile/creatives when available, not fixed grey |
| No own account / no posts | No capture; Phase 1 degrade; generation continues |

---

## 5. Migration verdict

Additive only. (a) Enriched payload columns on `profile_analyses` (nullable JSON for palette/
descriptors/motifs/voice) — nullable, reversible. (b) New `profile_analysis_images` table (durable
refs: id, analysis_id, source_post_id, asset_index, storage_path, content_hash, created_at) — new
table, reversible drop. No data loss on rollback. Coordinated with the analyzer prompt-version bump.

---

## 6. Test-first ordering (Strict TDD)

1. **Domain** — `ProfilePost.ImageUrls`/`Caption`, `ProfilePostImageRef` (RED→GREEN).
2. **Application VOs** — `AestheticPaletteEntry`, extended `ClientAestheticProfile` (palette optional,
   forward-compat), `ClientAestheticDesignContext`.
3. **Ports** — `IClientPostMediaArchiver`, `IClientAestheticStyleExtractor`, `IVisualDesigner` optional
   aesthetic context (contract tests: no provider types in signatures).
4. **Analyzer orchestration** — archive + extract + persist, with degrade scenarios (no-multimodal,
   partial download failure, empty-palette-is-success).
5. **Generation wiring** — design-context build; no-grey colour source; non-multimodal generate
   degrade; reference cap truncation; caption-voice additive to concision.
6. **Infrastructure adapters** — Apify mapping (per OQ-1), archiver, extractor, designer vision path.
7. **EF** — enriched payload + image-ref round-trip; migration.
8. **Wiring + ADR + docs + spec merge + gates.**

---

## 7. Risks

| Risk | Mitigation |
|------|------------|
| Actor returns no media/captions | OQ-1 spike BEFORE build; switch actor or descope; do not ship an empty mapping |
| Apify URL expiry | Archive to Supabase on capture; never persist raw Apify URLs long-term |
| Cost (scrape + storage + vision tokens) | Bounds: N=18, thumbnails, reference cap 6 once-per-job; all owner-tunable |
| Output still looks AI/templated | Strong creatives-first art-director prompt + variation seed; review on a real client before "done" |
| Prompt-version invalidation | Bump only touched keys; accept first-run recompute |
| Privacy / IG ToS on stored images | Keep only latest analysis' thumbnails; scoped path; documented retention (OQ-7) |
| Architecture leak | Vision payload flows via existing multimodal `IAiCompletionGateway`; no provider types inward (gate-enforced) |
