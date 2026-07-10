# Persistence Architecture

## Principles
- Hexagonal boundaries preserved:
  - Domain/Application do not depend on Supabase SDKs.
  - Infrastructure implements persistence adapters.
- EF Core is the ORM adapter.
- PostgreSQL/Supabase is the current concrete provider.

## Runtime Setup
- Shared context: `AppIdentityDbContext`
  - ASP.NET Identity tables under schema `identity`
  - Platform tables under schema `platform`
    - `platform.organizations`
    - `platform.organization_memberships`
  - `platform.system_settings`
  - `platform.user_preferences`
- Unit of Work: `EfUnitOfWork`
- Repositories:
  - `EfTenantRepository`
  - `EfTenantMembershipService`
  - `EfSystemSettingRepository`
  - `EfUserPreferenceRepository`

## Configuration
- `Persistence:Provider = Postgres`
- `Persistence:ConnectionStringName = Postgres`
- Connection string is read from `ConnectionStrings:Postgres`

## Migrations
- Location: `Incoders.Hive.Infrastructure/Persistence/Postgres/Migrations`
- Design-time factory: `AppIdentityDbContextFactory`

## PostgreSQL Naming
- Database identifiers use lower `snake_case` for Supabase/PostgreSQL compatibility.
- C# entities and properties keep .NET naming conventions; EF Core maps them to relational names.
- Client-owned Social child tables use the `client_*` association prefix, for example `social.client_strategies` and `social.client_monthly_objectives`.
- ASP.NET Identity defaults such as `AspNetUsers` are not part of the database contract.
- Identity tables live in the application-owned `identity` schema:
  - `identity.users`
  - `identity.roles`
  - `identity.user_claims`
  - `identity.user_logins`
  - `identity.user_roles`
  - `identity.user_tokens`
  - `identity.role_claims`
- Do not use Supabase's `auth` schema for custom ASP.NET Identity tables.
- EF Core migration metadata uses `__ef_migrations_history`.

## Schema Ownership
- `identity` is authentication/authorization persistence.
- `platform` is cross-module platform persistence: organizations, memberships, system settings, provider catalogs, model catalogs and shared configuration.
- `social` is reserved for the Hive Social / Incoders Social product module.
- Future product modules SHOULD get one schema per module, not one schema per small bounded context.
- First Hive Social table: `social.clients`, scoped by `organization_id` and referenced to `platform.organizations`.

## Organization Boundary
- Product-facing language is Organization; database ownership columns are named `organization_id`.
- Current C# tenancy abstractions may still use `Tenant` internally, but their relational contract maps to `platform.organizations` and `organization_id`.
- Organization-scoped users can have only one row in `platform.organization_memberships`; platform sysadmins do not have organization memberships.
- User module assignments must reference the active membership, so removing a membership cascades stale module assignments.
- Organization-owned module tables MUST include `organization_id` unless the table is explicitly global/platform-owned.
- Platform catalogs such as AI providers/models are global/system-owned; organization-specific credentials, availability and preferences are organization-scoped records that reference those catalogs.
- User-level UI preferences (for example `theme_key`) are stored in `platform.user_preferences` and scoped by `user_id` (not tenant-global).

## Explicit Non-Goals (this iteration)
- No Redis/cache provider additions
- No direct SQL script workflow when EF migrations can model schema changes

## Migration Delivery Pipeline (issue #57)

### Local development database
- `docker-compose.dev.yml` runs a disposable, plain `postgres:16` container (no Supabase
  image/extensions — none are used). Exposed on host port `5433` to avoid clashing with any
  locally-installed Postgres.
- Start it with `docker compose -f docker-compose.dev.yml up -d`; stop with `down` (keeps the
  named volume) or `down -v` (wipes data).
- `Incoders.Hive.ApiService/appsettings.Development.json` points `ConnectionStrings:Postgres`
  at that local container. `dotnet run`/F5 in `Development` auto-applies pending migrations and
  runs `SeedData` against the local DB only — it never touches the shared cloud database.
- Optional, nice-to-have: `scripts/db/dump-restore.ps1` can `pg_dump` the cloud `identity` /
  `platform` / `social` schemas and `pg_restore` them into the local container for
  realistic-data testing (not required for day-to-day dev).

### CI: migration-drift gate (Slice 1, secret-free)
- `scripts/gates/checks/Check-MigrationDrift.ps1`, registered in `scripts/gates/Run-Gates.ps1`
  and configured in `project.gates.yml` (`gates.migration-drift`, level `SHOULD` initially).
- Detects when the EF Core model diverges from the last committed migration using
  `dotnet ef migrations has-pending-model-changes` (falls back to a scratch
  `migrations add`-then-diff if that verb is ever unavailable). No DB connection or secret is
  used — the design-time factory only needs a syntactically-valid connection string to build
  `DbContextOptions`, never dials it.
- Run locally: `pwsh scripts/gates/Run-Gates.ps1 -Only migration-drift`.
- See `.github/incoders-ai/gates/migration-drift.gate.md` for the full gate definition.

### CI: ephemeral-verify job (Slice 1, secret-free)
- `.github/workflows/quality-gates.yml` job `ephemeral-verify` runs alongside the reusable gate
  runner (it needs a `services:` Postgres container, so it cannot live inside the reusable
  workflow).
- Applies the entire migration chain to a fresh `postgres:16` service container, publishes
  `dotnet ef migrations script --idempotent` as the `migrate.sql` build artifact (the reviewable
  PR artifact), then performs an `Up → Down → Up` round-trip against the previous migration for
  rollback confidence.
- `Down()` across the migration history is not audited or guaranteed as a production rollback
  path — the round-trip only builds confidence that the immediately-previous step reverts
  cleanly. Real rollback safety comes from the pre-apply backup in the apply-on-merge job
  (Slice 2).

### Apply-on-merge to the cloud DB (Slice 2 — not yet shipped)
Documented separately once `.github/workflows/db-apply.yml` ships. It is blocked on Docker/VT-x
being available locally for end-to-end dry-runs and on a user-provisioned `dev` GitHub
Environment + `POSTGRES_CONNECTION_STRING` secret. Local dev stays on `StartupTasks:ApplyMigrations`
defaulting to `true` only in `Development`; non-dev environments must set
`StartupTasks__ApplyMigrations=false` explicitly once they point at a shared/cloud database.
