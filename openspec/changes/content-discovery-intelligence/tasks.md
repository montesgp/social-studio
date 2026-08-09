# Tasks: content-discovery-intelligence

**Branch:** single feature branch off `dev`; one PR at the end (`size:exception` accepted).
**TDD mode:** STRICT — `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj -c Release`. Every behavioural task: RED (failing test from a spec scenario) → GREEN (implementation) → run suite.
**Do NOT modify** `ApifyActorMappingBackfill.Transform` — teaching it `niche-search` re-tags existing extraction actors and breaks the single analyzer (design §8.1).

> **Amendment (2026-08-09)** — this task list was amended in place, not regenerated, for two owner
> corrections applied to spec/design. See `## Amendment Note` after the work-unit table for exactly
> what changed and why. Tasks that survived the amendment keep their original numbering where
> possible; only WU-0, WU-1, WU-6 and WU-7 were restructured.

---

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 2,600–3,400 (revised down from 3,000–4,000: the ~200-line TOP-N fan-out slice is gone, replaced by a smaller frontend shortlist action; offset by new configuration/guard tasks — catalog analysis type + step, checklist item, seed mapping, repository-wide no-actor-literal guard) |
| 400-line budget risk | **High** |
| Chained PRs recommended | No — `size:exception` explicitly accepted by the owner |
| Suggested split | Single PR; work organized into 10 sequential/parallel work units for reviewable diff sections |
| Delivery strategy | exception-ok |
| Chain strategy | size-exception |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: size-exception
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| WU-0 | Apify niche-search **schema** spike against whatever actor the OPERATOR has configured — recorded fixture (BLOCKING for WU-6) | Same PR | N/A — spike, no test yet | Real Apify actor call using the operator-configured `(tiktok, niche-search)` mapping as an input to the spike; no actor id named in this document or in code | Delete fixture file; no production code |
| WU-1 | `DiscoveredContent`/`ContentDiscoveryRequest`/`ContentDiscoveryPlan`(+`UnconfiguredSourceKeys`)/`SourceDiscoveryResult` breaking contract | Same PR | `dotnet test --filter FullyQualifiedName~ContentDiscovery` | N/A — compiler-enforced coordinated unit | Revert unit as a whole (compiler blocks partial) |
| WU-2 | `CandidateIdentity` + `ContentDiscoveryChain` + DI replacement + composition test | Same PR | `dotnet test --filter FullyQualifiedName~DiscoveryCompositionTests` | Boot `AddInfrastructure`, resolve `IContentDiscoveryService` | Restore `AddScoped<IContentDiscoveryService, ReferenceAccountContentDiscovery>()` (one line) |
| WU-3 | Reference + Own discovery sources | Same PR | `dotnet test --filter FullyQualifiedName~DiscoverySource` | N/A — unit-tested against fakes | Remove `OwnAccountDiscoverySource` registration |
| WU-4 | Scorer + classifier + baseline provider | Same PR | `dotnet test --filter FullyQualifiedName~(OpportunityScorer\|TrendingProven\|AccountBaseline)` | N/A — pure/unit | Remove scoring call site in chain consumer |
| WU-5 | Persistence: entities, migration, repositories | Same PR | `dotnet test --filter FullyQualifiedName~ContentCandidateRepository` | EF test-host round-trip against local Postgres | Drop migration; both tables additive |
| WU-6 | Niche source + Apify adapter + configuration-driven actor routing (catalog analysis type/step, checklist item, seed, no-actor-literal guard) (depends on WU-0) | Same PR | `dotnet test --filter FullyQualifiedName~(TikTokNicheSearch\|ApifyNicheSearchResolver\|WorkflowConfigurationCatalog\|GetOrganizationIntegrationsHandler\|NoActorLiteral)` | N/A — adapter tested against WU-0 fixture, no live call | Remove `(tiktok, niche-search)` mapping/step registration; chain tolerates unconfigured source |
| WU-7 | Discovery shortlist — single-selection frontend action reusing the existing single-analysis start+poll path | Same PR | Frontend component test suite for the shortlist + overlay | N/A — no new backend transport; existing `AnalyzeSingleContentCommand` endpoint and existing polling overlay | Revert the frontend shortlist action; no backend code to roll back |
| WU-8 | `own` alias fix + workflow wiring end-to-end | Same PR | `dotnet test --filter FullyQualifiedName~ContentWorkflowServiceTests` | Full workflow session run through `InMemoryContentWorkflowService` with `own` criterion | Revert `SearchCriterionAliases` entry; independently safe |
| WU-9 | ADR verification + docs/spec sync + final gates | Same PR | N/A — documentation | `pwsh -File scripts/gates/Run-Gates.ps1` | Docs-only, trivially revertible |

### Amendment Note — what changed in this file and why

- **WU-0** (spike) — no longer names an actor id (`GdWCkxBtKWOsKjdch` / `clockworks/*` removed). The spike now
  verifies the response schema of whatever actor the **operator** has configured for `(tiktok, niche-search)`; that
  actor id is an input to the spike, never chosen or named here (design §7, D13).
- **WU-1** — `ContentDiscoveryPlan` now carries a third list, `UnconfiguredSourceKeys` (CD-13, D12), and the port's
  per-source result becomes the three-valued `SourceDiscoveryResult(Outcome: Ran|Unconfigured|Failed, Candidates)`
  instead of a bare list — folded into the coordinated contract-change unit per the amendment instruction, since it
  is part of the same breaking DTO family and must land in the same compiler-enforced commit.
- **WU-6** — gained: the repository-wide no-actor-literal guard (RED first, blocking); the `content-discovery`
  analysis type + `discover-niche-content` step under the **existing** `generator` workflow key (not a new
  top-level key, D11); the optional checklist item in `generatorChecklist`; the seeded placeholder
  `(tiktok, niche-search)` mapping (INT-DISC-02). Task numbering inside WU-6 was renumbered to put the guard test
  first.
- **WU-7 — REMOVED as originally written.** The TOP-N fan-out command (`AnalyzeTopCandidatesCommand` +
  `AnalyzeTopCandidatesCommandTests` + `TopCandidateAnalysisPlan`) is deleted from scope: the design found this
  change authors **no backend code** on the single-selection path — `AnalyzeSingleContentHandler` already does
  `StartAsync` → `ISingleAnalysisJobQueue.EnqueueAsync` → return id, and `single-analyzer-client.tsx` already polls
  every 2500 ms. WU-7 is **replaced in place** by a small frontend-only unit: the candidate shortlist UI, single
  selection, and handing the selected candidate into the existing analyze-and-poll path (design §8.2). The
  multi-select background variant is a named follow-up — Magic Generator, `state.yaml` roadmap phase 7.
- Ranking/scoring/TOP-N-**for-display** is unchanged (CD-05/CD-06/CD-09, WU-4) — only automatic analysis of all N
  disappeared.
- WU-2, WU-3, WU-4, WU-5, WU-8, WU-9 are unchanged in substance; task text below is preserved as originally
  authored except where an internal cross-reference needed updating.

---

## WU-0 — Apify TikTok Niche-Search **Schema** Spike (BLOCKING)

*Refs: design §7 (restructured 2026-08-09), §7.1, ADR 0012 Consequences, OQ-1, D13, CD-12. No file under
`Infrastructure/Social/Providers/Apify/TikTokNicheSearchAdapter.cs` may exist until this closes. This is a
**schema** spike, not an actor-selection spike — the actor id comes from whatever `(tiktok, niche-search)` route the
operator has configured; it is an input to the spike, never chosen or named by this document, and MUST NOT appear
in code, tests, or fixture filenames (D13).*

- [ ] 0.1 Take the actor id already resolvable via the operator-configured `(tiktok, niche-search)` route (or a
      temporary manual mapping used only for the purposes of running this spike) and call it with
      `{ searchQueries: ["<keyword>"], resultsPerPage: 20, shouldDownloadVideos: false, … }`; if it does not serve
      search, retry with `{ hashtags: ["<tag>"], resultsPerPage: 20, … }`. Record which input shape the configured
      actor honours — that becomes `TikTokNicheSearchAdapter`'s input builder. No actor id is written into this
      task list, code, or the fixture filename.
- [ ] 0.2 Confirm presence of `createTimeISO`, `playCount`, `diggCount`, `commentCount`, `shareCount`, `collectCount`, `authorMeta.name`/`nickName`, `webVideoUrl`, `text`, `videoMeta.coverUrl`, `hashtags[].name`.
- [ ] 0.3 **Exit criterion**: save one sanitized raw response as `Incoders.Hive.Tests/Infrastructure/Social/Fixtures/tiktok-niche-search.json` — a *capability*-named file, not an actor-named one. WU-6 tasks are BLOCKED until this file exists and is committed.
- [ ] 0.4 If `playCount` is absent from the response, STOP and escalate to the owner (design §7 escalation rule: unrankable source, do not ship) instead of proceeding to WU-6. If `createTimeISO` or share/save counts are absent, note the degradation (candidates become `Undetermined`; those fields excluded from the scorer denominator) and proceed.
- [ ] 0.5 Note the shape of an unmappable/empty response too (design §7.1) — this informs WU-6's "zero-of-many mapped → `Failed`" and "partial → `Ran`" test cases.

---

## WU-1 — Coordinated Breaking Contract Change

*Refs: D1, D2, D12, CD-04, CD-13; ADR 0012 §3; design §3.1, §3.2, §11 step 1. Amended 2026-08-09: `ContentDiscoveryPlan`
now carries `UnconfiguredSourceKeys` and the port's per-source result becomes `SourceDiscoveryResult` — folded into
this unit rather than left for WU-2, since it is part of the same breaking DTO family and must land in the same
compiler-enforced commit.*

- [ ] 1.1 **[RED]** Add `ContentDiscoveryPlanTests.cs` — assert `ContentDiscoveryPlan(Candidates, Shortfall, FailedSourceKeys, UnconfiguredSourceKeys)` 4-field shape (D12/CD-13). (`Incoders.Hive.Tests/Application/Abstractions/Social/ContentDiscoveryPlanTests.cs`)
- [ ] 1.2 **[RED]** Extend/add `DiscoveredContentTests.cs` — assert `Views`/`Likes`/`Comments` are `long?`; assert `Shares`, `Saves`, `PublishedAtUtc`, `SourceKey` exist and default to unavailable (`null`), never `0`. (`Incoders.Hive.Tests/Application/Abstractions/Social/DiscoveredContentTests.cs`)
- [ ] 1.3 **[RED]** Add `SourceDiscoveryResultTests.cs` — assert `SourceDiscoveryResult(Outcome, Candidates)` with `Outcome` a three-valued `SourceOutcome` (`Ran`/`Unconfigured`/`Failed`); `Candidates` empty unless `Ran`. (`Incoders.Hive.Tests/Application/Abstractions/Social/SourceDiscoveryResultTests.cs`)
- [ ] 1.4 **[GREEN]** Modify `DiscoveredContent`, `ContentDiscoveryRequest` (add `SearchCriterion`), `IContentDiscoveryService` (return `ContentDiscoveryPlan`); add `ContentDiscoveryPlan` record (4 fields) and `SourceDiscoveryResult`/`SourceOutcome`. (`Application/Abstractions/Social/ContentDiscovery/IContentDiscoveryService.cs`)
- [ ] 1.5 **[RED]** Update `ReferenceAccountContentDiscoveryTests.cs` for the new nullable shape; assert the `post.VideoViewCount ?? 0` zero-fill is gone (raw `post.VideoViewCount`). (existing test file, moved per WU-3)
- [ ] 1.6 Confirm solution does NOT compile until WU-1–WU-3 land together (coordinated unit — compiler is the safety net; no partial commit).

---

## WU-2 — Chain, Dedup Identity, DI Replacement, Composition Test

*Refs: CD-01, CD-02, CD-03, D3, D4, D5; ADR 0012 §1; design §3.2, §4.*

- [ ] 2.1 **[RED]** Add `CandidateIdentityTests.cs` — table test: scheme/host case-insensitive, drop `www.`, drop query/fragment, trim trailing `/`, network-key prefix. (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Discovery/CandidateIdentityTests.cs`)
- [ ] 2.2 **[GREEN]** Create `CandidateIdentity.From(network, url)` pure function. (`Application/Abstractions/Social/ContentDiscovery/CandidateIdentity.cs`)
- [ ] 2.3 **[RED]** Add `ContentDiscoveryChainTests.cs` — CD-01 remainder-only asks + one-source-failure tolerance + `Shortfall`/`FailedSourceKeys`; CD-02 cross-source dedup; CD-03 `niche`→niche-only, `own`→own-only, `viral`/`competitors`→all sources (D5), never `SearchCriterionInvalid`; **CD-13/D12: `Unconfigured` lands in `UnconfiguredSourceKeys`, `Ran`-with-zero lands in neither list** — two fake sources, one per outcome, assert the two lists are disjoint. (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Discovery/ContentDiscoveryChainTests.cs`)
- [ ] 2.4 **[GREEN]** Create `IContentDiscoverySource` port (`SourceKey`, `Order`, `Supports`, `DiscoverAsync` returning `Task<SourceDiscoveryResult>` — the DTO created in WU-1.4). (`Application/Abstractions/Social/ContentDiscovery/IContentDiscoverySource.cs`)
- [ ] 2.5 **[GREEN]** Create `ContentDiscoveryChain` implementing `IContentDiscoveryService` per the algorithm in design §3.2. (`Application/Features/Social/ContentGeneration/Discovery/ContentDiscoveryChain.cs`)
- [ ] 2.6 **[RED]** Add `DiscoveryCompositionTests.cs` — build the real container via `AddInfrastructure`; assert (a) `GetRequiredService<IContentDiscoveryService>()` is `ContentDiscoveryChain`, (b) `GetServices<IContentDiscoveryService>().Count() == 1` — **write this assertion first**, it is the one that catches a silent DI override, (c) `GetServices<IContentDiscoverySource>()` keys equal `{references, own, niche}`, (d) `Order` values distinct. (`Incoders.Hive.Tests/Infrastructure/DependencyInjection/DiscoveryCompositionTests.cs`)
- [ ] 2.7 **[GREEN]** In `DependencyInjection.cs:220`, **REPLACE** (do not supplement) `services.AddScoped<IContentDiscoveryService, ReferenceAccountContentDiscovery>()` with source registrations + `services.AddScoped<IContentDiscoveryService, ContentDiscoveryChain>()`. (`Infrastructure/DependencyInjection/DependencyInjection.cs`)
- [ ] 2.8 Run `dotnet test` — WU-2 chain/composition tests green (sources still stubs; full green after WU-3).

---

## WU-3 — Reference + Own Discovery Sources

*Refs: CD-01 (adapted), CR-DISC-02; ADR 0012 §1; design §2.2.*

- [ ] 3.1 **[GREEN]** Move `ReferenceAccountContentDiscovery.cs` → `ReferenceAccountDiscoverySource` implementing `IContentDiscoverySource`, `Order: 1`, `Supports("references")`; remove the `?? 0` zero-fill. (`Application/Features/Social/ContentGeneration/Discovery/ReferenceAccountDiscoverySource.cs`)
- [ ] 3.2 Move/rename its test file to `ReferenceAccountDiscoverySourceTests.cs`, updated for nullable metrics. (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Discovery/ReferenceAccountDiscoverySourceTests.cs`)
- [ ] 3.3 **[RED]** Add `OwnAccountDiscoverySourceTests.cs` — near-clone assertions over `IClientOwnAccountRepository` + `IProfileScrapeResolver`; assert `Order: 2`, `Supports("own")` only; assert empty non-throwing result for a TikTok-only client (CR-DISC-02 scenario 2). (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Discovery/OwnAccountDiscoverySourceTests.cs`)
- [ ] 3.4 **[GREEN]** Create `OwnAccountDiscoverySource` over `IClientOwnAccountRepository` + `IProfileScrapeResolver`. (`Application/Features/Social/ContentGeneration/Discovery/OwnAccountDiscoverySource.cs`)
- [ ] 3.5 Run `dotnet test` — WU-1+2+3 fully green; solution compiles (coordinated unit closes).

---

## WU-4 — Opportunity Scorer, Classifier, Baseline Provider

*Refs: CD-04, CD-05, CD-06, CD-07, CD-09; design §6.*

- [ ] 4.1 **[RED]** Add `OpportunityScorerTests.cs` — determinism (same input twice → same score, fixed `nowUtc` param); availability-aware denominator (missing signal excluded, not zeroed); degenerate-set fallback to `0.5`. (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Discovery/OpportunityScorerTests.cs`)
- [ ] 4.2 **[GREEN]** Create pure `OpportunityScorer.Score(set, baselines, nowUtc)` per weights table in design §6.1. (`Application/Features/Social/ContentGeneration/Discovery/OpportunityScorer.cs`)
- [ ] 4.3 **[RED]** Add `TrendingProvenClassifierTests.cs` — null `PublishedAtUtc` → `Undetermined` FIRST and unconditionally (never defaulted to `Proven`/`Trending`); recent+high-velocity → `Trending`; old+high-total → `Proven`. (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Discovery/TrendingProvenClassifierTests.cs`)
- [ ] 4.4 **[GREEN]** Create `TrendingProvenClassifier` per design §6.3. (`Application/Features/Social/ContentGeneration/Discovery/TrendingProvenClassifier.cs`)
- [ ] 4.5 **[RED]** Add `CoOccurrenceSignalTests.cs` — tokenizer (lowercase, strip punctuation, drop <4-char tokens, ES+EN stopwords); K=3 threshold; distinct-author counting; normalization by set author count. (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Discovery/CoOccurrenceSignalTests.cs`)
- [ ] 4.6 **[GREEN]** Implement co-occurrence signal inside `OpportunityScorer` per design §6.4.
- [ ] 4.7 **[RED]** Add `AccountBaselineProviderTests.cs` — fresh-cache hit → 0 scrapes (CD-09 scenario 1); stale cache → 1 recompute (CD-09 scenario 2); no baseline + not TOP-N → relative-performance omitted or candidate-set median, never a fabricated value (CD-07 scenario 2); ≤N scrapes for a 100-candidate set (design §6.5 economics test). (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Discovery/AccountBaselineProviderTests.cs`)
- [ ] 4.8 **[GREEN]** Create `AccountBaselineProvider.GetOrComputeAsync(org, network, handle, nowUtc)` — read-through cache over `IAccountBaselineRepository` + `IProfileScrapeResolver` + `ProfileMetricCalculator`. (`Application/Features/Social/ContentGeneration/Discovery/AccountBaselineProvider.cs`)
- [ ] 4.9 Run `dotnet test` — WU-4 scorer/classifier/baseline tests green.

---

## WU-5 — Persistence: Entities, Migration, Repositories

*Refs: CD-08, CD-09; ADR 0012 §4; design §5.*

- [ ] 5.1 **[GREEN]** Create `DiscoveredCandidateData` entity (schema `social`, snake_case, `organization_id`/`client_id` columns, nullable metrics per design §5.1). (`Infrastructure/Persistence/Postgres/Entities/DiscoveredCandidateData.cs`)
- [ ] 5.2 **[GREEN]** Create `AccountBaselineData` entity (schema `social`, unique `(organization_id, network_key, lower(author_handle))`, no `client_id`, per design §5.2). (`Infrastructure/Persistence/Postgres/Entities/AccountBaselineData.cs`)
- [ ] 5.3 **[GREEN]** Map both entities in `AppIdentityDbContext.cs` in the `content_generation_batches` style, including the `ix_discovered_candidates_org_client_key`, `ix_discovered_candidates_org_observed`, and unique `ux_discovered_candidates_key_observed` indexes. (`Infrastructure/Identity/AppIdentityDbContext.cs`)
- [ ] 5.4 Generate migration `Add_ContentDiscoveryCandidatesAndAccountBaselines` via `dotnet ef migrations add` into `Incoders.Hive.Infrastructure/Persistence/Postgres/Migrations/`, including `.Designer.cs` and the updated `AppIdentityDbContextModelSnapshot.cs`. Purely additive; no backfill.
- [ ] 5.5 **[RED]** Add `EfContentCandidateRepositoryTests.cs` — round-trip; unique constraint on `(organization_id, client_id, candidate_key, observed_at)` rejects a double-write inside one run (CD-08 scenario). (`Incoders.Hive.Tests/Infrastructure/Persistence/Postgres/Repositories/EfContentCandidateRepositoryTests.cs`)
- [ ] 5.6 **[GREEN]** Create `IContentCandidateRepository` port + `EfContentCandidateRepository` (`RecordObservationsAsync`, `GetLatestByKeysAsync`). (`Application/Abstractions/Social/ContentDiscovery/IContentCandidateRepository.cs`, `Infrastructure/Persistence/Postgres/Repositories/EfContentCandidateRepository.cs`)
- [ ] 5.7 **[RED]** Add `EfAccountBaselineRepositoryTests.cs` — upsert idempotence per `(org, network, handle)`. (`Incoders.Hive.Tests/Infrastructure/Persistence/Postgres/Repositories/EfAccountBaselineRepositoryTests.cs`)
- [ ] 5.8 **[GREEN]** Create `IAccountBaselineRepository` port + `EfAccountBaselineRepository` (`GetAsync`, `UpsertAsync`). (`Application/Abstractions/Social/ContentDiscovery/IAccountBaselineRepository.cs`, `Infrastructure/Persistence/Postgres/Repositories/EfAccountBaselineRepository.cs`)
- [ ] 5.9 Register both repositories in Infrastructure DI. (`Infrastructure/DependencyInjection/DependencyInjection.cs`)
- [ ] 5.10 Run `dotnet test` — WU-5 persistence tests green against the EF test-host pattern.

---

## WU-6 — Niche Source + Apify Adapter + Configuration-Driven Actor Routing (depends on WU-0)

*Refs: INT-DISC-01/02/03/04, CD-01 (niche), CD-12, CD-13, D11, D12, D13; ADR 0012 §6; design §7, §7.1, §8.1. BLOCKED
until WU-0.3 fixture exists. Amended 2026-08-09: gained the repository-wide no-actor-literal guard (written first),
the `content-discovery` analysis type + `discover-niche-content` step under the **existing** `generator` workflow
key (not a new top-level key), the optional checklist item in `generatorChecklist`, and the seeded placeholder
mapping.*

- [ ] 6.1 **[RED]** Add the repository-wide **no-actor-literal guard** — assert no actor-id-shaped literal (17-char
      alphanumeric token pattern, and no `"clockworks/…"` or similar vendor-actor-name literal) exists in shipped
      `Incoders.Hive.Application` or `Incoders.Hive.Infrastructure` source, outside test fixtures (CD-12/D13, design
      §11 Guard row, §13 risk). Written **first**, before any niche-search code — it is the assertion that makes "no
      forced actor" enforceable rather than aspirational. If the naive regex false-positives on unrelated
      17-character tokens elsewhere in the codebase, tighten it to the Apify call-site files
      (`ApifyProfileScrapeResolver.cs`, `ApifyNicheSearchResolver.cs`, `TikTokNicheSearchAdapter.cs`,
      `ApifyActorMappingLookup.cs`) rather than deleting the guard. (`Incoders.Hive.Tests/Architecture/NoActorLiteralGuardTests.cs`)
- [ ] 6.2 **[RED]** Add `TikTokNicheSearchAdapterTests.cs` — `MapItem` against the WU-0 recorded fixture only, no live call; assert degradation when `createTimeISO`/share/save absent per design §7 table; assert an unmappable item is **skipped**, not defaulted; assert **zero-of-many mapped → source `Failed`**; assert a partial map → source `Ran` with what mapped (design §7.1). (`Incoders.Hive.Tests/Infrastructure/Social/Fixtures` consumer test)
- [ ] 6.3 **[GREEN]** Create `TikTokNicheSearchAdapter` — input build (`searchQueries`/`hashtags`) + `MapItem`, per-item `JsonException` caught, never thrown. (`Infrastructure/Social/Providers/Apify/TikTokNicheSearchAdapter.cs`)
- [ ] 6.4 **[GREEN]** Extract `ApifyActorMappingLookup.GetAllMappedActors` out of `ApifyProfileScrapeResolver` (delegates, keeps its existing tests green). (`Infrastructure/Social/Providers/ApifyActorMappingLookup.cs`)
- [ ] 6.5 **[RED]** Add `ApifyNicheSearchResolverTests.cs` — `(tiktok, niche-search)` lookup succeeds with a configured mapping, returning the configured id read from configuration, never a literal (CD-12 scenario 1); missing/empty entry returns `SourceOutcome.Unconfigured` — a structured configuration failure, never an unhandled exception and never a substituted literal id (INT-DISC-01 scenario 2, CD-12 scenario 2), using the `ApifyProfileScrapeResolverTests` fake-handler pattern. (`Incoders.Hive.Tests/Infrastructure/Social/Providers/ApifyNicheSearchResolverTests.cs`)
- [ ] 6.6 **[GREEN]** Create `INicheSearchResolver` port + `ApifyNicheSearchResolver` — resolves via `IWorkflowConfigurationResolver.ResolveStepAsync` first, falls back to `GetAllMappedActors(actor_mappings_json, "tiktok", "niche-search")`, and returns `Unconfigured` (never a literal) when both are empty, per design §8.1 resolution order. (`Application/Abstractions/Social/ContentDiscovery/INicheSearchResolver.cs`, `Infrastructure/Social/Providers/ApifyNicheSearchResolver.cs`)
- [ ] 6.7 **[RED]** Extend `ContentDiscoveryChainTests.cs` (or add `TikTokNicheSearchSourceTests.cs`) — missing `niche-search` route reports its key in `UnconfiguredSourceKeys`, **not** `FailedSourceKeys` (CD-13, D12); other sources still run (INT-DISC-01 scenario 2 + CD-01). (`Incoders.Hive.Tests/Application/Social/ContentGeneration/Discovery/TikTokNicheSearchSourceTests.cs`)
- [ ] 6.8 **[GREEN]** Create `TikTokNicheSearchSource` — `Order: 3`, `Supports("niche")`, consumes `INicheSearchResolver` + `TikTokNicheSearchAdapter`, returns `SourceDiscoveryResult` with the correct `Outcome`. (`Application/Features/Social/ContentGeneration/Discovery/TikTokNicheSearchSource.cs`)
- [ ] 6.9 **[GREEN]** Register `TikTokNicheSearchSource` in `DependencyInjection.cs` (added to the WU-2.7 replacement block, `Order: 3`).
- [x] 6.10 **[RED]** Add/extend `WorkflowConfigurationCatalogTests.cs` — `generator` → `content-discovery` (new `WorkflowAnalysisTypeDefinition`) → `discover-niche-content` (new `WorkflowStepDefinition`) resolves via `FindStep`, carries `operationKey: "niche-search"`, `RequiresNetwork: true`, `SupportedNetworkKeys: ["tiktok"]`, `AllowedProviderCategories: [DataExtraction]`, `AllowedProviderKeys: ["apify"]`, `SupportsActorSelection: true`, `ActorVariantKey: "niche-search"` (INT-DISC-02, D11). Landed as `Incoders.Hive.Tests/Application/Integrations/WorkflowConfigurationCatalogContentDiscoveryStepTests.cs` (matching the sibling catalog test files' actual folder, not the path originally sketched here).
- [x] 6.11 **[GREEN]** Add the `content-discovery` analysis type (with the `discover-niche-content` step) under the **existing** `generator` workflow key in `WorkflowConfigurationCatalog` per design §8.1 — explicitly NOT a new top-level workflow key (D11: the settings selector is built from `checklistGroups`, and `generator` already has one). (`Application/Features/Integrations/WorkflowConfigurations/WorkflowConfigurationCatalog.cs`)
- [x] 6.12 **[RED]** Extend `OrganizationIntegrationsFlowTests.cs` (the actual `GetOrganizationIntegrationsHandler` test host) — `generatorChecklist` contains a new optional item `("generator-niche-search-actor", "Configure a TikTok niche-search actor", Required: false)`, satisfied when an enabled `(tiktok, niche-search)` route resolves (INT-DISC-03); unsatisfied by default; a pre-existing "all items completed" assertion updated to check `IsRequired` items only, since the new item is intentionally optional.
- [x] 6.13 **[GREEN]** Append the `generator-niche-search-actor` checklist item to `generatorChecklist` in `GetOrganizationIntegrationsHandler` (additive; `Required: false`). Satisfaction is resolved via a new optional `IWorkflowStepConfigurationRepository? stepConfigurations` constructor dependency (defaults to `null`, so existing callers/tests are unaffected); when present, it checks for an enabled `(generator, content-discovery, discover-niche-content, niche-search, tiktok)` row with a non-empty `ActorId`.
- [x] 6.14 **[GREEN]** Seed a placeholder, **disabled**, **unassigned** `discover-niche-content` step configuration as **data** via a new `TikTokNicheSearchWorkflowStepSeeder` (mirrors `ComfyUiOrganizationSeeder`/`ClaudePromptCliOrganizationSeeder`), bound to the organization's already-connected Apify integration with `ActorId: null` and `IsEnabled: false` — the operator only has to choose the actor and enable it. Skips organizations with no connected data-extraction integration; idempotent — never overwrites an existing row for this step (operator-configured or previously seeded). Wired into `Program.cs` startup seeding and registered in `DependencyInjection.cs`. (`Infrastructure/Integrations/TikTokNicheSearchWorkflowStepSeeder.cs`, `TikTokNicheSearchWorkflowStepSeederExtensions.cs`)

  **Amendment note (this partial apply run, 2026-08-09):** only 6.10–6.14 were implemented in this
  work-unit slice, by explicit owner scope. 6.1–6.9 and 6.15–6.16 (no-actor-literal guard,
  `TikTokNicheSearchAdapter`, `ApifyActorMappingLookup` extraction, `INicheSearchResolver` +
  `ApifyNicheSearchResolver`, `TikTokNicheSearchSource`, its DI registration, and the WU-6 full-suite
  run) remain unimplemented and unblocked-but-pending for a later apply pass. WU-0 (the schema spike)
  is still open and still blocks 6.1–6.9.
- [ ] 6.15 Confirm `ApifyActorMappingBackfill.Transform` is untouched — no diff in that file, and it never produces a `niche-search` entry (design explicit "do not modify"; INT-DISC-04).
- [ ] 6.16 Run `dotnet test` — WU-6 niche source/adapter/resolver/catalog/checklist tests green; the no-actor-literal guard (6.1) passes repository-wide; composition test (2.6) still asserts exactly one `IContentDiscoveryService` with three sources.

---

## WU-7 — Discovery Shortlist: Single-Selection Frontend Action (replaces the removed TOP-N fan-out)

*Refs: CD-10 (rewritten 2026-08-09); ADR 0012 §6; design §8.2, D8.*

> **REMOVED by the 2026-08-09 amendment**: the original WU-7 (`AnalyzeTopCandidatesCommandTests.cs`,
> `AnalyzeTopCandidatesCommand` + handler, `TopCandidateAnalysisPlan`) is deleted from scope. The design found this
> path authors **no backend code**: `AnalyzeSingleContentHandler` already does `StartAsync` →
> `ISingleAnalysisJobQueue.EnqueueAsync` → return id, and `single-analyzer-client.tsx` already polls every 2500 ms.
> A genuinely synchronous fan-out command would have been a *second* mechanism, not "the same UX." The multi-select
> background variant is a named follow-up — **Magic Generator**, `state.yaml` roadmap phase 7 — and is not built
> here.

- [ ] 7.1 **[RED]** Add a frontend component test for the discovery shortlist — asserts the shortlist enforces
      **exactly one** selection (selecting a second candidate replaces, never adds, the first); asserts clicking
      "Analyze this one" issues exactly **one** `POST` to the **existing** single-analysis start endpoint
      (`AnalyzeSingleContentCommand`) with the selected candidate's `ContentUrl` — no new endpoint, no new request
      shape (CD-10 scenario 1).
- [ ] 7.2 **[GREEN]** Implement the shortlist single-selection action: render the ranked TOP-N-for-display
      candidates (unchanged from WU-4 scoring/classification), wire the "Analyze this one" action to the existing
      single-analysis start call, and hand the returned analysis id into the **existing** polling overlay
      (`single-analyzer-client.tsx`'s `POLL_INTERVAL_MS` / `ANALYSIS_STEPS` machinery, reused verbatim, not
      reimplemented).
- [ ] 7.3 **[RED]** Extend/add a frontend component test over the **existing** overlay — asserts it renders the
      existing staged progress (`Queued → Fetching content → Content ready → AI analysis`) and the existing failure
      state for that one analysis, with mocked poll responses; asserts there is no partial-batch or multi-candidate
      failure UI, because only one candidate was ever submitted (CD-10 scenario 2).
- [ ] 7.4 **[GREEN]** Wire completion: on success, the resulting `AnalysisId` flows unchanged into the existing
      generation rails (format choice, package generation) — confirm no new state is introduced on that path.
- [ ] 7.5 Run the frontend test suite — WU-7 shortlist/overlay tests green; no backend test command applies (no
      backend code on this path).

---

## WU-8 — `own` Alias Fix + Workflow Wiring (End-to-End)

*Refs: CR-DISC-01, CR-DISC-02.*

- [ ] 8.1 **[RED]** Extend `ContentWorkflowServiceTests.cs` — `references` and `niche` criteria consult different sources for the same client (CR-DISC-01 scenario); selecting `own` no longer returns `SearchCriterionInvalid` and consults the own-account source (CR-DISC-02 scenario 1); `own` on a TikTok-only client returns an empty, non-throwing result with reported shortfall (CR-DISC-02 scenario 2). (`Incoders.Hive.Tests/Application/Social/Workflow/ContentWorkflowServiceTests.cs`)
- [ ] 8.2 **[GREEN]** Add `"own"`/`"propios"` entries to `ContentWorkflowCatalog.SearchCriterionAliases`. (`Application/Features/Social/ContentStudio/.../ContentWorkflowCatalog.cs`)
- [ ] 8.3 **[GREEN]** Modify `InMemoryContentWorkflowService.DiscoverResultsAsync` (line ~130) to pass the session's normalized `SearchCriterion` into `ContentDiscoveryRequest` and consume `plan.Candidates`/`plan.Shortfall`/`plan.FailedSourceKeys`/`plan.UnconfiguredSourceKeys`. (`Infrastructure/Social/Workflow/InMemoryContentWorkflowService.cs`)
- [ ] 8.4 Run `dotnet test` — full suite green; `own` criterion works end to end.

---

## WU-9 — ADR Verification, Docs Sync, Final Gates

*Refs: repo `CLAUDE.md` docs-sync MUST; `openspec/config.yaml` `require_adr_for_architecture_changes`.*

- [ ] 9.1 Verify ADR `docs/architecture/adr/0012-content-discovery-source-chain-and-candidate-persistence.md` (re-amended 2026-08-09) still matches the shipped decisions (D1–D13, DI replacement, breaking contract incl. `UnconfiguredSourceKeys`/`SourceDiscoveryResult`, two tables, scorer/classifier, configuration-driven niche-search variant with no literal actor id, single-selection instant analysis, Domain untouched). Do NOT rewrite it — flag any drift only if implementation deviated.
- [ ] 9.2 Update `docs/quality-gates.md` / relevant `docs/` and `.github/project-ai/instructions/*.md` if discovery behavior changes anything documented there (per repo docs-sync MUST).
- [ ] 9.3 Merge the three delta specs into `openspec/specs/content-discovery/spec.md` (new), `openspec/specs/content-research/spec.md`, `openspec/specs/integrations/spec.md` per `sdd-archive` convention (or leave for the archive phase per project workflow — confirm which phase owns the merge before editing `openspec/specs/`).
- [ ] 9.4 Update `state.yaml` — `status: apply-complete`, `artifacts.tasks` path, `validation.gates` result.
- [ ] 9.5 Run `pwsh -File scripts/gates/Run-Gates.ps1` — MUST exit 0 before declaring the change complete; report the gate summary.

---

## Definition of Done

- [ ] WU-0 fixture recorded and committed (no actor id in the fixture filename or contents' identifying metadata); `playCount` present (or owner escalation resolved) before WU-6 started.
- [ ] WU-1–WU-3 contract change lands as one coordinated unit, including `ContentDiscoveryPlan.UnconfiguredSourceKeys` and `SourceDiscoveryResult`; solution compiles.
- [ ] `GetServices<IContentDiscoveryService>().Count() == 1` passes (DI replacement, not supplement).
- [ ] `ApifyActorMappingBackfill.Transform` has zero diff; never produces a `niche-search` entry.
- [ ] No actor-id-shaped literal exists in shipped `Incoders.Hive.Application` or `Incoders.Hive.Infrastructure` source (the WU-6.1 repository-wide guard passes).
- [ ] `content-discovery` analysis type + `discover-niche-content` step resolve under the existing `generator` workflow key; `generatorChecklist` carries the new optional item; the discovery stage is reachable from the integrations settings UI (INT-DISC-02/03).
- [ ] Operator selects exactly one candidate from the shortlist; that selection reuses the existing single-analysis start endpoint and the existing polling overlay with no new backend transport (CD-10).
- [ ] `own` criterion resolves end to end with no `SearchCriterionInvalid`.
- [ ] `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj -c Release` passes, 0 failures.
- [ ] Architecture gate green: no Apify/EF/Npgsql/ASP.NET types in Domain or Application.
- [ ] `pwsh -File scripts/gates/Run-Gates.ps1` exits 0.
- [ ] ADR 0012 verified against shipped decisions; docs/specs synced.
