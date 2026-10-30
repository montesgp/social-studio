# Codex Bootstrap — Social Studio

## Required Read Order
1. `/AGENTS.md` — project contract and non-negotiable rules.
2. `.github/project-ai/project.manifest.md` — active manifests, instructions, skills and agents for this project.
3. `.github/incoders-ai/core/ecosystem-loading-protocol.instructions.md` — load order and precedence.
4. `.github/incoders-ai/core/best-practice.rules.md` — engineering baseline.
5. `.github/incoders-ai/core/architecture-boundaries.rules.md` — hexagonal layering.
6. `.github/incoders-ai/core/git-safety.rules.md` — agents branch/commit/push and open PRs; the user approves and merges PRs to `dev`/`main`.
7. `.github/incoders-ai/gates/README.md` and any matching `*.gate.md` for the affected scope.
8. The project instructions declared by `project.manifest.md`.
9. OpenSpec specs relevant to the task, when present.

## Load Discipline
- Load only the global manifests declared by the active project manifest. Do not load unrelated stacks/providers.
- Project-specific rules override global rules. Stack/provider overrides profile. Task overrides all non-safety rules.

## Gates Contract (MUST)
- Read `.github/incoders-ai/gates/ai-agent.gate.md` before modifying code.
- Run `pwsh -File scripts/gates/Run-Gates.ps1` before declaring a task complete.
- Do NOT mark a task complete if any MUST gate failed.
- Do NOT downgrade a MUST gate without an ADR under `docs/architecture/adr/`.
- Do NOT use `--no-verify` or equivalent skip-hook flags.

## Architecture Discipline (MUST)
- Dependencies point inward: Domain ← Application ← Infrastructure / Api.
- Provider-specific concerns stay behind ports/adapters. Supabase, EF Core, ASP.NET types MUST NOT leak into Domain or Application.

## Lifecycle of Ecosystem Artifacts
- Creating, modifying or deprecating agents, skills, instructions, manifests or providers follows the templates under `.github/incoders-ai/meta/lifecycle/`.

## Documentation Sync (MUST)
- When behavior changes, update `docs/`, `openspec/specs/`, or the relevant `.github/project-ai/instructions/*.md` in the same change.
