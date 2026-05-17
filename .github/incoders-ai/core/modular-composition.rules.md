# Modular Composition (Global Rule)

## Purpose
Avoid monolithic startup/composition files by grouping DI registrations by capability or layer.

## Scope
Composition root, startup, bootstrap, and DI registration code.

## Load Conditions
Loaded for backend, fullstack, and monorepo profiles.

## Do Not Load When
Pure infrastructure-as-code or pure documentation work.

## Rules
- MUST register services through capability-named or layer-named DI modules (e.g. `AddApiPresentation`, `AddApplication`, `AddInfrastructure`, `AddPersistence`, `AddAuthentication`, `AddObservability`).
- MUST NOT create a single startup file that mixes presentation, application, infrastructure, persistence, auth, and observability registrations.
- SHOULD keep each composition module small, cohesive, and named for its responsibility.
- SHOULD make composition modules unit-testable in isolation when registration logic is non-trivial.

## Output Expectations
- The composition root reads as a list of capability registrations, not a wall of `services.Add*` calls.
- Adding a new infrastructure adapter touches the relevant module, not the program entry point.
