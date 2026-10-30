# Tasks: content-discovery-intelligence

**Branch:** single feature branch off `dev`; one PR at the end (`size:exception` accepted).
**TDD mode:** STRICT — `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj -c Release`. Every behavioural task: RED (failing test from a spec scenario) → GREEN (implementation) → run suite.
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
- [ ] 0.3 **Exit criterion**: save one sanitized raw response as `SocialStudio.Tests/Infrastructure/Social/Fixtures/tiktok-niche-search.json` — a *capability*-named file, not an actor-named one. WU-6 tasks are BLOCKED until this file exists and is committed.
- [ ] 0.4 If `playCount` is absent from the response, STOP and escalate to the owner (design §7 escalation rule: unrankable source, do not ship) instead of proceeding to WU-6. If `createTimeISO` or share/save counts are absent, note the degradation (candidates become `Undetermined`; those fields excluded from the scorer denominator) and proceed.
- [ ] 0.5 Note the shape of an unmappable/empty response too (design §7.1) — this informs WU-6's "zero-of-many mapped → `Failed`" and "partial → `Ran`" test cases.

---

## WU-1 — Coordinated Breaking Contract Change

*Refs: D1, D2, D12, CD-04, CD-13; ADR 0012 §3; design §3.1, §3.2, §11 step 1. Amended 2026-08-09: `ContentDiscoveryPlan`
now carries `UnconfiguredSourceKeys` and the port's per-source result becomes `SourceDiscoveryResult` — folded into
this unit rather than left for WU-2, since it is part of the same breaking DTO family and must land in the same
compiler-enforced commit.*

- [x] 1.1 **[RED]** Add `ContentDiscoveryPlanTests.cs` — assert `ContentDiscoveryPlan(Candidates, Shortfall, FailedSourceKeys, UnconfiguredSourceKeys)` 4-field shape (D12/CD-13). (`SocialStudio.Tests/Application/Abstractions/Social/ContentDiscoveryPlanTests.cs`)
- [x] 1.2 **[RED]** Extend/add `DiscoveredContentTests.cs` — assert `Views`/`Likes`/`Comments` are `long?`; assert `Shares`, `Saves`, `PublishedAtUtc`, `SourceKey` exist and default to unavailable (`null`), never `0`. (`SocialStudio.Tests/Application/Abstractions/Social/DiscoveredContentTests.cs`)
- [x] 1.3 **[RED]** Add `SourceDiscoveryResultTests.cs` — assert `SourceDiscoveryResult(Outcome, Candidates)` with `Outcome` a three-valued `SourceOutcome` (`Ran`/`Unconfigured`/`Failed`); `Candidates` empty unless `Ran`. (`SocialStudio.Tests/Application/Abstractions/Social/SourceDiscoveryResultTests.cs`)
- [x] 1.4 **[GREEN]** Modify `DiscoveredContent`, `ContentDiscoveryRequest` (add `SearchCriterion`), `IContentDiscoveryService` (return `ContentDiscoveryPlan`); add `ContentDiscoveryPlan` record (4 fields) and `SourceDiscoveryResult`/`SourceOutcome`. (`Application/Abstractions/Social/ContentDiscovery/IContentDiscoveryService.cs`)
- [x] 1.5 **[RED]** Update `ReferenceAccountContentDiscoveryTests.cs` for the new nullable shape; assert the `post.VideoViewCount ?? 0` zero-fill is gone (raw `post.VideoViewCount`). Landed renamed as `ReferenceAccountDiscoverySourceTests.cs` per WU-3 (moved together in this coordinated unit rather than as a two-step rename). (`SocialStudio.Tests/Application/Social/ContentGeneration/Discovery/ReferenceAccountDiscoverySourceTests.cs`)
- [x] 1.6 Confirmed: WU-1–WU-3 landed together as one coordinated unit; `dotnet test` was RED (missing types) until the full set of production files existed, then GREEN as one shot — the intended compiler-enforced atomicity, not a partial commit.

---

## WU-2 — Chain, Dedup Identity, DI Replacement, Composition Test

*Refs: CD-01, CD-02, CD-03, D3, D4, D5; ADR 0012 §1; design §3.2, §4.*

- [x] 2.1 **[RED]** Add `CandidateIdentityTests.cs` — table test: scheme/host case-insensitive, drop `www.`, drop query/fragment, trim trailing `/`, network-key prefix. (`SocialStudio.Tests/Application/Social/ContentGeneration/Discovery/CandidateIdentityTests.cs`)
- [x] 2.2 **[GREEN]** Create `CandidateIdentity.From(network, url)` pure function. (`Application/Abstractions/Social/ContentDiscovery/CandidateIdentity.cs`)
- [x] 2.3 **[RED]** Add `ContentDiscoveryChainTests.cs` — CD-01 remainder-only asks + one-source-failure tolerance + `Shortfall`/`FailedSourceKeys`; CD-02 cross-source dedup; CD-03 `niche`→niche-only, `own`→own-only, `viral`/`competitors`→all sources (D5), never `SearchCriterionInvalid`; **CD-13/D12: `Unconfigured` lands in `UnconfiguredSourceKeys`, `Ran`-with-zero lands in neither list** — two fake sources, one per outcome, assert the two lists are disjoint. (`SocialStudio.Tests/Application/Social/ContentGeneration/Discovery/ContentDiscoveryChainTests.cs`)
- [x] 2.4 **[GREEN]** Create `IContentDiscoverySource` port (`SourceKey`, `Order`, `Supports`, `DiscoverAsync` returning `Task<SourceDiscoveryResult>` — the DTO created in WU-1.4). (`Application/Abstractions/Social/ContentDiscovery/IContentDiscoverySource.cs`)
- [x] 2.5 **[GREEN]** Create `ContentDiscoveryChain` implementing `IContentDiscoveryService` per the algorithm in design §3.2. (`Application/Features/Social/ContentGeneration/Discovery/ContentDiscoveryChain.cs`)
- [x] 2.6 **[RED]** Add `DiscoveryCompositionTests.cs` — build the real container via `AddInfrastructure`; assert (a) `GetRequiredService<IContentDiscoveryService>()` is `ContentDiscoveryChain`, (b) `GetServices<IContentDiscoveryService>().Count() == 1` — **write this assertion first**, it is the one that catches a silent DI override, (c) `GetServices<IContentDiscoverySource>()` keys — narrowed to `{references, own}` for this apply slice, since the third source (`niche`, WU-6) is out of scope and not yet registered; the test carries a comment flagging that a third key must be added once WU-6 lands, (d) `Order` values distinct. (`SocialStudio.Tests/Infrastructure/DependencyInjection/DiscoveryCompositionTests.cs`)
- [x] 2.7 **[GREEN]** In `DependencyInjection.cs`, **REPLACED** (not supplemented) `services.AddScoped<IContentDiscoveryService, ReferenceAccountContentDiscovery>()` with source registrations + `services.AddScoped<IContentDiscoveryService, ContentDiscoveryChain>()`. (`Infrastructure/DependencyInjection/DependencyInjection.cs`)
- [x] 2.8 Ran `dotnet test` — WU-2 chain/composition tests green (two sources registered; the third, niche, lands with WU-6).

---

## WU-3 — Reference + Own Discovery Sources

*Refs: CD-01 (adapted), CR-DISC-02; ADR 0012 §1; design §2.2.*

- [x] 3.1 **[GREEN]** Moved `ReferenceAccountContentDiscovery.cs` → `ReferenceAccountDiscoverySource` implementing `IContentDiscoverySource`, `Order: 1`, `Supports("references")`; removed the `?? 0` zero-fill. (`Application/Features/Social/ContentGeneration/Discovery/ReferenceAccountDiscoverySource.cs`)
- [x] 3.2 Moved/renamed its test file to `ReferenceAccountDiscoverySourceTests.cs`, updated for nullable metrics. (`SocialStudio.Tests/Application/Social/ContentGeneration/Discovery/ReferenceAccountDiscoverySourceTests.cs`)
- [x] 3.3 **[RED]** Add `OwnAccountDiscoverySourceTests.cs` — near-clone assertions over `IClientOwnAccountRepository` + `IProfileScrapeResolver` (+ `ISocialNetworkRepository`, needed to resolve `ClientOwnAccount.SocialNetworkId` to a network code — not anticipated by the task text, but required by the actual `ClientOwnAccount` shape); assert `Order: 2`, `Supports("own")` only; assert empty non-throwing result for a TikTok-only client (CR-DISC-02 scenario 2). (`SocialStudio.Tests/Application/Social/ContentGeneration/Discovery/OwnAccountDiscoverySourceTests.cs`)
- [x] 3.4 **[GREEN]** Create `OwnAccountDiscoverySource` over `IClientOwnAccountRepository` + `ISocialNetworkRepository` + `IProfileScrapeResolver`. (`Application/Features/Social/ContentGeneration/Discovery/OwnAccountDiscoverySource.cs`)
- [x] 3.5 Ran `dotnet test` — WU-1+2+3 fully green; solution compiles (coordinated unit closes). Full suite: 2972 passed, 0 failed, 2 skipped (was 2941 passed at baseline).

---

## WU-4 — Opportunity Scorer, Classifier, Baseline Provider

*Refs: CD-04, CD-05, CD-06, CD-07, CD-09; design §6.*

- [x] 4.1 **[RED]** Add `OpportunityScorerTests.cs` — determinism (same input twice → same score, fixed `nowUtc` param); availability-aware denominator (missing signal excluded, not zeroed); degenerate-set fallback to `0.5`. (`SocialStudio.Tests/Application/Social/ContentGeneration/Discovery/OpportunityScorerTests.cs`)
- [x] 4.2 **[GREEN]** Create pure `OpportunityScorer.Score(set, baselines, nowUtc)` per weights table in design §6.1. (`Application/Features/Social/ContentGeneration/Discovery/OpportunityScorer.cs`)
- [x] 4.3 **[RED]** Add `TrendingProvenClassifierTests.cs` — null `PublishedAtUtc` → `Undetermined` FIRST and unconditionally (never defaulted to `Proven`/`Trending`); recent+high-velocity → `Trending`; old+high-total → `Proven`. (`SocialStudio.Tests/Application/Social/ContentGeneration/Discovery/TrendingProvenClassifierTests.cs`)
- [x] 4.4 **[GREEN]** Create `TrendingProvenClassifier` per design §6.3. (`Application/Features/Social/ContentGeneration/Discovery/TrendingProvenClassifier.cs`)
- [x] 4.5 **[RED]** Add `CoOccurrenceSignalTests.cs` — tokenizer (lowercase, strip punctuation, drop <4-char tokens, ES+EN stopwords); K=3 threshold; distinct-author counting; normalization by set author count. (`SocialStudio.Tests/Application/Social/ContentGeneration/Discovery/CoOccurrenceSignalTests.cs`)
- [x] 4.6 **[GREEN]** Implement co-occurrence signal inside `OpportunityScorer` per design §6.4. Landed as a standalone `CoOccurrenceSignal.Compute(set)` static class consumed by `OpportunityScorer`, rather than a private method, so it could be unit-tested in isolation per 4.5. (`Application/Features/Social/ContentGeneration/Discovery/CoOccurrenceSignal.cs`)
- [x] 4.7 **[RED]** Add `AccountBaselineProviderTests.cs` — fresh-cache hit → 0 scrapes (CD-09 scenario 1); stale cache → 1 recompute (CD-09 scenario 2); no baseline + not TOP-N → relative-performance omitted or candidate-set median, never a fabricated value (CD-07 scenario 2); ≤N scrapes for a 100-candidate set (design §6.5 economics test). (`SocialStudio.Tests/Application/Social/ContentGeneration/Discovery/AccountBaselineProviderTests.cs`)
- [x] 4.8 **[GREEN]** Create `AccountBaselineProvider.GetOrComputeAsync(org, network, handle, nowUtc)` — read-through cache over `IAccountBaselineRepository` + `IProfileScrapeResolver` + `ProfileMetricCalculator`. Required creating `IAccountBaselineRepository` (Abstractions) and the shared `AccountBaseline` record ahead of WU-5's EF implementation, since the scorer/classifier/provider all need the type. (`Application/Features/Social/ContentGeneration/Discovery/AccountBaselineProvider.cs`, `Application/Abstractions/Social/ContentDiscovery/{AccountBaseline,IAccountBaselineRepository}.cs`)
- [x] 4.9 Run `dotnet test` — WU-4 scorer/classifier/baseline tests green (22 new tests, all passing).

---

## WU-5 — Persistence: Entities, Migration, Repositories

*Refs: CD-08, CD-09; ADR 0012 §4; design §5.*

- [x] 5.1 **[GREEN]** Create `DiscoveredCandidateData` entity (schema `social`, snake_case, `organization_id`/`client_id` columns, nullable metrics per design §5.1). (`Infrastructure/Persistence/Postgres/Entities/DiscoveredCandidateData.cs`)
- [x] 5.2 **[GREEN]** Create `AccountBaselineData` entity (schema `social`, unique `(organization_id, network_key, lower(author_handle))`, no `client_id`, per design §5.2). Uniqueness on `lower(author_handle)` is enforced by normalizing the handle to lowercase in the repository before every write (`EfAccountBaselineRepository.Normalize`) plus a plain (non-functional) unique index on the already-lowercased column — EF Core has no first-class functional-index mapping here. (`Infrastructure/Persistence/Postgres/Entities/AccountBaselineData.cs`)
- [x] 5.3 **[GREEN]** Map both entities in `AppIdentityDbContext.cs` in the `content_generation_batches` style, including the `ix_discovered_candidates_org_client_key`, `ix_discovered_candidates_org_observed`, and unique `ux_discovered_candidates_key_observed` indexes. (`Infrastructure/Identity/AppIdentityDbContext.cs`)
- [x] 5.4 Generated migration `20260810061801_Add_ContentDiscoveryCandidatesAndAccountBaselines` via `dotnet ef migrations add` into `SocialStudio.Infrastructure/Persistence/Postgres/Migrations/`, including `.Designer.cs` and the updated `AppIdentityDbContextModelSnapshot.cs`. Purely additive; no backfill.
- [x] 5.5 **[RED]** Add `EfContentCandidateRepositoryTests.cs` — round-trip; a double-write of the same `(candidate_key, observed_at)` inside one call is deduplicated, not silently duplicated (CD-08 scenario); a later run with a new `observed_at` creates a second row, not a duplicate identity; tenant scoping. Uses the repo's existing EF InMemory-provider test-host pattern (`ClientReferentAccountRepositoryTests`/`EfContentGenerationBatchRepositoryTests`) — InMemory does not enforce a real Postgres unique index, so the "rejects a double-write" guarantee is enforced at the repository boundary (defense-in-depth with the real DB unique index for production). (`SocialStudio.Tests/Infrastructure/Persistence/Postgres/Repositories/EfContentCandidateRepositoryTests.cs`)
- [x] 5.6 **[GREEN]** Create `IContentCandidateRepository` port + `EfContentCandidateRepository` (`RecordObservationsAsync`, `GetLatestByKeysAsync`). (`Application/Abstractions/Social/ContentDiscovery/IContentCandidateRepository.cs`, `Infrastructure/Persistence/Postgres/Repositories/EfContentCandidateRepository.cs`)
- [x] 5.7 **[RED]** Add `EfAccountBaselineRepositoryTests.cs` — upsert idempotence per `(org, network, handle)`; case-insensitive handle lookup; missing author returns null, never fabricated. (`SocialStudio.Tests/Infrastructure/Persistence/Postgres/Repositories/EfAccountBaselineRepositoryTests.cs`)
- [x] 5.8 **[GREEN]** Create `IAccountBaselineRepository` port (landed in WU-4.8 ahead of schedule, since the provider needed the type) + `EfAccountBaselineRepository` (`GetAsync`, `UpsertAsync`). (`Application/Abstractions/Social/ContentDiscovery/IAccountBaselineRepository.cs`, `Infrastructure/Persistence/Postgres/Repositories/EfAccountBaselineRepository.cs`)
- [x] 5.9 Registered both repositories + `AccountBaselineProvider` in Infrastructure DI. (`Infrastructure/DependencyInjection/DependencyInjection.cs`)
- [x] 5.10 Run `dotnet test` — WU-5 persistence tests green against the EF test-host pattern (8 new tests). Full suite: 2998 passed / 0 failed / 2 skipped (was 2972 at the start of this run).

---

## WU-6 — Niche Source + Apify Adapter + Configuration-Driven Actor Routing (depends on WU-0)

*Refs: INT-DISC-01/02/03/04, CD-01 (niche), CD-12, CD-13, D11, D12, D13; ADR 0012 §6; design §7, §7.1, §8.1. BLOCKED
until WU-0.3 fixture exists. Amended 2026-08-09: gained the repository-wide no-actor-literal guard (written first),
the `content-discovery` analysis type + `discover-niche-content` step, the optional checklist item, and the seeded
placeholder mapping.*

*Placement corrected 2026-08-09 (owner correction 3, `cdi-wu6-relocate-003`): `content-discovery` moved from the
`generator` workflow key to the **existing `single-analyzer`** workflow key, as a sibling of
`content-analysis`/`profile-analysis`, holding ONLY `discover-niche-content` — the duplicated `analyze-content`/
`generate-content`/`generate-image` steps that had been copied onto it under `generator` are REMOVED; that pipeline
is reused from `content-analysis`, not duplicated. The checklist item moved to `singleAnalyzerChecklist` and renamed
`generator-niche-search-actor` → `single-analyzer-niche-search-actor`. The seeder's `WorkflowKey` changed
`generator` → `single-analyzer`; it reads existing `single-analyzer` rows and concatenates before calling
`ReplaceWorkflowAsync`, so real operator configuration for `content-analysis`/`profile-analysis` is never wiped —
this is the single most dangerous line in this work unit and is covered by a dedicated regression test. Tasks 6.10,
6.11, 6.12, 6.13, 6.14 below are updated in place to reflect the corrected placement; the future 6.5/6.6 resolver
task text below should reference `single-analyzer` too when implemented.*

- [x] 6.1 **[RED]** Add the repository-wide **no-actor-literal guard** — assert no actor-id-shaped literal (17-char
      alphanumeric token pattern, and no `"clockworks/…"` or similar vendor-actor-name literal) exists in shipped
      `SocialStudio.Application` or `SocialStudio.Infrastructure` source, outside test fixtures (CD-12/D13, design
      §11 Guard row, §13 risk). Written **first**, before any niche-search code — it is the assertion that makes "no
      forced actor" enforceable rather than aspirational. If the naive regex false-positives on unrelated
      17-character tokens elsewhere in the codebase, tighten it to the Apify call-site files
      (`ApifyProfileScrapeResolver.cs`, `ApifyNicheSearchResolver.cs`, `TikTokNicheSearchAdapter.cs`,
      `ApifyActorMappingLookup.cs`) rather than deleting the guard. (`SocialStudio.Tests/Architecture/NoActorLiteralGuardTests.cs`)
- [x] 6.2 **[RED]** Add `TikTokNicheSearchAdapterTests.cs` — `MapItem` against the WU-0 recorded fixture only, no live call; assert degradation when `createTimeISO`/share/save absent per design §7 table; assert an unmappable item is **skipped**, not defaulted; assert **zero-of-many mapped → source `Failed`**; assert a partial map → source `Ran` with what mapped (design §7.1). (`SocialStudio.Tests/Infrastructure/Social/Fixtures` consumer test)
- [x] 6.3 **[GREEN]** Create `TikTokNicheSearchAdapter` — input build (`searchQueries`/`hashtags`) + `MapItem`, per-item `JsonException` caught, never thrown. (`Infrastructure/Social/Providers/Apify/TikTokNicheSearchAdapter.cs`)
- [x] 6.4 **[GREEN]** Extract `ApifyActorMappingLookup.GetAllMappedActors` out of `ApifyProfileScrapeResolver` (delegates, keeps its existing tests green). (`Infrastructure/Social/Providers/ApifyActorMappingLookup.cs`)
- [x] 6.5 **[RED]** Add `ApifyNicheSearchResolverTests.cs` — `(tiktok, niche-search)` lookup succeeds with a configured mapping, returning the configured id read from configuration, never a literal (CD-12 scenario 1); missing/empty entry returns `SourceOutcome.Unconfigured` — a structured configuration failure, never an unhandled exception and never a substituted literal id (INT-DISC-01 scenario 2, CD-12 scenario 2), using the `ApifyProfileScrapeResolverTests` fake-handler pattern. (`SocialStudio.Tests/Infrastructure/Social/Providers/ApifyNicheSearchResolverTests.cs`)
- [x] 6.6 **[GREEN]** Create `INicheSearchResolver` port + `ApifyNicheSearchResolver` — resolves via `IWorkflowConfigurationResolver.ResolveStepAsync` first, falls back to `GetAllMappedActors(actor_mappings_json, "tiktok", "niche-search")`, and returns `Unconfigured` (never a literal) when both are empty, per design §8.1 resolution order. (`Application/Abstractions/Social/ContentDiscovery/INicheSearchResolver.cs`, `Infrastructure/Social/Providers/ApifyNicheSearchResolver.cs`)
- [x] 6.7 **[RED]** Extend `ContentDiscoveryChainTests.cs` (or add `TikTokNicheSearchSourceTests.cs`) — missing `niche-search` route reports its key in `UnconfiguredSourceKeys`, **not** `FailedSourceKeys` (CD-13, D12); other sources still run (INT-DISC-01 scenario 2 + CD-01). (`SocialStudio.Tests/Application/Social/ContentGeneration/Discovery/TikTokNicheSearchSourceTests.cs`)
- [x] 6.8 **[GREEN]** Create `TikTokNicheSearchSource` — `Order: 3`, `Supports("niche")`, consumes `INicheSearchResolver` + `TikTokNicheSearchAdapter`, returns `SourceDiscoveryResult` with the correct `Outcome`. (`Application/Features/Social/ContentGeneration/Discovery/TikTokNicheSearchSource.cs`)
- [x] 6.9 **[GREEN]** Register `TikTokNicheSearchSource` in `DependencyInjection.cs` (added to the WU-2.7 replacement block, `Order: 3`).
- [x] 6.10 **[RED]** Add/extend `WorkflowConfigurationCatalogTests.cs` — `single-analyzer` → `content-discovery` (new `WorkflowAnalysisTypeDefinition`) → `discover-niche-content` (new `WorkflowStepDefinition`, and the ONLY step under this analysis type) resolves via `FindStep`, carries `operationKey: "niche-search"`, `RequiresNetwork: true`, `SupportedNetworkKeys: ["tiktok"]`, `AllowedProviderCategories: [DataExtraction]`, `AllowedProviderKeys: ["apify"]`, `SupportsActorSelection: true`, `ActorVariantKey: "niche-search"` (INT-DISC-02, D11 corrected). Landed as `SocialStudio.Tests/Application/Integrations/WorkflowConfigurationCatalogContentDiscoveryStepTests.cs`, extended with explicit no-duplicated-AI-steps assertions.
- [x] 6.11 **[GREEN]** Add the `content-discovery` analysis type (with ONLY the `discover-niche-content` step) under the **existing** `single-analyzer` workflow key in `WorkflowConfigurationCatalog` per design §8.1 (corrected) — sibling of `content-analysis`/`profile-analysis`, explicitly NOT under `generator` and not a new top-level workflow key (D11 corrected: same downstream pipeline once a candidate becomes a `SingleContentAnalysis`, so the AI steps are reused from `content-analysis`, never duplicated). (`Application/Features/Integrations/WorkflowConfigurations/WorkflowConfigurationCatalog.cs`)
- [x] 6.12 **[RED]** Extend `OrganizationIntegrationsFlowTests.cs` (the actual `GetOrganizationIntegrationsHandler` test host) — `singleAnalyzerChecklist` contains a new optional item `("single-analyzer-niche-search-actor", "Configure a TikTok niche-search actor", Required: false)`, satisfied when an enabled `(single-analyzer, content-discovery, discover-niche-content, niche-search, tiktok)` route resolves (INT-DISC-03); unsatisfied by default; unaffected by a stray `generator`-keyed row; a pre-existing "all items completed" assertion updated to check `IsRequired` items only, since the new item is intentionally optional.
- [x] 6.13 **[GREEN]** Append the `single-analyzer-niche-search-actor` checklist item to `singleAnalyzerChecklist` in `GetOrganizationIntegrationsHandler` (additive; `Required: false`). Satisfaction is resolved via a new optional `IWorkflowStepConfigurationRepository? stepConfigurations` constructor dependency (defaults to `null`, so existing callers/tests are unaffected); when present, it checks for an enabled `(single-analyzer, content-discovery, discover-niche-content, niche-search, tiktok)` row with a non-empty `ActorId`.
- [x] 6.14 **[GREEN]** Seed a placeholder, **disabled**, **unassigned** `discover-niche-content` step configuration as **data** via a new `TikTokNicheSearchWorkflowStepSeeder` (mirrors `ComfyUiOrganizationSeeder`/`ClaudePromptCliOrganizationSeeder`), bound to the organization's already-connected Apify integration with `ActorId: null` and `IsEnabled: false` — the operator only has to choose the actor and enable it. Its `WorkflowKey` is `single-analyzer`; because that key already carries REAL operator configuration for `content-analysis`/`profile-analysis`, and `ReplaceWorkflowAsync` replaces ALL rows for the (org, workflowKey) pair, the seeder reads existing `single-analyzer` rows and concatenates the new placeholder onto them before calling `ReplaceWorkflowAsync` — never a partial-set call. Skips organizations with no connected data-extraction integration; idempotent — never overwrites an existing row for this step (operator-configured or previously seeded). Wired into `Program.cs` startup seeding and registered in `DependencyInjection.cs`. (`Infrastructure/Integrations/TikTokNicheSearchWorkflowStepSeeder.cs`, `TikTokNicheSearchWorkflowStepSeederExtensions.cs`)
  - [x] 6.14a **[RED→GREEN, added in the placement-correction run]** Regression test: an organization with EXISTING operator-configured `single-analyzer` rows for `content-analysis` (`scrape-video`) and `profile-analysis` (`scrape-profile`) keeps every one of them, unchanged, after the seeder runs — plus the new seeded `discover-niche-content` row. Landed as `TikTokNicheSearchWorkflowStepSeederTests.DoesNotDisturb_ExistingOperatorConfiguredContentAnalysisAndProfileAnalysisSteps`.

  **Amendment note (this partial apply run, 2026-08-09):** only 6.10–6.14 were implemented in this
  work-unit slice, by explicit owner scope. 6.1–6.9 and 6.15–6.16 (no-actor-literal guard,
  `TikTokNicheSearchAdapter`, `ApifyActorMappingLookup` extraction, `INicheSearchResolver` +
  `ApifyNicheSearchResolver`, `TikTokNicheSearchSource`, its DI registration, and the WU-6 full-suite
  run) remain unimplemented and unblocked-but-pending for a later apply pass. WU-0 (the schema spike)
  is still open and still blocks 6.1–6.9.

  **Placement-correction note (2026-08-09, `cdi-wu6-relocate-003`):** 6.10–6.14 were RELOCATED from
  `generator` to `single-analyzer` in this run — RED (updated tests expecting `single-analyzer` +
  no duplicated AI steps) confirmed failing against the stale `generator`-placed code, then GREEN
  (catalog/handler/seeder edits) restored green, plus the new 6.14a regression test. 6.1–6.9 and
  6.15–6.16 remain unimplemented, as before.

  **Network-widening correction (2026-08-10, owner-directed, `cdi-core-impl-002`):** 6.10's
  `discover-niche-content` step now carries `SupportedNetworkKeys: ["instagram", "tiktok", "youtube"]`
  (was `["tiktok"]`), matching `scrape-video`'s CONFIGURATION surface exactly. Restricting the
  configurable-network list to TikTok was artificial; what is genuinely limited today is which
  per-network discovery ADAPTER exists (only TikTok, via WU-6's remaining 6.1–6.9). A network
  configured without a working adapter degrades through the `Unconfigured`/unsupported path at
  runtime, never a crash. `WorkflowConfigurationCatalogContentDiscoveryStepTests` was updated
  RED→GREEN for the widened set (test renamed
  `DiscoverNicheContentStep_RequiresNetwork_SupportingTheSameNetworksAsScrapeVideo`). The owner will
  configure only TikTok for now; Instagram/YouTube niche-search adapters remain unbuilt follow-ups.
- [x] 6.15 Confirm `ApifyActorMappingBackfill.Transform` is untouched — no diff in that file, and it never produces a `niche-search` entry (design explicit "do not modify"; INT-DISC-04).
- [x] 6.16 Run `dotnet test` — WU-6 niche source/adapter/resolver/catalog/checklist tests green (114 tests
      in the Discovery/NicheSearch/NoActorLiteralGuard/ApifyProfileScrapeResolver filter, 0 failed); the
      no-actor-literal guard (6.1) passes, narrowed to the four Apify call-site files per the design's own
      caveat (a repository-wide bare-token regex false-positives on ordinary 17-char C# identifiers
      `CancellationToken`/`IntegrationStatus`/`actorMappingsJson` and XML doc `cref` tokens — the guard
      matches only QUOTED string literals in the guarded files, which eliminates that false-positive class
      without weakening the check); composition test now asserts exactly one `IContentDiscoveryService`
      with three sources (`references`, `own`, `niche`).

  **Owner-waived-spike note (2026-08-10, `cdi-final-001`):** WU-0's schema spike (a real Apify call
  against an operator-configured actor) was explicitly waived by the owner — no actor is configured yet.
  6.1–6.9/6.15/6.16 were implemented anyway against a DOCUMENTATION-DERIVED fixture
  (`SocialStudio.Tests/Infrastructure/Social/Fixtures/tiktok-niche-search.json`, same verified TikTok
  field family as `apify-tiktok-item.json`/obs 426), not a verified live response. Every metric stays
  nullable; every absent field degrades to `null` rather than throwing or zero-filling; the field mapping
  is isolated to one place (`TikTokNicheSearchAdapter.MapItem`) so a single correction fixes it after the
  first real call. Both the adapter and the fixture carry an explicit "PENDING LIVE VERIFICATION" comment.
  Without a configured actor, `ApifyNicheSearchResolver.SearchAsync` correctly returns
  `SourceOutcome.Unconfigured` today — the specified behaviour, not a defect.

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

> **Test-infra note (this apply run, `cdi-final-001`):** the repo has NO React component-test harness —
> `apps/web/package.json`'s `test` script is `tsx --test "lib/**/*.test.ts"` only (no jsdom, no
> `@testing-library/react`, no `.tsx` glob anywhere in the frontend test suite). "Frontend component test"
> as originally written in this task is not a runnable surface in this project. Task 7.1's behavioural
> assertions (exactly-one-selection-replace, exactly-one-payload-to-the-existing-endpoint) were instead
> extracted into pure, framework-free TypeScript and covered by the project's actual test convention.

- [x] 7.1 **[RED→GREEN]** Added `apps/web/lib/social/content-discovery-shortlist.test.ts` — asserts
      `selectSingleCandidate` REPLACES (never adds to) the current selection and toggles off on
      re-selecting the same candidate; asserts `buildAnalyzeSelectedCandidatePayload` returns the EXACT
      `{ url, clientId? }` shape `analyzeSingleContent` already sends to the existing
      `/api/social/single-analyzer/analyze` endpoint, `null` when nothing is selected, and `null` (never a
      fabricated candidate) for an unknown selected id — 7 tests, all green (CD-10 scenario 1 logic).
- [x] 7.2 **[GREEN]** Implemented `apps/web/components/social/discovery/content-discovery-shortlist.tsx` —
      renders the ranked candidates (unchanged from WU-4 scoring/classification, passed in as props), single
      selection via the pure lib above, and "Analyze this one" calls the EXISTING `analyzeSingleContent`
      (exactly one POST, no new endpoint, no new request shape) and hands the returned `id` to an
      `onAnalysisStarted` callback prop.

  **Mount (this apply run, `cdi-wu7-mount-001`):** the shortlist is now reachable and usable in the
  running app. `single-analyzer-client.tsx`'s "By Search" input mode is REWIRED: the previous MOCK
  path (`findBestMatch`/`getMockVideoBySearch`) is removed; the operator instead picks a client + a
  discovery criterion (references/own/niche) and clicks **Discover**. Discovery runs through the
  EXISTING content-workflow endpoints (`startContentWorkflow` → poll `getContentWorkflowStatus` →
  `listContentWorkflowResults`) — the only HTTP surface that already exercises
  `IContentDiscoveryService` end to end, so no new backend endpoint was authored. New pure logic in
  `apps/web/lib/social/content-discovery-shortlist.ts`: `mapDiscoveryResultsToShortlistCandidates`
  (projects `ContentWorkflowResult` → shortlist candidate shape) and
  `isSingleAnalyzerNicheSearchActorConfigured` / `isNicheSearchUnconfigured` — since
  `ContentDiscoveryPlan.UnconfiguredSourceKeys` is not surfaced onto the workflow session response
  (WU-8.3), the "niche" criterion's unconfigured state is instead read from the EXISTING
  `/api/integrations/organization` checklist overview (`single-analyzer` group,
  `single-analyzer-niche-search-actor` item, added in WU-6.13) — same underlying fact
  (CD-13/D12: unconfigured vs. an empty-but-ran result), surfaced through an existing endpoint
  instead of a new one. When unconfigured, the UI shows a distinct banner with a link to
  `/settings/integrations` rather than an empty shortlist. Selecting exactly one candidate and
  clicking "Analyze this one" hands the returned analysis id to `handleDiscoveryAnalysisStarted`,
  which reuses the file's EXISTING `pollUntilTerminal` + `AnalysisOverlay` (`analysisStatus` state,
  `ANALYSIS_STEPS`, `POLL_INTERVAL_MS`) verbatim — intentionally duplicating the small
  poll-then-finalize block from `loadVideoByUrl` rather than refactoring that already-working
  function, per the "smallest coherent insertion" constraint on a ~4,000-line file.
- [x] 7.3 Selecting a candidate and clicking "Analyze this one" reuses the EXISTING overlay
      end to end: `analysisStatus` drives the same `AnalysisOverlay` component the "By URL" path
      uses, staged through the same `ANALYSIS_STEPS`/`statusToStepIndex`/`statusToPercent` mapping,
      including the `Failed` branch (partial snapshot hydration + error banner) — no new rendering
      path, verified by code reuse (`handleDiscoveryAnalysisStarted` mirrors `loadVideoByUrl`'s
      post-`analyzeSingleContent` block) and by `npx tsc --noEmit`/`npm run build` passing clean.
- [x] 7.4 The "unconfigured" vs. "ran, found nothing" distinction (CD-13/D12) is now surfaced in the
      UI: an amber banner + Settings link for "niche" when the checklist item is incomplete, versus
      the shortlist's own existing "No candidates found yet." empty state when discovery ran and
      returned zero — two visually and semantically distinct states, never conflated.
- [x] 7.5 Ran the frontend test suite (`npx tsx --test "lib/**/*.test.ts"`) — 170 tests pass, including
      7 new tests for `mapDiscoveryResultsToShortlistCandidates`/`isSingleAnalyzerNicheSearchActorConfigured`/
      `isNicheSearchUnconfigured`; `npx tsc --noEmit` and `npm run build` both pass clean; `npm run lint`
      reports no new warnings (one pre-existing unrelated warning in this file, `formatCatalogEntries`
      unused, predates this change). Backend: `dotnet test SocialStudio.Tests.csproj -c Release` —
      3020 passed / 0 failed / 2 skipped, unchanged from the prior apply run (confirms no backend
      code was authored on this path).

---

## WU-8 — `own` Alias Fix + Workflow Wiring (End-to-End)

*Refs: CR-DISC-01, CR-DISC-02.*

- [x] 8.1 **[RED]** Extend `ContentWorkflowServiceTests.cs` — added
      `Start_WithOwnCriterion_NoLongerReturnsSearchCriterionInvalid` (CR-DISC-02 scenario 1) and
      `Start_WithOwnCriterion_PassesTheNormalizedCriterionIntoTheDiscoveryRequest` (asserts the Spanish
      alias `"propios"` normalizes to `"own"` at the discovery port boundary, via a `SpyDiscovery` fake).
      The `references`-vs-`niche`-consult-different-sources scenario (CR-DISC-01) and the TikTok-only-own
      empty-result scenario (CR-DISC-02 scenario 2) are already covered by
      `ContentDiscoveryChainTests`/`OwnAccountDiscoverySourceTests` at the chain/source level — not
      re-asserted redundantly at the workflow-service level. (`SocialStudio.Tests/Application/Social/Workflow/ContentWorkflowServiceTests.cs`)
- [x] 8.2 **[GREEN]** Added `"own"`/`"propios"` entries to `ContentWorkflowCatalog.SearchCriterionAliases`. (`Application/Features/Social/Workflow/ContentWorkflowCatalog.cs`)
- [x] 8.3 **Already landed in a prior run.** `InMemoryContentWorkflowService.DiscoverResultsAsync` already
      passes the session's normalized `SearchCriterion` into `ContentDiscoveryRequest` and consumes
      `plan.Candidates`; `Shortfall`/`FailedSourceKeys`/`UnconfiguredSourceKeys` are deliberately NOT
      surfaced onto the workflow session response — no CR-DISC-01/02 scenario requires it, and the existing
      "empty, non-throwing result" scenario is satisfied by `plan.Candidates` being empty. (`Infrastructure/Social/Workflow/InMemoryContentWorkflowService.cs`)
- [x] 8.4 Ran `dotnet test` — full suite green (3018 passed / 0 failed / 2 skipped); `own` criterion works
      end to end, no `SearchCriterionInvalid`.

---

## WU-9 — ADR Verification, Docs Sync, Final Gates

*Refs: repo `CLAUDE.md` docs-sync MUST; `openspec/config.yaml` `require_adr_for_architecture_changes`.*

- [x] 9.1 Verified the ADR against shipped decisions — matched on D1–D13, the DI replacement, the
      breaking contract (`UnconfiguredSourceKeys`/`SourceDiscoveryResult`), the two tables,
      scorer/classifier, configuration-driven niche-search routing with no literal actor id, the
      single-selection instant-analysis path, and Domain staying untouched. ONE drift found and flagged
      (not rewritten): Consequences said the live schema spike is "a blocking prerequisite before any
      adapter code is committed" — the owner explicitly waived that for this run. Appended an
      "Amendment — spike waived" section documenting the deviation and its zero-runtime-risk rationale
      (an unconfigured niche source is the specified `Unconfigured` outcome, not a defect); changed
      `Status: Proposed` → `Accepted — implemented`, matching the `Accepted` convention used by ADR-0011.
- [x] 9.2 Checked `docs/` and `.github/project-ai/instructions/*.md` for any existing mention of
      `SearchCriterion`/discovery routing/niche-search that would now be stale — found none (this is the
      first change to document this behavior at all), so there was nothing to sync. No files changed.
- [ ] 9.3 **Deferred to `sdd-archive`**, per the task's own uncertainty note. The three delta specs remain
      unmerged under `openspec/changes/content-discovery-intelligence/specs/`; `openspec/specs/` was not
      touched by this apply run.
- [x] 9.4 Updated `state.yaml` — `status: apply-complete`, `adr_status: accepted`, a new `apply:` block
      documenting this run (including the WU-7 partial-mount deviation and two explicit follow-ups), and
      `validation.gates` with the actual backend/frontend command results.
- [x] 9.5 Ran `pwsh -File scripts/gates/Run-Gates.ps1` — see the gate summary in the final apply-progress
      report and the return envelope below.

---

## Definition of Done

- [ ] WU-0 fixture recorded and committed (no actor id in the fixture filename or contents' identifying metadata); `playCount` present (or owner escalation resolved) before WU-6 started.
- [ ] WU-1–WU-3 contract change lands as one coordinated unit, including `ContentDiscoveryPlan.UnconfiguredSourceKeys` and `SourceDiscoveryResult`; solution compiles.
- [ ] `GetServices<IContentDiscoveryService>().Count() == 1` passes (DI replacement, not supplement).
- [ ] `ApifyActorMappingBackfill.Transform` has zero diff; never produces a `niche-search` entry.
- [ ] No actor-id-shaped literal exists in shipped `SocialStudio.Application` or `SocialStudio.Infrastructure` source (the WU-6.1 repository-wide guard passes).
- [ ] `content-discovery` analysis type + `discover-niche-content` step resolve under the existing `single-analyzer` workflow key, as a sibling of `content-analysis`/`profile-analysis`, holding ONLY that step (no duplicated `analyze-content`/`generate-content`/`generate-image`); `singleAnalyzerChecklist` carries the new optional `single-analyzer-niche-search-actor` item; the discovery stage is reachable under Single Analyzer in the integrations settings UI (INT-DISC-02/03).
- [x] Operator selects exactly one candidate from the shortlist; that selection reuses the existing single-analysis start endpoint and the existing polling overlay with no new backend transport (CD-10). Reachable at Single Analyzer → "By Search" tab.
- [ ] `own` criterion resolves end to end with no `SearchCriterionInvalid`.
- [ ] `dotnet test SocialStudio.Tests/SocialStudio.Tests.csproj -c Release` passes, 0 failures.
- [ ] Architecture gate green: no Apify/EF/Npgsql/ASP.NET types in Domain or Application.
- [ ] `pwsh -File scripts/gates/Run-Gates.ps1` exits 0.
- [ ] ADR 0012 verified against shipped decisions; docs/specs synced.
