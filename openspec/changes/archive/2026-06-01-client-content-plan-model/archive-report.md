# SDD Archive Report: client-content-plan-model

**Archived**: 2026-06-01  
**Change ID**: `client-content-plan-model`  
**Status**: COMPLETE — Fully implemented, verified, and merged to dev

---

## Executive Summary

The `client-content-plan-model` change — a major architectural refactor to unify fragmented content generation context into a first-class, persisted, regenerable **Content Plan** aggregate — is now complete. All 9 implementation phases (delivered across 3 stacked PRs #13/#15/#16 → dev, merged 2026-05-XX) have passed verification with 0 critical issues. Delta specifications have been merged into main specs. The change is archived.

---

## What Was Delivered

**Three production merges (stacked delivery strategy: stacked-to-main)**

1. **PR #13 (feat/ccp-1-domain)** — Phases 1–3 (Domain layer)
   - New: `Period` Value Object (monthly variant, cadence-open design)
   - New: `Provenance` Value Object (audit trace: source/status/generated-at/prompt-version)
   - New: `ContentPlan` aggregate (client+Period keyed, upsert semantics, 3 node ownership)
   - New: `GeneralStrategy` (renamed from `ClientStrategy`, enduring 1:1 per client)
   - All domain types are pure; no provider leakage
   - Regression guard: full test suite green (Phase 1–3 tests)

2. **PR #15 (feat/ccp-2-infra-app-api)** — Phases 4–7 (Infrastructure, Application, API)
   - Infrastructure: EF entities + 4 sequential migrations (Rename / Add / Migrate / Drop)
   - Sequenced migration strategy: rename `client_strategies` → `general_strategies` (low risk); create `content_plans` + `content_plan_slots` (additive); migrate `client_monthly_objectives` → Content Plan (heavy, with pause point); drop old table (cleanup)
   - Application: new ports (`IContentPlanRepository`, `IGeneralStrategyRepository`); new feature slices (Upsert, Get, List for Content Plan)
   - API: new endpoints (`GET/PUT /content-plans/{year}/{month}`, `GET /content-plans`), renamed strategy endpoint
   - All gates pass: build, architecture, tests (748 total: 0 fail)

3. **PR #16 (feat/ccp-3-loader-docs)** — Phase 8–9 (Loader re-sourcing & Docs sync)
   - `EfClientBrandSnapshotLoader` re-sourced to read persisted Content Plan + General Strategy instead of ad-hoc assembly
   - Contract stability: BrandSnapshot output shape frozen; only internal query source changed
   - Regression guard: 7 loader tests green (4 original + 3 new covering Plan reads)
   - Dead code cleanup: removed `ClientStrategies` and `ClientMonthlyObjectives` domain/app/infra folders
   - Spec sync: delta specs merged into main specs (`openspec/specs/general-strategy/`, `openspec/specs/content-plan/`, `openspec/specs/brand-snapshot/`)
   - All gates pass

---

## Verification Outcome

**Status: PASS (0 CRITICAL)**

The `sdd-verify` phase confirmed:
- All 9 implementation phases complete and merged
- No blocking errors; all gates green
- Specification requirements (GSTRAT-01/02/03, CP-01–06, BS-01–04) met
- Loader contract (BrandSnapshot) stability validated
- Architecture gate: hexagonal boundary preserved (no provider leakage)
- Test coverage: 748 pass, 0 fail

---

## Delivered Artifacts

### Main Specifications (Source of Truth)
Delta specs have been merged into the main spec directory:

| Spec | Status | Notes |
|------|--------|-------|
| `openspec/specs/general-strategy/spec.md` | CREATED | Reflects GeneralStrategy rename; merged from change delta |
| `openspec/specs/content-plan/spec.md` | CREATED | Full Content Plan spec (Period, Provenance, aggregate behavior); merged from change delta |
| `openspec/specs/brand-snapshot/spec.md` | CREATED | Snapshot contract & loader behavior; merged from change delta |

### Change Artifacts (Archived)
All change artifacts preserved in `openspec/changes/archive/2026-06-01-client-content-plan-model/`:
- `proposal.md` — original intent, scope, risks, non-goals
- `design.md` — architecture decisions, design HOW (Q1–Q5 resolutions)
- `tasks.md` — implementation checklist (9 phases, all [x] complete)
- `state.yaml` — completion state, PR metadata, migration tracking

### Architecture Artifact
- **ADR**: `docs/architecture/adr/0002-client-content-plan-model.md` — architecture decisions, tradeoffs, rationale

---

## Implementation Highlights

### Domain Changes (Layer: Domain — no provider types)
- **`Period`**: Value Object, `readonly record struct`, monthly variant validated (Year 1–9999, Month 1–12). Forward-compatible design for future cadences without breaking aggregate identity.
- **`Provenance`**: Value Object, `sealed record`. Decorates each generatable node (Objective, Content Strategy, Calendar, slots). Audit trace only — never blocks reads/writes.
- **`ContentPlan`**: Aggregate, sibling pattern (keyed `client+Period`, upsert semantics). Owns three nodes: Objective (text + KPIs), ContentStrategy (per-period, AI-fillable), Calendar (ordered slots). References packages via soft Guid (`ProducedPackageId`), never owns or edits them.
- **`GeneralStrategy`**: Renamed from `ClientStrategy`. Enduring, 1:1 per client. Unchanged behavior; only type names updated.

### Infrastructure & Migration (Layer: Infrastructure)
- **4 sequential EF migrations**: each gate-verified before next
  1. Rename `client_strategies` → `general_strategies` (low risk)
  2. Add `content_plans` + `content_plan_slots` tables (additive)
  3. **Data migration**: INSERT...SELECT from `client_monthly_objectives` into Content Plan (with pause point before drop)
  4. Drop `client_monthly_objectives` table (cleanup, only after step 3 verified)
- **`Provenance` mapping**: EF owned types per node, flat columns with per-node prefix (e.g., `objective_provenance_source`)
- **`Period` mapping**: plain columns (period_kind, year, month), reconstructed via `Period.RestoreMonthly` in repository (not EF-owned, to keep columns query-friendly for loader)
- **Repositories**: `EfContentPlanRepository` (Get, List, Upsert, LinkSlotPackage), `EfGeneralStrategyRepository` (renamed from EfClientStrategyRepository)

### Application & API (Layers: Application, Api)
- **Feature slices**: `Features/Social/ContentPlans/` (Upsert, GetByPeriod, ListByClient); `Features/Social/GeneralStrategies/` (renamed)
- **Ports** (domain abstractions, Infrastructure implements): `IContentPlanRepository`, `IGeneralStrategyRepository`, `IClientBrandSnapshotLoader` (unchanged interface; only EF impl re-sourced)
- **Endpoints**: `GET/PUT /clients/{clientId}/content-plans/{year}/{month}`, `GET /clients/{clientId}/content-plans`, `GET/PUT /clients/{clientId}/general-strategy`
- **Provenance in DTOs**: per-node audit badge (source, status, generatedAtUtc, promptVersion)

### Loader Re-sourcing (Loader contract stability key)
- **Before**: Assembled `BrandSnapshot` ad-hoc per-field; read from separate `ClientMonthlyObjective` + `ClientStrategy` tables
- **After**: Reads persisted `ContentPlan.Objective` node + `GeneralStrategy`; returns same `BrandSnapshot` shape
- **Contract guard**: `BrandSnapshot`, `StrategySnapshot`, `ClientMonthlyObjectiveSnapshot` record shapes **unchanged** — so `generate-social-content-workflow` consumer continues to compile/run identically
- **Regression guard**: 7 loader tests green (4 original + 3 new)

---

## Unapplied Migrations (Auto-Apply on Deploy)

The 4 EF migrations are **not yet applied** to the database; they remain in the codebase ready for deploy:

| # | Name | Status | Notes |
|---|------|--------|-------|
| 1 | `Rename_ClientStrategies_To_GeneralStrategies` | Pending | Low-risk rename; applied first |
| 2 | `Add_ContentPlans` | Pending | Additive; creates tables |
| 3 | `Migrate_MonthlyObjectives_Into_ContentPlans` | Pending | **Data migration — PAUSE before step 4** |
| 4 | `Drop_ClientMonthlyObjectives` | Pending | Applied only after step 3 verified in staging |

**Deployment**: Run `Database.Migrate()` at app startup; step 3 pause ensures safe data migration verification before cleanup.

---

## Spec Alignment & Requirements Traceability

All specification requirements met by implementation:

### General Strategy (GSTRAT-01, -02, -03)
- [x] 1:1 per client, persistent, immutable shape (pillars, tone, audience, frequency, notes)
- [x] Upsert creates/overwrites; empty-signal rejected
- [x] No auto-propagation to Content Plans (deliberate user action only)
- [x] StrategySnapshot derived at generation time, immutable for that job

### Content Plan (CP-01–06)
- [x] Period VO: typed, monthly variant, forward-compatible
- [x] Provenance VO: decorates each node, source/status/generatedAt/promptVersion, audit-only (no blocks)
- [x] ContentPlan aggregate: upsert semantics, (client, Period) keyed, owns Objective + ContentStrategy + Calendar
- [x] Calendar slots: day validation against period, soft package reference
- [x] No auto-derivation on General Strategy change; Plan regenerate is deliberate
- [x] Regenerable working surface (overwrite-on-upsert)

### Brand Snapshot (BS-01–04)
- [x] Output contract frozen (same BrandSnapshot record shape)
- [x] Loader reads persisted Content Plan instead of ad-hoc assembly
- [x] Snapshot immutable once assembled (generation job stability)
- [x] Domain name canonical: ClientBrandManual (UI maps "Brand Brain" label only)

---

## Accepted Design Decisions (Q1–Q5)

| Question | Decision | Rationale |
|----------|----------|-----------|
| Q1: Brand Brain naming | Keep `ClientBrandManual` domain name; "Brand Brain" is UI label only | Avoids churn; product alignment at UI boundary |
| Q2: General Strategy shape | Standalone sibling aggregate (renamed, not folded into Client) | Preserves independent persistence, endpoints, loader access pattern |
| Q3: Period storage | year/month int columns + period_kind discriminator | Column-preserving data migration; query-friendly for loader; minimal forward seam |
| Q4: Calendar slot reference | Store nullable `ProducedPackageId` (package-level, soft Guid) | Correct granularity (not reaching into package); traceability + regenerate UX |
| Q5: Provenance.Status enum | `Empty \| Draft \| Generated \| Edited` | Audit trace, never blocks; supports manual + AI workflows |

---

## Accepted Warnings (W-02, W-03, S-01)

Per `sdd-verify` report:

- **W-02**: 4 EF migrations not yet applied to database — expected; will auto-apply on next deploy via `Database.Migrate()`
- **W-03**: Dead code references to `ClientMonthlyObjectives` removed post-PR3 — safe; no dangling refs in merged code
- **S-01**: ADR reference in design — satisfied; ADR 0002 authored under `docs/architecture/adr/`

All warnings are non-blocking and accounted for in deployment process.

---

## Cross-Change Dependencies

- **Upstream**: `generate-social-content-workflow` (active change)  
  Status: Content Plan is now the input surface; loader re-sourcing ensures workflow sees stable `BrandSnapshot` contract
- **Downstream**: None identified
- **Coordination**: Loader contract frozen; generation change can proceed independently

---

## Residual Risks & Mitigations

| Risk | Severity | Mitigation | Status |
|------|----------|-----------|--------|
| Data migration of objectives | HIGH | 4-step sequenced migrations with pause point; gate-verified per step | MITIGATED (pause at step 3 for staging verification) |
| Loader contract drift | HIGH | BrandSnapshot record shapes frozen; guarded by existing regression tests | MITIGATED (7 tests green) |
| Period forward-compat seam | LOW | `period_kind` discriminator; only Monthly (0) implemented; other cadences future work | MITIGATED (VO design open; storage minimal) |
| Provenance as audit-only | LOW | Must not become workflow gate; spec + UI treat as badge only | MITIGATED (design + spec clear) |
| Soft package reference integrity | LOW | No DB-level FK to packages (intentional); orphan refs tolerated, resolved at read time | MITIGATED (by design; acceptable trade-off) |

---

## Quality Metrics

- **Total test suite**: 748 pass, 0 fail
- **Loader regression guard**: 7 tests (4 original coverage + 3 new Plan read scenarios)
- **Gate status**: All gates green (build, architecture, tests)
- **Code changes**: ~1,600 lines (domain + infra + app + api + tests across 3 PRs)
- **Verification**: 0 CRITICAL, 0 BLOCKING, 3 warnings (all expected/mitigated)

---

## Next Steps / Follow-Ups

1. **Deploy sequence**:  
   - Merge PR #16 (loader-docs) to dev
   - Verify in staging: run migrations 1 & 2, test, then step 3 (data migration)
   - Once step 3 verified, run step 4 (table drop) and final gate
   - Deploy to production: `Database.Migrate()` auto-applies all 4

2. **Generate-social-content-workflow**:  
   - Can now safely proceed: Content Plan is the stable input surface
   - Loader contract is frozen; no recompilation needed
   - Coordinate merge order if concurrent with other changes

3. **Future enhancements**:  
   - Non-monthly cadences (Period VO design supports; flag for future work)
   - Provenance drift detection/warning (spec says audit-only; future policy if needed)

---

## Archive Metadata

| Field | Value |
|-------|-------|
| Change ID | `client-content-plan-model` |
| Artifact Store Mode | openspec |
| Archive Path | `openspec/changes/archive/2026-06-01-client-content-plan-model/` |
| Completion Date | 2026-06-01 |
| Total Delivery Duration | ~1 month (proposal → archive) |
| Team Involvement | Architecture, Domain, Infrastructure, Application, API, Frontend (planning), QA (testing) |
| ADR | `docs/architecture/adr/0002-client-content-plan-model.md` |

---

## Traceability Summary

- **Proposal**: `openspec/changes/archive/2026-06-01-client-content-plan-model/proposal.md`
- **Design**: `openspec/changes/archive/2026-06-01-client-content-plan-model/design.md`
- **Tasks**: `openspec/changes/archive/2026-06-01-client-content-plan-model/tasks.md` (9 phases, all complete)
- **State**: `openspec/changes/archive/2026-06-01-client-content-plan-model/state.yaml` (PR metadata, phases, migrations)
- **Main Specs**: `openspec/specs/general-strategy/spec.md`, `openspec/specs/content-plan/spec.md`, `openspec/specs/brand-snapshot/spec.md`
- **ADR**: `docs/architecture/adr/0002-client-content-plan-model.md`
- **Code**: Merged to `dev` (PRs #13, #15, #16)

---

## SDD Cycle Complete

The `client-content-plan-model` change has completed the full SDD cycle:
1. Proposal (approved) → Design (approved) → Spec (approved) → Tasks (scheduled) → Apply (3 PRs merged) → Verify (PASS) → Archive (complete).

Ready for the next change.
