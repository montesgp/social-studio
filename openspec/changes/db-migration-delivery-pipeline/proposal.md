# Proposal: DB Migration Delivery Pipeline

Tracking issue: #57

## Intent

Today every local `dotnet run` auto-migrates the single shared Supabase cloud DB (`Program.cs:93`, default `ApplyMigrations = IsDevelopment()`), there is no CD workflow, and nothing fails a PR when the EF model drifts from its migrations. Schema changes reach the only real database uncontrolled and unreviewed. This change delivers a controlled, reviewable, forward-compatible EF Core migration pipeline: safe local dev DB, a secret-free PR gate that proves migrations apply, and an automatic apply-on-merge to the cloud DB with a pre-apply backup.

## Scope

### In Scope — Slice 1 (secret-free, ship first)
- Disposable local Docker Postgres (`postgres:16` compose) as the dev DB target; app auto-migrates locally + existing `SeedData` bootstraps.
- CI PR gate: fail PR if model changed without a migration (drift check).
- CI generates `dotnet ef migrations script --idempotent` as a reviewable artifact.
- CI applies that script to an **ephemeral** Postgres service container to prove clean apply, plus an `Up→Down→Up` round-trip for rollback confidence.

### In Scope — Slice 2 (secret-bearing, apply)
- Apply-on-merge to `dev`: on merge, apply the idempotent migration to the cloud DB via a GitHub Environment secret. **Automatic** (no manual gate) — CI ephemeral verification is the safety net.
- **Pre-apply Supabase backup/snapshot** as a required step before any apply.
- Disable startup auto-migrate in non-dev (`StartupTasks:ApplyMigrations=false`); keep it for local dev only.
- Env/secret indirection so per-tier GitHub Environment secrets slot in with config only.

### Out of Scope (Non-goals)
- Supabase branch DBs / real staging+prod tiers now (no Pro plan) — later env/secret config, not a redesign.
- Production app hosting/deployment (undefined in repo; pipeline migrates the DB independent of where the app runs).
- Direct-SQL authoring workflow (EF code-first stays the source of truth).
- Treating EF `Down()` as a rollback guarantee — backup + forward-fix is the real safety net.

## Capabilities

### New Capabilities
- `migration-drift-gate`: CI check failing a PR when the EF model diverges from committed migrations.
- `migration-delivery`: pipeline that verifies migrations on ephemeral DB and applies them to the target cloud DB on merge with pre-apply backup.
- `local-dev-database`: disposable Docker Postgres provisioning for branch-local schema.

### Modified Capabilities
- `startup-tasks`: `ApplyMigrations` disabled outside local dev; pipeline becomes the apply authority.

## Approach

EF Core code-first stays the source of truth (`dotnet-ef 10.0.0` pinned; `AppIdentityDbContextFactory` works standalone in CI via `ConnectionStrings__Postgres`). Slice 1 adds a `Check-MigrationDrift.ps1` gate into the existing `scripts/gates` + `project.gates.yml` pattern and an ephemeral-DB verify job (service container) — zero secrets. Slice 2 adds a merge-triggered apply job scoped to a single GitHub Environment (`dev` today) that snapshots then applies the freshly regenerated idempotent script. Apply invocation stays identical per tier — only the environment + secret value change, so adding staging/prod later is config-only.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `.github/workflows/` (new CD + reusable, `quality-gates.yml`) | New/Modified | Ephemeral-verify job, apply-on-merge job, environment/secret wiring |
| `scripts/gates/checks/Check-MigrationDrift.ps1` + `project.gates.yml` | New/Modified | Drift gate following existing gate pattern |
| `.github/incoders-ai/gates/README.md` + new `*.gate.md` | New/Modified | Gate index + doc |
| `docker-compose.*.yml` + seed/dump script | New | Local disposable Postgres provisioning |
| `SocialStudio.ApiService/Program.cs` (~93) + `appsettings*` | Modified | `ApplyMigrations` local-only |
| `docs/architecture/persistence.md` | Modified | Document the pipeline and non-goals |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Single shared cloud DB — any apply mistake hits the only environment | High | Pre-apply backup + ephemeral CI verify + idempotent script |
| Unaudited `Down()` across 45+ migrations | Med | Do not rely on `Down()`; backup + forward-fix; `Up→Down→Up` adds confidence only |
| Docker/VT-x prerequisite (user-owned) | Med | Design assumes Docker; do not hard-block; enabled same-day |
| Secret handling in CI | Med | GitHub Environment-scoped secret, least-privilege, never in PR context |
| Drift-check CLI verb unverified on `dotnet-ef 10.0.0` | Med | Confirm at design; fall back to scratch-add-then-diff |

## Rollback Plan

Slice 1 is secret-free and additive — remove the workflow/gate to revert. For a bad applied migration: restore the pre-apply Supabase snapshot, then ship a forward-fix migration. EF `Down()` is not a guaranteed rollback path.

## Dependencies

- Docker available locally (BIOS VT-x enabling, user-owned; not blocking design).
- One GitHub Environment + `POSTGRES_CONNECTION_STRING` secret for Slice 2.

## Success Criteria

- [ ] A PR that changes the model without a migration FAILS CI.
- [ ] CI publishes a reviewable idempotent SQL artifact and proves it applies cleanly on an ephemeral Postgres, incl. `Up→Down→Up`.
- [ ] Merge to `dev` snapshots then applies migrations automatically to the cloud DB.
- [ ] Non-dev startup no longer auto-migrates; local dev still does against local Docker Postgres.
- [ ] Adding staging/prod tiers later requires only new environments + secrets, no pipeline redesign.

## Open Questions for Design

1. Ephemeral-DB seeding: apply-all-migrations from empty vs restore a cloud dump?
2. Where/how is the pre-apply Supabase backup taken (Supabase API/CLI vs `pg_dump` to artifact storage)?
3. Secret naming + environment shape for forward-compat (single `dev` env now vs repo secret).
4. Exact `dotnet-ef 10.0.0` drift-check verb (`has-pending-model-changes`) vs scratch-add-diff fallback.
5. Drift gate initial level: MUST vs SHOULD (warn-first, promote later).
