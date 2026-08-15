# Niche Discovery Specification

## Purpose

New capability answering "which niche, sub-niche and content format is worth building a NEW
channel around, in which market?" — distinct from the shipped Content Discovery capability
(which answers "what content should I generate FROM for an existing client?"). A `DiscoveryRun`
sources candidate channels/videos through configurable, multi-origin providers, computes
deterministic analytics and an independent AI-driven semantic layer, compares source-market
traction against target-market demand/competition, and produces ranked, append-only
`NicheOpportunity` records — each carrying an `OpportunityScore`, an independent
`ConfidenceScore`, and navigable evidence down to the concrete channels and videos. The AI layer
names and groups; it never produces a number consumed by a scorer.

## Requirements

### Requirement: ND-01 — DiscoveryRun Captures Full Reproducible Parameters

A `DiscoveryRun` MUST be created with source market, target market, platform, content format
(long / shorts / both), an operator-chosen analysis window (default 90 days), and an
operator-chosen depth (e.g. quick / standard / deep) bounding provider API calls and AI
completions. The run MUST persist an immutable snapshot of the resolved configuration
(origin mapping, weights, depth ceiling) used at execution time, plus creation/start/completion
timestamps, so two runs with identical parameters over the same window are comparable.

#### Scenario: Run created with all required parameters

- GIVEN an operator supplies source market, target market, platform, format, a 30-day window,
  and depth = "quick"
- WHEN the run is created
- THEN the run persists all supplied parameters plus a configuration snapshot and a creation
  timestamp

#### Scenario: Two runs with identical parameters are comparable

- GIVEN two `DiscoveryRun` records created with the same market pair, platform, format, window
  and depth
- WHEN their configuration snapshots are compared
- THEN both snapshots resolve to the same origin mapping and weights, making their results
  directly comparable

### Requirement: ND-02 — Execution Status Lifecycle Distinguishes Unconfigured, Exhausted, and Empty

A `DiscoveryRun` MUST transition through an explicit execution status (e.g. Pending → Running →
Completed / Failed). A run whose candidate origin has no configured mapping MUST fail with a
status/reason distinguishable from a run that legitimately found zero candidates. A run that
exhausts its depth ceiling or provider quota mid-execution MUST transition to `Failed` with a
structured reason, never silently reported as "no opportunities found."

#### Scenario: Unconfigured origin fails distinctly from empty result

- GIVEN a run created for a platform with no origin mapped in configuration
- WHEN the run executes
- THEN the run status reports `Unconfigured` (or an equivalent structured reason)
- AND this is distinguishable from a run that ran and found zero opportunities

#### Scenario: Quota or depth ceiling exhausted mid-run surfaces as Failed

- GIVEN a run in progress that exhausts its configured depth ceiling or provider quota
- WHEN execution halts
- THEN the run transitions to `Failed` with a structured reason identifying the ceiling
  exhausted
- AND the run is never reported as having completed with zero opportunities

### Requirement: ND-03 — Multi-Origin Candidate Sourcing Follows the Ran/Unconfigured/Failed Contract

Candidate sourcing MUST go through an `INicheDiscoverySource` chain selected exclusively by
per-stage configuration (enabled workflow-step route first, raw mapping second, empty →
`Unconfigured` third), mirroring `ContentDiscoveryChain`. One failing or unconfigured origin
MUST NOT empty the run when another configured origin can still contribute. No literal actor id,
endpoint or API host MUST appear in Application or Infrastructure code.

#### Scenario: One origin fails, a configured origin still contributes

- GIVEN two configured origins where the first returns a transport error
- WHEN the chain runs
- THEN candidates from the surviving origin are still returned
- AND the failed origin is reported distinctly, without throwing

#### Scenario: No origin configured for the platform

- GIVEN a platform with zero configured origins
- WHEN the chain runs
- THEN the chain reports `Unconfigured` for that platform
- AND no literal actor id or endpoint is substituted in code

### Requirement: ND-04 — Normalization Boundary Excludes Provider Types From Domain/Application

External provider DTOs (YouTube Data API v3 responses, future Apify payloads) MUST normalize
into a provider-agnostic channel/video model inside the Infrastructure boundary before crossing
into Application. No `JsonElement`, `HttpClient`, EF type, or platform-specific DTO MUST appear
in Domain or Application code.

#### Scenario: Provider response normalizes before crossing into Application

- GIVEN a raw YouTube Data API v3 channel/video response
- WHEN it is mapped by the Infrastructure adapter
- THEN the object handed to Application is a normalized, provider-agnostic type
- AND no `JsonElement` or `HttpClient` type appears in the normalized model

### Requirement: ND-05 — Metrics Are Nullable and Never Zero-Filled

Every observed metric (views, likes, comments, subscriber count, etc.) MUST be nullable at the
normalization boundary. Null MUST mean the network does not report that metric; zero MUST mean
the network reported zero. No metric MUST be zero-filled when a network does not report it.

#### Scenario: Unreported metric is stored as unavailable, not zero

- GIVEN a channel whose source network does not report subscriber count
- WHEN it is normalized
- THEN subscriber count is null (unavailable), not `0`

#### Scenario: Reported zero is preserved as zero

- GIVEN a video the source network reports with zero comments
- WHEN it is normalized
- THEN comments is `0`, not null

### Requirement: ND-06 — Deterministic Analytics Are Pure and Reproducible

Median views, outlier ratio, outlier density, growth/velocity proxy, and cross-channel
repetition MUST be computed by pure, I/O-free Application code taking `nowUtc` as an explicit
parameter. The same input set MUST always produce the same analytics output.

#### Scenario: Same input yields the same analytics twice

- GIVEN a fixed set of normalized videos and a fixed `nowUtc`
- WHEN the analytics functions are invoked twice with identical input
- THEN both invocations return identical median views, outlier ratio, outlier density, growth
  proxy, and cross-channel repetition values

### Requirement: ND-07 — Semantic Layer Emits Labels Only, Never a Scorer Input

Classification, clustering, cluster naming, content-pattern detection and topic/entity
identification run through `IAiCompletionGateway` and MUST return labels and groupings only. No
scorer (`NicheOpportunityScorer`, `ConfidenceScorer`, or any analytics function) MUST accept an
AI response as a direct numeric input.

#### Scenario: AI response contains no scorer-consumed number

- GIVEN a semantic classification/clustering response from `IAiCompletionGateway`
- WHEN the response is inspected
- THEN it contains only labels, cluster names, and groupings
- AND no numeric field from that response is passed into `NicheOpportunityScorer` or
  `ConfidenceScorer`

### Requirement: ND-08 — Market Comparison Computes Traction, Demand, Competition, and Gap

`EvaluateTargetMarket()` MUST compute source-market traction (from source-market evidence),
target-market demand, target-market competition (count/strength of channels already publishing
the same content pattern in the target market's language/locale — an explicit assumption, not a
geography-of-audience partition), and the resulting `TargetMarketGap`.

#### Scenario: Gap reflects demand minus competition

- GIVEN a pattern with strong source-market traction, high target-market demand, and low
  target-market competition
- WHEN `EvaluateTargetMarket()` runs
- THEN `TargetMarketGap` reflects a favorable opportunity
- AND all four values (traction, demand, competition, gap) are individually inspectable

### Requirement: ND-09 — OpportunityScore Uses Configurable, Availability-Aware Renormalized Weights

`NicheOpportunityScorer` MUST be pure and I/O-free, combining ViralPerformance (0.20),
CrossChannelValidation (0.15), DemandMomentum (0.15), TargetMarketGap (0.15),
AutomationSuitability (0.10), EvergreenPotential (0.10), CrossPlatformValidation (0.10), and
MonetizationPotential (0.05) as configurable weights. Any signal unavailable for a given
opportunity (notably `CrossPlatformValidation`, which has no data source in this change) MUST be
excluded and the remaining weights renormalized, never treated as zero.

#### Scenario: Unavailable signal is excluded, not zero

- GIVEN an opportunity with `CrossPlatformValidation` unavailable
- WHEN the score is computed
- THEN `CrossPlatformValidation` is excluded from the weighted sum
- AND the remaining weights are renormalized to sum to 1.0

#### Scenario: Renormalization yields the same ranking as manually removing the weight

- GIVEN a fixture set of opportunities scored once with `CrossPlatformValidation` unavailable
  (renormalized) and once with that weight manually deleted and the rest hand-renormalized
- WHEN both scored sets are ranked
- THEN the resulting ranking order is identical between the two computations

### Requirement: ND-10 — ConfidenceScore Is Independent of OpportunityScore

`ConfidenceScore` MUST be computed independently of `OpportunityScore`, derived from channel
count, video count, sample size, timespan, source count, data freshness, channel diversity, and
signal consistency. `OpportunityScore` and `ConfidenceScore` MUST always be displayed together
and MUST NOT be averaged or combined into a single number.

#### Scenario: High opportunity, low confidence — both surfaced separately

- GIVEN an opportunity fixture with `OpportunityScore = 94` and thin evidence yielding
  `ConfidenceScore = 37`
- WHEN the opportunity is rendered
- THEN both scores are displayed side by side
- AND no combined single number replaces either score

#### Scenario: Moderate opportunity, high confidence — both surfaced separately

- GIVEN an opportunity fixture with `OpportunityScore = 88` and broad, fresh evidence yielding
  `ConfidenceScore = 95`
- WHEN the opportunity is rendered
- THEN both scores are displayed side by side, distinctly from the 94/37 fixture

### Requirement: ND-11 — Evidence Persistence Is Append-Only and Links to Concrete Sources

Every `NicheOpportunity` MUST link to the concrete channels and videos that back it, persisted
in `social` schema, snake_case, `organization_id` tenant boundary. Evidence rows and
`NicheOpportunity` rows MUST be append-only observations of one `DiscoveryRun`: a later run
produces new rows; nothing already persisted is updated in place. There is no operator
"pursued / dismissed / tracked" state on an opportunity in this change; comparison over time is
done by querying across runs.

#### Scenario: Opportunity links to its backing evidence

- GIVEN a `NicheOpportunity` produced by a run
- WHEN its evidence is queried
- THEN the concrete channels and videos that justified the score are returned, navigable from
  the opportunity

#### Scenario: A later run creates new rows, never updates prior ones

- GIVEN a prior `NicheOpportunity` for a niche/market pair from an earlier run
- WHEN a new `DiscoveryRun` evaluates the same niche/market pair
- THEN a new `NicheOpportunity` row is created
- AND the earlier row remains unchanged, retrievable by its own run identity

### Requirement: ND-12 — Viability Dashboard Sorts and Filters Across Result Dimensions

The dashboard MUST support sorting and filtering by opportunity score, confidence score, market,
format, trend, competition, and automation suitability.

#### Scenario: Filter by target market and sort by opportunity score

- GIVEN a set of opportunities spanning two target markets
- WHEN the operator filters to one target market and sorts by opportunity score descending
- THEN only opportunities for that market are shown, ordered by opportunity score descending

### Requirement: ND-13 — Opportunity Detail Exposes Full Evidence and Scoring Context

The opportunity detail view MUST expose: name/description, niche/sub-niche, formats and content
patterns found, both scores (opportunity and confidence, displayed together), channels analyzed,
videos analyzed, analysis period, data freshness, median views, outlier distribution,
independent-channel repetition count, estimated target-market competition, automation
suitability, creator independence, main content patterns, risks/limitations, and navigable
evidence down to the real channels and videos.

#### Scenario: Detail view renders all required fields

- GIVEN a persisted `NicheOpportunity` with complete evidence
- WHEN the operator opens its detail view
- THEN every field listed above is present and populated or explicitly marked unavailable
- AND the evidence section links to the concrete channels and videos

### Requirement: ND-14 — Market Catalog Is Data, Not Code

Target markets MUST be resolved through a market catalog (seeded data), never through an enum
member, constant, or literal named after a country. Adding a second target market MUST require
only a new catalog row, no code change. Argentina is seed data and the initial default target
only.

#### Scenario: A second target market works with zero code changes

- GIVEN a second market seeded into the market catalog with no code change
- WHEN a `DiscoveryRun` is created against that second market
- THEN the full pipeline (sourcing, analytics, comparison, scoring) runs against it exactly as
  it does for Argentina

### Requirement: ND-15 — No Identifier Is Named After a Country

No enum member, constant, method, class, column, or UI label produced by this change MUST be
named after a specific country (e.g. `YouTubeNiche`, `IsViableInArgentina()`,
`ArgentinaCompetitionScore` are forbidden). Market-specific concepts MUST use neutral names
(`NicheOpportunity`, `EvaluateTargetMarket()`, `TargetMarketCompetition`, `TargetMarketGap`).

#### Scenario: Guard test rejects a country-named identifier

- GIVEN the NicheDiscovery codebase
- WHEN the naming guard test scans identifiers and column names
- THEN no identifier contains a hardcoded country name

### Requirement: ND-16 — Every Run, Opportunity, and Evidence Row Is Tenant-Scoped

`DiscoveryRun`, `NicheOpportunity`, and evidence rows MUST carry `organization_id`, matching the
existing tenant boundary discipline (FK'd, cascade delete, never cross-tenant readable).

#### Scenario: Cross-tenant read returns nothing

- GIVEN opportunities belonging to organization A
- WHEN organization B queries the dashboard
- THEN organization A's runs, opportunities, and evidence are not returned

### Requirement: ND-17 — Content Generator Projection Seam Is Named, Not Built

A future boundary contract, `INicheOpportunityActivationSource`, exposing a `ContentPattern` +
`Topic` projection, is named as the integration seam toward the existing Content Generator. This
change MUST NOT implement that contract's behavior, and MUST NOT introduce any reference from
NicheDiscovery to a ContentGeneration type or vice versa, in either direction.

#### Scenario: No cross-reference exists between the two capabilities

- GIVEN the NicheDiscovery and ContentGeneration namespaces after this change
- WHEN their dependencies are inspected
- THEN NicheDiscovery references no ContentGeneration type
- AND ContentGeneration references no NicheDiscovery type

## Non-Goals (explicit, not implemented in this change)

- Automatic video generation, thumbnails, or auto-publishing from an opportunity.
- A consolidated channel dashboard (requires launched channels).
- Automatic experimentation or a closed-loop learning cycle over published outcomes.
- Google Trends or other external demand-signal providers beyond YouTube Data API v3.
- TikTok/Instagram cross-platform validation data (`CrossPlatformValidation` is an unavailable
  signal, renormalized out per ND-09 — not a zero-filled one).
- Apify as a second sourcing origin adapter (the port and per-stage routing support it; the
  adapter itself is not built here).
- Modifying or migrating `Domain.Social.Niches.Niche` (parallel hierarchy, cross-referenced by
  `NicheId`, per the settled taxonomy decision).
