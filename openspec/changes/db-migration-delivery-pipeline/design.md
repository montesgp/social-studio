# Design: DB Migration Delivery Pipeline (issue #57)

## Technical Approach

EF Core code-first stays the single source of truth (`dotnet-ef 10.0.0`, pinned in
`dotnet-tools.json`, restorable via `dotnet tool restore`). The design layers three
independently-shippable capabilities onto existing infrastructure with zero redesign of the
current gate/CI machinery:

- **Slice 1 (secret-free):** a `migration-drift` gate mirroring the existing
  `scripts/gates/checks/*.ps1` pattern, plus an ephemeral-Postgres verify job proving the
  idempotent script applies cleanly with an `Up → Down → Up` round-trip. No DB credentials.
- **Slice 2 (secret-bearing):** an apply-on-merge-to-`dev` job scoped to a single GitHub
  Environment that snapshots the cloud DB, then applies a freshly regenerated idempotent
  script. The apply invocation is **parameterized by environment** so staging/prod later is
  config-only (new Environment + secret + trigger condition), never a code change.
- **Local dev safety:** disposable `postgres:16` Docker container becomes the dev DB;
  startup auto-migrate is flipped to local-dev-only so no local run mutates the cloud DB.

The design uses the app's existing standalone design-time factory
(`AppIdentityDbContextFactory`), which self-locates the ApiService project and reads
`ConnectionStrings__Postgres` from env vars — so every EF command works in CI without
user-secrets.

## Resolved Decisions (the 5 Open Questions)

### Decision 1 — Ephemeral-DB seeding: apply-all-migrations from empty

**Choice:** Seed the ephemeral verify DB by applying the full migration chain from an EMPTY
`postgres:16` service container. Do NOT restore a cloud dump.

| Option | Tradeoff | Verdict |
|---|---|---|
| Apply-all-from-empty | No secret, proves the ENTIRE chain replays cleanly, fully deterministic in CI | **Chosen** |
| Restore cloud dump | Closer to prod data shape, but needs cloud DB access (secret) inside the PR gate — breaks the secret-free guarantee and couples the gate to cloud availability | Rejected |

**Rationale:** Slice 1's entire value is a secret-free PR gate. Applying from empty also
proves the property we actually care about — that 45+ migrations replay on a clean DB. Data-
shape realism is a separate, later concern handled by the optional dump/restore path below.

### Decision 2 — Pre-apply backup: Supabase Management API snapshot, `pg_dump` fallback

**Choice:** Take a pre-apply logical backup with `pg_dump` (custom format, target schemas
`identity`, `platform`, `social`) written to a CI artifact with retention, as the reliable,
scriptable, provider-agnostic baseline. Prefer a Supabase Management API / `supabase db dump`
snapshot when a management token is provisioned, but do NOT block the pipeline on it. The
backup step is a hard gate: `apply` MUST NOT run if backup fails.

| Option | Tradeoff | Verdict |
|---|---|---|
| `pg_dump` to CI artifact | Works with only the connection string we already need, fully scriptable, provider-agnostic, restore is standard `pg_restore` | **Chosen (baseline)** |
| Supabase Management API snapshot | Native point-in-time restore, but needs a separate management token/secret and Pro-tier features not guaranteed today | Preferred-when-available |

**Rationale:** The connection secret required for `apply` already grants `pg_dump`. No new
credential, works today, restore path is well-understood. Backup-before-apply ordering with
`needs`/step-failure propagation makes it a true gate.

### Decision 3 — Secret naming + Environment shape

**Choice:** One GitHub Environment named `dev` today, holding secret
`POSTGRES_CONNECTION_STRING`. The apply job maps it to `ConnectionStrings__Postgres` at
runtime. Adding tiers later = new Environments `staging` / `production`, each with the
same-named secret, selected by target branch — no workflow logic change.

| Concern | Decision |
|---|---|
| Secret name | `POSTGRES_CONNECTION_STRING` (Environment secret, not repo secret) |
| Env var mapping | `ConnectionStrings__Postgres: ${{ secrets.POSTGRES_CONNECTION_STRING }}` |
| Environment today | single `dev` (matches the branch that triggers apply) |
| Forward-compat | `environment: ${{ needs.pick-env.outputs.name }}` — branch→env map only |

**Rationale:** Environment-scoped secrets give least-privilege + a native protection/approval
surface. Identical secret name across tiers keeps the apply step byte-for-byte identical, so
the 3-tier upgrade is provisioning, not engineering.

### Decision 4 — Drift verb: `has-pending-model-changes` primary, scratch-add-diff fallback

**Choice:** Primary drift check is
`dotnet ef migrations has-pending-model-changes` (shipped in EF Core 8, present through the
pinned EF Core 10 tooling; exits non-zero when the model has changes not captured by a
migration). Fallback, if the verb ever misbehaves on 10.0.0 in CI: scratch
`dotnet ef migrations add __DriftCheck` in a temp output dir, fail if the generated migration
body is non-empty, always discard.

**Rationale:** The verb is purpose-built, needs no DB, and returns a clean exit code the gate
can consume directly. The scratch-add fallback is deterministic and needs no DB either, so the
gate stays secret-free under both paths. `Check-MigrationDrift.ps1` probes the verb once and
degrades to the fallback automatically.

### Decision 5 — Drift gate level: SHOULD first, promote to MUST

**Choice:** Ship the `migration-drift` gate at **SHOULD** (warn-only) initially, then promote
to **MUST** in a follow-up once the baseline is confirmed green on a few PRs.

**Rationale:** This is the repo's established rollout pattern — `vulnerabilities` and
`coverage` both ship SHOULD "warn-only initially, promote in Phase 2"
(`project.gates.yml:82-88`). Warn-first avoids a false-positive from an unverified verb
blocking every PR on day one. Promotion is a one-line `level:` change.

## Workflow / Job Designs

### Job A — `migration-drift` gate (Slice 1, part of Quality Gates)

Register in three places, mirroring every existing gate:

1. `scripts/gates/checks/Check-MigrationDrift.ps1` — `dotnet tool restore` → run the drift
   verb (or fallback) → emit `New-GateResult` with the gate's configured level.
2. `scripts/gates/Run-Gates.ps1` — add `'migration-drift' = 'checks\Check-MigrationDrift.ps1'`
   to the hardcoded `$gateScripts` ordered map (lines 63-72). **This registration is required;
   the orchestrator does not auto-discover checks.**
3. `project.gates.yml` `gates:` block — `migration-drift: { level: SHOULD }`.
   Plus README Gate Index row + `migration-drift.gate.md` doc.

Runs inside the existing reusable workflow with no changes to the workflow YAML — it flows
through `Run-Gates.ps1` like every other gate. Secret-free, DB-free.

### Job B — ephemeral-verify (Slice 1, new job in the caller workflow)

```
services: postgres:16 (health-checked)
env: ConnectionStrings__Postgres = Host=localhost;...;Database=verify
steps:
  restore tools → ef database update (apply all from empty)
              → ef migrations script --idempotent -o migrate.sql (upload artifact)
              → round-trip: update <PrevMigration> (Down) → update (Up again)
```

Added as a sibling job (not inside the reusable gate runner) because it needs a `services:`
container. Publishes `migrate.sql` as the reviewable PR artifact.

### Job C — apply-on-merge (Slice 2, new `db-apply.yml`)

```
on: push: branches: [dev]        # merge to dev
job apply:
  environment: dev               # ← parameterized; Environment-scoped secret
  env: ConnectionStrings__Postgres = ${{ secrets.POSTGRES_CONNECTION_STRING }}
  steps:
    1. restore tools
    2. BACKUP (gate): pg_dump -Fc target schemas → upload artifact   # fail ⇒ stop
    3. regenerate idempotent script FRESH (never reuse PR artifact)
    4. apply: ef database update   (or psql -f migrate.sql)
    5. record applied migration id in job summary
```

Automatic on merge; the ephemeral-verify job is the pre-merge safety net. Backup precedes
apply via step ordering + `set -e` failure propagation.

## Program.cs / Config Change

`Program.cs:93` currently defaults `StartupTasks:ApplyMigrations` to `IsDevelopment()`. Keep
the config key; change only the effective non-dev behavior and documentation so the pipeline
owns applying migrations everywhere except local dev.

| Key | Local dev | CI apply job | Future non-dev |
|---|---|---|---|
| `StartupTasks:ApplyMigrations` | `true` (local Docker PG) | `false` | `false` |
| `ConnectionStrings__Postgres` | local Docker container | Environment secret | Environment secret |

Set `StartupTasks__ApplyMigrations=false` explicitly in any environment where the pipeline
applies migrations, to prevent double-apply / apply-without-review. `appsettings.Development.json`
points `ConnectionStrings:Postgres` at the local Docker DB so the existing dev auto-migrate is
safe again.

## Local Postgres (docker-compose)

`docker-compose.dev.yml` runs plain `postgres:16` (no Supabase image — no Supabase extensions
are used). Seed path = **apply-from-empty + existing `StartupTasks:SeedData`** (sysadmin,
roles, catalogs auto-seed on startup). Optional `scripts/db/dump-restore.ps1` (`pg_dump` cloud
schemas → `pg_restore` local) is a nice-to-have for realistic-data testing, not a prerequisite.

## Component Change Map

| File | Action | Purpose |
|---|---|---|
| `scripts/gates/checks/Check-MigrationDrift.ps1` | Create | Drift gate check (verb + fallback) |
| `scripts/gates/Run-Gates.ps1` | Modify | Register `migration-drift` in `$gateScripts` |
| `project.gates.yml` | Modify | `migration-drift: { level: SHOULD }` |
| `.github/incoders-ai/gates/README.md` | Modify | Gate Index row |
| `.github/incoders-ai/gates/migration-drift.gate.md` | Create | Gate definition doc |
| `.github/workflows/quality-gates.yml` | Modify | Add ephemeral-verify job (service container) |
| `.github/workflows/db-apply.yml` | Create | Apply-on-merge job, `environment: dev` |
| `docker-compose.dev.yml` | Create | Local `postgres:16` dev DB |
| `scripts/db/dump-restore.ps1` | Create (optional) | Cloud→local realistic seed |
| `SocialStudio.ApiService/Program.cs` | Modify | `ApplyMigrations` local-dev-only (behavior/doc) |
| `SocialStudio.ApiService/appsettings.Development.json` | Modify | Point `Postgres` at local Docker |
| `docs/architecture/persistence.md` | Modify | Document the pipeline + local DB |

## Verification Strategy

Most verification is **CI-observable, not xUnit** (note: `dotnet test` is SAC-blocked locally →
verify via CI dispatch).

| Layer | What | How |
|---|---|---|
| Drift gate | model-without-migration fails/warns | Local `pwsh Run-Gates.ps1 -Only migration-drift`; a scratch model edit must trigger it |
| Ephemeral verify | full chain applies from empty + round-trips | CI job green; `migrate.sql` artifact present |
| Apply job | backup-before-apply ordering | CI dispatch to a throwaway DB; assert backup artifact exists before apply step |
| Local dev | no cloud mutation on `dotnet run` | `appsettings.Development.json` targets local Docker; auto-migrate hits local only |
| Forward-compat | tier addition is config-only | Review: apply step references `environment` + same secret name, no branch-specific logic |

## Residual Risks

- Single shared cloud DB — any bad apply hits the only tier. Mitigated by ephemeral pre-merge
  verify + mandatory pre-apply backup + idempotent script. An optional manual approval on the
  `dev` Environment can gate apply until staging exists.
- `Down()` across 45+ migrations unaudited — do NOT rely on `Down()` for rollback; recovery is
  restore-snapshot + forward-fix.
- Drift verb behavior on 10.0.0 unverified in CI — SHOULD-first + scratch-add fallback contains
  the blast radius.
- Docker/VT-x is user-owned — design assumes Docker available, does not hard-block.
- `pg_dump` client version vs server: pin a client matching Postgres 16 in the apply runner.

## Open Questions

- [ ] Confirm whether to enable a manual approval on the `dev` Environment for apply until a
  staging tier exists (product/ops call, not a design blocker).
