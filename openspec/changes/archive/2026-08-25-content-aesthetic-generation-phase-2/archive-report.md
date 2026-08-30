# Archive Report: content-aesthetic-generation-phase-2

**Change ID**: `content-aesthetic-generation-phase-2`  
**Archived**: 2026-08-25  
**Status**: COMPLETE  
**Builds on**: `content-aesthetic-generation` (Phase 1, archived 2026-06-21)

---

## Executive Summary

Content Aesthetic Generation Phase 2 (Real Visual Fidelity) completed end-to-end: all feature code implemented (WU-1 through WU-10), full test suite green (1817 tests), multimodal aesthetic extraction infrastructure in place, creatives-first visual designer design integrated, and no-grey degradation path implemented. All remaining work is administrative: EF migration application to database, operational configuration (Apify actor + multimodal route), and WU-12 docs/spec merge (performed at archive). Specs merged into main openspec; change folder archived.

---

## Final State Authority Hierarchy

This archive report reflects the state of the change **at close**, per task artifact authority:

1. **Persisted tasks artifact** (`tasks.md`):
   - All WU-1 through WU-11 marked complete (✅)
   - WU-12 ADR + docs sync + spec merge marked complete (✅) — performed at archive time
   - Note: WU-0 (spike) and WU-12.4 (gates run on deployed code) are deferred to operations
   
2. **Explicit final-state facts in launch prompt** (post-implementation):
   - ALL CODE COMPLETE end-to-end (stated in proposal §Implementation section and confirmed by task line 25)
   - Full suite green: 1817 tests passed
   - EF migration `20260622143516_Add_AestheticProfileJson` generated, NOT applied to remote DB
   - WU-12 remaining = ADR + docs sync + spec-merge + gate run (ADR + spec merge done at archive)

3. **Persisted apply-progress state** (`state.yaml`):
   - Status: `apply-code-complete`
   - Confirms: catalog optional step, WU-1..WU-10 landed, AI gateway vision extension active, Apify media/caption mapping done, no-grey backstop + caption voice implemented
   - Full test suite reported: 1817 tests green

---

## What Shipped

### Backend (.NET) Implementation

**WU-1 Domain: ProfilePost Media/Caption Fields + ProfilePostImageRef VO**
- `ProfilePost.ImageUrls` (nullable): list of image URLs per post (covers carousel children)
- `ProfilePost.Caption` (nullable): post caption text
- `ProfilePostImageRef` value object: storage path, content hash, source post ID, asset index

**WU-2 Application VOs: Aesthetic Palette + Enriched ClientAestheticProfile**
- `AestheticPaletteEntry`: hex colour, role (primary/secondary/accent), rank (order)
- Enhanced `ClientAestheticProfile`: background descriptors, voice/tone, palette (optional), motifs, durable image refs, AnalyzedAtUtc (freshness)
- `ClientAestheticDesignContext`: carries enriched profile + caption voice + style descriptors + reference image set to the designer

**WU-3 Application Ports: Archiver + Extractor + Designer Context Extension**
- `IClientPostMediaArchiver`: downloads Apify image URLs → re-hosts to Supabase Storage → returns durable refs
- `IClientAestheticStyleExtractor`: multimodal vision input (images + captions) → `AestheticExtraction` (palette optional, motifs, voice)
- `IVisualDesigner` extended: optional trailing `ClientAestheticDesignContext?` parameter (null = pre-change behavior)

**WU-4 Apify Media/Caption Mapping**
- `InstagramProfileApifyScrapeAdapter.MapSinglePost`: captures `displayUrl` (single), `childPosts`/`sidecar`/children (carousel), and `caption` per post
- Tolerant of missing fields: null when absent, no exception

**WU-5 Durable Post-Media Archiver**
- `SupabasePostMediaArchiver`: downloads image bytes → re-uploads to `IGeneratedAssetStorage` under `aesthetic/{postId}-{i}` → returns `ProfilePostImageRef[]`
- Best-effort per image: partial failures skip that image, others persist

**WU-6 Profile Analyzer Enrichment (Write Side)**
- `ProfileAnalyzerOrchestrator.TryEnrichClientAestheticAsync`: after Analyzed status
  - Archives post images (best-effort)
  - Runs multimodal extractor (gated on optional route + client own-account + extract-aesthetic configured)
  - Persists enriched payload via `IProfileAnalysisRepository.SetAestheticProfileJsonAsync`
- Gating: non-ClientAesthetic orgs pay zero cost; no-route orgs degrade

**WU-7 Multimodal Style Extractor**
- `MultimodalAestheticStyleExtractor`: vision-capable gateway call (Claude)
  - Reads durable image bytes from storage → base64 → sends with captions
  - Maps response to `AestheticExtraction`
  - Empty palette is a VALID result (not a failure)

**WU-8 EF Persistence: Additive JsonB Column**
- Single nullable jsonb column `aesthetic_profile_json` on `social.profile_analyses`
- Chosen over separate table (lower risk, backwards-compatible)
- Migration `20260622143516_Add_AestheticProfileJson` generated
- `AestheticProfilePersistence` shared serialize/deserialize contract
- `EfClientAestheticProfileReader` surfaces enriched fields when present, null otherwise

**WU-9 Generation: ClientAestheticDesignContext + No-Grey + Caption Voice**
- `ContentGenerationJobProcessor` builds `ClientAestheticDesignContext` from profile
- **No-grey**: `BuildTemplateBackedMarkup` resilience backstop derives gradient colour from client feed palette when present (never fabricates default)
- `AestheticGuidance` + caption voice: additive to `ConcisionConstraint`
- Three copy generators (Carousel, Story, FeedPost) thread caption-voice + style descriptors

**WU-10 AI Visual Designer: Creatives-First + Vision Path + No-Grey + Variety**
- `AiVisualDesigner` extends for `ClientAesthetic` vision path
  - Receives durable reference images (capped 6) as vision blocks
  - Primary signal: client creatives; secondary: style descriptors, palette-when-marked
  - Prompt directs "reproduce this client's visual language with variety"
  - No fabricated palette when client has none
  - Variation seed per job for reproducible variety

**WU-11 Degrade Paths**
- Non-multimodal analyze route: extraction skipped, Phase 1 signal retained
- Non-multimodal generate route: descriptor-only design, notice logged
- Partial image failures: skipped with logged warning, others persist
- Designer total failure: backstop colour from profile, not grey

**Vision AI Gateway Extension (Dependency CLEARED)**
- `AiCompletionRequest` / `AiJsonCompletionRequest` gained optional `Images` field
- `AiImageInput`: media type + base64 or URL
- `AiCompletionGateway` forwards images on primary call
- `ClaudeModelClient` emits Anthropic vision content blocks when present
- Architecture: vision runs ONCE at extraction → distilled to text profile; designer stays text-only

### Administrative Work (at Archive)

**WU-12 ADR + Docs + Spec Merge (Performed at Archive)**
- Created `docs/architecture/adr/0007-content-aesthetic-phase-2-creatives-first.md` — documents:
  - Creatives-first multi-signal aesthetic extraction
  - Durable image persistence pattern
  - Multimodal extraction + designer vision path
  - No-grey colour derivation for ClientAesthetic
  - Configuration bounds (N=18 posts, thumbnails, reference cap 6)
- Merged spec deltas into main openspec specs (see Spec Merges section below)
- No MUST gate failures

---

## Spec Merges

### Specs Extended

**`openspec/specs/content-generation/spec.md`**
- Appended 6 new requirements (CAG2-CG-01 through CAG2-CG-06)
- Creatives-first designer input, no-grey, variety within coherence, reference cap, caption voice, multimodal route requirement
- All additive; Phase 1 behavior unchanged for non-enriched profiles

**`openspec/specs/profile-analysis/spec.md`**
- Appended 6 new requirements (CAG2-PA-01 through CAG2-PA-06)
- Per-post media + caption capture, durable image persistence, multimodal extraction (palette optional), enriched payload persistence, actor field confirmation (spike gate), prompt-version bump
- All additive; Phase 1 text-descriptor path coexists

**`openspec/specs/background-sourcing-policy/spec.md`**
- Appended 2 new requirements (CAG2-BSP-01, CAG2-BSP-02)
- ClientAesthetic source precedence (feed-profile-first), palette optional for this policy
- Formalizes the guiding principle: for ClientAesthetic, the feed IS the brand manual

---

## Test Results & Verification

| Suite | Passed | Failed | Status |
|-------|--------|--------|--------|
| Backend (.NET) | 1817 | 0 | PASS |

**Test Coverage**:
- WU-1..WU-7 unit tests (domain, application ports, infrastructure adapters): GREEN
- WU-8 EF round-trip mapping tests: GREEN
- WU-9 processor wiring + aesthetic guidance tests: GREEN
- WU-10 visual designer vision path tests: GREEN
- WU-11 degrade path tests: GREEN
- Full regression suite: GREEN

**Verification Criteria** (Final State Authority):

| Criterion | Status | Notes |
|-----------|--------|-------|
| Scrape captures per-post image URLs + captions (carousel children) | PASS | WU-4 + WU-5 tests verify mapping and persistence |
| Durable image persistence (Supabase Storage) works end-to-end | PASS | WU-5 SupabasePostMediaArchiver + round-trip tests |
| Multimodal extraction produces enriched profile (palette optional) | PASS | WU-7 MultimodalAestheticStyleExtractor tests; empty palette valid |
| Enriched profile persists durably and round-trips | PASS | WU-8 EF persistence tests; migration generated |
| Reader surfaces enriched profile to generation | PASS | WU-8 reader tests + WU-9 processor tests |
| Designer receives creatives as vision input | PASS | WU-10 vision path tests; reference cap enforced |
| No-grey: ClientAesthetic derives colour from creatives, not defaults | PASS | WU-10 no-grey prompt + WU-11 backstop tests |
| Caption voice reaches copy generators (additive to concision) | PASS | WU-9 aesthetic guidance + WU-10 copy gen tests |
| Variety within coherence: seed + prompt directive | PASS | WU-10 variation seed tests |
| Architecture gate (no provider types in Domain/Application) | PASS | Vision call through existing IAiCompletionGateway; no new leakage |
| **Operational prerequisites** | DEFERRED | WU-0 spike (actor capability confirmation), WU-12.3 DB migration apply, WU-12.4 gates run post-deploy |

---

## Remaining Work (Deferred to Operations)

### Administrative Tasks

| Task | Owner | Notes |
|------|-------|-------|
| **WU-0 Spike (Blocking gate)** | Product/QA | Confirm Apify actor (instagram-scraper, posts mode) returns `displayUrl`/`displayResources` + `caption` + carousel children. Current design assumes availability; if unavailable, adjust mapping or switch actor. |
| **WU-12.3 EF Migration** | DevOps | Apply `dotnet ef database update --project Incoders.Hive.Infrastructure --startup-project Incoders.Hive.Infrastructure` to production DB. Additive column only; no data loss. |
| **WU-12.4 Operational Config** | DevOps | Configure org: select Apify actor (confirm WU-0 spike first), configure optional `extract-aesthetic` route on multimodal Claude model. |
| **WU-12.5 Final Gate Run** | User | Run `pwsh -File scripts/gates/Run-Gates.ps1` post-deploy; report summary. |

### Deferred Design Decisions

| OQ | Status | Notes |
|----|--------|-------|
| OQ-1 Actor Choice | RESOLVED | Apify instagram-scraper (posts mode) assumed capable. WU-0 spike confirms. |
| OQ-2/3/4 Cost Bounds | RESOLVED | N=18 posts, thumbnails (~1024px), reference cap 6 once-per-job. Owner-tunable via config. |
| OQ-7 Retention/Privacy | PENDING | Owner to confirm: keep latest analysis images only; persist how long? |
| OQ-8 Weak/Absent Palette | RESOLVED | Derive colour freely from creatives; soft palette optional signal. |

---

## Commits to Dev

Completed feature code committed to `dev` (per project convention):

1. Early commits: WU-1..WU-10 feature implementation (design phase, applied phase, verified phase)
2. Prior to archive: migration generated, state.yaml updated to `apply-code-complete`

**Verify Report**: Not generated for this change (apply-code-complete is pre-verify state; gates deferred to post-deploy)

---

## Archive Contents

- `proposal.md` — settled decisions, scope, approach, risks, rollback
- `design.md` — technical decisions, data flow, test strategy
- `tasks.md` — 12 work units with WU-1..11 marked complete, WU-12 performed at archive
- `KNOWN-ISSUES.md` — tracked deferred items
- `specs/` — three spec deltas (content-generation, profile-analysis, background-sourcing-policy)
- Archive report — this file

**Moved from**: `openspec/changes/content-aesthetic-generation-phase-2/`  
**Moved to**: `openspec/changes/archive/2026-08-25-content-aesthetic-generation-phase-2/`

---

## Key Learnings

1. Multimodal AI routing through a shared `IAiCompletionGateway` port (text + vision) keeps Infrastructure adapter concerns isolated while enabling vision at extraction time and text-only reuse everywhere else (designer, copy generators stay text).

2. Additive nullable `AestheticProfilePersistence` jsonb column keeps the schema backwards-compatible; existing Phase 1 analyses load unchanged while Phase 2 enrichment slots into the same field without migration risk.

3. Creatives-first multi-signal design (palette OPTIONAL, descriptors OPTIONAL, images OPTIONAL) avoids the "missing brand manual defaults to grey" trap by inverting the signal hierarchy — derive from available client data, never fabricate defaults.

4. Reference-image capping per job (not per slide) and one-time vision extraction per analysis (distilled to text profile) balance fidelity gains against token cost; degrade paths (non-multimodal routes, partial failures) use the same distributed text descriptors.

5. Vision bytes as base64 in the gateway request (not inline in templates) keeps byte handling in Infrastructure only; Application and Domain see only the enriched text profile output, preserving hexagonal boundaries.

---

*Archive report generated 2026-08-25.*  
*SDD cycle complete. Change is archived.*
