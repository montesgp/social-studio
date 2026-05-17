# Skill Lifecycle

## Purpose
Manage the full lifecycle of skill definitions: create new skills, modify existing ones, deprecate obsolete behaviors.

## Placement Decision
- Cross-stack reusable behavior (works across multiple stacks of the same profile) → `.github/incoders-ai/skills/<scope>/<name>.skill.md` (where `<scope>` is `frontend`, `backend`, `fullstack`, `shared`, `ai`, `testing`).
- Stack-specific behavior (only applies when a specific stack is active) → `.github/incoders-ai/stacks/<stack>/<name>.skill.md`. Examples: a "create Next.js feature" skill belongs in `stacks/nextjs/`, not `skills/frontend/`. A "Reactive Forms validator" skill belongs in `stacks/angular/`, not `skills/frontend/`.
- Provider- or tool-specific behavior → `.github/incoders-ai/providers/<category>/<provider>/<name>.skill.md` (alongside the provider's instructions).
- Project- or domain-specific behavior → `.github/project-ai/skills/<name>.skill.md`.

**Stack-leak test (MUST):** if a skill's name, content, or examples reference a specific stack (Next.js, Angular, ASP.NET, React, etc.) and that reference cannot be removed without destroying the skill, the file MUST live under `stacks/<that-stack>/`, never under the generic `skills/<scope>/`. Skills under `skills/<scope>/` MUST be genuinely cross-stack — the content should apply equally whether the active stack is Next.js, Angular, or anything else of the same profile.

## Create
Trigger: a recurring procedure or pattern is not yet captured by any existing skill.

Required sections (per `core/documentation-standards.instructions.md`):
- Purpose
- Scope
- Load Conditions
- Do Not Load When
- Inputs
- Procedure
- Output Expectations

Procedure steps SHOULD be numbered and verifiable.

## Modify
Trigger: the procedure changed, inputs/outputs evolved, or load conditions need to be tightened or broadened.

Rules:
- Preserve the file path; manifests reference skills by path.
- Tightening Load Conditions is safe. Broadening them requires checking that callers still apply.
- Bump `.github/incoders-ai/VERSION.md` (minor) when reusable skills change behavior.
- Record the change in `CHANGELOG.md`.

## Deprecate
Trigger: superseded by a better skill, no longer relevant, or absorbed into another file.

Rules:
- Replace the body with a one-line pointer to the successor (or to a removal date).
- Remove the skill from any manifest LOAD line before deletion.
- Keep the file for one minor cycle to avoid broken references.
- Note the deprecation in `CHANGELOG.md`.
