# HTTP Client Factory

## Purpose
All outbound HTTP traffic flows through a centralized client factory with timeouts, retries, and resilience policy.

## Scope
Any project that makes outbound HTTP/REST/gRPC-over-HTTP calls.

## Load Conditions
A backend or fullstack profile is active and external HTTP calls are in scope.

## Do Not Load When
Pure frontend repos where browser-side fetch is used (those have their own interceptor/middleware skill).

## Inputs
- The external service URL set
- Expected timeout/retry/circuit-breaker policy

## Procedure
1. Register named or typed clients in a DI module (e.g. `AddInfrastructure`).
2. Configure base URL, default headers, timeout, retry policy, and circuit breaker at registration time.
3. Inject the typed/named client into the adapter (never `HttpClient` directly).
4. Avoid per-call `new HttpClient(...)` instantiation anywhere in the codebase.
5. Add health checks for critical outbound dependencies.

## Output Expectations
- Zero raw `HttpClient` instantiations in adapters or services.
- One DI module owns the configuration surface for outbound traffic.
- Outbound calls participate in correlation/tracing per `core/observability.rules.md`.
