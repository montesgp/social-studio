# Bearer Authentication and Policy-Based Authorization

## Purpose
Secure endpoints by default and centralize authorization decisions.

## Scope
Any backend with HTTP endpoints that require identity or authorization.

## Load Conditions
A backend or fullstack profile is active and authentication is in scope.

## Do Not Load When
Public-only APIs with no protected resources.

## Inputs
- The endpoint's required identity model (anonymous, authenticated user, role, claim, policy).

## Procedure
1. Default every endpoint to "requires bearer authentication". Mark public endpoints explicitly.
2. Centralize authentication setup in a dedicated DI module (`AddAuthentication`).
3. Express authorization as named policies, not inline checks scattered through handlers.
4. Use explicit and maintainable permission/policy naming (e.g. `tenant.read`, `admin.user.delete`).
5. Avoid duplicating authorization logic across handlers — put it in the policy or a shared guard.

## Output Expectations
- Discovering "what permissions an endpoint requires" is a one-step lookup.
- Authorization rules are reviewable as a flat list of policies, not a search across handlers.
- Adding a new role/permission requires touching the policy registration, not every endpoint.
