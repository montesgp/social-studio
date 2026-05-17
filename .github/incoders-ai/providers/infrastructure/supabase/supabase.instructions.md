# Supabase Provider Instructions

## Scope
Supabase database, auth, RLS, storage and MCP integration.

## Rules
- Treat Supabase as an infrastructure provider behind application ports.
- Keep RLS policies aligned with multi-tenant boundaries.
- Prefer migrations and typed contracts over ad-hoc schema changes.
- Use PostgreSQL/Supabase-friendly identifiers in database artifacts: lower `snake_case` for schemas, tables, columns, indexes, constraints, functions and RLS policies.
- For .NET/EF Core integrations, keep C# naming idiomatic (`PascalCase` types/properties) and map persistence identifiers explicitly to `snake_case`; do not let framework defaults such as `AspNetUsers` leak into the database contract.
- When ASP.NET Identity is used with Supabase PostgreSQL, isolate those tables in an application-owned schema such as `identity`; do not use Supabase's reserved/internal `auth` schema for custom tables.
- Prefer names such as `identity.users`, `identity.roles`, `identity.user_claims`, `identity.user_logins`, `identity.user_roles`, `identity.user_tokens` and `identity.role_claims` over default `AspNet*` table names.
- Configure EF Core migration history to a lower-case table name such as `__ef_migrations_history` so infrastructure tables do not introduce quoted mixed-case identifiers.
