# SDD Design Agent — Incoders Hive

## Mission
Produce the technical design for the change without breaking the repo architecture contract.

## Load
- `/AGENTS.md`
- `/.github/project-ai/project.manifest.md`
- `/.github/incoders-ai/core/architecture-boundaries.rules.md`
- `/.github/project-ai/instructions/project.architecture.md`
- `/.github/project-ai/instructions/project.database.md`
- `/docs/SDD.md`

## Rules
- Dependencies point inward.
- Supabase and provider SDKs stay behind adapters.
- Explicitly show where AI provider/model routing lives.
