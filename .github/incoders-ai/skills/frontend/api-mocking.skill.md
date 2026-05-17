# API Mocking (Mock-First Frontend Development)

## Purpose
Implement frontend features safely when the real API is missing, partial, or must not be called directly.

## Scope
Any frontend feature whose backend contract is incomplete or unavailable.

## Load Conditions
A frontend or fullstack profile is active and the backend contract is incomplete or unavailable.

## Do Not Load When
The full backend contract is already implemented and stable.

## Inputs
- The UI's data needs.
- The eventual real API contract (even if incomplete).

## Procedure
1. Do NOT block feature implementation because of a missing API.
2. Define typed contracts in a data-access layer; feature components consume services/facades only.
3. Implement a mock service/repository/gateway behind the same contract as the real one.
4. Pick one mock strategy and stick with it: in-memory fake service, mock repository/gateway, local JSON mapped through a service, environment-based provider swap, or injection-token-based provider swap.
5. Components MUST NOT call HttpClient directly. Components MUST NOT know whether data is mocked or real.
6. Name mocks explicitly (e.g. `FeatureService` + `MockFeatureService`, `EntityGateway` + `MockEntityGateway`, `ItemsRepository` + `MockItemsRepository`).
7. Use environment-aware providers (or app-level provider selection) so swapping mock for real is configuration, not refactor.
8. Mock data must be coherent, typed, realistic enough to drive every UI state (loading, success, empty, error, partial when useful), and reusable across tests.
9. Document mock services: why mock mode exists, which contract it simulates, which real integration replaces it later.
10. Tests validate UI behavior independent of transport.

## Output Expectations
- The UI works end-to-end against mocks before the API exists.
- Replacing the mock with the real implementation is a provider swap, not a rewrite.
- No mock-only assumptions leak into components.
