# Architecture Gate

## Purpose
Enforce hexagonal/Clean Architecture boundaries declared in `core/architecture-boundaries.rules.md` and the project manifest. Prevent provider-specific code (EF Core, Supabase, Npgsql, ASP.NET) from leaking into Domain/Application layers.

## Scope
Applies to:
- All `.csproj` files under the projects declared in `project.gates.yml -> backend.architecture.*`.
- All `.cs` files under those projects.

## Load Conditions
Every CI run; every local change that touches `*.cs` or `*.csproj`.

## Do Not Load When
Pure frontend changes (frontend layering rules live elsewhere — Phase 2).

## Rules

### Layer Reference Rules (default level: **MUST**)
Project references (`<ProjectReference>` in `.csproj`) MUST follow the inward-pointing rule:

| Layer | May reference |
|---|---|
| Domain | (none) |
| Application | Domain |
| Infrastructure | Application, Domain |
| Api | Application, Infrastructure, Domain, ServiceDefaults |
| Tests | Any (allowed by definition) |

Any other project reference is a violation.

### Forbidden Namespace Imports (default level: **MUST**)
For each layer, `project.gates.yml -> backend.architecture.forbidden_namespace_imports[]` lists namespace prefixes that MUST NOT appear in `using` directives.

Default policy:
- **Domain**: MUST NOT import `Microsoft.EntityFrameworkCore*`, `Microsoft.AspNetCore*`, `Npgsql*`, `Supabase*`, `Microsoft.Extensions.DependencyInjection*` (except `Abstractions`).
- **Application**: MUST NOT import `Microsoft.EntityFrameworkCore*`, `Microsoft.AspNetCore.Http*`, `Npgsql*`, `Supabase*`.

### Provider Leakage (default level: **MUST**)
`Supabase`, `Npgsql`, and EF Core types MUST live only in `Infrastructure` (and migrations).
Any reference outside Infrastructure/Api MUST be flagged.

## Output Expectations
- `[ MUST ] architecture.layering: pass|fail — <violations> violations`.
- `[ MUST ] architecture.imports: pass|fail — <violations> forbidden imports`.
- Each violation MUST be reported as `<file>:<line>: <namespace> in <layer>`.
- Non-zero exit code on any MUST violation.

## Future Enhancements (Phase 2)
- Architecture fitness functions via NetArchTest or ArchUnitNET.
- Frontend layer rules (e.g., `app/` MUST NOT import directly from `lib/infra/*`).
- Module boundaries (bounded contexts).
