# Tasks: niche-discovery-engine

**Delivery:** sequential commits directly to `dev`. No branches, no PRs, no PR chaining. Each work unit below
closes with one commit using the exact conventional-commit message given, scope `niche-discovery`. No
`Co-Authored-By` or AI-attribution trailer in any commit message.

**TDD mode:** STRICT — `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj -c Release` for backend,
frontend test command per `scripts/gates/Run-Gates.ps1` for `apps/web`. Every behavioural task is RED (failing
test from a spec scenario or design guard) → GREEN (implementation) → run the focused filter, then the full
suite before the commit that closes the work unit.

**Gate:** `pwsh -File scripts/gates/Run-Gates.ps1` MUST pass before every commit listed below. No MUST gate may
be downgraded or skipped; `--no-verify` and equivalents are forbidden (repo rule, `.github/incoders-ai/gates`).

> **Plan correction note.** This plan supersedes the proposal's 13-work-unit forecast per design §18. Four
> corrections applied: WU-7 (persistence) splits schema+migration from repositories by default; the
> `DiscoveryBudget`/`IDiscoveryBudgetLedger` Application contracts move from WU-5 into WU-3; WU-11 splits into
> orchestration (11a) and endpoints (11b); WU-4 is flagged, not assumed safe, if the settings UI needs a new
> branch for `IntegrationProviderCategory.OfficialDataApi`. One ordering correction: WU-9 (market comparison)
> depends on WU-6 (`TargetMarketDemand` consumes `VelocityProxy`), not merely "independent of WU-10" as the
> proposal stated. Result: **15 work units**, not 13 and not exactly 14 — the design's own "~14" undercounted
> because both the WU-7 split (its own "default, not fallback" recommendation) and the WU-11 split apply
> simultaneously. Content placement (which type lives in Domain vs Application) follows design §2's component
> map exactly, which is more precise than the proposal's "Domain: Evidence/Metric/Signal/Score value objects"
> phrasing — those are Application types (Abstractions for Evidence, Features for Metric/Signal/Score); only
> `DiscoveryRun` and its immediate value objects are Domain.

---

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines (authored, tests ≈ 1:1) | **~9,100** |
| Generated migration diff (WU-7a `.Designer.cs` + `AppIdentityDbContextModelSnapshot.cs`) | **+300–500, counted separately, not against the 800 budget** (design §18.1) |
| 800-line budget risk | **Medium-High** — two units sit at the ceiling (WU-10, WU-12); WU-4 is flagged as optimistic and may need its own split if the settings UI needs a new provider-category branch |
| Any unit over 800? | No unit is planned above 800; WU-10 and WU-12 are AT 800 and are the units most likely to blow through it in practice, exactly as the proposal already flagged before the corrections in this plan |
| Delivery mode | **Sequential commits directly to `dev`** — no PR, no PR chain. The "commit sequence" below is the delivery artifact; each commit is independently revertible with `git revert` since the migration is additive and DI registrations are additive-until-WU-11b |
| Commit sequence | WU-1 → WU-2 → WU-3 → WU-4 → WU-5 → WU-6 → WU-7a → WU-7b → WU-8 → WU-9 → WU-10 → WU-11a → WU-11b → WU-12 → WU-13 (linear; see dependency table below for which steps could reorder without breaking anything, e.g. WU-4 before WU-3 is safe, WU-9 cannot precede WU-6) |

### Work-unit table

| # | Goal | Depends on | Est. lines | Budget risk | Commit message |
|---|---|---|---|---|---|
| WU-1 | Domain: `DiscoveryRun` aggregate, `DiscoveryRunId`, `DiscoveryRunStatus`, `DiscoveryWindow`, `DiscoveryDepth`, `DiscoveryStage`, `NicheDiscoveryErrors` | — | ~500 | Low | `feat(niche-discovery): add DiscoveryRun aggregate and lifecycle value objects` |
| WU-2 | Domain: taxonomy hierarchy (`NicheTaxonomyNode`, `SubNicheRef`, `ContentPatternKind`, `TopicLabel`) cross-referencing `Niches.NicheId` by value; `MarketCode` + `MarketCatalogEntry`; country-name guard test (RED first) | WU-1 | ~550 | Low | `feat(niche-discovery): add taxonomy hierarchy and market catalog domain model` |
| WU-3 | Application ports: `INicheDiscoverySource`, `NicheDiscoverySourceResult`/`NicheSourceOutcome`, `NicheDiscoveryQuery`, `ChannelEvidence`, `VideoEvidence`, `EvidenceSet`, repository ports, `INicheDiscoveryOriginProbe`, `INicheDiscoveryRunQueue`, `INicheOpportunityActivationSource` (named seam) **+ `DiscoveryBudget` + `IDiscoveryBudgetLedger`** (moved in from WU-5 per design §18.2) | WU-1, WU-2 | ~720 | Medium | `feat(niche-discovery): add NicheDiscovery application ports and discovery budget contracts` |
| WU-4 | Integrations: `IntegrationProviderCategory.OfficialDataApi` (appended, never renumbered), API-key credential surface, `youtube-data-api-v3` catalog entry, `niche-discovery` workflow key + `market-opportunity` analysis type + 4 stage steps, third checklist group in `GetOrganizationIntegrationsHandler` | — (independent of WU-1→3; may commit before or after) | ~750 | **Flagged** — verify the frontend integrations category renderer first; if it needs a new branch, split into 4a (backend catalog/credential/checklist) / 4b (frontend category rendering) before landing | `feat(integrations): add official-API provider category and niche-discovery workflow checklist group` |
| WU-5 | Infrastructure: `YouTubeDataApiNicheDiscoverySource`, `YouTubeDataApiClient`, `YouTubeEvidenceMapper` (normalization boundary), `YouTubeQuotaBudget`, `NicheDiscoveryOriginConfiguration`, `NicheDiscoveryOriginProbe`; extend `NoActorLiteralGuardTests` with the 3 new origin files | WU-3, WU-4 | ~680 | Medium | `feat(niche-discovery): add YouTube Data API v3 origin adapter and normalization boundary` |
| WU-6 | Deterministic analytics: `DeterministicMetricCalculator`, `Metric`, `MetricSet`, `SampleShape`; reflection guard (a) `Metric.From` has exactly one calling type | WU-3 | ~700 | Low | `feat(niche-discovery): add deterministic metric calculator and analytics guard` |
| WU-7a | Persistence — schema + migration only: 4 EF entities (`DiscoveryRunData`, `NicheOpportunityData`, `NicheDiscoveryEvidenceData`, `MarketCatalogData`), `AppIdentityDbContext` mappings, one migration `Add_NicheDiscoveryRunsOpportunitiesAndEvidence`, `MarketCatalogSeeder` | WU-1, WU-2 | ~450 authored (+300–500 generated, tracked separately) | Medium (generated diff) | `feat(niche-discovery): add persistence schema, migration, and market catalog seeder` |
| WU-7b | Persistence — repositories: `EfDiscoveryRunRepository`, `EfNicheOpportunityRepository`, `EfNicheDiscoveryEvidenceRepository`, `EfMarketCatalogRepository`, append-only enforcement at the port (no update/delete method) | WU-7a | ~500 | Low | `feat(niche-discovery): add EF repositories for runs, opportunities, evidence, and market catalog` |
| WU-8 | `Scoring/AvailabilityWeightedAggregate`, `NicheOpportunityScorer`, `OpportunityScoreWeights`, `ConfidenceScorer`; reflection guard (c) no `…Scoring`/`…Analytics` signature references a `…Semantics` type (written now, re-verified after WU-10) | WU-6 | ~700 | Low | `feat(niche-discovery): add opportunity scorer and independent confidence scorer` |
| WU-9 | `MarketComparison/TargetMarketEvaluator`, `TargetMarketDemand`, `TargetMarketCompetition`, `TargetMarketGap` | **WU-6** (consumes `VelocityProxy` — proposal's "independent of WU-10" claim does not extend to WU-6, per design §18) | ~620 | Low | `feat(niche-discovery): add target market demand, competition, and gap evaluation` |
| WU-10 | `Semantics/SemanticAnnotator`, `SemanticAnnotation`, `ClusterProposal`, `FormatTrait`, `LexicalClusterFallback`; reflection guard (b) no numeric member on any `…Semantics` type | WU-3 | ~800 | **At budget** — split points if it overshoots: 10a classification+clustering / 10b naming+pattern extraction, same as the sibling's WU-10 pattern | `feat(niche-discovery): add semantic annotation layer with lexical fallback` |
| WU-11a | `NicheDiscoverySourceChain`, `NicheDiscoveryRunProcessor`, `NicheDiscoveryRunQueue` + `NicheDiscoveryRunBackgroundService`; the "second target market needs no code change" criterion test | WU-5, WU-6, WU-8, WU-9, WU-10, WU-7b | ~500 | Medium | `feat(niche-discovery): add run orchestrator, queue, and background processing` |
| WU-11b | `MapNicheDiscoveryEndpoints`, `StartDiscoveryRunCommand`+`Handler`, `GetDiscoveryRunQuery`+`Handler`, `ListRunOpportunitiesQuery`+`Handler`, `GetOpportunityDetailQuery`+`Handler`; DI composition test (exactly one impl per NicheDiscovery port); namespace guard + `INicheOpportunityActivationSource` empty-registration test | WU-11a | ~500 | Low | `feat(niche-discovery): add run endpoints and composition guards` |
| WU-12 | Frontend: `lib/api/niche-discovery.ts`, `ranking.ts`, `filters.ts`, `availability.ts`, `viability-dashboard-client.tsx`, `opportunity-detail.tsx`, `evidence-list.tsx`, `score-pair-badge.tsx`, `excluded-signals-note.tsx` | WU-11b | ~800 | **At budget** — split points if it overshoots: 12a dashboard list / 12b detail view, same as the sibling's WU-12 pattern | `feat(niche-discovery): add viability dashboard and opportunity detail UI` |
| WU-13 | ADR `0013`, `docs/PRD.md` new section, spec/doc sync, operator doc | WU-1…WU-12 | ~350 | Low | `docs(niche-discovery): add ADR 0013, PRD section, and operator setup guide` |
| | **Total** | | **~9,100** (+300–500 generated) | | 15 commits |

---

## Blocked / prerequisite

- **Owner-side prerequisite, not blocking implementation**: a Google Cloud project with **YouTube Data API v3
  enabled** and a **per-organization API key** must exist before WU-5 can be verified against a live response.
  WU-5 is implemented and unit-tested entirely against the recorded fixture
  (`Incoders.Hive.Tests/Infrastructure/Social/Fixtures/youtube-niche-search.json`) — **no live call is made or
  required to land WU-5, WU-6, WU-8, WU-9, WU-10, WU-11a/b, WU-12**. Only a final live-response verification of
  `YouTubeEvidenceMapper.MapItem` against a real payload is deferred until the owner's Google Cloud project
  exists, exactly as ADR-0012's amendment recorded the same deviation for the Apify actor spike in the sibling
  change. This is noted in the WU-5 commit and in ADR-0013 (WU-13) as "PENDING LIVE VERIFICATION," never as a
  silent gap.
- **Fixture-only until the prerequisite exists**: WU-5's `YouTubeDataApiClient`/`YouTubeEvidenceMapper` tests,
  and WU-11a's end-to-end processor test that exercises the YouTube origin. WU-3, WU-4 (routing/config only —
  `Unconfigured` is the correctly specified behaviour without a key), WU-6, WU-7a/b, WU-8, WU-9, WU-10, WU-11b,
  WU-12 have no dependency on this prerequisite at all.

---

## Decision checkpoint carried forward

The design's open question on `TargetMarketCompetition`'s partition (content language+region vs. audience
geography) is **not a blocker for WU-1 through WU-8**. It is attached to **WU-9**: confirm with the owner
before implementing `TargetMarketEvaluator` whether the locale-of-content assumption (design §4, stated
explicitly as an assumption, not a decision) is correct, or whether audience geography is expected despite
YouTube's materially less reliable regional reporting for that dimension. If audience geography is chosen,
only `TargetMarketEvaluator`'s `TargetMarketCompetition` computation changes — the type signature and every
other work unit are unaffected.

---

## WU-1 — Domain: `DiscoveryRun` Aggregate and Lifecycle Value Objects

*Refs: ND-01, ND-02. Design §2.1, §3 (evidence/metric/signal/score layering — NOT built here; those are
Application types per WU-3/WU-6/WU-8/WU-9/WU-10).*

- [x] 1.1 **[RED]** Add `DiscoveryRunIdTests.cs` — value-object equality, `New()` produces a non-empty GUID. (`Incoders.Hive.Tests/Domain/Social/NicheDiscovery/DiscoveryRunIdTests.cs`)
- [x] 1.2 **[GREEN]** Create `DiscoveryRunId.cs`. (`Domain/Social/NicheDiscovery/DiscoveryRunId.cs`)
- [x] 1.3 **[RED]** Add `DiscoveryWindowTests.cs` — valid range 7..365, default 90, rejects out-of-range (ND-01). (`Incoders.Hive.Tests/Domain/Social/NicheDiscovery/DiscoveryWindowTests.cs`)
- [x] 1.4 **[GREEN]** Create `DiscoveryWindow.cs`, `DiscoveryDepth.cs` (`Quick|Standard|Deep`), `DiscoveryStage.cs` (`ChannelSearch|VideoSearch|ChannelDetail|VideoDetail`). (`Domain/Social/NicheDiscovery/{DiscoveryWindow,DiscoveryDepth,DiscoveryStage}.cs`)
- [x] 1.5 **[RED]** Add `DiscoveryRunTests.cs` — construction requires source market, target market, network key, content format, window, depth; snapshots configuration at creation (ND-01 scenario 1); status transitions `Requested → Running → Completed/PartiallyCompleted/Failed`; two runs with identical parameters produce comparable snapshots (ND-01 scenario 2); an unconfigured origin transitions distinctly from an empty-but-ran result (ND-02 scenario 1); quota/depth exhaustion transitions to `Failed` with a structured reason, never silently "completed with zero" (ND-02 scenario 2). (`Incoders.Hive.Tests/Domain/Social/NicheDiscovery/DiscoveryRunTests.cs`)
- [x] 1.6 **[GREEN]** Create `DiscoveryRunStatus.cs` (`Requested|Running|Completed|PartiallyCompleted|Failed`) and `DiscoveryRun.cs : Entity<DiscoveryRunId>` with the status-transition methods asserted above. (`Domain/Social/NicheDiscovery/{DiscoveryRunStatus,DiscoveryRun}.cs`)
- [x] 1.7 **[RED]** Add `NicheDiscoveryErrorsTests.cs` — every error code follows `social.niche_discovery.{reason}`, matching the codes enumerated in design §10. (`Incoders.Hive.Tests/Domain/Social/NicheDiscovery/NicheDiscoveryErrorsTests.cs`)
- [x] 1.8 **[GREEN]** Create `NicheDiscoveryErrors.cs` (static `Error.Validation`/`Error.Failure` factory, per `NicheDiscoveryErrors` sibling pattern). (`Domain/Social/NicheDiscovery/NicheDiscoveryErrors.cs`)
- [x] 1.9 Run `dotnet test --filter FullyQualifiedName~Domain.Social.NicheDiscovery`, then the full suite.
- [x] 1.10 `pwsh -File scripts/gates/Run-Gates.ps1` green. Commit: `feat(niche-discovery): add DiscoveryRun aggregate and lifecycle value objects`. **Committed as `3c454c1`.**

---

## WU-2 — Domain: Taxonomy Hierarchy, Market Catalog, Country-Name Guard

*Refs: ND-14, ND-15. Design §2.1, §4.*

- [x] 2.1 **[RED]** Add `MarketCodeTests.cs` — table test: `es-AR`/`pt-BR`/`en-US` parse to lowercase language + uppercase region; malformed input (`ARG`, `es_ar`, empty) yields a validation `Result` failure, never a throw; `.Language` is the only readable part beyond raw `.Value`. (`Incoders.Hive.Tests/Domain/Social/NicheDiscovery/MarketCodeTests.cs`)
- [x] 2.2 **[GREEN]** Create `MarketCode.cs` (`readonly record struct` + `TryParse`). (`Domain/Social/NicheDiscovery/MarketCode.cs`)
- [x] 2.3 **[RED]** Add `MarketCatalogEntryTests.cs` — construction requires `MarketCode`, `DisplayNameKey` (i18n key, never a literal display string), `PrimaryLanguage`, `IsActive`. (`Incoders.Hive.Tests/Domain/Social/NicheDiscovery/MarketCatalogEntryTests.cs`)
- [x] 2.4 **[GREEN]** Create `MarketCatalogEntry.cs : Entity<MarketId>` (+ `MarketId.cs`). (`Domain/Social/NicheDiscovery/{MarketId,MarketCatalogEntry}.cs`)
- [x] 2.5 **[RED]** Add `NicheTaxonomyNodeTests.cs`/`SubNicheRefTests.cs` — `SubNicheRef` cross-references `Domain.Social.Niches.NicheId` **by value** only, no navigation property, no modification of `Niche`. (`Incoders.Hive.Tests/Domain/Social/NicheDiscovery/Taxonomy/{NicheTaxonomyNodeTests,SubNicheRefTests}.cs`)
- [x] 2.6 **[GREEN]** Create `Taxonomy/{NicheTaxonomyNode,SubNicheRef,ContentPatternKind,TopicLabel}.cs`. `ContentPatternKind` includes `Unclassified` (semantic-failure fallback, N11). (`Domain/Social/NicheDiscovery/Taxonomy/*.cs`)
- [x] 2.7 **[RED — the country-name guard, written before anything below could tempt a shortcut]** Add `CountryNameGuardTests.cs` — source-scans every `.cs` file under `Domain/Social/NicheDiscovery`, `Application/**/NicheDiscovery`, `Infrastructure/**/NicheDiscovery`, plus `apps/web/**/niche-discovery/**` for country names/demonyms (Argentina/Argentine/AR as a bare identifier fragment, and the full ISO-3166 country name list) as identifiers or string literals; whitelist only `MarketCatalogSeeder.cs` and i18n resource files (`*.json` under locale folders). Assert this test is RED against a deliberately seeded violation fixture first (design §16 Guard row), then green against the real (clean) tree. (`Incoders.Hive.Tests/Architecture/NicheDiscoveryCountryNameGuardTests.cs`) — implemented as `NicheDiscoveryCountryNameGuardTests` with a representative (not exhaustive ISO-3166) country/demonym list, extend as new markets are seeded.
- [x] 2.8 Confirm the guard passes against WU-1/WU-2's actual shipped files (it will trivially pass now since nothing built yet references a country by name; re-run this exact test at the end of every subsequent work unit as a cheap regression check — call it out in each WU's gate step below).
- [x] 2.9 Run `dotnet test --filter FullyQualifiedName~NicheDiscovery`, then the full suite.
- [x] 2.10 `pwsh -File scripts/gates/Run-Gates.ps1` green. Commit: `feat(niche-discovery): add taxonomy hierarchy and market catalog domain model`.

---

## WU-3 — Application Ports, Multi-Origin Source-Chain Contract, Discovery Budget

*Refs: ND-03, ND-04, ND-05 (nullable contract shape), ND-17 (seam contract). Design §2.2, §5.1, §5.3 (budget
moved in per §18.2), §12.*

- [x] 3.1 **[RED]** Add `NicheDiscoverySourceResultTests.cs` — `NicheSourceOutcome{Ran,Unconfigured,Failed}`; `Ran` may carry empty evidence (success, not failure); `Unconfigured`/`Failed` carry empty evidence lists by construction. (`Incoders.Hive.Tests/Application/Abstractions/Social/NicheDiscovery/NicheDiscoverySourceResultTests.cs`)
- [x] 3.2 **[GREEN]** Create `ChannelEvidence.cs`, `VideoEvidence.cs` (nullable metrics — `long? Views`, etc., never zero-filled, ND-05), `EvidenceSet.cs`, `NicheDiscoveryQuery.cs`, `NicheDiscoverySourceResult.cs`, `NicheSourceOutcome.cs`, `BudgetSpend.cs`, `PartialReason.cs` (`BudgetExhausted|ProviderBudgetExhausted`). (`Application/Abstractions/Social/NicheDiscovery/*.cs`)
- [x] 3.3 **[RED]** Add `INicheDiscoverySourceContractTests.cs` — interface shape assertions via reflection: `SourceKey` is a provider key never an actor/endpoint id (naming convention check), `Supports(DiscoveryStage)`, `DiscoverAsync` returns `Task<NicheDiscoverySourceResult>`. (`Incoders.Hive.Tests/Application/Abstractions/Social/NicheDiscovery/INicheDiscoverySourceContractTests.cs`)
- [x] 3.4 **[GREEN]** Create `INicheDiscoverySource.cs`. (`Application/Abstractions/Social/NicheDiscovery/INicheDiscoverySource.cs`)
- [x] 3.5 **[RED]** Add `DiscoveryBudgetTests.cs` (moved in from WU-5, design §18.2) — `DiscoveryBudget(ProviderUnits, AiCompletions)` per the depth ceiling table (§5.3: Quick 400/5, Standard 1,000/15, Deep 2,500/40); `IDiscoveryBudgetLedger` decrements before each spend and reports when the remaining units cannot cover the next call. (`Incoders.Hive.Tests/Application/Abstractions/Social/NicheDiscovery/DiscoveryBudgetTests.cs`) — exercised via an in-file fake ledger since the concrete implementation lands with the adapter in WU-5.
- [x] 3.6 **[GREEN]** Create `DiscoveryBudget.cs`, `IDiscoveryBudgetLedger.cs` (run-scoped, in-memory, pure counter contract — implementation lands with the adapter in WU-5). (`Application/Abstractions/Social/NicheDiscovery/{DiscoveryBudget,IDiscoveryBudgetLedger}.cs`)
- [x] 3.7 **[GREEN]** Create the remaining repository/probe/queue/seam ports (no behavioural test needed for bare interfaces; each gets its exercising test when a real implementation lands): `IDiscoveryRunRepository.cs`, `INicheOpportunityRepository.cs`, `INicheDiscoveryEvidenceRepository.cs`, `IMarketCatalogRepository.cs`, `INicheDiscoveryOriginProbe.cs`, `INicheDiscoveryRunQueue.cs`. (`Application/Abstractions/Social/NicheDiscovery/*.cs`)
- [x] 3.8 **[RED]** Add `INicheOpportunityActivationSourceSeamTests.cs` — asserts the interface and `ActivatableContentPattern` compile with the exact shape from design §12, and (namespace-scan) that neither this file nor any other file under `Application.Abstractions.Social.NicheDiscovery` references a `ContentGeneration` type. (`Incoders.Hive.Tests/Application/Abstractions/Social/NicheDiscovery/INicheOpportunityActivationSourceSeamTests.cs`)
- [x] 3.9 **[GREEN]** Create `INicheOpportunityActivationSource.cs` + `ActivatableContentPattern.cs` — **no implementation, no DI registration** in this work unit (composition test that asserts zero registrations lands in WU-11b, once the container exists to test against). (`Application/Abstractions/Social/NicheDiscovery/{INicheOpportunityActivationSource,ActivatableContentPattern}.cs`)
- [x] 3.10 Run `dotnet test --filter FullyQualifiedName~Application.Abstractions.Social.NicheDiscovery`, then the full suite. Re-run the WU-2.7 country-name guard.
- [x] 3.11 `pwsh -File scripts/gates/Run-Gates.ps1` green. Commit: `feat(niche-discovery): add NicheDiscovery application ports and discovery budget contracts`.

---

## WU-4 — Integrations: Official-API Provider Category, Credential, Catalog, Checklist Group

*Refs: INT-ND-01, INT-ND-02, INT-ND-03, INT-ND-04. Design §5.2, N12. Flagged: verify the frontend integrations
category renderer before assuming ~750 lines holds — if it needs a new UI branch for the category, split into
4a (backend) / 4b (frontend) rather than absorbing the overrun silently.*

- [x] 4.1 **[RED]** Add `IntegrationProviderCategoryTests.cs` (extend existing) — `OfficialDataApi = 5` is **appended**, not renumbered; existing category values are unchanged (regression, since the enum is persisted as an int). (`Incoders.Hive.Tests/Domain/Integrations/IntegrationProviderCategoryTests.cs`)
- [x] 4.2 **[GREEN]** Append `OfficialDataApi = 5` to `IntegrationProviderCategory`. (`Domain/Integrations/IntegrationProviderCategory.cs`)
- [x] 4.3 **[RED]** Extend `IntegrationCatalogSeederTests.cs`/equivalent — `youtube-data-api-v3` seeds as a distinct provider from the existing `youtube` publishing/scrape row (INT-ND-02); its credential schema is a single API-key field, reaching `Connected` once present and validated, without an `actor_mappings_json` entry (INT-ND-01 scenario 1); missing key leaves it `NotConfigured` (INT-ND-01 scenario 2). (`Incoders.Hive.Tests/Application/Integrations/IntegrationCatalogSeederYouTubeDataApiTests.cs`)
- [x] 4.4 **[GREEN]** Add the `youtube-data-api-v3` catalog entry + API-key credential schema handling in `IntegrationCatalogSeeder`/`IntegrationResponseFactory` (per design §5.2 — reuses the existing per-organization credential storage path, no new secret store). (`Application/Features/Integrations/*.cs`)
- [x] 4.5 **[RED]** Extend `WorkflowConfigurationCatalogTests.cs` — new top-level workflow key `niche-discovery` → analysis type `market-opportunity` → 4 steps (one per `DiscoveryStage`), each `RequiresNetwork: true`, `SupportedNetworkKeys: ["youtube"]`, `AllowedProviderCategories: [OfficialDataApi, DataExtraction]`. (`Incoders.Hive.Tests/Application/Integrations/WorkflowConfigurationCatalogNicheDiscoveryStepTests.cs`)
- [x] 4.6 **[GREEN]** Register the `niche-discovery`/`market-opportunity`/4-step block in `WorkflowConfigurationCatalog`. (`Application/Features/Integrations/WorkflowConfigurations/WorkflowConfigurationCatalog.cs`)
- [x] 4.7 **[RED]** Extend `OrganizationIntegrationsFlowTests.cs` — a **third** checklist group `("niche-discovery", "Niche Discovery", nicheDiscoveryChecklist)` alongside `single-analyzer`/`generator`, with items "Connect a YouTube Data API key" and "Enable a discovery origin for channel search", each satisfied by `INicheDiscoveryOriginProbe` (INT-ND-03 scenario 1); a workflow key with no matching checklist group is a documented failure mode of this requirement, not an acceptable gap (INT-ND-03 scenario 2 — assert the group's presence directly, do not just assert absence-of-crash). (`Incoders.Hive.Tests/Application/Integrations/OrganizationIntegrationsFlowTests.cs`)
- [x] 4.8 **[GREEN]** Add the third checklist group to `GetOrganizationIntegrationsHandler` (`INicheDiscoveryOriginProbe` real implementation stubbed to always report unsatisfied until WU-5 lands; a satisfied-path test is added once the probe has a real implementation in WU-5). (`Application/Features/Integrations/GetOverview/GetOrganizationIntegrationsHandler.cs`)
- [x] 4.9 **Checkpoint before continuing**: inspect `apps/web/lib/api/integrations.ts` and the settings integrations page for how existing categories render. If `OfficialDataApi` needs a new branch (icon, credential form shape, category label), implement it here with its own RED test (`apps/web/lib/social/integrations/*.test.ts` or component test) and note the actual line count against the ~750 estimate in the WU-4 commit message; if the existing category rendering is generic/data-driven and needs no new branch, note that explicitly instead.
- [x] 4.10 Run `dotnet test --filter FullyQualifiedName~(IntegrationProviderCategory|WorkflowConfigurationCatalog|OrganizationIntegrationsFlow)`, then the full suite; run the frontend test command if 4.9 touched frontend code.
- [x] 4.11 `pwsh -File scripts/gates/Run-Gates.ps1` green. Commit: `feat(integrations): add official-API provider category and niche-discovery workflow checklist group`.

---

## WU-5 — Infrastructure: YouTube Data API v3 Origin Adapter and Normalization Boundary

*Refs: ND-03, ND-04, ND-05, INT-ND-04. Design §2.4, §5.2, §5.3, §5.4. Fixture-only until the Google Cloud
prerequisite exists (see Blocked/prerequisite section above).*

- [x] 5.1 **[RED]** Extend `NoActorLiteralGuardTests.GuardedRelativePaths` with the three new origin files
      (`YouTubeDataApiNicheDiscoverySource.cs`, `YouTubeDataApiClient.cs`, `NicheDiscoveryOriginConfiguration.cs`)
      **before** those files exist, so the guard is meaningful from the first line written. (`Incoders.Hive.Tests/Architecture/NoActorLiteralGuardTests.cs`)
- [x] 5.2 **[RED]** Add `NicheDiscoveryOriginConfigurationTests.cs` — enabled workflow-step route wins first; raw `actor_mappings_json` entry second (Apify origin only, not exercised here since Apify is out of scope); empty → `Unconfigured`, never a literal or a throw; blank `IOptions<NicheDiscoveryOriginOptions>` base address → `Unconfigured` (INT-ND-04). Fake `IWorkflowConfigurationResolver`, mirrors `ApifyNicheSearchResolverTests`. (`Incoders.Hive.Tests/Infrastructure/Social/NicheDiscovery/NicheDiscoveryOriginConfigurationTests.cs`)
- [x] 5.3 **[GREEN]** Create `NicheDiscoveryOriginConfiguration.cs`, `NicheDiscoveryOriginOptions.cs`. (`Infrastructure/Social/NicheDiscovery/NicheDiscoveryOriginConfiguration.cs`)
- [x] 5.4 **[RED]** Add `YouTubeEvidenceMapperTests.cs` — against a **recorded fixture** `Fixtures/youtube-niche-search.json` (capability-named, no live call): unmappable item skipped, never defaulted; zero-of-many mapped from a non-empty response → the source reports `Failed`; partial → `Ran` with the skipped count logged; absent `statistics.viewCount` → `null`, never `0` (ND-05). (`Incoders.Hive.Tests/Infrastructure/Social/NicheDiscovery/YouTubeEvidenceMapperTests.cs`, `Incoders.Hive.Tests/Infrastructure/Social/Fixtures/youtube-niche-search.json`)
- [x] 5.5 **[GREEN]** Create `YouTubeEvidenceMapper.cs` — the only type that sees `JsonElement` for this origin (ND-04). (`Infrastructure/Social/NicheDiscovery/Origins/YouTubeEvidenceMapper.cs`)
- [x] 5.6 **[RED]** Add `YouTubeQuotaBudgetTests.cs` — depth → ceiling table (§5.3: Quick 400/Standard 1,000/Deep 2,500 provider units); stops before an uncoverable call; a `403 quotaExceeded` with zero results maps to `Failed` (`FailureReasonKey = "social.niche_discovery.provider_quota_exhausted"`), with partial results maps to `Ran` + `PartialReason.ProviderBudgetExhausted` (N9). Counting fake HTTP handler. (`Incoders.Hive.Tests/Infrastructure/Social/NicheDiscovery/YouTubeQuotaBudgetTests.cs`)
- [x] 5.7 **[GREEN]** Create `YouTubeQuotaBudget.cs` implementing `IDiscoveryBudgetLedger` for this origin, and `YouTubeDataApiClient.cs` (named `IHttpClientFactory` client, search-sparingly-batch-everything strategy per §5.3). (`Infrastructure/Social/NicheDiscovery/Origins/{YouTubeQuotaBudget,YouTubeDataApiClient}.cs`)
- [x] 5.8 **[RED]** Add `YouTubeDataApiNicheDiscoverySourceTests.cs` — `Supports(stage)` per `DiscoveryStage`; `DiscoverAsync` composes client + mapper + budget; `SourceKey = "youtube-data-api"` (a provider key, never an actor/endpoint literal, ND-03). (`Incoders.Hive.Tests/Infrastructure/Social/NicheDiscovery/YouTubeDataApiNicheDiscoverySourceTests.cs`)
- [x] 5.9 **[GREEN]** Create `YouTubeDataApiNicheDiscoverySource.cs` implementing `INicheDiscoverySource`. (`Infrastructure/Social/NicheDiscovery/Origins/YouTubeDataApiNicheDiscoverySource.cs`)
- [x] 5.10 **[RED]** Add `NicheDiscoveryOriginProbeTests.cs` — reports satisfied once the API key is present AND at least one stage route resolves; wire the real implementation into WU-4.8's checklist group (extend `OrganizationIntegrationsFlowTests.cs` with the now-satisfiable path). (`Incoders.Hive.Tests/Infrastructure/Social/NicheDiscovery/NicheDiscoveryOriginProbeTests.cs`)
- [x] 5.11 **[GREEN]** Create `NicheDiscoveryOriginProbe.cs` implementing `INicheDiscoveryOriginProbe`. (`Infrastructure/Social/NicheDiscovery/NicheDiscoveryOriginProbe.cs`)
- [x] 5.12 **Fixture-only note**: 5.4/5.6/5.8 run entirely against the recorded fixture and counting fakes. No live YouTube call is made. Mark `YouTubeEvidenceMapper` and the fixture "PENDING LIVE VERIFICATION" in a code comment, mirroring ADR-0012's precedent for the Apify actor spike.
- [x] 5.13 Run `dotnet test --filter FullyQualifiedName~Infrastructure.Social.NicheDiscovery`, then the full suite. Re-run the WU-2.7 country-name guard and the extended no-actor-literal guard (5.1).
- [x] 5.14 `pwsh -File scripts/gates/Run-Gates.ps1` green. Commit: `feat(niche-discovery): add YouTube Data API v3 origin adapter and normalization boundary`.

---

## WU-6 — Deterministic Analytics

*Refs: ND-05, ND-06. Design §2.3, §6.*

- [x] 6.1 **[RED]** Add `MetricFromSingleCallerGuardTests.cs` — reflection guard (a): `Metric.From` has exactly one calling type (`DeterministicMetricCalculator`) across the `Incoders.Hive.Application` assembly. Written before `DeterministicMetricCalculator` exists, so it is vacuously inapplicable until 6.3 lands it — confirm it turns meaningfully green (not just "no callers found") once 6.3 lands. (`Incoders.Hive.Tests/Architecture/NicheDiscoveryMetricFactoryGuardTests.cs`)
- [x] 6.2 **[RED]** Add `DeterministicMetricCalculatorTests.cs` — table tests for every formula and null-propagation row in design §6: `ChannelMedianViews`, `OutlierRatio`, `OutlierDensity`, `VelocityProxy`, `GrowthProxy`, `CrossChannelRepetition` (K=3 floor, `0` is available not missing), `EvergreenShare`; fixed `nowUtc` parameter; asserts `null` never becomes `0` anywhere; tie-breaking on `ExternalRef` ordinal is a total order independent of input order. (`Incoders.Hive.Tests/Application/Features/Social/NicheDiscovery/Analytics/DeterministicMetricCalculatorTests.cs`)
- [x] 6.3 **[GREEN]** Create `Metric.cs` (private ctor, single `internal static From` factory), `MetricKey.cs`, `MetricSet.cs`, `SampleShape.cs`, `DeterministicMetricCalculator.cs` — pure, I/O-free, `nowUtc` as an explicit parameter. (`Application/Features/Social/NicheDiscovery/Analytics/{Metric,MetricKey,MetricSet,SampleShape,DeterministicMetricCalculator}.cs`)
- [x] 6.4 Run `dotnet test --filter FullyQualifiedName~Application.Features.Social.NicheDiscovery.Analytics`, then the full suite, confirming 6.1 is now meaningfully green.
- [x] 6.5 `pwsh -File scripts/gates/Run-Gates.ps1` green. Commit: `feat(niche-discovery): add deterministic metric calculator and analytics guard`.

---

## WU-7a — Persistence: Schema and Migration

*Refs: ND-11, ND-16. Design §2.4, §9. Split from repositories per design §18.1 (default, not fallback).*

- [x] 7a.1 **[GREEN]** Create `DiscoveryRunData.cs`, `NicheOpportunityData.cs`, `NicheDiscoveryEvidenceData.cs`, `MarketCatalogData.cs` entities exactly per the column list in design §9 (schema `social`, snake_case, `organization_id` cascade-delete FK on the first three; `market_catalog` is platform-level, no `organization_id`). (`Infrastructure/Persistence/Postgres/Entities/*.cs`)
- [x] 7a.2 **[GREEN]** Map all four entities in `AppIdentityDbContext.cs`: `ix_discovery_runs_org_created`, unique `ux_niche_opportunities_run_rank`, `ix_niche_opportunities_org_created`, unique `ux_niche_discovery_evidence_run_kind_ref`, unique `ux_market_catalog_code`. (`Infrastructure/Identity/AppIdentityDbContext.cs`)
- [x] 7a.3 Generate migration `Add_NicheDiscoveryRunsOpportunitiesAndEvidence` via `dotnet ef migrations add` (produces `.Designer.cs` + updated `AppIdentityDbContextModelSnapshot.cs` — the generated-diff lines tracked separately from the ~450 authored). (`Infrastructure/Persistence/Postgres/Migrations/*`)
- [x] 7a.4 **Gate**: run `dotnet ef migrations has-pending-model-changes` (the `migration-drift` gate) — MUST be clean. This is a distinct, explicit task, not folded into 7a.6, because a drifted model snapshot is the single most common silent regression in this class of change.
- [x] 7a.5 **[GREEN]** Create `MarketCatalogSeeder.cs` — deterministic GUIDs, upsert-on-missing-only, one row `es-AR` (mirrors `NicheCatalogSeeder`). Wire into `Program.cs` startup seeding. (`Infrastructure/Social/NicheDiscovery/MarketCatalogSeeder.cs`)
- [x] 7a.6 **[RED]** Add `MarketCatalogSeederTests.cs` — idempotent re-run does not duplicate the `es-AR` row; deterministic GUID is stable across runs. (`Incoders.Hive.Tests/Infrastructure/Social/NicheDiscovery/MarketCatalogSeederTests.cs`)
- [x] 7a.7 Run `dotnet test --filter FullyQualifiedName~NicheDiscovery`, then the full suite.
- [x] 7a.8 `pwsh -File scripts/gates/Run-Gates.ps1` green (`migration-drift` gate included). Commit: `feat(niche-discovery): add persistence schema, migration, and market catalog seeder`.

---

## WU-7b — Persistence: Repositories

*Refs: ND-11, ND-16. Design §2.4, §9. Depends on WU-7a.*

- [x] 7b.1 **[RED]** Add `EfDiscoveryRunRepositoryTests.cs` — round-trip; status-lifecycle UPDATE path (the one table that updates); tenant scoping (cross-org read returns nothing, ND-16). (`Incoders.Hive.Tests/Infrastructure/Persistence/Postgres/Repositories/EfDiscoveryRunRepositoryTests.cs`)
- [x] 7b.2 **[GREEN]** Create `IDiscoveryRunRepository` (already declared in WU-3.7) implementation `EfDiscoveryRunRepository.cs`. (`Infrastructure/Persistence/Postgres/Repositories/EfDiscoveryRunRepository.cs`)
- [x] 7b.3 **[RED]** Add `EfNicheOpportunityRepositoryTests.cs` — `AppendAsync` round-trip; `ux_niche_opportunities_run_rank` rejects a double-write inside a run as a hard error, mirroring `ux_discovered_candidates_key_observed`; no update/delete method exists on the port (compile-time assertion via reflection over the interface's member set); a later run creates new rows, earlier rows unchanged (ND-11 scenario 2); cascade delete from the organization removes opportunities. (`Incoders.Hive.Tests/Infrastructure/Persistence/Postgres/Repositories/EfNicheOpportunityRepositoryTests.cs`)
- [x] 7b.4 **[GREEN]** Create `EfNicheOpportunityRepository.cs` (`AppendAsync` + readers only). (`Infrastructure/Persistence/Postgres/Repositories/EfNicheOpportunityRepository.cs`)
- [x] 7b.5 **[RED]** Add `EfNicheDiscoveryEvidenceRepositoryTests.cs` — `AppendAsync` round-trip; unique `(run_id, evidence_kind, external_ref)` rejects a double-write; navigable — evidence links back to `opportunity_id`. (`Incoders.Hive.Tests/Infrastructure/Persistence/Postgres/Repositories/EfNicheDiscoveryEvidenceRepositoryTests.cs`)
- [x] 7b.6 **[GREEN]** Create `EfNicheDiscoveryEvidenceRepository.cs`. (`Infrastructure/Persistence/Postgres/Repositories/EfNicheDiscoveryEvidenceRepository.cs`)
- [x] 7b.7 **[RED]** Add `EfMarketCatalogRepositoryTests.cs` — read-only listing of active markets; `es-AR` present after seeding. (`Incoders.Hive.Tests/Infrastructure/Persistence/Postgres/Repositories/EfMarketCatalogRepositoryTests.cs`)
- [x] 7b.8 **[GREEN]** Create `EfMarketCatalogRepository.cs`. (`Infrastructure/Persistence/Postgres/Repositories/EfMarketCatalogRepository.cs`)
- [x] 7b.9 Register all four repositories in Infrastructure DI. (`Infrastructure/DependencyInjection/DependencyInjection.cs`)
- [x] 7b.10 Run `dotnet test --filter FullyQualifiedName~Repositories`, then the full suite.
- [x] 7b.11 `pwsh -File scripts/gates/Run-Gates.ps1` green. Commit: `feat(niche-discovery): add EF repositories for runs, opportunities, evidence, and market catalog`.

---

## WU-8 — Opportunity Scorer and Independent Confidence Scorer

*Refs: ND-09, ND-10. Design §2.3, §7.1, §7.2.*

- [x] 8.1 **[RED]** Add `AvailabilityWeightedAggregateTests.cs` — renormalising denominator; degenerate set (all signals unavailable) → `0.5`; determinism; parity test against a hand-computed renormalised expectation (design §7.1 — the duplication-cannot-drift-silently test). (`Incoders.Hive.Tests/Application/Features/Social/NicheDiscovery/Scoring/AvailabilityWeightedAggregateTests.cs`)
- [x] 8.2 **[GREEN]** Create `AvailabilityWeightedAggregate.cs` (~25 lines, re-declared not imported per N2). (`Application/Features/Social/NicheDiscovery/Scoring/AvailabilityWeightedAggregate.cs`)
- [x] 8.3 **[RED]** Add `NicheOpportunityScorerTests.cs` — full weight table (§7.1); the `CrossPlatformValidation` criterion directly from the proposal's success criteria: a fixture with the signal `NotSupportedYet` ranks identically to the same fixture with the weight removed by hand (ND-09 scenario 2); `excludedSignals`/`effectiveWeightDenominator` surfaced correctly. (`Incoders.Hive.Tests/Application/Features/Social/NicheDiscovery/Scoring/NicheOpportunityScorerTests.cs`)
- [x] 8.4 **[GREEN]** Create `OpportunityScoreWeights.cs` (`Default`, snapshot-per-run shape) and `NicheOpportunityScorer.cs`. (`Application/Features/Social/NicheDiscovery/Scoring/{OpportunityScoreWeights,NicheOpportunityScorer}.cs`)
- [x] 8.5 **[RED]** Add `ConfidenceScorerIndependenceTests.cs` — the two mirrored fixtures from design §7.2: scaling every `Views` value ×10 leaves `ConfidenceScore` bit-identical while `OpportunityScore` changes; doubling channel/video counts with identical distributions leaves `OpportunityScore` unchanged while `ConfidenceScore` changes; the 94/37 and 88/95 fixture pair renders both scores, never averaged (ND-10). (`Incoders.Hive.Tests/Application/Features/Social/NicheDiscovery/Scoring/ConfidenceScorerIndependenceTests.cs`)
- [x] 8.6 **[GREEN]** Create `ConfidenceScorer.cs` over `SampleShape` (min-max normalised, equal-weight average). (`Application/Features/Social/NicheDiscovery/Scoring/ConfidenceScorer.cs`)
- [x] 8.7 **[RED]** Add `ScoringDoesNotReferenceSemanticsGuardTests.cs` — reflection guard (c): no method on any type in `…NicheDiscovery.Scoring` or `…NicheDiscovery.Analytics` has a parameter or return type declared in `…NicheDiscovery.Semantics`. This is meaningful now even though the `Semantics` namespace does not exist yet (an empty/nonexistent namespace trivially satisfies "no reference to it"); it MUST be re-run and re-verified once WU-10 lands the `Semantics` types, without modification — that re-run is called out explicitly in WU-10. (`Incoders.Hive.Tests/Architecture/NicheDiscoveryScoringSemanticsGuardTests.cs`)
- [x] 8.8 Run `dotnet test --filter FullyQualifiedName~Application.Features.Social.NicheDiscovery.Scoring`, then the full suite. Re-run the WU-6.1 metric-factory guard and the WU-2.7 country-name guard.
- [x] 8.9 `pwsh -File scripts/gates/Run-Gates.ps1` green. Commit: `feat(niche-discovery): add opportunity scorer and independent confidence scorer`.

---

## WU-9 — Market Comparison

*Refs: ND-08, ND-14. Design §2.3, §4. **Depends on WU-6** (`TargetMarketDemand` consumes `VelocityProxy`) — do
not schedule before WU-6, per the ordering correction in design §18.*

- [x] 9.1 **Decision checkpoint**: confirm with the owner, before writing 9.2, whether `TargetMarketCompetition`'s
      partition is content locale (assumed, per design §4) or audience geography, per the decision checkpoint
      carried forward at the top of this document. Record the answer in the WU-9 commit message. **Resolved**: content
      language + region (locale), not audience geography — recorded verbatim in commit `cecc28d`.
- [x] 9.2 **[RED]** Add `TargetMarketEvaluatorTests.cs` — fixture with two languages: `TargetMarketDemand` = median `VelocityProxy` of window videos matching `target.Language`, normalized within the run set; `TargetMarketCompetition` = distinct channels publishing the same `ContentPatternKind` in `target.Language`, weighted by subscriber decile; `TargetMarketGap = Demand × (1 − Competition)`, `NotReported` when either input is null; all four values (traction, demand, competition, gap) individually inspectable (ND-08 scenario). (`Incoders.Hive.Tests/Application/Features/Social/NicheDiscovery/MarketComparison/TargetMarketEvaluatorTests.cs`)
- [x] 9.3 **[GREEN]** Create `TargetMarketDemand.cs`, `TargetMarketCompetition.cs`, `TargetMarketGap.cs`, `TargetMarketEvaluator.cs`. (`Application/Features/Social/NicheDiscovery/MarketComparison/*.cs`)
- [x] 9.4 Run `dotnet test --filter FullyQualifiedName~MarketComparison`, then the full suite.
- [x] 9.5 `pwsh -File scripts/gates/Run-Gates.ps1` green. Commit: `feat(niche-discovery): add target market demand, competition, and gap evaluation`.

---

## WU-10 — Semantic Layer

*Refs: ND-07. Design §2.3, §8. Guard (b) lands here.*

- [x] 10.1 **[RED]** Add `SemanticsNoNumericMemberGuardTests.cs` — reflection guard (b): every public property of every type in `…NicheDiscovery.Semantics` is `string`, `bool`, an `enum`, or a collection of those — no numeric type anywhere. Written before `SemanticAnnotation`/`ClusterProposal` exist (assert it fails to compile/is vacuous until 10.3 adds real types, matching design §16's "RED before the semantic layer exists"). (`Incoders.Hive.Tests/Architecture/NicheDiscoverySemanticsNoNumericGuardTests.cs`)
- [x] 10.2 **[RED]** Add `SemanticAnnotatorTests.cs` — gateway success path: `IAiCompletionGateway.CompleteJsonAsync<SemanticAnnotation>` with `CapabilityKey = "ai-analysis"` and the five prompt version keys from design §8; within-run memoisation by SHA-256 of the canonical JSON payload (N10) so an identical input is never re-annotated twice in one run. Fake `IAiCompletionGateway`. (`Incoders.Hive.Tests/Application/Features/Social/NicheDiscovery/Semantics/SemanticAnnotatorTests.cs`)
- [x] 10.3 **[GREEN]** Create `SemanticAnnotation.cs`, `ClusterProposal.cs`, `FormatTrait.cs` (closed enum), `SemanticAnnotator.cs`. Confirm 10.1 is now meaningfully green (real types scanned, zero numeric members found). **Flagged deviation**: today's `SemanticAnnotator` issues ONE consolidated gateway call on `ClusterPromptVersionKey`; the other four design §8 prompt-version keys are reserved as named constants (asserted present by test), not yet wired to separate staged calls — see the class doc for the reasoning.
- [x] 10.4 **[RED]** Add `LexicalClusterFallbackTests.cs` — gateway failure or unparsable payload triggers the fallback: tokenise title+description, drop <4-char tokens and an ES+EN stopword list, group by shared token across ≥K=3 distinct channels (same idiom as `CoOccurrenceSignal`); cluster name = top token; `ContentPatternKind.Unclassified`; empty `FormatTrait[]` ⇒ `AutomationSuitability` signal → `NotReported` (N11 scenario). (`Incoders.Hive.Tests/Application/Features/Social/NicheDiscovery/Semantics/LexicalClusterFallbackTests.cs`)
- [x] 10.5 **[GREEN]** Create `LexicalClusterFallback.cs`; wire `SemanticAnnotator` to fall back on gateway failure/unparsable payload and to flag the run `semantic_degraded = true` (returned as part of its result contract, actually persisted onto `discovery_runs.semantic_degraded` in WU-11a's processor). (`Application/Features/Social/NicheDiscovery/Semantics/LexicalClusterFallback.cs`)
- [x] 10.6 **[RED]** Add `NicheDiscoverySemanticsIsolationGuardTests.cs` — repeats the WU-8.7 guard (c) against the now-real `Semantics` namespace: no `…Scoring`/`…Analytics` signature references any `…Semantics` type, confirming the earlier assertion still holds now that there is something to violate it with. (`Incoders.Hive.Tests/Architecture/NicheDiscoveryScoringSemanticsGuardTests.cs` — extend the WU-8.7 file, do not duplicate it)
- [x] 10.7 Run `dotnet test --filter FullyQualifiedName~Application.Features.Social.NicheDiscovery.Semantics`, then the full suite.
- [x] 10.8 `pwsh -File scripts/gates/Run-Gates.ps1` green. Commit: `feat(niche-discovery): add semantic annotation layer with lexical fallback`. Real diff was 701 lines — under the 800-line budget, landed as a single commit (no 10a/10b split needed).

---

## WU-11a — Run Orchestrator, Queue, Background Processing, "Second Market" Criterion

*Refs: ND-02, ND-03, ND-14 (criterion). Design §2.3, §2.4, §4.*

- [x] 11a.1 **[RED]** Add `NicheDiscoverySourceChainTests.cs` — per-stage origin selection via `Supports(stage)`; `Unconfigured` vs `Failed` vs `Ran`-with-zero are three disjoint reports; budget threading through `IDiscoveryBudgetLedger`; one failing/unconfigured origin does not empty the run when another configured origin can still contribute (ND-03). Fake sources, mirrors `ContentIdeaChainTests`. (`Incoders.Hive.Tests/Application/Features/Social/NicheDiscovery/DiscoveryRuns/NicheDiscoverySourceChainTests.cs`)
- [x] 11a.2 **[GREEN]** Create `NicheDiscoverySourceChain.cs` (ordered, partial-tolerant). (`Application/Features/Social/NicheDiscovery/DiscoveryRuns/NicheDiscoverySourceChain.cs`)
- [x] 11a.3 **[RED]** Add `NicheDiscoveryRunProcessorTests.cs` — end-to-end over a fixture: sourcing → analytics → semantics (fake gateway) → market comparison → scoring → append-only persistence (fake repositories); status transitions correctly through the lifecycle (ND-02); quota/depth exhaustion mid-run transitions to `Failed` with a structured reason (ND-02 scenario 2); an unconfigured origin is reported distinctly from an empty result (ND-02 scenario 1). (`Incoders.Hive.Tests/Application/Features/Social/NicheDiscovery/DiscoveryRuns/NicheDiscoveryRunProcessorTests.cs`)
- [x] 11a.4 **[GREEN]** Create `NicheDiscoveryRunProcessor.cs` composing WU-3/5/6/7b/8/9/10/11a.2 per the pipeline diagram in design §1. (`Application/Features/Social/NicheDiscovery/DiscoveryRuns/NicheDiscoveryRunProcessor.cs`)
- [x] 11a.5 **[RED — the "second market needs no code change" criterion, ND-14]** Add `SecondTargetMarketNoCodeChangeTests.cs` — seed a second market row (`pt-BR`) into an in-memory catalog fake; run `NicheDiscoveryRunProcessor` end-to-end over the **same recorded evidence fixture** used for `es-AR`, with `targetMarket = pt-BR`; assert a non-empty ranked result; assert (via a source-tree scan, same technique as the WU-2.7 guard) that no per-market file exists anywhere under `Domain/Social/NicheDiscovery`, `Application/**/NicheDiscovery`, `Infrastructure/**/NicheDiscovery` — i.e. the assertion is literally "one code path, two rows" (design §4, §16 Criterion row). (`Incoders.Hive.Tests/Application/Features/Social/NicheDiscovery/DiscoveryRuns/SecondTargetMarketNoCodeChangeTests.cs`)
- [x] 11a.6 **[RED]** Add `NicheDiscoveryRunQueueTests.cs`/`NicheDiscoveryRunBackgroundServiceTests.cs` — enqueue → the hosted service dequeues and invokes the processor; poll-friendly status projection matches the run's current `DiscoveryRunStatus`. (`Incoders.Hive.Tests/Infrastructure/Social/NicheDiscovery/{NicheDiscoveryRunQueueTests,NicheDiscoveryRunBackgroundServiceTests}.cs`)
- [x] 11a.7 **[GREEN]** Create `NicheDiscoveryRunQueue.cs` + `NicheDiscoveryRunBackgroundService.cs` (start-and-poll idiom, mirrors the single-analyzer queue/hosted-service pair). (`Infrastructure/Social/NicheDiscovery/{NicheDiscoveryRunQueue,NicheDiscoveryRunBackgroundService}.cs`)
- [x] 11a.8 Run `dotnet test --filter FullyQualifiedName~NicheDiscovery.DiscoveryRuns`, then the full suite.
- [x] 11a.9 `pwsh -File scripts/gates/Run-Gates.ps1` green. Commit: `feat(niche-discovery): add run orchestrator, queue, and background processing`.

---

## WU-11b — Endpoints and Composition Guards

*Refs: ND-02, ND-03, ND-17. Design §2.5, §10, §12, §16 (Composition row).*

- [x] 11b.1 **[RED]** Add `StartDiscoveryRunCommandTests.cs`, `GetDiscoveryRunQueryTests.cs`, `ListRunOpportunitiesQueryTests.cs`, `GetOpportunityDetailQueryTests.cs` — validation error codes per design §10 (`market_unknown`, `window_out_of_range`, `depth_unknown`, `content_format_invalid`, `network_unsupported`, `run_not_found`, `run_already_running`, `opportunity_not_found`); happy path enqueues via `INicheDiscoveryRunQueue` and returns `202 { runId, status }`. (`Incoders.Hive.Tests/Application/Features/Social/NicheDiscovery/DiscoveryRuns/*Tests.cs`)
- [x] 11b.2 **[GREEN]** Create the four command/query + handler pairs. (`Application/Features/Social/NicheDiscovery/DiscoveryRuns/*.cs`)
- [x] 11b.3 **[RED]** Add `MapNicheDiscoveryEndpointsTests.cs` — the 5 routes from design §10 map to the correct handlers and status codes, including the `GET /markets` catalog listing (ND-14 — the market selector is data-driven by construction). (`Incoders.Hive.Tests/ApiService/Endpoints/Social/MapNicheDiscoveryEndpointsTests.cs`)
- [x] 11b.4 **[GREEN]** Create `MapNicheDiscoveryEndpoints.cs`. (`ApiService/Endpoints/Social/MapNicheDiscoveryEndpoints.cs`)
- [x] 11b.5 **[RED]** Add `NicheDiscoveryCompositionTests.cs` — build the real container via `AddInfrastructure`; **write the count assertion first** (design §16 — "the count assertion is the load-bearing one"): exactly one implementation resolves per NicheDiscovery port; `GetServices<INicheDiscoverySource>()` keys are distinct with distinct `Order`; `GetServices<INicheOpportunityActivationSource>()` is **empty** — asserting "named, not built" rather than trusting it (ND-17). (`Incoders.Hive.Tests/Infrastructure/DependencyInjection/NicheDiscoveryCompositionTests.cs`)
- [x] 11b.6 **[RED]** Add `NicheDiscoveryContentGenerationIsolationGuardTests.cs` — namespace-scan both directions: no type under `…NicheDiscovery.*` references any `ContentGeneration` type, and no type under `ContentGeneration.*` references any `NicheDiscovery` type (ND-17 scenario). (`Incoders.Hive.Tests/Architecture/NicheDiscoveryContentGenerationIsolationGuardTests.cs`)
- [x] 11b.7 Register the queue, background service, repositories (already registered in WU-7b), origin (WU-5), and handlers in DI; map the endpoints in `Program.cs`. (`Infrastructure/DependencyInjection/DependencyInjection.cs`, `ApiService/Program.cs`)
- [x] 11b.8 Run `dotnet test --filter FullyQualifiedName~(NicheDiscoveryCompositionTests|MapNicheDiscoveryEndpoints|NicheDiscoveryContentGenerationIsolationGuard)`, then the full suite. Re-run the WU-2.7 country-name guard, the WU-5.1 no-actor-literal guard, and the WU-8.7/10.6 semantics-isolation guard.
- [x] 11b.9 `pwsh -File scripts/gates/Run-Gates.ps1` green. Commit: `feat(niche-discovery): add run endpoints and composition guards`.

---

## WU-12 — Frontend: Viability Dashboard and Opportunity Detail

*Refs: ND-12, ND-13, ND-05 (frontend zero-fill last line of defence), ND-10 (never-averaged display). Design §11.*

- [x] 12.1 **[RED]** Add `apps/web/lib/social/niche-discovery/availability.test.ts` — `null → "unavailable"`, `0 → "0"` (never conflated). (`apps/web/lib/social/niche-discovery/availability.test.ts`)
- [x] 12.2 **[GREEN]** Create `availability.ts`. (`apps/web/lib/social/niche-discovery/availability.ts`)
- [x] 12.3 **[RED]** Add `ranking.test.ts` — sort comparators for all seven axes (opportunity, confidence, market, format, trend, competition, automation) (ND-12). (`apps/web/lib/social/niche-discovery/ranking.test.ts`)
- [x] 12.4 **[GREEN]** Create `ranking.ts`. (`apps/web/lib/social/niche-discovery/ranking.ts`)
- [x] 12.5 **[RED]** Add `filters.test.ts` — predicate builders (market, format, trend, competition band); filter-by-market + sort-by-opportunity-score integration scenario (ND-12 scenario). (`apps/web/lib/social/niche-discovery/filters.test.ts`)
- [x] 12.6 **[GREEN]** Create `filters.ts`. (`apps/web/lib/social/niche-discovery/filters.ts`)
- [x] 12.7 **[GREEN]** Create `lib/api/niche-discovery.ts` — thin client over the shared `apiRequest` helper, no decision logic. (`apps/web/lib/api/niche-discovery.ts`)
- [x] 12.8 **[RED]** Add a component/lib test asserting `score-pair-badge` renders `opportunity` and `confidence` as two distinct labelled values, with no code path that combines them into one number (ND-10). Implemented as `apps/web/lib/social/niche-discovery/score-pair.test.ts` over the pure `buildScorePair` decision function — no component-test harness exists in this repo (only `lib/**/*.test.ts` is wired into `npm run test`), matching the sibling's WU-7 test-infra note.
- [x] 12.9 **[GREEN]** Create `viability-dashboard-client.tsx`, `opportunity-detail.tsx`, `evidence-list.tsx`, `score-pair-badge.tsx`, `excluded-signals-note.tsx` (ND-13 — all fields from design §11 and spec ND-13 populated or explicitly marked unavailable; evidence links navigate to real channel/video URLs). (`apps/web/components/social/niche-discovery/*.tsx`)
- [x] 12.10 Run the frontend test command used by `scripts/gates/Run-Gates.ps1`, then `npx tsc --noEmit` and `npm run build`. All clean (323/323 frontend tests, tsc clean, build clean).
- [x] 12.11 `pwsh -File scripts/gates/Run-Gates.ps1` green. **Split as pre-agreed**: the real diff was ~1287 authored lines (over the 800 budget), so this landed as two commits — **12a** `43eb4e1` `feat(niche-discovery): add viability dashboard list (WU-12a)` (818 lines: availability/ranking/filters + api client + score-pair + dashboard component, plus a WU-11b DTO gap fix — `DiscoveryRunStatusResponse`/`NicheOpportunitySummary` were missing the market/format/competition/automation/trend fields the dashboard needs) and **12b** `373b9fe` `feat(niche-discovery): add opportunity detail view (WU-12b)` (469 lines: evidence-links, opportunity-narrative, and the three remaining detail components). Gap: no app-router page mounts these components yet — WU-12's task list names only the component files, not a route; the dashboard is implemented and unit-tested but not yet reachable from navigation.

---

## WU-13 — ADR 0013, PRD Section, Spec/Doc Sync

*Refs: repo `CLAUDE.md` docs-sync MUST; `openspec/config.yaml` `require_adr_for_architecture_changes` and
`require_prd_traceability`. Design §13.*

- [x] 13.1 Write `docs/architecture/adr/0013-niche-discovery-multi-origin-provider-and-opportunity-evidence-model.md` recording, verbatim per design §13: the vocabulary table; N1 (one multi-origin port, provider-named ports rejected); N2 (deliberate contract duplication over cross-capability import); N3 (the three structural AI-cannot-produce-a-number guards, cross-referencing the actual test files landed in WU-6.1, WU-8.7/10.6, WU-10.1); N5 (`DiscoveryRun` aggregate vs `NicheOpportunity` read model, with the revisit trigger); N6 (market as data, country-named identifiers forbidden); N7 (weights snapshotted per run, superseding ADR-0012 D7 for this capability only, with the reason); N8/N9 (three-valued availability; quota exhaustion semantics); N12 (new top-level workflow key plus its checklist group, and the trap it avoids); the `OfficialDataApi` category appended rather than renumbered; the locale-not-geography competition partition as a stated assumption (record whatever WU-9.1's decision checkpoint actually resolved); and the consequences (additive rollback, one reversible migration, config-removal disables discovery without a code change). Status: `Accepted — implemented`. Also records the WU-11a/11b/WU-12 gap fixes as consequences.
- [x] 13.2 Add a new numbered section to `docs/PRD.md` (§13), in the style of §11 Profile Analysis / §12 Content Bank Planner (the PRD's §6 MVP Success Path starts at "select an organization/client" and does not cover pre-client channel-launch discovery — this section closes that traceability gap honestly rather than stretching §1). Covers: the business problem, the vocabulary distinction from Content Discovery, the `DiscoveryRun` → `NicheOpportunity` outcome, the OpportunityScore/ConfidenceScore distinction, and the market-catalog-is-data model.
- [x] 13.3 Add an operator doc under `docs/operators/niche-discovery-setup.md`, mirroring `docs/operators/content-discovery-setup.md`, covering: obtaining a YouTube Data API v3 key, connecting it under Settings → Integrations, the depth/quota tradeoffs from design §5.3, reading the OpportunityScore/ConfidenceScore pair, and what `Failed` vs `PartiallyCompleted` means.
- [x] 13.4 **Deferred to `sdd-archive`**, per the sibling's WU-9.3 precedent. The `niche-discovery` capability spec and the `integrations` spec-delta remain unmerged under `openspec/changes/niche-discovery-engine/specs/`; `openspec/specs/` was not touched by this apply run.
- [x] 13.5 Update `openspec/changes/niche-discovery-engine/state.yaml` — `status: apply-complete`, `adr_status: accepted`, an `apply:` block documenting the run, `validation.gates` with actual backend/frontend command results.
- [x] 13.6 Run `pwsh -File scripts/gates/Run-Gates.ps1` one final time across the whole change.
- [x] 13.7 Commit: `docs(niche-discovery): add ADR 0013, PRD section, and operator setup guide`.

---

## WU-14 — Frontend: Mount the Viability Dashboard Route

*Refs: ND-01, ND-02, ND-09, ND-12, ND-13. Design §10, §11. Follow-up to the WU-12 gap flagged in
`state.yaml`'s `known_gaps` and in the WU-12b commit message: the dashboard/detail components
existed and were unit-tested but no route rendered them, so the operator could not reach the
screen this change exists to deliver.*

- [x] 14.1 **[RED]** Add `apps/web/lib/social/niche-discovery/run-state.test.ts` — Requested/Running/
  Completed/PartiallyCompleted/Failed each render a distinct label and tone; only
  `PartiallyCompleted` is flagged degraded; `Failed` carries its structured `failureReasonKey`,
  never invented text (ND-02, ND-09).
- [x] 14.2 **[GREEN]** Create `run-state.ts` — `isRunPollable`, `isRunViewable`, `isRunDegraded`,
  `describeRunState`.
- [x] 14.3 **[RED]** Add `run-url.test.ts` — `buildRunPath`/`readRunIdFromSearchParams` round-trip a
  run id through the `?run=` query string (design §10 — no cross-run listing endpoint, so the URL
  is the operator's only handle to a run that already cost API quota); reads both `URLSearchParams`
  and a plain server `searchParams` record.
- [x] 14.4 **[GREEN]** Create `run-url.ts`.
- [x] 14.5 **[RED]** Add `start-run-form.test.ts` — window-days bounds (7..365, default 90 — mirrors
  the backend `DiscoveryWindow` value object), Quick/Standard/Deep depth options, required
  source/target market with a must-differ check, and `buildStartRunRequest` mapping 1:1 onto
  `StartDiscoveryRunRequest` (ND-01). The 90-day default and the operator-selectable depth are
  explicit owner decisions, not defaults of convenience.
- [x] 14.6 **[GREEN]** Create `start-run-form.ts`.
- [x] 14.7 Create `apps/web/components/social/niche-discovery/niche-discovery-client.tsx` — thin
  start-and-poll screen (2500 ms interval, matching design §10's stated idiom): start form, a
  status badge per run state, a visible structured-reason panel for `Failed`, a visible degraded
  notice for `PartiallyCompleted` (never presented as a full result), `ViabilityDashboardClient` for
  a viewable run, and `OpportunityDetail` for a selected opportunity.
- [x] 14.8 Create `apps/web/app/(dashboard)/niche-discovery/page.tsx` — mirrors
  `app/(dashboard)/ai-analysis/page.tsx`: server component, `getServerAuthSession()`, redirect to
  `/login` when the token is missing or expired, reads the run id out of the async Next 15
  `searchParams` via `readRunIdFromSearchParams`.
- [x] 14.9 Update `apps/web/lib/navigation.ts` — add a "Niche Discovery" entry (icon: `Radar`) first
  in the `socialModuleNavigation` "Content" group, ahead of Single Analyzer/AI Analysis/Generator/
  Publications, and update the group's ordering comment to state why (answers which market/niche to
  build for before any client or content exists).
- [x] 14.10 Checked `next.config.ts`'s rewrite table (`/content-generator → /workflow`,
  `/single-analyzer → /analyzer`); no rewrite added — the new route path already matches the
  navigation href, so no clean-URL mapping is needed.
- [x] 14.11 Run the frontend test command used by `scripts/gates/Run-Gates.ps1`, then
  `npx tsc --noEmit` and `npm run build`. All clean (343/343 frontend tests — 20 new — tsc clean,
  build clean, `/niche-discovery` registered as a dynamic route).
- [x] 14.12 `pwsh -File scripts/gates/Run-Gates.ps1` green (17 pass / 1 skipped / 1 warn — the
  pre-existing frontend vulnerability advisory, unrelated). Commit
  `feat(niche-discovery): mount viability dashboard route and navigation entry`, directly on `dev`.

**Reported gap, not silently narrowed**: `INicheOpportunityActivationSource` stays a named,
unimplemented seam by design (§12) — this work unit does not add a "send to Generator" action, and
none should be inferred from the screen now being reachable. The screen still gives the operator no
way to list or browse past runs (no cross-run listing endpoint exists, per design §10) — only the
URL round-trip this work unit adds. `YouTubeEvidenceMapper` live verification remains pending an
owner-side Google Cloud project, unchanged from WU-13.

---

## Traceability

| Spec requirement | Satisfied by |
|---|---|
| ND-01 — reproducible run parameters | WU-1 |
| ND-02 — status lifecycle distinguishes unconfigured/exhausted/empty | WU-1, WU-3, WU-11a |
| ND-03 — Ran/Unconfigured/Failed source chain | WU-3, WU-5, WU-11a |
| ND-04 — normalization boundary excludes provider types | WU-5 |
| ND-05 — nullable metrics, never zero-filled | WU-3, WU-5, WU-6, WU-12 |
| ND-06 — deterministic, pure, reproducible analytics | WU-6 |
| ND-07 — semantic layer emits labels only | WU-10 |
| ND-08 — market comparison traction/demand/competition/gap | WU-9 |
| ND-09 — availability-aware renormalized OpportunityScore | WU-8 |
| ND-10 — independent ConfidenceScore | WU-8, WU-12 |
| ND-11 — append-only evidence persistence | WU-7a, WU-7b |
| ND-12 — dashboard sort/filter | WU-12, WU-14 (route mount) |
| ND-13 — opportunity detail full context | WU-12, WU-14 (route mount) |
| ND-14 — market catalog is data, second market needs no code change | WU-2, WU-7a, WU-9, WU-11a |
| ND-15 — no country-named identifier | WU-2 |
| ND-16 — tenant-scoped rows | WU-7a, WU-7b |
| ND-17 — Content Generator seam named, not built | WU-3, WU-11b |
| INT-ND-01 — official-API provider category + API-key credential | WU-4 |
| INT-ND-02 — YouTube Data API v3 seeded | WU-4 |
| INT-ND-03 — checklist group | WU-4 |
| INT-ND-04 — per-stage config-driven origin selection | WU-4, WU-5 |

---

## Definition of Done

- [x] All 16 work units (15 planned + WU-14 follow-up mounting the dashboard route) land as sequential commits directly on `dev`, each with `pwsh -File scripts/gates/Run-Gates.ps1` green before the commit.
- [x] `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj -c Release` passes, 0 failures, across the whole change (3518/3520, 2 pre-existing skips).
- [x] The frontend test command, `npx tsc --noEmit`, and `npm run build` all pass clean (343/343 tests after WU-14).
- [x] `dotnet ef migrations has-pending-model-changes` is clean (`migration-drift` gate).
- [x] Architecture gate green: no `JsonElement`/`HttpClient`/EF type in Domain or Application under NicheDiscovery.
- [x] The country-name guard (WU-2.7), the extended no-actor-literal guard (WU-5.1), the three AI-cannot-produce-a-number reflection guards (WU-6.1, WU-8.7/10.6, WU-10.1), and the NicheDiscovery/ContentGeneration isolation guard (WU-11b.6) all pass.
- [x] `GetServices<INicheDiscoverySource>()` and every other NicheDiscovery port resolve to exactly one implementation; `GetServices<INicheOpportunityActivationSource>()` is empty.
- [x] The second-target-market criterion test (WU-11a.5) passes with no per-market source file existing.
- [x] ADR 0013 exists and is `Accepted`; `docs/PRD.md` gains the niche-discovery section; specs synced or explicitly deferred to `sdd-archive`.
- [x] `openspec/changes/niche-discovery-engine/state.yaml` reflects the terminal status of this run.
- [x] The viability dashboard is reachable from navigation and usable end to end via `/niche-discovery` (WU-14).
