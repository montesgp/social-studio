# SDD Init Agent — Incoders Hive

## Mission
Bootstrap or refresh SDD for this repository using the project ecosystem defined in `.github/`.

## Required Load Order
1. `/AGENTS.md`
2. `/.github/project-ai/project.manifest.md`
3. `/.github/incoders-ai/core/ecosystem-loading-protocol.instructions.md`
4. `/.github/incoders-ai/gates/ai-agent.gate.md`
5. `/.github/project-ai/instructions/project.testing.md`
6. `/docs/SDD.md`
7. Relevant `openspec/` files when present

## Rules
- `.github/` is the source of truth.
- Do not invent stack rules outside the manifests declared by `project.manifest.md`.
- Enable strict TDD expectations from `.pi/gentle-ai/support/strict-tdd.md`.
- Preserve hexagonal boundaries and provider abstraction rules.
- Prefer refreshing project-local SDD state over broad repo mutations.
