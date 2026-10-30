# ADR 0006 - Client Aesthetic Ensure-Fresh Port

## Status
Accepted

## Context

The `ClientAesthetic` background sourcing policy (ADR 0005, §4) shipped as a seam only: `ClientAestheticStrategy` read text descriptors from the latest `ProfileAiAnalysis` record via `IClientAestheticDescriptorReader`, but there was no mechanism to keep that analysis current. If a client's aesthetic profile aged past a useful threshold, the strategy silently served stale descriptors without any attempt to refresh, degrading background sourcing quality.

Three related gaps existed:

1. **No refresh trigger.** The generation pipeline had no pre-loop hook to detect a stale aesthetic and enqueue a new analysis before proceeding.
2. **Thin descriptor contract.** `IClientAestheticDescriptorReader` returned only `IReadOnlyList<string>` (descriptor strings). The richer context needed by generators — voice tone, average carousel slide count — could not be expressed.
3. **No per-client staleness control.** The only possible staleness threshold was a system-wide constant, making it impossible to tune refresh frequency per client.

Additionally, the carousel generator lacked an advisory for typical slide depth; the story and feed-post generators had no voice-tone hint. Both gaps meant the AI received less calibration signal from the client's own content profile.

## Decision

### 1. `IClientAestheticEnsureFreshService` port (non-blocking refresh)

A new Application port `IClientAestheticEnsureFreshService` exposes a single method:

```
EnsureFreshAsync(orgId, clientId, ct) → EnsureFreshOutcome
```

`ClientAestheticEnsureFreshService` implements the port. It runs as a **non-blocking fire-and-forget** step at the start of the content generation pre-loop. Any exception from `EnsureFreshAsync` is caught, logged as a warning, and swallowed — generation proceeds regardless. Possible outcomes: `Fresh`, `RefreshEnqueued`, `SkippedInFlight`, `SkippedNoOwnAccount`.

The ensure-fresh logic:

1. Resolves the client from `ISocialClientRepository` to read its per-client threshold (see §3 below).
2. Calls `IProfileAnalysisRepository.HasNonTerminalAnalysisForClientAsync` (debounce guard) — skips enqueue if an analysis is already running.
3. Calls `IProfileAnalysisRepository.GetLatestAnalyzedCompletedAtAsync` — compares against the effective threshold.
4. If stale, resolves the client's own Instagram account via `IClientOwnAccountRepository`, creates and persists a new `ProfileAnalysis` entity, enqueues via `IProfileAnalysisJobQueue`, returns `RefreshEnqueued`.

This mirrors the `AnalyzeProfileHandler` create→persist→enqueue pattern and inherits the same transactional boundary via `IUnitOfWork`.

### 2. `IClientAestheticProfileReader` port (richer contract)

`IClientAestheticDescriptorReader` (returning `IReadOnlyList<string>`) is replaced by `IClientAestheticProfileReader`:

```
GetProfileAsync(orgId, clientId, ct) → ClientAestheticProfile?
```

`ClientAestheticProfile` is a sealed Application-layer record with four fields:
- `DetectedBackgroundImages: IReadOnlyList<string>` — visual style descriptors
- `VoiceTone: string?` — voice tone from `ai_analysis_json`
- `AverageSlidesPerCarousel: double?` — from `server_derived_stats_json`
- `AnalyzedAtUtc: DateTime` — timestamp of the source analysis

Infrastructure provides `EfClientAestheticProfileReader`. The processor reads the profile lazily (only when policy is `ClientAesthetic`) and threads `AestheticProfile` into `OrchestratorRequest` → `GenerationContext` → generators.

### 3. Per-client staleness threshold (`Client.AestheticRefreshThresholdDays`)

`Client` gains an `int? AestheticRefreshThresholdDays` property with a `SetAestheticRefreshThresholdDays(int? days, DateTime nowUtc)` setter. Valid non-null range: 1–365 days. `null` means the system default (30 days, `AestheticFreshness.DefaultStalenessThresholdDays`).

The value is persisted via a new additive EF migration: `aesthetic_refresh_threshold_days smallint NULL` on `social.clients`. Existing rows remain `NULL` (system default). The field is exposed in the client admin API (`SocialClientResponse`, `CreateSocialClientCommand`, `UpdateSocialClientCommand`) and the web ABM form.

`EfSocialClientRepository` maps the field in all three critical sites: `ToData` (create), `Update` method body (explicit assignment — predecessor bug guard identical to `BackgroundSourcingPolicy`), and `ToDomain`/`Restore`.

### 4. Aesthetic guidance threading into generators

`GenerationPromptContext.AestheticGuidance(ClientAestheticProfile? p)` returns an advisory string appended **after** `ConcisionConstraint` (additive, never replacing). The guidance:

- Includes voice-tone hint when `VoiceTone` is non-null.
- Includes an avg-slides hint in `CarouselGenerator` only (carousel-only advisory); avg-slides is clamped 3–10 in the rendered hint only (the unclamped value is stored in `ProfileStats.AverageSlidesPerCarousel`).
- Returns empty string when profile is null or both fields are null.

Prompt version constants are bumped: `Carousel → v4`, `Story → story-single.v3`, `FeedPost → feed-single.v3`.

### 5. Profile metric additions

`ProfilePost` gains `int? CarouselChildrenCount` (null for non-carousel posts). `ProfileStats` gains `double? AverageSlidesPerCarousel` (mean of non-null carousel child counts, rounded to 1 d.p.; null when no carousel posts exist). The Apify adapter populates `CarouselChildrenCount` best-effort from `childrenCount` / `children` / `sidecar` array length. `maxPostsPerProfile` increases from 12 to 18 to improve sample size.

## Consequences

- The `ClientAesthetic` policy now refreshes stale profiles automatically before each generation run. Failure to refresh never fails the job.
- `IClientAestheticDescriptorReader` is fully retired; `IClientAestheticProfileReader` is the single reader contract for aesthetic profile data.
- Generator prompts receive voice-tone and avg-slides hints when a profile is available, improving content calibration at zero additional AI cost.
- Clients can tune refresh frequency independently via `AestheticRefreshThresholdDays`; the system default (30 days) applies for clients that do not configure an override.
- The `aesthetic_refresh_threshold_days` column is `smallint NULL`; existing rows are unaffected.
- `ConcisionConstraint` text is preserved in all generators; aesthetic guidance is strictly additive.
- Carousel slide-count hint is clamped 3–10 in prompts only; the stored metric value is unclamped.

## Related

- Proposal / Spec / Design: `openspec/changes/content-aesthetic-generation/`
- Builds on: ADR 0005 (Background Sourcing Strategy Port — `ClientAesthetic` seam)
- Builds on: ADR 0003 (Profile Analysis bounded-context isolation)
- Migration: `SocialStudio.Infrastructure/Persistence/Postgres/Migrations/20260621100602_Add_AestheticRefreshThresholdDays.cs`
