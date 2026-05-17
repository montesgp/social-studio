# Backend Profile

## Purpose
Bundle of rules and skills loaded when the active project manifest declares a backend application.

## Scope
Any backend repository (.NET, Node, Go, Python, etc.).

## Load Conditions
Project manifest declares any backend stack.

## Do Not Load When
Pure frontend repositories with no backend code.

## Rules
- MUST follow `core/global-engineering-constitution.instructions.md`, `core/architecture-boundaries.rules.md`, `core/reuse-before-create.rules.md`, `core/utc-and-localization.rules.md`, `core/observability.rules.md`, `core/external-integrations.rules.md`, `core/modular-composition.rules.md`, `core/git-safety.rules.md`.
- MUST follow the active stack's instructions (e.g. `stacks/dotnet/*`, `stacks/aspnet/*`, `stacks/nodejs/*`).
- MUST preserve clean separation of concerns and hexagonal architecture principles.
- MUST prioritize reusable abstractions.
- MUST document every public endpoint with OpenAPI metadata (or equivalent contract surface).
- MUST use localization-ready messages.
- MUST assume all timestamps are UTC.
- MUST route external integrations through Anti-Corruption Layers.
- MUST keep persistence replaceable via provider abstraction.
- MUST prefer DI modules/extensions over centralized startup sprawl.
- MUST generate or update automated tests for every backend change.
- MUST treat the product's PRD (when present) as the source of business truth.
- MUST treat the product's SDD (when present) as the source of architectural constraints.

## Hard constraints
Never:
- Create duplicate helper or mapper systems.
- Hardcode localized outward-facing messages when localization is expected.
- Instantiate ad-hoc HTTP clients (use the project's client factory).
- Expose raw external provider contracts to Domain/Application.
- Bind business logic to a specific persistence technology.
- Leave endpoints undocumented.
- Introduce hidden side effects.
- Commit or push without explicit user permission.
- Execute any external-provider write operation without explicit user authorization.

## When implementing from frontend needs
If a frontend feature or component documentation exists:
1. Inspect the consuming feature or component markdown.
2. Identify the exact contract required by the UI.
3. Design an API contract that is also suitable for non-specific clients.
4. Implement the minimum stable backend contract.
5. Document the API thoroughly (OpenAPI).
6. Validate authentication, validation, logging, and localization.

## Testing rules
- Every new feature MUST include tests.
- Domain logic → unit tests.
- Infrastructure/providers → integration tests.
- External APIs MUST be mocked unless explicitly required.
- Tests MUST be deterministic.

## Skills loaded by default
- `skills/shared/audit.skill.md`
- `skills/shared/reuse-before-create.skill.md`
- `skills/shared/git-commit-workflow.skill.md`
- `skills/backend/clean-architecture-evolution.skill.md`
- `skills/backend/anti-corruption-layer.skill.md`
- `skills/backend/http-client-factory.skill.md`
- `skills/backend/openapi-first.skill.md`
- `skills/backend/bearer-auth.skill.md`
- `skills/backend/observability.skill.md`
- `skills/backend/localization-ready-api.skill.md`
- `skills/shared/entity-key-governance.skill.md`

## Output Expectations
- Reviews verify every endpoint is OpenAPI-documented, secured, validated, observable, and localization-ready.
- Provider replacement only touches Infrastructure modules.
- Outbound traffic shares one resilience policy surface.
