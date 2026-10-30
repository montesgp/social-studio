# AGENTS.md - Social Studio

## Project Contract
Agents working in this repository must build Social Studio as a workflow-oriented, AI-native SaaS module for social content operations.

## Required Read Order
1. `AGENTS.md`.
2. `.github/project-ai/project.manifest.md`.
3. `docs/PRD.md`.
4. `docs/SDD.md`.
5. `docs/ai-governance.md`.
6. `.github/incoders-ai/gates/README.md` and any matching `*.gate.md`.
7. Active Incoders AI manifests declared by project manifest.
8. OpenSpec specs relevant to the task.

## Quality Gates Contract
- Read `.github/incoders-ai/gates/ai-agent.gate.md` before modifying code.
- Run `pwsh -File scripts/gates/Run-Gates.ps1` before declaring a task complete.
- Do NOT finalize a task with a failing MUST gate.
- Do NOT relax MUST gates without an ADR justifying it.
- Configuration: `project.gates.yml`. Documentation: `docs/quality-gates.md`.

## Active Architecture
- Monorepo.
- .NET Aspire orchestration.
- Backend uses hexagonal architecture.
- Frontend uses Next.js + React + TypeScript + Tailwind + shadcn/ui + Radix + Lucide + Framer Motion.
- Supabase is the initial infrastructure provider, but must remain behind ports/adapters.
- AI providers must be configurable and abstracted.

## Non-Negotiable Rules
- Frontend code MUST follow the active stack declared in "Active Architecture" (Next.js + React + TypeScript). Stack-specific conventions come from `.github/incoders-ai/stacks/<stack>/`, never from generic frontend rules.
- Do not hardcode Supabase into application/domain layers.
- Do not hardcode a single AI model in business use cases.
- Do not bypass PRD/SDD/OpenSpec for workflow behavior.
- Update docs/specs when changing product behavior.
