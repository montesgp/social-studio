# Project Database

Persistence provider: **Supabase Postgres** (managed PostgreSQL). Authentication: **ASP.NET Identity** (own users/roles/claims in the application-owned `identity` schema). The backend is the single source of truth for login, registration, refresh, password reset, and email confirmation. Design multi-tenant schema around Organization, Client and SocialAccount. RLS must protect tenant boundaries when accessed from non-trusted contexts.

## Authentication

- Authentication is owned by the backend (`ASP.NET Core Identity` + `UserManager<ApplicationUser>` + own JWT issuance via `JwtTokenService`).
- The backend issues and validates its own JWTs. Issuer / audience / signing key live under the `Authentication:Jwt` configuration section (production secret in user-secrets or environment, never in `appsettings.json`).
- Supabase Auth (the `auth` schema and the GoTrue service) is NOT used. The reserved `auth` schema MUST NOT be referenced from application code.
- The frontend MUST NOT call Supabase Auth or any other Supabase API directly. All authentication flows (`/api/auth/register`, `/login`, `/refresh`, `/forgot-password`, `/reset-password`, `/confirm-email`, sign-out) go through the backend API.
- Supabase Postgres is reachable from the backend only, via EF Core / Npgsql, as a standard PostgreSQL backing store.

## Roles

- `sysadmin` — platform-wide visibility and administration. Manages AI providers/models, publication providers, system catalogs, sees ALL Organizations and their data.
- `admin` — Organization-scoped administrator. Manages clients, social accounts, members, integrations and configuration belonging to their own Organization. Cannot see other Organizations.
- Roles are stored in `identity.roles` and assigned via `identity.user_roles`.
- A user MAY hold multiple roles. `sysadmin` strictly extends what `admin` can do.

## Naming

- PostgreSQL identifiers use lower `snake_case`.
- C# code keeps idiomatic .NET naming; EF Core maps tables, columns, indexes and constraints to `snake_case`.
- ASP.NET Identity tables are named by domain purpose (`identity.users`, `identity.roles`, `identity.user_claims`, `identity.user_logins`, `identity.user_roles`, `identity.user_tokens`, `identity.role_claims`), not the default `AspNet*` names.
- EF Core migration history is stored in `__ef_migrations_history`.

## Schemas

- `identity` owns ASP.NET Identity persistence (users, roles, claims, logins, role_claims, user_tokens, refresh_tokens when persisted).
- `platform` owns cross-module platform data: organizations, memberships, system settings, provider/model catalogs and shared configuration. Membership rows reference `identity.users(id)`.
- Product modules use their own schemas, starting with `social` for Incoders Social.
- `auth` is Supabase-reserved and not used by this project.
- Do not use dashed schema names; use lower `snake_case` identifiers.

## Multi-Tenancy

- The product language is Organization. Tenant remains an internal implementation term only where already present.
- Organization-owned module data MUST include an `organization_id` boundary column unless it is explicitly global/system-owned.
- Client, campaign, social account, workflow, reference content, idea, publication and metrics data belong to an organization.
- System catalogs such as AI providers, AI models, publication providers, prompt definitions and provider capabilities are platform-level by default.
- Organization-specific credentials, overrides, enabled/disabled provider selections and prompt/model preferences must be stored as organization-scoped records referencing the platform catalogs.
- Authorization checks: `sysadmin` bypasses Organization filters; `admin` is scoped to their Organization membership.
