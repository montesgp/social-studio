# ADR 0013 - Niche Discovery Multi-Origin Provider and Opportunity Evidence Model

## Status
Accepted — implemented.

> Numbering note: `docs/architecture/adr/` contains two `0005-*` and two `0006-*` files. `0013` is the
> next unambiguously free number (max in use is `0012`).

## Context

`Social.ContentDiscovery` (ADR 0012) answers *"what should I generate FROM for this client?"* — it
mines content for a client that already exists. This change adds a second, deliberately separate
capability: **`Social.NicheDiscovery`**, which answers a different question, asked **before** a
client exists — *"which niche/format/market is worth building a new channel around?"*.

**Vocabulary guard.** The two capabilities share the word "discovery" and nothing else. They share
no namespace, no type, and no `using`. Every contract that would otherwise be a natural shared type
(`SourceOutcome`, evidence shapes, port contracts) is **re-declared** in `Social.NicheDiscovery`,
never imported from `Social.ContentDiscovery`. A namespace-scan guard test asserts no type under
either capability references the other.

| Term | Capability | Question it answers |
|---|---|---|
| Content Discovery | `Social.ContentDiscovery` | What should I generate FROM, for this existing client? |
| Niche Discovery | `Social.NicheDiscovery` (this ADR) | Which niche/format/market is worth a NEW channel? |

An operator-parameterised `DiscoveryRun` sources channel/video evidence through **one multi-origin
port** (YouTube Data API v3 first, Apify second, both configuration-selected), turns it into
deterministic Metrics, interprets those into Signals, aggregates a renormalising OpportunityScore
plus a genuinely independent ConfidenceScore, compares a source market against a target market from
a data catalog, and writes append-only `NicheOpportunity` + evidence rows. The AI layer names and
groups; it never produces a number.

## Decision

### N1 — One multi-origin provider port, not one port per provider

`INicheDiscoverySource` (`SourceKey`, `Supports(DiscoveryStage)`, `DiscoverAsync`) is the **only**
Application port for sourcing. A per-provider port (`IYouTubeChannelSearchResolver`, as an earlier
exploration sketched) was **rejected**: it bakes the vendor into Application, so adding Apify as a
second origin would add a second port and a second chain instead of one configuration row.
Channel search and video detail have different cost profiles and different provider strengths, so
routing is **per stage**, not globally per run — that is the entire point of `DiscoveryStage`
(`ChannelSearch | VideoSearch | ChannelDetail | VideoDetail`) as a first-class configuration surface.
`NicheDiscoverySourceChain` tries every source that supports a stage (not first-match), accumulating
evidence from each `Ran` source; a `Failed`/`Unconfigured` source never empties a run another
configured source can still contribute to.

### N2 — Deliberate contract duplication over cross-capability import

`NicheSourceOutcome { Ran, Unconfigured, Failed }`, evidence shapes (`ChannelEvidence`,
`VideoEvidence`, `EvidenceSet`), and every other NicheDiscovery contract are **re-declared** in
`Application.Abstractions.Social.NicheDiscovery`, not imported from `Social.ContentDiscovery`. The
vocabulary guard (above) forbids cross-capability imports. The cost is a handful of duplicated
4-line records; the benefit is that the two capabilities can never be coupled by an "obvious" shared
type — a lesson carried forward from ADR-0012 §2 (N2 there), applied here in the opposite direction.

### N3 — Three structural guards make "a Metric from AI" impossible, not discouraged

A `Metric` has a private constructor and exactly one factory,
`DeterministicMetricCalculator.Compute(EvidenceSet, nowUtc)`. The semantic contract types
(`SemanticAnnotation`, `ClusterProposal`, `FormatTrait`) carry **no numeric member at all**. No
scoring signature (`NicheOpportunityScorer`, `ConfidenceScorer`, any `…Analytics` type) mentions a
`…Semantics` type. Three reflection guard tests enforce all three, landed across the batches that
built each layer:

- `NicheDiscoveryMetricFactoryGuardTests` (WU-6.1) — `Metric.From` has exactly one calling type.
- `NicheDiscoveryScoringSemanticsGuardTests` (WU-8.7, re-verified WU-10.6) — no `…Scoring`/`…Analytics`
  signature references a `…Semantics` type.
- `NicheDiscoverySemanticsNoNumericGuardTests` (WU-10.1) — every public property of every
  `…Semantics` type is a `string`, `bool`, `enum`, or a collection of those.

"Discouraged" decays under delivery pressure. A type you cannot construct, and a signature you
cannot compile against, does not.

### N5 — `DiscoveryRun` is a Domain aggregate; `NicheOpportunity` is an Application read model

`DiscoveryRun` has a real status lifecycle (`Requested → Running → Completed | PartiallyCompleted |
Failed`) and lives in Domain. `NicheOpportunity` is an **append-only observation of one run** — it
has no operator action (`Pursue()`/`Dismiss()`/`Track()`) in this change, so inventing that lifecycle
would repeat the exact mistake ADR-0012 §7 already avoided for `DiscoveredContent`. It stays an
Application read model, recomputed each run.

**Revisit trigger, named**: the first time an operator can mark an opportunity *pursued*, *dismissed*
or *tracked*, it acquires invariants and becomes a Domain aggregate — that follow-up also owns
retention.

### N6 — Market is data; no identifier is ever named after a country

`MarketCode` (`{iso639-1}-{ISO3166-1}`, e.g. `es-AR`) plus a seeded `MarketCatalogEntry`
table/seeder. Display names are **i18n keys**, never literals. Adding a second target market is one
catalog row — no code branch reads a market's identity, only its `MarketCode` string and language
partition. A source-scanning guard test (`NicheDiscoveryCountryNameGuardTests`) fails the build on
any country name, demonym, or bare ISO-3166 alpha-2 code appearing as an identifier or string
literal anywhere under the four NicheDiscovery namespaces or the web `niche-discovery` folders,
whitelisting only the seeder file and locale/i18n resources. `SecondTargetMarketNoCodeChangeTests`
(WU-11a) proves the criterion literally: a second market (`pt-BR`) seeded with zero code change runs
the identical pipeline end to end over the same fixture, and a source-tree scan confirms no
per-market file exists anywhere in the shipped tree.

### N7 — Scorer weights are snapshotted per run, superseding ADR-0012 D7 for this capability only

`OpportunityScoreWeights` carries a `Default` and is **snapshotted onto the run**
(`weights_json`), with an optional per-run override on the start request. ADR-0012 D7 chose fixed
in-code constants for a 6-signal single-tenant scorer — a good decision for that scorer, which has no
run-level identity to snapshot against. Niche Discovery's unit of reproducibility **is** the run: two
runs scored with two different weight sets are not comparable, so the weights must travel with the
run rather than live in a global table that can drift between two runs an operator wants to compare.
This does not revisit ADR-0012 D7 for Content Discovery, which keeps fixed constants.

### N8/N9 — Three-valued availability; quota exhaustion is a distinct, honest outcome

`SignalAvailability { Available, NotReported, NotSupportedYet }`. `CrossPlatformValidation` is not
"the network didn't report it" — it is "this version has no data source for it at all". Collapsing
the two into zero-fill would lie to the operator about their data; the score computation excludes an
unavailable signal from the weighted sum and **renormalizes the remaining weights**, never treats it
as zero. The UI (WU-12, `excluded-signals-note.tsx`) makes this visible: it names the excluded
signal and states the denominator was renormalized, not silently reduced.

Quota exhaustion: zero usable results plus a provider quota error maps to `Failed`
(`social.niche_discovery.provider_quota_exhausted`); partial results followed by a quota error maps
to `Ran` with `PartialReason.ProviderBudgetExhausted`, and the run completes as `PartiallyCompleted`.
A truncated-but-usable run is still an honest answer — `ConfidenceScore` already reflects the
smaller sample, so the run does not need to lie about completeness to be useful.

### N12 — A new top-level workflow key, with its own checklist group

`niche-discovery` (analysis type `market-opportunity`, four steps — one per `DiscoveryStage`) is a
**new top-level workflow key**, with its own checklist group in `GetOrganizationIntegrationsHandler`
(a third group beside `single-analyzer`/`generator`). This is deliberately **not** nested under
`single-analyzer`: the operator has no client and no analysis at this point, so hiding a
channel-launch tool under Single Analyzer would be a UI lie about what the tool does. The trap this
avoids is `WorkflowConfigurationCatalog`'s own documented rule — no checklist group for a workflow
key means "not configurable yet" — paid for explicitly here (a real group, with real items) rather
than discovered later as a silent gap, exactly the trap ADR-0012 §6 named for `content-discovery`.

### Provider category: `OfficialDataApi` appended, never renumbered

`IntegrationProviderCategory` gains `OfficialDataApi = 5`, **appended** to the existing enum, not
renumbered — the enum is persisted as an integer, so renumbering would silently reclassify every
existing integration row. YouTube Data API v3 is seeded as a distinct provider
(`youtube-data-api-v3`) from the existing `youtube` publishing/scrape row, with a single API-key
credential schema — the platform's first official-vendor-API integration, as opposed to the actor-
based scraping providers (Apify) the rest of the platform uses.

### Target-market competition partition: locale, not audience geography (stated assumption)

`TargetMarketCompetition` is computed from the count/strength of channels already publishing the
same content pattern in the **target market's language/locale** — content-locale, not
audience-geography. This was an open decision checkpoint carried from design into WU-9 and resolved
there: content language + region (locale) was chosen over audience geography, because YouTube's
audience-geography reporting is materially less reliable than content locale for this purpose. This
is recorded here as a **stated assumption**, not a closed decision — the design explicitly leaves
open that a future revisit may prefer audience geography if reporting improves. If revisited, only
`TargetMarketEvaluator.TargetMarketCompetition` changes; the type signature and every other
component are unaffected.

## Consequences

- **Additive rollback.** The migration (`Add_NicheDiscoveryRunsOpportunitiesAndEvidence`) adds four
  new tables under the `social` schema and touches nothing existing; dropping them loses only
  accumulated runs, opportunities, and evidence.
- **One reversible migration.** No existing table, column, or index is altered.
- **Config-removal disables discovery without a code change.** Removing the YouTube Data API key or
  disabling the workflow step returns the origin to `Unconfigured` by contract — the run reports it
  distinguishably from "ran and found nothing" (ND-02), exactly as ADR-0012 established for
  `UnconfiguredSourceKeys` on the content-discovery side.
- `GetServices<INicheDiscoverySource>()` resolves to exactly the configured origins with distinct
  `Order`; a composition test (`NicheDiscoveryCompositionTests`, WU-11b) makes the DI composition
  hazard from ADR-0012 §1 (a second registration silently winning `GetRequiredService`) impossible
  for the chain shape too, since the chain, not a single resolved service, is what the container
  hands out.
- The **Content Generator seam** (`INicheOpportunityActivationSource`) ships **named, not built**: no
  implementation, no registration. `GetServices<INicheOpportunityActivationSource>()` is asserted
  **empty** by a composition test (WU-11b) — "named, not built" is enforced, not trusted.
- **PENDING LIVE VERIFICATION** (mirrors this ADR's own precedent from ADR-0012's Amendment for the
  Apify TikTok actor spike): `YouTubeEvidenceMapper` and its recorded fixture
  (`SocialStudio.Tests/Infrastructure/Social/Fixtures/youtube-niche-search.json`) are built and
  unit-tested entirely against a recorded response. No Google Cloud project with YouTube Data API v3
  has been provisioned yet; a final verification against a real response is deferred to the owner
  provisioning that project. This affects only the YouTube origin adapter — every other component
  (analytics, scoring, market comparison, semantics, persistence, endpoints, and the WU-12 UI) has no
  dependency on this prerequisite and is fully implemented and tested.
- **WU-11a/11b gap fixes found while wiring the pipeline end to end for the first time** (each
  flagged in its own commit, not silently patched): `DiscoveryRun` gained
  `RecordExecutionSnapshot(...)` because it had no fields for `WeightsJson`/
  `UnconfiguredSourceKeys`/`FailedSourceKeys`/`ProviderUnitsSpent`/`AiCompletionsSpent`/
  `SemanticDegraded` until a real caller (the run processor) needed to persist them;
  `INicheDiscoveryRunQueue`'s shape was corrected to carry `organizationId` alongside the run id,
  because the tenant-scoped repository has no other source for it after dequeue; `YouTubeDataApiClient`'s
  constructor was made public because MS DI's default constructor resolution only considers public
  constructors. These are exactly the class of finding this ADR's guards (N3) and structural tests
  exist to catch early rather than in production.
- **WU-12 gap fix**: the run/opportunity DTOs (`DiscoveryRunStatusResponse`, `NicheOpportunitySummary`)
  shipped in WU-11b before the dashboard existed to consume every field it needs. WU-12 extended both
  additively (target market code, content format, target-market competition, automation suitability,
  growth proxy) rather than leaving the dashboard's market/format/competition/automation/trend
  sort-and-filter axes (ND-12) unimplementable.

## Related

- Design: `openspec/changes/niche-discovery-engine/design.md`
- Proposal: `openspec/changes/niche-discovery-engine/proposal.md`
- Specs: `openspec/changes/niche-discovery-engine/specs/`
- Follows the source-chain, append-only-evidence, and availability-aware-scoring precedent of
  ADR-0012 (Content Discovery), while deliberately sharing no type or namespace with it (vocabulary
  guard, above).
