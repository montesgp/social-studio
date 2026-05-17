# Angular Data Access

## Purpose
Standard layering for Angular ↔ backend HTTP integration.

## Scope
Angular projects with HTTP-based data access.

## Load Conditions
Project manifest declares an Angular stack and the project calls backends over HTTP.

## Do Not Load When
Angular projects without HTTP integration (rare).

## Rules
- MUST layer data access as: interceptors (cross-cutting) → feature API service / gateway (endpoint calls) → mapper (API → UI model) → page/container (orchestration).
- MUST keep HTTP details out of presentational components.
- MUST use typed request and response models.
- MUST centralize base URL and environment configuration.
- MUST handle loading, server errors, empty responses, and retries deliberately.
- SHOULD use a global loader interceptor that blocks concurrent malicious or accidental repeated user actions where the UX requires it.
- MUST prefer idempotent UI handling and explicit disabled states on submit actions.
- MUST NOT hardcode API URLs, secrets, tokens, or environment-specific values.
- MUST separate UI models from API DTOs when their shapes differ.
- SHOULD inspect backend OpenAPI before integrating; preserve backward compatibility when possible.

## Output Expectations
- Components never import `HttpClient`.
- All HTTP cross-cutting concerns live in interceptors.
- Adding a new endpoint follows the layered template.
