# AI Agent Workflow Gate

## Purpose
Codify the obligations of any AI agent (Claude, Codex, Cursor, Copilot, etc.) operating in this repository so that quality gates and governance are not bypassed.

## Scope
Applies to every agent invocation that reads, writes, refactors, or commits code.

## Load Conditions
Always loaded together with `AGENTS.md` and the project manifest.

## Do Not Load When
Never. This is a baseline.

## Rules

### 1. Pre-Work Reading (default level: **MUST**)
Before modifying any code, the agent MUST have loaded:
- `/AGENTS.md`.
- `.github/project-ai/project.manifest.md` and the global manifests it declares.
- `.github/incoders-ai/core/global-engineering-constitution.instructions.md`.
- `.github/incoders-ai/core/best-practice.rules.md`.
- `.github/incoders-ai/core/architecture-boundaries.rules.md`.
- `.github/incoders-ai/gates/README.md` and any gate file matching the work at hand.
- The relevant project instructions in `.github/project-ai/instructions/**`.

The agent MUST NOT load unrelated stack/provider files (per `core/ecosystem-loading-protocol.instructions.md`).

### 2. Pre-Completion Validation (default level: **MUST**)
Before declaring a task complete the agent MUST:
- Run `scripts/gates/Run-Gates.ps1` (or the documented invocation for the affected scope).
- Report the gate summary back to the user.
- NOT mark the task complete if any MUST gate failed.
- NOT silently skip a SHOULD gate; warnings MUST be surfaced.

### 3. Rule Relaxation (default level: **MUST**)
- The agent MUST NOT downgrade or disable a MUST gate.
- A SHOULD downgrade to MAY requires explicit user approval AND a note in the PR description.
- Permanent changes to `project.gates.yml` severity levels require an ADR under `docs/architecture/adr/`.

### 4. Commit / Push Discipline (default level: **MUST**)
- The agent MUST NOT push or commit if a MUST gate fails.
- The agent MUST follow `.github/incoders-ai/core/git-safety.rules.md` (agents branch/commit/push and open PRs; the user approves and merges PRs into `dev`/`main`).
- The agent MUST NOT use `--no-verify`, `-c core.hooksPath=/dev/null`, or any equivalent skip-hook flag. The pre-commit and pre-push hooks installed by `scripts/gates/Install-Hooks.ps1` exist precisely to enforce sections 2 and 3 of this gate.

### 5. Architecture Discipline (default level: **MUST**)
- The agent MUST NOT introduce a `<ProjectReference>` or `using` directive that violates `architecture.gate.md`.
- If a feature seems to need a forbidden import, the agent MUST add an adapter in `Infrastructure` instead.

### 6. Documentation Sync (default level: **MUST**)
- When behavior changes, the agent MUST update `docs/`, `openspec/specs/`, or the relevant project instruction file in the same change.

### 7. Reporting (default level: **SHOULD**)
- The agent SHOULD include in its final response: files modified, gates run, gate summary, and any deferred follow-ups.

## Output Expectations
- `[ MUST ] ai-agent.protocol-followed: pass|fail — <missing-step>`.
- This gate is partly self-attested. The script form covers items 4 and 6 (commit hygiene, doc sync) where mechanically verifiable.
