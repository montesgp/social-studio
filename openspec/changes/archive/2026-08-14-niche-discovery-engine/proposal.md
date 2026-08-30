# Proposal: Niche Discovery Engine

## Change ID
`niche-discovery-engine`

## Status
Proposal — builds on `exploration.md` (verified codebase map, 8 boundary risks, naming table) and on the
three settled owner decisions recorded in Engram `sdd/niche-discovery-engine/state`. Specs and design next.

## Vocabulary — read this first

Two capabilities under `Social` will carry the word *discovery*. They are not the same thing and must never
be merged, aliased, or cross-imported.

| Capability | Question it answers | Status |
|---|---|---|
| **Content Discovery** (`content-discovery-intelligence`, shipped 2026-08-10) | "What content should I generate FROM for this existing client?" | Live |
| **Niche Discovery** (this change) | "Which niche, sub-niche and content format is worth building a NEW channel around, in which market?" | Proposed |

Every doc comment, spec id, namespace and UI label produced by this change must disambiguate explicitly.

## Intent

**Business problem.** Deciding to launch a new channel is today an unaided judgement call. The operator
watches YouTube by hand, forms an intuition about "this format works", and cannot answer the two questions
that actually decide the bet: *how strong is the evidence* and *is the market I would launch into already
saturated*. There is no repeatable artifact, no audit trail, and no way to compare two candidate niches.
When the bet is wrong the cost is months of production, not a wasted prompt.

**Current-state gap.** The shipped Content Discovery capability finds *posts* for an *existing* client, on
TikTok only, one market, no market comparison and no confidence notion. `Domain.Social.Niches.Niche` is a
flat 5-entry display catalog, not a taxonomy. YouTube has no search-shaped adapter at all — only a
single-URL Apify scrape used by the single analyzer.

**Outcome.** The operator launches a `DiscoveryRun` (source market, target market, platform, content format,
analysis window), and receives a ranked list of `NicheOpportunity` records, each carrying an
**OpportunityScore** *and an independent* **ConfidenceScore**, plus navigable evidence — the actual channels
and videos that justify the recommendation. An opportunity of 94/37 reads visibly differently from 88/95.
Every number is deterministic and reproducible; the AI names and groups things, it never produces a metric.

**Conceptual layering enforced end to end**: **Evidence** (observed datum) → **Metric** (deterministic
calculation) → **Signal** (interpretation) → **Score** (quantitative aggregation). Each layer is a distinct
type; a Score may never be computed from anything but Metrics and Signals it can cite.

**Multi-market from day one**: Source Market → observed/proven pattern → Target Market → demand +
competition + gap → opportunity. Argentina is *seed data* and the initial default target, exactly as
`NicheCatalogSeeder` seeds niches as data. Adding a country must require zero code changes to core logic;
adding a platform provider must require zero changes to the NicheDiscovery domain.

## Settled decisions (do not re-open)

1. **Provider access is multi-origin and configurable per discovery stage.** One port, several
   interchangeable adapters, selected by configuration, following the existing `IWorkflowConfigurationResolver`
   / `actor_mappings_json` discipline and its `Ran` / `Unconfigured` / `Failed` three-outcome contract.
   **YouTube Data API v3 is the first and primary origin**; Apify is a secondary origin behind the same port.
2. **Taxonomy is a parallel hierarchy.** `NicheDiscovery` owns `Niche → SubNiche → ContentPattern →
   Topic/Entity` and cross-references the existing flat catalog by `NicheId`. The shipped aggregate is not
   modified or migrated.
3. **The semantic/AI layer ships in the first vertical slice** — classification, clustering, cluster naming,
   ContentPattern extraction — behind `IAiCompletionGateway`. "AI never invents metrics" is absolute.

## Scope

### In Scope

1. **`DiscoveryRun` aggregate** — source market, target market, platform, content format (long / shorts /
   both), analysis window, the configuration snapshot used, execution status and timestamps. A run is the
   unit of reproducibility: two runs with the same configuration over the same window must be comparable.
2. **Multi-origin candidate discovery** — a `INicheDiscoverySource` chain mirroring `ContentDiscoveryChain`
   1:1, with the same ordered-sources / partial-tolerance / `Ran|Unconfigured|Failed` contract. YouTube Data
   API v3 adapter ships; the Apify origin is wired-capable but not built here (see non-goals).
3. **Normalization at the Infrastructure boundary** — channels, videos and metrics map to a normalized social
   model *inside* `Infrastructure/Social/NicheDiscovery/**` before crossing into Application. No `ApifyVideo`,
   `YouTubeNiche`, `JsonElement`, `HttpClient` or EF type ever appears in Domain or Application.
4. **Deterministic analytics** (pure Application, I/O-free, `nowUtc` as a parameter): median views, outlier
   ratio, outlier density, growth / velocity proxy, cross-channel repetition.
5. **Semantic analysis** via `IAiCompletionGateway`: classification, clustering, cluster naming, content
   pattern detection, topic/entity identification. Output is labels and groupings only — never numbers.
6. **Market comparison**: source-market traction, target-market demand, target-market competition, and the
   resulting gap. `EvaluateTargetMarket()`, `TargetMarketCompetition`, `TargetMarketGap`.
7. **`OpportunityScore`** — configurable weights, starting point 0.20 ViralPerformance + 0.15
   CrossChannelValidation + 0.15 DemandMomentum + 0.15 TargetMarketGap + 0.10 AutomationSuitability + 0.10
   EvergreenPotential + 0.10 CrossPlatformValidation + 0.05 MonetizationPotential. Any signal unavailable in
   the MVP (notably CrossPlatformValidation, which has no cross-platform data) is **excluded and the
   remaining weights renormalized**, mirroring the availability-aware weighted denominator already proven in
   `OpportunityScorer`.
8. **`ConfidenceScore`** — fully independent of OpportunityScore. Derived from channel count, video count,
   sample size, timespan, source count, data freshness, channel diversity and signal consistency.
9. **Append-only evidence persistence** following the `social.discovered_candidates` /
   `social.account_baselines` precedent: schema `social`, snake_case, `organization_id` tenant boundary,
   nullable metric columns (**null = the network does not report it, 0 = it reported zero, never zero-filled**).
10. **Viability dashboard + opportunity detail** — sortable/filterable by opportunity, confidence, market,
    format, trend, competition and automation suitability; detail exposes scores, sample sizes, analysis
    period, data freshness, median views, outlier distribution, independent-channel repetition count,
    estimated target-market competition, automation suitability, creator independence, main content patterns,
    risks/limitations, and navigable evidence down to the real videos and channels.
11. **New provider category + API-key credential surface** for YouTube Data API v3, including its
    `WorkflowConfigurationCatalog` entry and a matching checklist group (without one it renders "not
    configurable yet").

### Out of Scope — explicit non-goals

| Deferred | Why now is wrong |
|---|---|
| Automatic video generation, thumbnails, auto-publishing | Downstream of a validated opportunity; a different capability entirely |
| Consolidated channel dashboard | Requires launched channels, which this change only recommends |
| Automatic experimentation / closed-loop learning cycle | Needs published outcomes to learn from; nothing to close the loop with yet |
| Google Trends / external demand signals | A second unverified provider integration on top of the first |
| TikTok + Instagram cross-platform validation | No cross-platform data exists; `CrossPlatformValidation` is therefore an *unavailable* signal, renormalized out — not zero-filled |
| Apify as an actual second origin adapter | The port and configuration routing must support it; building two adapters at once doubles unverified provider schema risk |
| Modifying or migrating `Domain.Social.Niches.Niche` | Settled decision 2 — parallel hierarchy, cross-referenced |
| **NicheOpportunity → ContentPattern → Topic/local adaptation → existing Content Generator** | See named seam below |

### The Content Generator seam — named, not built

This is the most important non-goal, because leaving it unnamed turns it into a breaking-contract exercise
later. The future integration will consume opportunities through **one** boundary contract, provisionally
`INicheOpportunityActivationSource` in `Application.Abstractions.Social.NicheDiscovery`, exposing a
`ContentPattern` + `Topic` projection and nothing else. This change **defines the projection shape in the
spec and builds nothing behind it**. NicheDiscovery must not reference any ContentGeneration type, and
ContentGeneration must not reference any NicheDiscovery type, in either direction, in this change.

## Capabilities

### New Capabilities
- `niche-discovery`: discovery runs, multi-origin candidate sourcing, the Evidence→Metric→Signal→Score
  pipeline, market comparison, OpportunityScore + independent ConfidenceScore, append-only evidence
  persistence, and the viability dashboard.

### Modified Capabilities (additive deltas)
- `integrations`: new **provider category** (official-API providers with API-key credentials, no precedent
  today) plus its `WorkflowConfigurationCatalog` entry, checklist group, and per-stage origin routing.

## Approach

Mirror the shipped Content Discovery capability's shape exactly rather than inventing a second style.

| Piece | Layer | Namespace / path |
|---|---|---|
| `DiscoveryRun`, `NicheOpportunity`, `Evidence`/`Metric`/`Signal`/`Score` value objects, taxonomy hierarchy, `NicheDiscoveryErrors` | Domain | `Domain.Social.NicheDiscovery` |
| `IDiscoveryRunRepository`, `INicheOpportunityRepository`, `INicheDiscoveryEvidenceRepository`, `INicheDiscoverySource`, market catalog port | Application (Abstractions) | `Application.Abstractions.Social.NicheDiscovery` |
| Source chain, deterministic analytics, scorers, confidence, market comparison, semantic orchestration | Application (Features) | `Application.Features.Social.NicheDiscovery.{DiscoveryRuns,Analytics,Scoring,MarketComparison,Semantics}` |
| YouTube Data API v3 adapter, normalization, EF entities/configs/migration, repositories, DI | Infrastructure | `Infrastructure.Social.NicheDiscovery` |
| Minimal-API mapping | Api | `ApiService/Endpoints/Social/MapNicheDiscoveryEndpoints.cs` |
| Dashboard + detail | Web | `apps/web/lib/api/niche-discovery.ts`, `apps/web/lib/social/niche-discovery/*.ts` (pure, unit-tested), `apps/web/components/social/niche-discovery/` |

Error codes `social.niche_discovery.{reason_snake}` with matching i18n keys `social.niche_discovery.errors.{reason}`.

**Forbidden names** (requirement, guard-tested): `YouTubeNiche`, `IsViableInArgentina()`,
`ArgentinaCompetitionScore`, or any enum member, constant, method or column named after a country.

### Reused vs new — concretely

| Reused unchanged | New |
|---|---|
| `IAiCompletionGateway.CompleteJsonAsync<T>` (CapabilityKey + PromptVersionKey routing) | Semantic capability keys + prompt versions for classify/cluster/name/pattern |
| `IWorkflowConfigurationResolver` + `actor_mappings_json` + `ApifyActorMappingLookup` routing discipline | Per-stage **origin** selection generalized beyond Apify actors; API-key credential type |
| `SourceOutcome.Ran|Unconfigured|Failed` contract and chain-tolerates-failure semantics | `INicheDiscoverySource` chain over channel/video search |
| `OpportunityScorer` shape: pure, I/O-free, `nowUtc` parameter, availability-aware denominator | `NicheOpportunityScorer` + `ConfidenceScorer` (independent) |
| Nullable-metrics-never-zero-filled persistence pattern; `social` schema, tenant FK, append-only | `discovery_runs`, `niche_opportunities`, `niche_discovery_evidence` tables |
| `NicheCatalogSeeder` seed-as-data pattern; `Domain.Social.Niches.Niche` read-only by `NicheId` | Market catalog seeder (Argentina as a seed row); NicheDiscovery taxonomy hierarchy |
| Frontend thin-client + pure-decision-logic split | `niche-discovery` client, dashboard, detail |

### Multi-origin provider configuration

One `INicheDiscoverySource` port; origin selection is **exclusively configuration-driven per stage** — an
enabled workflow-step route first, raw mapping second, empty → `Unconfigured` third. Never a literal actor
id, endpoint or API host in Application or Infrastructure code; the repo-wide guard test stays green. The
new API-key credential surface is a **new provider category**, since `.github/project-ai/providers/data/`
has only Apify and data365 today; it needs a catalog entry *and* a checklist group, or Settings will render
"not configurable yet" while the wiring is in fact complete.

## ADR — required

`openspec/config.yaml` sets `require_adr_for_architecture_changes: true`. This change introduces a new
Domain bounded area, new Application ports, a new persistence model, a new provider category and a new
credential type — comfortably above the bar that produced ADR-0012 for the sibling change.

**ADR file**: `docs/architecture/adr/0013-niche-discovery-multi-origin-provider-and-opportunity-evidence-model.md`
(`0013` is the next free number; note `0005` and `0006` are each duplicated in that folder.)

## PRD Traceability

`openspec/config.yaml` sets `require_prd_traceability: true`.

| PRD anchor | This change | Honesty note |
|---|---|---|
| §1 Product Summary — "discover high-performing content in a niche" | Partial ancestor of this capability | The PRD sentence is about content for an existing client, not market selection for a new channel |
| §2 Problem — "manual, inconsistent and difficult to scale" | Same class of pain, applied to the channel-launch decision | — |
| §3 Goals — "Automate content research" | Extends research upstream of client work | — |
| §6 MVP Success Path | **Not covered** | The success path starts at "select an organization/client"; niche discovery happens *before* a client exists |

**Traceability gap, flagged for the spec phase**: `docs/PRD.md` has no section describing channel-launch
opportunity discovery. A PRD addition (a new numbered section, in the style of §11 Profile Analysis and §12
Content Bank Planner) is required as part of this change to satisfy `require_prd_traceability` honestly
rather than by stretching §1.

## Delivery forecast — sequential commits to `dev`

Delivery is direct commits to `dev` (no PRs, no chained PRs). Budget: **800 changed lines per work unit**.
Estimates are `additions + deletions`, Strict TDD included (tests ≈ 1:1).

| # | Work unit | Est. lines | Over budget? |
|---|---|---|---|
| WU-1 | Domain: `DiscoveryRun`, `NicheOpportunity`, Evidence/Metric/Signal/Score value objects, `NicheDiscoveryErrors` | ~700 | No |
| WU-2 | Domain: NicheDiscovery taxonomy hierarchy + `NicheId` cross-reference; market catalog + Argentina seed row | ~500 | No |
| WU-3 | Application ports + multi-origin source-chain contract (`Ran|Unconfigured|Failed`) | ~600 | No |
| WU-4 | Integrations: new provider category, API-key credential surface, catalog entry + checklist group | ~700 | No |
| WU-5 | Infrastructure: YouTube Data API v3 adapter + normalization boundary | ~800 | At budget |
| WU-6 | Deterministic analytics (median, outlier ratio/density, growth proxy, cross-channel repetition) | ~700 | No |
| WU-7 | Persistence: 3 tables, EF configs, one migration, repositories | ~800 | At budget |
| WU-8 | `NicheOpportunityScorer` (renormalizing weights) + independent `ConfidenceScorer` | ~700 | No |
| WU-9 | Market comparison: source traction vs target demand / competition / gap | ~600 | No |
| WU-10 | Semantic layer via `IAiCompletionGateway` (classify, cluster, name, pattern, topic) | ~800 | At budget |
| WU-11 | Run orchestration + `MapNicheDiscoveryEndpoints` + DI composition tests | ~650 | No |
| WU-12 | Frontend: viability dashboard + opportunity detail with navigable evidence | ~800 | At budget |
| WU-13 | ADR 0013 + PRD section + spec/doc sync | ~350 | No |
| | **Total** | **~8,700** | 13 commits, none exceeding 800 |

`800-line budget risk: Medium`. No single unit exceeds the budget, but four sit at the ceiling (WU-5, WU-7,
WU-10, WU-12) and will need splitting if the estimate proves low. Suggested split points, pre-agreed:
WU-5 → transport+auth / mapping+normalization; WU-7 → schema+migration / repositories; WU-10 →
classification+clustering / naming+pattern extraction; WU-12 → dashboard list / detail view.

**Ordering constraint**: WU-1→WU-3 are contract-only and unblock everything. WU-5 depends on WU-4. WU-8
depends on WU-6. WU-12 depends on WU-11. WU-9 and WU-10 are independent of each other and can be reordered.

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| **YouTube Data API v3 quota** — 10k units/day default; `search.list` costs 100 units per call | High | Quota budgeting is a design-phase deliverable: search sparingly, then batch `videos.list`/`channels.list` (1 unit, up to 50 ids). Quota exhaustion must surface as `Failed`, never as "no opportunities found" |
| Scope is ~8.7k lines — the largest change in the repo, ~2.5× the sibling | High (accepted) | 13 bounded commits with named split points; contract-first ordering so a stall leaves usable contracts |
| Two "niche" concepts under `Social` confuse future contributors | High | Vocabulary table above is copied into the ADR and the spec header; namespaces are `Niches` vs `NicheDiscovery`, never abbreviated |
| Semantic layer drifts into producing numbers | Medium | Guard test: no scorer or metric type may take an AI response as input; the AI contract returns labels/groupings only |
| Hardcoded-market leakage under delivery pressure | Medium | Naming guard test forbidding country-named identifiers, added in WU-2 |
| DI override trap (a second registration silently wins) | Medium | Composition test in WU-11 asserting exactly one implementation per NicheDiscovery port |
| New credential type widens the secret surface | Medium | Reuse the existing per-organization credential storage path; no new secret store, no key in configuration files |
| Confidence read as a second opinion on opportunity | Medium | Spec requires both to be displayed together, never averaged or combined into one number |
| No cross-platform data makes `CrossPlatformValidation` permanently dead weight | Low | Renormalized out and *labelled unavailable in the UI*, so the operator sees why the denominator changed |

## Rollback Plan

- **Fully additive.** No shipped behavior is modified: `Domain.Social.Niches.Niche`, the Content Discovery
  chain, the workflow pipeline and the single analyzer are untouched. Reverting means removing the
  NicheDiscovery namespaces and unmapping `MapNicheDiscoveryEndpoints`; nothing else regresses.
- The three tables are additive and read by nothing outside NicheDiscovery; dropping them loses accumulated
  runs and evidence only. The migration is the one non-trivially reversible artifact and must be a single
  migration so it reverts as one unit.
- The YouTube provider category is configuration: removing the origin mapping disables discovery and the
  chain reports `Unconfigured` by contract, without a code change.
- Per-work-unit rollback: WU-1→WU-4 are contracts and config with no runtime effect until WU-5 lands, so
  the first four commits are individually safe to leave in place even if the change stalls.

## Dependencies

- A Google Cloud project with YouTube Data API v3 enabled and an API key available per organization — an
  **external, owner-side prerequisite** that must exist before WU-5 can be verified.
- Existing `IAiCompletionGateway`, `IWorkflowConfigurationResolver`, `INicheRepository` — reused read-only.
- One new EF migration (three tables).
- `migration-drift` gate (`dotnet ef migrations has-pending-model-changes`) must stay green after WU-7.

## Success Criteria

- [ ] An operator creates a `DiscoveryRun` with source market, target market, platform, format and window,
      and the run records its configuration snapshot, status and timestamps.
- [ ] The run sources candidates through YouTube Data API v3 selected **by configuration**; removing the
      configuration yields `Unconfigured`, distinguishable from a run that legitimately found nothing.
- [ ] No provider DTO, `JsonElement`, `HttpClient` or EF type exists in Domain or Application; architecture
      gate green.
- [ ] Median views, outlier ratio, outlier density, growth proxy and cross-channel repetition are computed
      by pure, I/O-free code taking `nowUtc` as a parameter, and are reproducible across runs.
- [ ] The semantic layer produces classifications, clusters, cluster names and content patterns, and
      produces **no numeric field** consumed by any scorer.
- [ ] `OpportunityScore` excludes unavailable signals and renormalizes the remaining weights; a fixture with
      `CrossPlatformValidation` unavailable yields the same ranking as the same fixture with that weight
      removed by hand.
- [ ] `ConfidenceScore` varies independently of `OpportunityScore`; a fixture pair produces 94/37 and 88/95
      and both are displayed side by side.
- [ ] Every opportunity links to the concrete channels and videos backing it; evidence rows are append-only.
- [ ] A metric a network does not report is stored and displayed as **unavailable**, never `0`.
- [ ] The dashboard sorts and filters by opportunity, confidence, market, format, trend, competition and
      automation suitability; the detail view exposes all fields listed in scope item 10.
- [ ] Adding a second target market requires only a catalog row — no code change (asserted by a test that
      runs the pipeline against a second seeded market).
- [ ] No identifier anywhere is named after a country (guard test).
- [ ] Exactly one implementation resolves per NicheDiscovery port (composition test).
- [ ] ADR `0013` exists; `docs/PRD.md` gains a niche-discovery section; specs updated.
- [ ] All MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`); Strict TDD followed.

## Proposal question round (non-blocking — auto mode)

Three forks were settled by the owner before this phase. These remain genuinely open and are **assumed** as
stated below; correct any of them before spec/design starts.

1. **Analysis window default** — assumed a configurable window with a 90-day default, since outlier density
   and growth proxies need enough history to be meaningful but stale data misleads. Is the operator's mental
   model shorter (30 days, "what is hot now") or longer (12 months, "what is evergreen")?
2. **Run cost ceiling** — assumed a per-run cap on API calls and AI completions, configurable, because a
   niche sweep is unbounded by nature and YouTube quota is 10k units/day. Should the operator see and choose
   the depth of a run (e.g. "quick / standard / deep"), or is depth a fixed product decision?
3. **What counts as "competition" in the target market** — assumed: the count and strength of channels
   already publishing the same content pattern *in the target market's language and locale*. Is locale the
   right partition, or is it geography of audience, which YouTube reports far less reliably?
4. **Automation suitability** — assumed a deterministic signal derived from observable format traits
   (faceless, voiceover-only, static-visual, no on-camera presenter) inferred by the semantic layer as
   *labels* and turned into a number by a pure scorer. Is that the operator's definition, or does it also
   include production cost and asset availability?
5. **Opportunity lifecycle** — assumed opportunities are immutable observations of a run (append-only, like
   `discovered_candidates`), so a later run produces new rows rather than updating old ones. Does the
   operator instead expect a single evolving "opportunity" they can track over time, mark as pursued, or
   dismiss? This one materially changes the persistence model and should be answered before WU-1.
