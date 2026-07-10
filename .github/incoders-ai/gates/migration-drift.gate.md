# Migration Drift Gate

## Purpose
Fail (or warn) a PR when the EF Core model diverges from the last committed migration, so schema drift is caught before merge instead of at the shared cloud database (issue #57).

## Scope
Applies to `Incoders.Hive.Infrastructure` (`AppIdentityDbContext` + `Persistence/Postgres/Migrations`), driven via the `Incoders.Hive.ApiService` design-time factory (`AppIdentityDbContextFactory`).

## Load Conditions
Every CI run through `scripts/gates/Run-Gates.ps1`; runnable locally with `pwsh scripts/gates/Run-Gates.ps1 -Only migration-drift`. Requires only the pinned `dotnet-ef` tool (`dotnet tool restore`) — no Docker, no database.

## Do Not Load When
The repo has no EF Core migrations project, or `dotnet` SDK is unavailable (the gate self-skips in that case).

## Rules

### Drift Detection (default level: **SHOULD**, warn-only initially)
- Primary check: `dotnet ef migrations has-pending-model-changes`. Non-zero exit ⇒ the model has changes not captured by a migration.
- Fallback (only if the primary verb is unavailable/misbehaves): scratch `dotnet ef migrations add __DriftCheck`; a non-empty generated `Up()` body ⇒ drift. The scratch migration and any snapshot-file mutation are always discarded, regardless of outcome.
- Promote to **MUST** once the verb is confirmed stable across a few PRs (see `project.gates.yml -> gates.migration-drift.level`).

### No DB Access Required (default level: **MUST**, mechanically enforced)
- The gate MUST run with zero database connection strings or CI secrets. `ConnectionStrings:Postgres` is only ever a local placeholder value used to satisfy the design-time factory's config binding — it is never dialed.

### Gate Registration (default level: **MUST**, mechanically enforced)
- The check script MUST be registered in the ordered `$gateScripts` map in `scripts/gates/Run-Gates.ps1` — there is no auto-discovery. A check script alone never runs without this line.

## Output Expectations
- `[ SHOULD ] migration-drift: pass|warn|fail — <message>`.
- `skipped` when `dotnet` is unavailable or the Infrastructure/ApiService projects are missing.

## Future Enhancements (Phase 2 / Phase 3)
- Promote SHOULD → MUST once the primary verb has a clean multi-PR baseline.
- Surface the specific entity/property causing drift in the gate message (today the raw `dotnet ef` output is attached as details).
