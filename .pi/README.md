# Pi Bootstrap — Incoders Hive

This project keeps its AI ecosystem definition in `.github/`.

The `.pi/` folder is only the Pi-facing materialization layer:
- SDD agents live in `.pi/agents/`
- SDD chains live in `.pi/chains/`
- Strict TDD support lives in `.pi/gentle-ai/support/`

## Source of Truth

Always derive context from:
1. `AGENTS.md`
2. `.github/project-ai/project.manifest.md`
3. `.github/incoders-ai/core/ecosystem-loading-protocol.instructions.md`
4. The manifests, instructions, providers, skills, and gates declared by that manifest
5. `docs/PRD.md`, `docs/SDD.md`, `docs/ai-governance.md`
6. Relevant `openspec/specs/`

## Local Keys

1. Copy `.env.local.example` to `.env.local`
2. Fill your local provider keys
3. Start Pi with:

```powershell
pwsh -File scripts/ai/Start-Pi.ps1
```

That script loads `.env.local`, makes the npm global bin available for the current session, and launches `pi`.

## Inside Pi

After opening Pi in this repo:

1. Run `/gentle-ai:status`
2. Run `/gentle-ai:install-sdd --force`
3. Run `/gentleman:models`

If Pi packages are healthy, the local `.pi/agents` and `.pi/chains` materialization in this repo will be the project-specific layer that points back to `.github`.
