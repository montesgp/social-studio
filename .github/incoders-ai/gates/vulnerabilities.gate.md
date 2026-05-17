# Vulnerabilities Gate

## Purpose
Surface known vulnerabilities in third-party dependencies.

## Scope
Applies to:
- The .NET solution (`dotnet list package --vulnerable --include-transitive`).
- Each frontend app (`npm audit --omit=dev`).

## Load Conditions
Every CI run. Locally MAY be skipped via `--skip vulnerabilities` to keep iteration fast; CI MUST always run it.

## Do Not Load When
Offline environments. Requires network access to NuGet and npm advisory feeds.

## Rules

### Backend (default level: **SHOULD**)
- Any `Critical` or `High` advisory MUST raise a warning that includes package, version, and CVE.
- Once Phase 2 baseline is set, this gate SHOULD be promoted to MUST.

### Frontend (default level: **SHOULD**)
- `npm audit --audit-level=high --omit=dev` MUST exit cleanly. Findings raise warnings.
- Findings SHOULD be triaged within 7 days for High/Critical.

### Network Failures (default level: **MAY**)
- Transient network errors MUST NOT break CI. The gate downgrades to a warning if registries are unreachable, and prints the cause.

## Output Expectations
- `[ SHOULD ] vulnerabilities.backend: pass|warn|fail — <high>/<critical>`.
- `[ SHOULD ] vulnerabilities.frontend.<app>: pass|warn|fail — <high>/<critical>`.

## Future Enhancements (Phase 2 / Phase 3)
- Promote SHOULD → MUST once a clean baseline is reached.
- SBOM generation (CycloneDX).
- Renovate/Dependabot integration with auto-merge for safe ranges.
- License compliance scan.
