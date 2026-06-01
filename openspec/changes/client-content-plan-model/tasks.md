# Tasks: Client Content Plan Model

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 1,400–1,800 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (Domain + GeneralStrategy rename) → PR 2 (ContentPlan persistence + migrations) → PR 3 (Loader re-sourcing + API + docs) |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Notes |
|------|------|-----------|-------|
| 1 | GeneralStrategy rename + Period/Provenance VOs + Domain ContentPlan aggregate | PR 1 | Base: dev; pure domain — no migration needed; all tests green |
| 2 | EF entities, 4 migrations, repositories, Application slices, API endpoints | PR 2 | Base: PR 1 branch; heaviest migration step here — rollback pause after step 3 |
| 3 | Loader re-sourcing, spec alignment fix, docs sync, gate run | PR 3 | Base: PR 2 branch; BrandSnapshot regression guard must be green |

---

## Phase 1 — Domain: GeneralStrategy Rename (PR 1, Work Unit 1) — refs GSTRAT-01, GSTRAT-02, GSTRAT-03

> TDD: each RED task writes failing tests first; GREEN task makes them pass.

- [x] 1.1 **[RED]** Add failing tests in `Incoders.Hive.Tests/Domain/Social/GeneralStrategyTests.cs` asserting `GeneralStrategy` type name, `GeneralStrategyId`, and `GeneralStrategyErrors` — mirrors `ClientStrategyTests.cs` scenarios.
- [x] 1.2 **[GREEN]** In `Incoders.Hive.Domain/Social/GeneralStrategies/` add `GeneralStrategyId.cs`, `GeneralStrategyErrors.cs`, and `GeneralStrategy.cs` as renames of the `ClientStrategies` counterparts; keep identical invariants. Delete old `ClientStrategies/` files.
- [x] 1.3 Update `Incoders.Hive.Tests/Domain/Social/ClientStrategyTests.cs` → rename file to `GeneralStrategyTests.cs`; update all type references.
- [x] 1.4 **Spec alignment fix**: update `openspec/changes/client-content-plan-model/specs/content-plan/spec.md` §CP-02 `Status` values to include `Empty` (`Empty|Draft|Generated|Edited`) to match design decision Q5. (Requirement: design-is-authoritative reconciliation note.)

## Phase 2 — Domain: Period and Provenance Value Objects (PR 1, Work Unit 1) — refs CP-01, CP-02

- [x] 2.1 **[RED]** Add `Incoders.Hive.Tests/Domain/Social/PeriodTests.cs` covering CP-01 scenarios: valid monthly, invalid month (13), invalid year (0), `RestoreMonthly` bypass, stable string representation.
- [x] 2.2 **[GREEN]** Create `Incoders.Hive.Domain/Social/ContentPlans/Period.cs` — `readonly record struct` with `PeriodKind` enum (`Monthly=0`), `Year`, `Month`; `Period.Monthly(int, int)` factory with `YearMin/Max/MonthMin/Max` validation (migrated from `ClientMonthlyObjectiveErrors`); `Period.RestoreMonthly(int, int)` trusted re-hydration; `ContentPlanErrors.cs` containing `InvalidYear`/`InvalidMonth`.
- [x] 2.3 **[RED]** Add `Incoders.Hive.Tests/Domain/Social/ProvenanceTests.cs` covering CP-02 scenarios: `Empty` factory, `Manual` factory (`Status=Draft`), immutability, and that provenance values do not block construction.
- [x] 2.4 **[GREEN]** Create `Incoders.Hive.Domain/Social/ContentPlans/Provenance.cs` — `sealed record` with `ProvenanceSource` enum (`Manual, AI`), `ProvenanceStatus` enum (`Empty, Draft, Generated, Edited`), `GeneratedAtUtc?`, `PromptVersion?`; `Provenance.Empty`, `Provenance.Manual(DateTime)` factories.

## Phase 3 — Domain: ContentPlan Aggregate (PR 1, Work Unit 1) — refs CP-03, CP-04, CP-05, CP-06

- [x] 3.1 **[RED]** Add `Incoders.Hive.Tests/Domain/Social/ContentPlanTests.cs` covering: valid create, empty objective rejected, calendar slot day-out-of-range (CP-04), slot order preserved, upsert overwrites (CP-03), period not-found behavior documented.
- [x] 3.2 **[GREEN]** Create `Incoders.Hive.Domain/Social/ContentPlans/ContentPlanId.cs`, `ObjectiveNode.cs`, `ContentStrategyNode.cs`, `CalendarSlot.cs` (`CalendarSlotId`, day, `ContentFormat`, `SortOrder`, `Guid? ProducedPackageId`, `Provenance`), `ContentCalendar.cs` (ordered `IReadOnlyList<CalendarSlot>`), `ContentPlan.cs` aggregate (`Create`, `Restore`, `UpsertObjective`, `UpsertContentStrategy`, `ReplaceCalendarSlots`, `LinkSlotPackage`).
- [x] 3.3 **[RED]** Add test in `ContentPlanTests.cs`: day 30 for February 2026 rejected via `Period`-aware day validation (CP-04 scenario "Day out of range").
- [x] 3.4 Run `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj` — all Phase 1–3 tests green, no regressions.

## Phase 4 — Infrastructure: GeneralStrategy Rename (PR 2, Work Unit 2) — refs GSTRAT-01

- [ ] 4.1 Rename `Incoders.Hive.Infrastructure/Persistence/Postgres/Entities/ClientStrategyData.cs` → `GeneralStrategyData.cs`; update class name and all property/config references; update `AppIdentityDbContext`: rename `DbSet<ClientStrategyData> ClientStrategies` → `DbSet<GeneralStrategyData> GeneralStrategies`.
- [ ] 4.2 Rename `Incoders.Hive.Infrastructure/Persistence/Postgres/Repositories/EfClientStrategyRepository.cs` → `EfGeneralStrategyRepository.cs`; update all `ClientStrategy` → `GeneralStrategy` type references inside.
- [ ] 4.3 Add EF migration `Rename_ClientStrategies_To_GeneralStrategies` via `dotnet ef migrations add`; in `Up()` use `migrationBuilder.RenameTable(name: "client_strategies", newName: "general_strategies", schema: "social")` plus matching index renames; `Down()` reverses.
- [ ] 4.4 Run gates: `pwsh -File scripts/gates/Run-Gates.ps1` — build + architecture gate must pass. Fix any `ClientStrategies` references in DbContext snapshot.

## Phase 5 — Infrastructure: ContentPlan Persistence (PR 2, Work Unit 2) — refs CP-03, CP-04, Q3, Q4, Q5

- [ ] 5.1 Create `Incoders.Hive.Infrastructure/Persistence/Postgres/Entities/ContentPlanData.cs` (all columns per design §5.1: `id`, `client_id`, `organization_id`, `period_kind smallint`, `year int`, `month int`, objective columns + owned `Provenance` prefixed columns, content-strategy columns + `Provenance`, calendar `Provenance`, timestamps). Create `ContentPlanSlotData.cs` (FK to content_plans cascade, `sort_order`, `content_format`, `produced_package_id uuid NULL`, slot `Provenance` columns, timestamps). Register both `DbSet`s in `AppIdentityDbContext`; add fluent config using `OwnsOne` per `Provenance` node with consistent prefix convention; add unique index `ix_content_plans_client_period` on `(client_id, period_kind, year, month)`; no EF navigation to packages (soft Guid only).
- [ ] 5.2 Add EF migration `Add_ContentPlans` — additive only: creates `social.content_plans`, `social.content_plan_slots`, unique index, FKs to `social.clients` and `platform.organizations`. No data yet.
- [ ] 5.3 Run gates after this migration to verify build + architecture pass before continuing.
- [ ] 5.4 Create `Incoders.Hive.Infrastructure/Persistence/Postgres/Repositories/EfContentPlanRepository.cs` implementing `IContentPlanRepository` (defined in next phase). Methods: `GetByClientAndPeriodAsync`, `ListByClientAsync`, `UpsertAsync`, `LinkSlotPackageAsync`. Maps `Period` via `Period.RestoreMonthly`; maps `Provenance` from owned columns; no navigation properties (sibling pattern).
- [ ] 5.5 Add EF migration `Migrate_MonthlyObjectives_Into_ContentPlans` — `INSERT INTO social.content_plans (...) SELECT ...` from `social.client_monthly_objectives`: `period_kind=0`, `year`/`month` carry over, `objectives` → `objective_text`, `kpis` → `objective_kpis` jsonb, objective `Provenance` = `Source=1(Manual), Status=1(Draft), GeneratedAt=NULL`; content-strategy and calendar nodes left as `Empty` provenance. Include data migration as raw SQL inside `migrationBuilder.Sql`. **PAUSE HERE — verify data migration in staging before proceeding.**
- [ ] 5.6 Add EF migration `Drop_ClientMonthlyObjectives` (separate migration, applied only after 5.5 verified): drop `social.client_monthly_objectives` and its indexes; remove `ClientMonthlyObjectiveData` entity and `DbSet` from `AppIdentityDbContext`.
- [ ] 5.7 Run full gate suite after 5.6: `pwsh -File scripts/gates/Run-Gates.ps1`.

## Phase 6 — Application: Ports and GeneralStrategy Slices (PR 2, Work Unit 2) — refs GSTRAT-01, GSTRAT-02, CP-03

- [ ] 6.1 Rename `Incoders.Hive.Application/Abstractions/Social/IClientStrategyRepository.cs` → `IGeneralStrategyRepository.cs`; update interface name and all references in handlers/DI.
- [ ] 6.2 Add `Incoders.Hive.Application/Abstractions/Social/IContentPlanRepository.cs` defining `GetByClientAndPeriodAsync(Guid orgId, Guid clientId, Period period, CancellationToken)`, `ListByClientAsync(Guid orgId, Guid clientId, CancellationToken)`, `UpsertAsync(ContentPlan plan, CancellationToken)`, `LinkSlotPackageAsync(Guid planId, CalendarSlotId slotId, Guid packageId, CancellationToken)`.
- [ ] 6.3 Rename application slice folder `Features/Social/ClientStrategies/` → `Features/Social/GeneralStrategies/`; update all type references (`ClientStrategyResponse` → `GeneralStrategyResponse`, command/handler/validator names).
- [ ] 6.4 **[RED]** Add `Incoders.Hive.Tests/Application/Social/GeneralStrategy/GeneralStrategyLifecycleTests.cs` — mirrors existing `ClientStrategyTests` scenarios for GSTRAT-01 (upsert creates, upsert overwrites, empty-signal rejected, length violations, get returns current).
- [ ] 6.5 **[GREEN]** Create `Features/Social/ContentPlans/Upsert/UpsertContentPlanCommand.cs`, `UpsertContentPlanCommandHandler.cs`, `UpsertContentPlanCommandValidator.cs` — keyed `(clientId, year, month)`; validator calls `Period.Monthly(year, month)` for period validation; validates Objective non-empty, max 3000 chars; validates KPIs max 20; validates calendar slot days against the period; handler upserts via `IContentPlanRepository`.
- [ ] 6.6 **[GREEN]** Create `Features/Social/ContentPlans/GetByPeriod/GetContentPlanQuery.cs`, `GetContentPlanHandler.cs`, `ContentPlanResponse.cs` — returns full plan with all nodes + per-node `ProvenanceDto`; returns not-found when plan absent.
- [ ] 6.7 **[GREEN]** Create `Features/Social/ContentPlans/ListByClient/ListContentPlansQuery.cs`, `ListContentPlansHandler.cs`, `ContentPlanSummaryResponse.cs` — lists all periods for the client.
- [ ] 6.8 **[RED]** Add `Incoders.Hive.Tests/Application/Social/ContentPlans/ContentPlanLifecycleTests.cs` covering CP-03 scenarios (create new period, overwrite, empty objective rejected, get not-found, list returns all periods) and CP-05 (no auto-generation triggered).
- [ ] 6.9 Register `IContentPlanRepository` → `EfContentPlanRepository` and `IGeneralStrategyRepository` → `EfGeneralStrategyRepository` in DI composition root.

## Phase 7 — Api Layer: Endpoints (PR 2, Work Unit 2) — refs GSTRAT-01, CP-03, BS-01

- [ ] 7.1 Rename `Incoders.Hive.ApiService/Endpoints/Social/MapClientStrategyEndpoints.cs` → `MapGeneralStrategyEndpoints.cs`; update route from `/clients/{clientId}/strategy` to `/clients/{clientId}/general-strategy`; update all handler/command references.
- [ ] 7.2 Create `Incoders.Hive.ApiService/Endpoints/Social/MapContentPlanEndpoints.cs`: `GET /clients/{clientId}/content-plans`, `GET /clients/{clientId}/content-plans/{year}/{month}`, `PUT /clients/{clientId}/content-plans/{year}/{month}`. Response DTOs include per-node `Provenance { source, status, generatedAtUtc, promptVersion }`. Register in API route builder.
- [ ] 7.3 Remove `MapClientMonthlyObjectivesEndpoints.cs` (superseded by 7.2 — CP-06).
- [ ] 7.4 **[RED]** Add `Incoders.Hive.Tests/Api/Social/ContentPlanEndpointTests.cs` — smoke tests for the three new Content Plan endpoints (create, get, list) and the renamed general-strategy endpoint.
- [ ] 7.5 Run full gate suite: `pwsh -File scripts/gates/Run-Gates.ps1`.

## Phase 8 — Infrastructure: Loader Re-sourcing (PR 3, Work Unit 3) — refs BS-01, BS-02, BS-03, BS-04

- [ ] 8.1 **[RED]** Verify existing `Incoders.Hive.Tests/Infrastructure/Social/ContentGeneration/ClientBrandSnapshotLoaderTests.cs` still compiles and passes (acts as regression guard before re-sourcing).
- [ ] 8.2 **[RED]** Add new test in `ClientBrandSnapshotLoaderTests.cs`: loader reads `MonthlyObjective` from a persisted `ContentPlanData` row rather than `ClientMonthlyObjectiveData` (per BS-02 scenario "Loader reads Content Plan Objective for period"). Add test: missing Content Plan yields inferred objective without throwing (BS-02 scenario "Missing Content Plan yields inferred objective").
- [ ] 8.3 **[GREEN]** In `Incoders.Hive.Infrastructure/Social/ContentGeneration/EfClientBrandSnapshotLoader.cs`, replace the `ClientMonthlyObjectives` query with a `ContentPlans` query (ordered `period_kind=0, year desc, month desc`, prefer current month); map `Objective` node → `ClientMonthlyObjectiveSnapshot(Objectives, Kpis)`. Replace `ClientStrategies` DbSet reference with `GeneralStrategies`. Keep `BrandSnapshot.FromBrandData` call signature and argument order unchanged (BS-01 contract stability).
- [ ] 8.4 Run `dotnet test Incoders.Hive.Tests/Incoders.Hive.Tests.csproj` — ALL `BrandSnapshot` and `ClientBrandSnapshotLoader` tests must be green.
- [ ] 8.5 Run full gate suite: `pwsh -File scripts/gates/Run-Gates.ps1`.

## Phase 9 — Docs Sync and Cleanup (PR 3, Work Unit 3) — refs GSTRAT-01–03, CP-01–06, BS-01–04

- [ ] 9.1 Update `openspec/specs/general-strategy/spec.md` (main spec) to reflect `GeneralStrategy` rename (was `ClientStrategy`); merge delta from `openspec/changes/client-content-plan-model/specs/general-strategy/spec.md`.
- [ ] 9.2 Create or update `openspec/specs/content-plan/spec.md` with the full Content Plan spec (merge from change delta).
- [ ] 9.3 Create or update `openspec/specs/brand-snapshot/spec.md` with the BS spec (merge from change delta).
- [ ] 9.4 Update `openspec/changes/client-content-plan-model/state.yaml` to mark phases complete.
- [ ] 9.5 Remove `Features/Social/ClientMonthlyObjectives/` application folder (superseded — CP-06); remove `Domain/Social/ClientMonthlyObjectives/` domain folder after confirming no remaining usages.
- [ ] 9.6 Final gate run: `pwsh -File scripts/gates/Run-Gates.ps1` — all MUST gates green before declaring done.
