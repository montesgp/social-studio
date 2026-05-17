# Supabase Integration Notes

## Role in Architecture
Supabase is the initial infrastructure provider for PostgreSQL persistence. It remains behind Infrastructure adapters.

## Security Rules
- Never commit real Supabase secrets or JWT values.
- Keep `.mcp.json` local only (gitignored).
- Use `.mcp.example.json` with placeholders.

## Connection
- Application uses PostgreSQL connection string via `ConnectionStrings__Postgres`.
- For Supabase this usually maps to the project database host/user/password.

## MCP Usage
- Project includes `.mcp.example.json` template for Supabase MCP.
- In this bootstrap implementation, schema operations are modeled with EF migrations.
- MCP can still be used locally for inspection/verification flows where available.

## Database Naming
- Supabase/PostgreSQL identifiers use lower `snake_case`.
- EF Core maps C# `PascalCase` types/properties to database `snake_case` names.
- ASP.NET Identity tables use the application-owned `identity` schema and descriptive table names instead of `AspNet*` defaults.
- The Supabase-managed `auth` schema remains reserved for Supabase Auth internals.
- EF Core migration history uses `__ef_migrations_history` to avoid a mixed-case infrastructure table.

## Schema Layout
- `identity`: application-owned ASP.NET Identity tables.
- `platform`: organizations, memberships, system settings and shared system/provider catalogs.
- `social`: Hive Social / Incoders Social module data.
- The first `social` table is `social.clients`, scoped by `organization_id`.

## Multi-Tenancy
- Current first slice stores organizations as tenant aggregate data.
- Database ownership columns use `organization_id`.
- Organization-owned module data must be filtered by organization; RLS policies should use `organization_id` as the tenant boundary when hardened.
- AI provider/model catalogs are platform-level system data. Organization-specific API credentials, provider enablement and model preferences are organization-scoped records that reference platform catalogs.
- RLS strategy for Supabase is a next-stage hardening step after the initial vertical slice.
