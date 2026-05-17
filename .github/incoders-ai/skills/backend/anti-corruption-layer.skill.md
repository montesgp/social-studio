# Anti-Corruption Layer

## Purpose
Isolate every external system behind an adapter so its DTOs, vocabulary, and breaking changes never reach Domain or Application.

## Scope
Any code that consumes a third-party HTTP API, messaging bus, BaaS, SDK, or managed service.

## Load Conditions
A new external integration is being added or an existing one is being refactored.

## Do Not Load When
Internal-only code paths with no outbound integration.

## Inputs
- The external provider contract (OpenAPI spec, SDK signatures, or message schemas).
- The internal application/domain contract that needs the data.

## Procedure
1. Define provider-side DTOs in an Infrastructure-only namespace.
2. Define internal contracts (application-side ports, domain-side types) without referencing provider types.
3. Implement a mapper that translates between the two, owning all provider quirks (date formats, error codes, partial fields).
4. Register the adapter in a DI module dedicated to this provider.
5. Add resilience policy (timeout, retry, circuit-breaker) on the adapter, not on the consumer.
6. Write tests against the internal contract with the adapter mocked or faked.

## Output Expectations
- Domain/Application packages never import provider SDK types.
- When the provider releases a breaking change, only the adapter mapper needs to change.
- Provider-specific semantics (date formats, sentinel values, partial responses) are normalized at the boundary.
