# Tasks: DB Migration Delivery Pipeline (issue #57)

Delivery strategy: `ask-on-risk`. Chain strategy: TBD if chaining is needed (see Review Workload
Forecast). Work units follow `work-unit-commits` — each unit has a clear start/finish/verify
boundary and produces one focused commit.

Legend: `[P]` = can run in parallel with sibling `[P]` tasks in the same work unit. Tasks without
`[P]` are sequential within their unit. Work units themselves are ordered top to bottom unless
marked otherwise.

---

## Slice 1 — Secret-free (ship first)

### WU1.1 — Local Docker Postgres dev DB
**Satisfies:** `local-dev-database` spec — Requirement: Disposable Local Postgres.

- [x] Create `docker-compose.dev.yml`: plain `postgres:16` service, no Supabase image/extensions,
      exposed on a local port, named volume for persistence across restarts.
- [x] Document the exact `ConnectionStrings:Postgres` value the compose service exposes (host,
      port, db name, user/pass) for local `appsettings.Development.json` wiring.
- **Verify:** `docker compose -f docker-compose.dev.yml up -d` starts a reachable Postgres 16
  instance (`psql`/`pg_isready`). Requires Docker/VT-x locally — see blocked-until note below.
- **Depends on:** nothing.

### WU1.2 — Local dev auto-migrate + seed wiring
**Satisfies:** `local-dev-database` spec — Requirement: Local Auto-Migrate and Seed;
`startup-tasks` spec — Requirement: Startup Migration Application (local-dev scenario).

- [x] Modify `SocialStudio.ApiService/appsettings.Development.json`: point
      `ConnectionStrings:Postgres` at the local Docker Postgres from WU1.1.
- [x] Modify `SocialStudio.ApiService/Program.cs` (~line 93): keep
      `StartupTasks:ApplyMigrations` defaulting to `true` only for local dev (against the local
      DB); confirm the effective default for any other environment is `false` (paired with
      WU-Program-nonDev below — same file, do together to avoid two edits to the same lines).
- [x] Add explicit `StartupTasks__ApplyMigrations=false` note/config for any non-local-dev
      environment, per design's "Set explicitly to prevent double-apply" guidance.
- **Verify:** run app locally against empty local Docker DB → all migrations apply, `SeedData`
  bootstraps sysadmin/roles/catalogs (per `local-dev-database` Scenario: First run against empty
  local DB). Blocked on Docker being available locally (see blocked-until note).
- **Depends on:** WU1.1.

### WU1.3 — Migration drift gate check script
**Satisfies:** `migration-drift-gate` spec — Requirement: Drift Detection on PR; Requirement: No
DB Access Required.

- [x] Create `scripts/gates/checks/Check-MigrationDrift.ps1` mirroring existing
      `scripts/gates/checks/*.ps1` pattern (see `Check-Architecture.ps1`/`Check-Secrets.ps1` for
      shape): `dotnet tool restore` → run `dotnet ef migrations has-pending-model-changes` →
      non-zero exit ⇒ drift found.
- [x] Implement the scratch-add fallback: `dotnet ef migrations add __DriftCheck` to a temp
      output dir, fail if generated migration body is non-empty, always discard the scratch
      migration file regardless of outcome.
- [x] Emit result via `New-GateResult` (existing helper convention) at the level read from
      `project.gates.yml` (not hardcoded), so the SHOULD→MUST promotion later is a config change
      only.
- [x] No DB connection string or secret referenced anywhere in the script.
- **Verify:** local `pwsh scripts/gates/Run-Gates.ps1 -Only migration-drift` — with no model
  changes it passes (Scenario: No model changes); a scratch local model edit without a matching
  migration makes it fail/warn per configured level (Scenario: Model changed without a
  migration); with CI env vars unset it still runs to completion (Scenario: Gate runs with zero
  DB credentials configured). This does NOT require Docker.
- **Depends on:** nothing (independent of WU1.1/1.2).

### WU1.4 — Register drift gate (CRITICAL: hardcoded map)
**Satisfies:** `migration-drift-gate` spec — Requirement: Gate Registration.

- [x] Modify `scripts/gates/Run-Gates.ps1`: add
      `'migration-drift' = 'checks\Check-MigrationDrift.ps1'` to the ordered `$gateScripts` map
      (currently lines ~63-72). **This is the CRITICAL step from design — there is no
      auto-discovery; a check file alone will never run without this registration.**
- [x] Modify `project.gates.yml`: add `migration-drift: { level: SHOULD }` under `gates:`,
      matching the `vulnerabilities`/`coverage` SHOULD-first pattern already in the file (lines
      82-88), with a comment explaining warn-first → promote-to-MUST-later.
- [x] Modify `.github/incoders-ai/gates/README.md`: add a Gate Index row for `migration-drift`
      (level, doc link), matching existing rows.
- [x] Create `.github/incoders-ai/gates/migration-drift.gate.md`: gate definition doc following
      the existing `*.gate.md` template shape used by sibling gates.
- **Verify:** `pwsh scripts/gates/Run-Gates.ps1` (full run, no `-Only`) includes `migration-drift`
  in its executed gate list and reports a SHOULD-level result; README + gate doc are
  cross-linked and consistent with sibling gates (Scenario: Gate is discoverable).
- **Depends on:** WU1.3 (script must exist before it can be registered/run).

### WU1.5 — Ephemeral-verify CI job
**Satisfies:** `migration-delivery` spec — Requirement: Idempotent Script Artifact; Requirement:
Ephemeral Verification.

- [x] Modify `.github/workflows/quality-gates.yml`: add a new sibling job (not inside the
      reusable gate runner, because it needs a `services:` container) with a health-checked
      `postgres:16` service and `ConnectionStrings__Postgres` env pointing at it.
- [x] Steps: `dotnet tool restore` → `dotnet ef database update` (apply full chain from empty) →
      `dotnet ef migrations script --idempotent -o migrate.sql` → upload `migrate.sql` as a build
      artifact → round-trip: `dotnet ef database update <PreviousMigration>` (Down) →
      `dotnet ef database update` (Up again).
- [x] Job fails the workflow (and thus blocks required-check PRs) if any step in the chain
      errors.
- **Verify:** CI run on a PR (or `workflow_dispatch`) shows the job green, `migrate.sql` artifact
  downloadable and non-empty (Scenario: Script applies cleanly); inject a deliberately broken
  migration on a scratch branch to confirm the job goes red (Scenario: Script fails to apply).
  Verified via CI dispatch, not local `dotnet test` (SAC-blocked locally).
- **Depends on:** WU1.3/WU1.4 conceptually independent but should land in the same PR as the
  drift gate for a coherent Slice 1 review; no hard code dependency.

### WU1.6 — Docs: local DB + Slice 1 pipeline
**Satisfies:** cross-cutting documentation for `local-dev-database` + `migration-drift-gate` +
`migration-delivery` (Slice 1 portion).

- [x] Modify `docs/architecture/persistence.md`: document `docker-compose.dev.yml` usage, the
      drift gate, and the ephemeral-verify job; note the Slice 2 apply job is documented
      separately once shipped.
- [ ] (Optional, nice-to-have per design — not a Slice 1 prerequisite; deferred, not implemented
      this batch) Create `scripts/db/dump-restore.ps1`: `pg_dump` cloud schemas (`identity`,
      `platform`, `social`) → `pg_restore` into the local Docker DB, for realistic-data local
      testing.
- **Verify:** doc reviewed for accuracy against the actually-shipped compose file and workflow
  job names.
- **Depends on:** WU1.1, WU1.5 (needs final file/job names to document correctly).

---

## Slice 2 — Secret-bearing (apply-on-merge)

**BLOCKED UNTIL:**
(a) Docker/VT-x enabled locally — needed to test the backup/apply flow end-to-end against a
    throwaway local target before trusting it against the shared cloud DB, and
(b) the `dev` GitHub Environment + `POSTGRES_CONNECTION_STRING` secret are created by the user
    (repo admin action, not agent-executable).

Do not start WU2.x implementation until both are confirmed. Slice 1 ships and is reviewable
independently in the meantime.

### WU2.1 — Non-dev startup no longer auto-migrates
**Satisfies:** `startup-tasks` spec — Requirement: Startup Migration Application (non-dev
scenarios + explicit override scenario).

- [ ] Modify `SocialStudio.ApiService/Program.cs` (~line 93): confirm/enforce
      `StartupTasks:ApplyMigrations` defaults to `false` in every environment except local dev
      against the local DB, while still honoring an explicit `StartupTasks:ApplyMigrations=true`
      override when set (Scenario: Explicit override still respected).
- [ ] Cross-check this does not regress WU1.2's local-dev default (same file/region — reconcile
      as one coherent change if WU1.2 hasn't landed yet, or as a follow-up diff if it has).
- **Verify:** config review + (if Docker available) a non-dev-configured local run against the
  local DB shows no auto-apply unless explicitly overridden.
- **Depends on:** WU1.2 (same file region).

### WU2.2 — Apply-on-merge workflow with pre-apply backup
**Satisfies:** `migration-delivery` spec — Requirement: Pre-Apply Backup Gate; Requirement:
Automatic Apply on Merge; Requirement: Environment-Scoped Secret, Forward-Compatible.

- [ ] Create `.github/workflows/db-apply.yml`: `on: push: branches: [dev]`; job `apply` with
      `environment: dev` (parameterized, e.g. via `environment: ${{ needs.pick-env.outputs.name
      }}` or a direct `dev` literal today per design's forward-compat note) and
      `ConnectionStrings__Postgres: ${{ secrets.POSTGRES_CONNECTION_STRING }}`.
- [ ] Step order (hard gate via step failure propagation): (1) restore tools, (2) `pg_dump`
      custom-format backup of target schemas (`identity`, `platform`, `social`) uploaded as a CI
      artifact with retention — MUST NOT proceed to apply if this step fails, (3) regenerate the
      idempotent script FRESH (never reuse the PR's `migrate.sql` artifact), (4) apply via
      `dotnet ef database update` against `ConnectionStrings__Postgres`, (5) record the applied
      migration id in the job summary.
- [ ] Pin a `pg_dump` client version compatible with Postgres 16 in the apply runner (per design
      residual risk).
- [ ] No manual approval step in the workflow itself — automatic on merge, per design Decision
      and spec Requirement: Automatic Apply on Merge.
- **Verify:** requires the `dev` Environment + secret to exist (user-provisioned) — CANNOT be
  fully verified until unblocked. Once available: merge a scratch PR to `dev`, confirm backup
  artifact appears before the apply step runs (Scenario: Backup succeeds then apply runs);
  simulate a backup failure (e.g. temporarily invalid schema name) to confirm apply is skipped
  and the job reports failure (Scenario: Backup fails and apply is blocked); confirm the job
  triggers with zero manual clicks (Scenario: Merge triggers apply without manual intervention).
- **Depends on:** WU2.1, the two blocked-until preconditions above.

### WU2.3 — Forward-compat review + docs
**Satisfies:** `migration-delivery` spec — Requirement: Environment-Scoped Secret,
Forward-Compatible (review scenario); cross-cutting docs completion.

- [ ] Review `db-apply.yml`: confirm the apply step references `environment` + the same secret
      name with no branch-specific or environment-specific conditional logic baked into the
      steps themselves (Scenario: Adding a future tier requires no workflow change).
- [ ] Modify `docs/architecture/persistence.md`: document the apply-on-merge job, backup/restore
      procedure (`pg_restore` from the CI artifact), and the tier-addition procedure (new
      Environment + secret, no code change).
- **Verify:** doc + workflow reviewed together; a hypothetical "add staging" walkthrough in the
  doc requires no code diff, only environment/secret provisioning steps.
- **Depends on:** WU2.2.

---

## Review Workload Forecast

| Metric | Estimate |
|---|---|
| Files touched, Slice 1 | ~9 (docker-compose.dev.yml, Check-MigrationDrift.ps1, Run-Gates.ps1, project.gates.yml, gates README, migration-drift.gate.md, quality-gates.yml, Program.cs, appsettings.Development.json, persistence.md) |
| Files touched, Slice 2 | ~3 (Program.cs, db-apply.yml, persistence.md) |
| Estimated changed lines, Slice 1 | ~350-450 (new PS1 check ~80-120 lines, new CI job YAML ~40-60 lines, compose file ~20-30 lines, gate registration/config/docs ~80-120 lines, small Program.cs/appsettings diffs) |
| Estimated changed lines, Slice 2 | ~120-180 (new workflow YAML ~80-120 lines, Program.cs diff, doc updates) |
| 400-line budget risk | **Medium-High for Slice 1** if delivered as one PR (new script + new CI job + gate registration + docs cluster near the 400-line line); **Low for Slice 2** standalone |
| Chained PRs recommended | **Yes, recommended split**: PR1 = drift gate only (WU1.3, WU1.4) — smallest, secret-free, independently reviewable; PR2 = local DB + ephemeral verify + docs (WU1.1, WU1.2, WU1.5, WU1.6); PR3 (blocked) = Slice 2 apply pipeline (WU2.1-2.3), opened only after both blockers clear |
| Decision needed before apply | **Yes** — confirm PR-splitting/chain strategy for Slice 1 (drift-gate-first vs combined) before `sdd-apply` starts, per `delivery_strategy = ask-on-risk` |

## Cross-Cutting Notes

- `dotnet test` is SAC-blocked locally on this machine — verification for CI-workflow-observable
  items (gate runs, job passes, artifacts present) happens via CI dispatch/PR runs, not local
  xUnit, except where a task explicitly says "local `pwsh Run-Gates.ps1`" (the drift check script
  itself is DB-free and PS1-only, so it runs fine locally).
- Docker/VT-x is a user-owned local prerequisite for WU1.1/WU1.2 local verification and for any
  local dry-run of the Slice 2 flow; it does not block writing or CI-verifying WU1.3-WU1.6, and
  it does not block writing WU2.1-2.3 code (only their full end-to-end verification).
