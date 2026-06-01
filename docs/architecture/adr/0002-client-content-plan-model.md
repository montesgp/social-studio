# ADR 0002 - Client Content Plan Model

## Status
Accepted

## Context
The per-client, per-period generation context that feeds social content generation is fragmented and assembled implicitly at job time:

- `ClientStrategy` (aggregate, table `social.client_strategies`, unique `client_id`) persists the enduring strategy.
- `ClientMonthlyObjective` (aggregate, table `social.client_monthly_objectives`, unique `client_id+year+month`, with `year`/`month` as two `int` columns) holds the per-month objective + KPIs.
- `ClientBrandManual` (aggregate) holds enduring brand assets.
- `EfClientBrandSnapshotLoader` freezes all of the above into a `BrandSnapshot` at job start.

The "plan" a community manager reasons about (objective + content strategy + calendar) has no persisted home; it is re-derived each run, cannot be reviewed/edited before generation, and has no place to attach AI-generated strategy or a calendar. The active `generate-social-content-workflow` change needs a stable, inspectable input surface. This is an architecture change (`require_adr_for_architecture_changes: true`).

A prior factual error (sub-agent hallucination) claimed `ClientStrategy` was not persisted. It IS fully persisted (verified). "General Strategy" is therefore a **rename/restructure**, not a net-new promotion.

## Decision

Introduce a persisted, regenerable **Content Plan** aggregate and reconcile enduring client context, following the existing hexagonal sibling-aggregate pattern (aggregates keyed by `ClientId` FK, no EF navigations, provider types confined to Infrastructure).

1. **Brand Brain naming** — Keep `ClientBrandManual` / `BrandManual` as the single canonical domain name. "Brand Brain" is a UI label only. (Rejected: rename to `BrandBrain` — high cross-layer churn for zero behavior change.)

2. **General Strategy** — Keep the existing `ClientStrategy` as a **standalone sibling aggregate** (1:1 per client). Rename the type to `GeneralStrategy`; physical table and shape unchanged (optionally rename `client_strategies` → `general_strategies` as a low-risk table rename). (Rejected: fold into an owned VO on `Client` — discards working repository/endpoints and forces a risky table restructure.)

3. **Content Plan aggregate** — New aggregate keyed by `(ClientId, Period)`, succeeding `ClientMonthlyObjective`. Owns three provenance-decorated nodes: **Objective** (migrated from the monthly objective payload), **Content Strategy** (per-period, distinct lifecycle from General Strategy — they are NOT merged), and **Content Calendar** (ordered slots). It **references** Generated Content Packages; it never owns or edits package internals.

4. **`Period` Value Object** — Cadence-open VO; only the `Monthly { Year, Month }` variant implemented now. Validation moves off `ClientMonthlyObjective` into `Period.Monthly`. Persisted as `year`/`month` int columns plus a `period_kind smallint` discriminator (0 = Monthly); unique `(client_id, period_kind, year, month)` preserves the old monthly key. (Rejected: single canonical period-key string — loses cheap range/desc queries, adds parsing, over-engineers a monthly-only feature.)

5. **`Provenance` Value Object** — `{ Source: Manual|AI, Status: Empty|Draft|Generated|Edited, GeneratedAtUtc?, PromptVersion? }`, decorating each generatable node. EF **owned type** with flat prefixed columns; enums persisted as `smallint`. `Status` is an **audit trace, never a blocking gate**. Transitions: `Empty→Draft/Generated`, `Draft→Generated`, `Generated→Edited/Generated`, `Edited→Generated/Edited`; `GeneratedAtUtc` set only on transitions into `Generated`.

6. **Calendar slot traceability** — Each slot has its own `CalendarSlotId` and a nullable `ProducedPackageId` (plain Guid, **soft** reference, no EF FK) pointing at the produced Generated Content Package boundary — not individual asset ids. (Rejected: store nothing — loses traceability; store asset ids — violates the package-internals non-goal; hard FK — couples aggregate lifetimes.)

7. **Loader re-sourcing with stable contract** — `EfClientBrandSnapshotLoader` reads the persisted Content Plan's Objective node and the renamed General Strategy. The `IClientBrandSnapshotLoader` signature and the `BrandSnapshot` / `StrategySnapshot` / `ClientMonthlyObjectiveSnapshot` record shapes are **frozen** so `generate-social-content-workflow` does not break.

8. **Sequenced migrations** — (1) rename strategies (low risk), (2) additively add `content_plans` + `content_plan_slots`, (3) data-move objectives into Content Plans, (4) drop the old objectives table only after verification. One structural step per migration; each gate-verified.

## Consequences

- The generation context becomes a first-class, persisted, inspectable, regenerable artifact with per-node provenance.
- General Strategy and Content Strategy remain distinct (enduring vs per-period); merging is explicitly avoided.
- The `BrandSnapshot` output contract stays stable, protecting the in-flight generation change; only the loader's data source changes.
- High-risk objective data migration is bounded by a sequenced, gate-verified migration plan with a rollback pause point before the old table is dropped.
- `period_kind` is a minimal forward-compat seam; non-monthly cadences remain unimplemented (YAGNI).
- The Plan references packages softly; orphan references are tolerated and resolved at read time, preserving aggregate independence.
- Hexagonal boundaries are preserved: `Period`, `Provenance`, `ContentPlan`, `GeneralStrategy` are pure domain; EF owned-type and enum conversions live only in Infrastructure.

## Related
- Proposal: `openspec/changes/client-content-plan-model/proposal.md`
- Design: `openspec/changes/client-content-plan-model/design.md`
- Upstream consumer: `openspec/changes/generate-social-content-workflow`
- Engram: `architecture/client-content-plan-model` (obs #467 — ClientStrategy already persisted; #454 staleness/regeneration decisions)
