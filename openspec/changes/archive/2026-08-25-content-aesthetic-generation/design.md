# Design — content-aesthetic-generation (CASE C, Phase 1)

Hexagonal design that turns the passive `BackgroundSourcingPolicy.ClientAesthetic` seam into an active, non-blocking, client-aligned generation path. It adds a refresh-if-stale orchestration port, widens the descriptor reader into a richer `ClientAestheticProfile`, threads that profile into BOTH the background designer AND the copy generators (additive to `ConcisionConstraint`), and captures a server-derived `AverageSlidesPerCarousel`. Every provider concern (EF, Apify, AI) stays behind an Application port with an Infrastructure adapter; Domain and Application stay provider-free. This document fixes the decisions, component map, the new flow (component + sequence), the degrade matrix, the test-first ordering for the risky pieces, the migration verdict, and risks. It does NOT enumerate task steps — that is `sdd-tasks`.

## Status
Design — architecture decisions resolved. Implements the HOW for `proposal.md`. New Application port family (ensure-fresh) + a profile-reader contract shape change + per-client threshold field on `Client` + one additive EF migration column → an ADR under `docs/architecture/adr/` is authored at apply time (`require_adr_for_architecture_changes`). One small additive EF migration (`aesthetic_refresh_threshold_days smallint NULL` on `social.clients`) — see §7.

## Scope reminder (Non-Goals are hard boundaries)
Ships text-descriptor + voice-tone + avg-slides aesthetic guidance with non-blocking background refresh, plus a per-client configurable staleness threshold (`AestheticRefreshThresholdDays`) with an additive EF migration column and ABM surface. Does NOT capture real post-image URLs or captions (Phase 2/3, deferred), does NOT add a blocking/"waiting-for-analysis" job state, does NOT add per-org post-limit configuration, and does NOT change the `ProfileAiAnalysis` AI schema or its prompt version.

---

## 0. The decisions (read this first)

| # | Open question | Decision | One-line rationale |
|---|---------------|----------|--------------------|
| **OQ-1** | Staleness threshold N | **System default 30 days** (`AestheticFreshness.DefaultStalenessThresholdDays = 30`); **per-client override** via `Client.AestheticRefreshThresholdDays` (`int?`; null = use default). Validation: min 1d, max 365d. Effective threshold = `client.AestheticRefreshThresholdDays ?? 30`. | Owner-locked. 30-day default balances freshness vs Apify cost; per-client override lets high-frequency clients tighten their own window via ABM. |
| **OQ-2** | avg-slides soft hint vs hard | **Soft advisory hint, clamped 3–10** when surfaced; best-effort/null if `childrenCount` absent. | Owner-locked. No hard rejection of AI output; fractional/extreme averages are clamped so the prompt hint stays sane. |
| **OQ-3** | VoiceTone delivery | **Direct-read into `GenerationContext`/prompt at generation time** — NOT imputed into `BrandManual.VoiceAndTone`. | Owner-locked. Keeps the signal advisory + decoupled; no mutation of the client's authored brand manual. |
| **OQ-5** | Degrade-when-no-analysis | **Degrade to Creative (CASE B-equivalent) + fire refresh; log a structured warning.** | The existing `ClientAestheticStrategy` already degrades empty descriptors to `Creative`; we extend the same behavior to copy + fire the refresh. |
| **OQ-6** | Reader evolution shape | **Widen `IClientAestheticDescriptorReader` → return `ClientAestheticProfile`** (rename to `IClientAestheticProfileReader`). | Single caller today (`ContentGenerationJobProcessor`); a coexisting sibling is ceremony with no payoff. Compiler enforces the one-call-site update. |
| **OQ-7** | `maxPostsPerProfile` value | **18.** | Owner-locked. Richer aesthetic window; ~1.5x Apify cost vs 12, below the 20 ceiling. |
| **OQ-8** | Refresh debounce | **Debounce to one in-flight refresh per client.** | Owner-locked. Repeated generations within the refresh window MUST NOT enqueue duplicate analyses. |

---

## 1. Architecture approach

Dependency rule, unchanged: **Domain ← Application ← Infrastructure / Api**. No EF/Npgsql/Apify/`HttpClient`/ASP.NET types in Domain or Application.

- **Domain** (`Domain/Social/ProfileAnalysis/` and `Domain/Social/Clients/`) gains three additive fields: `ProfilePost.CarouselChildrenCount` (`int?`, best-effort), `ProfileStats.AverageSlidesPerCarousel` (`double?`), and `Client.AestheticRefreshThresholdDays` (`int?`, nullable, min 1/max 365). All pure. **No `ProfileAiAnalysis` change** — `VoiceTone` already exists; avg-slides is a server stat, not an AI field.
- **Application** gains: a new `IClientAestheticEnsureFreshService` port + implementation (refresh-if-stale orchestration), the `ClientAestheticProfile` value object, the widened `IClientAestheticProfileReader` port, an additive freshness method on `IProfileAnalysisRepository`, the `AverageSlidesPerCarousel` computation in `ProfileMetricCalculator`, and the threading of `ClientAestheticProfile` through `OrchestratorRequest` → `GenerationContext` → the copy generators + into `BackgroundPlanRequest` for the designer.
- **Infrastructure** carries only wiring: the EF profile reader returns the richer profile + freshness; the Apify adapter maps `childrenCount`; the post-limit constant rises to 18; DI registration of the ensure-fresh service. The on-demand trigger REUSES the existing `IProfileAnalysisJobQueue` + `ProfileAnalysisBackgroundWorker` pipeline (no new worker).
- **Api / Web**: expose `AestheticRefreshThresholdDays` on the client ABM API (Create/Update request + response + `MapSocialClientsEndpoints`) and the Next.js client ABM form (`use-client-form` + `types/social-clients` API lib) — mirroring the `BackgroundSourcingPolicy` pattern exactly.

The ensure-fresh orchestration lives in **Application** as a pure port; the EF freshness/in-flight queries and the Apify/AI work stay behind their existing Infrastructure adapters. The generation job NEVER awaits the analysis pipeline — refresh is fire-and-forget through the already-async channel queue.

---

## 2. Component map — ports, adapters, layers

### 2.1 Domain (pure, additive)
| Element | Path | Change |
|---------|------|--------|
| `ProfilePost.CarouselChildrenCount` (`int?`) | `Domain/Social/ProfileAnalysis/ProfilePost.cs` | **Modified** — append nullable carousel child-count; null for non-carousel or when actor omits it |
| `ProfileStats.AverageSlidesPerCarousel` (`double?`) | `Domain/Social/ProfileAnalysis/ProfileStats.cs` | **Modified** — append server-derived stat; null when no carousel child counts captured |
| `Client.AestheticRefreshThresholdDays` (`int?`) | `Domain/Social/Clients/Client.cs` | **Modified** — nullable per-client staleness override; min 1d, max 365d; null = system default (30d). Add to constructor params, `Create`/`Restore` signatures, and `SetAestheticRefreshThresholdDays(int? days, DateTime nowUtc)` setter — mirrors `SetBackgroundSourcingPolicy` pattern |

### 2.2 Application (ports + use cases)
| Element | Kind | Path | Change |
|---------|------|------|--------|
| `ClientAestheticProfile` | value object | `Application/Features/Social/ContentGeneration/Shared/ClientAestheticProfile.cs` | **New** — descriptors + VoiceTone + avg-slides + AnalyzedAtUtc; image-URL-ready (nullable) |
| `IClientAestheticProfileReader` | Port (replaces `IClientAestheticDescriptorReader`) | `Application/Abstractions/Social/ContentGeneration/IClientAestheticProfileReader.cs` | **Modified/renamed** — `GetProfileAsync(orgId, clientId, ct) → ClientAestheticProfile?` |
| `IClientAestheticEnsureFreshService` | Port | `Application/Abstractions/Social/ContentGeneration/IClientAestheticEnsureFreshService.cs` | **New** — `EnsureFreshAsync(orgId, clientId, ct)` non-blocking refresh-if-stale + debounce |
| `ClientAestheticEnsureFreshService` | Use case (impl) | `Application/Features/Social/ContentGeneration/Background/ClientAestheticEnsureFreshService.cs` | **New** — freshness check, own-account resolve, in-flight debounce, fire-and-forget enqueue |
| `IProfileAnalysisRepository.GetLatestAnalyzedForClientAsync` + `HasNonTerminalForClientAsync` | Port | `Application/Abstractions/Social/IProfileAnalysisRepository.cs` | **Modified** — freshness lookup + in-flight debounce probe |
| `ProfileMetricCalculator` | Use case | `Application/Features/Social/ProfileAnalyzer/.../ProfileMetricCalculator.cs` | **Modified** — compute `AverageSlidesPerCarousel` (clamp surfaced hint 3–10) |
| `ContentGenerationJobProcessor` | Use case | `Application/Features/Social/ContentGeneration/Pipeline/ContentGenerationJobProcessor.cs` | **Modified** — pre-loop: ensure-fresh, read profile, thread to designer + orchestrator |
| `OrchestratorRequest` + `GenerationContext` | contracts | `…/Orchestration/OrchestratorContracts.cs`, `…/Shared/GenerationContext.cs` | **Modified** — carry `ClientAestheticProfile?` |
| `GenerationPromptContext` | prompt builder | `…/Shared/GenerationPromptContext.cs` | **Modified** — additive `AestheticGuidance(profile)` helper, coexists with `ConcisionConstraint` |
| `Carousel/Story/FeedPostGenerator` | generators | `…/Formats/**` | **Modified** — append aesthetic advisory guidance from `GenerationContext.AestheticProfile` |

### 2.3 Infrastructure (adapters + persistence)
| Element | Path | Change |
|---------|------|--------|
| `EfClientAestheticDescriptorReader` → `EfClientAestheticProfileReader` | `…/Social/ContentGeneration/EfClientAestheticProfileReader.cs` | **Modified** — deserialize `voiceTone`; read `CompletedAtUtc`; read `ServerDerivedStatsJson.AverageSlidesPerCarousel`; return `ClientAestheticProfile?` |
| `EfProfileAnalysisRepository` | `…/Persistence/.../EfProfileAnalysisRepository.cs` | **Modified** — implement the two new freshness/debounce methods (org+client scoped) |
| `InstagramProfileApifyScrapeAdapter.MapSinglePost` | `…/Social/Providers/Apify/InstagramProfileApifyScrapeAdapter.cs` | **Modified** — best-effort `GetInt(postEl,"childrenCount")` (fallback `children`/`sidecar` length) → `CarouselChildrenCount` |
| `ApifyProfileScrapeResolver` (line 269) | `…/Social/Providers/ApifyProfileScrapeResolver.cs` | **Modified** — `maxPostsPerProfile: 12 → 18` |
| DI registration | Infrastructure DI module | **New** — register `IClientAestheticEnsureFreshService` + the renamed reader |
| `SocialClientData.AestheticRefreshThresholdDays` | `Infrastructure/Persistence/Postgres/Entities/SocialClientData.cs` | **Modified** — add nullable `short?` column property (`aesthetic_refresh_threshold_days`) |
| `EfSocialClientRepository` — `ToData` / `Update` / `ToDomain` | `Infrastructure/Persistence/Postgres/EfSocialClientRepository.cs` | **Modified** — map `AestheticRefreshThresholdDays` in **all three methods**. CRITICAL: `Update` method must assign `entity.AestheticRefreshThresholdDays = client.AestheticRefreshThresholdDays` explicitly. Omitting `Update` is the predecessor's `BackgroundSourcingPolicy` drop-bug pattern — do NOT repeat it. |
| EF migration | Infrastructure | **New (additive)** — `aesthetic_refresh_threshold_days smallint NULL` on `social.clients`; no data loss; rolled back with a single `DROP COLUMN` |
| `SocialClientResponse` | Api / `ApiService` | **Modified** — add `int? AestheticRefreshThresholdDays` |
| `CreateSocialClientCommand` / `Handler` | Api / `ApiService` | **Modified** — accept `int? AestheticRefreshThresholdDays`; call `SetAestheticRefreshThresholdDays` after client creation |
| `UpdateSocialClientCommand` / `Handler` | Api / `ApiService` | **Modified** — accept `int? AestheticRefreshThresholdDays`; call `SetAestheticRefreshThresholdDays` |
| `MapSocialClientsEndpoints` | Api / `ApiService` | **Modified** — bind the field in Create and Update route handlers |
| `apps/web` — client form + `use-client-form` + `types/social-clients` | Web / `apps/web` | **Modified** — add `aestheticRefreshThresholdDays` optional numeric input; mirror `BackgroundSourcingPolicy` field handling |

> **Naming/verification note:** the existing reader is `EfClientAestheticDescriptorReader` (`GetDescriptorsAsync`) with a single caller at `ContentGenerationJobProcessor.cs:267-275`. `maxPostsPerProfile: 12` is the literal at `ApifyProfileScrapeResolver.cs:269`. `MapSinglePost` is at `InstagramProfileApifyScrapeAdapter.cs:327`. The job-trigger pattern is `AnalyzeProfileHandler` (create `ProfileAnalysis.CreateFromClient` → `repo.AddAsync` → `unitOfWork.SaveChangesAsync` → `queue.EnqueueAsync(new ProfileAnalysisJob(id, orgId))`).

---

## 3. New + changed types

### 3.1 `ClientAestheticProfile` (Application value object, image-URL-ready)
```
public sealed record ClientAestheticProfile(
    IReadOnlyList<string> BackgroundDescriptors,  // Phase 1: DetectedBackgroundImages
    string? VoiceTone,                            // Phase 1: direct-read copy-style signal (OQ-3)
    double? AverageSlidesPerCarousel,             // Phase 1: server stat, clamped 3-10 when surfaced
    DateTime? AnalyzedAtUtc);                      // freshness provenance (CompletedAtUtc)
    // Phase 2 (additive, nullable): IReadOnlyList<string>? BackgroundImageUrls
```
- `null` return from the reader ≡ no analysis at all → degrade to Creative + refresh.
- Empty `BackgroundDescriptors` with non-null `AnalyzedAtUtc` ≡ analysis exists but no descriptors → still degrade backgrounds to Creative, but VoiceTone/avg-slides may still feed copy.

### 3.2 `IClientAestheticProfileReader` (widened, OQ-6)
```
public interface IClientAestheticProfileReader
{
    Task<ClientAestheticProfile?> GetProfileAsync(Guid orgId, Guid clientId, CancellationToken ct);
}
```
- Read-only over `social.profile_analyses`; latest `Status=="Analyzed"` row by `CompletedAtUtc`. No Apify call. Returns `null` when no Analyzed row exists. The Infrastructure adapter deserializes `ai_analysis_json` (`detectedBackgroundImages`, `voiceTone`) and `server_derived_stats_json` (`averageSlidesPerCarousel`), and projects `CompletedAtUtc` → `AnalyzedAtUtc`.

### 3.3 `IClientAestheticEnsureFreshService` (new port)
```
public interface IClientAestheticEnsureFreshService
{
    // Fire-and-forget orchestration. NEVER throws into the generation path; NEVER blocks on analysis.
    // Returns a small result for logging/telemetry only — the caller does not act on it beyond logging.
    Task<EnsureFreshOutcome> EnsureFreshAsync(Guid orgId, Guid clientId, CancellationToken ct);
}

public enum EnsureFreshOutcome { Fresh, RefreshEnqueued, SkippedInFlight, SkippedNoOwnAccount, NoActionNeeded }
```

### 3.4 `IProfileAnalysisRepository` additions (freshness + debounce)
```
Task<DateTime?> GetLatestAnalyzedCompletedAtAsync(Guid orgId, Guid clientId, CancellationToken ct);
Task<bool> HasNonTerminalAnalysisForClientAsync(Guid orgId, Guid clientId, CancellationToken ct);
```
- `GetLatestAnalyzedCompletedAtAsync`: MAX `CompletedAtUtc` where `Status=="Analyzed"`. Drives the per-client freshness check (effective threshold = `client.AestheticRefreshThresholdDays ?? 30d`; the reader already needs the same row; the dedicated method keeps the service free of the reader).
- `HasNonTerminalAnalysisForClientAsync`: EXISTS a row with `Status NOT IN ("Analyzed","Failed")` (i.e. Requested/Scraping/ProfileScraped/Analyzing) → the in-flight debounce probe (OQ-8).

---

## 4. The ensure-fresh service (the core new logic)

```
EnsureFreshAsync(orgId, clientId, ct):
  client = clientRepo.GetByIdAsync(clientId, orgId)  // needed for per-client threshold
  effectiveThreshold = TimeSpan.FromDays(client?.AestheticRefreshThresholdDays ?? AestheticFreshness.DefaultStalenessThresholdDays)
  latest = repo.GetLatestAnalyzedCompletedAtAsync(orgId, clientId)
  if latest != null AND (clock.UtcNow - latest) <= effectiveThreshold:
       return Fresh                                  // no refresh needed
  if repo.HasNonTerminalAnalysisForClientAsync(orgId, clientId):
       return SkippedInFlight                        // debounce — one in-flight refresh per client (OQ-8)
  ownAccount = ResolvePrimaryOwnAccount(clientId, orgId)   // see §4.1
  if ownAccount == null:
       return SkippedNoOwnAccount                    // degrade safely, NO refresh
  target = InstagramProfileTargetNormalizer.Normalize(ownAccount.Handle ?? ownAccount.ProfileUrl)
  analysis = ProfileAnalysis.CreateFromClient(orgId, clientId, ownAccount.Id, target, clock.UtcNow)
  await repo.AddAsync(analysis); await unitOfWork.SaveChangesAsync()
  await queue.EnqueueAsync(new ProfileAnalysisJob(analysis.Id, orgId))   // fire-and-forget
  return RefreshEnqueued
```
- **Mirrors `AnalyzeProfileHandler`** exactly (create → persist → enqueue) but with no command/response surface. Dependencies: `IProfileAnalysisRepository`, `ISocialClientRepository` (to resolve `AestheticRefreshThresholdDays`), `IClientOwnAccountRepository`, `IUnitOfWork`, `IClock`, `IProfileAnalysisJobQueue`, `ILogger`. All already exist.
- **Non-blocking guarantee:** the method only persists a row + writes to the bounded channel (capacity 200, `FullMode=Wait`). The heavy scrape+AI runs in `ProfileAnalysisBackgroundWorker`. The generation job NEVER awaits that work.
- **Exception isolation:** the processor wraps the call in try/catch and logs; any failure here MUST NOT fail generation (the job proceeds with best-available data).

### 4.1 Own-account resolution
The processor has only `ClientId`. Resolve via the existing `IClientOwnAccountRepository.GetAllByClientAsync(clientId, orgId, ct)` and pick the **primary/first active** account (mirroring how analysis targets a client's account). If none → `SkippedNoOwnAccount` and degrade. (No new repository method needed.)

---

## 5. New flow — component + sequence

### 5.1 Component view (processor pre-loop, around line 256-275)
```
ContentGenerationJobProcessor.ProcessInternalAsync
  backgroundPolicy = brand.Client?.BackgroundSourcingPolicy
  │
  ├─ if policy == ClientAesthetic AND clientId.HasValue:
  │     try { await ensureFresh.EnsureFreshAsync(orgId, clientId) }   // fire-and-forget, isolated
  │     catch -> log, continue
  │     aestheticProfile = await profileReader.GetProfileAsync(orgId, clientId)   // best-available NOW
  │  else:
  │     aestheticProfile = null
  │
  ├─ descriptors = aestheticProfile?.BackgroundDescriptors ?? []     // designer side (existing path)
  │
  ├─ orchestrator.RunAsync(OrchestratorRequest{ ..., AestheticProfile = aestheticProfile })
  │      → GenerationContext{ ..., AestheticProfile }
  │          → Carousel/Story/FeedPostGenerator: prompt += AestheticGuidance(profile)  // additive to ConcisionConstraint
  │
  └─ per format: strategy.Plan(BackgroundPlanRequest{ ..., ClientAestheticDescriptors = descriptors })
        → BackgroundSelection.ClientAesthetic(descriptors)  → AiVisualDesigner (existing)
```

### 5.2 Sequence — fresh analysis present
```
Processor → ensureFresh.EnsureFreshAsync → repo: latest=now-3d → return Fresh (no enqueue)
         → profileReader.GetProfileAsync → ClientAestheticProfile{ descriptors, voiceTone, avg=5, analyzedAt }
         → designer gets descriptors; copy generators get voiceTone + avg-slides hint
         → output generated immediately
```

### 5.3 Sequence — stale or missing analysis
```
Processor → ensureFresh.EnsureFreshAsync
              effectiveThreshold = client.AestheticRefreshThresholdDays ?? 30d
              latest=now-45d (stale beyond 30d default) OR null (missing)
              → not in-flight → ownAccount resolved
              → create ProfileAnalysis + persist + enqueue ProfileAnalysisJob   (background worker picks it up later)
              → return RefreshEnqueued
         → profileReader.GetProfileAsync → stale profile (use it) OR null (degrade)
         → output generated NOW with best-available data; NEXT generation sees fresh analysis
```

---

## 6. Copy-generator threading (additive, concision wins)

`GenerationPromptContext` gains a pure helper that emits advisory guidance ONLY when the profile is present:
```
public static string AestheticGuidance(ClientAestheticProfile? p) =>
    p is null ? "" :
    "CLIENT AESTHETIC (advisory): "
    + (p.VoiceTone is { Length: >0 } vt ? $"match the client's voice/tone — {vt}. " : "")
    + (p.AverageSlidesPerCarousel is { } avg
        ? $"the client typically posts ~{Clamp(avg,3,10):0} slides per carousel; prefer a similar length when it does not harm the message. " : "")
    + "These are ADVISORY: the CONCISION constraint above WINS on length and slot count.";
```
- Each generator appends `AestheticGuidance(ctx.AestheticProfile)` AFTER `ConcisionConstraint` so concision is the dominant directive (proposal non-goal + OQ-2). The avg-slides value is clamped 3–10 only at the point it is rendered into the hint; the stored `ProfileStats.AverageSlidesPerCarousel` keeps the raw computed value.
- **No change** to the `ConcisionConstraint` constant or per-slot ceilings — guidance is strictly additive.
- **No prompt-version bump** for the copy formats: the directive is advisory-only and does not change the expected-output schema. (If `sdd-verify` finds the team treats any prompt-text change as a version event, bump only the touched copy format keys — but the default here is NO bump.)

---

## 7. Profile-analysis changes + migration verdict

| Change | Where | Note |
|--------|-------|------|
| Capture `childrenCount` | `MapSinglePost` (Apify adapter) | **Best-effort** — `GetInt(postEl,"childrenCount")`, fallback to `children`/`sidecar` array length; `null` when absent (actor-confirmation caveat, Risk R1). |
| `ProfilePost.CarouselChildrenCount` | Domain | Additive nullable field; lives in scrape raw + the post list. |
| `AverageSlidesPerCarousel` | `ProfileMetricCalculator` → `ProfileStats` | Server stat: mean of `CarouselChildrenCount` over non-degraded owner Carousel posts with a non-null count; `null` if none. Surfaced hint clamped 3–10 (raw value stored unclamped). |
| `maxPostsPerProfile` | `ApifyProfileScrapeResolver:269` | `12 → 18` (OQ-7). |

**AI schema / prompt version:** **NO CHANGE.** `VoiceTone` already exists on `ProfileAiAnalysis` and is read directly (OQ-3). `AverageSlidesPerCarousel` is a pure server stat computed in `ProfileMetricCalculator` and persisted in `ServerDerivedStatsJson` — it does NOT enter `CreateAiInput` / `ProfileAnalysisResult`, so `ProfileAnalyzerPromptVersions.AnalysisV1` is untouched and no cached AI output is invalidated.

**EF migration:** **ONE small additive migration** (revised from the earlier "none" verdict). The avg-slides / childrenCount data continues to live in jsonb — no relational column for those. The single new column is:

```sql
ALTER TABLE social.clients
  ADD COLUMN aesthetic_refresh_threshold_days smallint NULL;
```

This column stores `Client.AestheticRefreshThresholdDays` as a nullable `smallint`. It is purely additive: existing rows get `NULL` (= use the 30-day system default), no data is lost, and rollback is a single `DROP COLUMN`. Confirm during apply that `ProfilePost`/`ProfileStats` are persisted as serialized JSON (not relational columns) so no migration is needed there.

> **EfSocialClientRepository mapping guard**: Map `aesthetic_refresh_threshold_days` in ALL THREE of `ToData`, `Update`, and `ToDomain`. The predecessor change (`content-background-sourcing-policy`) had a bug where `BackgroundSourcingPolicy` was accidentally omitted from the `Update` method body, causing silent data loss on update. Do NOT repeat this pattern for `AestheticRefreshThresholdDays`.

---

## 8. Degrade matrix (non-blocking guarantee)

| State | Background designer | Copy generators | Refresh action | Job blocks? |
|-------|---------------------|-----------------|----------------|-------------|
| **Fresh** (≤ effective threshold: `client.AestheticRefreshThresholdDays ?? 30d`) | `ClientAesthetic(descriptors)` | VoiceTone + avg-slides hints | none (`Fresh`) | No |
| **Stale** (> effective threshold) | use stale descriptors | use stale VoiceTone + avg-slides | enqueue (`RefreshEnqueued`) | No |
| **None** (no Analyzed row) | degrade to `Creative` | no aesthetic guidance (concision only) | enqueue (`RefreshEnqueued`) | No |
| **In-flight refresh exists** | best-available (stale/none) | best-available | skip (`SkippedInFlight`) | No |
| **Missing own account** | best-available (stale/none) | best-available | skip (`SkippedNoOwnAccount`), logged | No |

No new "waiting-for-analysis" job state is introduced anywhere. Every cell produces output immediately.

---

## 9. Test-first ordering (Strict TDD active — `dotnet test`)

Risky pieces FIRST: the non-blocking ensure-fresh service and the processor wiring. Write the failing test, then the production change, per row.

| # | Unit (test-first) | What the test pins | Then change |
|---|-------------------|--------------------|-------------|
| 1 | `ProfileMetricCalculator.AverageSlidesPerCarousel` | mean over owner non-degraded carousels with non-null counts; `null` when none; ignores non-carousels | `ProfileMetricCalculator`, `ProfileStats` |
| 2 | `ClientAestheticProfile` + reader projection | descriptors + voiceTone + avg + AnalyzedAtUtc mapped; `null` when no Analyzed row; tolerant of missing keys | `ClientAestheticProfile`, `EfClientAestheticProfileReader` (mapping unit) |
| 3 | `EnsureFreshAsync` freshness | null threshold → 30d default; within default → `Fresh`, no enqueue; beyond default → enqueue; null analysis → enqueue; custom threshold honored (7d client: analysis at 10d → stale) | `ClientAestheticEnsureFreshService` |
| 4 | `EnsureFreshAsync` debounce | non-terminal row exists → `SkippedInFlight`, NO enqueue (OQ-8) | service + repo probe |
| 5 | `EnsureFreshAsync` own-account/degrade | no own account → `SkippedNoOwnAccount`, no enqueue; resolves primary otherwise | service + `IClientOwnAccountRepository` |
| 6 | `EnsureFreshAsync` non-blocking + isolation | enqueue is fire-and-forget; a thrown repo/queue error is swallowed by the processor and generation proceeds | service + processor try/catch |
| 7 | Processor wiring | when `policy==ClientAesthetic`: ensure-fresh called once, profile read once, profile threaded into `OrchestratorRequest` AND descriptors into `BackgroundPlanRequest` | `ContentGenerationJobProcessor` |
| 8 | `AestheticGuidance` additivity | guidance appended AFTER `ConcisionConstraint`; empty when profile null; avg clamped 3–10 in the hint only | `GenerationPromptContext` + a generator |
| 9 | `MapSinglePost` child-count | maps `childrenCount` when present; `null` when absent (best-effort, no throw) | Apify adapter |
| 10 | `maxPostsPerProfile` | resolver passes 18 | `ApifyProfileScrapeResolver` |

The renamed reader interface (`IClientAestheticDescriptorReader` → `IClientAestheticProfileReader`) is a compiler-enforced single-call-site update done alongside #2/#7 in one unit so the build never stays red.

---

## 10. Architecture-gate compliance
- **No provider types in Domain/Application.** `ClientAestheticProfile`, both new ports, the ensure-fresh service, and the metric change are pure. EF queries (freshness/debounce/reader) and Apify mapping stay in Infrastructure adapters; the queue/worker are reused, not changed.
- **Dependency direction preserved**: Domain ← Application ← Infrastructure/Api.
- **No hardcoded AI model**: the refresh reuses the existing `ProfileAnalyzerOrchestrator` provider-resolution path unchanged.
- **Architecture change** (new port family + reader contract shape) → ADR authored at apply time.
- **Docs-sync**: `openspec/specs/*` updated for the modified capabilities in the spec phase.
- Run `pwsh -File scripts/gates/Run-Gates.ps1` before completion.

---

## 11. Risks and mitigations

| # | Risk | Severity | Mitigation |
|---|------|----------|------------|
| R1 | Actor does not return `childrenCount` in profile-scraper mode | Medium | Best-effort map with `children`/`sidecar` fallback; `null` when absent → `AverageSlidesPerCarousel` null → no avg hint. Confirm during apply; smaller change than image URLs. |
| R2 | First-ever generation for un-analyzed client uses degraded data | Medium | Explicit non-blocking degrade-to-Creative + background refresh; the next run is fresh. No job blocking. Communicated in spec/UX copy. |
| R3 | Duplicate refreshes from repeated generations | Medium | `HasNonTerminalAnalysisForClientAsync` debounce → at most one in-flight refresh per client (OQ-8). |
| R4 | Ensure-fresh failure leaks into generation | Medium | Processor wraps the call in try/catch + log; failure never fails the job (test #6). |
| R5 | Aesthetic copy guidance contradicts `ConcisionConstraint` | Medium | Guidance strictly additive, appended after concision; concision wins on length; clamp 3–10; test #8. No change to concision constants. |
| R6 | `maxPostsPerProfile` 12→18 raises Apify cost ~1.5x | Medium | Single constant; revert to 12 restores prior cost. Owner-accepted tradeoff. |
| R7 | Missing own account blocks refresh | Low | `SkippedNoOwnAccount` degrade path; logged; generation still proceeds. |
| R8 | Accidental AI-schema/prompt drift | Low | Design mandates NO `ProfileAiAnalysis`/prompt change; avg-slides is server-side; VoiceTone direct-read. Verify gate. |

## 12. Rollback
- Disabling the `EnsureFreshAsync` call in the processor reverts to today's passive descriptor read.
- The widened reader can fall back to returning only `BackgroundDescriptors` (others null) — designer behavior unchanged.
- `maxPostsPerProfile` revert to 12; `CarouselChildrenCount`/`AverageSlidesPerCarousel` are additive — unused they yield no avg hint.
- The `aesthetic_refresh_threshold_days` column is nullable `smallint` — rollback is `DROP COLUMN aesthetic_refresh_threshold_days` from `social.clients` with no data loss.

## 13. Next step
`sdd-tasks` — slice in seam order: (1) metric + Apify child-count + post-limit (server stat foundation) → (2) `ClientAestheticProfile` + reader rename + freshness/debounce repo methods → (3) ensure-fresh service (test-first, risky) → (4) processor wiring + `OrchestratorRequest`/`GenerationContext` threading (test-first, risky) → (5) copy-generator additive guidance. Tasks owns the checklist; this design owns the HOW.
