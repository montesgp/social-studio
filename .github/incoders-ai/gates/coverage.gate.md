# Coverage Gate

## Purpose
Track test coverage as a trend signal without blocking development before a baseline is established.

## Scope
Applies to backend test projects with `coverlet.collector` enabled (already configured in `Incoders.Hive.Tests.csproj`).

## Load Conditions
Every CI run; optional locally.

## Do Not Load When
Tests gate is disabled or no test project is declared.

## Rules

### Phase 1 Behavior (default level: **SHOULD**, warn-only)
- The gate computes line coverage from coverlet output (`coverage.cobertura.xml`).
- A configured `minimum` threshold of `0` MUST yield a warn-only outcome.
- If `minimum > 0` is set in `project.gates.yml -> gates.coverage.minimum`, falling below MUST fail.
- A `target` value SHOULD be set to communicate intent even when not enforced.
- The gate skips when no coverage report exists. To produce one, enable `gates.test.collect_coverage: true` in `project.gates.yml` (subject to the coverlet/SDK compatibility note in `test.gate.md`).

### Trend (default level: **SHOULD**)
- Coverage SHOULD trend upward for critical layers (Domain/Application). Phase 2 will compare to a stored baseline.

### Critical Path Justification (default level: **MUST** — documented, not script-enforced in Phase 1)
- Untested critical paths (auth, multi-tenant isolation, money-equivalent) MUST be justified in PR notes per `core/best-practice.rules.md` §10.

## Output Expectations
- `[ SHOULD ] coverage.line: pass|warn|fail — <pct>% (min=<min>%, target=<target>%)`.
- `[ SHOULD ] coverage.report: <path-to-report>`.

## Future Enhancements (Phase 2 / Phase 3)
- Persist baseline in `.gates/coverage.baseline.json`; fail when trend drops > 1pp.
- Per-layer thresholds (Domain ≥ 90%, Application ≥ 80%).
- Mutation testing with Stryker.NET on Domain/Application.
- Differential coverage (only files changed in PR).
