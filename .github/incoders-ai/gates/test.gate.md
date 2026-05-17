# Test Gate

## Purpose
Guarantee that the declared test projects execute and pass before code is merged or a task is finalized.

## Scope
Applies to every project listed in `project.gates.yml -> backend.test_projects[]` and to any frontend app that declares `test_script` in `frontend.apps[]`.

## Load Conditions
Every CI run, every local pre-task validation.

## Do Not Load When
Pure documentation/manifest changes that have no compiled artifact impact.

## Rules
- Default level: **MUST**.
- Tests MUST be deterministic, isolated, and stable in CI (per `core/best-practice.rules.md` §9).
- Flaky tests MUST be fixed or quarantined with an ADR — not silently skipped.
- A regression MUST include a reproducing test before the fix.
- AI agents MUST NOT mark a task complete with red tests.
- Coverage collection is opt-in via `gates.test.collect_coverage` in `project.gates.yml`. When enabled, `dotnet test` is invoked with `--collect:XPlat Code Coverage`. Some coverlet/SDK combinations break tests when this flag is added; keep it off until a clean baseline is reached and validated locally.
- The `coverage.gate.md` gate runs independently and skips when no report exists.

## Output Expectations
- `[ MUST ] tests.backend: pass|fail — <total> total, <failed> failed in <duration>s`.
- `[ MUST ] tests.frontend.<app>: pass|fail|skipped — <summary>`.
- Non-zero exit code on any failure.
