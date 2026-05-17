# Pull Request

## Summary
<!-- One-paragraph description of WHAT changes and WHY (motivation, not implementation). -->

## Scope
<!-- Mark all that apply -->
- [ ] Backend (.NET / Aspire / API)
- [ ] Frontend (Next.js / `apps/web`)
- [ ] Domain / Application logic
- [ ] Infrastructure / Persistence
- [ ] AI orchestration / providers
- [ ] Documentation / specs / governance
- [ ] CI / quality gates / scripts

## Quality Gates Checklist
> Reference: `.github/incoders-ai/gates/README.md`. CI runs `Quality Gates` on every PR; this checklist is for the author.

- [ ] `pwsh -File scripts/gates/Run-Gates.ps1` executed locally and **all MUST gates pass**.
- [ ] No new SHOULD warnings I am not consciously accepting.
- [ ] If a gate was relaxed in `project.gates.yml`, an ADR justifying it is included.

## Architecture & Boundaries
- [ ] Hexagonal layering preserved (Domain → Application → Infrastructure → Api).
- [ ] No EF Core / Supabase / Npgsql / ASP.NET types leaked into Domain or Application.
- [ ] Provider-specific concerns kept behind ports/adapters.
- [ ] If a new project reference was added, it respects `architecture.gate.md`.

## Tests
- [ ] Unit tests added/updated for changed Domain/Application behavior.
- [ ] Bug fixes include a regression test.
- [ ] All declared test projects green locally.
- [ ] Coverage trend acceptable for touched areas (Phase 1 is warn-only; Phase 2 will enforce).

## Security
- [ ] No real secrets committed (verified by `secrets.gate.md`).
- [ ] No insecure defaults introduced (CORS+credentials, cert validation bypass, etc.).
- [ ] If new dependencies were added, vulnerability scan reviewed.

## Docs / Governance
- [ ] `docs/`, `openspec/specs/`, or relevant `project-ai/instructions/*.md` updated when behavior changed.
- [ ] PRD/SDD/ADR updated if architecture or product behavior changed.

## AI Agent Compliance (only if AI assisted this PR)
- [ ] Agent loaded `AGENTS.md`, project manifest, and matching gates before editing.
- [ ] Agent ran `Run-Gates.ps1` before declaring the task complete.
- [ ] Agent did not relax any MUST gate.
- [ ] Agent did not commit/push without explicit user approval (per `core/git-safety.rules.md`).

## Risks & Rollback
<!-- What could break? How do we roll back? Any required ops/migration steps? -->

## Linked Issues / Specs
<!-- Closes #..., related to openspec/changes/..., relates to PRD section ... -->
