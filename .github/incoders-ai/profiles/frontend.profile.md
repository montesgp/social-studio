# Frontend Profile

## Purpose
Bundle of rules and skills loaded when the active project manifest declares a frontend application.

## Scope
Any frontend repository (Angular, React, Next.js, Vue, etc.).

## Load Conditions
Project manifest declares any frontend stack.

## Do Not Load When
Pure backend or infrastructure repositories.

## Rules
- MUST follow `core/global-engineering-constitution.instructions.md`, `core/architecture-boundaries.rules.md`, `core/reuse-before-create.rules.md`, `core/utc-and-localization.rules.md`, `core/git-safety.rules.md`.
- MUST follow the active stack's instructions (e.g. `stacks/angular/*`, `stacks/nextjs/*`, `stacks/react/*`).
- MUST keep a strict separation between presentation, routing, state orchestration, and data access.
- MUST keep business rules out of presentational components.
- Every relevant screen MUST support loading, empty, error, and success states.
- Every HTTP flow MUST comply with the global loader and blocking UX strategy the project declares.
- Every new reusable or non-trivial component MUST include the full file set the project declares. The exact file set is owned by the active stack (e.g. for Angular: TS + template + style + spec + docs; for React/Next.js: as declared by the active stack).
- User-facing text MUST be prepared for i18n from day one; do not hardcode UI copy that belongs in translation files.
- Multi-theme support is mandatory when the project declares more than one theme. Use semantic design tokens; avoid hardcoded colors in components.
- Full responsive behavior is mandatory.
- SHOULD prefer simple, production-oriented implementations over speculative abstractions.

## Skills loaded by default
- `skills/shared/audit.skill.md`
- `skills/shared/reuse-before-create.skill.md`
- `skills/shared/git-commit-workflow.skill.md`
- `skills/frontend/forms-and-validation.skill.md`
- `skills/frontend/loading-and-progress.skill.md`
- `skills/frontend/notifications-and-feedback.skill.md`
- `skills/frontend/list-and-card-views.skill.md`
- `skills/frontend/search-and-selection.skill.md`
- `skills/frontend/admin-crud.skill.md`
- `skills/frontend/master-detail-child-collections.skill.md`
- `skills/frontend/api-mocking.skill.md`
- `skills/frontend/app-layout-navigation.skill.md`
- `skills/frontend/icon-system.skill.md`
- `skills/frontend/multi-theme.skill.md`
- `skills/frontend/multi-language.skill.md`
- `skills/frontend/modern-enterprise-ui.skill.md`
- `skills/frontend/auth-form-standards.skill.md`
- `skills/frontend/code-style.skill.md`
- `skills/shared/entity-key-governance.skill.md`
- `skills/shared/drag-sort-and-preponderance.skill.md`

## Loading-feedback doctrine (mandatory)
Choose loading feedback by operation semantics, not visual preference. See `skills/frontend/loading-and-progress.skill.md`.

## Date-range safety (mandatory)
Whenever a semantic date-range pair appears in a request/feature/screen/filter/form/model/DTO/query (e.g. `start`/`end`, `from`/`to`, `date_from`/`date_to`, `valid_from`/`valid_to`), treat it as a protected date-range scenario: block inconsistent selection in the UI when technically possible; add cross-field form validation; prevent primary submit while the range is invalid; show a clear translated validation message; preserve the rule in create and edit flows; enforce the rule again in the data/business boundary. Never leave date consistency implicit.

## Master-detail doctrine (mandatory)
Whenever a feature describes header+items, parent+child rows, nested editable collections, subtables, child rows under tabs, one-to-many editable collections, FK-backed detail rows, or reorderable child items, first evaluate whether the shared master-detail child-collection primitive applies. See `skills/frontend/master-detail-child-collections.skill.md`.

## Output Expectations
- Reviews enforce the doctrine above before merge.
- Every new feature consumes the shared primitives the active stack declares.
