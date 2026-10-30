# Design: Client Content Plan Model

## Change ID
`client-content-plan-model`

## Status
Design — architecture decisions resolved. Implements the technical HOW for the proposal. ADR: `docs/architecture/adr/0002-client-content-plan-model.md`.

## Scope reminder (Non-Goals are hard boundaries)
This design covers the **shape and persistence** of the context model: the Content Plan aggregate, the `Period` and `Provenance` Value Objects, the General Strategy rename, and the `EfClientBrandSnapshotLoader` re-sourcing. It does **not** design the AI generation orchestration, provider calls, or any Generated Content Package internals (Non-Goals #1, #2). The Plan only **references** packages.

---

## 1. Architecture Approach

Same hexagonal, sibling-aggregate pattern already used by `ClientStrategy` and `ClientMonthlyObjective`:

- **Domain** holds pure aggregates + Value Objects, no provider types. `Result<T>` factory/validation pattern preserved.
- **Application** holds vertical feature slices (MediatR command/query handlers) + ports.
- **Infrastructure** holds EF `*Data` entities, fluent config in `AppIdentityDbContext`, repositories, migrations, and the snapshot loader adapter.
- **Api** maps minimal endpoints per feature.

We do **not** introduce a navigation-property object graph between Client and its sibling context aggregates. The existing codebase deliberately uses **sibling aggregates keyed by `ClientId` FK** with no EF navigations (see `ClientMonthlyObjectiveData` comment: "No navigation properties — sibling aggregate pattern"). The Content Plan continues that pattern.

This choice keeps the architecture gate green (no provider leakage), keeps each aggregate independently loadable by the loader, and avoids a large `Client` aggregate that would have to be fully materialized for any context read.

---

## 2. Resolution of the 5 Open Questions

### Q1 — Canonical domain name: **keep `ClientBrandManual` / `BrandManual`**
**Decision:** The domain name stays `ClientBrandManual` (aggregate) / `BrandManual*` (snapshots). **"Brand Brain" is a UI label only**, mapped in the frontend copy layer.

**Rationale:** `ClientBrandManual` already spans Domain (`ClientBrandManual`, `BackgroundEntry`, `ColorEntry`, `LogoEntry`, `TypographyEntry`, `ClientBrandManualId`, errors), Infrastructure (`EfClientBrandManualRepository`, table, migration), Application slices, and Api endpoints. Renaming would be pure churn across 4 layers + a data migration for **zero domain behavior change**. The proposal's own Open Question #1 recommends this. Product alignment is satisfied by mapping the label at the UI boundary.

**Rejected:** Rename to `BrandBrain`. Better marketing alignment, but high cross-layer churn, a low-value table rename, and naming drift risk during an already-heavy change. Not worth it.

### Q2 — General Strategy persistence shape: **keep standalone aggregate, rename only**
**Decision:** Keep `ClientStrategy` as a **standalone sibling aggregate** (table `social.client_strategies`, unique `client_id`). Rename the type to **`GeneralStrategy`** (aggregate `GeneralStrategy`, id `GeneralStrategyId`, errors `GeneralStrategyErrors`) and the Application/Api slices to `GeneralStrategies`. The **table name and physical shape are unchanged** — only the C# type names and the EF entity (`ClientStrategyData` → `GeneralStrategyData`) change, with `ToTable` still pointing at `client_strategies` (or a low-risk table rename if cohesion is preferred — see Q-migration). `StrategySnapshot` (the generation-time frozen VO) keeps deriving from it unchanged.

**Rationale:** It is ALREADY a fully persisted aggregate with its own repository and 1:1-per-client guarantee. Folding it into an owned VO on `Client` would (a) force `Client` to be loaded/saved to edit strategy, (b) break the existing independent repository + endpoints, (c) require migrating `client_strategies` rows into a `clients`-owned table — a heavy, risky restructure for no behavioral gain. The enduring lifecycle is already correctly modeled as a standalone aggregate.

**Rejected:** Owned VO on `Client`. Conceptually "tidier" (strategy is client-level), but it discards working persistence, working endpoints, and the independent-load property the loader relies on. Pure cost, no benefit. YAGNI.

### Q3 — Period mapping in Postgres: **year/month int columns + `period_kind` discriminator**
**Decision:** Persist the monthly `Period` as the existing two `int` columns `year` and `month`, plus a new `period_kind smallint NOT NULL DEFAULT 0` discriminator column (0 = `Monthly`). Uniqueness becomes `(client_id, period_kind, year, month)`, preserving today's `(client_id, year, month)` semantics for the monthly variant.

**Rationale:** Keeps the data migration of `client_monthly_objectives` a **column-preserving** move (year/month carry over directly), preserves the existing unique key as the monthly `Period` key, and keeps the columns query-friendly (the loader orders by `year desc, month desc`). The `period_kind` discriminator is the **minimal** forward-compat seam: a future weekly/quarterly variant adds a kind value and reinterprets the int columns, without a canonical-key rewrite now. This honors YAGNI — we add one small column, not a generic period engine.

**Rejected:** Single canonical period-key string (e.g. `"2026-05"` / ISO interval). More "elegant" for arbitrary cadences, but (a) loses cheap range/desc queries the loader uses, (b) forces parsing logic in Infrastructure, (c) requires rewriting the unique index and migrating every row into a derived key now — over-engineering for a monthly-only feature. The VO stays cadence-open in the **domain**; the storage stays simple.

### Q4 — Calendar slot identity & traceability: **slot stores nullable `ProducedPackageId` (package-level reference)**
**Decision:** Each calendar slot has its own identity (`CalendarSlotId`, a Guid) and stores a **nullable `ProducedPackageId` (Guid)** referencing the Generated Content Package that was produced from that slot. It does **not** store individual asset ids. A slot is "one generation unit"; the package is the unit of generation output that already has durable per-asset storage and its own lifecycle.

**Rationale:** Package-level reference is the correct granularity: the Package aggregate already owns its assets (Non-Goal #2 forbids touching its internals), so the slot points at the package boundary, not into it. Nullable because a slot exists (planned) before anything is generated. Slot identity is required so the frontend can reorder, edit, and re-generate a specific slot, and so a regenerate replaces the reference rather than duplicating slots. The reference is **soft** (a plain Guid, no EF FK navigation) to preserve aggregate independence and avoid coupling the Plan's lifetime to package deletion.

**Rejected:** (a) Store nothing — loses traceability ("which package came from this slot"), blocks the regenerate-this-slot UX. (b) Store asset ids — violates Non-Goal #2 by reaching into Package internals and duplicates the Package's own asset model. (c) Hard EF FK to packages — couples deletes and crosses the aggregate boundary.

### Q5 — `Provenance.Status` enum: values and transitions
**Decision:** `ProvenanceStatus { Empty, Draft, Generated, Edited }`.

| Value | Meaning |
|---|---|
| `Empty` | Node exists structurally but has no content yet (e.g. a freshly created Plan's Content Strategy before anyone fills it). |
| `Draft` | Manually authored content, work-in-progress (Source = `Manual`). |
| `Generated` | Content was produced by AI (Source = `AI`), untouched since. |
| `Edited` | AI-generated content subsequently modified by a human (Source remains the original generator's, Status records human override). |

**Allowed transitions:**
```
Empty   -> Draft      (user starts typing)        Source=Manual
Empty   -> Generated  (AI fills the node)          Source=AI
Draft   -> Draft      (continued manual edits)     Source=Manual
Draft   -> Generated  (AI regenerates over manual) Source=AI   (overwrite-on-regenerate)
Generated -> Edited   (human tweaks AI output)     Source unchanged
Generated -> Generated (AI regenerates)            Source=AI
Edited  -> Edited     (continued manual edits)     Source unchanged
Edited  -> Generated  (AI regenerates over edits)  Source=AI
```
`GeneratedAt` is set/refreshed only on transitions **into** `Generated`. `Source` is `Manual` for `Draft`, `AI` for `Generated`/`Edited` (the original AI author). `Status` is an **audit trace, never a block** (proposal §Provenance, closed decision #467) — transitions are recorded, not enforced as a workflow gate. This change persists and surfaces the status; it does not implement the AI transitions themselves (Non-Goal #1).

---

## 3. Domain Model

### 3.1 Value Object — `Period`
Namespace `SocialStudio.Domain.Social.ContentPlans` (or a shared `Periods` namespace).

```
public readonly record struct Period
{
    public PeriodKind Kind { get; }   // enum: Monthly = 0
    public int Year  { get; }         // 1..9999  (monthly)
    public int Month { get; }         // 1..12    (monthly)

    public static Result<Period> Monthly(int year, int month);  // moves ClientMonthlyObjective.YearMin/Max etc. validation here
    public static Period RestoreMonthly(int year, int month);   // trusted re-hydration, no validation
}
```
- Validation (`YearMin/YearMax/MonthMin/MonthMax`) **moves off** `ClientMonthlyObjective` into `Period.Monthly`. The corresponding `ClientMonthlyObjectiveErrors.InvalidYear/InvalidMonth` migrate to `ContentPlanErrors`.
- Immutable, value-equality (record struct). Cadence-open by construction; only `Monthly` is implemented now (Non-Goal #5).

### 3.2 Value Object — `Provenance`
```
public sealed record Provenance(
    ProvenanceSource Source,        // enum: Manual, AI
    ProvenanceStatus Status,        // enum: Empty, Draft, Generated, Edited (Q5)
    DateTime? GeneratedAtUtc,       // set only when Status enters Generated
    string? PromptVersion)          // nullable; populated by the generator (future)
{
    public static Provenance Empty => new(ProvenanceSource.Manual, ProvenanceStatus.Empty, null, null);
    public static Provenance Manual(DateTime nowUtc) => new(..., ProvenanceStatus.Draft, ...);
    // AI/Generated/Edited constructors are exercised by the future generator (Non-Goal #1) but the VO supports them now.
}
```
Small, immutable, EF-owned (see §5.2). Decorates each generatable node.

### 3.3 Aggregate — `ContentPlan`
Namespace `SocialStudio.Domain.Social.ContentPlans`. Sibling aggregate, keyed by `(ClientId, Period)`; **succeeds** `ClientMonthlyObjective`.

```
public sealed class ContentPlan : Entity<ContentPlanId>
{
    public Guid ClientId { get; }
    public Guid OrganizationId { get; }
    public Period Period { get; }

    public ObjectiveNode       Objective       { get; }   // owns: Objectives text + Kpis  (migrated from ClientMonthlyObjective payload) + Provenance
    public ContentStrategyNode ContentStrategy { get; }    // per-period strategy, manual+AI-fillable + Provenance
    public ContentCalendar     Calendar        { get; }    // ordered List<CalendarSlot> + Provenance

    public DateTime CreatedAtUtc { get; }
    public DateTime UpdatedAtUtc { get; }

    Result<ContentPlan> Create(...);   // factory + validation
    static ContentPlan  Restore(...);  // trusted re-hydration for EF
    Result UpsertObjective(...);
    Result UpsertContentStrategy(...);
    Result ReplaceCalendarSlots(IReadOnlyList<...>);
    Result LinkSlotPackage(CalendarSlotId slotId, Guid producedPackageId);
}
```

Nodes (Domain types inside the aggregate, each carrying its own `Provenance`):
- **`ObjectiveNode`** — `string Objectives` (max 3000), `IReadOnlyList<string> Kpis` (max 20), `Provenance`. Carries today's `ClientMonthlyObjective` validation rules.
- **`ContentStrategyNode`** — per-period strategy fields (initially mirrors the kind of fields in `StrategySnapshot`: pillars/keywords/audience/cadence-style text, but a **separate, per-period** lifecycle), `Provenance`. Distinct from `GeneralStrategy` — they are not merged.
- **`ContentCalendar`** — `IReadOnlyList<CalendarSlot> Slots` (ordered by an explicit `SortOrder`), `Provenance`.
- **`CalendarSlot`** — `CalendarSlotId Id`, cadence/day descriptor, `ContentFormat` reference (the format to produce), `int SortOrder`, `Guid? ProducedPackageId` (Q4), `Provenance`.

The aggregate **references** Generated Content Packages only via `CalendarSlot.ProducedPackageId` (soft Guid). It never embeds or edits package data (Non-Goal #2).

### 3.4 Aggregate — `GeneralStrategy` (rename of `ClientStrategy`)
Identical structure/behavior to today's `ClientStrategy` (pillars, tone keywords, target audience, posting frequency, notes; `Create`/`Restore`/`Update`; `HasSomeSignal` rule). Only the type names change. `StrategySnapshot` continues to derive from it (it reads pillars/keywords/audience/frequency — no `Notes`), unchanged.

### 3.5 `ClientBrandManual` — unchanged
No domain change. UI maps "Brand Brain" label to it.

---

## 4. Data Flow & Integration Points

```
                 (enduring, 1:1 client)
  Client ──┬── ClientBrandManual            ─┐
           └── GeneralStrategy (was Strategy) │
                                              ├──► EfClientBrandSnapshotLoader ──► BrandSnapshot (STABLE contract)
  Client ───── ContentPlan (client+Period) ──┘            ▲
                 ├─ Objective  (was ClientMonthlyObjective)│ reads persisted Plan instead of ad-hoc objective
                 ├─ ContentStrategy                        │
                 └─ Calendar (slots ─► ProducedPackageId) ─┘ references (no ownership)
                                       │
                                       └──► Generated Content Package (separate aggregate, untouched)
```

**Loader re-sourcing (the contract-stability requirement):**
`EfClientBrandSnapshotLoader.LoadAsync(orgId, clientId)` keeps the **same signature** and keeps returning the **same `BrandSnapshot` record shape** (`ClientProfileSnapshot?`, `BrandManualSnapshot?`, `StrategySnapshot?`, `ClientMonthlyObjectiveSnapshot?`, referent/competitor lists, inferred map). Only its **internal source** changes:
- `StrategySnapshot` now comes from `GeneralStrategyData` (same table, renamed entity).
- `ClientMonthlyObjectiveSnapshot` now comes from the **persisted `ContentPlan`'s Objective node** for the relevant `Period` (selecting current month, else latest) instead of the standalone `client_monthly_objectives` table.

`BrandSnapshot`, `BrandSnapshot.FromBrandData`, `StrategySnapshot`, and `ClientMonthlyObjectiveSnapshot` record shapes are **unchanged**, so `generate-social-content-workflow` and every `BrandSnapshot` consumer keep compiling and behaving identically. This is the single hard constraint that protects the in-flight change.

---

## 5. Infrastructure / Persistence

### 5.1 EF entities (`*Data`) and DbContext
- `ClientStrategyData` → **`GeneralStrategyData`** (rename; same columns/jsonb config; `DbSet ClientStrategies` → `GeneralStrategies`).
- New **`ContentPlanData`** mapping table `social.content_plans`: `id`, `client_id`, `organization_id`, `period_kind smallint`, `year int`, `month int`, objective columns (`objective_text`, `objective_kpis` jsonb + objective `Provenance` owned columns), content-strategy columns (+ its `Provenance` owned columns), `created_at_utc`, `updated_at_utc`.
- New **`ContentPlanSlotData`** mapping table `social.content_plan_slots`: `id`, `content_plan_id` (FK → content_plans, cascade), `sort_order int`, format descriptor, `produced_package_id uuid NULL`, slot-level `Provenance` owned columns, timestamps. Calendar-level `Provenance` lives on `content_plans`.
- `ClientMonthlyObjectiveData` is **removed** after data migration completes (its `DbSet`, config block, and entity deleted in the final migration step).

### 5.2 `Provenance` and `Period` mapping (stays in Infrastructure)
- **`Provenance`** → EF **owned type** (`OwnsOne`) per host node, producing flat columns with a per-node prefix, e.g. `objective_provenance_source`, `objective_provenance_status`, `objective_provenance_generated_at_utc`, `objective_provenance_prompt_version`. Same prefixing for `content_strategy_provenance_*`, `calendar_provenance_*`, and `slot_provenance_*`. Enums persisted as `smallint` via `HasConversion<short>()` — provider mapping never leaks to Domain.
- **`Period`** → mapped as **plain columns** (`period_kind`, `year`, `month`) on `ContentPlanData`, reconstructed via `Period.RestoreMonthly` in the repository's mapping (not as an EF owned type, to keep the int columns directly indexable for the loader's ordering).
- Unique index `ix_content_plans_client_period` on `(client_id, period_kind, year, month)` — preserves the old `(client_id, year, month)` monthly key. Plus `ix_content_plans_client_id`, `ix_content_plans_organization_id`. FKs to `social.clients` and `platform.organizations` with cascade, mirroring the existing objective config.

### 5.3 Migration strategy (sequenced, one structural step per migration)
Per the gate rule and the proposal's High-severity data-migration risk, split into ordered EF migrations:

1. **`Rename_ClientStrategies_To_GeneralStrategies`** (low risk). Rename the EF entity; keep `ToTable("client_strategies")` OR rename the table to `general_strategies` with `migrationBuilder.RenameTable` + index renames. **Recommendation:** rename the table to `general_strategies` for naming cohesion since it is low-risk and 1:1; if the team prefers zero physical change, keep `client_strategies` and only rename in C#. Either way, columns/rows are untouched. (ADR records this as the chosen sub-option.)
2. **`Add_ContentPlans`** (additive). Create `social.content_plans` + `social.content_plan_slots` with all columns, owned `Provenance` columns, unique index, FKs. No data yet.
3. **`Migrate_MonthlyObjectives_Into_ContentPlans`** (data move — the heavy step). `INSERT INTO content_plans (...) SELECT ...` from `client_monthly_objectives`: map `year`/`month` directly, set `period_kind = 0`, copy `objectives`→`objective_text`, `kpis`→`objective_kpis`, set `objective_provenance_*` to `Source=Manual, Status=Draft, GeneratedAt=NULL` (existing rows are manual), leave content-strategy/calendar `Empty`. Preserve `created_at_utc`/`updated_at_utc`. Run inside the migration so it is gate-verified.
4. **`Drop_ClientMonthlyObjectives`** (cleanup). Drop `social.client_monthly_objectives` and its indexes only **after** step 3 is verified. Keeping it as a separate migration allows a safe pause/rollback point between data move and table drop.

Each migration must pass `pwsh -File scripts/gates/Run-Gates.ps1` (build + architecture + tests) before the next.

---

## 6. Application — ports & contracts

### 6.1 Renamed slices
- `Features/Social/ClientStrategies/**` → `Features/Social/GeneralStrategies/**` (Upsert, GetByClientId). Behavior identical; type renames only.

### 6.2 Replaced slices (objective → Content Plan)
- `Features/Social/ClientMonthlyObjectives/**` → `Features/Social/ContentPlans/**`:
  - `UpsertContentPlanCommand` (keyed `client + Period`) — upserts Objective + (optionally) Content Strategy + Calendar nodes.
  - `GetContentPlanQuery` (by `client + Period`) → `ContentPlanResponse` (Objective, ContentStrategy, Calendar slots, each with `Provenance`).
  - `ListContentPlansQuery` (by client) → list of `Period` summaries.
  - Validators move the year/month bounds onto `Period.Monthly`; the command validator validates the requested period and node payloads.

### 6.3 New ports (Infrastructure implements)
- `IContentPlanRepository` — `GetByClientAndPeriodAsync`, `ListByClientAsync`, `UpsertAsync`, `LinkSlotPackageAsync`.
- `IGeneralStrategyRepository` — rename of the existing `IClientStrategyRepository` (Get/Upsert by client).
- `IClientBrandSnapshotLoader` — **unchanged interface**; only its EF implementation re-sources (see §4).

All ports return domain types / `Result<T>`; no provider types cross into Application. AI/provider model selection stays behind existing ports (Non-Goal #1).

---

## 7. Api — endpoint shape changes

- `MapClientStrategyEndpoints` → **`MapGeneralStrategyEndpoints`** (`GET/PUT /clients/{clientId}/general-strategy`). Same payload, renamed route.
- `MapClientMonthlyObjectivesEndpoints` → **`MapContentPlanEndpoints`**:
  - `GET  /clients/{clientId}/content-plans/{year}/{month}` → full Plan (Objective + ContentStrategy + Calendar + per-node Provenance).
  - `PUT  /clients/{clientId}/content-plans/{year}/{month}` → upsert nodes.
  - `GET  /clients/{clientId}/content-plans` → list periods.
  - Sub-resources for node-level editing surfaced under the Plan (objective, content-strategy, calendar/slots), each returning `Provenance`. Slot read includes `producedPackageId` when linked.
- Response DTOs expose `Provenance { source, status, generatedAtUtc, promptVersion }` per node as an **audit badge** payload (not a block flag).

---

## 8. Coordination & Sequencing Risk (call-out)

`generate-social-content-workflow` is an **active** change that consumes `BrandSnapshot` via `IClientBrandSnapshotLoader`. The mitigation is structural, not procedural:

- The `IClientBrandSnapshotLoader` signature and the `BrandSnapshot` / `StrategySnapshot` / `ClientMonthlyObjectiveSnapshot` record shapes are **frozen** by this design. Only the loader's internal query source changes. As long as those records are untouched, the generation change cannot break.
- **Sequencing recommendation:** land the Domain/Infrastructure rename + Content Plan persistence + loader re-sourcing as one coherent change, and rebase/merge it relative to `generate-social-content-workflow` so the loader is re-sourced **once**. If both changes touch `EfClientBrandSnapshotLoader` concurrently, resolve by keeping this change's source-swap and the generation change's consumption untouched.
- **Verification:** the existing `BrandSnapshot` tests (inference-flag behavior) must stay green after re-sourcing — they are the contract guard. Add a test that the re-sourced loader reads the Objective from a persisted `ContentPlan`.

---

## 9. Architecture-gate compliance

- No EF Core / Npgsql / Supabase / ASP.NET types in Domain or Application — `Period`, `Provenance`, `ContentPlan`, `GeneralStrategy` are pure. Enum-to-`smallint` conversion and owned-type mapping live only in `AppIdentityDbContext` / `*Data` entities.
- Dependency direction preserved: Domain ← Application ← Infrastructure/Api.
- This is an **architecture change** (`require_adr_for_architecture_changes: true`) → ADR `0002-client-content-plan-model.md` authored.
- Docs-sync: `openspec/specs/*` updated for the renamed/added capabilities in the spec phase; this design + ADR satisfy the architecture-decision record requirement.

---

## 10. Residual Risks (design-level)
- **Data migration of objectives** (High) — mitigated by the 4-step sequenced migrations with a pause point before dropping the old table; gate-verified per step.
- **Loader contract drift** (High) — mitigated by freezing the snapshot record shapes; guarded by existing BrandSnapshot tests.
- **`period_kind` is a forward seam, not a feature** — only `Monthly` (0) is implemented; any other cadence is unimplemented and out of scope (Non-Goal #5).
- **Provenance status is audit-only** — must not become a workflow gate; spec/UI must treat it as a badge.
- **Soft package reference** (`ProducedPackageId` as plain Guid) means no DB-level FK integrity to packages — intentional, to keep aggregate independence; orphan references are tolerated and resolved at read time.
