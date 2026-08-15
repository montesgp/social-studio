# Design — niche-discovery-engine

Hexagonal design for **Niche Discovery**: an operator-parameterised `DiscoveryRun` that sources channel/video
evidence through **one multi-origin port** (YouTube Data API v3 first, Apify second, both configuration-selected),
turns it into deterministic **Metrics**, interprets those into **Signals**, aggregates a renormalising
**OpportunityScore** plus a genuinely independent **ConfidenceScore**, compares a **source market** against a
**target market** from a data catalog, and writes append-only `NicheOpportunity` + evidence rows. The AI layer
names and groups; it never produces a number, and the type system — not a convention — is what makes that true.

> **Vocabulary guard.** `Social.ContentDiscovery` answers *"what should I generate FROM for this client?"*.
> `Social.NicheDiscovery` (this change) answers *"which niche/format/market is worth a NEW channel?"*.
> They share no namespace, no type and no `using`. Every mirrored contract below is **re-declared**, never imported.

## Status

Design. Inputs: `proposal.md`, `exploration.md`, `content-discovery-intelligence/design.md`, ADR-0012.
Architecture change (new Domain area, new Application port family, new persistence model, new provider category,
new credential type) → ADR `0013` is required, authored in WU-13 (§13). Six owner decisions are **settled inputs**
and are not re-opened here.

---

## 0. Decisions (read this first)

| # | Question | Decision | Rationale |
|---|---|---|---|
| **N1** | Provider port shape | **One** `INicheDiscoverySource` port with `Supports(DiscoveryStage)`; origins are adapters selected per stage by configuration | Settled decision 1. A per-provider port (`IYouTubeChannelSearchResolver`, as `exploration.md` §3 sketched) bakes the vendor into Application and is **rejected**: adding Apify would then add a port, not a row of config. |
| **N2** | Reuse `SourceOutcome` from ContentDiscovery? | **No — re-declare** `NicheSourceOutcome { Ran, Unconfigured, Failed }` in `Application.Abstractions.Social.NicheDiscovery` | The vocabulary guard forbids cross-capability imports. Cost is one 4-line enum; benefit is that the two capabilities can never be coupled by an "obvious" shared type. |
| **N3** | Can a Metric come from the AI? | **Structurally impossible** (§2.3): `Metric` has a private constructor and exactly one factory, `DeterministicMetricCalculator.Compute(EvidenceSet, nowUtc)`; the semantic contract types carry **no numeric member at all**; no scoring signature mentions a `Semantics` type. Three reflection guard tests enforce all three. | "Discouraged" decays under delivery pressure. A type you cannot construct does not. |
| **N4** | Labels crossing into a scorer | Allowed **only** as a closed `enum` parsed at the semantic boundary (`FormatTrait`, `ContentPatternKind`); unknown label → dropped, never coerced | AutomationSuitability legitimately needs AI-observed traits. A closed enum is a label, free text is a side channel. |
| **N5** | `NicheOpportunity` lifecycle | **Application read model, append-only** — one immutable observation of one run (settled decision 4). `DiscoveryRun` **is** a Domain aggregate (it has a real status lifecycle). | Mirrors ADR-0012 §7. **Revisit trigger, named**: the first time an operator can mark an opportunity *pursued*, *dismissed* or *snoozed*, it acquires invariants and becomes a Domain aggregate — that follow-up also owns retention. |
| **N6** | Market representation | `MarketCode` value object (`{iso639-1}-{ISO3166-1}`, e.g. `es-AR`), plus a seeded `MarketCatalogEntry` table/seeder. Display names are **i18n keys**, never literals | Settled: adding a country = one catalog row. A country cannot become a code branch because no code ever reads a market's *identity* — only its `MarketCode` string and its language partition (§3). |
| **N7** | Scorer weights | `OpportunityScoreWeights` record with a `Default`, **snapshotted onto the run** (`weights_json`) and optionally overridden per run request | ADR-0012 D7 chose fixed constants for a 6-signal single-tenant scorer. Different problem: here the run is the unit of reproducibility, so the weights must travel *with the run* or two runs are not comparable. |
| **N8** | Unavailable vs not-supported | Three-valued `SignalAvailability { Available, NotReported, NotSupportedYet }` | `CrossPlatformValidation` is not "the network didn't report it" — it is "this version does not measure it". Collapsing the two would tell the operator a lie about their data. |
| **N9** | Quota exhaustion | Zero usable results + provider quota error → `Failed`. Partial results then quota error → `Ran` with `PartialReason = ProviderBudgetExhausted` | The proposal's rule ("never *no opportunities found*") is satisfied; a truncated-but-usable run is still an honest answer, and `ConfidenceScore` already drops with the smaller sample. |
| **N10** | Semantic determinism | Derivation is **not** deterministic; *display* is — labels are persisted with the opportunity, and the **run**, not the opportunity, is the unit of comparison | Pretending an LLM clusters identically twice would be false. Saying so in the type docs and the UI is cheaper than a cache that lies. |
| **N11** | Semantic failure | Deterministic pipeline **must still rank**: fall back to lexical clustering (K=3 distinct channels), pattern `Unclassified`, AutomationSuitability → `NotReported`, run flagged `semantic_degraded` | Settled decision 3 puts AI in the first slice; it must not be a single point of failure for the whole run. |
| **N12** | Where the stage is configured | **New top-level workflow key `niche-discovery` WITH its own checklist group** in `GetOrganizationIntegrationsHandler` (third group beside `single-analyzer`/`generator`) | This is not a Single Analyzer input — the operator has no client yet. Reusing `single-analyzer` would hide it under the wrong tool. The `WorkflowConfigurationCatalog.cs:132-135` trap ("no group ⇒ *not configurable yet*") is paid for explicitly, not discovered later. |

---

## 1. Architecture approach

Dependency rule unchanged: **Domain ← Application ← Infrastructure / Api**.

```
 Operator ──POST run──► MapNicheDiscoveryEndpoints ──► StartDiscoveryRunCommand
                                                          │ (Domain: DiscoveryRun aggregate, status Requested)
                                                          ▼  enqueue
                                          INicheDiscoveryRunQueue ──► NicheDiscoveryRunProcessor
                                                                            │
   ┌──────────────── Infrastructure ────────────────┐                       │
   │ YouTubeDataApiNicheDiscoverySource  (origin A) │◄──── INicheDiscoverySource (ordered, per-stage)
   │ ApifyNicheDiscoverySource           (origin B) │       Ran | Unconfigured | Failed  + BudgetSpend
   │ NicheDiscoveryOriginConfiguration (per stage)  │                       │
   └───────────────────┬────────────────────────────┘                       ▼
              normalization boundary                       EvidenceSet (channels + videos, all metrics long?)
                                                                            ▼
                                    DeterministicMetricCalculator(EvidenceSet, nowUtc)  ── pure ──► MetricSet
                                                                            ▼
                     SemanticAnnotator (IAiCompletionGateway) ──labels only──► SignalBuilder ──► SignalSet
                                                                            ▼
                        TargetMarketEvaluator(MarketCode src, MarketCode tgt, MetricSet, SignalSet)
                                                                            ▼
              NicheOpportunityScorer(MetricSet, SignalSet, weights, nowUtc)  +  ConfidenceScorer(SampleShape)
                                                                            ▼
       INicheOpportunityRepository.AppendAsync  +  INicheDiscoveryEvidenceRepository.AppendAsync   (append-only)
                                                                            ▼
                              GET runs/{id}/opportunities  ──►  viability dashboard + opportunity detail
```

---

## 2. Component and namespace map

### 2.1 Domain — `Incoders.Hive.Domain/Social/NicheDiscovery/`

| File | Type | Note |
|---|---|---|
| `DiscoveryRunId.cs` | `readonly record struct DiscoveryRunId(Guid)` | mirrors `NicheId.cs` |
| `DiscoveryRun.cs` | `sealed class DiscoveryRun : Entity<DiscoveryRunId>` | the ONE aggregate: parameters + config snapshot + status lifecycle |
| `DiscoveryRunStatus.cs` | `enum { Requested, Running, Completed, PartiallyCompleted, Failed }` | |
| `DiscoveryWindow.cs` | `readonly record struct DiscoveryWindow(int Days)` | 7..365, default 90 (settled decision 5) |
| `DiscoveryDepth.cs` | `enum { Quick, Standard, Deep }` | settled decision 6 |
| `DiscoveryStage.cs` | `enum { ChannelSearch, VideoSearch, ChannelDetail, VideoDetail }` | the per-stage routing axis |
| `MarketCode.cs` | value object + `TryParse` | §3 |
| `MarketCatalogEntry.cs` | `sealed class : Entity<MarketId>` | `MarketCode`, `DisplayNameKey` (i18n key), `PrimaryLanguage`, `IsActive` |
| `Taxonomy/NicheTaxonomyNode.cs`, `SubNicheRef.cs`, `ContentPatternKind.cs`, `TopicLabel.cs` | taxonomy hierarchy | cross-references `Domain.Social.Niches.NicheId` **by value**, never by navigation |
| `NicheDiscoveryErrors.cs` | static `Error.Validation` / `Error.Failure` | codes `social.niche_discovery.{reason}` |

### 2.2 Application — `Abstractions/Social/NicheDiscovery/`

`INicheDiscoverySource.cs`, `NicheDiscoverySourceResult.cs` (+ `NicheSourceOutcome`), `NicheDiscoveryQuery.cs`,
`ChannelEvidence.cs`, `VideoEvidence.cs`, `EvidenceSet.cs`, `DiscoveryBudget.cs`, `IDiscoveryBudgetLedger.cs`,
`IDiscoveryRunRepository.cs`, `INicheOpportunityRepository.cs`, `INicheDiscoveryEvidenceRepository.cs`,
`IMarketCatalogRepository.cs`, `INicheDiscoveryOriginProbe.cs` (checklist readiness, mirrors
`INicheSearchConfigurationProbe`), `INicheDiscoveryRunQueue.cs`,
**`INicheOpportunityActivationSource.cs`** (the named-not-built Content Generator seam, §12).

### 2.3 Application — `Features/Social/NicheDiscovery/`

| Folder | Types |
|---|---|
| `DiscoveryRuns/` | `StartDiscoveryRunCommand`+`Handler`, `GetDiscoveryRunQuery`+`Handler`, `ListRunOpportunitiesQuery`+`Handler`, `GetOpportunityDetailQuery`+`Handler`, `NicheDiscoverySourceChain` (ordered, partial-tolerant), `NicheDiscoveryRunProcessor` |
| `Analytics/` | `DeterministicMetricCalculator`, `Metric`, `MetricSet`, `SampleShape` — **pure, I/O-free, `nowUtc` parameter** |
| `Semantics/` | `SemanticAnnotator`, `SemanticAnnotation`, `ClusterProposal`, `FormatTrait`, `LexicalClusterFallback` — **string/enum members only** |
| `Signals/` | `SignalBuilder`, `Signal`, `SignalKey`, `SignalAvailability`, `SignalSet` |
| `Scoring/` | `NicheOpportunityScorer`, `OpportunityScoreWeights`, `AvailabilityWeightedAggregate`, `ConfidenceScorer` |
| `MarketComparison/` | `TargetMarketEvaluator`, `TargetMarketDemand`, `TargetMarketCompetition`, `TargetMarketGap` |

### 2.4 Infrastructure — `Incoders.Hive.Infrastructure/Social/NicheDiscovery/`

`NicheDiscoveryOriginConfiguration.cs` (per-stage resolution, §4), `Origins/YouTubeDataApiNicheDiscoverySource.cs`,
`Origins/YouTubeDataApiClient.cs`, `Origins/YouTubeEvidenceMapper.cs` (**the normalization boundary**),
`Origins/YouTubeQuotaBudget.cs`, `NicheDiscoveryOriginProbe.cs`, `MarketCatalogSeeder.cs`,
`NicheDiscoveryRunQueue.cs` + `NicheDiscoveryRunBackgroundService.cs`; persistence under
`Persistence/Postgres/Entities/{DiscoveryRunData,NicheOpportunityData,NicheDiscoveryEvidenceData,MarketCatalogData}.cs`,
`Persistence/Postgres/Repositories/Ef*.cs`, one migration.

### 2.5 Api / Web

`Incoders.Hive.ApiService/Endpoints/Social/MapNicheDiscoveryEndpoints.cs` (§9);
`apps/web/lib/api/niche-discovery.ts`, `apps/web/lib/social/niche-discovery/*.ts`,
`apps/web/components/social/niche-discovery/*` (§10).

---

## 3. The type model — four layers made structural

```csharp
// ── 1. EVIDENCE — produced ONLY by an INicheDiscoverySource adapter, after normalization.
public sealed record VideoEvidence(
    string ExternalRef, string ChannelExternalRef, string Title, string? Description,
    DateTimeOffset? PublishedAtUtc,
    long? Views, long? Likes, long? Comments,     // null = not reported, 0 = reported zero. NEVER zero-filled.
    int? DurationSeconds, string? LanguageCode, string SourceKey);

public sealed record ChannelEvidence(
    string ExternalRef, string Handle, string? Country,   // provider-reported, opaque, never branched on
    long? SubscriberCount, long? TotalViews, int? VideoCount,
    string? LanguageCode, DateTimeOffset? CreatedAtUtc, string SourceKey);

public sealed record EvidenceSet(IReadOnlyList<ChannelEvidence> Channels, IReadOnlyList<VideoEvidence> Videos);

// ── 2. METRIC — private ctor, ONE factory. There is literally no other way to obtain one.
public sealed record Metric
{
    private Metric(MetricKey key, double? value, int sampleSize) { … }
    internal static Metric From(MetricKey key, double? value, int sampleSize) => new(key, value, sampleSize);
    public MetricKey Key { get; }   public double? Value { get; }   public int SampleSize { get; }
}
public static class DeterministicMetricCalculator
{   // the ONLY caller of Metric.From — asserted by a reflection guard test
    public static MetricSet Compute(EvidenceSet evidence, DiscoveryWindow window, DateTimeOffset nowUtc);
}

// ── 3. SIGNAL — interpretation. Built from Metrics and CLOSED-ENUM labels only (N4).
public enum SignalAvailability { Available, NotReported, NotSupportedYet }
public sealed record Signal(SignalKey Key, double? Normalized, SignalAvailability Availability, string? ReasonKey);
public static class SignalBuilder
{ public static SignalSet Build(MetricSet metrics, IReadOnlyList<FormatTrait> traits, TargetMarketEvaluation market); }

// ── 4. SCORE — computable only from Metrics and Signals. No Semantics type appears in this signature.
public static class NicheOpportunityScorer
{ public static IReadOnlyList<ScoredOpportunity> Score(
      IReadOnlyList<OpportunityInput> set, OpportunityScoreWeights weights, DateTimeOffset nowUtc); }
```

**The three guards that make it structural** (all RED first, WU-1/WU-8/WU-10):

1. `Metric`'s only factory takes an `EvidenceSet`. A reflection test asserts `Metric.From` has exactly one
   calling type (`DeterministicMetricCalculator`) across the Application assembly.
2. Every public property of every type in `…NicheDiscovery.Semantics` is `string`, `bool`, an `enum`, or a
   collection of those — **no numeric type anywhere**. The AI cannot emit a number because the contract has
   nowhere to put one.
3. No method on any type in `…NicheDiscovery.Scoring` or `…Analytics` has a parameter or return type declared
   in `…NicheDiscovery.Semantics`.

---

## 4. Market modelling — why no branch can be named after a country

`MarketCode` is a normalized `{language}-{REGION}` value object (`es-AR`, `pt-BR`, `en-US`). It carries **no
semantics beyond string identity and its language part**. The catalog is data:

```csharp
public readonly record struct MarketCode
{
    public string Value { get; }              // "es-AR"
    public string Language { get; }           // "es"  — the ONLY part any algorithm reads
    public static Result<MarketCode> TryParse(string raw);   // lowercase lang + uppercase region, else validation error
}
```

`TargetMarketEvaluator.Evaluate(MarketCode source, MarketCode target, EvidenceSet, MetricSet, SignalSet)` returns:

- **`TargetMarketDemand`** — median velocity of window videos whose `LanguageCode` (or channel language) matches
  `target.Language`, normalized within the run set.
- **`TargetMarketCompetition`** — distinct channels publishing the same `ContentPatternKind` in `target.Language`,
  weighted by their subscriber decile. *Locale partition, per the proposal's assumption* — YouTube audience
  geography is not reliably reported, so language+region of the **content** is the partition, not of the viewer.
  Recorded as an assumption in ADR-0013; changing it is a change to this one function.
- **`TargetMarketGap`** = `Demand × (1 − Competition)`, `NotReported` when either input is null.

**Why a country-named branch is impossible**: no function takes a market *identity*; they take a `MarketCode` and
compare `.Language` to evidence. Argentina is one seeded `MarketCatalogEntry` row with `DisplayNameKey =
"social.markets.es_ar"`. Adding `pt-BR` is a seeder row. The **country-name guard test** (WU-2) scans every file
under the four NicheDiscovery namespaces plus the web `niche-discovery` folders for country names/demonyms as
identifiers or string literals, whitelisting only `MarketCatalogSeeder.cs` and i18n resource files.

**The "second market needs no code change" criterion is asserted directly**: a test seeds a second market row into
an in-memory catalog, runs `NicheDiscoveryRunProcessor` end-to-end over the recorded evidence fixture with
`targetMarket = pt-BR`, asserts a non-empty ranked result **and** that the git-tracked source under
`Domain/Social/NicheDiscovery`, `Application/**/NicheDiscovery`, `Infrastructure/**/NicheDiscovery` is byte-identical
to what the `es-AR` path uses (no per-market file exists) — i.e. the assertion is "one code path, two rows".

---

## 5. The provider port, per-stage routing, and the quota budget

### 5.1 Port and three-outcome contract

```csharp
public interface INicheDiscoverySource
{
    string SourceKey { get; }                    // "youtube-data-api", "apify" — a PROVIDER key, never an actor/endpoint id
    int Order { get; }                           // preference; DI registration order never decides
    bool Supports(DiscoveryStage stage);
    Task<NicheDiscoverySourceResult> DiscoverAsync(NicheDiscoveryQuery query, DiscoveryBudget budget, CancellationToken ct);
}

public enum NicheSourceOutcome { Ran, Unconfigured, Failed }

public sealed record NicheDiscoverySourceResult(
    NicheSourceOutcome Outcome,
    IReadOnlyList<ChannelEvidence> Channels,     // empty unless Ran
    IReadOnlyList<VideoEvidence> Videos,
    BudgetSpend Spend,
    PartialReason? Partial,                      // BudgetExhausted | ProviderBudgetExhausted | null
    string? FailureReasonKey);                   // "social.niche_discovery.provider_quota_exhausted", …
```

`Ran` with zero evidence is a **success**. `Unconfigured` means no enabled origin route resolved — reported on its
own channel (`UnconfiguredSourceKeys` on the run), never as a failure, exactly as CD-13/D12 established.

### 5.2 Configuration resolution (N12) — no literal endpoint or actor id in code

New workflow key `niche-discovery` → analysis type `market-opportunity` → four steps, one per `DiscoveryStage`,
each `RequiresNetwork: true`, `SupportedNetworkKeys: ["youtube"]`, `AllowedProviderCategories:
[OfficialDataApi, DataExtraction]`. New enum member `IntegrationProviderCategory.OfficialDataApi = 5`
(**appended** — the enum is persisted as an int; renumbering would silently re-categorise every stored provider).

`NicheDiscoveryOriginConfiguration.ResolveOriginAsync(orgId, stage, networkKey, ct)` mirrors
`ApifyNicheSearchActorConfiguration.ResolveActorIdsAsync` exactly:

```
1. IWorkflowConfigurationResolver.ResolveStepAsync(org, "niche-discovery", "market-opportunity",
                                                   stepKey(stage), capabilityKey(stage), "youtube")
   → enabled candidates ordered by Priority → provider key (+ actor id when the origin is Apify)
2. otherwise the raw actor_mappings_json entry (Apify origin only) via ApifyActorMappingLookup
3. otherwise EMPTY → NicheSourceOutcome.Unconfigured — never a literal, never a throw
```

The YouTube base address is **not** a code literal either: it comes from `IOptions<NicheDiscoveryOriginOptions>`
bound to configuration and registered as a named `IHttpClientFactory` client; a null/blank base address degrades to
`Unconfigured`. The API key is an organization credential (`api_key`) resolved through the existing
`IIntegrationResolver` path — **no new secret store**. `NoActorLiteralGuardTests.GuardedRelativePaths` gains the
three new Infrastructure origin files.

**Checklist group (the trap, paid explicitly)**: `GetOrganizationIntegrationsHandler` gains a third group
`new("niche-discovery", "Niche Discovery", nicheDiscoveryChecklist)` with items *"Connect a YouTube Data API key"*
and *"Enable a discovery origin for channel search"*, each satisfied by `INicheDiscoveryOriginProbe`. Without this
group the stage renders **"not configurable yet"** even though the backend is fully wired.

### 5.3 YouTube Data API v3 quota budget

Costs (documented v3 pricing): `search.list` = **100 units**; `videos.list` / `channels.list` /
`playlistItems.list` = **1 unit**, up to **50 ids per call**. Default project quota **10,000 units/day**.

**Strategy — search sparingly, batch everything else.** One `search.list` per *(seed term × content format)* with
`type=video&order=viewCount&maxResults=50&publishedAfter=<window start>&relevanceLanguage=<target language>`,
harvesting video ids **and** channel ids from the same response. Then **one** `videos.list?part=snippet,statistics,
contentDetails&id=<≤50 ids>` per 50 videos, and **one** `channels.list?part=snippet,statistics&id=<≤50 ids>` per 50
distinct channels. Detail enrichment is therefore ~1–2% of the run's cost.

| Depth | `search.list` calls | Search units | Detail units (≈) | **Run ceiling** | AI completions |
|---|---|---|---|---|---|
| Quick | 3 | 300 | ≤ 10 | **400** | 5 |
| Standard | 8 | 800 | ≤ 25 | **1,000** | 15 |
| Deep | 20 | 2,000 | ≤ 60 | **2,500** | 40 |

`DiscoveryBudget(int ProviderUnits, int AiCompletions)` is created from the depth and threaded through
`IDiscoveryBudgetLedger` (run-scoped, in-memory, pure counter). The adapter decrements **before** each call and
stops when the remaining units cannot cover the next call. Exhaustion semantics per **N9**. A provider `403
quotaExceeded` maps to `FailureReasonKey = "social.niche_discovery.provider_quota_exhausted"`. Deep runs at 2,500
units cap the day at 4 deep runs per project — surfaced in the run response as `providerUnitsSpent` so the operator
can see it, not discover it.

### 5.4 Normalization boundary

`YouTubeEvidenceMapper` is the **only** type that sees `JsonElement`. Per-item and total: an item it cannot map is
**skipped**, never defaulted; zero-of-many mapped from a non-empty response → `Failed`; partial → `Ran` with the
skipped count logged. Absent statistics (e.g. `statistics.viewCount` hidden by the channel) → `null`, never `0`.

---

## 6. Deterministic analytics — formulas and null semantics

All pure, I/O-free, `nowUtc` as a parameter (mirrors `OpportunityScorer.Score`). `n` = videos in window.

| Metric | Formula | Null when | Effect of a null |
|---|---|---|---|
| `ChannelMedianViews` | median of non-null `Views` over that channel's window videos | zero non-null views | `OutlierRatio` and `OutlierDensity` become null for that channel |
| `OutlierRatio` (per video) | `Views / ChannelMedianViews` | `Views` null **or** median null/≤0 | video excluded from the density numerator *and* denominator |
| `OutlierDensity` (per channel) | `count(OutlierRatio ≥ 3.0) / count(non-null OutlierRatio)` | denominator 0 | `ViralPerformance` signal → `NotReported`, drops out of the score denominator |
| `VelocityProxy` (per video) | `Views / max(ageDays, 1)`, `ageDays = (nowUtc − PublishedAtUtc).TotalDays` | `Views` or `PublishedAtUtc` null | excluded from every velocity median |
| `GrowthProxy` (per cluster) | `median(VelocityProxy of newest ⅓ of window) / median(VelocityProxy of oldest ⅓)` | either third has no non-null velocity | `DemandMomentum` → `NotReported` |
| `CrossChannelRepetition` | distinct channel count in the cluster; below **K = 3** the value is `0` (**available**, not missing) | never null | `CrossChannelValidation` always available |
| `EvergreenShare` | `count(videos older than ½ window with OutlierRatio ≥ 1) / count(videos older than ½ window)` | denominator 0 | `EvergreenPotential` → `NotReported` |

**The rule, absolute**: `null` = the provider does not report it; `0` = it reported zero and **does** participate in
every median, ratio and count. Nothing is ever zero-filled — the exact bug ADR-0012 §3 removed. Ties break on
`ExternalRef` ordinal so ranking is a total order independent of input order.

---

## 7. Scoring

### 7.1 OpportunityScore

`score = Σ(wᵢ·sᵢ over AVAILABLE i) / Σ(wᵢ over AVAILABLE i)` — the **same** availability-aware mechanism as
`OpportunityScorer.Accumulate`/`NormalizeWithinSet`, re-declared as
`Scoring/AvailabilityWeightedAggregate.cs` (~25 lines) rather than imported, per the vocabulary guard (N2). A unit
test asserts numerical parity with a hand-computed renormalised expectation, so the duplication cannot drift silently.

| Signal | Weight | Source | Availability in this change |
|---|---|---|---|
| ViralPerformance | 0.20 | `OutlierDensity` | Available |
| CrossChannelValidation | 0.15 | `CrossChannelRepetition` | Always available |
| DemandMomentum | 0.15 | `GrowthProxy` | Available |
| TargetMarketGap | 0.15 | `TargetMarketGap` | Available |
| AutomationSuitability | 0.10 | `FormatTrait[]` (closed enum, N4) | `NotReported` if semantics degraded |
| EvergreenPotential | 0.10 | `EvergreenShare` | Available |
| CrossPlatformValidation | 0.10 | — | **`NotSupportedYet`** (N8) — permanently excluded in this change |
| MonetizationPotential | 0.05 | channel subscriber/view deciles in `target.Language` | Available |

With CrossPlatformValidation excluded the effective denominator is **0.90**, and the run response carries
`excludedSignals: [{ key: "cross_platform_validation", reason: "not_supported_yet",
i18nKey: "social.niche_discovery.signals.not_supported_yet" }]` plus `effectiveWeightDenominator`. The dashboard
renders that list next to the score so the operator sees *why* the denominator changed rather than inferring it.

`OpportunityScoreWeights` defaults live in `Scoring/OpportunityScoreWeights.cs`; the effective set is snapshotted
into `discovery_runs.weights_json` at run start (N7). Optional per-run override on the start request; the sum is
**not** normalised to 1 at input — renormalisation is what the aggregate already does.

### 7.2 ConfidenceScore — genuinely independent

Inputs are **sample-shape facts only**, never magnitudes: `SampleShape(ChannelCount, VideoCount,
DistinctChannelCount, WindowCoverageDays, SourceCount, DataAgeDays, SignalConsistency)`, where
`SignalConsistency = 1 − normalizedDispersion(per-channel OutlierRatio medians)`. Each term is min–max normalised
across the run and averaged with equal weight.

**Independence is asserted, not claimed**: a fixture test scales every `Views` value in the evidence by 10× and
asserts `OpportunityScore` changes while `ConfidenceScore` is bit-identical; a second test doubles the channel and
video counts with identical distributions and asserts the reverse. The 94/37 and 88/95 fixture pair from the
proposal's success criteria is one of these fixtures, and a frontend test asserts both are rendered side by side
and never averaged.

---

## 8. Semantic layer

`SemanticAnnotator` uses the existing `IAiCompletionGateway.CompleteJsonAsync<T>` with `CapabilityKey =
"ai-analysis"` (existing routing) and prompt version keys `niche-discovery.classify.v1`,
`niche-discovery.cluster.v1`, `niche-discovery.name-cluster.v1`, `niche-discovery.extract-pattern.v1`,
`niche-discovery.identify-topic.v1`.

```csharp
// EVERY member is a string, bool or enum. There is nowhere to put a number (guard §3.2).
public sealed record SemanticAnnotation(
    IReadOnlyList<ClusterProposal> Clusters,
    IReadOnlyList<string> TopicLabels,
    IReadOnlyList<string> FormatTraitLabels);       // parsed into the closed FormatTrait enum, unknown → dropped

public sealed record ClusterProposal(
    string ClusterLabel, string PatternLabel, IReadOnlyList<string> VideoExternalRefs);
```

**Taxonomy mapping**: `ClusterProposal` → `SubNicheRef(label)` under the run's `NicheId` (cross-referenced from the
existing flat catalog, never modified); `PatternLabel` → `ContentPatternKind` (closed enum, unknown →
`Unclassified`); `TopicLabels` → `TopicLabel[]` on the opportunity. The AI never assigns a `NicheId`; the operator
picks the niche (or the run runs unscoped) and the AI subdivides within it.

**Determinism and caching (N10)**: within a run, identical input payloads are memoised by SHA-256 of the canonical
JSON so a cluster is never re-named twice; across runs there is **no** cache — labels are persisted on the
opportunity so a stored run always displays what it produced. The type doc states plainly that two runs may cluster
differently and that the `DiscoveryRun` is the comparison unit.

**Failure (N11)**: gateway failure or unparsable payload → `LexicalClusterFallback` (tokenise title+description,
drop <4 chars and an ES+EN stopword list, group by shared token across ≥ K=3 **distinct channels** — the same idiom
as `CoOccurrenceSignal`), cluster name = top token, `ContentPatternKind.Unclassified`, `FormatTrait[]` empty ⇒
`AutomationSuitability` → `NotReported` and out of the denominator, `discovery_runs.semantic_degraded = true`,
status `PartiallyCompleted`. **The run still ranks.**

---

## 9. Persistence — one migration

Schema `social`, snake_case, `organization_id` FK → `platform.organizations` **cascade delete** on every table
(the tenant boundary), nullable metric columns, append-only. One migration
`Add_NicheDiscoveryRunsOpportunitiesAndEvidence` (+ `.Designer.cs` + updated `AppIdentityDbContextModelSnapshot.cs`)
so it reverts as a unit. The `migration-drift` gate (`dotnet ef migrations has-pending-model-changes`) must be green.

**`social.discovery_runs`** — the one table with UPDATEs (status lifecycle):
`id` uuid PK · `organization_id` uuid · `requested_by_user_id` uuid · `source_market_code`, `target_market_code`
varchar(16) · `network_key` varchar(32) · `content_format` varchar(16) · `window_days` int · `depth` smallint ·
`status` smallint · `weights_json` jsonb · `origin_source_keys`, `unconfigured_source_keys`, `failed_source_keys`
text[] · `provider_units_spent`, `ai_completions_spent` int · `semantic_degraded` bool ·
`failure_reason_key` text NULL · `started_at`, `completed_at` timestamptz NULL · `created_at` timestamptz.
Index `ix_discovery_runs_org_created (organization_id, created_at)`.

**`social.niche_opportunities`** — append-only read model:
`id` uuid PK · `run_id` uuid FK → `discovery_runs` cascade · `organization_id` uuid · `niche_id` uuid **NULL**
(value cross-reference to `Domain.Social.Niches`, deliberately **not** an FK — the catalogs are parallel, N5/settled
decision 2) · `sub_niche_label`, `content_pattern`, `topic_labels` (text[]) · `opportunity_score`,
`confidence_score` double NOT NULL · `effective_weight_denominator` double NOT NULL · `excluded_signals_json` jsonb ·
`median_views` bigint NULL · `outlier_ratio`, `outlier_density`, `growth_proxy` double NULL ·
`cross_channel_repetition` int NULL · `target_market_demand`, `target_market_competition`, `target_market_gap`,
`automation_suitability` double NULL · `channel_sample_size`, `video_sample_size` int · `rank` int ·
`observed_at`, `created_at` timestamptz.
Unique `ux_niche_opportunities_run_rank (run_id, rank)` — a double-write inside a run is a hard error, mirroring
`ux_discovered_candidates_key_observed`. Index `ix_niche_opportunities_org_created (organization_id, created_at)`
so the later purge is one ranged delete.

**`social.niche_discovery_evidence`** — append-only, navigable:
`id` uuid PK · `run_id` uuid FK cascade · `opportunity_id` uuid FK cascade · `organization_id` uuid ·
`evidence_kind` smallint (0 Channel / 1 Video) · `external_ref` varchar(2048) · `channel_handle` varchar(256) ·
`title` varchar(1000) · `thumbnail_url` varchar(2048) NULL · `published_at` timestamptz NULL ·
`views`, `likes`, `comments`, `subscriber_count`, `total_views` bigint NULL · `video_count`,
`duration_seconds` int NULL · `language_code` varchar(16) NULL · `source_key` varchar(32) ·
`observed_at`, `created_at`.
Unique `ux_niche_discovery_evidence_run_kind_ref (run_id, evidence_kind, external_ref)`.

**`social.market_catalog`** — `id` uuid PK · `market_code` varchar(16) · `display_name_key` varchar(128) ·
`primary_language` varchar(8) · `is_active` bool · `created_at`. Unique `ux_market_catalog_code (market_code)`.
Global (platform-level, no `organization_id`) exactly like the niche catalog, seeded by `MarketCatalogSeeder` with
deterministic GUIDs, upsert-on-missing-only — **one row: `es-AR`**.

Append-only is enforced at the port: `INicheOpportunityRepository` and `INicheDiscoveryEvidenceRepository` expose
`AppendAsync` + readers and **no** update or delete method.

---

## 10. API surface

| Method | Route | Contract |
|---|---|---|
| `POST` | `/api/social/niche-discovery/runs` | `StartDiscoveryRunRequest(SourceMarketCode, TargetMarketCode, NetworkKey, ContentFormat, WindowDays?, Depth?, NicheId?, SeedTerms[], Weights?)` → `202 { runId, status }` |
| `GET` | `/api/social/niche-discovery/runs/{runId}` | `{ status, window, depth, providerUnitsSpent, aiCompletionsSpent, semanticDegraded, unconfiguredSourceKeys, failedSourceKeys, failureReasonKey }` |
| `GET` | `/api/social/niche-discovery/runs/{runId}/opportunities` | ranked list + `excludedSignals` + `effectiveWeightDenominator` |
| `GET` | `/api/social/niche-discovery/opportunities/{id}` | detail + evidence (channels and videos, navigable URLs) |
| `GET` | `/api/social/niche-discovery/markets` | the seeded catalog — the market selector is **data-driven by construction** |

Execution is start-and-poll, matching the single-analyzer idiom the operator already knows
(`INicheDiscoveryRunQueue` + a hosted `NicheDiscoveryRunBackgroundService`; poll interval 2500 ms client-side).

Error codes `social.niche_discovery.{reason}` with i18n keys `social.niche_discovery.errors.{reason}`:
`market_unknown`, `market_inactive`, `window_out_of_range`, `depth_unknown`, `content_format_invalid`,
`network_unsupported`, `origin_unconfigured`, `provider_quota_exhausted`, `provider_unavailable`,
`run_not_found`, `run_already_running`, `opportunity_not_found`.

---

## 11. Frontend

| File | Kind |
|---|---|
| `apps/web/lib/api/niche-discovery.ts` | thin client over the shared `apiRequest` helper — no decision logic |
| `apps/web/lib/social/niche-discovery/ranking.ts` | pure sort comparators (opportunity, confidence, gap, competition, automation) |
| `apps/web/lib/social/niche-discovery/filters.ts` | pure predicate builders (market, format, trend, competition band) |
| `apps/web/lib/social/niche-discovery/availability.ts` | pure `null → "unavailable"` / `0 → "0"` formatting — **the zero-fill trap's last line of defence** |
| `apps/web/components/social/niche-discovery/viability-dashboard-client.tsx` | sortable/filterable table |
| `…/opportunity-detail.tsx`, `…/evidence-list.tsx`, `…/score-pair-badge.tsx`, `…/excluded-signals-note.tsx` | detail, navigable evidence, side-by-side scores, denominator explanation |

`score-pair-badge` renders `opportunity` and `confidence` as two distinct values with two distinct labels; there is
no code path that combines them, asserted by a component test.

---

## 12. The Content Generator seam — named, not built

```csharp
namespace Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

/// NAMED SEAM, NOT IMPLEMENTED IN THIS CHANGE (proposal "Content Generator seam").
/// The future activation path consumes opportunities through THIS contract and nothing else.
public interface INicheOpportunityActivationSource
{
    Task<IReadOnlyList<ActivatableContentPattern>> GetActivatablePatternsAsync(
        Guid organizationId, Guid opportunityId, CancellationToken ct = default);
}

public sealed record ActivatableContentPattern(
    ContentPatternKind Pattern,
    IReadOnlyList<string> TopicLabels,
    string TargetMarketCode,        // the local-adaptation hint — a code, never a country
    string SubNicheLabel,
    double OpportunityScore,
    double ConfidenceScore);
```

The interface ships with **no implementation and no registration**; a composition test asserts
`GetServices<INicheOpportunityActivationSource>()` is **empty** in this change, so "named, not built" is enforced
rather than trusted. NicheDiscovery references no `ContentGeneration` type and vice versa — asserted by a namespace
guard test in both directions.

---

## 13. What ADR-0013 must record

`docs/architecture/adr/0013-niche-discovery-multi-origin-provider-and-opportunity-evidence-model.md` (WU-13):
the vocabulary table verbatim; N1 (one multi-origin port, provider-named ports rejected); N2 (deliberate contract
duplication over cross-capability import); N3 (the three structural AI-cannot-produce-a-number guards); N5
(`DiscoveryRun` aggregate vs `NicheOpportunity` read model, **with the revisit trigger**); N6 (market as data,
country-named identifiers forbidden); N7 (weights snapshotted per run, superseding ADR-0012 D7 *for this
capability only*, with the reason); N8/N9 (three-valued availability; quota exhaustion semantics); N12 (new
top-level workflow key **plus** its checklist group, and the trap it avoids); the new `OfficialDataApi` provider
category appended rather than renumbered; the locale-not-geography competition partition as a stated assumption;
and the consequences: additive rollback, one reversible migration, config-removal disables discovery without a
code change.

---

## 14. Alternatives considered and rejected

| Area | Rejected | Why |
|---|---|---|
| Provider port | `IYouTubeChannelSearchResolver` (per-provider port, as `exploration.md` §3 sketched) | Bakes the vendor into Application; Apify would then need a second port and a second chain. Settled decision 1 requires config-swappable origins. |
| Provider port | One port, origin chosen **globally** per run | Channel search and video detail have different cost profiles and different provider strengths; per-stage routing is the whole point of a "discovery stage" configuration surface. |
| Taxonomy | Extend `Domain.Social.Niches.Niche` into the hierarchy root | Settled decision 2. It is a shipped platform display catalog with 5 rows and its own seeder; adding parenting would migrate live data for an unvalidated shape and couple two capabilities permanently. |
| Taxonomy | Fully independent taxonomy with no `NicheId` reference | Loses the ability to say "this opportunity is in a niche the platform already knows", which is exactly what the operator will ask next. |
| Opportunity | Domain aggregate with `Pursue()`/`Dismiss()` | Settled decision 4. No operator action exists in this change, so the lifecycle would be invented, not observed — the exact mistake ADR-0012 §7 avoided. Revisit trigger named in N5. |
| Scores | One blended "viability" number | The 94/37 vs 88/95 distinction is the product. Blending destroys it and is explicitly forbidden by the proposal's risk table. |
| Weights | Global configuration table | Weights that can change between two runs make two runs incomparable, which contradicts "the run is the unit of reproducibility". Snapshot per run instead (N7). |
| CrossPlatformValidation | Zero-fill it | Systematically penalises every opportunity by 0.10 and hides the reason. Renormalise + label `NotSupportedYet` (N8). |
| Semantics | Persist a cross-run semantic cache table | A 4th table and a staleness policy to make an LLM *look* deterministic. Honest non-determinism plus persisted labels costs nothing and lies about nothing (N10). |
| Config placement | Nest under the existing `single-analyzer` workflow key (as content-discovery did) | That reuse was justified by a shared downstream pipeline. Here there is no client and no analysis — hiding a channel-launch tool under Single Analyzer is a UI lie. Cost: one new checklist group (N12). |
| Quota | Rely on `search.list` for everything | 100 units/call would cap a project at 100 searches/day with no detail data. Search-then-batch is ~50× cheaper per enriched item. |

---

## 15. Threat matrix

`N/A` — no routing of shell commands, no subprocess, no VCS/PR automation, no executable-file classification, no
process integration. New external surface: one HTTPS origin reached through `IHttpClientFactory` with a
configuration-supplied base address and an organization-scoped API key resolved via the existing
`IIntegrationResolver`. The key is never logged, never written to a configuration file, and never returned by an
endpoint — the `secrets` gate covers it.

---

## 16. Testing strategy (Strict TDD — RED first, every row)

| Layer | What | How |
|---|---|---|
| Unit (pure) | `DeterministicMetricCalculator`: every formula in §6, and every null-propagation row | Table tests over hand-built `EvidenceSet`s with a fixed `nowUtc`; asserts `null` never becomes `0` |
| Unit (pure) | `AvailabilityWeightedAggregate`: renormalising denominator, degenerate set → 0.5, determinism | Parity test against a hand-computed expectation |
| Unit (pure) | `NicheOpportunityScorer`: the CrossPlatformValidation criterion — fixture with the signal `NotSupportedYet` ranks identically to the same fixture with the weight removed by hand | Direct from the proposal's success criteria |
| Unit (pure) | `ConfidenceScorer` independence: ×10 views ⇒ confidence bit-identical; ×2 sample ⇒ opportunity unchanged | Two mirrored fixtures |
| Unit (pure) | `TargetMarketEvaluator`: demand/competition/gap, null handling | Fixture with two languages |
| Unit (pure) | `MarketCode.TryParse` normalisation and rejection | Table test |
| Unit | `NicheDiscoverySourceChain`: `Unconfigured` vs `Failed` vs `Ran`-with-zero are three disjoint reports; per-stage selection; budget threading | Fake sources, mirrors `ContentIdeaChainTests` |
| Unit | `SemanticAnnotator`: gateway failure ⇒ lexical fallback, `semantic_degraded`, ranked result still produced | Fake `IAiCompletionGateway` returning failure |
| Unit | `YouTubeEvidenceMapper` against a **recorded fixture** (`Fixtures/youtube-niche-search.json`, capability-named) — unmappable item skipped, zero-of-many ⇒ `Failed`, partial ⇒ `Ran`, absent statistics ⇒ `null` | Mutated fixture copies. **No live calls.** |
| Unit | `YouTubeQuotaBudget`: depth → ceiling table §5.3; stops before an uncoverable call; quota 403 with zero results ⇒ `Failed`, with partial results ⇒ `Ran` + `PartialReason` | Counting fake client |
| Unit | `NicheDiscoveryOriginConfiguration`: enabled route wins, raw mapping second, empty ⇒ `Unconfigured`; blank base address ⇒ `Unconfigured` | Fake `IWorkflowConfigurationResolver`, the `ApifyNicheSearchResolverTests` pattern |
| Unit | Catalog: `niche-discovery`/`market-opportunity`/4 steps resolve; overview returns a `niche-discovery` checklist group | `WorkflowConfigurationCatalog.FindStep`, existing handler test host |
| **Guard** | **No country-named identifier or literal** in the NicheDiscovery namespaces or web folders (whitelist: `MarketCatalogSeeder.cs`, i18n resources) | Source scan, WU-2, RED first |
| **Guard** | **No literal actor/endpoint id** in the new origin files | Extend `NoActorLiteralGuardTests.GuardedRelativePaths` |
| **Guard** | **AI cannot produce a number**: (a) `Metric.From` has exactly one calling type; (b) no numeric member on any `…Semantics` type; (c) no `…Scoring`/`…Analytics` signature references a `…Semantics` type | Three reflection tests, RED before the semantic layer exists |
| **Guard** | NicheDiscovery ↔ ContentGeneration: no type reference in either direction; `GetServices<INicheOpportunityActivationSource>()` is empty | Namespace + composition tests |
| **Composition** | Exactly one implementation per NicheDiscovery port; `GetServices<INicheDiscoverySource>()` keys are distinct with distinct `Order` | Real container from `AddInfrastructure` — the count assertion is the load-bearing one |
| **Criterion** | **Adding a second target market needs no code change**: seed `pt-BR`, run the processor over the same fixture, assert a ranked result and that no per-market source file exists | §4 |
| Integration | Repository round-trip + migration; append-only ports expose no update; `ux_niche_opportunities_run_rank` rejects a double-write; cascade delete from the organization | Existing EF test-host pattern |
| Frontend | Dashboard sorts/filters by all seven axes; `availability.ts` renders `null` as *unavailable* and `0` as `0`; both scores rendered side by side, never combined; evidence links navigate to real channel/video URLs | Pure unit tests + component tests |

Commands: `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj -c Release`, then
`pwsh -File scripts/gates/Run-Gates.ps1`.

---

## 17. Migration / rollout

Fully additive. No shipped behaviour changes: `Domain.Social.Niches.Niche`, the Content Discovery chain, the
workflow pipeline and the single analyzer are untouched. One migration, revertible as a unit. Removing the origin
configuration disables discovery with **no code change** — the chain reports `Unconfigured` by contract. WU-1→WU-4
have no runtime effect until WU-5 lands, so a stall leaves usable contracts rather than a broken system.

---

## 18. Work-unit forecast — where the proposal looks wrong (flagged, not silently re-planned)

Having read the real code, four items in the proposal's 13-unit table do not survive contact:

1. **WU-7 (persistence, ~800) is materially under-estimated.** The sibling's single migration produced a
   `.Designer.cs` plus an `AppIdentityDbContextModelSnapshot.cs` diff running to hundreds of generated lines — for
   **two** tables. This change has **four**, plus four EF entity classes, four mapping blocks and three
   repositories. Recommendation: treat the pre-agreed split (schema+migration / repositories) as the **default**,
   not the fallback, and count generated migration artifacts separately from authored lines.
2. **WU-5 (~800, at budget) carries work that belongs in WU-3.** `DiscoveryBudget` and `IDiscoveryBudgetLedger` are
   Application contracts, not adapter details, and the adapter cannot be TDD'd without them. Move them into WU-3
   (+≈120 lines there, −≈120 in WU-5); WU-3 has headroom at ~600, WU-5 does not.
3. **WU-11 (~650) hides a second deliverable.** Run orchestration needs a queue, a hosted background service and a
   status projection (≈400 lines with tests) *before* endpoints and DI composition tests. Recommendation: split
   into **WU-11a** run orchestrator + queue + background service, and **WU-11b** endpoints + composition tests —
   making the plan **14 units**, none at the ceiling.
4. **WU-4 (~700) is optimistic if it also touches the settings UI.** Appending
   `IntegrationProviderCategory.OfficialDataApi`, the credential surface, the catalog entry, the third checklist
   group, `IntegrationResponseFactory` and the frontend integrations client is a wide, shallow change across ~10
   files; ~700 is plausible only if the frontend category rendering needs no new branch. Verify that first.

One ordering correction: the proposal calls WU-9 and WU-10 mutually independent, which holds — but **WU-9 depends
on WU-6**, since `TargetMarketDemand` consumes `VelocityProxy`. WU-9 must not be scheduled before WU-6.

Everything else in the forecast reads correct, including the contract-first WU-1→WU-3 ordering and the WU-5→WU-4,
WU-8→WU-6, WU-12→WU-11 dependencies.

## 19. Open questions

- [ ] Competition partition is **locale** (content language+region), per the proposal's stated assumption. If the
      operator means audience geography, `TargetMarketCompetition` changes — one function, but the evidence model
      would need YouTube's far less reliable regional data. Confirm before WU-9.
- [ ] Outlier threshold `3.0`, evergreen half-window and the `K = 3` repetition floor are assumed product values,
      configurable in code. Confirm before WU-6.
- [ ] A Google Cloud project with YouTube Data API v3 enabled and a per-organization API key is an **owner-side
      prerequisite** for verifying WU-5. Without it the origin correctly reports `Unconfigured` and WU-5 ships
      against the recorded fixture only — the same deviation ADR-0012's amendment recorded for the Apify actor.
