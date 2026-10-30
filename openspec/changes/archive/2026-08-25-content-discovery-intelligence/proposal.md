# Proposal: Content Discovery Intelligence

## Change ID
`content-discovery-intelligence`

## Status
Proposal — builds directly on `exploration.md` (verified current state, 8 gaps, tradeoffs). Specs and design: next.

## Intent

Today an operator generates content from a bare prompt or one URL they happened to find themselves. The
research step — "what is actually working in this niche right now?" — is manual, unrepeatable, and
invisible to the product. `PRD.md` §1/§3 promise exactly that step ("discover high-performing content in
a niche", "automate content research"), and §6 step 2–3 name the source strategies. Only one of them
exists (reference accounts, Instagram-only) and it never persists what it found.

**Outcome**: the operator picks a search criterion, the system goes out and finds real candidates,
**scores** them on evidence it can show, runs the existing deep analysis only on the winners, and feeds
generation from that. The operator judges a ranked shortlist with numbers attached instead of a flat list
or a blank prompt.

**Why discover → score → analyse beats the alternatives**

| Path | Cost | Evidence behind the output |
|---|---|---|
| Bare prompt | AI tokens only | None — the model invents the angle |
| Single URL | 1 scrape + 1 analysis | One post the human already found |
| **This change** | N cheap scrapes + M deep analyses (M ≪ N) | Ranked, measured, cross-account |

Deep analysis is the expensive stage. Cheap discovery + a pure scorer in front of it is what makes
"analyse the winners" affordable at all.

## Scope

### In Scope

1. **`ContentDiscoveryChain`** (Application) mirroring `ContentIdeaChain`: ordered
   `IContentDiscoverySource[]`, asks each source only for what is still missing, dedups, tolerates a
   failing source, reports shortfall + failed source keys.
2. **`SearchCriterion` becomes a router.** It is already normalized and validated
   (`InMemoryContentWorkflowService.cs:57-60`) and already echoed into result payloads (lines 252, 511,
   526, 550). What it does **not** do is route: line 130 builds `ContentDiscoveryRequest` without it. The
   criterion is added to the request and selects which sources the chain consults.
3. **Three sources**:
   - `references` — existing `ReferenceAccountContentDiscovery`, adapted to the new contract.
   - `own` — client's own historical winners. Near-free: same shape as the referents source, swapping
     `IClientBrandSnapshotLoader.ReferentAccounts` for `IClientOwnAccountRepository` over the same
     `IProfileScrapeResolver` (`ReferenceAccountContentDiscovery.cs:57-83`). Instagram-only, like its twin.
   - `niche` — **new** TikTok hashtag/keyword search source (new `ActorVariantKey` `"niche-search"` +
     Apify adapter). TikTok first: its URL adapter already parses shares and publish date, the lowest
     schema risk.
4. **The `"own"` criterion defect is fixed.** The frontend offers `{ value: "own" }`
   (`workflow-client.tsx:110`) but `ContentWorkflowCatalog.SearchCriterionAliases` (lines 5-18) has no
   `"own"` entry, so the option returns `SearchCriterionInvalid`. The alias lands **and** the source
   behind it lands, because the source costs a near-clone of code that already works.
5. **Breaking contract change to `DiscoveredContent`**: add `PublishedAtUtc`, shares, saves, and an
   author-baseline reference. Metrics become **nullable with explicit availability, never zero-filled** —
   TikTok reports shares, Instagram does not report them the same way, YouTube reports neither shares nor
   saves; zero-filling would systematically penalise whole networks in the score.
6. **Pure opportunity scorer** (Application, I/O-free, modelled on `ProfileMetricCalculator`): absolute
   engagement, engagement rate, age, views/day, relative performance vs author baseline when available,
   and a cheap lexical co-occurrence signal ("same pattern seen in K creators").
7. **Two persistence tables**: `discovered_candidates` (candidate + metrics + `observed_at`) and
   `account_baselines` (cached per-author baseline). Baselines are computed for accounts already scraped
   for free (referents, own accounts) and for the TOP N after cheap filtering — one extra scrape each,
   amortized by the cache. **Never per candidate.**
8. **TOP-N fan-out** into the existing `ISingleContentAnalyzer`, which accepts **one URL per call** — the
   fan-out orchestration is new. The existing batch machinery (`CreateContentGenerationBatchCommand`) is
   for *generation* off an already-completed `AnalysisId` and is not reused here.
9. **DI replacement in the same move** (see Approach).

### Out of Scope — explicit non-goals

The operator's long-term vision is deliberately larger than this change. Deferring is a decision, not an
oversight.

| Deferred | Why now is wrong |
|---|---|
| Instagram + YouTube niche search | One search adapter proves the shape; three multiply unverified provider schemas |
| Weighted 35/35/20/10 strategy mix | Dead configuration over a chain this small; revisit at ≥3 live sources per criterion |
| Clustering component + patterns table | Commits an unvalidated schema before any clustering exists; only the cheap lexical signal ships |
| Embeddings | Nothing in the repo has them; not required by any shipping signal |
| True growth velocity | Needs a second observation of the same candidate (see below) |
| Competitor source | `competitors` criterion stays on the default chain |
| YouTube network seeding (`SocialNetworkCatalogSeeder`) | No YouTube source ships, so seeding it advertises a capability that is not there |
| Persisting workflow sessions | `InMemoryContentWorkflowService`'s `ConcurrentDictionary` stays; unrelated to discovery persistence |
| Frontend redesign | The criterion selector already exists and already works; only its `own` option starts succeeding |

### Trending vs Proven — operational definition

Both are computed from a **single snapshot** and are honest proxies, not measurements.

| Label | Rule from one snapshot |
|---|---|
| **Trending** | Age ≤ trending window **and** views/day above the author baseline's median views/day (falling back to the candidate-set median when no baseline exists) |
| **Proven** | Age ≥ proven floor **and** total performance above the author baseline median by a configured factor |
| **Undetermined** | `PublishedAtUtc` unavailable — the honest answer, not a default to Proven |

**Admitted limitation**: real growth velocity requires observing the *same* candidate twice. This change
does not measure velocity. It records `(candidate, observed_at, metrics)` in `discovered_candidates` so a
future second observation becomes possible — the table is the enabler, the metric is a follow-up.

## Capabilities

### New Capabilities

- `content-discovery`: multi-source candidate discovery routed by search criterion, opportunity scoring,
  Trending/Proven classification, candidate + account-baseline persistence, and TOP-N fan-out into deep
  analysis.

### Modified Capabilities (additive deltas)

- `content-research`: `SearchCriterion` routes discovery instead of only being displayed; `own` becomes a
  valid criterion; discovery results are persisted rather than discarded.
- `integrations`: new `ActorVariantKey` `"niche-search"` in the `(networkKey, variantKey)` → actor-id
  mapping, alongside the existing `"profile"` and `"social-extraction"`.

## Approach

Introduce `IContentDiscoverySource` (Application port) and `ContentDiscoveryChain`. The chain **is** the
new `IContentDiscoveryService` implementation, composed over the ordered sources.

**The DI trap, handled explicitly.** `DependencyInjection.cs:220` registers
`services.AddScoped<IContentDiscoveryService, ReferenceAccountContentDiscovery>()`, and
`InMemoryContentWorkflowService.cs:127` resolves it with `GetRequiredService`. Adding a second
implementation registration would silently win and erase the first, with no compile error and no runtime
error — just wrong results. The fix is not a new registration: line 220 is **replaced** in the same commit
so `IContentDiscoveryService` → `ContentDiscoveryChain`, and the three sources register as
`IContentDiscoverySource`. A DI resolution test asserts that the resolved service is the chain and that
all three sources are present, so a future fourth source cannot regress this silently.

Flow: criterion → chain selects sources → sources return normalized candidates with nullable metrics →
persist candidates → pure scorer ranks → TOP N get an author baseline (cached) and re-score → TOP N fan
out into `ISingleContentAnalyzer` → existing generation rails continue unchanged.

### Architecture boundaries

| Piece | Layer | Why there |
|---|---|---|
| `DiscoveredContent`, `ContentDiscoveryRequest`, `IContentDiscoverySource`, `IContentCandidateRepository`, `IAccountBaselineRepository` | Application (Abstractions) | Ports and DTOs; where `IContentDiscoveryService` already lives |
| `ContentDiscoveryChain`, the three sources, opportunity scorer, Trending/Proven classifier, TOP-N fan-out orchestrator | Application (Features) | Use-case orchestration and pure logic; no provider types |
| Apify TikTok niche-search adapter, EF entities/configs/migration, repository implementations, DI wiring | Infrastructure | Provider and persistence detail only |
| Domain | **untouched** | Discovery candidates are research data, not a business aggregate with invariants |

No Apify, EF Core, or ASP.NET type crosses into Domain or Application. The scorer is pure and I/O-free,
following `ProfileMetricCalculator`. Architecture gate must stay green.

## ADR — required

`openspec/config.yaml` sets `require_adr_for_architecture_changes: true`, and this change introduces a new
Application port **plus** a new persistence model **plus** a replacement of an existing DI registration —
squarely the shape covered by ADR-0005 (`background-sourcing-strategy-port`) and ADR-0007
(`image-generation-gateway-port`).

**ADR file**: `docs/architecture/adr/0012-content-discovery-source-chain-and-candidate-persistence.md`

> Numbering note: `docs/architecture/adr/` currently has two `0005-*` and two `0006-*` files. `0012` is
> the next unambiguously free number.

## PRD Traceability

`openspec/config.yaml` sets `require_prd_traceability: true`.

| PRD anchor | This change |
|---|---|
| §1 Product Summary — "discover high-performing content in a niche" | The `niche` source is the first implementation of the niche half of that sentence |
| §2 Problem — "repeatedly research viral content... manual, inconsistent" | Discovery + scoring replaces the manual research pass |
| §3 Goals — "Automate content research and idea generation" | Automates the research half |
| §6 step 2 — "niche, reference accounts, own accounts, or manual URL list" | Ships niche + reference + own; manual URL is the existing single-analyzer path |
| §6 step 3 — "System retrieves candidate high-performing content through configured data provider" | The chain over Apify-backed sources |
| §6 step 4 — structured attributes | Fed by the TOP-N fan-out into the existing analyzer, not re-implemented |

## Affected Areas (intent level)

| Area | Impact | Description |
|---|---|---|
| `Application/Abstractions/Social/IContentDiscoveryService.cs` | Modified | Breaking: `DiscoveredContent` gains `PublishedAtUtc` + nullable shares/saves/availability; `ContentDiscoveryRequest` gains `SearchCriterion` |
| `Application/Abstractions/Social/IContentDiscoverySource.cs` | New | Per-source port with a source key |
| `Application/Abstractions/Social/ContentDiscovery/I{ContentCandidate,AccountBaseline}Repository.cs` | New | Persistence ports |
| `Application/Features/Social/ContentGeneration/Discovery/ContentDiscoveryChain.cs` | New | Ordered composition; mirrors `ContentIdeaChain` |
| `.../Discovery/{ReferenceAccount,OwnAccount,TikTokNicheSearch}DiscoverySource.cs` | Modified/New/New | The three sources |
| `.../Discovery/OpportunityScorer.cs` + `TrendingProvenClassifier.cs` | New | Pure, I/O-free |
| `.../Discovery/AccountBaselineProvider.cs` | New | Baseline compute + cache read-through |
| `.../Discovery/AnalyzeTopCandidatesCommand.cs` | New | TOP-N fan-out into `ISingleContentAnalyzer` |
| `Application/Features/Social/Workflow/ContentWorkflowCatalog.cs` (lines 5-18) | Modified | `"own"` alias (+ `"propios"`); defect fix |
| `Infrastructure/Social/Workflow/InMemoryContentWorkflowService.cs` (line 130) | Modified | Pass `SearchCriterion` into `ContentDiscoveryRequest` |
| `Infrastructure/DependencyInjection/DependencyInjection.cs` (line 220) | Modified | **Replace** single registration with chain + three sources |
| `Infrastructure/Social/Providers/Apify/TikTokNicheSearchAdapter.cs` + actor mapping | New | `"niche-search"` variant |
| `Infrastructure/Persistence/Postgres/Entities/` + configs + migration | New | `discovered_candidates`, `account_baselines` |
| `Infrastructure/Persistence/Postgres/Repositories/Ef{ContentCandidate,AccountBaseline}Repository.cs` | New | Port implementations |
| `SocialStudio.Tests/` | Modified/New | Strict TDD; `ReferenceAccountContentDiscoveryTests` + `ContentWorkflowServiceTests` break on the contract change and move with it |
| `docs/architecture/adr/0012-*.md` | New | Required ADR |

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| **Apify search-actor schema unverified.** The `clockworks/tiktok-hashtag-scraper` class was confirmed by documentation only, never by a live call | High | **Spike before any adapter code is committed**: one live call, record the real response fields, then write the adapter against observed reality. Named as a blocking design-phase task. If the schema does not carry publish date or shares, Trending degrades to `Undetermined` for that source rather than the change failing |
| Silent DI override erasing a source | Medium | Line 220 is replaced, not supplemented; a DI resolution test asserts chain identity and source count |
| `DiscoveredContent` breaking change ripples into existing tests | High (certain) | Deliberate; `ReferenceAccountContentDiscovery` and its tests move in the same commit, Strict TDD-first |
| Baseline scraping cost overrun | Medium | Baselines only for already-scraped accounts and TOP N; cached in `account_baselines`; explicitly never per candidate |
| Zero-filled metrics skewing the score across networks | Medium | Nullable + explicit availability; the scorer must exclude unavailable metrics from the denominator, asserted by unit test |
| Trending/Proven read as a measurement, not a proxy | Medium | Documented as snapshot heuristics in the spec; `Undetermined` is a first-class outcome |
| PR size well over the review budget | High (accepted) | `size:exception` accepted by the owner; see forecast |
| Instagram-only limit on `references`/`own` disappoints a TikTok-first client | Medium | Stated as a known limitation in the spec; profile scraping is Instagram-only today (exploration gap 1) |

## Size Forecast (single PR, accepted `size:exception`)

| Slice | Est. changed lines |
|---|---|
| Contract change + ports | ~250 |
| Chain + three sources | ~600 |
| Scorer + classifier + baseline provider | ~400 |
| Persistence (entities, configs, migration, repositories) | ~450 |
| Apify niche-search adapter + actor mapping | ~300 |
| TOP-N fan-out orchestration | ~200 |
| Wiring, DI, catalog defect fix | ~150 |
| Tests (Strict TDD, roughly 1:1) | ~1,400 |
| ADR + spec/doc sync | ~200 |
| **Total** | **~3,000–4,000** |

`400-line budget risk: High`. Roughly 2–2.7× the 1,500-line session budget and ~8× the 400-line default.
This is knowingly accepted; chained PRs were explicitly declined by the owner. Reviewers should read in
the order: contract → chain + DI replacement → sources → scorer → persistence → fan-out.

## Rollback Plan

- **Behavioral rollback is one line.** Restore `DependencyInjection.cs:220` to
  `AddScoped<IContentDiscoveryService, ReferenceAccountContentDiscovery>()` and discovery returns to
  today's referents-only behavior. The chain and extra sources become dead code, not a broken system.
- The two new tables are **purely additive** and read by nothing outside discovery; dropping them loses
  accumulated candidates and cached baselines but no existing behavior.
- The `DiscoveredContent` contract change is the only non-trivially reversible piece: it and its consumers
  (`ReferenceAccountContentDiscovery`, `InMemoryContentWorkflowService`, their tests) form one coordinated
  rollback unit.
- The `"own"` alias fix is independently revertible and independently safe to keep.
- The Apify `"niche-search"` actor mapping is configuration; removing the mapping disables the niche source
  without a code change (the chain tolerates a failing source by contract).

## Dependencies

- Existing `IProfileScrapeResolver` / `IClientBrandSnapshotLoader` / `IClientOwnAccountRepository` — reused
  read-only.
- Existing `ISingleContentAnalyzer` (`StartAsync` / `RunQueuedAsync` / `GetAsync`) — reused; only fan-out
  orchestration is new.
- New EF migration (two tables).
- **Open provider dependency**: real Apify TikTok search-actor response schema. Spike required before
  adapter code.
- Configured Apify integration per organization (existing prerequisite, unchanged).

## Success Criteria

- [ ] Selecting `own` in the workflow UI no longer returns `SearchCriterionInvalid`.
- [ ] The search criterion selects which sources run; `references`, `own`, and `niche` each produce
      distinct candidate sets from the same client.
- [ ] `IContentDiscoveryService` resolves to `ContentDiscoveryChain` and all three sources are registered
      (asserted by a DI resolution test).
- [ ] One failing or unconfigured source yields a partial result with a reported shortfall, never an
      exception (existing port contract: return empty, never throw).
- [ ] A TikTok candidate carries `PublishedAtUtc` and shares; an Instagram candidate reports shares as
      *unavailable*, not `0`, and the scorer excludes it from that candidate's denominator.
- [ ] Candidates persist to `discovered_candidates` with `observed_at`; baselines persist to and are read
      back from `account_baselines` without a second scrape.
- [ ] Each candidate is classified `Trending`, `Proven`, or `Undetermined`; a candidate with no publish
      date is `Undetermined`.
- [ ] TOP N candidates fan out into `ISingleContentAnalyzer` and the resulting analyses are usable by the
      existing generation rails unchanged.
- [ ] ADR `0012` exists and records the port + persistence decision.
- [ ] Provider types (Apify, EF, ASP.NET) do not leak into Domain or Application; architecture gate green.
- [ ] All MUST gates pass (`pwsh -File scripts/gates/Run-Gates.ps1`); Strict TDD followed
      (`dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj -c Release`).
- [ ] `docs/PRD.md` / `openspec/specs/` updated for the behavior change (AGENTS.md documentation-sync rule).

## Proposal question round (non-blocking — auto mode)

The owner resolved 9 decisions before this phase. These remain genuinely open and are **assumed** as
stated; correct any of them before spec/design starts.

1. **Trending window and proven floor** — assumed 14 days and 90 days, both configurable. Are those the
   operator's real mental model, or is a TikTok "trend" shorter (72h)?
2. **TOP N** — assumed a small fixed N (5–10) because each one costs a deep analysis plus possibly a
   baseline scrape. Is N operator-chosen per run, or a fixed product decision?
3. **Baseline staleness** — assumed a cached baseline is reused for some TTL (e.g. 30 days) before being
   recomputed. What staleness is acceptable before a relative-performance number misleads?
4. **Instagram-only `references`/`own`** — profile scraping is Instagram-only today. Is a client with only
   a TikTok presence getting an empty `own` result an acceptable first slice, or a blocker?
5. **`viral` and `competitors` criteria** — assumed they fall back to the default ordered chain rather
   than erroring, since both are already valid catalog values with no dedicated source. Confirm that
   silent fallback is preferable to telling the operator the strategy is not implemented yet.

## Scope Amendment — 2026-08-09

The owner issued two corrections to this draft after the initial spec round, both applied
surgically to `specs/content-discovery/spec.md`, `specs/integrations/spec-delta.md`, and (by
review, no changes required) `specs/content-research/spec-delta.md`. `design.md` and `tasks.md`
are being re-amended in the phases immediately following this one; this section is the durable
record of *what* changed and *why*, kept alongside the original proposal rather than rewriting
it.

### Correction 1 — No actor may ever be forced in code; routing is always configuration

The owner's words, in substance: no actor may ever be summoned forcibly by any part of the
system — the workflow configuration always indicates it. This is not new intent (it was already
the house pattern in `ApifyProfileScrapeResolver` for the single-analyzer URL flow), but it was
under-specified for the niche-search stage this change introduces, and the settings-UI
visibility trap (`WorkflowConfigurationCatalog.cs:123-126`) was not called out as a requirement.

**Applied**:
- `content-discovery` spec gains **CD-12** (actor resolution is exclusively
  configuration-driven, no hardcoded fallback anywhere) and **CD-13** (an unconfigured stage
  must be distinguishable from a stage that legitimately found nothing).
- `integrations` spec-delta gains **INT-DISC-02** (a default workflow/step ships, with a
  seed-not-fallback placeholder mapping), **INT-DISC-03** (the discovery workflow MUST have a
  matching checklist group in `GetOrganizationIntegrationsHandler`, or it is invisible in
  settings), and **INT-DISC-04** (`niche-search` is its own actor variant, never inferred from
  `profile`/`social-extraction`, and backfill MUST NOT be extended to produce it).
- `INT-DISC-01` (unchanged id) is tightened with an explicit "never a hardcoded fallback"
  sentence.

### Correction 2 — This change is instant, single-selection — not the batch flow

The owner clarified the runtime shape: discovery proposes a ranked shortlist; the operator
selects **exactly one** candidate; that one candidate is analyzed **instantly and
synchronously**, reusing the existing Single Analyzer by URL's screen-blocking progress UX.
Selecting multiple candidates for background batch processing is a **separate, later** change
(**Magic Generator**) and is explicitly out of scope here.

**Applied**:
- `content-discovery` **CD-10** is rewritten in place from "TOP-N fan-out tolerating partial
  failure across N background analyses" to "operator selects exactly one ranked candidate for
  instant, synchronous analysis via the existing single-analysis path." The former text is
  preserved verbatim under `## Removed / Superseded Requirements` in that spec file, with
  Reason and Migration, rather than silently deleted.
- Ranking/scoring/TOP-N *selection for display* is **unchanged and still required** (CD-05,
  CD-06, CD-09 stand as written) — the operator still sees a ranked shortlist with numbers
  attached. What is removed is *automatic analysis of all of them*.
- The following items from this proposal's original scope are now **superseded, not
  cancelled** — they move to the Magic Generator follow-up:
  - Item 8 ("TOP-N fan-out into the existing `ISingleContentAnalyzer`... the fan-out
    orchestration is new") — superseded by single-selection instant analysis.
  - `AnalyzeTopCandidatesCommand.cs` (Affected Areas table) — not built in this change;
    single-selection reuses the existing single-analysis start path directly.
  - OQ-6 ("Fan-out failure semantics: if 3 of 10 deep analyses fail...") — moot; only one
    analysis ever runs per operator action.
  - The Success Criteria checkbox "TOP N candidates fan out into `ISingleContentAnalyzer`..." —
    superseded by: *the operator's single selected candidate starts instantly through
    `ISingleContentAnalyzer` and is visible on a screen-blocking progress surface identical to
    the existing Single Analyzer by URL.*
- **New non-goal, explicit**: multi-select background batch analysis of discovered candidates
  is the Magic Generator, a later change, not built here.

### What did NOT change

- The three discovery sources (`references`, `own`, `niche`), the `own` criterion defect fix,
  the breaking `DiscoveredContent` contract change, nullable-metrics-never-zero-filled, the
  pure opportunity scorer, Trending/Proven/Undetermined classification, the two persistence
  tables, and the DI replacement are all unaffected by either correction and remain as
  originally proposed.
- Size forecast, risks, rollback plan, and PRD traceability in this document are not rewritten;
  the fan-out-orchestration line item they referenced is now understood as reduced to "start
  one existing single-analysis call," which is smaller, not larger, than originally forecast —
  no size increase results from this amendment.

## Open Questions (for spec/design)

- **OQ-1** — Spike outcome: exact Apify TikTok search-actor response fields; does it carry publish date
  and shares?
- **OQ-2** — Chain ordering per criterion: does `niche` also consult `references` as a filler when short,
  or return a shortfall?
- **OQ-3** — Candidate dedup key across sources and networks (`ContentUrl`? normalized URL? author+postId?).
- **OQ-4** — Candidate retention: does `discovered_candidates` grow unbounded, or is there a purge policy?
- **OQ-5** — Scorer weighting: fixed weights in code, or configurable per organization?
- **OQ-6** — Fan-out failure semantics: if 3 of 10 deep analyses fail, is the run partial-success or retried?
- **OQ-7** — Lexical co-occurrence signal: exact tokenization and the K threshold for "seen in K creators".
