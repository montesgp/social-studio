# Content Discovery Specification

## Purpose

Multi-source discovery of candidate high-performing content, routed by `SearchCriterion`,
normalized across networks, scored by a pure opportunity scorer, classified as
Trending/Proven/Undetermined from a single snapshot, persisted with author baselines, and
proposed to the operator as a ranked shortlist. The operator selects exactly one candidate,
which is then analyzed instantly and synchronously through the existing single-URL analyzer,
reusing its screen-blocking progress UX. No actor is ever hardcoded: every discovery stage
resolves its provider actor exclusively through workflow step configuration.

> **Amendment (2026-08-09)** — two owner corrections applied to this draft spec before
> implementation: (1) no actor may ever be forced in code — routing is always
> configuration-driven, per the existing single-analyzer pattern; (2) this change is an
> **instant, single-selection** flow, not automatic batch fan-out. Automatic TOP-N background
> analysis is removed from scope; it becomes the separate, later **Magic Generator** change.
> See `## Removed / Superseded Requirements` at the end of this file and the proposal's
> `Scope Amendment` section.

## Requirements

### Requirement: CD-01 — Ordered Discovery Chain Tolerates a Failing Source

`ContentDiscoveryChain` MUST consult its ordered `IContentDiscoverySource[]` and MUST NOT let
one failing or unconfigured source empty the run. It MUST report `Shortfall` (requested minus
returned) and `FailedSourceKeys` for any source that could not contribute.

#### Scenario: One source fails, others still contribute

- GIVEN two sources where the first throws/returns empty due to a provider error
- WHEN the chain runs
- THEN the result contains candidates from the surviving source(s)
- AND `FailedSourceKeys` includes the failed source's key
- AND `Shortfall` reflects the unmet remainder

#### Scenario: Chain asks each source only for the remainder

- GIVEN a requested count of 20 and a first source that returns 12 candidates
- WHEN the chain asks the second source
- THEN the second source is asked for at most 8 (the remainder), not 20

### Requirement: CD-02 — Cross-Source Deduplication

The chain MUST deduplicate candidates across sources by a stable identity before returning
results, so the same content item is never counted twice toward the requested count.

#### Scenario: Same content returned by two sources is deduplicated

- GIVEN two sources that both return a candidate resolving to the same dedup identity
- WHEN the chain merges results
- THEN the final candidate list contains that item exactly once

### Requirement: CD-03 — SearchCriterion Routes Source Selection

`ContentDiscoveryRequest` MUST carry the normalized `SearchCriterion`, and the chain MUST use
it to select which sources are consulted: `references` → reference-account source, `own` →
own-account source, `niche` → TikTok niche-search source. `viral` and `competitors` are valid
catalog values with no dedicated source in this change and MUST fall back to the default
ordered chain (all configured sources), never to a validation error. `niche` MUST be routed as
a distinct capability with its own actor variant, separate from the URL-shaped
`social-extraction` and profile-shaped `profile` variants (see integrations spec INT-DISC-04);
it MUST NOT reuse either variant's actor mapping.

#### Scenario: `niche` criterion consults the niche source

- GIVEN a request with `SearchCriterion = niche`
- WHEN the chain runs
- THEN only the niche-search source (and any configured fallback per chain ordering) is asked

#### Scenario: `viral` criterion falls back to the default chain without error

- GIVEN a request with `SearchCriterion = viral`
- WHEN the chain runs
- THEN no `SearchCriterionInvalid` or unimplemented-strategy error is returned
- AND the default ordered chain of configured sources is consulted

#### Scenario: `competitors` criterion falls back to the default chain without error

- GIVEN a request with `SearchCriterion = competitors`
- WHEN the chain runs
- THEN the default ordered chain of configured sources is consulted, not a dedicated source

### Requirement: CD-04 — Candidate Metrics Are Nullable With Explicit Availability

`DiscoveredContent` MUST expose shares, saves, and other network-variable metrics as nullable,
with explicit availability distinguishing "not reported by this network" from "reported as
zero." The scorer MUST exclude unavailable metrics from its denominator rather than treating
them as zero.

#### Scenario: TikTok candidate reports shares

- GIVEN a TikTok candidate with 40 shares
- WHEN it is normalized into `DiscoveredContent`
- THEN `Shares` is `40` and marked available

#### Scenario: Instagram candidate reports shares as unavailable, not zero

- GIVEN an Instagram candidate where the source network does not report shares
- WHEN it is normalized into `DiscoveredContent`
- THEN `Shares` is unavailable (not `0`)
- AND the opportunity scorer excludes shares from that candidate's denominator

### Requirement: CD-05 — Pure, Deterministic Opportunity Scorer

The opportunity scorer MUST be a pure function of its inputs (no I/O), combining: absolute
engagement, engagement rate, age, views/day, relative performance vs. author baseline when
available, and the lexical co-occurrence signal. The same candidate input MUST always produce
the same score.

#### Scenario: Same input yields the same score

- GIVEN a fixed candidate snapshot and a fixed set of available signals
- WHEN the scorer is invoked twice with identical input
- THEN both invocations return the identical score

#### Scenario: Score excludes unavailable signals from computation, not as zero

- GIVEN a candidate missing shares and lacking an author baseline
- WHEN the scorer computes the opportunity score
- THEN the score is computed from only the available signals
- AND missing signals are excluded, not substituted with zero

### Requirement: CD-06 — Trending/Proven/Undetermined From a Single Snapshot

Each candidate MUST be classified as `Trending`, `Proven`, or `Undetermined` from one
observation: `Trending` when age is within the configured trending window and views/day
exceeds the baseline (or candidate-set) median; `Proven` when age exceeds the configured
proven floor and total performance exceeds the baseline median by a configured factor;
`Undetermined` when `PublishedAtUtc` is unavailable. True growth velocity (requiring a second
observation) is out of scope; the classification is an honest single-snapshot proxy.

#### Scenario: Candidate with no publish date is Undetermined

- GIVEN a candidate whose `PublishedAtUtc` could not be resolved
- WHEN classification runs
- THEN the candidate is classified `Undetermined`, never defaulted to `Proven` or `Trending`

#### Scenario: Recent high-velocity candidate is Trending

- GIVEN a candidate younger than the trending window with views/day above its author baseline median
- WHEN classification runs
- THEN the candidate is classified `Trending`

#### Scenario: Old, consistently high performer is Proven

- GIVEN a candidate older than the proven floor with total performance above the baseline median by the configured factor
- WHEN classification runs
- THEN the candidate is classified `Proven`

### Requirement: CD-07 — Relative Performance Degrades Gracefully Without a Baseline

Relative-performance scoring MUST use the author's cached baseline when one exists for a known
account (reference/own account) or a TOP-N candidate account. When no baseline exists, the
system MUST NOT fabricate one; it MUST fall back to the candidate-set median (for
Trending/Proven) or omit the relative-performance signal from the score, never silently
inventing a baseline value.

#### Scenario: Baseline available — relative performance is computed against it

- GIVEN a candidate whose author has a cached `AccountBaseline`
- WHEN the score is computed
- THEN relative performance uses that baseline's stored median metrics

#### Scenario: Baseline absent — score degrades, no fabricated baseline

- GIVEN a candidate whose author has no cached baseline and is not in TOP N
- WHEN the score is computed
- THEN the relative-performance signal is either omitted from the score or computed against the candidate-set median
- AND no per-author baseline value is invented or defaulted to a fixed number

### Requirement: CD-08 — Candidate Persistence With a Stable Dedup Identity

Discovered candidates MUST persist to `discovered_candidates` with `observed_at` and the
normalized metrics snapshot. Dedup identity across runs MUST be based on a stable key (source
platform + normalized content URL or author+post identifier), so the same content observed in
a later run is recognized as the same candidate rather than duplicated.

#### Scenario: Same candidate observed twice creates a new observation, not a duplicate identity

- GIVEN a candidate previously persisted with a given dedup key
- WHEN the same content is discovered again in a later run
- THEN the system recognizes it as the same candidate identity
- AND records a new `observed_at` entry rather than creating a second unrelated candidate record

### Requirement: CD-09 — Account Baseline Persistence and Staleness

`account_baselines` MUST cache one baseline per author, computed only for known accounts
(reference/own) and TOP-N candidates after cheap filtering — never per raw candidate. A cached
baseline read after its configured staleness window MUST trigger recomputation before being
used for relative-performance scoring; a fresh cached baseline MUST be read without a new
scrape.

#### Scenario: Fresh baseline is reused without a new scrape

- GIVEN a cached baseline computed within the staleness window
- WHEN relative-performance scoring needs that author's baseline
- THEN the cached value is read and no new profile scrape is triggered

#### Scenario: Stale baseline triggers recomputation

- GIVEN a cached baseline older than the configured staleness window
- WHEN relative-performance scoring needs that author's baseline
- THEN the system recomputes the baseline before using it for scoring

### Requirement: CD-10 — Operator Selects Exactly One Ranked Candidate for Instant, Synchronous Analysis

(Previously: automatic TOP-N fan-out into `ISingleContentAnalyzer` for all TOP N candidates,
tolerating partial failure across the batch. REMOVED — see `## Removed / Superseded
Requirements` below. Multi-candidate background analysis moves to the separate, later Magic
Generator change and is out of scope here.)

After scoring and classification, the chain MUST return the TOP N candidates as a ranked
shortlist for operator review; the system MUST NOT start analysis on any candidate
automatically. The operator MUST select exactly one candidate from the shortlist. That single
selected candidate MUST then be analyzed instantly and synchronously through the existing
`ISingleContentAnalyzer` (one URL per call, the same path the Single Analyzer by URL already
uses), presenting the same screen-blocking progress UX (start → per-stage status →
completion/failure) as the existing Single Analyzer by URL. The resulting `AnalysisId` MUST
remain usable by the existing generation rails unchanged. Selecting more than one candidate for
analysis in a single request is out of scope for this change.

#### Scenario: Operator selects one candidate and sees synchronous blocking progress

- GIVEN a ranked shortlist of scored candidates presented to the operator
- WHEN the operator selects exactly one candidate to analyze
- THEN the system starts that single analysis instantly through `ISingleContentAnalyzer`
- AND the operator sees a screen-blocking progress bar with per-stage status, identical in
  shape to the existing Single Analyzer by URL
- AND on completion the resulting `AnalysisId` is usable by the existing generation rails
  unchanged

#### Scenario: The single selected analysis fails — no partial-batch semantics

- GIVEN the operator has selected exactly one candidate for instant analysis
- WHEN that analysis fails (provider error, timeout)
- THEN the failure is surfaced on the same blocking progress screen for that one analysis
- AND there is no partial-batch or multi-candidate failure reporting, because only one
  candidate was ever submitted

#### Scenario: Selecting multiple candidates for background analysis is out of scope

- GIVEN a ranked shortlist of scored candidates
- WHEN the operator would want to submit more than one candidate for analysis at once
- THEN this change does not support that; multi-select background batch processing is the
  separate Magic Generator change, not implemented here

### Requirement: CD-11 — Empty Discovery Results Are a Legitimate, Non-Throwing Outcome

The discovery port contract MUST return empty (with shortfall/failed-source reporting) rather
than throw when no candidates are found across all consulted sources, mirroring the existing
`IContentDiscoveryService` contract.

#### Scenario: All sources return nothing — operator sees an empty, explained result

- GIVEN a criterion whose configured sources all return zero candidates (e.g., unconfigured provider, no matches)
- WHEN the discovery request completes
- THEN no exception is thrown
- AND the response is an empty candidate list with `Shortfall` equal to the requested count and any applicable `FailedSourceKeys`

### Requirement: CD-12 — Discovery Actor Resolution Is Exclusively Configuration-Driven

No discovery source MUST ever hardcode, default to, or silently substitute a literal actor id
in Application or Infrastructure code, for any network or capability. Every discovery source
that calls out to a provider (the niche-search source, and any future discovery source) MUST
resolve its actor id exclusively through the workflow/step configuration resolution mechanism
— the same `(NetworkKey, CapabilityKey)` → `actor_mappings_json` pattern already used by
`ApifyProfileScrapeResolver` for the single-analyzer URL flow. A default workflow/step
definition and a placeholder actor mapping MAY ship so the operator has a non-empty starting
configuration to edit, but the system MUST NOT fall back to a code-level literal actor id when
that configuration is absent or empty.

#### Scenario: Configured actor resolves and the source runs

- GIVEN an organization with a configured `(tiktok, niche-search)` actor mapping
- WHEN the niche-search source resolves its actor
- THEN the configured actor id is used, read from configuration, not from a literal in code

#### Scenario: No hardcoded fallback exists when the actor mapping is empty

- GIVEN an organization whose `(tiktok, niche-search)` actor mapping is empty
- WHEN the niche-search source attempts to resolve its actor
- THEN resolution fails with a structured configuration error
- AND no literal or default actor id is substituted anywhere in Application or Infrastructure
  code

### Requirement: CD-13 — Unconfigured Discovery Stage Is Distinguishable From a Legitimate Empty Result

When a discovery source has no configured/enabled actor for the requested network, it MUST
report itself as **unconfigured**, not merely as "found nothing" or a generic failure, so the
operator can tell an unconfigured stage apart from a stage that ran and legitimately returned
zero candidates. This MUST be surfaced through the existing `FailedSourceKeys`/shortfall
reporting (or an equivalent structured signal carried alongside it) and MUST NOT throw.

#### Scenario: Unconfigured niche stage tells the operator to configure it

- GIVEN the niche-search discovery stage has no enabled actor configured for `tiktok`
- WHEN a `niche` discovery request runs
- THEN the source reports itself as unconfigured via `FailedSourceKeys` (or an equivalent
  structured signal) rather than throwing
- AND the operator-facing result is distinguishable from "ran and found nothing"

#### Scenario: Configured niche stage genuinely finds nothing

- GIVEN the niche-search discovery stage has a valid enabled actor configured for `tiktok`
- WHEN a `niche` discovery request runs and the provider returns zero matches
- THEN the source is reported as having run successfully with zero results
- AND it is not reported in `FailedSourceKeys` as unconfigured

## Removed / Superseded Requirements

### Requirement: (former) CD-10 — TOP-N Fan-Out Into the Single-URL Analyzer Tolerates Partial Failure

(Reason: owner correction — this change is an instant, single-selection flow, not a batch
flow. Automatic analysis of all TOP N candidates with partial-batch failure reporting no
longer matches the product shape.)

(Migration: replaced in place by the current `CD-10 — Operator Selects Exactly One Ranked
Candidate for Instant, Synchronous Analysis` above. Multi-candidate background analysis is
deferred to the separate, later Magic Generator change and is explicitly out of scope here.)
