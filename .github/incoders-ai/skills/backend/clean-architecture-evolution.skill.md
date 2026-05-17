# Clean Architecture Evolution

## Purpose
Apply hexagonal/clean-architecture boundaries even when the repository is still a single project, so later extraction into Api/Application/Domain/Infrastructure remains cheap.

## Scope
Small or early-stage backends expected to grow.

## Load Conditions
A backend or fullstack profile is active and the repository has not yet split into multiple projects/packages.

## Do Not Load When
The codebase is already fully split into bounded contexts/packages with enforced architecture tests.

## Inputs
- Current project layout
- Anticipated growth (modules, integrations, persistence providers)

## Procedure
1. Inspect the proposed change and locate which conceptual layer (Domain, Application, Infrastructure, Api) it belongs to.
2. Place files in folders that mirror those layers, even within a single project.
3. Enforce dependency direction: Domain → nothing; Application → Domain; Infrastructure → Application/Domain abstractions; Api → Application.
4. Avoid coupling shortcuts (e.g. referencing `DbContext` from Application) even when "it would work for now".
5. Document any deliberate deviation as a temporary debt item with a target removal milestone.

## Output Expectations
- Folder structure already mirrors the eventual project split.
- No coupling debt is created that would block a future extraction.
- Imports/usings reflect the dependency direction declared by `core/architecture-boundaries.rules.md`.
