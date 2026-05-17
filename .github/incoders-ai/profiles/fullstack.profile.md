# Fullstack Profile

## Purpose
Bundle of rules and skills loaded when the active project manifest declares both frontend and backend layers.

## Scope
Any repository (single or monorepo) that contains both a frontend and a backend.

## Load Conditions
Project manifest declares a fullstack composition.

## Do Not Load When
Pure frontend or pure backend repositories.

## Rules
- MUST follow everything in `profiles/frontend.profile.md` and `profiles/backend.profile.md`.
- MUST follow `core/contract-stability.rules.md` for cross-cutting contract design.
- MUST coordinate frontend and backend changes when contracts change.
- MUST keep frontend mocks isolated and labeled until the real endpoint is adopted.
- MUST surface backend error codes/messages consistently in client error handling.
- MUST NOT design backend resources in isolation from real frontend usage; do not stop at simple `GET`s when the client requires a full CRUD.

## Skills loaded by default
- Union of frontend and backend default skills.

## Output Expectations
- Cross-stack PRs include both frontend and backend changes.
- Mocks disappear (or are clearly isolated) once the real endpoint ships.
- Errors are presented consistently end-to-end.
