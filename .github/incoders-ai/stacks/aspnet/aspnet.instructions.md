# ASP.NET Core API Instructions

## Purpose
Standards for building HTTP APIs with modern ASP.NET Core.

## Scope
ASP.NET Core projects (.NET 8+), including Minimal APIs, controllers, and endpoint groups.

## Load Conditions
Project manifest declares a `stacks/aspnet` or equivalent ASP.NET layer.

## Do Not Load When
Non-.NET backends (Node, Go, Python, etc.).

## Rules
- MUST use modern ASP.NET Core APIs with clear endpoint grouping (Minimal API groups, route tags, or controller groups consistently chosen per project).
- MUST declare every endpoint's purpose and response contract via OpenAPI metadata: summary, response types, error responses, auth requirements, tags. XML comments and endpoint metadata SHOULD be used together to maximize Swagger/OpenAPI generation quality.
- MUST centralize exception-to-response mapping (e.g. ProblemDetails middleware, exception handlers) — do not throw raw exceptions onto the wire.
- MUST keep request/response DTOs separate from domain entities. Map at the boundary.
- MUST NOT let controllers/endpoints become orchestration centers — they translate transport and delegate to Application.
- MUST default endpoints to "requires bearer authentication" and opt out explicitly for public endpoints.
- SHOULD use policy-based authorization with explicit, maintainable permission naming.
- SHOULD centralize authentication setup in a dedicated DI module (see `core/modular-composition.rules.md`).
- MUST prefer FluentValidation (or equivalent validation pipeline) at the application boundary; do not scatter manual guard code through controllers.
- MUST keep validation outputs consistent and localizable; validation lives near the boundary, not inside business orchestration.
- MUST keep validation messages localizable per `core/utc-and-localization.rules.md`.

## Output Expectations
- Every endpoint discoverable in OpenAPI with summary, response types, error responses, auth metadata, and tags.
- Validation errors share one consistent shape across the API.
- No business logic, persistence calls, or external integrations executed directly inside endpoint handlers.
