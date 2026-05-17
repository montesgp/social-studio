# Architecture Boundaries

## Purpose
Enforce inward dependency direction and provider isolation across all repositories.

## Scope
All repositories that load the Incoders AI ecosystem, especially backend and fullstack profiles.

## Load Conditions
Always loaded.

## Do Not Load When
Never skipped.

## Rules
- Domain MUST NOT depend on infrastructure, UI, external SDKs, or persistence engines.
- Application layer owns use cases and ports; orchestrates Domain logic; does not depend on Infrastructure types.
- Infrastructure implements adapters/providers; depends on Application abstractions; never the other way around.
- API/presentation layer depends on Application; never on Infrastructure or Domain directly.
- Frontend state and backend state MUST be synchronized through explicit contracts; never via shared persistence types.
- Provider-specific concerns MUST be encapsulated behind interfaces declared in Application.
- All persistence access MUST go through repository or port abstractions defined in Application. Never bypass repository interfaces.
- Persistence provider selection MUST be configurable; no business logic bound to a specific database vendor.
- All external APIs MUST be accessed via Anti-Corruption Layers (see `core/external-integrations.rules.md`).
- Composition root assembles dependencies through capability-named DI modules (see `core/modular-composition.rules.md`).

## Inward-dependency graph
```
Api            → Application
Application    → Domain
Infrastructure → Application (abstractions) and Domain (entities/contracts only)
Domain         → nothing
```

## Output Expectations
- Architecture tests (when supported by the stack) verify the inward dependency rule.
- Reviews catch types from Infrastructure leaking into Application or Domain.
- Provider replacement only requires touching Infrastructure modules.
