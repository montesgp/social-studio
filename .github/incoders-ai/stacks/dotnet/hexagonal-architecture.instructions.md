# .NET Hexagonal Architecture

## Layers
- Domain: entities, value objects, domain services, domain errors.
- Application: use cases, ports, DTO/contracts, validators.
- Infrastructure: adapters, persistence, external providers.
- Api: HTTP endpoints, auth, request/response mapping.

## Rule
Dependencies point inward.
