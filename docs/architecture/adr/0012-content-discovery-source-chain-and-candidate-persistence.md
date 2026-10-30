# ADR 0012 - Content Discovery Source Chain and Candidate Persistence

## Status
Accepted — implemented (2026-08-10). See "Amendment — spike waived" below for one deviation from
Consequences.

> Numbering note: `docs/architecture/adr/` contains two `0005-*` and two `0006-*` files. `0012` is the next
> unambiguously free number (max in use is `0011`).

## Context

Content discovery today has exactly one implementation. `IContentDiscoveryService` is registered as
`ReferenceAccountContentDiscovery` at `SocialStudio.Infrastructure/DependencyInjection/DependencyInjection.cs:220`,
and `InMemoryContentWorkflowService.cs:127` resolves it with `GetRequiredService`. Three problems follow:

1. **One source, no composition.** Discovery mines only the client's Instagram reference accounts. The PRD's other
   named source strategies (niche search, own historical winners) have nowhere to live.
2. **`SearchCriterion` does not route.** It is normalized, validated, stored on the session and echoed into result
   payloads, but `DiscoverResultsAsync` builds `ContentDiscoveryRequest` at line 130 without it. It is a display
   value. The `own` option offered by the frontend is not even in `ContentWorkflowCatalog.SearchCriterionAliases`,
   so selecting it returns `SearchCriterionInvalid`.
3. **Nothing is persisted and nothing is measurable.** `DiscoveredContent` carries no publish date, no shares/saves
   and no author baseline, so neither relative performance nor any Trending/Proven distinction is computable. Results
   are discarded at the end of the run.

There is also a composition hazard: adding a second `AddScoped<IContentDiscoveryService, …>` would silently win
`GetRequiredService` and erase the first, with no compile error and no runtime error — only wrong results.

## Decision

### 1. `ContentDiscoveryChain` replaces the single registration (new Application port family)

A new Application port `IContentDiscoverySource` (`SourceKey`, `Order`, `Supports(searchCriterion)`, `DiscoverAsync`)
is introduced, and `ContentDiscoveryChain` becomes the one and only `IContentDiscoveryService`. The chain mirrors the
proven `ContentIdeaChain` idiom: sorted by `Order` (never DI registration order), asks each source only for the
remainder, deduplicates, tolerates a failing source, and reports `Shortfall` + `FailedSourceKeys`.

`DependencyInjection.cs:220` is **replaced**, not supplemented. A composition test asserts that
`GetServices<IContentDiscoveryService>()` yields exactly one registration and that it is the chain — that count
assertion is what makes the silent-override hazard a red test instead of a production defect.

Three sources ship: `references` (the adapted existing implementation), `own` (near-clone over
`IClientOwnAccountRepository`), and `niche` (new TikTok search source). `viral` and `competitors` are valid catalog
values with no dedicated source; when no source claims a criterion the chain consults all sources in order rather
than raising an error.

### 2. `SearchCriterion` becomes the router; the `own` defect is fixed with its source

`ContentDiscoveryRequest` gains the normalized `SearchCriterion`, and `ContentWorkflowCatalog.SearchCriterionAliases`
gains `own`/`propios`. Routing by an existing, already-validated field was chosen over a weighted strategy allocator:
a configurable mix over a small chain is dead configuration with maintenance cost. Weighted blending is revisited at
three or more live sources per criterion.

### 3. Breaking contract change on `DiscoveredContent`

`Views`, `Likes` and `Comments` become `long?`; `Shares`, `Saves`, `PublishedAtUtc` and `SourceKey` are added; the port
returns `ContentDiscoveryPlan(Candidates, Shortfall, FailedSourceKeys)` instead of a bare list. `null` means *the
network does not report this metric*; `0` means it reported zero. Zero-filling was rejected because it systematically
penalises whole networks — TikTok reports shares, Instagram does not, YouTube reports neither shares nor saves. The
existing `post.VideoViewCount ?? 0` zero-fill is removed as part of this change.

This breaks `ReferenceAccountContentDiscovery`, `InMemoryContentWorkflowService` and their tests. They move in the same
unit; the compiler is the safety net that prevents a half-applied change.

### 4. Two additive tables, append-only observations

`social.discovered_candidates` stores one row per *observation*, keyed by a stable `candidate_key`
(`"{network}:{normalized content URL}"`) produced by a single pure function shared by the in-run dedup and the
database, so identity can never disagree between the two. `social.account_baselines` caches one median-metrics
baseline per `(organization, network, author handle)` with a staleness TTL.

An identity/observation table split was rejected: it normalizes for a reader that does not exist yet. A patterns or
clustering table was rejected outright — no clustering component ships, and committing that schema now would fix an
unvalidated shape. Baselines are computed only for accounts already scraped for free (reference and own accounts) and
for the TOP N after cheap filtering, never per raw candidate.

### 5. Pure opportunity scorer and single-snapshot classification

An I/O-free `OpportunityScorer` in Application, modelled on `ProfileMetricCalculator`, combines absolute engagement,
engagement rate, views/day, recency, relative performance versus baseline, and a cheap lexical co-occurrence signal
with fixed in-code weights. Availability-aware aggregation divides only by the weights of the *available* signals, so a
missing metric shrinks the denominator instead of scoring zero. `nowUtc` is a parameter, never read inside, which is
what makes the age-dependent signals deterministic and unit-testable.

`Trending` / `Proven` / `Undetermined` are computed from one snapshot and documented as honest proxies, not
measurements. A candidate without a publish date is `Undetermined` — never defaulted. True growth velocity requires a
second observation of the same candidate; the observation table is the enabler, the metric is a follow-up.

### 6. `niche-search` actor variant, resolved only from configuration

*(Amended 2026-08-09 — owner correction 1; placement corrected 2026-08-09 — owner correction 3.)*

A new `ActorVariantKey` `"niche-search"` rides the existing generic `(networkKey, variantKey)` → actor-id mapping with
no framework change. It is carried by a new `content-discovery` analysis type holding ONLY a `discover-niche-content`
step, added under the **existing `single-analyzer` workflow key**, as a sibling of `content-analysis`/`profile-analysis`
— not under `generator` and not a new top-level key. Once the operator selects one discovered candidate it becomes a
`SingleContentAnalysis`, and the downstream pipeline (`CreateContentGenerationBatchCommand`,
`GenerateContentPackageCommand`) anchors to that `AnalysisId`, identical to the by-URL flow already configured under
`single-analyzer`/`content-analysis` — so `content-discovery` reuses those AI steps rather than duplicating them.
(An earlier iteration placed the analysis type under `generator` and duplicated the `analyze-content`/`generate-content`/
`generate-image` steps to make that group self-sufficient; this was corrected because it forced the operator to
configure the same AI providers twice for one pipeline, and because the settings selector, while built from checklist
groups, correctly already has a `single-analyzer` group — no new group is required either way.) One optional checklist
item, `single-analyzer-niche-search-actor`, points the operator at the new stage.

**No actor id is ever forced.** No literal actor id may exist in Application or Infrastructure code, for any network or
capability. Resolution goes through `IWorkflowConfigurationResolver.ResolveStepAsync` and `actor_mappings_json`,
exactly as `ApifyProfileScrapeResolver` already does for the single-analyzer URL flow — which fails structurally on an
empty mapping instead of substituting a default. A placeholder mapping may be **seeded as data** so the operator has
something concrete to edit; clearing it degrades to an unconfigured stage, never to a code-level default. A
repository-wide test asserts the absence of actor-id literals in shipped source.

`ApifyActorMappingBackfill` is deliberately **not** extended — it retags legacy entries to `profile` /
`social-extraction`, and teaching it about `niche-search` would re-tag existing extraction actors and break the single
analyzer. Operators add the mapping explicitly.

An **unconfigured** source is reported through its own `UnconfiguredSourceKeys` channel on `ContentDiscoveryPlan`,
separate from `FailedSourceKeys`. `FailedSourceKeys` alone cannot carry the distinction — "no actor configured" and
"the provider returned 500" would be the same key in the same list, and the operator needs to know which of the two
sent them to the settings screen.

### 6b. The operator selects exactly one candidate; no automatic batch analysis

*(Amended 2026-08-09 — owner correction 2. The previous decision, automatic TOP-N fan-out across N background
analyses tolerating partial failure, is withdrawn.)*

Discovery ranks and proposes; it starts nothing. The operator selects **exactly one** candidate, and that candidate
takes the path the Single Analyzer by URL already takes: the existing `AnalyzeSingleContentCommand` endpoint
(`StartAsync` → `ISingleAnalysisJobQueue` → return id) plus the existing client-side polling overlay that maps
`SingleContentAnalysisStatus` onto staged progress. The screen-blocking experience is the overlay, not the transport;
a genuinely synchronous request would be a second mechanism holding an HTTP connection open across a scrape and an AI
call. Consequently this change authors **no** new command, endpoint, queue, progress channel or concurrency primitive
on that path — only a frontend action that enforces single selection.

Multi-select background batch analysis of discovered candidates is deferred to the separate **Magic Generator**
change, which inherits the withdrawn fan-out design intact.

### 7. Domain stays untouched

Discovery candidates, scores and classifications live in Application, alongside the existing `DiscoveredContent`. The
nearest precedent points the other way — `ProfileMetricCalculator` (Application) returns `ProfileStats` (Domain) — but
`ProfileStats` is owned and rehydrated by the persisted `ProfileAnalysis` aggregate. A discovered candidate has no
lifecycle: it is a source observation, recomputed each run, written as flat rows and read only by discovery. Its score
and classification are derived read-model values, not enforced business state.

**Revisit trigger**: as soon as an operator can select, dismiss or archive a candidate, it acquires a lifecycle and
invariants and becomes a Domain aggregate. That follow-up also owns the retention policy.

## Consequences

- `IContentDiscoveryService` has exactly one registration and it is the chain; adding a fourth source is one DI line
  and cannot silently erase the others.
- Behavioral rollback is one line: restoring the old registration returns discovery to today's referents-only
  behavior, leaving the chain and extra sources as dead code rather than a broken system.
- The `DiscoveredContent` contract change is the one non-trivially reversible piece; it and its consumers form a single
  coordinated rollback unit.
- Both tables are purely additive and read by nothing outside discovery; dropping them loses accumulated candidates and
  cached baselines only.
- `discovered_candidates` grows unbounded in this change. An index on `(organization_id, observed_at)` is added so the
  later purge is a single ranged delete; the retention policy itself is a named follow-up.
- Removing the `(tiktok, niche-search)` actor mapping, or disabling the `discover-niche-content` step, disables the
  niche source with no code change — the chain reports it as unconfigured by contract, distinguishably from a source
  that ran and found nothing.
- Because no actor id exists in code, a niche source can never run for an organization that has not configured one.
  That is the intended behavior: an empty configuration produces an explained empty result, not a vendor default.
- The single-selection path adds no backend surface, so its rollback is a frontend revert. The withdrawn fan-out
  design is preserved for the Magic Generator change rather than discarded.
- `references` and `own` remain Instagram-only because profile scraping is Instagram-only today; a TikTok-only client
  gets an empty, non-throwing `own` result with a reported shortfall.
- The response schema of whichever actor an organization configures for niche search is confirmed by documentation
  only. A live schema spike against the configured actor, recorded as a capability-named test fixture, is a blocking
  prerequisite before any adapter code is committed. If the response carries no play count the source is unrankable
  and ships nowhere without an explicit owner decision. If a configured actor answers with a shape the adapter cannot
  map at all, the source reports a failure naming the configured actor — never an invented or defaulted candidate.

## Amendment — spike waived, adapter shipped documentation-derived (2026-08-10)

The Consequences section above states the live schema spike is "a blocking prerequisite before any
adapter code is committed." The owner explicitly waived that prerequisite: no organization has a
`(tiktok, niche-search)` actor configured yet, so there was nothing to call. `TikTokNicheSearchAdapter`
was implemented anyway, against the DOCUMENTED TikTok Apify item shape — the same field family already
verified live for the single-video flow (`TikTokApifyScrapeAdapter`, obs 426) — with every metric
nullable and every absent field degrading to `null` rather than throwing or zero-filling, per the
Decision's own D2 rule. The mapping is isolated to `TikTokNicheSearchAdapter.MapItem`, one place, so a
single correction fixes it once the owner configures a real actor and the first live call is recorded.
Both the adapter and its test fixture (`tiktok-niche-search.json`) carry an explicit
"PENDING LIVE VERIFICATION" comment. Until an actor is configured, `ApifyNicheSearchResolver` correctly
returns `SourceOutcome.Unconfigured` — the specified behavior, not a defect — so this deviation carries
no runtime risk: the niche source simply does not run until the owner finishes configuring it, exactly
as CD-13/D12 already require for any unconfigured stage.

## Related

- Exploration: `openspec/changes/content-discovery-intelligence/exploration.md`
- Proposal: `openspec/changes/content-discovery-intelligence/proposal.md`
- Design: `openspec/changes/content-discovery-intelligence/design.md`
- Specs: `openspec/changes/content-discovery-intelligence/specs/`
- Follows the port + adapter precedent of ADR 0005 (background sourcing strategy port) and ADR 0007 (image generation
  gateway port)
- Reuses the single-analysis rails established alongside ADR 0003 (profile analysis model)
