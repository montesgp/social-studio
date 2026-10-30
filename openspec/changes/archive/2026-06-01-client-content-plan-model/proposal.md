# Proposal: Client Content Plan Model

## Change ID
`client-content-plan-model`

## One-line intent
Make the per-client, per-period generation **context** a first-class, persisted, regenerable domain artifact — the **Content Plan** — instead of an ad-hoc bundle assembled at job time, by unifying today's separate `ClientStrategy` (enduring) and `ClientMonthlyObjective` (per-month) concepts into a coherent context model.

---

## Intent

### What problem
Today the context that feeds content generation is fragmented and assembled implicitly:

- `ClientMonthlyObjective` (aggregate, table `social.client_monthly_objectives`, unique `client_id+year+month`, with `year`/`month` as two `int` columns) holds the per-month objective + KPIs.
- `ClientStrategy` (aggregate, table `social.client_strategies`, unique `client_id`) already persists the enduring strategy — content pillars, tone keywords, target audience, posting frequency, notes. (`StrategySnapshot` is a separate **frozen copy derived from it** at job time, not the source of truth.)
- `ClientBrandManual` (aggregate) holds enduring brand assets (palette, logos, typography, voice/tone).
- `EfClientBrandSnapshotLoader` reads all of the above plus client profile + referent/competitor accounts and freezes them into a `BrandSnapshot` **at job start**, marking missing fields as `Inferred`.

The result: the "plan" a community manager would reason about (objective for this month + the AI content strategy + the calendar of what to post when) has **no home**. It is reconstructed each generation run, cannot be reviewed/edited before generating, and there is no clean place to attach AI-generated strategy or a calendar.

### Why now
The active change `generate-social-content-workflow` is about to build the orchestration that *generates* content. That workflow needs a stable, inspectable **input surface**. Building generation on top of the current ad-hoc context assembly will bake the fragmentation into the new workflow. We want the context model settled and persisted **before** the generation workflow consumes it, so generation reads a prepared Content Plan rather than re-deriving context every run.

### What success looks like
- A community manager opens a client for a given period and sees one coherent **Content Plan**: Objective + Content Strategy + Content Calendar, each tagged with where it came from (manual vs AI).
- The enduring, client-level facts (**Brand Brain** = today's `BrandManual`, and **General Strategy**) live on the Client, are entered once, and persist across periods.
- `EfClientBrandSnapshotLoader` reads a *persisted* Content Plan for the period instead of assembling objective + strategy ad-hoc.
- The model is not locked to monthly cadence: the period is a Value Object that can later represent other cadences.
- Regenerating is a deliberate user action; historical Generated Content Packages are never retro-edited.

---

## The Target Model

### Enduring (client-level, 1:1 with Client)
| Concept | Source today | Target |
|---|---|---|
| **Brand Brain** | `ClientBrandManual` aggregate | Reconciled to ONE domain name. "Brand Brain" is the product/UI name; the domain keeps a single canonical name (the existing `BrandManual`/`ClientBrandManual`). Not duplicated. |
| **General Strategy** | `ClientStrategy` aggregate (table `social.client_strategies`, 1:1 client) | **Rename/restructure** of the existing persisted `ClientStrategy` (content pillars, tone keywords, target audience, posting cadence, notes) into General Strategy. Enduring lifecycle, persistence already exists. |

### Per-period — the **Content Plan** aggregate (regenerable working surface)
Keyed by `client + Period` (upsert), succeeding today's `ClientMonthlyObjective`:

| Node | Description | Provenance-decorated |
|---|---|---|
| **Period** | Value Object. Today: `Monthly { Year, Month }`. NOT baked into the aggregate name. | n/a |
| **Objective** | The period objective + KPIs (today's `ClientMonthlyObjective` payload). | yes |
| **Content Strategy** | Per-period strategy, manually editable AND AI-fillable. Distinct lifecycle from General Strategy. | yes |
| **Content Calendar** | Ordered **slots**: each slot maps a cadence/day → a content format. Each slot is one generation unit. | yes |

References (does NOT own): the immutable **Generated Content Package** (separate aggregate, already has durable per-asset storage). The Plan points at packages produced from it; it never embeds or edits them.

### Cross-cutting Value Object: **Provenance**
`Provenance { Source: Manual | AI, GeneratedAt, Status, PromptVersion? }` decorates each generatable node (Objective, Content Strategy, Calendar — and per-slot where meaningful). It is a decorator on each node, **not** a single flag on the aggregate, and is an **audit trace** ("generated with the strategy of date X"), **not** a blocking drift flag.

### Two strategies — do not merge
- **General Strategy**: enduring, client-level. Changes rarely. This is today's persisted `ClientStrategy` aggregate, renamed/restructured.
- **Content Strategy**: per-period, AI-generated/refined, lives inside the Content Plan.

Merging them would lose the distinction between "how this brand always operates" and "what we decided for this specific period".

---

## What Changes

### Domain (`SocialStudio.Domain`)
- Introduce **`Period`** Value Object (initial variant: monthly `{Year, Month}`), with validation moved off the current loose `int Year`/`int Month` on `ClientMonthlyObjective`.
- Introduce **`Provenance`** Value Object `{ Source, GeneratedAt, Status, PromptVersion? }`.
- Introduce the **Content Plan** aggregate (period-keyed) owning Objective + Content Strategy + Content Calendar (ordered slots), each decorated with `Provenance`. Migrate `ClientMonthlyObjective`'s payload into the Objective node.
- **Rename/restructure the existing `ClientStrategy` aggregate into General Strategy** — it already persists pillars/tone/audience/frequency/notes 1:1 per client; this is a rename + alignment, not a net-new model. Leave `StrategySnapshot` (the generation-time frozen copy) deriving from it.
- Reconcile **Brand Brain ↔ BrandManual** to a single canonical domain name.
- The Content Plan **references** Generated Content Package ids; it does not own package internals.

### Application (`SocialStudio.Application`)
- Replace/rename `ClientMonthlyObjectives` feature slices (Upsert/Get/List) with **Content Plan** use cases (upsert per `client+Period`, get-by-`client+Period`, list-by-client).
- Add ports for persisting/reading the Content Plan, General Strategy, and calendar slots.
- Update `IClientBrandSnapshotLoader` contract so the snapshot is built from a persisted Content Plan + enduring client context, not ad-hoc per-field assembly.
- Keep AI/provider model selection abstracted behind ports (no hardcoded model in use cases).

### Infrastructure (`SocialStudio.Infrastructure`)
- EF Core entities + **migrations** for: Content Plan (with `Period` mapped — likely retaining year/month columns for the monthly variant plus a period-kind discriminator for forward-compat), General Strategy, calendar slots, and embedded `Provenance` columns/owned types.
- Migrate data: the `social.client_strategies` table is a **rename/restructure** (its 1:1-per-client data carries over directly); the heavier work is restructuring `social.client_monthly_objectives` into the Content Plan's Objective node. Preserve the existing `client_id+year+month` uniqueness as the `client+Period` uniqueness for the monthly variant.
- Update `EfClientBrandSnapshotLoader` to read the persisted Content Plan.
- Provider/persistence types stay in Infrastructure (hexagonal boundary).

### Api (`SocialStudio.ApiService`)
- Replace `MapClientMonthlyObjectivesEndpoints` with Content Plan endpoints keyed by `client+Period`.
- Add endpoints to read/edit General Strategy and to read/edit the Content Strategy + Calendar nodes, surfacing `Provenance` per node.

### Frontend (`apps/web`)
- Surface the Content Plan as the per-period editing surface: Objective, Content Strategy (manual + AI-fillable), and Content Calendar (ordered slots, format per slot).
- Surface enduring **Brand Brain** + **General Strategy** at client level.
- Show `Provenance` per node (manual vs AI, when generated) as an audit badge, not a blocking warning.

---

## Impact by Layer (summary)

| Layer | Impact | Notes |
|---|---|---|
| Domain | New: `Period` VO, `Provenance` VO, Content Plan aggregate, General Strategy; rename BrandManual↔BrandBrain | No provider types; pure domain |
| Application | Rename objective slices → Content Plan use cases; new ports; loader contract change | Model stays abstract |
| Infrastructure | New entities + **migrations** + data migration; loader rewrite | Provider isolation preserved |
| Api | Endpoint rename + new strategy/calendar/provenance endpoints | — |
| Frontend | Content Plan editing surface + provenance badges | — |
| Docs/Specs | Update `openspec/specs/*` + `docs/` per docs-sync rule | Mandatory in same change |
| ADR | **Required** — this is an architecture change (`require_adr_for_architecture_changes: true`) | Add ADR under `docs/architecture/adr/` |

---

## Non-Goals (explicit)

1. **The AI generation workflow itself.** Producing the Content Strategy / Calendar content (the orchestration, provider calls, progress tracking) belongs with/after `generate-social-content-workflow`. This change defines the *shape and persistence* of those artifacts and makes Content Strategy a **manually-editable + AI-fillable** structure — it does not implement the generator.
2. **Any change to Generated Content Package internals.** The package already has durable per-asset storage and its own lifecycle. This change only adds *references* from the Content Plan to packages. No retro-editing, no re-snapshotting of historical packages.
3. **Automatic re-derivation / drift propagation.** When upstream inputs (e.g. General Strategy) change, the Plan is NOT silently re-curated. The user redefines and regenerates; the Plan is upserted. `Provenance` is an audit trace, not a blocking drift flag. (Per closed decision #467.)
4. **Versioning/history of the Content Plan.** The Plan is a regenerable working surface (overwrite-on-upsert, like today's `ClientMonthlyObjective`). History lives in the immutable Generated Content Packages, not in Plan versions.
5. **Non-monthly cadences as a working feature.** The `Period` VO is *designed* not to lock to monthly, but only the monthly variant is implemented now. Other cadences are future work enabled by the VO.

---

## Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **Data migration** restructuring `social.client_monthly_objectives` into the Content Plan (the `social.client_strategies` table is a lower-risk rename) | High | Plan migration carefully; preserve `client_id+year+month` uniqueness as monthly `Period` key; one EF migration per structural step; verify with gates before complete |
| `EfClientBrandSnapshotLoader` rewrite while `generate-social-content-workflow` depends on `BrandSnapshot` | High | Keep `BrandSnapshot` output contract stable; change only its *source* (persisted Plan vs ad-hoc). Coordinate sequencing with the generation change |
| Over-modeling `Period`/calendar before real multi-cadence need (YAGNI) | Medium | Implement only monthly `Period` + slot list now; keep VO open but minimal |
| Brand Brain vs BrandManual naming churn across layers/UI | Medium | Pick ONE canonical domain name; "Brand Brain" stays a UI label only; document mapping in ADR |
| Scope creep into generation orchestration | Medium | Non-Goals #1/#2 are hard boundaries; specs must not describe the generator |
| Provenance as owned type adds EF mapping complexity per node | Low | Use EF owned types; keep `Provenance` small and immutable |
| ADR + PRD traceability required by config | Low | Author ADR and link PRD/SDD sections in spec phase |

---

## Open Questions

1. **Canonical name** for the reconciled Brand Brain / BrandManual concept in the domain — keep `ClientBrandManual` (less churn) or rename to a Brand-Brain-aligned name (better product alignment)? Recommend keeping `BrandManual` and mapping the UI label.
2. **General Strategy persistence shape**: keep the existing `ClientStrategy` as a standalone aggregate (renamed) vs. fold it into an owned value object on Client. (The persisted table `social.client_strategies` already exists either way — this is about aggregate shape, not whether it exists.) Decide in design.
3. **Period mapping in Postgres**: keep `year`/`month` int columns + a `period_kind` discriminator (forward-compat) vs. a single canonical period key column. Decide in design.
4. **Calendar slot identity & traceability**: exact slot → generated-asset linkage shape (does the slot store the produced package/asset id, or only the package?). Refine in spec/design.
5. **Status enum** on `Provenance` (`Draft | Generated | Edited | ...`) — define the allowed values and transitions in spec.

---

## Traceability
- Upstream context for: `openspec/changes/generate-social-content-workflow` (this Plan is the input surface that change consumes).
- Closed design decisions: engram `architecture/client-content-plan-model` (obs #467 — Plan regenerable / Package immutable; staleness resolution).
- PRD/SDD traceability and ADR to be linked in the spec and design phases (`require_prd_traceability: true`, `require_adr_for_architecture_changes: true`).
