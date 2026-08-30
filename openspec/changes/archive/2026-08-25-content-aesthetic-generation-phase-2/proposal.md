# Proposal: Content Aesthetic Generation (CASE C — Phase 2: Real Visual Fidelity)

## Change ID
`content-aesthetic-generation-phase-2`

## Status
Proposal — DRAFT for owner review. Builds on `content-aesthetic-generation` (Phase 1, archived `apply-complete`). Specs, design, and tasks: TBD after this proposal is approved.

## Why / Problem

Phase 1 turned `BackgroundSourcingPolicy.ClientAesthetic` into an active, non-blocking, refresh-if-stale path — but it ships **text-descriptor-based** aesthetic only. It cannot deliver the owner's actual goal:

> Generated content must be **indistinguishable from what the client already posts** — same visual language, palette, mood, composition and voice — so the client can auto-publish it and it does NOT look AI-made. For `ClientAesthetic` clients we must **copy the client themselves**.

Two facts, verified against the production database for the most recent ClientAesthetic client (`Atlas Editorial`, policy `ClientAesthetic = 3`), prove Phase 1 cannot reach that bar:

1. **We capture ZERO visual or textual signal from the client's posts.** The persisted scrape dataset (`social.profile_analyses.raw_posts_dataset_json`) contains only metadata per post — `postId, mediaType, likesCount, commentsCount, hashtags (empty), isVideo, carouselChildrenCount (null), timestamp`. **No image URLs, no captions, no colors.** This resolves the Phase 1 deferred gate ("Apify actor spike confirming field availability"): the current scrape/mapping does NOT return post images or captions.
2. **The aesthetic signal never reaches the visual layer as real data.** `ClientAestheticProfile` is thin and text-only (`BackgroundDescriptors`, `VoiceTone`, `AverageSlidesPerCarousel`). `AiVisualDesigner` is driven solely by the brand-manual palette/typography; with no manual, the palette is empty and the AI defaults to generic greys.

Without the client's real posts as input, there is no material to learn or reproduce their aesthetic. Phase 2 closes that gap.

## Guiding Principle (owner clarification — load-bearing)

**For `ClientAesthetic`, the client's feed IS the brand manual.** A client having NO brand manual is an expected, valid configuration for this policy — it is the signal to derive style from reading the client, not a degradation to greys. The brand manual remains the source of truth for `ClientDefinedSet` / `AiPalette` (strict, pattern-based clients).

**Palette is NOT assumed to be the spine (owner clarification).** For many clients the colour palette is *inconsequential* — they do not have a strongly marked palette; their identity lives in **the backgrounds/creatives they use** (imagery, composition, treatment). For others the palette IS a defining trait. Therefore the aesthetic profile is **multi-signal**, and the system MUST NOT center on palette:

- The **client's real creatives (reference images/backgrounds) are the primary signal** of their style.
- Palette, style/mood descriptors, composition patterns, motifs and voice are **additional signals, each optional**, weighted by what the client actually exhibits.
- A client with a weak/absent palette must still be emulated well via their creatives; a client with a strong palette has it honoured. No single signal (least of all palette) is treated as mandatory.

Source-of-truth precedence:

- `ClientAesthetic` → **feed-derived multi-signal profile** (creatives-first); brand manual optional/ignored.
- `ClientDefinedSet` / `AiPalette` → brand manual is authoritative (unchanged).

## Fidelity Target (owner clarification)

Not pixel-cloning a single post. **Reproduce the client's visual LANGUAGE with variety inside a coherent sense.** Clients rotate backgrounds for variety but keep a recognizable style; output must vary while staying on-style. The current HTML/CSS → raster engine is adequate for the client's graphic/editorial feeds IF fed the real palette, style descriptors, and reference images. No image-generation model and no template pixel-reuse in this phase.

## Goals

- **Capture real post media + captions.** Scrape and persist, per recent post (and per carousel child), the image URL(s) and caption text. Foundation for everything else.
- **Durable image persistence.** Re-host captured post images in Supabase Storage (Apify presigned URLs expire); persist durable references on the analysis.
- **Multimodal style extraction (Claude).** Feed real post images + captions to a multimodal analysis step and extract a RICH aesthetic profile: role-ordered real palette (hex), style/mood/composition descriptors, recurring motifs, and voice/tone.
- **Feed real style into the designer.** Thread the enriched profile into `AiVisualDesigner` for `ClientAesthetic` jobs, led by **the client's real creatives (reference images / backgrounds) as the primary signal**, complemented by style/composition descriptors, motifs and palette-when-marked; instruct varied-but-coherent output.
- **No-manual is first-class.** For `ClientAesthetic`, source style from the feed profile (creatives-first); never fall back to greys for lack of a manual, and never force a palette the client does not actually have.
- **Stronger copy fidelity.** Thread captured caption voice/style into copy generators for `ClientAesthetic` (additive; concision still wins on length).

## Non-Goals (explicit)

- **Image-generation models / diffusion** (generating photographic backgrounds in the client's style). Out of scope; engine stays HTML/CSS → raster.
- **Pixel-perfect reuse of a client's exact post as a template.** That is `ClientDefinedSet` territory.
- **Photographic feeds** (real photos of the client). Acknowledged as a ceiling of the current engine; not targeted here.
- **Blocking generation on analysis.** Phase 1's non-blocking refresh-if-stale contract is preserved — first run for an un-captured client may be degraded; converges next run.
- **Changing `ClientDefinedSet` / `AiPalette` behavior.** Untouched.

## Approach

1. **Capture (Infrastructure).** Select/configure the Apify actor (or a posts-capable actor) that returns per-post `displayUrl`/`displayResources` and `caption`, including carousel children; extend `InstagramProfileApifyScrapeAdapter` mapping to persist them. Spike first to confirm the chosen actor's fields (Phase 1's open gate).
2. **Persist durably (Infrastructure).** On analysis completion, download the captured images and re-upload to Supabase Storage under a client/analysis-scoped path; store durable URLs (+ content hash) on the analysis. Bound count (N recent posts × M images) for cost.
3. **Extract style (Application + Infrastructure AI).** Add a multimodal step to the profile analyzer: send durable images + captions to Claude; produce an enriched aesthetic payload (palette hex + roles, style/mood/composition descriptors, motifs, voice). Persist on `ProfileAiAnalysis` (schema add → prompt-version bump).
4. **Enrich the profile model (Application).** Extend `ClientAestheticProfile` via the reserved nullable fields (image URLs already designed for; add palette + style descriptors). No breaking change to existing callers.
5. **Thread into the designer (Application + Infrastructure).** For `ClientAesthetic`, build the designer input from the feed profile: palette = feed palette (primary), plus style descriptors and a bounded reference-image set passed as vision blocks; system prompt directs "reproduce this client's visual language with variety, do not look templated/AI."
6. **Precedence + no-grey (Application).** Make palette/style source explicit by policy; remove the empty-palette→grey path for `ClientAesthetic`.
7. **Copy fidelity (Application).** Thread caption-derived voice/style into copy generators for `ClientAesthetic`, additive to `ConcisionConstraint`.

## Affected Areas (intent level)

| Area | Layer | Impact | Description |
|------|------|--------|-------------|
| Apify scrape actor selection + `InstagramProfileApifyScrapeAdapter` | Infrastructure | Modified | Capture per-post `displayUrl`/`displayResources` + `caption` + carousel children |
| `ApifyProfileScrapeResolver` | Infrastructure | Possibly Modified | Actor/variant capable of returning media + captions |
| `Domain/Social/ProfileAnalysis/ProfilePost.cs` | Domain | Modified | Add image URL(s) + caption fields (the Phase-1-deferred fields) |
| Durable image persistence service + port (new) | Application + Infrastructure | New | Download Apify images → re-upload to Supabase Storage; durable refs |
| `social.profile_analyses` / new asset table | Infrastructure | New (additive) | Persist durable image references (path + hash); EF migration |
| `ProfileAiAnalysis` + analyzer prompt | Domain + Application + Infrastructure | Modified | Multimodal extraction: palette hex/roles, style/mood descriptors, motifs, voice; prompt-version bump |
| `ProfileAnalyzerOrchestrator` | Application | Modified | Add multimodal step using durable images (Claude) |
| `ClientAestheticProfile` (+ reader) | Application | Modified | Add palette + style descriptors + image URLs (reserved nullable fields) |
| `AiVisualDesigner` | Infrastructure | Modified | `ClientAesthetic`: feed palette primary + style descriptors + reference images (vision blocks) |
| `ContentPackageOrchestrator` / `GenerationContext` | Application | Modified | Carry enriched profile + policy-driven palette source to the designer |
| `ContentGenerationJobProcessor` | Application | Modified | Policy-driven palette precedence; no-grey for ClientAesthetic |
| Copy generators (`Carousel`/`Story`/`FeedPost`) | Application | Modified | Caption-derived voice/style, additive to concision |
| `analyze-profile` workflow route | Config/Data | Changed by owner | Point to Claude (multimodal) — prerequisite |

## Capabilities (additive deltas)

- `profile-analysis`: capture per-post image URLs + captions (carousel children included); durable image persistence; multimodal style/palette/voice extraction; prompt-version bump.
- `content-generation`: `ClientAesthetic` designer input built from the feed profile (palette primary + style descriptors + reference images); varied-but-coherent direction; no-grey when no manual.
- `background-sourcing-policy`: `ClientAesthetic` palette/style precedence formalized — feed is the source of truth; manual optional.

## Risks

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| **Actor does not return media/captions in profile mode.** Verified the current persisted shape lacks them. | High | Spike actor options FIRST (profile-scraper vs all-in-one vs dedicated posts actor); pick one that returns `displayUrl`/children/caption before building. |
| **Apify image URL durability.** Presigned URLs expire. | High | Re-upload to Supabase Storage on capture; never persist raw Apify URLs as long-term refs. |
| **Cost increase.** Image scrape + durable storage + multimodal tokens + per-format reference images. | Medium/High | Bound posts (N) × images (M); downscale to thumbnails sufficient for style; cap reference images per designer call; surface tradeoffs in spec. |
| **Multimodal token cost / latency on the designer call.** Passing images per format/slide. | Medium | Pass a small curated reference set once per job, not per slide; consider extracted palette/descriptors as the primary signal and images as light reference. |
| **Output still looks templated/AI.** Fidelity is a quality bar, not just data. | Medium | Strong art-director prompt + variety seed; review loop on real clients before declaring done; this phase is graphic/editorial only (documented ceiling). |
| **Prompt-version bump invalidates cached AI outputs.** | Medium | Bump only touched keys; accept first-run recompute cost. |
| **Privacy / IG ToS on storing scraped images.** | Medium | Confirm retention policy; store only what's needed; scoped per org/client; document. |

## Rollback Plan

- Capture/persistence/multimodal steps are additive; disabling the multimodal step and the durable-image capture reverts `ClientAesthetic` to Phase 1 text-descriptor behavior.
- New EF migration(s) are additive (nullable columns / new asset table) — standard reversible drops, no data loss.
- Designer enrichment is gated on policy + presence of enriched profile; absent enriched data falls back to Phase 1 path.
- Prompt-version bumps revert together with their schema fields (coordinated unit).

## Dependencies

- Phase 1 (`content-aesthetic-generation`) non-blocking refresh-if-stale + `ClientAestheticProfile` plumbing (built on).
- `analyze-profile` route on a **multimodal** provider (Claude) — owner is setting this; required for visual extraction.
- Supabase Storage adapter (`IGeneratedAssetStorage` pattern) — reused for durable post-image hosting.
- **Spike dependency:** confirm the chosen Apify actor returns `displayUrl`/`displayResources` + `caption` + carousel children before implementation.

## Success Criteria

- [ ] For a `ClientAesthetic` client, the pipeline captures recent post images (carousel children included) + captions and persists durable copies in Supabase Storage.
- [ ] Profile analysis runs a multimodal step that produces a real role-ordered palette (hex), style/mood/composition descriptors, and voice from the client's actual posts.
- [ ] `ClientAesthetic` generation uses the feed-derived palette as the PRIMARY palette and reference images/style descriptors in the designer; never defaults to greys for lack of a brand manual.
- [ ] Output varies across a package while staying recognizably on the client's style (variety-within-coherence), validated on a real client.
- [ ] Caption-derived voice/style reaches copy generators, additive to `ConcisionConstraint`.
- [ ] Non-blocking contract preserved; provider types do not leak into Domain/Application; all MUST gates pass (Strict TDD).

## Open Questions (need owner / design input)

- **OQ-1 — Actor choice.** Which Apify actor returns post `displayUrl`/`displayResources` + carousel children + `caption` reliably (profile-scraper vs all-in-one vs a dedicated posts actor)? Spike decides.
- **OQ-2 — Sample size & cost.** How many recent posts (N) and images per post (M) do we capture/persist for a good aesthetic signal vs Apify + storage + token cost?
- **OQ-3 — Image fidelity stored.** Full-res vs downscaled thumbnails — thumbnails likely enough for style and far cheaper. Acceptable?
- **OQ-4 — Reference images to the designer.** How many images, and once-per-job vs per-format/slide? (Token cost vs fidelity.)
- **OQ-5 — Primary signal — DIRECTION SET.** The client's real creatives (reference images/backgrounds) are the primary signal; palette is optional and possibly inconsequential. Remaining decision is cost-shaping: how heavily to push images (vision tokens) vs lean on cheaper extracted descriptors as a complement.
- **OQ-8 — Weak/absent palette handling.** When the client has no marked palette, what does the designer use for colour — let it derive freely from the reference creatives, or extract a loose palette only as soft guidance? (Must NOT force an invented brand palette.)
- **OQ-6 — Variety mechanism.** How to guarantee variety across a package while staying on-style (seed-based variation, explicit "vary X" directives)?
- **OQ-7 — Retention/privacy.** How long do we retain scraped client images, and under what policy?
