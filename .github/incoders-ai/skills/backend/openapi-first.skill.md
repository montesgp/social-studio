# OpenAPI First

## Purpose
Treat OpenAPI/Swagger metadata as a deliverable, not an optional decoration.

## Scope
Any HTTP endpoint that ships in a backend.

## Load Conditions
A backend or fullstack profile is active and an HTTP endpoint is being created or changed.

## Do Not Load When
Pure internal messaging or event-driven projects with no HTTP API.

## Inputs
- The endpoint's purpose, inputs, outputs, error responses, and auth requirements.

## Procedure
1. Declare a summary on every endpoint.
2. Declare a description whenever behavior is non-trivial.
3. Declare typed request and response contracts; do not return raw anonymous objects.
4. Declare standard error responses (4xx and 5xx shapes) using a consistent error contract.
5. Declare auth requirements explicitly (anonymous, bearer, policy name).
6. Group endpoints with explicit tags.
7. Consider example payloads when they help external consumers.
8. Verify Swagger/OpenAPI output renders correctly before considering the change complete.

## Output Expectations
- The OpenAPI document is readable by an external consumer who has no access to source.
- No endpoint ships undocumented.
- Error responses are consistent across the API surface.
