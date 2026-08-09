# Spec Delta: Content Research — SearchCriterion Routing and `own` Criterion Fix

<!-- Change: content-discovery-intelligence -->
<!-- Applies to: openspec/specs/content-research/spec.md (additive delta) -->

## Purpose

`SearchCriterion` moves from a display-only, echoed value to an actual router of discovery
sources, and the pre-existing `"own"` criterion defect (returns `SearchCriterionInvalid`) is
fixed so it resolves end to end through the client's own-account discovery source.

> **Amendment (2026-08-09)** — reviewed against both owner corrections in this round
> (configuration-driven actor routing; instant single-selection instead of batch fan-out).
> Neither requirement below changes: criterion routing selects *sources*, not actors, and is
> unaffected by how those sources later resolve their provider actor (content-discovery
> CD-12) or by how the operator later picks one candidate to analyze (content-discovery
> CD-10). No requirements added, modified, or removed in this file for this amendment round.

## ADDED Requirements

### Requirement: CR-DISC-01 — SearchCriterion Routes the Discovery Chain

`InMemoryContentWorkflowService.DiscoverResultsAsync` MUST pass the session's normalized
`SearchCriterion` into `ContentDiscoveryRequest`, and the resulting discovery MUST reflect
that criterion's routing (see `content-discovery` spec CD-03) instead of always running the
same source regardless of criterion.

#### Scenario: `references` and `niche` produce distinct candidate sets for the same client

- GIVEN the same client and organization
- WHEN a discovery run uses `SearchCriterion = references` and another uses `SearchCriterion = niche`
- THEN the two runs consult different sources
- AND the returned candidate sets are not required to be identical

### Requirement: CR-DISC-02 — `own` Is a Valid, Working SearchCriterion

`ContentWorkflowCatalog.SearchCriterionAliases` MUST include an `"own"` entry (and its
existing-language alias), so selecting `own` in the workflow UI resolves to the own-account
discovery source instead of failing validation.

#### Scenario: Selecting `own` no longer returns SearchCriterionInvalid

- GIVEN a workflow session where the operator selects the `own` search criterion
- WHEN discovery is requested
- THEN the request is accepted (no `SearchCriterionInvalid` error)
- AND the own-account discovery source is consulted

#### Scenario: `own` with a TikTok-only client yields an empty, non-throwing result

- GIVEN a client whose only presence is on TikTok (own-account discovery is Instagram-only in this change)
- WHEN `own` discovery runs for that client
- THEN the result is an empty candidate list with reported shortfall, not an exception
