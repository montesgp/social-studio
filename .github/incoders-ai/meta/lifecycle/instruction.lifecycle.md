# Instruction Lifecycle

## Purpose
Manage the full lifecycle of instruction / rules documents: create new instruction files, modify existing rules, deprecate obsolete ones.

## Placement Decision
- Universal rule → `.github/incoders-ai/core/`.
- Profile rule (frontend, backend, fullstack, monorepo) → `.github/incoders-ai/profiles/`.
- Stack rule (dotnet, react, nextjs, etc.) → `.github/incoders-ai/stacks/<stack>/`.
- Provider rule (supabase, openai, anthropic, etc.) → `.github/incoders-ai/providers/<category>/<provider>/`.
- Project rule → `.github/project-ai/instructions/<name>.md`.

## Create
Trigger: a rule is recurring, missing, or scattered across multiple files.

Required sections (per `core/documentation-standards.instructions.md`):
- Purpose
- Scope
- Load Conditions
- Do Not Load When
- Rules (use MUST / SHOULD / MAY)
- Output Expectations

Rules MUST NOT mention stacks, frameworks, or providers that the file's placement does not justify (e.g. no Angular rules inside a project that declares Next.js, no Blazor mentions inside a project-level instruction).

## Modify
Trigger: behavior changed, rules need tightening or relaxation, or scope shifted.

Rules:
- Preserve the file path; manifests and `ai-agent.gate.md` reference instructions by path.
- Tightening MUSTs is always allowed; relaxing a MUST requires an ADR.
- Project rules MAY tighten global rules but MUST NOT weaken safety/security controls.
- Bump `.github/incoders-ai/VERSION.md` (minor) when reusable instructions change.
- Record the change in `CHANGELOG.md`.

## Deprecate
Trigger: rule replaced, merged into another instruction, or obsolete.

Rules:
- Replace the body with a one-line pointer to the successor or removal date.
- Remove the file from any manifest LOAD line and from `project.manifest.md` references.
- Keep the file for one minor cycle before deletion.
- Note the deprecation in `CHANGELOG.md`.
