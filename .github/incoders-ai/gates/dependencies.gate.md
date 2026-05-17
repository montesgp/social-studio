# Dependencies Gate

## Purpose
Detect invalid or circular project-to-project references in the .NET solution and surface drift early.

## Scope
Applies to all `<ProjectReference>` entries within `*.csproj` files in the configured solution.

## Load Conditions
Every CI run; every local change that touches `*.csproj`.

## Do Not Load When
Documentation-only or app-config-only changes.

## Rules

### Circular Dependencies (default level: **MUST**)
- The project reference graph MUST be acyclic.
- A cycle MUST be reported as a path: `A -> B -> C -> A`.

### Solution Drift (default level: **MUST**)
- Every project listed in `project.gates.yml -> backend.architecture.*` MUST be present in the solution file (`*.slnx` or `*.sln`).
- A project listed in the solution but missing from `project.gates.yml` MUST raise a warning unless `dependencies.allow_unlisted_projects: true`.

### Reference Reciprocity Sanity (default level: **SHOULD**)
- A project that exposes `InternalsVisibleTo` MUST have at least one declared project that consumes it.

## Output Expectations
- `[ MUST ] dependencies.cycles: pass|fail — <count> cycles`.
- `[ MUST ] dependencies.solution-drift: pass|fail — <missing> missing, <unlisted> unlisted`.
- `[ SHOULD ] dependencies.reciprocity: pass|warn — <orphans> orphan IVT`.
- Non-zero exit code on any MUST failure.

## Future Enhancements (Phase 2)
- NuGet package version drift (Central Package Management).
- Cross-app dependency policy in monorepos (frontend ↔ shared packages).
- Dependency-cruiser for Next.js layers.
