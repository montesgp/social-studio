# Exploration — Content Discovery / Content Intelligence

**Change**: `content-discovery-intelligence`
**Phase**: sdd-explore
**Date**: 2026-08-09
**Artifact store**: openspec
**Status**: complete — NOT ready for proposal (open product questions below)

---

## 1. Intent

Hive Social should stop generating content from a bare prompt or a single isolated video. Instead it
should **find content and angles that are already showing performance signals**, score them, select
the strongest, run the existing deep analysis only on those winners, and feed generation from the
result.

Source types (long-term): trending content in the niche; historically proven content in the niche;
the client's configured reference accounts; the client's own historical winners; later comments,
FAQs and external trends.

Cross-platform evidence is a first-class goal: the same topic, hook or angle appearing across
accounts and networks is stronger evidence than three independent hits.

---

## 2. Current state — verified

`IContentDiscoveryService` is the existing discovery port. It has exactly one implementation
(`ReferenceAccountContentDiscovery`, Instagram-only, profile-based) and is already wired end to end
through `InMemoryContentWorkflowService`. Discovery results are never persisted.

### Reusable — do NOT rebuild

| Asset | Location | Why it matters |
| --- | --- | --- |
| Discovery port | `Application/Abstractions/Social/IContentDiscoveryService.cs` | `DiscoveredContent` + `ContentDiscoveryRequest` already exist. Contract: return empty, never throw. |
| Referents source | `Application/Features/Social/ContentGeneration/Ideas/ReferenceAccountContentDiscovery.cs` | Working Instagram implementation over `IClientBrandSnapshotLoader` + `IProfileScrapeResolver`; excludes paid-partnership and degraded posts. |
| End-to-end wiring | `Infrastructure/Social/Workflow/InMemoryContentWorkflowService.cs:127` | Resolves the port via `IServiceScopeFactory` (singleton service, scoped discovery), `DefaultResultCount = 72`. Frontend mock flag is already off (`workflow-client.tsx:157`). |
| Multi-source chain idiom | `Application/Features/Social/ContentGeneration/Ideas/ContentIdeaChain.cs` | Asks each source, in preference order, only for what is still missing; dedups by normalized title; tolerates a failing source; reports `Shortfall` + `FailedSourceKeys`. |
| Capability routing | `Application/Features/Integrations/WorkflowConfigurations/WorkflowConfigurationCatalog.cs` | The `"generator"` workflow already routes steps by provider category + actor + network via `ActorVariantKey`. Existing variants: `"profile"`, `"social-extraction"`. |
| Per-network adapters | `Infrastructure/Social/Providers/Apify/{Instagram,TikTok,YouTube,Default}ApifyScrapeAdapter.cs` | URL extraction per network behind internal `ISocialPlatformScrapeAdapter`. |
| Pure metric calculator | `Application/Features/Social/ProfileAnalyzer/Analyze/ProfileMetricCalculator.cs` | Already computes median views, engagement rate, content-type mix, cadence — the account baseline needed for relative performance, and the purity style the scorer must copy. |
| Deep analysis rails | `Application/Features/Social/SingleAnalyzer/Analyze/` + `CreateContentGenerationBatchCommand` | `StartAsync` / `RunQueuedAsync` / `GetAsync` are async, persisted and pollable; batch generation anchors to an `AnalysisId`. Discovery → TOP N → analysis → batch fits the existing rails. |

### Gaps — verified

1. **No niche/keyword/hashtag search capability exists anywhere.** Everything scrapeable today is
   single-URL or profile-based, and profile scraping is Instagram-only. Trending and Proven — the
   majority of the intended source mix — are not buildable on current capabilities.
2. **`DiscoveredContent` lacks `PublishedAtUtc`, shares/saves, and any author baseline.** Trending
   vs Proven and relative performance are not computable under the current contract. A breaking
   contract change is required, and it breaks `ReferenceAccountContentDiscovery` and its tests in
   the same move.
3. **Zero discovery persistence.** Workflow sessions live in a `ConcurrentDictionary` (lost on
   restart); there is no EF table for discovered candidates. This blocks both the accumulated-niche-
   intelligence goal and any real velocity measurement.
4. **YouTube is absent from the network catalog.** `Infrastructure/Social/SocialNetworkCatalogSeeder.cs`
   seeds only `instagram`, `facebook`, `tiktok`, even though the extractor and
   `ContentWorkflowCatalog.NetworkAliases` both accept `youtube`.
5. **No embeddings and no clustering component exist** anywhere in the repository.
6. **DI trap.** `Infrastructure/DependencyInjection/DependencyInjection.cs:220` registers
   `IContentDiscoveryService` as a single implementation and the workflow service calls
   `GetRequiredService`. A second registration would silently win and erase the first. The chain must
   be introduced in the same change as the second source.
7. **`SearchCriterion` never routes discovery.** It is normalized and validated
   (`InMemoryContentWorkflowService.cs:57-60`), stored on the session and echoed into result payloads
   (lines 252, 511, 526, 550), but `DiscoverResultsAsync` builds
   `ContentDiscoveryRequest(organizationId, clientId, networks, DefaultResultCount)` at line 130 —
   the criterion is not passed. It is a display value, not a router.
8. **Pre-existing defect: `"own"` criterion fails validation.** The frontend offers
   `{ value: "own", label: "Own best performers" }` (`workflow-client.tsx:110`) but
   `ContentWorkflowCatalog.SearchCriterionAliases` has no `"own"` entry, so selecting it returns
   `SearchCriterionInvalid`. Adjacent to this change; needs an explicit in-scope / out-of-scope call.

---

## 3. Approaches and tradeoffs

### 3.1 Discovery chain shape

Mirror `ContentIdeaChain` with a `ContentDiscoveryChain` composed of an ordered
`IContentDiscoverySource[]`. Proven idiom in this codebase, fixes the DI trap in the same move, and
dedup plus failure tolerance are already solved there.

No material downside. **Recommended.**

### 3.2 Source selection: weighted mix vs criterion routing

| Option | Pros | Cons |
| --- | --- | --- |
| Literal 35/35/20/10 weighted allocator | Matches the stated hypothesis exactly | Dead configuration while only one source is live; contradicts the existing single-select UI shape |
| Route by the existing `SearchCriterion` | Uses a field that already exists end to end; no new config surface | Does not express blending |

**Recommended**: route by `SearchCriterion` now; defer weighted blending until at least three sources
are live. A configurable mix over a single source is a no-op with maintenance cost.

### 3.3 Scorer placement

A pure, I/O-free scoring service in Application, modelled on `ProfileMetricCalculator`. Fully unit
testable, no infrastructure dependency, no provider leakage. Signals it can compute from a single
snapshot: absolute engagement, engagement rate, age, views/day. Signals it cannot: true growth
velocity (needs a second observation) and relative performance (needs an author baseline).

### 3.4 Persistence model

| Option | Assessment |
| --- | --- |
| 3 tables now (candidates + account baselines + patterns) | Commits to an unvalidated clustering schema before any clustering exists |
| 2 tables now (candidates + account baselines) | Unblocks Trending-vs-Proven and baseline caching; defers the schema we cannot yet justify |

**Recommended**: two tables now.

### 3.5 Normalization across networks with unequal metrics

Networks do not expose the same fields: TikTok exposes shares, Instagram does not expose shares the
same way, YouTube exposes neither shares nor saves. The normalized candidate must distinguish
"metric is zero" from "network does not report this metric", or the scorer will systematically
penalise whole networks. Nullable metrics with explicit availability, not zero-filling.

### 3.6 Clustering placement

Cross-platform clustering **before** deep analysis is fragile without embeddings — only titles and
captions are available at that point. Clustering **after** deep analysis operates over extracted
topics and hooks: fewer items, far richer signal.

**Recommended**: land cheap lexical co-occurrence pre-analysis as a *scoring signal only* ("same
pattern seen in K creators"); defer the clustering component itself.

### 3.7 Relative performance economics

Computing an author baseline costs one extra profile scrape per author — unviable across ~100 niche
candidates. Compute it only for already-known accounts (referents, own accounts) and for the TOP N
after cheap filtering, caching baselines in the persistence layer so the cost amortizes.

---

## 4. Open questions for the proposal phase

These are genuine product decisions, not implementation details.

1. **Scope split vs review budget.** The cached delivery strategy is `single-pr` with a 1500-line
   budget. The full capability plus strict-TDD tests is unlikely to fit, especially if
   `InMemoryContentWorkflowService` must become persistence-backed in the same change.
2. **`SearchCriterion` as the router** — confirm this replaces the weighted mix for now.
3. **Baseline computation scope** — known accounts only, TOP N only, or both.
4. **Definition of "full discovery capability"** — does referents + one new niche source satisfy it,
   or must more networks land in this change?
5. **The `"own"` criterion defect** — fix inside this change or split as a separate bugfix.
6. **Which network first for niche search** — TikTok is the lowest-risk starting point (see §5).

---

## 5. Provider risk

Apify hashtag/search actors of the `clockworks/tiktok-hashtag-scraper` class exist and accept
hashtags plus a result limit. Their exact response schema was verified by documentation search only,
not by a live call. The existing TikTok URL-extraction adapter already parses shares and publish
date, which suggests the same vendor's search actor returns a comparable schema — but the design
phase should budget a **spike to confirm the real response fields before adapter code is committed**.

Routing-wise, a search-shaped capability (keyword input) differs from the existing URL- and
profile-shaped ones only by a new `ActorVariantKey` (e.g. `"niche-search"`) plus an adapter. The
`(networkKey, variantKey)` → actor-id mapping used by `ApifyProfileScrapeResolver` and
`ApifySocialContentExtractor` is already generic enough to carry it with no framework change.

---

## 6. Recommended shipping shape

`ContentDiscoveryChain` + pure opportunity scorer + `discovered_candidates` and `account_baselines`
persistence + the existing referents source adapted to the new contract + one new niche-search source
(TikTok) + TOP-N fan-out into the existing `ISingleContentAnalyzer`.

Deferred to follow-ups: the clustering component and pattern table, Instagram and YouTube niche
search, competitor and own-winners sources, weighted strategy mix.

---

## 7. Note on fan-out

`ISingleContentAnalyzer` accepts one URL per call. "Run deep analysis on the TOP N winners" therefore
requires new fan-out orchestration — there is no existing batch analysis API. The batch machinery
that does exist (`CreateContentGenerationBatchCommand`) is for *generation*, anchored to an
already-completed `AnalysisId`.
