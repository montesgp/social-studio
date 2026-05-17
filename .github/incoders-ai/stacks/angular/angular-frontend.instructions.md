# Angular Frontend

## Purpose
Standards for building Angular applications in this ecosystem.

## Scope
Angular projects (standalone, modern Angular versions declared by the active manifest).

## Load Conditions
Project manifest declares an Angular stack.

## Do Not Load When
Non-Angular frontends (React, Next.js, Vue, etc.).

## Rules
- MUST use standalone components.
- MUST use lazy-loaded feature routes.
- MUST use a feature-first folder layout under `src/app/features/**`.
- `core/` SHOULD contain singleton infrastructure only.
- `shared/` SHOULD contain only truly reusable UI or system primitives.
- SHOULD use Signals for local component state when appropriate.
- Preferred per-feature structure:
  - `pages/` — route containers
  - `components/` — feature UI pieces
  - `services/` — feature orchestration or API facades
  - `models/` — typed contracts or view models
  - `mappers/` — transformation logic
  - `store/` — only when state complexity truly requires it
  - `docs/` — when documentation lives close to the feature
- MUST keep container/page components responsible for orchestration only.
- MUST keep presentational components simple and reusable.
- MUST use typed inputs and outputs.
- MUST NOT put heavy business logic in templates.
- SHOULD prefer modern Angular template features when they improve readability.
- MUST prefer route-level code splitting.
- Component file convention: `.component.ts`, `.component.html`, `.component.scss`, `.component.spec.ts`, `.component.md` for every non-trivial component.
- MUST use SCSS as the standard component styling format.

## Component completeness (mandatory)
Every non-trivial Angular component MUST be delivered with all of: `.component.ts`, `.component.html`, `.component.scss`, `.component.spec.ts`, `.component.md`. Do not omit any of these unless explicitly requested.

## Form behavior
All forms MUST use Reactive Forms, use shared validators when possible, disable primary submit actions until business prerequisites are satisfied, prevent duplicate submissions, expose clear validation/helper feedback, and support loading/submitting/success/error/disabled states.

## Button enablement
Buttons MUST NOT be enabled only because fields contain some value. Enable only when validation rules pass, business prerequisites pass, dependent selections are complete, and the use case allows the action.

## Architecture
- MUST NOT call HttpClient from components.
- All API calls go through services/facades/gateways.
- Interceptors handle global concerns (auth headers, tracing, error normalization, global loader).
- No duplicated shared UI patterns.

## Output Expectations
- New features land in `src/app/features/<feature>/<subfeature>/` with the declared subfolder layout.
- Every non-trivial component ships with its full file set.
- Reviews catch components that talk to HttpClient directly.
