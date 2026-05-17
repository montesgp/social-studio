# Quality Gates (Phase 1)

## Purpose
Enforce engineering quality and governance rules automatically through scripts and CI, on top of the existing Incoders AI documentation layer.

## Scope
This folder describes WHAT each gate enforces, WHY, and at which level (MUST/SHOULD/MAY).
Implementation lives in:
- `scripts/gates/**` (executable checks).
- `.github/workflows/_reusable-quality-gates.yml` (reusable CI).
- `.github/workflows/quality-gates.yml` (project caller).
- `project.gates.yml` (project-local configuration and overrides).

## Load Conditions
Loaded by every agent before modifying source code or finishing a task in this repository.

## Do Not Load When
Editing isolated documentation outside of `docs/`, `AGENTS.md`, manifests, or specs that have no code impact.

## Rules
- Gate definitions in this folder are normative and override generic best-practice text only when stricter.
- Gate severity uses MUST / SHOULD / MAY semantics defined in `gates.policy.md`.
- Project-local configuration (`project.gates.yml`) MAY tighten severity but MUST NOT lower a MUST gate to SHOULD/MAY without an ADR justifying it.
- Agents and humans MUST run `scripts/gates/Run-Gates.ps1` before declaring a task complete.

## Gate Index
| Gate | Level (default) | File |
|---|---|---|
| Build (restore+compile) | MUST | `build.gate.md` |
| Tests (unit + integration) | MUST | `test.gate.md` |
| Architecture boundaries | MUST | `architecture.gate.md` |
| Project dependency graph | MUST | `dependencies.gate.md` |
| Secret detection | MUST | `secrets.gate.md` |
| Security baseline | MUST | `security-baseline.gate.md` |
| Dependency vulnerabilities | SHOULD | `vulnerabilities.gate.md` |
| Test coverage | SHOULD (warn-only initially) | `coverage.gate.md` |
| AI agent workflow | MUST | `ai-agent.gate.md` |

## Output Expectations
- Each gate produces a deterministic pass / warn / fail result.
- Failures (MUST) MUST block CI and local task completion.
- Warnings (SHOULD) MUST be surfaced but MUST NOT block.
- Informational checks (MAY) MUST NOT affect status.

## Roadmap
Phase 2 and Phase 3 enhancements (SonarCloud, CodeQL, mutation testing, contract testing, OpenAPI governance, runtime governance, observability-driven quality, advanced DevSecOps) are tracked in `docs/quality-gates.md`.
