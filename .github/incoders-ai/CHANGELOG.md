# Changelog

## 1.7.0
- **New skill: `skills/frontend/ux-focus-management.skill.md`** — Formalizes mandatory UX focus management for all interactive frontend surfaces: auto-focus on form/dialog/panel mount, viewport scroll-into-view on route navigation (with `prefers-reduced-motion` guard), and focus-first-error fallback chain on failed form submit.
- **`skills/frontend/forms-and-validation.skill.md` §Procedure** — Added step 11: apply `ux-focus-management` focus-first-error chain after a failed submit. Error messages alone are not sufficient.
- **`skills/shared/reuse-before-create.skill.md` §Procedure step 3** — Added explicit variant-via-props boundary: same behavior + different configuration → extend via prop; different interaction contract → new registered primitive with documented justification.
- **`core/reuse-before-create.rules.md`** — Added "multi-step progress indicator" to protected primitive list; added MUST rule against creating parallel UI components for configuration variants; cross-referenced `core/ui-primitives.rules.md` §Extending primitives.
- **`profiles/frontend.profile.md`** — Registered `skills/frontend/ux-focus-management.skill.md` in the default frontend skill loadout.
- **`VERSION.md`** — Synced to 1.7.0 (was stale at 1.4.0; latest released was 1.6.0).

## 1.6.0
- **`core/best-practice.rules.md` §7 — external-provider write authorization moved to PR approval.** Removed the per-query "show the exact SQL and request authorization" gate. Schema/data changes (incl. EF migrations) are now authorized by PR approval — the PR review IS the verification — and run on merge/deploy without separate confirmation. This repo applies migrations on startup via `Database.Migrate()`, so an approved+merged change auto-applies pending migrations on the next deploy. Destructive AD-HOC operations outside a reviewed PR still require explicit authorization.

## 1.5.0
- **`core/git-safety.rules.md` relaxed from STRICT.** Agents may now create branches, stage, commit, push to non-protected work branches, and open pull requests without per-action authorization. The user retains exclusive authority over integration: approving and merging pull requests into the protected branches `dev` and `main` (decided after reviewing the full PR; approval may be delegated to an agent only on explicit request). Always-forbidden items remain: hook-skip flags, force push / history rewrite on shared branches, and `rm -rf` on source. Synced references in `AGENTS.md` (`.codex/`, `.claude/CLAUDE.md`), `gates/ai-agent.gate.md`, `skills/shared/git-commit-workflow.skill.md`, `core/best-practice.rules.md`, and `.github/copilot-instructions.md`.

## 1.4.0
- Added the reusable TestSprite QA provider layer for ecosystem-wide AI-assisted validation.
- Introduced `providers/testing/testsprite/testsprite-validation.skill.md` and wired the global test engineer agent to use it when available.
- Fixed the TestSprite provider manifest so every LOAD target resolves to an existing file.

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
