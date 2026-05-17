# External Integrations and Anti-Corruption (Global Rule)

## Purpose
Isolate external providers behind anti-corruption layers so provider changes do not propagate into Domain/Application.

## Scope
Any code that consumes an external HTTP, gRPC, message-bus, BaaS, or SDK boundary.

## Load Conditions
Always loaded for backend, fullstack, and monorepo profiles. Loaded for frontend profiles when the frontend calls non-internal providers directly.

## Do Not Load When
The work is purely internal (no outbound integration).

## Rules
- MUST route all outbound HTTP calls through a centralized client factory with timeouts, retries, cancellation, and resilience policy. No ad-hoc client instantiation.
- MUST define provider contracts separately from internal application contracts; map at the boundary.
- MUST NOT leak provider DTOs, error shapes, or vendor-specific semantics into Domain or Application.
- MUST register clients in DI/IoC modules; keep them named or typed deliberately.
- SHOULD centralize resilience policy (timeout/retry/circuit-breaker) so provider changes adjust one place.
- SHOULD treat managed services (databases-as-a-service, auth providers, search providers) as external integrations subject to the same anti-corruption discipline as third-party APIs.

## Output Expectations
- Domain and Application packages contain zero direct references to provider SDK types.
- Outbound HTTP calls share a single configuration surface for timeouts and retries.
- When a provider changes its contract, only its adapter module is touched.
