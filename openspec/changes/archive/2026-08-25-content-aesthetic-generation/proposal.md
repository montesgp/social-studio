# Proposal: Content Aesthetic Generation (CASE C — Phase 1)

## Change ID
`content-aesthetic-generation`

## Status
Proposal — turns the passive `BackgroundSourcingPolicy.ClientAesthetic` seam into an active, client-aligned generation path using best-available profile analysis with non-blocking background refresh. Specs and design: TBD.

## Why / Problem

The completed `content-background-sourcing-policy` change shipped CASE C (`BackgroundSourcingPolicy.ClientAesthetic`) as a **passive seam only**:

- `ClientAestheticStrategy` + `EfClientAestheticDescriptorReader` read ONLY `ProfileAiAnalysis.DetectedBackgroundImages` (TEXT descriptors) from the latest completed `ProfileAiAnalysis`.
- No profile scrape/analysis is triggered when a ClientAesthetic client generates content — generation silently rides on whatever analysis happens to already exist (possibly months old, possibly none).
- Copy style (`VoiceTone`) and carousel structure (slides per carousel) are completely ignored at generation time: the descriptors feed only the AI background designer, never the copy generators.

The result is that "generate in the client's own aesthetic" does not actually align to **how the client currently posts**. The gap versus the owner's intent:

1. **Stale or absent signal, silently.** If the client has never been analyzed (or was analyzed long ago), CASE C degrades to whatever text descriptors exist, with no mechanism to ever refresh them.
2. **Copy style ignored.** The client's voice/tone derived from their feed never reaches the copy generators — only the background designer.
3. **Carousel structure ignored.** Nothing reflects how many slides the client typically uses per carousel.

This change makes ClientAesthetic generation actually derive alignment from the client's recent posts, while keeping the async analysis pipeline non-blocking.

## Goals

- **Refresh-if-stale, non-blocking.** At generation time for a ClientAesthetic client, use a fresh analysis when present; if stale or missing, generate NOW with best-available data and fire-and-forget a profile re-analysis so the NEXT generation is fresh. The generation job NEVER blocks waiting on the analysis pipeline (channel + BackgroundService, takes minutes).
- **Richer ClientAestheticProfile** threaded into BOTH the AI background designer AND the copy generators: background text descriptors + voice/copy style + average slides per carousel.
- **Server-derived AverageSlidesPerCarousel** computed from carousel child counts (no AI needed), captured in the profile scrape mapping.
- **Wider post sample.** Increase `maxPostsPerProfile` from 12 toward 15–20 so the aesthetic signal is derived from a more representative window (Apify cost impact noted).
- **Future-proof domain model.** The new aesthetic profile type is designed to accept real post-image URLs LATER without a breaking change (e.g. nullable fields), but does NOT implement image capture now.

## Non-Goals (explicit)

- **Real client background IMAGES + image persistence (Approach B).** Capturing real per-post image URLs (`displayUrl`/`displayResources`) and persisting them is a **separate Phase 2 follow-up**, GATED on an Apify actor spike (confirm field availability) AND likely-short-lived presigned-URL durability. This change only ships text-descriptor-based aesthetic guidance.
- **Capturing post CAPTIONS for deeper copy inference.** Another mapping gap; noted as a future follow-up, not in this slice.
- **Blocking / "waiting-for-analysis" generation job state.** Explicitly rejected. Generation proceeds immediately with best-available data; refresh is always background fire-and-forget.
- **Per-org configurable staleness threshold or post-limit (post-limit only).** The staleness threshold is now per-client configurable (see Affected Areas); per-org post-limit configuration remains a future option.
- **Hard constraints from the aesthetic signal.** AverageSlidesPerCarousel and copy-style guidance are advisory hints; no hard rejection of AI output. Copy-style guidance is ADDITIVE and MUST coexist with the existing advisory `ConcisionConstraint` (it does not replace concision).

## Phased Scope (this change = Phase 1)

| Phase | Scope | Status |
|-------|-------|--------|
| **1 (THIS change)** | Non-blocking refresh-if-stale orchestration; ClientAestheticProfile (background descriptors + voice/copy style + avg slides) threaded into background designer + copy generators; server-derived AverageSlidesPerCarousel from `childrenCount`; raise `maxPostsPerProfile` to 15–20; domain model ready for image URLs. | Proposed |
| **2 (deferred)** | Real client background IMAGES (Approach B): capture per-post `displayUrl`/`displayResources`, persist durable copies (presigned URLs expire), feed real images into the designer. | Deferred — gated on Apify actor spike + URL durability decision |
| **3 (deferred)** | Post CAPTION capture for deeper copy-style inference. | Deferred — another Apify mapping gap |

## Behavior: Non-Blocking Refresh-if-Stale

At the `ContentGenerationJobProcessor` pre-loop injection point (where policy + descriptors are already resolved, around line 256), when `policy == ClientAesthetic`:

1. **Resolve the client's own account** via `IClientOwnAccountRepository` (the processor only has `ClientId`; an analysis trigger needs the account to scrape — mirroring `AnalyzeProfileHandler`).
2. **Check freshness** of the latest `Analyzed` `ProfileAiAnalysis` for `(orgId, clientId)` against the client's effective staleness threshold: `client.AestheticRefreshThresholdDays ?? 30` (system default **30 days**).
3. **If stale or missing:** enqueue a fresh profile analysis via the existing `IProfileAnalysisJobQueue` seam (create a `ProfileAnalysis` aggregate, persist, enqueue `ProfileAnalysisJob`) — **fire-and-forget**.
4. **Generate NOW regardless** with best-available data:
   - Fresh analysis present → use it.
   - Stale analysis present → use the stale data (the refresh updates the NEXT run).
   - No analysis at all → degrade gracefully (e.g. Creative / AI-palette behavior) and still fire the refresh.
5. The generation job **never waits** on the analysis pipeline.

This guarantees the user always gets output immediately, and the aesthetic signal converges to fresh over subsequent generations.

## ClientAestheticProfile Model (image-URL-ready)

A new Application-layer value object replacing the flat `IReadOnlyList<string>` descriptor return. Designed so Phase 2 image URLs slot in via nullable fields with NO breaking change:

```
public sealed record ClientAestheticProfile(
    IReadOnlyList<string> BackgroundDescriptors,   // Phase 1: text descriptors (DetectedBackgroundImages)
    string? VoiceTone,                             // Phase 1: copy-style signal
    double? AverageSlidesPerCarousel,              // Phase 1: server-derived stat
    DateTime? AnalyzedAtUtc                        // freshness provenance
    // Phase 2 (future, additive, nullable):
    // IReadOnlyList<string>? BackgroundImageUrls
);
```

Evolution of `IClientAestheticDescriptorReader`: either widen it to return `ClientAestheticProfile`, or introduce a sibling `IClientAestheticProfileReader` that coexists until the old one is retired (decide in design — the existing interface has a single caller, `ContentGenerationJobProcessor`). Threading: the profile flows into the `CarouselBackgroundPlan` (designer side, already wired via `BackgroundSelection.ClientAesthetic`) AND into the copy generators via `GenerationContext` / `GenerationPromptContext` (voice-tone + avg-slides hints), additive to `ConcisionConstraint`.

## Approach

Approach C from exploration (phased text-first). Implement the non-blocking refresh-if-stale orchestration as a new Application service/port invoked from the `ContentGenerationJobProcessor` pre-loop seam; reuse the existing `IProfileAnalysisJobQueue` trigger pattern from `AnalyzeProfileHandler`. Evolve the descriptor reader into a richer `ClientAestheticProfile` reader that also surfaces `CompletedAtUtc` for the freshness check. Capture carousel `childrenCount` in `InstagramProfileApifyScrapeAdapter.MapSinglePost` (confirm availability during apply; treat avg-slides as best-effort with the same actor caveat if absent) and compute `AverageSlidesPerCarousel` in `IProfileMetricCalculator` as a pure server stat. Raise `maxPostsPerProfile` to 15–20. Thread the richer profile into the background designer and into the copy generators as additive advisory guidance, and bump the relevant prompt version(s) only if `ProfileAiAnalysis` schema or copy prompts actually change.

## Affected Areas (intent level)

| Area | Layer | Impact | Description |
|------|-------|--------|-------------|
| `Domain/Social/ProfileAnalysis/ProfilePost.cs` | Domain | Modified | Add carousel child-count field (image URL field deferred to Phase 2) |
| `Domain/Social/ProfileAnalysis/ProfileStats.cs` | Domain | Modified | Add `AverageSlidesPerCarousel` server-derived stat |
| `Domain/Social/ProfileAnalysis/ProfileAiAnalysis.cs` | Domain | Possibly Modified | Only if richer copy-style signals beyond `VoiceTone` are needed |
| `Application/.../ClientAestheticProfile` (new) | Application | New | Value object: descriptors + voice/copy style + avg slides + AnalyzedAtUtc (image-URL-ready) |
| `Application/.../IClientAestheticDescriptorReader.cs` | Application | Modified/New | Evolve to richer profile reader (or sibling `IClientAestheticProfileReader`) exposing freshness |
| `Application/.../IClientAestheticEnsureFreshService` (new) + port | Application | New | Refresh-if-stale orchestration (freshness check + fire-and-forget enqueue) |
| `Application/.../IProfileAnalysisRepository.cs` | Application | Modified | Add `GetMostRecentAnalyzedForClientAsync` (or expose `CompletedAtUtc`) for freshness |
| `Application/.../Pipeline/ContentGenerationJobProcessor.cs` | Application | Modified | Pre-loop: resolve own account, ensure-fresh, read richer profile, thread to generators |
| `Application/.../Shared/GenerationPromptContext.cs` + `GenerationContext` | Application | Modified | Carry aesthetic profile (voice/copy style, avg slides) into prompt context, additive to concision |
| `Application/.../Formats/{Carousel,Story,FeedPost}Generator.cs` | Application | Modified | Add advisory copy-style + avg-slides guidance (coexist with `ConcisionConstraint`) |
| `Application/.../ProfileAnalyzer/.../ProfileAnalyzerOrchestrator.cs` + `ProfileAnalysisResult.cs` | Application | Possibly Modified | Only if richer copy-style schema/prompt is added (prompt-version bump) |
| `Application/.../IProfileMetricCalculator` / impl | Application | Modified | Compute `AverageSlidesPerCarousel` from child counts |
| `Infrastructure/.../Apify/InstagramProfileApifyScrapeAdapter.cs` | Infrastructure | Modified | Capture `childrenCount` per carousel post (confirm actor field availability) |
| `Infrastructure/.../Apify/ApifyProfileScrapeResolver.cs` (~line 269) | Infrastructure | Modified | Raise `maxPostsPerProfile` 12 → 15–20 |
| `Infrastructure/.../EfClientAestheticDescriptorReader.cs` | Infrastructure | Modified | Return richer profile + expose `CompletedAtUtc`/freshness |
| `Infrastructure/.../AiVisualDesigner.cs` | Infrastructure | Possibly Modified | Aesthetic profile already arrives via `BackgroundSelection.ClientAesthetic`; extend if richer fields used |
| EF migration | Infrastructure | Possibly New | Only if a `ProfileAiAnalysis` schema field is added; child count may live on scrape JSON / stats only |
| `Domain/Social/Clients/Client.cs` | Domain | Modified | Add nullable `AestheticRefreshThresholdDays` (`int?`); `Create`/`Restore` + `SetAestheticRefreshThresholdDays` setter; validation: min 1 day, max 365 days |
| EF migration (`social.clients`) | Infrastructure | New (additive) | One nullable column `aesthetic_refresh_threshold_days smallint NULL` on `social.clients`; no data loss |
| `SocialClientData` (EF entity) | Infrastructure | Modified | Add `AestheticRefreshThresholdDays` property; map in `ToData`, `Update`, `ToDomain` — ALL THREE methods (predecessor bug guard) |
| `ApiService` — client ABM endpoints | Api | Modified | `SocialClientResponse`, `CreateSocialClientCommand/Handler`, `UpdateSocialClientCommand/Handler`, `MapSocialClientsEndpoints` — expose `AestheticRefreshThresholdDays`; mirror `BackgroundSourcingPolicy` pattern |
| `apps/web` — client ABM form | Web | Modified | Client form, `use-client-form` hook, `types/social-clients` API lib — add `aestheticRefreshThresholdDays` field; mirror `BackgroundSourcingPolicy` pattern |

## Capabilities

### Modified Capabilities (additive deltas)

- `profile-analysis`: capture carousel child counts in the scrape mapping; compute `AverageSlidesPerCarousel` server stat; raise `maxPostsPerProfile`; expose `CompletedAtUtc`/freshness for on-demand refresh.
- `content-generation`: ClientAesthetic path becomes active — non-blocking refresh-if-stale; richer `ClientAestheticProfile` threaded into the background designer AND copy generators (additive to advisory concision); avg-slides advisory hint.
- `background-sourcing-policy`: `ClientAesthetic` strategy goes from passive descriptor read to active best-available + background-refresh behavior; per-client `AestheticRefreshThresholdDays` configures the staleness window (system default 30 days).
- `client-settings` (ABM): `Client` domain aggregate gains `AestheticRefreshThresholdDays` (`int?`); persisted in `social.clients` (one additive migration column); exposed in client create/update ABM surface.

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| **UNCONFIRMED actor `childrenCount`.** Not verified the all-in-one actor returns carousel child counts in profile-scraper mode. | Medium | Confirm during apply; if absent, treat `AverageSlidesPerCarousel` as best-effort/null and document. Smaller mapping change than image URLs. |
| **Async-pipeline non-blocking UX.** First-ever generation for an un-analyzed client uses degraded data; the "fresh" result only appears next run. | Medium | Explicit non-blocking design + graceful degrade; communicate "refreshing in background" semantics in spec; never block the job. |
| **Image-URL durability (Phase 2).** Apify presigned URLs likely expire; can't persist raw URLs. | Medium (Phase 2 only) | Out of scope here; Phase 2 gated on actor spike + a durability decision (re-upload to Supabase Storage). |
| **`maxPostsPerProfile` cost.** 12 → 20 increases Apify cost per scrape ~1.7x. | Medium | System-default value; tune to 15 if cost-sensitive; flag the tradeoff in spec. |
| **OwnAccountId resolution.** Processor only has `ClientId`; on-demand trigger needs the client's own account. | Medium | Resolve via `IClientOwnAccountRepository` in the pre-loop seam (mirror `AnalyzeProfileHandler`); handle missing-own-account by skipping refresh + degrading. |
| **Concision + aesthetic copy coexistence.** Copy-style guidance could contradict `ConcisionConstraint`. | Medium | Make aesthetic copy guidance strictly ADDITIVE/advisory; concision wins on length; cover with tests (Strict TDD). |
| **Prompt-version bump if `ProfileAiAnalysis` schema changes.** Adding copy-style fields invalidates cached AI outputs. | Low/Medium | Only add schema/prompt fields if needed; bump only the touched version key; accept first-run cost. |

## Rollback Plan

- The ensure-fresh service is additive and isolated to the ClientAesthetic pre-loop seam; disabling its call reverts to today's passive descriptor read.
- `maxPostsPerProfile` is a single constant — revert restores 12.
- Carousel child-count capture and `AverageSlidesPerCarousel` are additive stats; if unused, generation falls back to no avg-slides hint.
- The richer profile reader can fall back to returning only `BackgroundDescriptors` (existing behavior).
- The `aesthetic_refresh_threshold_days` column on `social.clients` is nullable — rolling it back is a standard single-column `DROP COLUMN` with no data loss.
- If a `ProfileAiAnalysis` schema field + migration is added, reverting requires reverting the prompt-version bump and the migration together (coordinated rollback unit).

## Dependencies

- Existing profile-analysis pipeline (`AnalyzeProfileHandler` pattern, `IProfileAnalysisJobQueue`, `ProfileAnalyzerOrchestrator`) — reused for on-demand refresh, read + trigger only.
- Existing `IClientOwnAccountRepository` — to resolve the client's own account for the refresh trigger.
- Existing `ContentGenerationJobProcessor` pre-loop injection point + `BackgroundSelection.ClientAesthetic` / `CarouselBackgroundPlan` threading (from `content-background-sourcing-policy`).
- **OPEN dependency (Phase 2):** Apify actor spike confirming `displayUrl`/`displayResources` + URL durability before any real-image work.
- **Soft dependency (Phase 1):** confirm `childrenCount` availability for avg-slides (best-effort if absent).

## Success Criteria

- [ ] ClientAesthetic generation for a client with a stale/missing analysis produces output IMMEDIATELY (no blocking) and fires a background re-analysis so the next run is fresh.
- [ ] A fresh analysis (within the client's effective staleness threshold — `client.AestheticRefreshThresholdDays ?? 30` days) is used directly when present; stale/missing degrades gracefully without failing the job.
- [ ] The client's own account is resolved via `IClientOwnAccountRepository` to drive the on-demand refresh; missing-own-account degrades safely.
- [ ] `ClientAestheticProfile` carries background descriptors + voice/copy style + `AverageSlidesPerCarousel` + `AnalyzedAtUtc`, and is threaded into BOTH the background designer AND copy generators.
- [ ] Copy-style guidance coexists with `ConcisionConstraint` (additive, advisory) — concision is not replaced.
- [ ] `AverageSlidesPerCarousel` is computed as a server-derived stat from carousel child counts (no AI), best-effort if `childrenCount` is unavailable.
- [ ] `maxPostsPerProfile` raised to the chosen 15–20 value.
- [ ] The aesthetic profile model accommodates Phase 2 image URLs via nullable fields with NO breaking change.
- [ ] Provider types (EF, Apify, AI) do not leak into Domain/Application (architecture gate green).
- [ ] All MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`); Strict TDD — domain/application rules tested before implementation.

## Open Questions (for spec/design)

- **OQ-1 — Staleness threshold N.** System default: **30 days** (`AestheticFreshness.DefaultStalenessThresholdDays = 30`). Per-client override: `client.AestheticRefreshThresholdDays` (nullable `int?`; `null` = use system default). Validation: min 1 day, max 365 days. ABM surface added to client Create/Update. RESOLVED.
- **OQ-2 — AverageSlidesPerCarousel: soft hint vs hard constraint.** Soft advisory hint proposed. How to handle fractional (1.5) or extreme (10+) averages? Clamp range?
- **OQ-3 — VoiceTone delivery.** Read the aesthetic `VoiceTone` directly into `GenerationContext`/prompt at generation time, OR impute it into `BrandManual.VoiceAndTone` (existing impute path already flows to the prompt)? Direct-read keeps it advisory and decoupled.
- **OQ-4 — `childrenCount` availability.** Confirm during apply whether the all-in-one actor returns carousel child counts in profile-scraper mode; if not, avg-slides is best-effort/null.
- **OQ-5 — Degrade-when-no-analysis behavior.** When NO analysis exists at all, exactly which fallback (Creative / AI-palette) does ClientAesthetic degrade to, and is that logged/surfaced?
- **OQ-6 — Reader evolution shape.** Widen `IClientAestheticDescriptorReader` to return `ClientAestheticProfile` (breaking, single caller) vs introduce a sibling `IClientAestheticProfileReader` (coexist then retire)?
- **OQ-7 — `maxPostsPerProfile` final value.** 15, 18, or 20 — balancing aesthetic signal richness against Apify cost.
- **OQ-8 — Refresh debounce.** Should repeated generations within a short window enqueue multiple refreshes, or debounce to one in-flight refresh per client?
