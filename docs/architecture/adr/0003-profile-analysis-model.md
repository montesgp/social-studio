# ADR 0003 - Profile Analysis Model

## Status
Accepted

## Context
The single-analyzer ("Mundo 1") analyzes ONE piece of content (a video URL) end to end: a single-snapshot scrape contract (`SocialContentSnapshot`), a single-video AI prompt (`single-analyzer.analysis.v1`), and a single persisted record (`social.single_content_analyses`). There is no way to analyze an entire Instagram account — its audience size, posting cadence, engagement, content mix — or to compare that posture against a client's stated objectives.

The new "Mundo 2" feature (`single-analyzer-profile-analysis`) scrapes a FULL Instagram profile via Apify, runs the organization's default AI model for an expert marketing analysis, persists the result, renders a dynamic dashboard, and optionally imputes detected fields into the client's brand manual / content plan / general strategy. This is an architecture change (`require_adr_for_architecture_changes: true`) introducing a new persisted artifact, a new extraction routing seam with an automatic-fallback capability the pipeline lacks today, and the first frontend charting dependency.

Three facts from a live Apify probe (engram obs #513) constrain the design:
1. The default actor (`get-leads~all-in-one-instagram-scraper`) returns an ARRAY of 3 items `[profile, quality_report, bandwidth_report]`; the profile must be selected by `resultType == "profile"`, NOT index `[0]`. The existing extractor hardcodes `doc.RootElement[0]`.
2. `latestPosts[]` can include tagged/coauthored NON-owner posts; owner metrics require filtering by `ownerId == instagramId`.
3. A RESIDENTIAL proxy is mandatory (IG blocks datacenter IPs); `videoViewCount` is video/reel-only (null for image/carousel).

`actor_mappings_json` is ASP.NET Data-Protection-encrypted and unreadable from the DB, so the required mapping entries are documented for operators rather than auto-provisioned.

## Decision

Introduce a persisted, first-class **Profile Analysis** aggregate plus a dedicated, fallback-aware profile-scrape seam, following the existing hexagonal sibling-aggregate pattern (keyed by FK, `OrganizationId`-scoped, no EF navigations, provider/EF types confined to Infrastructure) and the configurable-AI-port contract. Mundo 1 is explicitly NOT modified beyond a top-level frontend mode switch.

1. **`ProfileAnalysis` first-class aggregate** — New aggregate in a NEW namespace `Domain/Social/ProfileAnalysis/` (NOT `SingleAnalyzer/Profile/`, which holds the unrelated `SocialContentAnalysisProfile` strategy). Own table `social.profile_analyses` + own EF migration. Persistence is HYBRID: it stores the raw owner-filtered posts dataset AND server-derived stats AND the AI analysis as JSON string columns, mirroring the established `SingleContentAnalysisData.*Json` pattern, so the dashboard re-derives/charts without re-scraping. Status enum: `Requested → ScrapingProfile → ProfileScraped | ProfileScrapedPartial → Analyzing → Analyzed | Failed`. (Rejected: reuse `SingleContentAnalysisData` with a discriminator — bloats Mundo 1 and breaks its single-video invariants; fully typed post/stat columns — rigid against provider-specific extras like `_degraded`, `images[]`.)

2. **Dedicated profile-scrape port, not an extension of the single-video extractor** — New Application port `IProfileScrapeResolver` returning a profile + posts[] + reels[] + aggregates + diagnostics, implemented by a new Infrastructure `ApifyProfileScrapeResolver` + `InstagramProfileApifyScrapeAdapter`. It does NOT pass through `ApifySocialContentExtractor.ExtractAsync` (which hardcodes `[0]` and maps to a single-snapshot shape). Actor routing REUSES the proven `actor_mappings_json` mechanism via a request carrying `PlatformKey "instagram"` + `CapabilityKey "profile"`; `TryResolveMappedActor` matches `VariantKey` against the capability key, so a dedicated **`variantKey "profile"`** resolves the profile actor without colliding with the single-video `instagram` mapping and without any schema change. (Rejected: add a mode flag to `ISocialContentExtractor`/`InstagramApifyScrapeAdapter` — forks the `[0]` array handling and single-snapshot mapping internally, risking Mundo 1 regressions; `SocialContentSnapshot` has no slot for posts[]/aggregates.)

3. **Automatic in-run backup-actor fallback** — `ApifyProfileScrapeResolver` resolves both the default and backup actor ids for `(networkKey "instagram", variantKey "profile")`, runs the default, and on failure (transport / timeout / non-2xx / empty / parse) records a diagnostic and retries the backup ONCE; either output is normalized through the same adapter mapping. This is a NEW capability scoped to the profile variant — Mundo 1 has no fallback and is untouched. The two required, operator-configured entries are: `[{networkKey:"instagram",variantKey:"profile",actorId:"get-leads~all-in-one-instagram-scraper"},{networkKey:"instagram",variantKey:"profile",actorId:"apify~instagram-profile-scraper"}]` (Data-Protection-encrypted in DB). (Rejected: fail-fast like Mundo 1 — leaves the HIGH-risk encrypted-mapping mismatch unmitigated.)

4. **`profile-analyzer` AI flow with strict output schema** — New `IContentAnalysisProfile` (`FlowKey "profile-analyzer"`, `PromptVersionKey "profile-analyzer.analysis.v1"`, `AiCapabilityKey "ai-analysis"`) driving `IAiCompletionGateway.CompleteJsonAsync<ProfileAnalysisResult>` through the org default model (`IDefaultAiSelector`). The output schema (summary, audienceProfile, contentPillars[], topContentTypes[], postingDays[], detectedBrandColors[{name,hex}], detectedFonts[], detectedBackgroundImages[], voiceTone, recommendedKpis[], monthlyObjectiveSuggestion, contentStrategySuggestion, strengths[], weaknesses[], opportunities[], confidence, warnings[]) is fixed so each field maps to a concrete impute target. The AI model stays a configurable port; no hardcoded model. (Rejected: reuse `single-analyzer.analysis.v1` — wrong, idea-generation-shaped output.)

5. **Impute reuses existing write commands, per-field and idempotent** — One `ImputeProfileAnalysisCommand` carries the analysis id + selected field targets; the handler loads the current brand manual / content plan / general strategy, merges only the selected fields, and calls the EXISTING `Update`/upsert commands. Each impute is opt-in, requires a linked client, and is idempotent. No new write commands are introduced. (Rejected: dedicated impute write paths — duplicate validated invariants and violate the out-of-scope boundary; auto-apply — pollutes real client data.)

6. **Server-derived dashboard + render-time comparison** — A dashboard query returns server-derived stats (medianViews [video-scope only], engagementRate, avgViewsByWeekday, contentTypeMix, postingCadence, follower/post counts) plus the raw owner-filtered posts for client-side dynamism; a separate comparison query loads the linked client's `ContentPlan`/`GeneralStrategy` at read time, invoked ONLY when a client is linked (skipped for URL-only). (Rejected: all-client-side math — drift risk; frozen persisted comparison — couples to mutable plans.)

7. **`recharts`, isolated and lazy-loaded** — First charting library in `apps/web`, used via the shadcn/ui chart pattern and imported ONLY inside the lazy-loaded Profile Analyzer module so Mundo 1's bundle is unaffected. (Rejected: hand-rolled SVG — effort/a11y cost; Chart.js/visx — recharts integrates best with the existing shadcn/ui + React stack.)

## Consequences
- Account-level profile analysis becomes a first-class, persisted, inspectable artifact with its own lifecycle, distinct from single-content analysis.
- Mundo 1's scrape contract, AI prompt, persisted record, and frontend monolith are fully insulated; the only Mundo 1 edit is a top-level mode switch.
- Actor routing rides the existing encrypted-mapping seam with zero schema change; a dedicated `variantKey "profile"` prevents collisions with the single-video Instagram actor.
- A new automatic-fallback capability mitigates the HIGH-risk encrypted actor-mapping uncertainty and IG flakiness, bounded to the profile variant.
- AI selection stays org-integration-driven and model-agnostic, consistent with CAP3-MUST-04 and `docs/ai-governance.md`.
- Impute writes flow through validated existing aggregates with per-field confirmation, protecting real client data; provenance is preserved by the target aggregates.
- Hexagonal boundaries are preserved: `ProfileAnalysis` and its value objects are pure Domain; Apify/EF/Npgsql/AspNetCore types live only in Infrastructure/Api.
- `recharts` adds bundle weight, contained by lazy module isolation; operators must configure two mapping entries or the feature degrades to a clear diagnostic.
- Open risks carried to apply: `latestReels[]` shape unconfirmed (assume mirrors `latestPosts[]`); deterministic default-before-backup ordering of same-key mapping candidates must be enforced.

## Related
- Proposal: `openspec/changes/single-analyzer-profile-analysis/proposal.md`
- Design: `openspec/changes/single-analyzer-profile-analysis/design.md`
- Spec: `openspec/changes/single-analyzer-profile-analysis/specs/profile-analysis/spec.md`
- Sibling-aggregate precedent: `docs/architecture/adr/0002-client-content-plan-model.md`
- Diagram: `docs/architecture/diagrams/profile-analysis-flow.{excalidraw,md}` (authored in apply)
- Engram: `sdd/single-analyzer-profile-analysis/explore` (#511), `sdd/single-analyzer-profile-analysis/kickoff-decisions` (#512), `sdd/single-analyzer-profile-analysis/apify-probe` (#513), `sdd/single-analyzer-profile-analysis/proposal` (#514)
