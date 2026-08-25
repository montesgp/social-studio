# Design: Single-Analyzer Profile Analysis ("Mundo 2")

## Overview

Add a SECOND analyzer mode — Instagram **profile** analysis — alongside the untouched single-video flow ("Mundo 1"). A new first-class `ProfileAnalysis` aggregate captures the scraped profile (raw posts + pre-computed aggregates), an expert AI marketing analysis, and per-run diagnostics. The pipeline mirrors Mundo 1's shape (start → queue → background run → poll) but uses a DEDICATED domain namespace, a DEDICATED scrape capability/variant, a DEDICATED AI flow, and a SEPARATE frontend module. Mundo 1 code is not edited beyond a top-level mode switch.

Authoritative decisions live in [`ADR 0003 — profile-analysis-model`](../../../docs/architecture/adr/0003-profile-analysis-model.md). This doc is the HOW; the spec (`specs/profile-analysis/spec.md`) is the WHAT.

## Architectural Boundaries

Strict hexagonal — dependencies point inward (Domain ← Application ← Infrastructure/Api). No EF/Npgsql/Supabase/AspNetCore types in Domain or Application. AI model stays a configurable port (`IDefaultAiSelector` + `IAiCompletionGateway`).

| Layer | Responsibility |
|---|---|
| Domain | `ProfileAnalysis` aggregate + value objects + status enum + invariants. Pure. |
| Application | `AnalyzeProfileCommand/Handler`, `IProfileAnalyzer` orchestrator, `profile-analyzer` AI flow, impute orchestration, dashboard/comparison queries, scrape + fallback ports. |
| Infrastructure | Instagram profile Apify adapter, `variantKey "profile"` routing, automatic backup fallback, EF entity + mapping + migration, profile job processor, diagnostics. |
| Api | Profile-analysis endpoints (analyze/status/dashboard/impute/history/delete) + own-account picker query. |
| Frontend | New `profile-analyzer` module/route tree, mode switch, source picker, polling, recharts dashboard, impute UI. |

## Architecture Decisions

### Decision 1 — `ProfileAnalysis` is a new first-class aggregate in a new namespace

**Choice**: New aggregate `Domain/Social/ProfileAnalysis/` (NOT `SingleAnalyzer/Profile/`, which is the `SocialContentAnalysisProfile` strategy). Own table `social.profile_analyses`, own EF migration, sibling-aggregate pattern (keyed by `Id`, `OrganizationId` scoped, optional soft `ClientId`/`OwnAccountId` Guids, no EF navigations). Persisted shape is HYBRID: raw posts dataset + derived stats + AI analysis stored as JSON string columns (mirrors `SingleContentAnalysisData.*Json`).
**Alternatives considered**: (a) reuse `SingleContentAnalysisData` with a discriminator — bloats Mundo 1, breaks its single-video invariants; (b) fully typed columns for posts/stats — rigid, the post schema has provider-specific extras (`_degraded`, `images[]`) better kept as JSON.
**Rationale**: Profile analysis is a fundamentally different artifact (account-level, multi-post). A separate aggregate keeps Mundo 1 invariants intact and follows ADR 0002's sibling-aggregate precedent. JSON columns match the established `*Json` persistence pattern and keep Domain free of EF.

### Decision 2 — Dedicated profile-scrape path, NOT extension of the single-video extractor

**Choice**: A new Application port `IProfileScrapeResolver` (returns a `ProfileScrapeResult` = profile + posts[] + reels[] + aggregates + diagnostics) implemented by a new Infrastructure `ApifyProfileScrapeResolver` + `InstagramProfileApifyScrapeAdapter`. It does NOT route through `ApifySocialContentExtractor.ExtractAsync` because that hardcodes `doc.RootElement[0]` (line 166) — the profile actor returns a 3-item array and the profile must be selected by `resultType == "profile"`. Actor routing REUSES the proven mechanism: a `SocialContentRequest`-equivalent carrying `PlatformKey "instagram"` + `CapabilityKey "profile"`; `TryResolveMappedActor` matches `VariantKey` against the capability key, so `variantKey "profile"` resolves the profile actor with zero schema change.
**Alternatives considered**: extend `ISocialContentExtractor`/`InstagramApifyScrapeAdapter` with a mode flag — would force `MapItem` (single-snapshot shaped) and the `[0]` array handling to fork internally, risking Mundo 1 regressions; the snapshot contract (`SocialContentSnapshot`) has no slot for posts[]/aggregates.
**Rationale**: Hexagonal cleanliness + ZERO disruption to Mundo 1. The single-video path keeps its exact contract; profile gets a contract shaped for account data. Routing still rides the existing encrypted `actor_mappings_json` seam.

### Decision 3 — Automatic in-run backup-actor fallback (NEW capability)

**Choice**: `ApifyProfileScrapeResolver` resolves BOTH the default and backup actor ids for `(networkKey "instagram", variantKey "profile")`. It runs the default; on failure (transport/timeout/empty/non-2xx/parse) it logs, records a diagnostic, and retries the backup once. Output of either is normalized through the SAME adapter mapping. Mundo 1 has no fallback and is untouched.
**Alternatives considered**: fail fast like Mundo 1 — leaves the HIGH-risk actor-mapping mismatch unmitigated; manual retry endpoint only — worse UX for a long-running scrape.
**Rationale**: De-risks the encrypted-mapping uncertainty (probe obs #513) and IG's flakiness. Scoped to the profile variant so the blast radius is contained.

### Decision 4 — New AI flow `profile-analyzer` with its own prompt + strict JSON output

**Choice**: New `IContentAnalysisProfile` implementation `ProfileAnalyzerProfile` (`FlowKey "profile-analyzer"`, `AiCapabilityKey "ai-analysis"`, `PromptVersionKey "profile-analyzer.analysis.v1"`). The orchestrator assembles profile + filtered posts + derived stats into the AI input and calls `IAiCompletionGateway.CompleteJsonAsync<ProfileAnalysisResult>` via the org default model (`IDefaultAiSelector`). Output schema is fixed (see Interfaces). No hardcoded model.
**Alternatives considered**: reuse `single-analyzer.analysis.v1` — wrong output shape (video idea-generation, not account analysis).
**Rationale**: Follows the configurable-model port contract; each field maps to an impute target.

### Decision 5 — Impute reuses EXISTING write commands, per-field + opt-in + idempotent

**Choice**: A single `ImputeProfileAnalysisCommand` carries the analysis id + a list of selected field targets. The handler maps each selected field onto the EXISTING brand-manual / content-plan / general-strategy upsert commands (load current → merge selected fields → call existing `Update`/upsert). No new write commands. Each impute is opt-in (user-selected), idempotent (re-applying the same selection yields the same state), and requires a linked client.
**Alternatives considered**: new dedicated impute write paths — duplicates validated write logic and violates the out-of-scope boundary; auto-apply all fields — pollutes real client data.
**Rationale**: Reuses validated invariants; provenance/confirmation preserved; matches kickoff decision #512.

### Decision 6 — Server-derived dashboard stats + raw posts; comparison computed at read

**Choice**: A `GetProfileAnalysisDashboardQuery` returns server-derived stats (medianViews [video-scope only], engagementRate, avgViewsByWeekday, contentTypeMix, postingCadence, follower/post counts) AND the raw owner-filtered posts so the client can re-derive/animate. A separate `GetClientPlanComparisonQuery` loads the linked client's `ContentPlan`/`GeneralStrategy` for render-time comparison — invoked ONLY when a client is linked (skipped for URL-only).
**Alternatives considered**: compute everything client-side — duplicates math, risks drift; persist a frozen comparison — couples to mutable client plans.
**Rationale**: Stats are deterministic and cheap server-side; raw posts enable client dynamism; comparison stays fresh against current plan.

### Decision 7 — recharts, isolated and lazy-loaded to the Profile module

**Choice**: Add `recharts` to `apps/web`, used via the shadcn/ui chart pattern, imported ONLY inside the lazy-loaded Profile Analyzer module so Mundo 1's bundle is unaffected.
**Alternatives considered**: hand-rolled SVG charts — high effort, poor a11y; Chart.js/visx — recharts pairs best with the existing shadcn/ui + React stack.
**Rationale**: First charting lib in the repo; lazy isolation protects Mundo 1 (LOW/MEDIUM bundle risk in proposal).

## Data Flow

```
[Web: mode=Perfil] --POST /profile-analyzer/analyze--> AnalyzeProfileCommand/Handler
        |                                                   | StartAsync: validate + preflight + create record (Requested)
        |                                                   | enqueue ProfileAnalysisJob -> 202 {id}
        |<----- poll GET /profile-analyzer/{id} (status) ---|
                                                            v (background worker)
                          IProfileAnalyzer.RunQueuedAsync:
   ScrapingProfile -> IProfileScrapeResolver --default actor--> Apify --fail--> --backup actor--> Apify
                          (select resultType=="profile"; owner-filter posts; derive stats)
   ProfileScraped[Partial] -> persist raw posts + stats + diagnostics
   Analyzing -> IAiCompletionGateway.CompleteJsonAsync<ProfileAnalysisResult> (org default model)
   Analyzed | Failed -> persist analysis JSON + diagnostics
        |
        |-- GET /profile-analyzer/{id}/dashboard --> stats + raw posts (+ comparison if client linked)
        |-- POST /profile-analyzer/{id}/impute ----> map selected fields -> EXISTING BrandManual/ContentPlan/GeneralStrategy upserts
```

## File Changes (representative — full breakdown in tasks)

| File | Action | Description |
|---|---|---|
| `Domain/Social/ProfileAnalysis/ProfileAnalysis.cs` | Create | Aggregate root + factories + status transitions |
| `Domain/Social/ProfileAnalysis/ProfileAnalysisStatus.cs` | Create | Status enum |
| `Domain/Social/ProfileAnalysis/{ProfileSnapshot,ProfilePost,ProfileStats}.cs` | Create | Value objects (post dataset + derived stats) |
| `Application/Abstractions/Social/IProfileScrapeResolver.cs` | Create | Profile scrape port + result contract |
| `Application/Abstractions/Social/IProfileAnalysisRepository.cs` | Create | Repository port |
| `Application/Features/Social/ProfileAnalyzer/Analyze/*` | Create | Command/Handler, `IProfileAnalyzer`, contracts, prompt-version constant, AI result type |
| `Application/Features/Social/ProfileAnalyzer/Impute/*` | Create | `ImputeProfileAnalysisCommand/Handler` + field-target map |
| `Application/Features/Social/ProfileAnalyzer/Dashboard/*` | Create | Dashboard + plan-comparison queries |
| `Application/Features/Social/ProfileAnalyzer/ProfileAnalyzerProfile.cs` | Create | `IContentAnalysisProfile` for `profile-analyzer` |
| `Infrastructure/Social/Providers/Apify/InstagramProfileApifyScrapeAdapter.cs` | Create | Builds input, selects `resultType=="profile"`, maps posts/reels/aggregates |
| `Infrastructure/Social/Providers/ApifyProfileScrapeResolver.cs` | Create | Default→backup fallback, owner filtering, diagnostics |
| `Infrastructure/Persistence/Postgres/Entities/ProfileAnalysisData.cs` | Create | EF entity (JSON columns) |
| `Infrastructure/Persistence/Postgres/Configurations/ProfileAnalysisConfiguration.cs` | Create | EF mapping |
| `Infrastructure/Persistence/Postgres/Migrations/*_AddProfileAnalyses.cs` | Create | Migration |
| `Infrastructure/.../ProfileAnalysisJobProcessor.cs` + queue | Create | Background processing (mirrors single-analysis queue) |
| `ApiService/Endpoints/Social/MapProfileAnalyzerEndpoints.cs` | Create | New endpoint group (do NOT overload single-analyzer map) |
| `apps/web/.../profile-analyzer/*` | Create | New module/route + components + API client |
| `apps/web/components/social/single-analyzer-client.tsx` | Modify | Add ONLY a top-level mode switch (Contenido \| Perfil) |
| `apps/web/package.json` | Modify | Add `recharts` |
| `docs/architecture/adr/0003-profile-analysis-model.md` | Create | ADR |
| `docs/architecture/diagrams/profile-analysis-flow.{excalidraw,md}` | Create | Diagram (next phase) |
| `openspec/specs/integrations/spec.md` | Modify | Document required `actor_mappings_json` entries + fallback |

## Interfaces / Contracts

```csharp
// Application/Abstractions/Social — profile scrape port (no provider/JSON types leak out)
public interface IProfileScrapeResolver
{
    Task<Result<ProfileScrapeResult>> ResolveAsync(ProfileScrapeRequest request, CancellationToken ct = default);
}
public sealed record ProfileScrapeRequest(
    Guid OrganizationId, Guid AnalysisId, Guid? ClientId,
    string ProfileUrlOrHandle, string PlatformKey /* "instagram" */, string CapabilityKey /* "profile" */);
public sealed record ProfileScrapeResult(
    ProfileSnapshot Profile, IReadOnlyList<ProfilePost> OwnerPosts, IReadOnlyList<ProfilePost> Reels,
    ProfileAggregates ProviderAggregates, bool IsPartial,
    IReadOnlyList<ProviderFailureDiagnostic> Diagnostics);
```

```jsonc
// AI output (CompleteJsonAsync<ProfileAnalysisResult>) — every field maps to an impute target
{
  "summary": "string",
  "audienceProfile": "string",                                  // -> GeneralStrategy.TargetAudience
  "contentPillars": ["string"],                                 // -> GeneralStrategy.ContentPillars
  "topContentTypes": ["string"],
  "postingDays": ["Mon","Wed","Fri"],                           // -> GeneralStrategy.PostingFrequency
  "detectedBrandColors": [{ "name": "string", "hex": "#RRGGBB" }], // -> BrandManual.ColorPalette
  "detectedFonts": ["string"],                                  // -> BrandManual.Typography
  "detectedBackgroundImages": ["url"],                          // -> BrandManual.BackgroundTemplates
  "voiceTone": "string",                                        // -> BrandManual.VoiceAndTone
  "recommendedKpis": ["string"],                                // -> ContentPlan.ObjectiveKpis
  "monthlyObjectiveSuggestion": "string",                       // -> ContentPlan.ObjectiveText
  "contentStrategySuggestion": "string",                        // -> ContentPlan.ContentStrategyText
  "strengths": ["string"], "weaknesses": ["string"], "opportunities": ["string"],
  "confidence": 0.0,                                            // 0..1
  "warnings": ["string"]
}
```

```
ProfileAnalysisStatus: Requested -> ScrapingProfile -> ProfileScraped | ProfileScrapedPartial
                                  -> Analyzing -> Analyzed | Failed
Invariants: owner posts filtered by ownerId == profile.instagramId; videoViewCount null for non-Video;
            ProfileScrapedPartial when _degraded posts present or backup actor used; client required for impute.
```

API surface (new group `/api/social/profile-analyzer`): `POST /analyze` (202 + id), `GET /{id}` (status/poll), `GET /{id}/dashboard`, `POST /{id}/impute`, `GET /` (history, paged), `DELETE /{id}`, and `GET /api/social/clients/{clientId}/own-accounts?network=instagram` reused/added for the picker.

## Frontend Component Tree (new `profile-analyzer` module, lazy-loaded)

```
single-analyzer-client.tsx  (Mundo 1 — ONLY adds the mode switch)
  └─ ModeSwitch [Contenido | Perfil]
        └─ Perfil → <ProfileAnalyzerModule>  (React.lazy — recharts loaded only here)
              ├─ ProfileSourcePicker
              │     ├─ ClientSelect → OwnAccountMultiSelect (filtered network=instagram, multi-pick)
              │     └─ ProfileUrlInput (paste; mutually exclusive with client path)
              ├─ AnalysisProgress (polls GET /{id}; status → step labels)
              ├─ ProfileDashboard (renders when Analyzed)
              │     ├─ OverviewCards (followers, posts, engagementRate, cadence)
              │     ├─ AvgViewsByWeekdayChart (recharts Bar)
              │     ├─ EngagementTrendChart (recharts Line/Area)
              │     ├─ ContentTypeMixChart (recharts Pie/Donut)
              │     ├─ AiNarrativePanel (summary, strengths/weaknesses/opportunities)
              │     └─ ComparisonVsObjectivesPanel (only when client linked)
              └─ ImputePanel
                    └─ checkbox list grouped by target [Brand Manual | Content Plan | General Strategy]
                       + ConfirmDialog → POST /{id}/impute
  apiClient: analyzeProfile(), getProfileAnalysis(id), getDashboard(id), impute(id, fields), listHistory(), deleteAnalysis(id), listInstagramOwnAccounts(clientId)
```

## Excalidraw Diagram Deliverable (authored in apply)

Location/format MIRROR the existing pair `docs/architecture/diagrams/content-plan-flow.{excalidraw,md}`: a committed `.excalidraw` source + a GitHub-rendering Mermaid `.md` with a top note linking the `.excalidraw`. New files: `docs/architecture/diagrams/profile-analysis-flow.excalidraw` and `profile-analysis-flow.md`, with a cross-link to ADR 0003.

Nodes/edges to draw (profile-analysis workflow):
```
Source (Client+IG own-account | URL paste)
  → AnalyzeProfile (202, record Requested)
  → ScrapingProfile  --default actor--> Apify
        Apify --fail--> [backup actor] --> Apify        (automatic fallback)
  → ProfileScraped[Partial]  (select resultType=="profile"; owner-filter by ownerId; derive stats)
  → Analyzing  (org default AI, profile-analyzer.analysis.v1)
  → Analyzed | Failed
  → Dashboard (stats + raw posts + comparison-if-client)
  → Impute (per-field, confirmed) --> {Brand Manual, Content Plan, General Strategy}
```
Color legend mirrors content-plan-flow (source/enduring blue, pipeline purple, terminal green, decision/fallback amber).

## Testing Strategy

| Layer | What to Test | Approach |
|---|---|---|
| Unit (Domain) | Status transitions, owner-filter invariant, partial-state rules, impute idempotency math | xUnit, pure |
| Unit (Application) | Orchestrator stages, AI input assembly, impute field mapping, comparison query | mock ports |
| Integration (Infra) | `resultType=="profile"` selection, default→backup fallback, owner filtering, RESIDENTIAL proxy in input, EF round-trip/migration | recorded Apify payloads + test DB |
| Api | Endpoint contracts, 202/poll, impute requires client, owner-scoping | facade tests |
| Architecture | No Infra types in Domain/Application; AI model stays port | existing arch tests |

## Migration / Rollout

Additive only — new table `social.profile_analyses`, no changes to existing tables. One EF migration. `recharts` added to `apps/web`. Operator MUST configure the two `actor_mappings_json` entries (documented below) — feature degrades to a clear diagnostic if missing. No data migration.

**Required `actor_mappings_json` entries (operator-configured via integrations UI; values are Data-Protection-encrypted in DB):**
```json
[
  { "networkKey": "instagram", "variantKey": "profile", "actorId": "get-leads~all-in-one-instagram-scraper" },
  { "networkKey": "instagram", "variantKey": "profile", "actorId": "apify~instagram-profile-scraper" }
]
```
First entry = default; second = automatic backup. The single-video `instagram` mapping is untouched.

## Open Questions

- [ ] `latestReels[]` item shape unconfirmed (probe account had none) — verify against a reel-heavy account at apply; assume it mirrors `latestPosts[]` and degrade gracefully.
- [ ] If BOTH actors share `networkKey/variantKey`, the resolver must order candidates deterministically (default first) — confirm whether mapping order in JSON is preserved or a priority field is needed at apply.
