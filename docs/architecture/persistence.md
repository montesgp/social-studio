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
- `docker-compose.dev.yml` runs a disposable, plain `postgres:17` container (matches the
  Supabase prod major version for parity; no Supabase image/extensions — none are used). Exposed
  on host port `5433` to avoid clashing with any locally-installed Postgres.
- **Turnkey start:** `pwsh -File scripts/dev/Start-LocalDb.ps1` brings the container up, waits
  for health, and runs the safety check below. (`-Recreate` wipes the volume for a truly empty
  start.) Or manually: `docker compose -f docker-compose.dev.yml up -d` (`down` keeps the named
  volume, `down -v` wipes data).
- `Incoders.Hive.ApiService/appsettings.Development.json` points `ConnectionStrings:Postgres`
  at that local container. `dotnet run`/F5 in `Development` auto-applies pending migrations from
  empty and runs `SeedData` against the local DB only — it never touches the shared cloud database.

#### Connection-string source per environment
`ConnectionStrings:Postgres` is resolved by standard .NET config precedence. Keep each tier's
value in its own source so production is never tied to a dev value:

| Environment | Source | Committed? |
|-------------|--------|-----------|
| Local dev | `appsettings.Development.json` (local Docker) or root `.env.local` → env var | value is local-only/gitignored |
| CI verify/apply | GitHub Environment secret → `ConnectionStrings__Postgres` env var | no |
| Production runtime | host secret store → `ConnectionStrings__Postgres` env var | no |

- **Never put the shared/cloud DB connection in `dotnet user-secrets`.** user-secrets override
  `appsettings.Development.json` and are invisible, so a cloud value there makes "local" runs
  silently hit that database (issue #62). Reserve user-secrets for personal API keys only.
  `Start-LocalDb.ps1` refuses to continue if it finds a non-local Postgres user-secret.
- user-secrets and `appsettings.Development.json` load **only** in `Development`, so they can
  never leak into production.

#### Reproducing a bug with the same code + same data
1. `git checkout` the branch of the tier you want to reproduce (e.g. `main` for prod).
2. `pwsh -File scripts/dev/Start-LocalDb.ps1` then boot the API once — the schema is rebuilt from
   that branch's migrations (this is why migrations must be replayable from empty).
3. `pwsh -File scripts/db/Sync-LocalDbFromTier.ps1 -SourceConnection "postgresql://…"` loads that
   tier's data (schemas `identity`/`platform`/`social`) into the local DB, keeping the branch's
   schema. **This pulls real/possibly-PII data and uses egress on the source — a deliberate,
   occasional debugging action, not routine dev.**

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
- Applies the entire migration chain to a fresh `postgres:17` service container, publishes
  `dotnet ef migrations script --idempotent` as the `migrate.sql` build artifact (the reviewable
  PR artifact), then performs an `Up → Down → Up` round-trip against the previous migration for
  rollback confidence.
- `Down()` across the migration history is not audited or guaranteed as a production rollback
  path — the round-trip only builds confidence that the immediately-previous step reverts
  cleanly. Real rollback safety comes from the pre-apply backup in the apply-on-merge job
  (Slice 2).

### Apply-on-merge to the cloud DB (Slice 2 — not yet shipped)
Documented in full once `.github/workflows/db-apply.yml` ships. Design decisions to date:
- **Apply runs only on merge to `main`** (→ Supabase `main`/prod), via the `production` GitHub
  Environment holding `POSTGRES_CONNECTION_STRING`. Merges to `dev` are verified (ephemeral,
  secret-free) but never applied to a real DB, until a Supabase `dev`/`staging` branch exists.
- Docker/VT-x is enabled and the `production` Environment + secret are provisioned.
- Local dev keeps `StartupTasks:ApplyMigrations` defaulting to `true` only in `Development`;
  non-dev environments must set `StartupTasks__ApplyMigrations=false` explicitly once they point
  at a shared/cloud database, so the pipeline is the sole apply authority there.
