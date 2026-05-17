# Gates Policy

## Purpose
Define the enforcement contract that every gate in this folder must respect.

## Scope
Applies to all repositories that load Incoders AI manifests and ship a `project.gates.yml`.

## Load Conditions
Always loaded together with any individual `*.gate.md`.

## Do Not Load When
Never. This is the policy backbone.

## Rules

### 1. Severity Levels
- **MUST** — Mandatory. Fails CI and `Run-Gates.ps1` with non-zero exit code. Blocks merge.
- **SHOULD** — Strong default. Surfaces warnings; MUST NOT block CI unless project policy explicitly upgrades it to MUST.
- **MAY** — Informational/optional. Surfaces hints. MUST NOT influence exit code.

### 2. Severity Override Rules
- Projects MAY upgrade a SHOULD/MAY gate to MUST through `project.gates.yml`.
- Projects MUST NOT downgrade a MUST gate without:
  1. An ADR in `docs/architecture/adr/` explaining the rationale and expiration date.
  2. Explicit reference to that ADR in `project.gates.yml` next to the override.

### 3. Configuration Source of Truth
- Global gate definitions live in `.github/incoders-ai/gates/`.
- Project-local configuration lives at the repo root in `project.gates.yml`.
- Implementation lives in `scripts/gates/**` and `.github/workflows/**`.
- No gate may hardcode project paths — all targets MUST come from `project.gates.yml`.

### 4. Determinism
- A gate MUST produce the same result for the same code on the same machine.
- A gate MUST NOT depend on network availability except where explicitly documented (e.g. `vulnerabilities.gate.md` reads NuGet/npm advisory feeds).

### 5. Locality
- Every gate MUST be runnable locally with the same script that CI invokes.
- CI is a thin caller; the script is the source of truth.

### 6. Reusability
- Scripts MUST work with no project-specific knowledge other than `project.gates.yml`.
- The reusable workflow `_reusable-quality-gates.yml` MUST be copy-pasteable into other Incoders repos with only `project.gates.yml` adapted.

### 7. Result Contract
Each check script MUST emit:
- A single line per check: `[ <LEVEL> ] <gate-name>: <pass|warn|fail> — <one-line summary>`.
- Exit code: `0` if no MUST failed; `1` otherwise.

### 8. AI Agent Compliance
Per `ai-agent.gate.md`, agents MUST:
- Read `AGENTS.md`, the project manifest, and any matching gate file before changing code.
- Run `scripts/gates/Run-Gates.ps1` before declaring a task complete.
- Not relax a MUST without an ADR.
- Not finalize a task with a failing build or failing tests.

## Output Expectations
Compliance is binary per gate level. Aggregated status reported by `Run-Gates.ps1` summary table.
