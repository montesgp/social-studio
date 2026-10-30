# Brand Snapshot Specification

<!-- ADR: docs/architecture/adr/0002-client-content-plan-model.md -->

## Purpose

Define the contract for `BrandSnapshot` — the frozen, immutable context record assembled
at content-generation job start — and the behavior of the snapshot loader. This change
updates the loader's data source (persisted Content Plan instead of the deprecated
`ClientMonthlyObjective` table) while keeping the `BrandSnapshot` output contract stable
so the `generate-social-content-workflow` continues to compile and run unchanged.

---

## Requirements

### Requirement: BS-01 — BrandSnapshot Output Contract Is Immutable

The system MUST preserve the `BrandSnapshot` record shape:
`Client`, `Brand`, `Strategy`, `MonthlyObjective`, `ReferentAccounts`,
`CompetitorAccounts`, `InferredFields`.
No field MUST be removed or renamed. New optional fields MAY be added in a
backward-compatible way only.

`InferredFields` MUST continue to mark each named field `true` when the value
was absent or empty at snapshot time.

#### Scenario: Snapshot fields unchanged after source migration

- GIVEN a client with a persisted Content Plan for a given period and a General Strategy
- WHEN the snapshot loader assembles a BrandSnapshot for that client and period
- THEN the returned BrandSnapshot has all existing fields populated from their new sources
- AND no existing field is null where it was previously non-null from the same data

#### Scenario: Inferred fields set for missing plan nodes

- GIVEN a client with a Content Plan that has no Content Strategy text
- WHEN the snapshot loader assembles a BrandSnapshot
- THEN InferredFields["ContentPillars"] and other strategy-derived fields are true

---

### Requirement: BS-02 — Snapshot Loader Reads Persisted Content Plan

The system MUST build the `MonthlyObjective` component of `BrandSnapshot` from the
persisted Content Plan's Objective node for the relevant period.
It MUST NOT assemble this data from the deprecated `ClientMonthlyObjective` table.

The loader MUST build the `Strategy` component from the persisted General Strategy
(renamed from `ClientStrategy`). This source is unchanged; only the naming is aligned.

The loader resolves "current month" using `DateTime.UtcNow`; if no current-month Content
Plan exists, it falls back to the most recent plan (ordered by year desc, month desc).

#### Scenario: Loader reads Content Plan Objective for period

- GIVEN a client with a Content Plan for Period (2026, 6) containing an Objective
- WHEN the snapshot loader is called for that client
- THEN BrandSnapshot.MonthlyObjective is populated from the Content Plan's Objective node
- AND InferredFields["MonthlyObjectives"] is false

#### Scenario: Missing Content Plan yields inferred objective

- GIVEN a client with no Content Plan for any period
- WHEN the snapshot loader is called for that client
- THEN BrandSnapshot.MonthlyObjective is null
- AND InferredFields["MonthlyObjectives"] is true
- AND snapshot assembly MUST NOT fail — it returns a valid BrandSnapshot with inferred fields

#### Scenario: Loader reads General Strategy for strategy fields

- GIVEN a client with a persisted General Strategy containing content pillars and tone keywords
- WHEN the snapshot loader is called
- THEN BrandSnapshot.Strategy.ContentPillars and BrandSnapshot.Strategy.ToneKeywords match
  the current General Strategy values
- AND InferredFields["ContentPillars"] is false

---

### Requirement: BS-03 — Snapshot Is Frozen at Job-Start Time

The system MUST treat a `BrandSnapshot` as immutable once assembled. The loader
MUST NOT update a snapshot after the generation job has started. Any change to
General Strategy or Content Plan after job-start MUST NOT affect the in-progress job.

#### Scenario: Post-assembly changes do not mutate snapshot

- GIVEN a BrandSnapshot assembled at T0
- WHEN the Content Plan is updated at T1 (T1 > T0)
- THEN the BrandSnapshot from T0 is unchanged
- AND the generation job using T0 snapshot completes with original values

---

### Requirement: BS-04 — BrandManual Domain Name Canonical

The system MUST use a single canonical name for the enduring brand asset aggregate
in the domain layer. "Brand Brain" is a UI/product label only and MUST NOT appear
as a domain type, namespace, or identifier. The domain name (`ClientBrandManual`) is
fixed in the ADR for this change and MUST be consistent across Domain, Application,
and Infrastructure layers.

#### Scenario: No BrandBrain domain type introduced

- GIVEN the completed implementation
- WHEN the Domain and Application namespaces are inspected
- THEN no type, class, or interface named BrandBrain or ClientBrandBrain exists
- AND ClientBrandManual is used consistently

---

## Implementation Notes

- Loader: `SocialStudio.Infrastructure.Social.ContentGeneration.EfClientBrandSnapshotLoader`
- Port: `SocialStudio.Application.Abstractions.Social.ContentGeneration.IClientBrandSnapshotLoader`
- Frozen output records (DO NOT change): `BrandSnapshot`, `StrategySnapshot`, `ClientMonthlyObjectiveSnapshot`
  in `SocialStudio.Domain.Social.ContentGeneration`
- Strategy source: `GeneralStrategyData` (table `social.general_strategies`)
- Objective source: `ContentPlanData.ObjectiveText` + `ContentPlanData.ObjectiveKpis`
  (table `social.content_plans`, ordered by current-month-first then year/month desc)
