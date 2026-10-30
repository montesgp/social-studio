# Design — content-discovery-intelligence

Hexagonal design for multi-source content discovery: an ordered `ContentDiscoveryChain` that **replaces** the single
`IContentDiscoveryService` registration, three sources routed by `SearchCriterion`, a pure opportunity scorer and
Trending/Proven classifier, two additive `social` tables, and a ranked shortlist from which the operator selects
**exactly one** candidate that is analysed through the **existing** single-analysis start+poll path. Apify/EF/ASP.NET
stay in Infrastructure. Domain stays untouched (validated in §9).

## Status
Design — **amended 2026-08-09** for the two owner corrections (see `proposal.md` `## Scope Amendment`). Architecture
change (new Application port family + new persistence model + DI registration replacement) → ADR
`docs/architecture/adr/0012-content-discovery-source-chain-and-candidate-persistence.md`, amended with this design.

**Amendment summary** — what changed in this document:
- §0 D8 replaced (fan-out → single selection); **D11**, **D12**, **D13** added (workflow/step keys and settings-UI
  visibility, unconfigured-vs-empty signal, no actor id in code).
- §2.2 `AnalyzeTopCandidatesCommand` **removed** — single selection needs no new Application command.
- §3.2 `ContentDiscoveryPlan` gains `UnconfiguredSourceKeys` (CD-13).
- §7 spike **restructured**: it verifies the schema of whatever actor the operator configured; no actor id is named.
- §8 rewritten: §8.1 workflow/step/checklist wiring, §8.2 single-selection instant analysis replaces the fan-out.
- §11/§12/§13/§14 updated accordingly. Nothing else in this design changed.

**Blocking**: §7 (Apify niche-search schema spike) must resolve before any adapter code is committed.

---

## 0. The decisions (read this first)

| # | Question | Decision | One-line rationale |
|---|---|---|---|
| **D1** | Port return shape | `DiscoverAsync` returns `ContentDiscoveryPlan(Candidates, Shortfall, FailedSourceKeys)` | CD-01/CD-11 demand shortfall + failed keys *at the port*; a bare list forces the chain to swallow them or leak them through a side channel. Mirrors `ContentIdeaPlan`. |
| **D2** | Metric availability | Plain `long?` — `null` = network does not report, `0` = reported zero | Nullable IS the explicit availability. A parallel `(value, isAvailable)` wrapper gives two ways to say one thing and will drift. |
| **D3** | Dedup identity (OQ-3) | `candidate_key = "{network}:{normalized content URL}"`, one pure function used by both chain and DB | The URL is what the analyzer consumes anyway; `author+postId` is not stably present in every source payload. |
| **D4** | Criterion routing (OQ-2) | Source declares `Supports(criterion)`; `niche` gets the niche source **only** — no filler | Filling a `niche` request with reference-account posts silently answers a different question. Shortfall is the honest answer. |
| **D5** | Unsupported criteria | `viral`/`competitors`: no source claims them → chain consults **all** sources in order | CD-03. Silent fallback beats an error for values already valid in the catalog. |
| **D6** | Candidate persistence | Append-only observation rows sharing `candidate_key`; **no** identity/observation table split | CD-08 needs identity + a second `observed_at`, nothing more. Normalizing now buys a join for a reader that does not exist. |
| **D7** | Scorer weights (OQ-5) | Fixed constants in code | Same reasoning that killed the weighted source mix: configuration over a single scorer is dead config with maintenance cost. |
| **D8** | Analysing the chosen candidate (CD-10, OQ-6 now moot) | **Single selection reuses the existing path verbatim**: the frontend sends the selected candidate's `ContentUrl` to the existing `AnalyzeSingleContentCommand` endpoint and polls the existing status endpoint, rendering the existing overlay. **No new Application command, no new endpoint, no new queue usage authored by this change.** | The "screen-blocking progress bar" is not a synchronous HTTP call: `AnalyzeSingleContentHandler` does `StartAsync` → `ISingleAnalysisJobQueue.EnqueueAsync` → return id, and `single-analyzer-client.tsx` polls `GET` every 2500 ms (`POLL_INTERVAL_MS`) mapping `SingleContentAnalysisStatus` onto `ANALYSIS_STEPS`. Reusing *that* is literally "the same UX"; anything synchronous would be a second mechanism. |
| **D9** | Retention (OQ-4) | No purge in this change; index `(organization_id, observed_at)` so a later purge is one ranged delete | A purge policy with no operator-visible growth data yet would be guesswork. Named follow-up. |
| **D10** | Domain | **Untouched — confirmed** (§9) | Candidates carry no invariant and no lifecycle. Revisit trigger named. |
| **D11** (corrected) | Where the discovery stage is configured (CD-12, INT-DISC-02/03) | New **analysis type** `content-discovery` under the **existing `single-analyzer` workflow key**, holding ONLY the `discover-niche-content` step — sibling of `content-analysis`/`profile-analysis`, *not* a new top-level workflow key and *not* placed under `generator` | Once the operator-selected candidate becomes a `SingleContentAnalysis`, the downstream pipeline (`CreateContentGenerationBatchCommand`, `GenerateContentPackageCommand`) anchors to that `AnalysisId`, not to a URL — it is IDENTICAL to the by-URL `content-analysis` flow. Same pipeline ⇒ SAME configuration: the `analyze-content`/`generate-content`/`generate-image` steps are reused from `content-analysis`, never duplicated. (An earlier iteration placed this under `generator` and duplicated those three AI steps to make the group self-sufficient — rejected: the operator would have to configure the same AI providers twice, and the settings UI would hide the stage under the wrong tool.) The settings selector is built from `checklistGroups` (`integrations-settings-client.tsx:519-530`), today `single-analyzer` + `generator` (`GetOrganizationIntegrationsHandler.cs:134-138`); a workflow key with no matching group still renders "not configurable yet" — the trap at `WorkflowConfigurationCatalog.cs:123-126` — but `single-analyzer` already has one. Analysis types render inside the selected workflow (`integrations-settings-client.tsx:1844`), so the stage is visible under Single Analyzer, where the operator actually looks for it. |
| **D12** | Unconfigured ≠ empty (CD-13) | **`ContentDiscoveryPlan` gains `UnconfiguredSourceKeys`** — a third list, distinct from `FailedSourceKeys` | Confirmed, not assumed: `FailedSourceKeys` alone cannot carry it. "Actor mapping absent" and "Apify returned 500" are the same key in the same list, so the operator cannot be told *"configure this stage"* rather than *"the provider is down"*. One extra list is cheaper than an error taxonomy the UI would have to parse. |
| **D13** | Actor identity (CD-12, INT-DISC-01/04) | **No literal actor id exists anywhere in Application or Infrastructure** — including tests, fixtures' filenames and the adapter. Resolution is `(networkKey="tiktok", variantKey="niche-search")` → `actor_mappings_json`, exactly `ApifyProfileScrapeResolver.cs:146-163`, which returns a structured failure on an empty mapping instead of substituting a default | The house pattern already refuses to invent an actor. A code-level default would make every organization silently depend on one vendor actor and would make the operator's configuration a lie. |

Assumed product values, unchanged from the proposal and configurable: trending window **14d**, proven floor **90d**,
TOP **N = 8**, baseline TTL **30d**, co-occurrence threshold **K = 3**.

---

## 1. Architecture approach

Dependency rule, unchanged: **Domain ← Application ← Infrastructure / Api**.

- **Application / Abstractions** — the port family and DTOs, next to the existing `IContentDiscoveryService`:
  `IContentDiscoverySource`, `IContentCandidateRepository`, `IAccountBaselineRepository`, `INicheSearchResolver`,
  the reshaped `DiscoveredContent` / `ContentDiscoveryRequest`, and `ContentDiscoveryPlan`.
- **Application / Features** — `ContentDiscoveryChain` (the new `IContentDiscoveryService`), the three sources, the
  pure `OpportunityScorer` + `TrendingProvenClassifier`, and `AccountBaselineProvider` (read-through cache).
- **Infrastructure** — the Apify niche-search resolver/adapter, EF entities + mapping + one migration, the two
  repositories, and the DI **replacement**.
- **Api / Frontend** — **no new endpoints and no new analysis orchestration**. `InMemoryContentWorkflowService`
  passes the criterion; the criterion selector already exists and only its `own` option starts succeeding; the
  shortlist's "analyze this one" action calls the **existing** single-analysis start endpoint and reuses the
  **existing** polling overlay (§8.2).

---

## 2. Component map

### 2.1 Application — ports and DTOs (`Abstractions/Social/`)

| Element | Path | Change |
|---|---|---|
| `DiscoveredContent` | `IContentDiscoveryService.cs` | **Modified (breaking)** — see §3.1 |
| `ContentDiscoveryRequest` | `IContentDiscoveryService.cs` | **Modified (breaking)** — gains `SearchCriterion` |
| `IContentDiscoveryService` | `IContentDiscoveryService.cs` | **Modified (breaking)** — returns `ContentDiscoveryPlan` |
| `IContentDiscoverySource` | `ContentDiscovery/IContentDiscoverySource.cs` | **New** — `SourceKey`, `Order`, `Supports(criterion)`, `DiscoverAsync` |
| `IContentCandidateRepository` | `ContentDiscovery/IContentCandidateRepository.cs` | **New** — `RecordObservationsAsync`, `GetLatestByKeysAsync` |
| `IAccountBaselineRepository` | `ContentDiscovery/IAccountBaselineRepository.cs` | **New** — `GetAsync(org, network, handle)`, `UpsertAsync` |
| `INicheSearchResolver` | `ContentDiscovery/INicheSearchResolver.cs` | **New** — keyword/hashtag search port (Apify-free signature) |
| `CandidateIdentity` | `ContentDiscovery/CandidateIdentity.cs` | **New** — pure `From(network, url)` dedup key (D3) |

### 2.2 Application — features (`Features/Social/ContentGeneration/Discovery/`)

| Element | Change | Note |
|---|---|---|
| `ContentDiscoveryChain` | **New** | The `IContentDiscoveryService`. Ordered composition; mirrors `ContentIdeaChain` line-for-line in shape |
| `ReferenceAccountDiscoverySource` | **Modified** (moved from `Ideas/ReferenceAccountContentDiscovery.cs`) | Now an `IContentDiscoverySource`, `Order: 1`, `Supports("references")` |
| `OwnAccountDiscoverySource` | **New** | `Order: 2`, `Supports("own")`. Near-clone over `IClientOwnAccountRepository` + the same `IProfileScrapeResolver` |
| `TikTokNicheSearchSource` | **New** | `Order: 3`, `Supports("niche")`. Consumes `INicheSearchResolver`. **Gated on the §7 spike** |
| `OpportunityScorer` | **New** | Pure, I/O-free (§6) |
| `TrendingProvenClassifier` | **New** | Pure (§6.3) |
| `AccountBaselineProvider` | **New** | Read-through cache over `IAccountBaselineRepository` + `IProfileScrapeResolver` + `IProfileMetricCalculator` |
| `ContentWorkflowCatalog` | **Modified** | `"own"` / `"propios"` aliases — the defect fix |

> **Removed by the 2026-08-09 amendment**: `AnalyzeTopCandidatesCommand` (+ handler) and `TopCandidateAnalysisPlan`.
> Single selection needs no new Application command — the existing `AnalyzeSingleContentCommand` already does
> exactly one URL (§8.2). It returns to scope in the **Magic Generator** follow-up.

### 2.3 Infrastructure

| Element | Change | Where |
|---|---|---|
| `ApifyActorMappingLookup` | **New (extracted)** | `Social/Providers/ApifyActorMappingLookup.cs` — `GetAllMappedActors` lifted out of `ApifyProfileScrapeResolver` (which delegates, keeping its tests green) |
| `ApifyNicheSearchResolver` | **New** | `Social/Providers/ApifyNicheSearchResolver.cs` — `(tiktok, "niche-search")` lookup + credential resolution, same failure shape as `ApifyProfileScrapeResolver` |
| `TikTokNicheSearchAdapter` | **New** | `Social/Providers/Apify/TikTokNicheSearchAdapter.cs` — input build + `MapItem`. **Not committable before §7** |
| `DiscoveredCandidateData`, `AccountBaselineData` | **New** | `Persistence/Postgres/Entities/` |
| `AppIdentityDbContext` mapping | **Modified** | Two entity blocks in the `content_generation_batches` style (§5) |
| Migration `Add_ContentDiscoveryCandidatesAndAccountBaselines` | **New** | `Persistence/Postgres/Migrations/` — the only new migration |
| `EfContentCandidateRepository`, `EfAccountBaselineRepository` | **New** | `Persistence/Postgres/Repositories/` |
| `DependencyInjection.cs:220` | **Modified — REPLACED** | §4 |
| `InMemoryContentWorkflowService.cs:130` | **Modified** | Pass `searchCriterion`; consume `plan.Candidates` |

---

## 3. Contracts

### 3.1 `DiscoveredContent` — the breaking change

```csharp
public sealed record DiscoveredContent(
    string Title,
    string Summary,
    string NetworkSource,
    string ContentUrl,
    string? ThumbnailUrl,
    long? Views,                   // WAS long — Instagram carousels genuinely have no view count
    long? Likes,                   // WAS long
    long? Comments,                // WAS long
    long? Shares,                  // NEW — TikTok reports it, Instagram does not
    long? Saves,                   // NEW
    DateTimeOffset? PublishedAtUtc,// NEW — null => classification Undetermined
    string SourceHandle,
    string SourceKey);             // NEW — which source produced it (audit + shortfall attribution)
```

`null` means **the network does not report this metric**; `0` means it reported zero (D2). `ReferenceAccountContentDiscovery`'s
current `post.VideoViewCount ?? 0` (line 128) is a zero-fill bug this change deletes: it becomes `post.VideoViewCount`.

The `AuthorBaseline` is deliberately **not** a field on `DiscoveredContent`. A candidate is a source observation;
the baseline is a separately-cached, separately-refreshed account property joined by
`(NetworkSource, SourceHandle)` at scoring time. Embedding it would force every source to fetch one — exactly the
per-candidate scrape the proposal forbids.

### 3.2 The chain contracts

```csharp
public sealed record ContentDiscoveryPlan(
    IReadOnlyList<DiscoveredContent> Candidates,
    int Shortfall,
    IReadOnlyList<string> FailedSourceKeys,        // ran and errored
    IReadOnlyList<string> UnconfiguredSourceKeys); // never ran: no enabled actor/step config (D12, CD-13)

public sealed record ContentDiscoveryRequest(
    Guid OrganizationId,
    Guid ClientId,
    IReadOnlyList<string> NetworkSources,
    int MaxResults,
    string SearchCriterion);       // NEW — normalized by ContentWorkflowCatalog before it gets here

public interface IContentDiscoverySource
{
    string SourceKey { get; }
    int Order { get; }                       // preference; DI registration order never decides
    bool Supports(string searchCriterion);
    Task<SourceDiscoveryResult> DiscoverAsync(ContentDiscoveryRequest request, CancellationToken ct);
}

// Three outcomes, not two — "unconfigured" is a first-class one (CD-13, D12).
public sealed record SourceDiscoveryResult(
    SourceOutcome Outcome,                              // Ran | Unconfigured | Failed
    IReadOnlyList<DiscoveredContent> Candidates);       // empty unless Ran
```

`Ran` with zero candidates is a **success**: the source is reported nowhere. `Unconfigured` is returned when the
step/actor resolution finds no enabled route — never thrown, never logged as an error, never a code-level fallback.

Chain algorithm (mirrors `ContentIdeaChain`):

```
sources = all.OrderBy(Order).ThenBy(SourceKey)
selected = sources.Where(s => s.Supports(criterion))
if selected is empty -> selected = sources            // D5: viral / competitors
foreach source in selected:
    remaining = MaxResults - collected.Count; if remaining <= 0 break
    try   r = source.DiscoverAsync(request with { MaxResults = remaining })       // ask only for the remainder
    catch OperationCanceled when ct -> rethrow
    catch ex -> log; failed.Add(source.SourceKey); continue                       // CD-01
    if r.Outcome == Unconfigured -> unconfigured.Add(source.SourceKey); continue  // CD-13, NOT a failure
    if r.Outcome == Failed       -> failed.Add(source.SourceKey); continue        // CD-01
    foreach c in r.Candidates ordered by (Views ?? -1) desc:
        if !seen.Add(CandidateIdentity.From(c.NetworkSource, c.ContentUrl)) continue   // CD-02
        collected.Add(c)
return new ContentDiscoveryPlan(collected, MaxResults - collected.Count, failed, unconfigured)
```

`CandidateIdentity.From` — pure: lowercase scheme+host, drop `www.`, drop query and fragment, trim trailing `/`,
prefix with the lowercased network key. The **same** function produces the persisted `candidate_key` (D3), so
in-run dedup and cross-run identity can never disagree.

---

## 4. The DI replacement (the trap, handled)

`DependencyInjection.cs:220` today:

```csharp
services.AddScoped<IContentDiscoveryService, ReferenceAccountContentDiscovery>();
```

is **deleted and replaced** — not supplemented. A second `AddScoped<IContentDiscoveryService, …>` would silently win
`GetRequiredService` with no compile or runtime error:

```csharp
// Discovery sources, in preference order. Order is the ARGUMENT, never registration order.
services.AddScoped<IContentDiscoverySource, ReferenceAccountDiscoverySource>();   // Order 1
services.AddScoped<IContentDiscoverySource, OwnAccountDiscoverySource>();         // Order 2
services.AddScoped<IContentDiscoverySource, TikTokNicheSearchSource>();           // Order 3
// The chain IS the discovery service. Exactly ONE registration of this interface may exist.
services.AddScoped<IContentDiscoveryService, ContentDiscoveryChain>();
```

**Regression test (`DiscoveryCompositionTests`)** — builds the real container from `AddInfrastructure` and asserts:

1. `GetRequiredService<IContentDiscoveryService>()` is `ContentDiscoveryChain`;
2. `GetServices<IContentDiscoveryService>().Count() == 1` — *this* is the assertion that catches the silent override, and it must be written first;
3. `GetServices<IContentDiscoverySource>().Select(s => s.SourceKey)` equals `{references, own, niche}`;
4. the resolved `Order` values are distinct.

---

## 5. Persistence

Schema `social`, snake_case, `organization_id` boundary column on both tables (`project.database.md`), FK to
`TenantData` with cascade delete, mapped in `AppIdentityDbContext` in the `content_generation_batches` style.

### 5.1 `social.discovered_candidates` — append-only observations (D6)

| Column | Type | Note |
|---|---|---|
| `id` | uuid PK | |
| `organization_id`, `client_id` | uuid | tenant boundary + owner |
| `candidate_key` | text NOT NULL | dedup identity (D3) |
| `network_key`, `source_key`, `search_criterion` | text | provenance |
| `content_url`, `author_handle`, `title` | text NOT NULL | |
| `summary`, `thumbnail_url` | text NULL | |
| `published_at` | timestamptz NULL | null → `Undetermined` |
| `views`, `likes`, `comments`, `shares`, `saves` | bigint **NULL** | null ≠ 0 (D2) |
| `opportunity_score` | double NULL | score at observation time |
| `classification` | smallint NULL | 0 Undetermined / 1 Trending / 2 Proven |
| `observed_at`, `created_at` | timestamptz NOT NULL | |

Indexes: `ix_discovered_candidates_org_client_key (organization_id, client_id, candidate_key)` — identity lookup;
`ix_discovered_candidates_org_observed (organization_id, observed_at)` — history reads and the future ranged purge (D9);
unique `ux_discovered_candidates_key_observed (organization_id, client_id, candidate_key, observed_at)` — makes a
double-write inside one run a hard error rather than a silent duplicate.

Growth: `DefaultResultCount = 72` per run. No purge in this change (D9); the second index makes one later.

### 5.2 `social.account_baselines` — one row per author per org

| Column | Type | Note |
|---|---|---|
| `id` | uuid PK | |
| `organization_id` | uuid | tenant boundary |
| `network_key`, `author_handle` | text NOT NULL | |
| `median_views`, `median_engagement` | double NULL | from `ProfileMetricCalculator`; null when not computable |
| `post_count` | int NOT NULL | sample size behind the medians |
| `computed_at`, `created_at`, `updated_at` | timestamptz NOT NULL | |

Unique `ux_account_baselines_org_network_handle (organization_id, network_key, lower(author_handle))` — the upsert target.
**No `client_id`**: a baseline is a property of the *author*, not of the client that happened to discover them; two
clients with the same referent share one cached scrape. Staleness = `computed_at < nowUtc - TTL` (CD-09).

One migration, `Add_ContentDiscoveryCandidatesAndAccountBaselines`, generated by `dotnet ef migrations add` into
`SocialStudio.Infrastructure/Persistence/Postgres/Migrations/` with its `.Designer.cs` and the updated
`AppIdentityDbContextModelSnapshot.cs`. Purely additive; no backfill; dropping both tables loses accumulated
candidates and cached baselines and nothing else.

---

## 6. Scoring, classification, baselines

### 6.1 Signals and weights (fixed, D7)

| Signal | Needs | Weight |
|---|---|---|
| Absolute engagement (`likes+comments+shares+saves`, available terms only) | ≥1 engagement metric | 0.25 |
| Engagement rate (engagement / views) | `Views` | 0.20 |
| Views per day | `Views` + `PublishedAtUtc` | 0.20 |
| Recency (linear decay over the proven floor) | `PublishedAtUtc` | 0.10 |
| Relative performance (`Views / baseline.MedianViews`) | cached baseline | 0.15 |
| Lexical co-occurrence | always | 0.10 |

`score = Σ(wᵢ·sᵢ over AVAILABLE i) / Σ(wᵢ over AVAILABLE i)` — literally "excluded from the denominator" (CD-04/CD-05).
Each `sᵢ` is min–max normalized **within the candidate set**; a degenerate set (all equal, or a single candidate)
yields `0.5` for that signal rather than a divide-by-zero.

### 6.2 Determinism (CD-05)

`Score(IReadOnlyList<DiscoveredContent> set, IReadOnlyDictionary<string, AccountBaseline> baselines, DateTimeOffset nowUtc)`
— `nowUtc` is a **parameter**, never `DateTime.UtcNow` inside. That single choice is what makes age-dependent signals
unit-testable and the whole scorer a pure function. Ties break on `candidate_key` ordinal, so ranking is total.

### 6.3 Classification (CD-06)

`PublishedAtUtc == null` → `Undetermined`, first and unconditionally. Otherwise `age ≤ 14d` and
`viewsPerDay > baseline.MedianViews/day` (falling back to the candidate-set median when no baseline) → `Trending`;
`age ≥ 90d` and `views > baseline median × factor` → `Proven`; anything else → `Undetermined`. Snapshot proxy, never
a velocity measurement — documented on the type.

### 6.4 Co-occurrence signal (OQ-7)

Tokenize `Title + " " + Summary`: lowercase, strip punctuation, split on whitespace, drop tokens < 4 chars and a
small ES+EN stopword list. A candidate's raw signal = max over its distinct tokens of the number of **distinct author
handles** in the set whose candidate contains that token; below `K = 3` handles the raw signal is `0` (available, not
missing). Normalized by the set's distinct-author count. Cheap lexical proxy — explicitly not clustering.

### 6.5 Baseline economics (CD-09)

`AccountBaselineProvider.GetOrComputeAsync(org, network, handle, nowUtc)`: cache hit within TTL → return, no scrape.
Miss or stale → one `IProfileScrapeResolver.ResolveAsync` + `ProfileMetricCalculator` → upsert → return. Called **only**
for reference/own account handles (already scraped for free) and the TOP N after cheap filtering. **Never per raw
candidate** — enforced by call site, and pinned by a test asserting the resolver is invoked at most `N` times for a
100-candidate set.

---

## 7. Apify niche-search **schema** spike — BLOCKING (restructured by the amendment)

No file under `Infrastructure/Social/Providers/Apify/TikTokNicheSearchAdapter.cs` may be committed until this closes.

**What the spike is now.** It is a *schema* spike, not an actor-selection spike. It takes the actor id **the operator
has configured** for `(tiktok, niche-search)` in `actor_mappings_json` — supplied to the spike as an input, never
chosen by it — runs one call, and records the response. This design **names no actor**, and per D13/CD-12 **no actor
id may appear in the adapter, the resolver, DI, the catalog, a constant, a test or a fixture file name**. Which actor
a given organization uses is data the operator owns; the adapter's only contract is with the *shape* of the reply.

**Inputs to try** against the configured actor: `{ searchQueries: ["<keyword>"], resultsPerPage: 20,
shouldDownloadVideos: false, … }` and `{ hashtags: ["<tag>"], resultsPerPage: 20, … }`. Whichever the configured
actor honours becomes `TikTokNicheSearchAdapter`'s input builder.

**Fields to confirm**: `createTimeISO`, `playCount`, `diggCount`, `commentCount`, `shareCount`, `collectCount`,
`authorMeta.name`/`nickName`, `webVideoUrl`, `text`, `videoMeta.coverUrl`, `hashtags[].name`. These are the fields
`TikTokApifyScrapeAdapter.MapItem` already parses for the URL flow, so a configured actor emitting that shape costs
the adapter almost nothing.

**Output**: one sanitized raw response saved as a test fixture under
`SocialStudio.Tests/Infrastructure/Social/Fixtures/tiktok-niche-search.json` — a *capability*-named file, not an
actor-named one. That fixture is the adapter's unit-test input; the adapter is tested against recorded reality,
never a live call.

**Degradation rules if a field is absent**

| Missing | Consequence |
|---|---|
| `createTimeISO` | `PublishedAtUtc = null` → every niche candidate is `Undetermined`; recency and views/day drop out of the denominator. Source still ships. |
| `shareCount` / `collectCount` | Stay `null` → excluded from the denominator. Source still ships. |
| `playCount` | **Escalate — do not ship.** Without views there is no engagement rate, no views/day and no relative performance; the source would be unrankable and would dilute the shortlist. Owner decision required. |
| No enabled `(tiktok, niche-search)` route configured | Source returns `Unconfigured` → `UnconfiguredSourceKeys`, **not** `FailedSourceKeys` (CD-13, D12). No actor id is substituted (CD-12, INT-DISC-01). |

### 7.1 When the configured actor returns a shape the adapter cannot map

This is the case the first pass left unstated, and it is not the same as "unconfigured". The operator has configured
*something*; it answered; the reply does not carry a mappable item array (or carries items with no `webVideoUrl`).

- `MapItem` is **per item and total**: an item it cannot map is **skipped**, not defaulted — never a candidate with an
  invented URL, a zero-filled metric or a fabricated handle.
- If **zero** items in a non-empty response map, the source returns **`Failed`** (→ `FailedSourceKeys`), logging the
  configured actor id, the item count and the first item's top-level key names — **never the raw payload and never the
  credential**. `Failed` rather than `Unconfigured` because the configuration exists and is wrong or points at an
  incompatible actor; telling the operator "not configured" would send them to fix something already done.
- If the response maps **partially**, the source returns `Ran` with what mapped and logs the skipped count. A partial
  answer is still an honest answer, and the chain's shortfall already reports the gap.
- The adapter never throws on shape: a `JsonException` inside `MapItem` is caught per item. Only transport/auth
  failures surface as `Failed` through the existing Apify failure path.

---

## 8. Configuration-driven routing, and the single selected analysis

### 8.1 Where the operator configures the discovery actor (D11 corrected, D13 — CD-12, INT-DISC-02/03/04)

New `ActorVariantKey` `"niche-search"`, carried by the existing generic `(networkKey, variantKey)` → actor-id mapping
with **no framework change**. A new **analysis type** is added to the **existing `single-analyzer` workflow key**, as a
sibling of `content-analysis`/`profile-analysis` — chosen because the discovery step feeds the SAME downstream pipeline
as `content-analysis` once a candidate is selected, so it must share the SAME AI-step configuration rather than
duplicate it:

```
single-analyzer                    (existing key — already has a checklist group, so it is already selectable)
├── content-analysis               (existing — by URL: scrape-video, analyze-content, generate-content, generate-image)
├── profile-analysis               (existing)
└── content-discovery              NEW WorkflowAnalysisTypeDefinition, label "Content discovery"
    └── discover-niche-content     NEW WorkflowStepDefinition — the ONLY step; no duplicated AI steps
        operationKey "niche-search", operationLabel "Niche search",
        RequiresNetwork: true, SupportedNetworkKeys: ["tiktok"],
        AllowedProviderCategories: [DataExtraction], AllowedProviderKeys: ["apify"],
        SupportsModelSelection: false, SupportsActorSelection: true, ActorVariantKey: "niche-search"
```

Resolution at run time — the **only** way an actor id enters the process:

```
ApifyNicheSearchResolver
  └─ IWorkflowConfigurationResolver.ResolveStepAsync(
         WorkflowStepResolutionRequest(orgId, "single-analyzer", "content-discovery",
                                       "discover-niche-content", "niche-search", "tiktok"))
     → WorkflowStepRouteCandidate[]  →  .Where(IsEnabled).OrderBy(Priority).FirstOrDefault()
        null / no ActorId  →  fall back to GetAllMappedActors(actor_mappings_json, "tiktok", "niche-search")
                              (still configuration — this is exactly ApifyProfileScrapeResolver.cs:146-151)
        still empty        →  SourceOutcome.Unconfigured        (CD-13 — never a literal id, never a throw)
        otherwise          →  dispatch in order, then §7.1 shape rules
```

**Why `single-analyzer` and not `generator` (corrected; INT-DISC-03 still satisfied by construction).** Once the
operator selects one discovered candidate, it becomes a `SingleContentAnalysis` and the rest of the pipeline
(`CreateContentGenerationBatchCommand`, `GenerateContentPackageCommand`) anchors to that `AnalysisId` — identical to
the by-URL flow already configured under `single-analyzer`/`content-analysis`. Placing discovery under `generator`
would have forced a duplicate of `analyze-content`/`generate-content`/`generate-image` (a second, redundant AI-provider
configuration for the operator to maintain) purely so the `generator` checklist group would render the stage — a
mechanical UI justification, not a product one. `single-analyzer` already has a checklist group, so no new group is
needed and no AI steps are duplicated: `content-discovery` reuses `content-analysis`'s AI steps by construction, and
the settings UI shows the discovery stage where the operator actually looks for a Single Analyzer input — under Single
Analyzer.
**Rejected alternatives**: (a) the original `generator` placement — rejected because it forces double AI-provider
configuration; (b) a new `discovery` workflow key with its own group — rejected for the same duplicated-checklist
reason documented in the original D11.

**Operator visibility, additive.** One checklist **item** is appended to the existing `singleAnalyzerChecklist`:
`("single-analyzer-niche-search-actor", "Configure a TikTok niche-search actor", Required: false, …)`, satisfied when
an enabled `(single-analyzer, content-discovery, discover-niche-content, niche-search, tiktok)` route resolves.
Optional, not required — discovery still works on `references`/`own` with the niche stage unconfigured; the item is
the operator's pointer to the stage, not a gate.

**Seed, not fallback (INT-DISC-02).** A placeholder `(tiktok, niche-search)` mapping MAY be seeded as **data** so the
configuration surface is non-empty. It is fully editable and removable; clearing it degrades to `Unconfigured`, never
to a code-level default. The seed lives in the same seeding path as other configuration data — never in the resolver.

**Gotcha (verified, INT-DISC-04):** `ApifyActorMappingBackfill.Transform` retags legacy `default`/empty entries as
*last-two → `"profile"`, earlier → `"social-extraction"`*. It is **not** modified — `"niche-search"` is never produced
by backfill. Silently extending the backfill rule would re-tag existing extraction actors and break the single
analyzer.

### 8.2 The operator selects exactly one candidate (D8, CD-10) — replaces the removed fan-out

The chain returns a ranked shortlist (TOP N **for display**; scoring, classification and ranking are unchanged). The
system starts **no** analysis by itself. The operator picks **one**, and that one takes the path the Single Analyzer
by URL already takes:

```
Discovery shortlist (frontend)
  │  operator clicks "Analyze this one" on exactly ONE candidate
  ▼
POST existing single-analysis endpoint  →  AnalyzeSingleContentCommand(org, userId, candidate.ContentUrl, clientId)
  └─ AnalyzeSingleContentHandler:  ISingleContentAnalyzer.StartAsync  →  ISingleAnalysisJobQueue.EnqueueAsync
                                   → returns { Id, Status = Requested }
  ▼
Frontend blocking overlay: poll GET status every POLL_INTERVAL_MS (2500 ms), 5-min hard cap,
  SingleContentAnalysisStatus → ANALYSIS_STEPS (Queued → Fetching content → Content ready → AI analysis)
  ▼
Analyzed  →  AnalysisId  →  existing generation rails (format choice, package generation) UNCHANGED
```

**Grounded, not assumed**: "screen-blocking and synchronous" is the *operator's* experience, not the transport.
`AnalyzeSingleContentHandler` is start-and-enqueue and `single-analyzer-client.tsx` polls; the overlay is what blocks
the screen. Reusing that exact pair is the only way to get "the same UX" — a genuinely synchronous request would be a
**second** mechanism with its own timeout, its own failure rendering and no stage granularity, and would hold an HTTP
connection open for the length of a scrape plus an AI call.

**What this change therefore authors on this path: nothing on the backend.** No `AnalyzeTopCandidatesCommand`, no new
endpoint, no new queue, no new progress channel, no new concurrency primitive. The only new work is frontend: the
shortlist enforces single selection and reuses the existing overlay component. Failure is whatever the existing
overlay already renders for one analysis — there is no partial-batch semantics because there is never a batch.

**Named follow-up — Magic Generator.** Multi-select background batch analysis of discovered candidates (the removed
fan-out, `AnalyzeTopCandidatesCommand`, partial-batch failure reporting, and the `ISingleAnalysisJobQueue`
parallelism-of-3 reasoning) moves there intact. It is roadmap phase 7 in `state.yaml`, out of scope here.

---

## 9. Architecture boundaries — Domain untouched, validated

The proposal decided Domain stays untouched. **Challenged and confirmed**, with the reasoning made explicit because
the nearest precedent points the other way: `ProfileMetricCalculator` (Application) returns `ProfileStats`, which
lives in **Domain** (`Domain.Social.ProfileAnalysis`).

The distinction that holds: `ProfileStats` is owned by the persisted `ProfileAnalysis` aggregate, rehydrated with it
and covered by its invariants. A discovered candidate has **no lifecycle** — it is a source observation, recomputed on
every run, written as flat rows and read only by discovery itself. Its score and classification are derived read-model
values, not enforced business state. The existing `DiscoveredContent` already lives in `Application.Abstractions.Social`
for exactly that reason; moving its neighbours to Domain would split one concept across two layers to buy nothing.

**Revisit trigger, stated so it is not forgotten**: the moment an operator can *act* on a candidate (select, dismiss,
archive) it acquires a lifecycle and invariants, and `DiscoveredCandidate` becomes a Domain aggregate. That is the
follow-up that also owns the retention policy (D9).

Everything else: no Apify / EF / Npgsql / ASP.NET type in Domain or Application — Apify JSON stays inside
`TikTokNicheSearchAdapter` and `ApifyNicheSearchResolver`; `INicheSearchResolver` takes a keyword and returns
`DiscoveredContent`. The scorer and classifier take plain records plus `nowUtc`. Architecture gate must stay green.

## 10. Threat matrix

`N/A` — no routing of shell commands, no subprocess, no VCS/PR automation, no executable-file classification, no
process integration. The only new external surface is one HTTPS call to Apify through the existing
`IHttpClientFactory` + `IIntegrationResolver` credential path, with credentials never logged (existing convention).

---

## 11. Test strategy (Strict TDD — RED first, every row)

| Layer | What | How |
|---|---|---|
| Unit | `CandidateIdentity.From` normalization | Pure table test: scheme/host case, `www.`, query, fragment, trailing slash |
| Unit | Chain: remainder-only asks, failure tolerance, dedup, shortfall | Fake sources; mirrors `ContentIdeaChainTests` |
| Unit | **Chain: `Unconfigured` lands in `UnconfiguredSourceKeys`, `Ran`-with-zero lands in neither** (CD-13) | Two fake sources, one per outcome; asserts the two lists are disjoint and that an empty successful run is reported nowhere |
| Unit | Chain routing: `niche`→niche only, `own`→own, `viral`/`competitors`→all | Fake sources with distinct `Supports` |
| Unit | Scorer: determinism, availability-aware denominator, degenerate set | Fixed `nowUtc` parameter; assert two identical calls are equal and that a missing signal changes the denominator, not the numerator |
| Unit | Classifier: null publish date → `Undetermined` before any other branch | Explicit test that it is never defaulted to `Proven` |
| Unit | Co-occurrence: K threshold, distinct-author counting, stopwords | Pure |
| Unit | `TikTokNicheSearchAdapter.MapItem` | Against the §7 recorded fixture — **no live calls** |
| Unit | `TikTokNicheSearchAdapter`: unmappable item is skipped; **zero-of-many mapped → `Failed`**; partial → `Ran` (§7.1) | Mutated copies of the recorded fixture |
| Unit | `ApifyNicheSearchResolver`: configured route → that id; **no enabled route → `Unconfigured`, no id substituted** (CD-12) | Fake `IWorkflowConfigurationResolver` + recorded `actor_mappings_json`; the `ApifyProfileScrapeResolverTests` pattern |
| Unit | Catalog: `single-analyzer`/`content-discovery`/`discover-niche-content` resolves and carries `ActorVariantKey: "niche-search"`; `content-discovery` carries no duplicated `analyze-content`/`generate-content`/`generate-image` steps | `WorkflowConfigurationCatalog.FindStep` / `FindAnalysisType` |
| Unit | `GetOrganizationIntegrationsHandler` returns a `single-analyzer` checklist group containing the niche-search item (INT-DISC-03) | Existing handler test host |
| Unit | `ApifyActorMappingBackfill.Transform` **never** emits `niche-search` (INT-DISC-04) | Legacy `default`/empty entries in, assert absent out; existing `profile`/`social-extraction` unaffected |
| **Guard** | **No literal actor id in shipped source** (CD-12/D13) | Repository-wide assertion over `SocialStudio.Application` + `SocialStudio.Infrastructure` sources: no Apify actor-id-shaped literal (17-char alphanumeric token, and no `"clockworks/…"`) outside test fixtures. Written RED first — it is the assertion that makes "no forced actor" enforceable rather than aspirational |
| Unit | `AccountBaselineProvider`: fresh hit → 0 scrapes; stale → 1 recompute; ≤N scrapes for 100 candidates | Fake repository + counting resolver |
| Composition | `DiscoveryCompositionTests` (§4) | Real container; **`GetServices<IContentDiscoveryService>().Count() == 1` is the load-bearing assertion** |
| Integration | Repositories + migration round-trip; `candidate_key` unique constraint; baseline upsert idempotence | Existing EF test-host pattern (`ClientReferentAccountRepositoryTests`) |
| Integration | `own` criterion end-to-end no longer returns `SearchCriterionInvalid` | `ContentWorkflowServiceTests` |
| Frontend | Shortlist allows **exactly one** selection and starts the existing single-analysis call with that candidate's URL (CD-10) | Component test; asserts one POST to the existing endpoint and that selecting a second candidate replaces rather than adds |
| Frontend | The reused overlay renders the existing staged progress and the existing failure state for that one analysis | Component test over the existing overlay with mocked poll responses |

**Coordinated break (compiler is the safety net).** The `DiscoveredContent` / `ContentDiscoveryRequest` /
`IContentDiscoveryService` change lands as ONE unit — until every consumer moves the solution does not build, so it
cannot ship half-applied. Test-first order:

1. `DiscoveredContent` + `ContentDiscoveryPlan` + `ContentDiscoveryRequest` shape
2. `CandidateIdentity`
3. `IContentDiscoverySource` + `ContentDiscoveryChain`
4. `ReferenceAccountContentDiscoveryTests` → `ReferenceAccountDiscoverySourceTests` (nullable views, no zero-fill)
5. `ContentWorkflowServiceTests` — criterion passed through, `plan.Candidates` consumed
6. `DiscoveryCompositionTests`

Commands: `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj -c Release`, then
`pwsh -File scripts/gates/Run-Gates.ps1` before completion.

---

## 12. Rollback

- **Behavioral rollback is one line**: restore `AddScoped<IContentDiscoveryService, ReferenceAccountContentDiscovery>()`
  — the chain and extra sources become dead code, not a broken system.
- Both tables are additive and read by nothing outside discovery; dropping them loses accumulated candidates and
  cached baselines only.
- The contract change (`DiscoveredContent` + `IContentDiscoveryService` + their consumers and tests) is one
  **coordinated rollback unit** — flagged so a revert is never attempted piecemeal.
- Removing the `(tiktok, niche-search)` actor mapping or disabling the `discover-niche-content` step disables the
  niche source with no code change; the chain reports it as `Unconfigured` by contract.
- The single-selection path adds **no backend code to roll back** — it calls the existing single-analysis endpoint.
  Reverting it is reverting the frontend shortlist action.
- The `"own"` alias fix is independently revertible and independently safe to keep.

## 13. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| Apify niche-search schema still unverified for whatever actor the operator configures | **High** | §7 schema spike blocks adapter code; §7.1 defines the unmappable-shape behavior; `playCount` absent = escalate, not ship |
| A literal actor id creeping into code as a "temporary" convenience | **High** | CD-12/D13; the §11 repository-wide no-actor-literal guard, written RED first |
| Discovery stage invisible in settings (the `WorkflowConfigurationCatalog.cs:123-126` trap) | Medium | D11 reuses the `single-analyzer` key, which already has a checklist group; asserted by the checklist-group test |
| Operator cannot tell "unconfigured" from "found nothing" | Medium | D12 `UnconfiguredSourceKeys` as a distinct channel; disjointness asserted |
| Silent DI override erasing the chain | **High** | Line 220 replaced; `GetServices<IContentDiscoveryService>().Count() == 1` written RED first |
| Contract change ripples through consumers | **High (certain)** | One coordinated unit; compiler blocks partial application; §11 ordering |
| Baseline scrape cost overrun | Medium | Known accounts + TOP N only; TTL cache; ≤N-scrapes test |
| Zero-filled metrics skewing cross-network scores | Medium | `long?` everywhere; availability-aware denominator, asserted |
| `candidate_key` normalization too aggressive/lax (URL variants) | Medium | Pure function, table-tested; one implementation shared by chain and DB |
| `discovered_candidates` unbounded growth | Medium | Accepted for this change (D9); `(organization_id, observed_at)` index makes the later purge a ranged delete |
| Trending/Proven read as measurement | Medium | Documented as snapshot proxy on the type and in the spec; `Undetermined` is first class |
| Instagram-only `references`/`own` | Medium | Stated limitation; empty non-throwing result for a TikTok-only client (CR-DISC-02) |
| PR ~3,000–4,000 lines vs 1,500 budget | High (accepted) | `size:exception` accepted by the owner; read order: contract → chain + DI → sources → scorer → persistence → config/routing → shortlist selection |

## 14. Next step

`sdd-tasks` — slice in seam order: (1) contract change unit (incl. `SourceDiscoveryResult` + `UnconfiguredSourceKeys`)
→ (2) chain + DI replacement + composition test → (3) reference + own sources → (4) scorer + classifier + baseline
provider → (5) persistence + migration + repositories → (6) **§7 schema spike** → (7) catalog step + checklist group
item + resolver (config-driven, no-actor-literal guard RED first) → (8) niche source + Apify adapter (§7.1 shape
rules) → (9) frontend shortlist single-selection reusing the existing start+poll overlay → (10) `own` alias + workflow
wiring → (11) ADR + docs/spec sync. This design owns the HOW; tasks owns the checklist.

The size forecast **drops** relative to the proposal: the ~200-line fan-out orchestration slice disappears and is
replaced by a frontend action over an endpoint that already exists.
