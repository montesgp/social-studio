# Known Issues & Pending Work — content-aesthetic-generation-phase-2

Status snapshot (2026-06-23): the aesthetic capture pipeline WORKS end to end (profile analysis →
durable image archive → multimodal extraction → `aesthetic_profile_json` persisted; the designer
receives the client's images as vision and generates flat-template slides "approximating" the
client's style). The items below are NOT yet resolved and are recorded here for a future agent.

---

## ISSUE 1 — Generated carousels have NO creative/photographic image slides  [HYBRID REVERTED — it copied the client verbatim]

> UPDATE 2026-06-23 (later): the data-URI hybrid was REVERTED. Compositing the client's REAL finished
> slides as backgrounds reproduced their posts verbatim — the reference images already contain the
> client's own text, so our copy landed ON TOP (text-over-text), and a flat-colour reference slide was
> likewise copied. That is COPYING, not emulating style, and it contradicted the designer's own A6
> contract ("create new art; do not reproduce verbatim"). Removed `BuildClientCreativeBackgroundsAsync`
> + `ReadAsDataUriAsync` + the `useClientCreative` branch. Now ALL ClientAesthetic slides go through
> `AiVisualDesigner`, which receives the client's reference creatives as VISION input and CREATES NEW
> art in the client's visual line. The "real photographic slides" goal remains open, but the correct
> path is NOT verbatim compositing — options: an image-generation model, or a curated, text-free
> reference image used sparingly. Do NOT reintroduce verbatim compositing of finished client slides.

> UPDATE 2026-06-23 (earlier, superseded): IMPLEMENTED via a hybrid that composited the client's REAL
> creative images as backgrounds of ~2 slides. Superseded by the revert above.

**Symptom:** For a ClientAesthetic client (e.g. Atlas), generated carousels only ever contain
flat colour/template slides. The client actually mixes ~2 creative/photographic slides + 2-3 flat
template slides per carousel. The creative slides never appear.

**Root cause:** `AiVisualDesigner` produces HTML/CSS (gradients, shapes, text) rendered to PNG. It
**cannot generate photographic/creative imagery**. So "creative slides like the client" can never be
*generated as new art* by this engine. Sending the client's images as vision (done) only nudges the
*style* of the flat slides; it does not produce photographic slides.

**Approach to fix (hybrid — use the client's REAL creatives as backgrounds for 1-2 slides):**
- The enriched aesthetic profile already persists durable reference images
  (`aesthetic_profile_json.referenceImages[]` → storage paths in the GENERATED-ASSETS bucket).
- For a ClientAesthetic carousel, designate 1-2 slides to use a real reference image AS the slide
  background (composite text over it), and keep the rest AI-generated flat-template slides — mirroring
  how the client mixes creative + template slides.
- **Constraint discovered:** the durable images live in a PRIVATE Supabase bucket (accessed via the
  service-role key in `SupabaseStorageAdapter`). Chromium cannot fetch them by URL. So the image must
  be inlined as a **data-URI** (read bytes via `IGeneratedAssetStorage.OpenAsync` → base64 →
  `<img src="data:image/jpeg;base64,…">`). The existing `BuildTemplateBackedMarkup` already composites
  `<img src=…>` + text, so a data-URI slots in.
- **Files:** `ContentGenerationJobProcessor` (render loop — build a per-slide background that, for the
  chosen 1-2 slides, carries the client's reference image as a data-URI); reuse the
  `BackgroundSelection.ForcedDefault`/`References` + `BuildTemplateBackedMarkup` compositing path;
  optionally have `IClientAestheticStyleExtractor` tag which reference images are "creative" vs "flat"
  so the right ones are chosen as backgrounds.
- Decision needed: which/how-many slides get a real-image background; whether to downscale.

---

## ISSUE 2 — Regeneration "shows the same slides / nothing to do with the style"  [ROOT CAUSE FOUND — studio refresh fixed]

**Symptom:** A FRESH generation (new case) came out much closer to the client's style; a
REGENERATION of an existing carousel came out "nothing to do with" the style / "exactly the same slides".

> UPDATE 2026-06-23: ROOT CAUSE was NOT an enrichment gap. The studio never refreshed a regenerated
> format. `MaterializeAnalysisCommandHandler`'s incremental path only ADDED new format keys; a format
> already in the set kept its frozen `ContentDraft` snapshot, so the regenerated `CompositionSpecJson`
> + rendered assets were never read back. The user therefore always saw the pre-regeneration slides —
> regardless of whether the regenerated output matched the aesthetic. FIXED: added
> `ContentItem.RefreshFromSource` (guarded to pristine `Draft` items only — edited items are never
> clobbered) and a refresh loop in the materialize handler (refreshes when `result.UpdatedAt >
> item.UpdatedAtUtc`). Tests: `MaterializeAnalysisRegenerationTests`.

**Enrichment symmetry — VERIFIED, no gap:** generation and regeneration share the SAME
`ContentGenerationJobProcessor.ProcessInternalAsync`. The aesthetic profile read
(`EfClientAestheticProfileReader.GetProfileAsync(orgId, clientId)` → latest `Analyzed` analysis) and
the `ClientAestheticDesignContext` build run in the pre-loop UNCONDITIONALLY (before the
`formatsNeedingAi` branch), and the regenerated format goes through the same render loop +
`AiVisualDesigner.ComposeAsync(..., aestheticDesignContext)`. Regeneration is enriched identically to
fresh generation. There is no separate regeneration path to wire.

**To verify at runtime (now unblocked by the studio fix):** rebuild + run, regenerate Atlas's
carousel, re-open the studio and confirm the NEW slides appear and reflect the aesthetic. WATCH-OUT:
regenerated assets are re-stored at the SAME storage path (`…/{folder}/slide_N.png`), so if slides
still look stale in the browser AFTER the studio item refreshes, suspect HTTP/CDN image caching on the
identical URL (add a cache-buster / version query param on the asset URL) — that is a separate frontend
concern, not an enrichment gap.

---

## ISSUE 3 — Profile scrape returns only ~12 posts regardless of the configured limit

**Symptom:** `postsReceived=12` even after wiring per-client `MaxPostsToAnalyze` and sending a limit.

**Findings:** The official actor `apify/instagram-profile-scraper` input previously sent NO post-count
param; we now send `resultsLimit` (GUESSED name — UNVERIFIED). It still returns ~12, so either the
actor caps at ~12 in profile mode, the param name is wrong, or the client truly has ~12 recent posts.
**To do:** confirm the official actor's real post-limit param via its Apify docs/a test run; if it
genuinely caps, evaluate the `get-leads/all-in-one-instagram-scraper` (must first confirm it returns
post images). Files: `InstagramProfileApifyScrapeAdapter.BuildProfileActorInput` (official branch),
`ApifyProfileScrapeResolver`.

---

## PENDING (surfacing / UX — not bugs, just not built yet)

- **Per-client `MaxPostsToAnalyze` ABM + web** — backend + migration + scrape threading are DONE; the
  surfacing (Create/Update commands+handlers, `SocialClientResponse`, endpoint, web client form) is a
  mirror of the existing `AestheticRefreshThresholdDays` field. Until then, set via SQL.
- **Caption in Content Studio** — `publicationDescription` is generated, in the package manifest, and
  in `format_results.output_json` (DB). NOT surfaced in the studio item field / simulation. Needs:
  caption field on the content item (+ EF column + migration), materialize copies it, queries/preview
  return it, web shows it editable.
- **Studio editor: Delete key** to remove a selected component + **Ctrl+Z** undo. (Web.)
- **Remove autosave** — save only on the "Save design changes" button for now; add an
  `autosaveEnabled` client/system setting later. (Web + flag.)

---

## CTA & signature — client-directive precedence (particular → general)  [IMPLEMENTED]

> 2026-06-23: editorial decisions (CTA, signature/@handle) now follow a specific-over-general chain.
> The brand-manual notes (and explicit client fields) are AUTHORITATIVE directives passed to BOTH the
> copy generators and the visual designer: honored literally and overriding inferred defaults, including
> PROHIBITIONS ("do not sign with the @handle" wins even if the account does it). When the directives
> are silent, behavior falls back to inference from the client's analyzed content/style.
> - `GenerationPromptContext.ClientDirectivePrecedence` (shared clause), threaded into Carousel/Story/
>   FeedPost generators; CTA rule no longer fires on generic "analysis mentions a CTA".
> - `AiVisualDesigner`: brand notes injected as an authoritative directive block; removed the
>   unconditional "anchor CTA and signature"; added a NO-INVENTED-TEXT rule so the designer never
>   reproduces the @handle/username it sees in the reference images unless a directive requires it.
> - Prompt versions bumped: carousel.v6, story-single.v4, feed-single.v5.
> FOLLOW-UP: `visual-designer/v1` prompt version was NOT bumped (it would touch ~10 pinned tests); bump
> to v2 in a dedicated slice. FUTURE: dedicated client signature field / digital-signature image.

## DONE this phase (for context)
Optional `extract-aesthetic` workflow step; capture post images+captions; durable archive (depth-first,
whole-carousel sampling, up to 28 images); multimodal extraction (palette optional); enriched
`ClientAestheticProfile` + persistence (migration `Add_AestheticProfileJson`); designer receives the
client's images as vision (create-new, not copy); no-grey fallback; ensure-fresh requires aesthetic;
regeneration rewritten async; conditional CTA (never invent); per-client `MaxPostsToAnalyze` backend +
migration `Add_MaxPostsToAnalyze`.
