# Agent Lifecycle

## Purpose
Manage the full lifecycle of agent role definitions: create new agents, modify existing ones, deprecate obsolete roles.

## Placement Decision
- Reusable role → `.github/incoders-ai/agents/<scope>/<name>.agent.md`.
- Product/domain role → `.github/project-ai/agents/<name>.md`.

## Create
Trigger: a recurring role is not yet captured by any existing agent file.

Required sections (per `core/documentation-standards.instructions.md`):
- Purpose
- Responsibilities
- Load Conditions
- Tools / Skills
- Decision Rules
- Handoff Rules

Use MUST / SHOULD / MAY when a rule affects implementation behavior.

## Modify
Trigger: scope, rules, or handoffs changed; behavior must be tightened or relaxed.

Rules:
- Preserve the file path so manifests and references keep working.
- Update only the affected sections; do not silently broaden scope.
- Tightening MUSTs is always allowed; relaxing a MUST requires an ADR under `docs/architecture/adr/`.
- When reusable agents change behavior, bump `.github/incoders-ai/VERSION.md` (minor for additions, major for breaking).
- Record the change in `.github/incoders-ai/CHANGELOG.md`.

## Deprecate
Trigger: role replaced, merged into another agent, or no longer applies.

Rules:
- Replace the body with a one-line pointer to the successor file (or to a removal date).
- Keep the file for one minor version cycle before deletion so manifests can be updated without dangling links.
- Remove the agent's LOAD entries from any manifest that references it.
- Note the deprecation in `CHANGELOG.md`.
