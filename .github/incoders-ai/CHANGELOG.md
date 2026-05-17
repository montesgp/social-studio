# Changelog

## 1.3.0
- Major content enrichment. Ecosystem moved from skeleton (3-10 line stubs) to operational baseline across `core/`, `profiles/`, `stacks/`, `skills/`, `agents/`.
- **`core/`**:
  - `best-practice.rules.md` extended with sections 16 (Reuse-First), 17 (Contract Stability), 18 (Mock-First); section 7 (Security) tightened with external-provider write authorization; section 15 (Delivery) extended with explicit commit discipline.
  - `architecture-boundaries.rules.md` and `global-engineering-constitution.instructions.md` rewritten from stubs to full documents.
  - New files: `reuse-before-create.rules.md`, `utc-and-localization.rules.md`, `observability.rules.md`, `external-integrations.rules.md`, `modular-composition.rules.md`, `contract-stability.rules.md`.
- **`profiles/`**: `frontend.profile.md`, `backend.profile.md`, `fullstack.profile.md` rewritten from stubs with default skill loadouts and mandatory doctrines (date-range safety, master-detail, hard constraints).
- **`stacks/`**:
  - `stacks/dotnet/dotnet.instructions.md` and `stacks/aspire/aspire.instructions.md` rewritten with DI module discipline, persistence boundaries, HTTP-client-factory mandate.
  - New `stacks/aspnet/`: `aspnet.instructions.md`, `persistence.instructions.md`.
  - New `stacks/angular/` content: `angular-frontend.instructions.md`, `ui-system.instructions.md`, `data-access.instructions.md`, `testing.instructions.md`, `i18n.instructions.md`. Component-completeness contract (`.component.ts/html/scss/spec/md`) lives here as a hard mandate; the abstract version sits in `profiles/frontend.profile.md`.
  - New stack-scoped skill: `stacks/nextjs/create-feature.skill.md` (formerly `skills/frontend/create-nextjs-feature.skill.md` — moved per the stack-leak rule).
- **`skills/`** — 19 new cross-stack skills:
  - Backend (7): `clean-architecture-evolution`, `anti-corruption-layer`, `http-client-factory`, `openapi-first`, `bearer-auth`, `observability`, `localization-ready-api`.
  - Frontend (12): `forms-and-validation`, `loading-and-progress`, `notifications-and-feedback`, `list-and-card-views`, `search-and-selection`, `admin-crud`, `master-detail-child-collections`, `api-mocking`, `app-layout-navigation`, `icon-system`, `multi-theme`, `multi-language`, `modern-enterprise-ui`, `auth-form-standards`, `code-style`.
  - Shared (5): `git-commit-workflow`, `reuse-before-create`, `audit`, `entity-key-governance`, `drag-sort-and-preponderance`.
- **`agents/`** — all 8 existing stubs replaced with full lifecycle-spec agents (Purpose / Responsibilities / Load Conditions / Tools-Skills / Decision Rules / Handoff Rules). Three new agents: `agents/backend/api-contract-guardian`, `agents/backend/integration-guardian`, `agents/shared/authorization-steward`.
- **`meta/lifecycle/skill.lifecycle.md`**: formalized the stack-leak placement rule. Stack-specific skills now have an explicit home under `stacks/<stack>/<name>.skill.md`; cross-stack skills stay under `skills/<scope>/`.
- **`core/git-safety.rules.md` rewritten to STRICT.** All Git write operations (`add`, `commit`, `push`, `pull`, `merge`, `rebase`, `checkout`, `switch`, `reset`, `tag`, branch create/delete, force push, `rm -rf` on source) are forbidden by default. Only the user can authorize Git writes; agents modify files, users control Git. Replaces the previous permissive ask-first-on-commit/push/reset/rm-rf posture.
- **`core/00-ecosystem-loading-protocol.instructions.md` renamed to `ecosystem-loading-protocol.instructions.md`.** Numeric prefix dropped; load order is declared by manifests, not by filename. References in `AGENTS.md` (`.codex/`, `.claude/CLAUDE.md`) and `gates/ai-agent.gate.md` updated accordingly.
- Content harvested from sibling Incoders ecosystems and generalized (stripped of project names, entities, themes, locales, paths). Project-specific content stays in `project-ai/`, not in the reusable ecosystem.

## 1.2.0
- Renamed `meta/creators/` → `meta/lifecycle/`. Each file now covers the full lifecycle (Create / Modify / Deprecate), not only creation:
  - `agent-creator.md` → `agent.lifecycle.md`
  - `skill-creator.md` → `skill.lifecycle.md`
  - `instruction-creator.md` → `instruction.lifecycle.md`
  - `manifest-creator.md` → `manifest.lifecycle.md`
  - `provider-creator.md` → `provider.lifecycle.md`
- Removed broken LOAD lines (`.agents/<provider>/best-practices/*`) from `provider-supabase.manifest.md` and `provider-testsprite.manifest.md`.

## 1.1.0
- Added `gates/` (Phase 1 quality gates):
  - `gates.policy.md` (MUST/SHOULD/MAY contract).
  - 8 gate definitions: build, test, coverage, architecture, dependencies, secrets, security-baseline, vulnerabilities.
  - `ai-agent.gate.md` (agent compliance contract).
- Companion implementation lives in `scripts/gates/`, `.github/workflows/`, and `project.gates.yml` at consumer-repo level.
- Phase 2/3 roadmap captured in `docs/quality-gates.md`.

## 1.0.0
- Modular global/project split.
- Stack/provider/profile loading.
- Meta creators for skills, agents, instructions, providers and manifests.
