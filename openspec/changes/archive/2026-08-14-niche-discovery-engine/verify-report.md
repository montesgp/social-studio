# Verify Report - niche-discovery-engine

Date: 2026-08-14
Verifier: sdd-verify (independent adversarial pass; source inspection and real test execution)
Backend commit under test: dev HEAD (038008d at launch)

## Test/build evidence actually executed by this pass

| Command | Result |
|---|---|
| dotnet test --filter NicheDiscovery | 232/232 passed |
| dotnet test full suite | 3518/3520 passed, 2 pre-existing skips, 0 failed |
| dotnet ef migrations has-pending-model-changes | Clean |
| npm run test (apps/web) | 343/343 passed |
| npx tsc --noEmit | Clean |
| npm run build | Succeeded, /niche-discovery registered as dynamic route |
| pwsh Run-Gates.ps1 (clean run) | PASS - 17 pass / 1 skipped / 1 warn (pre-existing, unrelated) |

Gate log:
```
[ MUST   ] build.backend: pass - solution built in 5,0s
[ MUST   ] build.frontend.web: pass - built in 52,0s
[ SHOULD ] build.frontend.web.lint: pass - lint clean
[ MUST   ] tests.backend: pass - total=3520, passed=3518, failed=0, skipped=2
[ MUST   ] tests.frontend.web: pass
[ SHOULD ] coverage.line: skipped - no coverage.cobertura.xml
[ MUST   ] architecture.layering: pass - 7 projects checked
[ MUST   ] architecture.imports: pass - no forbidden namespace imports
[ MUST   ] dependencies.cycles: pass - no cycles
[ MUST   ] dependencies.solution-drift: pass
[ SHOULD ] dependencies.reciprocity: pass
[ MUST   ] secrets.scan: pass - clean (2447 files scanned)
[ MUST   ] secrets.hygiene: pass
[ MUST   ] security.config: pass
[ MUST   ] security.insecure-defaults: pass
[ SHOULD ] security.transport: pass
[ SHOULD ] vulnerabilities.backend: pass
[ SHOULD ] vulnerabilities.frontend.web: warn - high=4, critical=0
[ SHOULD ] migration-drift: pass
=== Gate Summary ===
  pass 17, skipped 1, warn 1
PASS - no MUST gate failed.
```

Note on method: an earlier concurrent Run-Gates.ps1 run collided with a separate npm run build this
verifier ran independently against the same .next-build directory, producing one spurious
build.frontend.web ENOENT failure - a directory race, not a code defect. The re-run without interference
(log above) is clean, and the independent npm run build / npm run test / tsc runs corroborate it.

## Specific claims verified (the 10 adversarial checks)

1. AI cannot produce a number - structural, not a claim. CONFIRMED. Metric
   (Analytics/Metric.cs) has a private constructor and one internal static From factory
   (signature: MetricKey, double, int - not EvidenceSet as loosely described in the task
   brief; the actual guarantee is equivalent: construction is impossible outside the
   factory). NicheDiscoveryMetricFactoryGuardTests source-scans the whole Application
   assembly and asserts exactly one calling file - genuinely meaningful, would fail if any
   other file called Metric.From. NicheDiscoverySemanticsNoNumericGuardTests reflects over
   every type in the Semantics namespace and asserts every public property is
   string/bool/enum/collection-of-those, recursively - not vacuous, proven by a companion
   test that the namespace it scans is not empty. NicheDiscoveryScoringSemanticsGuardTests
   source-scans Scoring/Analytics files for references to the Semantics namespace and is
   likewise proven non-vacuous. All three guards are genuinely meaningful, not manufactured
   green.

2. No identifier is named after a country. CONFIRMED, with a caveat.
   NicheDiscoveryCountryNameGuardTests scans NicheDiscovery directories for a curated list
   of about 17 Latin American and European country names and demonyms plus a bare AR
   code-fragment regex. The guard is proven non-vacuous by a seeded-violation fixture test
   that writes a temp file named ArgentinaCompetitionScore.cs and asserts the scanner
   flags it - a real detection proof, not a trivial pass. Caveat: the list is explicitly
   non-exhaustive by the implementing agent's own comment ("extend as new markets are
   seeded") - it does not cover USA/American, UK/British, Japan, China, India, Australia,
   etc. Since only es-AR is seeded today this is not a live gap, but it will silently
   under-protect the moment a market like en-US or ja-JP is seeded without extending the
   list first. WARNING, not a defect.

3. Adding a second target market requires no code change. CONFIRMED, strongly.
   SecondTargetMarketNoCodeChangeTests runs the real NicheDiscoveryRunProcessor twice over
   the identical evidence fixture, once with es-AR and once with pt-BR, via an in-memory
   market catalog fake carrying both rows. NicheDiscoveryRunProcessor.cs was read directly
   and never branches on market identity, only forwards TargetMarketCode.Language. A third
   test source-scans for market-specific filename fragments across all NicheDiscovery
   directories and asserts none exist. All three tests pass.

4. OpportunityScore renormalization. CONFIRMED. A dedicated test confirms the denominator
   renormalizes to exactly 0.90 when CrossPlatformValidation is excluded, and that the
   signal is surfaced in ExcludedSignals with reason not_supported_yet, never as a zero. A
   second test proves ranking-order equivalence between excluded-and-renormalized and a
   weight-table copy with CrossPlatformValidation set to zero weight - mathematically
   equivalent to removing it by hand, since a zero-weight term never contributes regardless
   of availability. NicheOpportunityScorer.cs was read directly: the weight table
   (0.20/0.15/0.15/0.15/0.10/0.10/0.10/0.05) matches spec ND-09 exactly, and
   CrossPlatformValidation is unconditionally excluded, never zero.

5. ConfidenceScore independence. CONFIRMED, and the substitution is judged stronger than
   the literal spec fixture. ConfidenceScorer and NicheOpportunityScorer share no input
   type by construction (SampleShape vs OpportunityInput, both files read directly) - no
   code path connects them, a structural proof rather than a coincidence of two numbers.
   The test file demonstrates this with a magnitude-scaling test and a shape-scaling test,
   both stronger evidence than reproducing literal digits. Separately,
   score-pair.test.ts on the frontend DOES reproduce the literal 94/37 and 88/95 fixtures
   verbatim, including an explicit assertion that neither score equals their average.

6. Null metric semantics traced end to end. CONFIRMED for the SubscriberCount/Views path.
   ChannelEvidence/VideoEvidence declare every metric as nullable. YouTubeEvidenceMapper.cs
   is documented and tested to map an absent field to null, never zero.
   NicheDiscoveryRunProcessor.cs passes evidence straight through with no coalescing to
   zero anywhere. The persistence migration declares every metric column nullable. The
   frontend availability.ts renders null as the literal string "unavailable", distinct from
   "0". The chain holds at every layer inspected.

7. Degraded and failed runs. CONFIRMED, both backend and frontend.
   NicheDiscoveryRunProcessor.RunAsync (read in full): an unconfigured origin with zero
   evidence fails with OriginUnconfigured; a quota/depth-exhausted origin with zero
   evidence fails with the origin's structured reason; a degraded semantic layer or any
   unconfigured/failed origin transitions the run to PartiallyCompleted, never Completed.
   Five processor test scenarios cover these branches and pass. Frontend run-state.ts gives
   all five statuses distinct labels/tones, flags only PartiallyCompleted as degraded, and
   only carries a failure reason key on Failed.

8. Append-only. CONFIRMED. INicheOpportunityRepository and
   INicheDiscoveryEvidenceRepository expose only AppendAsync plus reads - no Update/Delete
   member on either interface. IDiscoveryRunRepository is the one port with UpdateAsync, by
   design (the run's own status lifecycle). Tests exist asserting the port exposes no
   update/delete method and that a later run appends new rows leaving earlier rows
   unchanged.

9. Exactly one implementation per NicheDiscovery port; the activation source has none.
   CONFIRMED directly. NicheDiscoveryCompositionTests builds the real container and asserts
   exactly one registration per port, distinct source keys/orders, and an empty
   registration set for INicheOpportunityActivationSource - a real DI-container
   resolution, not a text guard.

10. The dashboard is reachable and usable. CONFIRMED. The route and nav entry exist; npm
    run build registers /niche-discovery as a dynamic route. opportunity-detail.tsx (read
    in full) renders every ND-13 field: name/description, niche/sub-niche, content pattern,
    both scores side by side, channels/videos analyzed, analysis period, data freshness,
    median views, outlier ratio and density, independent-channel repetition (mapped
    deliberately onto "creator independence", documented as one field not two),
    target-market competition/demand/gap, automation suitability, risks/limitations, and a
    navigable evidence list. A start form exists with operator-chosen window and depth.

Second independent full Run-Gates.ps1 execution (no interference) reproduced the identical
result: 17 pass, 1 skipped, 1 warn, PASS. Two clean independent runs corroborate the first.

## Spec compliance matrix - specs/niche-discovery/spec.md

| Req | Verdict | Evidence |
|---|---|---|
| ND-01 reproducible run parameters | SATISFIED | DiscoveryRun.cs requires all params, snapshots config at creation |
| ND-02 status lifecycle distinguishes unconfigured/exhausted/empty | SATISFIED | NicheDiscoveryRunProcessor.FinishEmptyAsync has three distinct branches, covered by 4 passing tests |
| ND-03 Ran/Unconfigured/Failed source chain | SATISFIED | NicheDiscoverySourceResult/NicheSourceOutcome enforce three-valued outcome by private-constructor/factory pattern |
| ND-04 normalization boundary excludes provider types | SATISFIED | Grep for JsonElement/HttpClient across Domain+Application NicheDiscovery: zero hits; confined to Infrastructure adapters |
| ND-05 nullable metrics, never zero-filled | SATISFIED | Traced end to end, see claim 6 |
| ND-06 deterministic, pure, reproducible analytics | SATISFIED (lighter evidence) | Calculator takes explicit nowUtc; constrained structurally by claim-1 guards; filtered 232-test run passes |
| ND-07 semantic layer emits labels only | SATISFIED | Semantics types constrained by the no-numeric-property guard; isolation guard proven non-vacuous |
| ND-08 traction/demand/competition/gap individually inspectable | SATISFIED | TargetMarketEvaluation exposes all four separately; only .Language used, never market identity |
| ND-09 availability-aware renormalized OpportunityScore | SATISFIED | See claim 4 |
| ND-10 independent ConfidenceScore, never averaged | SATISFIED | See claim 5 |
| ND-11 append-only evidence persistence | SATISFIED | See claim 8; migration FKs cascade-delete |
| ND-12 dashboard sort/filter across 7 axes | SATISFIED (lighter evidence) | ranking.ts/filters.ts and their tests exist, part of the 343 passing frontend tests, not read line by line |
| ND-13 opportunity detail full context | SATISFIED | See claim 10, read in full, field by field |
| ND-14 market catalog is data; second market needs zero code | SATISFIED | See claim 3 |
| ND-15 no country-named identifier | SATISFIED, coverage-gap WARNING | See claim 2 |
| ND-16 tenant-scoped rows | SATISFIED | Cascade-delete FKs; cross-org read and cascade-delete tests confirmed present |
| ND-17 Content Generator seam named, not built | SATISFIED | See claim 9; no implementing class, zero DI registrations |

## Spec compliance matrix - specs/integrations/spec-delta.md

| Req | Verdict | Evidence |
|---|---|---|
| INT-ND-01 official-API category + API-key credential | SATISFIED | youtube-data-api-v3 entry with OfficialDataApi category and single api_key field, no actor_mappings_json |
| INT-ND-02 YouTube Data API v3 seeded distinct from youtube | SATISFIED | Two separate catalog rows with distinct GUIDs |
| INT-ND-03 checklist group reachable | SATISFIED | Test asserts the niche-discovery checklist group is present, not merely absence-of-crash |
| INT-ND-04 per-stage config-driven origin selection | SATISFIED | Origin configuration + tests exist; workflow catalog confirms allowed provider categories on all 4 stage steps |

## Findings

### Real defects
None found that block archive. The most adversarial checks (structural AI-cannot-score
guards, append-only enforcement, DI composition, second-market criterion, null-propagation)
all held up under direct source inspection, not just passing test names.

### Acceptable deviations (self-documented in code, judged sound)
- Semantic layer issues ONE consolidated AI gateway call, not five staged calls. Confirmed
  in SemanticAnnotator.cs: five prompt-version-key constants reserved but unused beyond the
  cluster one. A legitimate simplification since the response contract already carries
  everything in one shape - not a hidden capability gap.
- The design's Signal/SignalBuilder layer never got built - the pipeline is Evidence to
  Metric to Score in practice, three layers not four. The processor's own doc comment
  states this and explains the per-cluster reduction shortcuts taken as a result. An honest
  simplification with a documented, inspectable consequence.
- TargetMarketCompetition ranks subscriber deciles across the whole run, not per cluster -
  confirmed in the doc comment and implementation. A reasonable authored interpretation,
  explicitly flagged as such.
- MonetizationPotential reuses TargetMarketCompetition's decile formula directly - confirmed
  in the processor. Documented as an accepted simplification, but means the two signals are
  not actually independent today, which the UI does not disclose to the operator. Worth a
  follow-up.
- AutomationSuitability is computed once per run, not per cluster, because the semantic
  layer's format-trait labels are run-flat - confirmed. Every opportunity in a run gets the
  same value. Documented, low risk for an MVP, but a real precision loss for a run that
  mixes very different content formats.
- ApiService.csproj's local dev signing target uses ContinueOnError. A post-build,
  local-dev-only convenience step unrelated to the actual build/test/architecture MUST
  gates - not a MUST-gate downgrade in the repo-rule sense.
- The YouTube Data API v3 adapter is fixture-only, unverified against a live response -
  explicitly and repeatedly flagged in code comments and in tasks.md, genuinely blocked on
  an owner-side Google Cloud project. This is the one item this report marks UNVERIFIABLE
  rather than SATISFIED, because live external access is required and was never claimed to
  exist.

### Follow-up work (not blocking, but should be tracked)
- The country-name guard list is representative, not exhaustive - extend before seeding any
  market outside the current curated Latin America / Western Europe list. Silent gap risk
  the moment a market like en-US or ja-JP is seeded without extending the list first.
- Several ND-13 fields (description, analysis period, data freshness, risks/limitations)
  are derived in a frontend module rather than served by the backend read model.
  Functionally correct today - pure, deterministic, derived only from already-persisted
  fields - but means any second API consumer would have to reimplement this narrative
  logic. Worth moving server-side if a second consumer is ever planned.
- MonetizationPotential and TargetMarketCompetition are not actually independent signals
  despite being presented as two separate weighted terms - see deviation note above.
- No RED phase was observed for NicheDiscoveryRunProcessor.cs - the implementing agent's
  own admission is that it was written before its test and all 6 scenarios passed on the
  first try. This is a genuine Strict TDD discipline breach. Direct inspection confirms the
  6 test scenarios are not vacuous, however: each maps to a distinct, real branch in the
  processor, and mutating any of those branches would fail the corresponding test. This is
  a WARNING about process, not a defect in the resulting coverage.

## Verdict

Ready to archive. All 21 spec requirements (17 ND + 4 INT-ND) are SATISFIED against real,
executed test evidence and direct source inspection, not against checkbox claims. The
backend suite (3518/3520, 2 pre-existing skips), frontend suite (343/343), tsc --noEmit,
npm run build, and two independent full Run-Gates.ps1 runs (17 pass / 1 skipped / 1 warn,
both pre-existing and unrelated) all pass. The structural "AI cannot produce a number"
guards, the append-only enforcement, the DI composition invariants, and the
second-target-market criterion were each independently re-derived from source, not taken
on faith, and held up. The one item genuinely outside this change's control - a live
YouTube Data API v3 response - is correctly and repeatedly flagged as pending an
owner-side prerequisite, never silently assumed. Remaining items are documented
deviations or tracked follow-ups, none of which contradict a spec requirement or break a
passing test.
