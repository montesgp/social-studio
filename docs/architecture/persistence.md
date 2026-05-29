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
