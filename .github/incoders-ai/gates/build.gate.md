# Build Gate

## Purpose
Guarantee that the entire solution and frontend apps restore and compile cleanly before any other gate runs.

## Scope
Applies to:
- The .NET solution defined in `project.gates.yml -> backend.solution`.
- Each frontend app declared in `project.gates.yml -> frontend.apps[]`.

## Load Conditions
Every CI run, every local pre-commit/pre-task validation by humans or AI agents.

## Do Not Load When
Documentation-only changes outside `docs/**`, `.github/**`, or any file path matched by `gates.skip_paths`.

## Rules
- Default level: **MUST**.
- Backend MUST be built with `TreatWarningsAsErrors=true` (already enabled in all `*.csproj`). Do not weaken this.
- A failing restore (`dotnet restore`) MUST be treated as a build failure.
- Frontend builds MUST run `npm install` (or the configured package manager) and the configured `build_script`.
- Build artifacts MUST NOT be committed; verify `bin/`, `obj/`, `.next/`, `node_modules/` remain in `.gitignore`.

## Output Expectations
- `[ MUST ] build.backend: pass|fail — <project count> projects built`.
- `[ MUST ] build.frontend.<app>: pass|fail — built in <duration>s`.
- Non-zero exit code on any failure.
