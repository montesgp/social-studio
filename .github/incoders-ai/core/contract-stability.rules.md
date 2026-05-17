# Contract Stability and Frontend-Driven Resource Design (Global Rule)

## Purpose
Backend resources MUST be shaped from real frontend/consumer workflows while remaining durable for multiple clients.

## Scope
Cross-cutting rule between frontend feature design and backend API design.

## Load Conditions
Loaded for fullstack, monorepo, and multi-repo-workspace profiles. Also loaded for backend profiles whenever the project also has a frontend or mobile client in the workspace.

## Do Not Load When
A repository ships an API with no known internal client (pure public SDK).

## Rules
- MUST inspect consuming frontend/mobile/component documentation before defining or changing endpoints for a resource.
- MUST design contracts that serve multiple clients (web, mobile, third parties) — do not overfit to a single client's implementation details.
- MUST NOT implement read-only endpoints for a resource if the consuming client requires create, update, delete, activation, pagination, filtering, sorting, or detail retrieval. Implement the full needed capability or explicitly mark the gap.
- MUST evaluate, for every resource, whether the client requires: list, detail-by-id, create, update, delete or soft-delete, activation/deactivation, pagination, filtering, sorting, lookup/autocomplete variants, validation rules, authorization rules.
- MUST update the consuming frontend service when a real endpoint replaces a mock; remove mocks or isolate them behind an explicit fallback.
- MUST surface backend error codes/messages consistently in client error handling.
- SHOULD use names that survive UI churn rather than names tied to the current UI label.

## Output Expectations
- Endpoints exist for every CRUD/operational capability the client requires; no half-finished resources where the menu/screen exists but the API does not.
- Frontend mocks are clearly isolated and labelled, and disappear when the real endpoint is adopted.
- Reviews catch contracts that work only for the current client.
